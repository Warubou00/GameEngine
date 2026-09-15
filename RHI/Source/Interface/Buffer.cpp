
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

			if (!makeBuffer(device))
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

		bool Buffer::makeBuffer(Device* device)
		{
			if (!device)return false;

			if (!makeGlobalVB(device))return false;
			if (!makeSceneVB(device))return false;
			if (!makeGlobalCB(device))return false;
			if (!makeSceneCB(device))return false;

			return true;
		}

		bool Buffer::makeGlobalVB(Device* device)
		{
			_globalVB = std::make_unique<RHI::DirectX12::ResourceBuffer>();
			
			HRESULT hr = _globalVB->Create(
				device->GetDx12Device(),
				_GLOBAL_VB_SIZE,
				D3D12_HEAP_TYPE_DEFAULT
			);

			return SUCCEEDED(hr);
		}

		bool Buffer::makeSceneVB(Device* device)
		{
			_sceneVB = std::make_unique<RHI::DirectX12::ResourceBuffer>();

			HRESULT hr = _sceneVB->Create(
				device->GetDx12Device(),
				_SCENE_VB_SIZE,
				D3D12_HEAP_TYPE_DEFAULT
			);

			return SUCCEEDED(hr);
		}

		bool Buffer::makeGlobalCB(Device* device)
		{
			_globalCB = std::make_unique<RHI::DirectX12::ResourceBuffer>();

			HRESULT hr = _globalCB->Create(
				device->GetDx12Device(),
				_GLOBAL_CB_SIZE,
				D3D12_HEAP_TYPE_UPLOAD
			);

			return SUCCEEDED(hr);
		}

		bool Buffer::makeSceneCB(Device* device)
		{
			_sceneCB = std::make_unique<RHI::DirectX12::ResourceBuffer>();

			HRESULT hr = _sceneCB->Create(
				device->GetDx12Device(),
				_SCENE_CB_SIZE,
				D3D12_HEAP_TYPE_UPLOAD
			);

			return SUCCEEDED(hr);
		}
	}
}

/************************************
* End of file
************************************/
