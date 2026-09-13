
/***********************************************************************************
* @file    CommandContext.cpp
* @brief   RHI CommandContext
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
#include "CommandContext.h"
#include "Device.h"

#include "../DirectX12/Object/dx12_graphicsCommandList.h"
#include "../DirectX12/Object/dx12_commandQueue.h"
#include "../DirectX12/Object/dx12_commandAllocator.h"
#include "../DirectX12/Object/dx12_swapChain.h"

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
		CommandContext::CommandContext() = default;
		CommandContext::~CommandContext() = default;

		bool CommandContext::Initialize()
		{
			return true;
		}

		bool CommandContext::Initialize(Device* device)
		{
			_commandQueue        = std::make_unique<DirectX12::CommandQueue>();
			_commandAllocator    = std::make_unique<DirectX12::CommandAllocator>();
			_graphicsCommandList = std::make_unique<DirectX12::GraphicsCommandList>();
			_swapChain           = std::make_unique<DirectX12::SwapChain>();

			HRESULT hr = _commandQueue->Create(device->GetDx12Device());
			hr         = _commandAllocator->Create(device->GetDx12Device());
			hr         = _graphicsCommandList->Create(device->GetDx12Device(), _commandAllocator->Get());
			hr         = _swapChain->Create(device->GetDxgiFactory6(), _commandQueue->Get(), nullptr, 1920, 1080);
			return true;
		}

		IDXGISwapChain4* CommandContext::GetSwapChain()const
		{
			return _swapChain->Get();
		}
	}
}

/************************************
* End of file
************************************/
