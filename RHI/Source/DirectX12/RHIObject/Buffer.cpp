
/***********************************************************************************
* @file    Buffer.cpp
* @brief   RHI Buffer
* @details
*
* @author  Keigo Onari
* @date    2026/4/27
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note
************************************************************************************/


/******************************************************************
* Include Headers
******************************************************************/
#include "Buffer.h"

#include <DirectXMath.h>

// 必要なクラスをインクルード
// ========================================
#include "Device.h"
#include "CommandContext.h"
#include "Texture.h"
// ========================================

// DirectX12 APIのラッパーをインクルード
// ========================================
#include "../dx12_descriptorHeapManager.h"
#include "../Object/dx12_resourceBuffer.h"
#include "../Object/dx12_swapChain.h"
#include "../Object/dx12_device.h"
#include "../Object/dx12_graphicsCommandList.h"
#include "../dx12_uploadManager.h"

#include <cstring>
// ========================================

#include "../../ResourceBufferAllocator.h"

// 外部クラスをインクルード
// ========================================
#include "../../Core/Source/Log/logger.h"
// ※以前は Renderer のヘッダー (objLoader / mesh / constant) を読んでいた。モデルの読み込みは Renderer に移した
// ========================================


/******************************************************************
* Macro Definitions
******************************************************************/


/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/

namespace RHI
{
	namespace ObjectInterface
	{
		namespace
		{
			// DirectXMath の行列を、シェーダー (mul(ベクトル, 行列) / column_major) 用に転置して保存する
			::DirectX::XMFLOAT4X4 transposeForShader(const ::DirectX::XMFLOAT4X4& m)
			{
				::DirectX::XMFLOAT4X4 result;
				::DirectX::XMStoreFloat4x4(&result, ::DirectX::XMMatrixTranspose(::DirectX::XMLoadFloat4x4(&m)));
				return result;
			}
		}

		Buffer::Buffer() {}
		Buffer::~Buffer()
		{
			Dispose();
		}

		RHI_RESULT Buffer::Initialize(RHI::ObjectInterface::IDevice* iDevice, RHI::ObjectInterface::ICommandContext* iCommandContext)
		{
			// 例外処理 nullptr
			if (!iDevice || !iCommandContext)return RHI_FAILED_INVALID_ARGUMENT;

			// 静的ダウンキャスト
			// TODO : Phase 3 で抽象化を見直すまでは具象型へのキャストで扱う
			auto device     = static_cast<RHI::ObjectInterface::Device*>(iDevice);
			auto cmdContext = static_cast<RHI::ObjectInterface::CommandContext*>(iCommandContext);

			_descriptorHeapManager = std::make_unique<RHI::DirectX12::DescriptorHeapManager>();

			// DescriptorHeapの作成
			HRESULT hr = _descriptorHeapManager->Create(device->GetDevice()->GetDevice());

			if (FAILED(hr))
			{
				return RHI_FAILED_INITIALIZED;
			}

			// ※レンダーターゲットビュー(RTV)は CommandContext が作る (リサイズ時に作り直すため)

			if (!makeCommonBuffer(device))
			{
				return RHI_FAILED_INITIALIZED;
			}

			// 頂点バッファのSRV (ヒープの 0 / 1 番)
			if (!makeCommonView(device))
			{
				return RHI_FAILED_INITIALIZED;
			}

			// 定数バッファは UPLOAD ヒープなので、生存中はずっと Map しておく (永続マップ)
			if (FAILED(_commonCB->Map()))
			{
				return RHI_FAILED_INITIALIZED;
			}

			if (FAILED(_globalCB->Map()))
			{
				return RHI_FAILED_INITIALIZED;
			}

			if (FAILED(_sceneCB->Map()))
			{
				return RHI_FAILED_INITIALIZED;
			}

			// 既定テクスチャ(白)を作る。ヒープの 2 番 = TextureID 0 になる
			// テクスチャを持たないマテリアルや読み込み失敗時はこれを使う (1-11)
			_texture = std::make_unique<RHI::ObjectInterface::Texture>();
			if (!_texture->CreateDefaultTexture(device, cmdContext, this))
			{
				return RHI_FAILED_INITIALIZED;
			}

			return RHI_SUCCEEDED;
		}

