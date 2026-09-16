
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
#include "../DirectX12/dx12_descriptorHeapManager.h"
#include "../DirectX12/Object/dx12_resourceBuffer.h"

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
			HRESULT hr = _descriptorHeapManager->Create(device->GetDx12Device());

			if (FAILED(hr))
			{
				return RHI_FAILED_INITIALIZED;
			}

			// レンダーターゲットビューの作成
			if (!makeRtv(device, commandContext))
			{
#ifdef CONSOLE_DEBUG
				std::cout << "**************************************************" << std::endl;
#endif
				return RHI_FAILED_INITIALIZED;
			}

			if (!makeCommonBuffer(device))
			{
#ifdef CONSOLE_DEBUG
				std::cout << "Failed Initialize VB and CB" << std::endl;
#endif
				return RHI_FAILED_INITIALIZED;
			}

#ifdef CONSOLE_DEBUG
			std::cout << "Succeeded Initialize Global Scene VB CB From C++" << std::endl;
#endif

#ifdef CONSOLE_DEBUG
			std::cout << "Succeeded Initialize Buffer From C++" << std::endl;
			std::cout << "**************************************************" << std::endl;
#endif

			return RHI_SUCCEEDED;
		}

		bool Buffer::makeRtv(Device* device, CommandContext* commandContext)
		{
			// Managerのnullチェック
			if (!nullCheckManager())
			{
#ifdef CONSOLE_DEBUG
				std::cout << "**************************************************" << std::endl;
				std::cout << "Failed Initialize Render Target View"               << std::endl;
#endif
				return false;
			}
			// RTVのハンドル取得
			auto rtvHandle = _descriptorHeapManager->GetRtvCpuHandleStart();

			// SwapChainDescを取得
			DXGI_SWAP_CHAIN_DESC1 scDesc = {};
			auto hr = commandContext->GetSwapChain()->GetDesc1(&scDesc);

			// rtvの作成
			ID3D12Resource* resource = nullptr;
			for (int i = 0; i < scDesc.BufferCount; i++)
			{
				commandContext->GetSwapChain()->GetBuffer(i, IID_PPV_ARGS(&resource));

				device->GetDx12Device()->CreateRenderTargetView(resource, nullptr, rtvHandle);

				// サイズ分進める
				rtvHandle.ptr += _descriptorHeapManager->GetRtvDescriptorSize();
			}

#ifdef CONSOLE_DEBUG
			std::cout << "**************************************************"   << std::endl;
			std::cout << "Succeeded Initialize Render Target View"              << std::endl;
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

		bool Buffer::makeBuffer(Device* device, RHI::DirectX12::ResourceBuffer* resourceBuffer, uint64_t sizeInBytes, D3D12_HEAP_TYPE heapType)
		{
			return SUCCEEDED(
				resourceBuffer->Create(
					device->GetDx12Device(),
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
				sizeof(uint64_t)		// 仮 TODO : 頂点構造体に変更(4Byte)
			);
		}

		bool Buffer::makeSceneVBV(Device* device)
		{
			if (!_sceneVB.get()) return false;

			makeVBView(
				device,
				_sceneVB.get(),
				_SCENE_VB_SIZE,
				sizeof(uint64_t)		// 仮 TODO : 頂点構造体に変更(4Byte)
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
			device->GetDx12Device()->CreateShaderResourceView(resourceBuffer->Get(), &srvDesc, cpuHandle);

			return S_OK;
		}

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
			device->GetDx12Device()->CreateConstantBufferView(&cbvDesc, cbvHandle);

			return S_OK;
		}
	}
}

/************************************
* End of file
************************************/
