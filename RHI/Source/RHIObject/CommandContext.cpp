
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

#include "../DXGI/dxgiFactory6.h"
#include "../DirectX12/Object/dx12_device.h"
#include "../DirectX12/Object/dx12_graphicsCommandList.h"
#include "../DirectX12/Object/dx12_commandQueue.h"
#include "../DirectX12/Object/dx12_commandAllocator.h"
#include "../DirectX12/Object/dx12_swapChain.h"

#include <d3d12.h>

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
		CommandContext::CommandContext() = default;
		CommandContext::~CommandContext() = default;

		RHI_RESULT CommandContext::Initialize()
		{
			return RHI_SUCCEEDED;
		}

		RHI_RESULT CommandContext::Initialize(Device* device, ObjectInfo* info)
		{
			if (!device)
			{
				return RHI_FAILED_INVALID_ARGUMENT;
			}

			_commandQueue        = std::make_unique<DirectX12::CommandQueue>();
			_commandAllocator    = std::make_unique<DirectX12::CommandAllocator>();
			_graphicsCommandList = std::make_unique<DirectX12::GraphicsCommandList>();
			_swapChain           = std::make_unique<DirectX12::SwapChain>();

			ID3D12Device* dx12Device = device->GetDevice()->GetDevice();
			HRESULT hr = _commandQueue->Create(dx12Device);
			hr         = _commandAllocator->Create(dx12Device);
			hr         = _graphicsCommandList->Create(dx12Device, _commandAllocator->Get());

			HWND hWnd   = static_cast<HWND>(info->HWnd);
			UINT width  = static_cast<UINT>(info->Width);
			UINT height = static_cast<UINT>(info->Height);
			hr         = _swapChain->Create(device->GetDxgiFactory()->Get(), _commandQueue->Get(), &hWnd, width, height);

#ifdef CONSOLE_DEBUG
			Core::Logger::Log("**************************************************");
			Core::Logger::Log("Succeeded Initialize CommandContext From C++");
			std::cout << "Succeeded Initialize CommandContext From C++" << std::endl;
			std::cout << "Width  : " << width  << std::endl;
			std::cout << "Height : " << height << std::endl;
			Core::Logger::Log("**************************************************");
#endif

			return RHI_SUCCEEDED;
		}

		RHI::DirectX12::GraphicsCommandList* CommandContext::GetGraphicsCommandList()const
		{
			return _graphicsCommandList.get();
		}
		RHI::DirectX12::CommandQueue* CommandContext::GetCommandQueue()const
		{
			return _commandQueue.get();
		}
		RHI::DirectX12::CommandAllocator* CommandContext::GetCommandAllocator()const
		{
			return _commandAllocator.get();
		}
		RHI::DirectX12::SwapChain* CommandContext::GetSwapChain()const
		{
			return _swapChain.get();
		}
	}
}

/************************************
* End of file
************************************/
