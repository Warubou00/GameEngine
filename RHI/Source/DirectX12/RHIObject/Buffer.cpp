
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
#include "Device.h"
#include "CommandContext.h"
#include "../dx12_descriptorHeapManager.h"
#include "../Object/dx12_resourceBuffer.h"
#include "../Object/dx12_swapChain.h"
#include "../Object/dx12_device.h"
#include "../Object/dx12_graphicsCommandList.h"

#include "../../Core/Source/Log/logger.h"

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
		Buffer::Buffer() {}
		Buffer::~Buffer() {}

		RHI_RESULT Buffer::Initialize()
		{
			return RHI_SUCCEEDED;
		}

		RHI_RESULT Buffer::Initialize(Device* device, CommandContext* commandContext)
		{
			if (!device || !commandContext)
			{
				return RHI_FAILED_INVALID_ARGUMENT;
			}

			_descriptorHeapManager = std::make_unique<RHI::DirectX12::DescriptorHeapManager>();

			// DescriptorHeapの作成
			HRESULT hr = _descriptorHeapManager->Create(device->GetDevice()->GetDevice());

			if (FAILED(hr))
			{
				return RHI_FAILED_INITIALIZED;
			}

			// レンダーターゲットビューの作成
			if (!makeRtv(device, commandContext))
			{
#ifdef CONSOLE_DEBUG
				Core::Logger::Log("**************************************************");
#endif
				return RHI_FAILED_INITIALIZED;
			}

			if (!makeCommonBuffer(device))
			{
#ifdef CONSOLE_DEBUG
				Core::Logger::Log("Failed Initialize VB and CB");
#endif
				return RHI_FAILED_INITIALIZED;
			}

			if (!makeCommonView(device))
			{
#ifdef CONSOLE_DEBUG
				Core::Logger::Log("Failed Initialize Common View");
#endif

				return RHI_FAILED_INITIALIZED;
			}

#ifdef CONSOLE_DEBUG
			Core::Logger::Log("Succeeded Initialize Global Scene VB CB From C++");
#endif

#ifdef CONSOLE_DEBUG
			Core::Logger::Log("Succeeded Initialize Buffer From C++");
			Core::Logger::Log("**************************************************");
#endif

			return RHI_SUCCEEDED;
		}

		bool Buffer::UploadVertexData(Device* device, CommandContext* commandContext, const std::vector<Vertex>& vertices)
		{
			if (vertices.empty()) return false;

			UINT64 dataSize = vertices.size() * sizeof(Vertex);

			// 1. 一時的なUPLOADバッファを作成
			auto uploadBuffer = std::make_unique<RHI::DirectX12::ResourceBuffer>();
			uploadBuffer->Create(device->GetDevice()->GetDevice(), dataSize, D3D12_HEAP_TYPE_UPLOAD);

				// 2. CPU側からUPLOADバッファへデータをコピー（Map/Unmap）
			void* mappedData = nullptr;
			// Map の引数: (Subresource, readRange(nullptrならCPUは読まない), ppData)
			HRESULT hr = uploadBuffer->Get()->Map(0, nullptr, &mappedData);
			if (FAILED(hr)) return false;

			memcpy(mappedData, vertices.data(), dataSize);
			uploadBuffer->Get()->Unmap(0, nullptr);

			// 3. CommandContext を使って GPU側に コピー命令 (CopyBufferRegion) を積む
			auto cmdList = commandContext->GetGraphicsCommandList()->GetCmdList();

			// DEFAULTヒープ(コピー先)の状態を COMMON -> COPY_DEST へ遷移
			D3D12_RESOURCE_BARRIER barrier = {};
			barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
			barrier.Flags = D3D12_RESOURCE_BARRIER_FLAG_NONE;
			barrier.Transition.pResource = _sceneVB->Get();
			barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
			barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_COMMON;
			barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_COPY_DEST;
			cmdList->ResourceBarrier(1, &barrier);

			// UPLOADバッファ から DEFAULTバッファ へデータをコピー
			cmdList->CopyBufferRegion(
				_sceneVB->Get(), // コピー先 (VRAM)
				0,               // コピー先オフセット
				uploadBuffer->Get(), // コピー元 (共有メモリ)
				0,               // コピー元オフセット
				dataSize         // コピーするサイズ
			);

			// DEFAULTヒープ(コピー先)の状態を COPY_DEST -> GENERIC_READ (シェーダーから読める状態) へ遷移
			barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_DEST;
			barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_GENERIC_READ;
			cmdList->ResourceBarrier(1, &barrier);

			// 4. コマンドを実行し、GPUの処理完了を待つ (フェンスを使用)
			// ※ この時点では uploadBuffer はまだ破棄してはいけません。
			// GPUがコピー処理を終えるまで待機するか、安全に破棄できる仕組みが必要です。

			// （※仮実装：コマンドを閉じて実行し、同期する処理をCommandContextに持たせる必要があります）
			commandContext->ExecuteAndSync();

			return true;
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

		bool Buffer::makeRtv(Device* device, CommandContext* commandContext)
		{
			// Managerのnullチェック
			if (!nullCheckManager())
			{
#ifdef CONSOLE_DEBUG
				Core::Logger::Log("**************************************************");
				Core::Logger::Log("Failed Initialize Render Target View");
#endif
				return false;
			}
			// RTVのハンドル取得
			auto rtvHandle = _descriptorHeapManager->GetRtvCpuHandleStart();

			// SwapChainDescを取得
			DXGI_SWAP_CHAIN_DESC1 scDesc = {};
			auto hr = commandContext->GetSwapChain()->Get()->GetDesc1(&scDesc);

			// rtvの作成
			ID3D12Resource* resource = nullptr;
			for (int i = 0; i < scDesc.BufferCount; i++)
			{
				commandContext->GetSwapChain()->Get()->GetBuffer(i, IID_PPV_ARGS(&resource));

				device->GetDevice()->GetDevice()->CreateRenderTargetView(resource, nullptr, rtvHandle);

				// サイズ分進める
				rtvHandle.ptr += _descriptorHeapManager->GetRtvDescriptorSize();
			}

#ifdef CONSOLE_DEBUG
			Core::Logger::Log("**************************************************");
			Core::Logger::Log("Succeeded Initialize Render Target View");
#endif

			return true;
		}

		bool Buffer::nullCheckManager()
		{
			return _descriptorHeapManager != nullptr;
		}

		bool Buffer::makeCommonBuffer(Device* device)
		{
			if (!device)return false;

			if (!makeGlobalVB(device))return false;
			if ( !makeSceneVB(device))return false;
			if (!makeGlobalCB(device))return false;
			if ( !makeSceneCB(device))return false;

			return true;
		}

		bool Buffer::makeGlobalVB(Device* device)
		{
			_globalVB = std::make_unique<RHI::DirectX12::ResourceBuffer>();
			
			return makeBuffer(
				device,
				_globalVB.get(),
				_GLOBAL_VB_SIZE,
				D3D12_HEAP_TYPE_DEFAULT
			);
		}

		bool Buffer::makeSceneVB(Device* device)
		{
			_sceneVB = std::make_unique<RHI::DirectX12::ResourceBuffer>();

			return makeBuffer(
				device,
				_sceneVB.get(),
				_SCENE_VB_SIZE,
				D3D12_HEAP_TYPE_DEFAULT
			);
		}

		bool Buffer::makeGlobalCB(Device* device)
		{
			_globalCB = std::make_unique<RHI::DirectX12::ResourceBuffer>();

			return makeBuffer(
				device,
				_globalCB.get(),
				_GLOBAL_CB_SIZE,
				D3D12_HEAP_TYPE_UPLOAD
			);
		}

		// 未使用 Root CBVに統合
		bool Buffer::makeSceneCB(Device* device)
		{
			_sceneCB = std::make_unique<RHI::DirectX12::ResourceBuffer>();

			return makeBuffer(
				device,
				_sceneCB.get(),
				_SCENE_CB_SIZE,
				D3D12_HEAP_TYPE_UPLOAD
			);
		}

		// 未使用 Root CBVに統合
		bool Buffer::makeBuffer(Device* device, RHI::DirectX12::ResourceBuffer* resourceBuffer, uint64_t sizeInBytes, D3D12_HEAP_TYPE heapType)
		{
			return SUCCEEDED(
				resourceBuffer->Create(
					device->GetDevice()->GetDevice(),
					sizeInBytes,
					heapType
				)
			);
		}

		bool Buffer::makeCommonView(Device* device)
		{
			if (!device)return false;

			if (!makeGlobalVBV(device))return false;
			if ( !makeSceneVBV(device))return false;

			// ※未使用 RootCBVを採用
			//if (!makeGlobalCBV(device))return false;
			//if ( !makeSceneCBV(device))return false;

			return true;
		}

		bool Buffer::makeGlobalVBV(Device* device)
		{
			if (!_globalVB.get()) return false;

			makeVBView(
				device,
				_globalVB.get(),
				_GLOBAL_VB_SIZE,
				sizeof(Vertex)
			);
		}

		bool Buffer::makeSceneVBV(Device* device)
		{
			if (!_sceneVB.get()) return false;

			makeVBView(
				device,
				_sceneVB.get(),
				_SCENE_VB_SIZE,
				sizeof(Vertex)
			);
		}

		bool Buffer::makeGlobalCBV(Device* device)
		{
			if (!_globalCB.get()) return false;

			uint64_t alignment = _sceneCB->Allocate(1, 256);

			makeCBView(
				device,
				_globalCB.get(),
				1,
				alignment
			);
		}

		bool Buffer::makeSceneCBV(Device* device)
		{
			if (!_sceneCB.get()) return false;

			uint64_t alignment = _sceneCB->Allocate(1, 256);

			makeCBView(
				device,
				_sceneCB.get(),
				1,
				alignment
			);
		}

		bool Buffer::makeVBView(Device* device, RHI::DirectX12::ResourceBuffer* resourceBuffer, uint64_t sizeInBytes, uint32_t structureByteStride)
		{
			// 1. Managerから空いている「枠番号（インデックス）」をもらう
			uint32_t srvIndex = _descriptorHeapManager->AllocateSrv();

			// 2. その枠のCPU側アドレスを取得
			D3D12_CPU_DESCRIPTOR_HANDLE cpuHandle = _descriptorHeapManager->GetSrvCpuHandle(srvIndex);

			// 3. Bufferの情報を定義（メガバッファをSRVとして登録する例）
			D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
			srvDesc.Format                          = DXGI_FORMAT_UNKNOWN;
			srvDesc.ViewDimension                   = D3D12_SRV_DIMENSION_BUFFER;
			srvDesc.Shader4ComponentMapping         = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
			srvDesc.Buffer.FirstElement             = 0;
			srvDesc.Buffer.NumElements              = sizeInBytes / structureByteStride; // 頂点の総数
			srvDesc.Buffer.StructureByteStride      = structureByteStride;

			// 4. パレットの枠(cpuHandle)に、絵の具(Buffer)を登録！
			device->GetDevice()->GetDevice()->CreateShaderResourceView(resourceBuffer->Get(), &srvDesc, cpuHandle);

			return true;
		}

		// 未使用 Root CBVに統合
		bool Buffer::makeCBView(Device* device, RHI::DirectX12::ResourceBuffer* resourceBuffer, uint64_t sizeInBytes, uint32_t cbOffset)
		{
			uint32_t cbvIndex = _descriptorHeapManager->AllocateSrv();
			D3D12_CPU_DESCRIPTOR_HANDLE cbvHandle = _descriptorHeapManager->GetSrvCpuHandle(cbvIndex);

			// 3. CBV専用の構造体を設定する
			D3D12_CONSTANT_BUFFER_VIEW_DESC cbvDesc = {};
			// バッファの先頭GPUアドレス ＋ オフセット位置
			cbvDesc.BufferLocation = resourceBuffer->Get()->GetGPUVirtualAddress() + cbOffset;
			// 【重要】サイズは必ず256の倍数（アライメントされたサイズ）を指定する
			cbvDesc.SizeInBytes = sizeInBytes;

			// 4. CreateShaderResourceView ではなく CreateConstantBufferView を呼ぶ！
			device->GetDevice()->GetDevice()->CreateConstantBufferView(&cbvDesc, cbvHandle);

			return true;
		}
	}
}

/************************************
* End of file
************************************/
