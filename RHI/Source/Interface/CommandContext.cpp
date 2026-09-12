
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
			_commandQueue = std::make_unique<DirectX12::CommandQueue>();
			_commandAllocator = std::make_unique<DirectX12::CommandAllocator>();
			_graphicsCommandList = std::make_unique<DirectX12::GraphicsCommandList>();
			
			HRESULT hr = _commandQueue->Create(device->GetDx12Device());
			hr = _commandAllocator->Create(device->GetDx12Device());
			hr = _graphicsCommandList->Create(device->GetDx12Device(), _commandAllocator->Get());

			return true;
		}
	}
}

/************************************
* End of file
************************************/
