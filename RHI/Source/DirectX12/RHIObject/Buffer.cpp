
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
// ========================================

#include "../../ResourceBufferAllocator.h"

// 外部クラスをインクルード
// ========================================
#include "../../Core/Source/Log/logger.h"
#include "../../Renderer/Source/Loader/objLoader.h"
#include "../../Renderer/Source/mesh.h"
#include "../../Renderer/Source/constant.h"
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
		// 1オブジェクト分の定数を 256byte 境界に揃えた間隔
		const uint64_t Buffer::SCENE_CB_STRIDE = Buffer::AlignConstantBufferSize(sizeof(Renderer::ConstantPerRenderObject));

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

			// レンダーターゲットビューの作成
			if (!makeRtv(device, cmdContext))
			{
				return RHI_FAILED_INITIALIZED;
			}

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

		bool Buffer::UploadVertexData(
			RHI::ObjectInterface::IDevice* iDevice,
			RHI::ObjectInterface::ICommandContext* iCommandContext,
			const std::vector<Renderer::Vertex>& vertices
		)
		{
			if (!iDevice || !iCommandContext)return false;
			if (vertices.empty()) return false;

			auto device     = static_cast<RHI::ObjectInterface::Device*>(iDevice);
			auto cmdContext = static_cast<RHI::ObjectInterface::CommandContext*>(iCommandContext);

			// リソースのメモリ位置を取得
			auto memoryAllocation = _sceneVBAllocator->Allocate(static_cast<uint32_t>(vertices.size()), sizeof(Renderer::Vertex));
			if (!memoryAllocation.IsValid)
			{
				// 以前は容量オーバーでもオフセット0が返り、既存の頂点を上書きしていた (2-2)
				Core::Logger::Log("[Buffer] Scene vertex buffer is full.");
				return false;
			}

			const uint64_t dataSize = vertices.size() * sizeof(Renderer::Vertex);
			return uploadToDefaultBuffer(device, cmdContext, _sceneVB.get(), memoryAllocation.ByteOffset, vertices.data(), dataSize);
		}

		bool Buffer::UploadIndexData(
			RHI::ObjectInterface::IDevice* iDevice,
			RHI::ObjectInterface::ICommandContext* iCommandContext,
			const std::vector<uint32_t>& indecies
		)
		{
			if (!iDevice || !iCommandContext)return false;
			if (indecies.empty()) return false;

			auto device     = static_cast<RHI::ObjectInterface::Device*>(iDevice);
			auto cmdContext = static_cast<RHI::ObjectInterface::CommandContext*>(iCommandContext);

			// リソースのメモリ位置を取得
			auto memoryAllocation = _sceneIBAllocator->Allocate(static_cast<uint32_t>(indecies.size()), sizeof(uint32_t));
			if (!memoryAllocation.IsValid)
			{
				Core::Logger::Log("[Buffer] Scene index buffer is full.");
				return false;
			}

			const uint64_t dataSize = indecies.size() * sizeof(uint32_t);
			return uploadToDefaultBuffer(device, cmdContext, _sceneIB.get(), memoryAllocation.ByteOffset, indecies.data(), dataSize);
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

			// 1. 一時的なUPLOADバッファを作成
			auto uploadBuffer = std::make_unique<RHI::DirectX12::ResourceBuffer>();
			if (FAILED(uploadBuffer->Create(device->GetDevice()->GetDevice(), dataSize, D3D12_HEAP_TYPE_UPLOAD)))
			{
				return false;
			}

			// 2. CPU側からUPLOADバッファへデータをコピー
			if (FAILED(uploadBuffer->Map()))
			{
				return false;
			}
			const bool written = uploadBuffer->WriteData(0, data, static_cast<size_t>(dataSize));
			uploadBuffer->Unmap();
			if (!written)
			{
				return false;
			}

			// 3. CommandContext を使って GPU側に コピー命令 (CopyBufferRegion) を積む
			if (!cmdContext->BeginRecording())
			{
				return false;
			}
			auto cmdList = cmdContext->GetGraphicsCommandList()->GetCmdList();

			// DEFAULTヒープ(コピー先)の状態を COMMON -> COPY_DEST へ遷移
			// バッファは ExecuteCommandLists の完了時に自動で COMMON に戻る(ステートの減衰)ため、
			// 2回目以降のアップロードでも遷移前は COMMON になる。
			// 以前のインデックスバッファは 2回目以降を GENERIC_READ としていたため、デバッグレイヤーでエラーになっていた (2-1)
			D3D12_RESOURCE_BARRIER barrier = {};
			barrier.Type                   = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
			barrier.Flags                  = D3D12_RESOURCE_BARRIER_FLAG_NONE;
			barrier.Transition.pResource   = destBuffer->Get();
			barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
			barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_COMMON;
			barrier.Transition.StateAfter  = D3D12_RESOURCE_STATE_COPY_DEST;
			cmdList->ResourceBarrier(1, &barrier);

			// UPLOADバッファ から DEFAULTバッファ へデータをコピー
			cmdList->CopyBufferRegion(
				destBuffer->Get(),             // コピー先 (VRAM)
				destByteOffset,                // コピー先オフセット
				uploadBuffer->Get(),           // コピー元 (共有メモリ)
				0,                             // コピー元オフセット
				dataSize                       // コピーするサイズ
			);

			// DEFAULTヒープ(コピー先)の状態を COPY_DEST -> GENERIC_READ (シェーダーから読める状態) へ遷移
			barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_DEST;
			barrier.Transition.StateAfter  = D3D12_RESOURCE_STATE_GENERIC_READ;
			cmdList->ResourceBarrier(1, &barrier);

			// GPUに送信して完了まで待機する (完了前にアップロードバッファを破棄してはいけない)
			const bool executed = cmdContext->ExecuteAndSync();

			// アップロードバッファーを破棄
			uploadBuffer.reset();

			return executed;
		}

		void Buffer::UpdateViewProjection(const Renderer::Camera& camera, Renderer::Projection::ProjectionType type)
		{
			if (!_commonCB) return;

			Renderer::View       view;
			Renderer::Projection projection;

			auto viewMat = view.GetViewMatrix(camera);
			// TODO : 画面サイズが 1280x720 固定。Phase 1 (2-4) でスワップチェーンのサイズを使うようにする
			auto projMat = projection.GetProjectionMatrix(1280.0f, 720.0f, 1.0f, 10000.0f, type);

			// View行列 → Projection行列 の順に詰める (シェーダーの cbuffer GlobalData と同じ並び)
			if (!_commonCB->WriteData(0, &viewMat, sizeof(viewMat)) ||
				!_commonCB->WriteData(sizeof(viewMat), &projMat, sizeof(projMat)))
			{
				Core::Logger::Log("[Buffer] Failed write view / projection matrix.");
			}
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

		bool Buffer::makeRtv(Device* device, CommandContext* cmdContext)
		{
			// Managerのnullチェック
			if (!nullCheckManager())
			{
				return false;
			}
			// RTVのハンドル取得
			auto rtvHandle = _descriptorHeapManager->GetRtvCpuHandleStart();

			const uint32_t bufferCount = cmdContext->GetSwapChain()->GetBufferCount();

			// rtvの作成 (バックバッファの数だけ)
			for (uint32_t i = 0; i < bufferCount; i++)
			{
				// スワップチェーンが保持しているので参照カウントは増えない (Release不要)
				ID3D12Resource* resource = cmdContext->GetSwapChain()->GetBufferFromIndex(i);
				if (!resource)
				{
					return false;
				}

				device->GetDevice()->GetDevice()->CreateRenderTargetView(resource, nullptr, rtvHandle);

				// サイズ分進める
				rtvHandle.ptr += _descriptorHeapManager->GetRtvDescriptorSize();
			}

			return true;
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
				sizeof(Renderer::Vertex),
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
				sizeof(Renderer::Vertex),
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

		Renderer::Mesh* Buffer::LoadModel(
			RHI::ObjectInterface::IDevice* iDevice,
			RHI::ObjectInterface::ICommandContext* iCommandContext,
			const std::wstring& filePath)
		{
			// 以前は読み込み失敗時に objLoader が delete されずリークしていた
			auto objLoader = std::make_unique<Renderer::ObjLoader>();
			Core::Logger::Log("Instanced ObjLoader");

			Renderer::Mesh* mesh = objLoader->LoadModel(filePath);
			if (mesh)
			{
				Core::Logger::Log("Success Loaded Mesh");
			}
			else
			{
				Core::Logger::Log("Failed Loaded Mesh");
				return nullptr;
			}

			// 以前は呼ぶたびに作り直しており、前に読み込んだテクスチャ(と既定テクスチャ)が破棄されていた
			if (!_texture)
			{
				_texture = std::make_unique<RHI::ObjectInterface::Texture>();
			}

			// TODO : 今は Scene定数バッファを先頭から使っている。複数モデルを読む場合は _sceneCBAllocator で確保する
			uint64_t idx = 0;
			for (auto material : mesh->Materials)
			{
				// テクスチャを使わないマテリアルは既定テクスチャ(白)にする
				// (以前は UseTexture を見ずに空のパスでも読み込もうとしていた : 1-11)
				TextureHandle handle;
				if (material && material->UseTexture)
				{
					Core::Logger::Log(material->TexturePath.c_str());
					handle = _texture->LoadTextureFromFile(iDevice, iCommandContext, this, material->TexturePath.c_str());
				}

				if (material)
				{
					material->TextureIndex = handle.TextureID;
				}

				Renderer::ConstantPerRenderObject cObj = {};
				// ワールド行列 (今は原点に置くだけ)
				// View / Projection と同じく、シェーダーへは転置して渡す (シェーダーは mul(ベクトル, 行列) で掛ける)
				// ※ :: は RHI::DirectX と区別するため
				cObj.WorldMatrix = ::DirectX::XMMatrixTranspose(::DirectX::XMMatrixIdentity());
				cObj.TextureID   = handle.TextureID;

				// 256byte 間隔で書き込む (RenderHardwareInterface 側の CbOffset と同じ間隔にすること)
				if (!_sceneCB->WriteData(SCENE_CB_STRIDE * idx, &cObj, sizeof(Renderer::ConstantPerRenderObject)))
				{
					Core::Logger::Log("[Buffer] Scene constant buffer is full.");
					break;
				}
				idx++;
			}

			if (!UploadVertexData(iDevice, iCommandContext, mesh->Vertices))
			{
				Core::Logger::Log("[Buffer] Failed upload vertices.");
			}

			for (const auto& index : mesh->Indecies)
			{
				if (!UploadIndexData(iDevice, iCommandContext, index))
				{
					Core::Logger::Log("[Buffer] Failed upload indices.");
				}
			}

			return mesh;
		}
	}
}

/************************************
* End of file
************************************/