		RHI::VertexRange Buffer::UploadVertexData(
			RHI::ObjectInterface::IDevice* iDevice,
			RHI::ObjectInterface::ICommandContext* iCommandContext,
			const RHI::Vertex* vertices,
			uint32_t count
		)
		{
			RHI::VertexRange range;
			if (!iDevice || !iCommandContext || !vertices || count == 0) return range;

			auto device     = static_cast<RHI::ObjectInterface::Device*>(iDevice);
			auto cmdContext = static_cast<RHI::ObjectInterface::CommandContext*>(iCommandContext);

			// リソースのメモリ位置を取得
			auto memoryAllocation = _sceneVBAllocator->Allocate(count, sizeof(RHI::Vertex));
			if (!memoryAllocation.IsValid)
			{
				// 以前は容量オーバーでもオフセット0が返り、既存の頂点を上書きしていた (2-2)
				Core::Logger::Log("[Buffer] Scene vertex buffer is full.");
				return range;
			}

			const uint64_t dataSize = static_cast<uint64_t>(count) * sizeof(RHI::Vertex);
			if (!uploadToDefaultBuffer(device, cmdContext, _sceneVB.get(), memoryAllocation.ByteOffset, vertices, dataSize))
			{
				return range;
			}

			// シェーダーは g_MegaVB[BaseVertex + インデックスの値] で読む
			range.BaseVertex = memoryAllocation.StrideOffset;
			range.Count      = count;
			return range;
		}

		RHI::IndexRange Buffer::UploadIndexData(
			RHI::ObjectInterface::IDevice* iDevice,
			RHI::ObjectInterface::ICommandContext* iCommandContext,
			const uint32_t* indices,
			uint32_t count
		)
		{
			RHI::IndexRange range;
			if (!iDevice || !iCommandContext || !indices || count == 0) return range;

			auto device     = static_cast<RHI::ObjectInterface::Device*>(iDevice);
			auto cmdContext = static_cast<RHI::ObjectInterface::CommandContext*>(iCommandContext);

			// リソースのメモリ位置を取得
			auto memoryAllocation = _sceneIBAllocator->Allocate(count, sizeof(uint32_t));
			if (!memoryAllocation.IsValid)
			{
				Core::Logger::Log("[Buffer] Scene index buffer is full.");
				return range;
			}

			const uint64_t dataSize = static_cast<uint64_t>(count) * sizeof(uint32_t);
			if (!uploadToDefaultBuffer(device, cmdContext, _sceneIB.get(), memoryAllocation.ByteOffset, indices, dataSize))
			{
				return range;
			}

			range.FirstIndex = memoryAllocation.StrideOffset;   // DrawIndexedInstanced の StartIndexLocation
			range.Count      = count;
			return range;
		}

