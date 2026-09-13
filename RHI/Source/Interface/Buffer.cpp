
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
#include "../DirectX12/dx12_resourceManager.h"
#include "../DirectX12/dx12_descriptorHeapManager.h"

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

		bool Buffer::Initialize()
		{
			return true;
		}

		bool Buffer::Initialize(Device* device, CommandContext* commandContext)
		{
			if (!device || !commandContext)
			{
				// TODO : 例外処理 -> nullptr
				return false;
			}

			// DescriptorHeapの作成
			DirectX12::DescriptorHeapManager::getInstance().Create(device->GetDx12Device());

			// レンダーターゲットビューの作成
			makeRtv(device, commandContext);

			return true;
		}

		bool Buffer::CreateVertexBuffer(Device* device)
		{
			D3D12_HEAP_PROPERTIES hDesc = {};
			D3D12_RESOURCE_DESC resDesc = {};
			DirectX12::ResourceManager::getInstance().CreateVertResource(device->GetDx12Device(), hDesc, resDesc);

			return true;
		}

		bool Buffer::makeRtv(Device* device, CommandContext* commandContext)
		{
			// RTVのハンドル取得
			auto rtvHandle = DirectX12::DescriptorHeapManager::getInstance().GetRtvCpuHandleStart();

			// SwapChainDescを取得
			DXGI_SWAP_CHAIN_DESC1 scDesc = {};
			auto swapChainDesc = commandContext->GetSwapChain()->GetDesc1(&scDesc);

			// rtvの作成
			ID3D12Resource* resource = nullptr;
			for (int i = 0; i = scDesc.BufferCount; i++)
			{
				commandContext->GetSwapChain()->GetBuffer(i, IID_PPV_ARGS(&resource));

				device->GetDx12Device()->CreateRenderTargetView(resource, nullptr, rtvHandle);

				// サイズ分進める
				rtvHandle.ptr += DirectX12::DescriptorHeapManager::getInstance().GetRtvDescriptorSize();

				// ResourceManagerに登録
				DirectX12::ResourceManager::getInstance().RegistRtvResource(resource);
			}

			return true;
		}
	}
}

/************************************
* End of file
************************************/