		bool Buffer::uploadToDefaultBuffer(
			RHI::ObjectInterface::Device* device,
			RHI::ObjectInterface::CommandContext* cmdContext,
			RHI::DirectX12::ResourceBuffer* destBuffer,
			uint64_t destByteOffset,
			const void* data,
			uint64_t dataSize)
		{
			if (!destBuffer || !destBuffer->Get() || !data || dataSize == 0) return false;

			auto uploadManager = cmdContext->GetUploadManager();
			if (!uploadManager) return false;

			// 1. リングバッファから領域をもらって、CPU から直接書き込む
			//    (以前は毎回 UPLOAD バッファを新規作成 → Map → Unmap していた)
			RHI::DirectX12::UploadManager::Allocation allocation;
			if (!uploadManager->Allocate(dataSize, 16, allocation))
			{
				return false;
			}
			std::memcpy(allocation.Cpu, data, static_cast<size_t>(dataSize));

			// 2. COPY 用コマンドリストにコピー命令を積む
			auto cmdList = uploadManager->GetCommandList();
			if (!cmdList)
			{
				return false;
			}

			// バリアは張らない:
			//   COPY キューでは COMMON → COPY_DEST に暗黙で昇格し、完了後は COMMON に戻る(減衰)。
			//   描画キューで読む時も COMMON → 頂点(SRV) / インデックスの状態に暗黙で昇格する。
			//   (バッファはどの状態にも暗黙で昇格できる。Phase 1 まではここで明示的にバリアを張っていた)
			cmdList->CopyBufferRegion(
				destBuffer->Get(),             // コピー先 (VRAM)
				destByteOffset,                // コピー先オフセット
				allocation.Resource,           // コピー元 (リングバッファ)
				allocation.Offset,             // コピー元オフセット
				dataSize                       // コピーするサイズ
			);

			// 3. 送信はしない (RenderEnd でまとめて 1 回送信される)
			return true;
		}

		void Buffer::SetViewProjection(const ::DirectX::XMFLOAT4X4& view, const ::DirectX::XMFLOAT4X4& projection)
		{
			// GPU が複数フレームを並行して処理するので、ここでは CPU 側の行列だけ更新し、
			// GPU へは WriteFrameConstants でフレームごとの領域に書く
			_viewMatrix = transposeForShader(view);
			_projMatrix = transposeForShader(projection);

			// 全フレームの領域に書き直しが必要になる
			_frameConstantsVersion++;
		}

		RHI::ObjectHandle Buffer::CreateObject()
		{
			RHI::ObjectHandle handle;
			if (_objects.size() >= MAX_OBJECTS)
			{
				Core::Logger::Log("[Buffer] Too many objects.");
				return handle;
			}

			handle.Index = static_cast<uint32_t>(_objects.size());

			// 初期値 : 原点に置いて既定テクスチャ(白)
			RHI::ObjectConstants initial;
			::DirectX::XMStoreFloat4x4(&initial.World, ::DirectX::XMMatrixIdentity());
			initial.TextureIndex = RHI::TextureHandle::DEFAULT;

			_objects.push_back(initial);                 // 単位行列は転置しても同じ
			_objectVersion.push_back(_objectsVersion);
			for (auto& written : _objectWritten)
			{
				written.push_back(0);                    // どのフレームの領域にもまだ書いていない
			}
			_objectsVersion++;
			return handle;
		}

		void Buffer::UpdateObject(RHI::ObjectHandle object, const RHI::ObjectConstants& constants)
		{
			if (!object.IsValid() || object.Index >= _objects.size())
			{
				return;
			}

			RHI::ObjectConstants stored = constants;
			stored.World = transposeForShader(constants.World);
			_objects[object.Index] = stored;

			_objectsVersion++;
			_objectVersion[object.Index] = _objectsVersion;
		}

		D3D12_GPU_VIRTUAL_ADDRESS Buffer::WriteObjectConstants(uint32_t frameIndex)
		{
			if (!_sceneCB || frameIndex >= CommandContext::FRAME_COUNT)
			{
				return 0;
			}

			// このフレーム用の領域の先頭
			const uint64_t frameOffset = SCENE_CB_FRAME_SIZE * frameIndex;

			// 前回このフレームの領域に書いてから何も変わっていなければ、ループごと省く
			if (_objectsWrittenVersion[frameIndex] != _objectsVersion)
			{
				auto& written = _objectWritten[frameIndex];
				for (size_t i = 0; i < _objects.size(); ++i)
				{
					// 変わったオブジェクトだけ書く (UPLOAD ヒープへの書き込みは遅いので最小限に)
					if (written[i] == _objectVersion[i]) continue;

					if (_sceneCB->WriteData(frameOffset + SCENE_CB_STRIDE * i, &_objects[i], sizeof(RHI::ObjectConstants)))
					{
						written[i] = _objectVersion[i];
					}
				}
				_objectsWrittenVersion[frameIndex] = _objectsVersion;
			}

			return _sceneCB->GetGPUVirtualAddress() + frameOffset;
		}

		D3D12_GPU_VIRTUAL_ADDRESS Buffer::WriteFrameConstants(uint32_t frameIndex)
		{
			if (!_commonCB || frameIndex >= CommandContext::FRAME_COUNT)
			{
				return 0;
			}

			// フレームごとに 256byte ずつ領域を分けている (Root CBV は 256byte 境界が必須)
			const uint64_t offset = CONSTANT_BUFFER_ALIGNMENT * frameIndex;

			// 行列が変わっていなければ書き込みを省く (UPLOAD ヒープは書き込みが遅いメモリなので、無駄に書かない)
			if (_writtenVersion[frameIndex] != _frameConstantsVersion)
			{
				// View行列 → Projection行列 の順に詰める (シェーダーの cbuffer GlobalData と同じ並び)
				if (_commonCB->WriteData(offset, &_viewMatrix, sizeof(_viewMatrix)) &&
					_commonCB->WriteData(offset + sizeof(_viewMatrix), &_projMatrix, sizeof(_projMatrix)))
				{
					_writtenVersion[frameIndex] = _frameConstantsVersion;
				}
				else
				{
					Core::Logger::Log("[Buffer] Failed write view / projection matrix.");
				}
			}

			return _commonCB->GetGPUVirtualAddress() + offset;
		}

		void Buffer::Dispose()
		{
			// GPUの完了待ちは CommandContext::Dispose で済ませてある前提
			// 以前は commonCB だけ Unmap していた
			if (_texture) { _texture->Dispose(); _texture.reset(); }

			if (_commonCB) _commonCB->Unmap();
			if (_globalCB) _globalCB->Unmap();
			if (_sceneCB)  _sceneCB->Unmap();
		}

		RHI::DirectX12::DescriptorHeapManager* Buffer::GetDescHeapManager()const
		{
			return _descriptorHeapManager.get();
		}

		RHI::DirectX12::ResourceBuffer* Buffer::GetGlobalVB()const
		{
			return _globalVB.get();
		}

		RHI::DirectX12::ResourceBuffer* Buffer::GetSceneVB()const
		{
			return _sceneVB.get();
		}

		RHI::DirectX12::ResourceBuffer* Buffer::GetGlobalCB()const
		{
			return _globalCB.get();
		}

		RHI::DirectX12::ResourceBuffer* Buffer::GetSceneCB()const
		{
			return _sceneCB.get();
		}

		RHI::DirectX12::ResourceBuffer* Buffer::GetCommonCB()const
		{
			return _commonCB.get();
		}

		RHI::DirectX12::ResourceBuffer* Buffer::GetGlobalIB()const
		{
			return _globalIB.get();
		}

		RHI::DirectX12::ResourceBuffer* Buffer::GetSceneIB()const
		{
			return _sceneIB.get();
		}

		const uint64_t Buffer::GetSceneIndexBufferSize()const
		{
			return _SCENE_IB_SIZE;
		}

		bool Buffer::nullCheckManager()
		{
			return _descriptorHeapManager != nullptr;
		}

		bool Buffer::makeCommonBuffer(RHI::ObjectInterface::Device* device)
		{
			if (!device)return false;

			if(!makeAllocator()      )return false;
			if (!makeGlobalVB(device))return false;
			if ( !makeSceneVB(device))return false;
			if (!makeGlobalCB(device))return false;
			if ( !makeSceneCB(device))return false;
			if (!makeCommonCB(device))return false;
			if (!makeGlobalIB(device))return false;
			if (!makeSceneIB(device))return false;

			return true;
		}

		bool Buffer::makeGlobalVB(RHI::ObjectInterface::Device* device)
		{
			_globalVB = std::make_unique<RHI::DirectX12::ResourceBuffer>();

			_globalVBAllocator->Initialize(_GLOBAL_VB_SIZE);

			return makeBuffer(
				device,
				_globalVB.get(),
				_GLOBAL_VB_SIZE,
				D3D12_HEAP_TYPE_DEFAULT
			);
		}

		bool Buffer::makeSceneVB(RHI::ObjectInterface::Device* device)
		{
			_sceneVB = std::make_unique<RHI::DirectX12::ResourceBuffer>();

			_sceneVBAllocator->Initialize(_SCENE_VB_SIZE);

			return makeBuffer(
				device,
				_sceneVB.get(),
				_SCENE_VB_SIZE,
				D3D12_HEAP_TYPE_DEFAULT
			);
		}

		bool Buffer::makeGlobalCB(RHI::ObjectInterface::Device* device)
		{
			_globalCB = std::make_unique<RHI::DirectX12::ResourceBuffer>();

			_globalCBAllocator->Initialize(_GLOBAL_CB_SIZE);

			return makeBuffer(
				device,
				_globalCB.get(),
				_GLOBAL_CB_SIZE,
				D3D12_HEAP_TYPE_UPLOAD
			);
		}

		bool Buffer::makeSceneCB(RHI::ObjectInterface::Device* device)
		{
			_sceneCB = std::make_unique<RHI::DirectX12::ResourceBuffer>();

			_sceneCBAllocator->Initialize(_SCENE_CB_SIZE);

			return makeBuffer(
				device,
				_sceneCB.get(),
				_SCENE_CB_SIZE,
				D3D12_HEAP_TYPE_UPLOAD
			);
		}

		bool Buffer::makeCommonCB(RHI::ObjectInterface::Device* device)
		{
			_commonCB = std::make_unique<RHI::DirectX12::ResourceBuffer>();

			_commonCBAllocator->Initialize(_COMMON_CB_SIZE);

			return makeBuffer(
				device,
				_commonCB.get(),
				_COMMON_CB_SIZE,
				D3D12_HEAP_TYPE_UPLOAD
			);
		}

		bool Buffer::makeGlobalIB(RHI::ObjectInterface::Device* device)
		{
			_globalIB = std::make_unique<RHI::DirectX12::ResourceBuffer>();

			_globalIBAllocator->Initialize(_GLOBAL_IB_SIZE);

			return makeBuffer(
				device,
				_globalIB.get(),
				_GLOBAL_IB_SIZE,
				D3D12_HEAP_TYPE_DEFAULT
			);
		}

		bool Buffer::makeSceneIB(RHI::ObjectInterface::Device* device)
		{
			_sceneIB = std::make_unique<RHI::DirectX12::ResourceBuffer>();

			_sceneIBAllocator->Initialize(_SCENE_IB_SIZE);

			return makeBuffer(
				device,
				_sceneIB.get(),
				_SCENE_IB_SIZE,
				D3D12_HEAP_TYPE_DEFAULT
			);
		}

		bool Buffer::makeBuffer(RHI::ObjectInterface::Device* device, RHI::DirectX12::ResourceBuffer* resourceBuffer, uint64_t sizeInBytes, D3D12_HEAP_TYPE heapType)
		{
			const HRESULT hr = resourceBuffer->Create(
				device->GetDevice()->GetDevice(),
				sizeInBytes,
				heapType
			);
			if (FAILED(hr))
			{
				// VRAM不足などで失敗する。どのサイズで失敗したかを残す
				char message[128] = {};
				sprintf_s(message, "[Buffer] Failed create buffer. size = %llu, HRESULT = 0x%08X", sizeInBytes, static_cast<unsigned int>(hr));
				Core::Logger::Log(message);
				return false;
			}
			return true;
		}

		bool Buffer::makeCommonView(RHI::ObjectInterface::Device* device)
		{
			if (!device)return false;

			// 作る順番 = ヒープの番号 (SRV_INDEX_GLOBAL_VB → SRV_INDEX_SCENE_VB)
			if (!makeGlobalVBV(device))return false;
			if ( !makeSceneVBV(device))return false;

#if 0
			// ※未使用 RootCBVを採用
			//if (!makeGlobalCBV(device))return false;
			//if ( !makeSceneCBV(device))return false;
#endif

			return true;
		}

		bool Buffer::makeGlobalVBV(RHI::ObjectInterface::Device* device)
		{
			if (!_globalVB.get()) return false;

			// 以前は return が無く、戻り値が不定だった (1-2)
			return makeVBView(
				device,
				_globalVB.get(),
				_GLOBAL_VB_SIZE,
				sizeof(RHI::Vertex),
				SRV_INDEX_GLOBAL_VB
			);
		}

		bool Buffer::makeSceneVBV(RHI::ObjectInterface::Device* device)
		{
			if (!_sceneVB.get()) return false;

			// 以前は return が無く、戻り値が不定だった (1-2)
			return makeVBView(
				device,
				_sceneVB.get(),
				_SCENE_VB_SIZE,
				sizeof(RHI::Vertex),
				SRV_INDEX_SCENE_VB
			);
		}

		bool Buffer::makeAllocator()
		{
			_globalVBAllocator = std::make_unique<RHI::ResourceBufferAllocator>();
			_sceneVBAllocator  = std::make_unique<RHI::ResourceBufferAllocator>();
			_globalCBAllocator = std::make_unique<RHI::ResourceBufferAllocator>();
			_sceneCBAllocator  = std::make_unique<RHI::ResourceBufferAllocator>();
			_commonCBAllocator = std::make_unique<RHI::ResourceBufferAllocator>();
			_globalIBAllocator = std::make_unique<RHI::ResourceBufferAllocator>();
			_sceneIBAllocator  = std::make_unique<RHI::ResourceBufferAllocator>();

			return true;
		}

		bool Buffer::makeVBView(RHI::ObjectInterface::Device* device, RHI::DirectX12::ResourceBuffer* resourceBuffer, uint64_t sizeInBytes, uint32_t structureByteStride, uint32_t expectedSrvIndex)
		{
			// 1. Managerから空いている「枠番号（インデックス）」をもらう
			const uint32_t srvIndex = _descriptorHeapManager->AllocateSrv();

			// 描画時はこの番号を決め打ちで参照するので、想定と違えば失敗にする
			if (srvIndex != expectedSrvIndex)
			{
				Core::Logger::Log("[Buffer] Unexpected SRV index for vertex buffer.");
				return false;
			}

			// 2. その枠のCPU側アドレスを取得
			D3D12_CPU_DESCRIPTOR_HANDLE cpuHandle = _descriptorHeapManager->GetSrvCpuHandle(srvIndex);

			// 3. Bufferの情報を定義（メガバッファを StructuredBuffer のSRVとして登録する）
			D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
			srvDesc.Format                          = DXGI_FORMAT_UNKNOWN;
			srvDesc.ViewDimension                   = D3D12_SRV_DIMENSION_BUFFER;
			srvDesc.Shader4ComponentMapping         = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
			srvDesc.Buffer.FirstElement             = 0;
			srvDesc.Buffer.NumElements              = static_cast<UINT>(sizeInBytes / structureByteStride); // 頂点の総数
			srvDesc.Buffer.StructureByteStride      = structureByteStride;

			// 4. パレットの枠(cpuHandle)に、絵の具(Buffer)を登録！
			device->GetDevice()->GetDevice()->CreateShaderResourceView(resourceBuffer->Get(), &srvDesc, cpuHandle);

			return true;
		}
	}
}

/************************************
* End of file
************************************/
