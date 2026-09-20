
/***********************************************************************************
* @file    dx12_commandQueue.cpp
* @brief   DX12のコマンドキュー
* @details
*
* @author  Keigo Onari
* @date    2026/02/02
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
#include "dx12_commandQueue.h"

// API DirectX12オブジェクトをインクルード
// ========================================
#include "dx12_device.h"
#include "dx12_graphicsCommandList.h"

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
	namespace DirectX12
	{
		CommandQueue::CommandQueue() {}
		CommandQueue::~CommandQueue() {}

		// ==============================================
		// コマンドキューの作成
		// ==============================================
		RHI_API_RESULT CommandQueue::Initialize(RHI::APIInterface::IDevice* iDevice)
		{
			if (!iDevice)
			{
				return RHI_API_FAILED_INVALID_ARGUMENT;
			}

			auto device = static_cast<RHI::DirectX12::Device*>(iDevice);
			if (!device)
			{
				return E_NOTIMPL;
			}

			D3D12_COMMAND_QUEUE_DESC cmdQueueDesc = {};
			cmdQueueDesc.Flags = D3D12_COMMAND_QUEUE_FLAG_NONE;																//タイムアウトなし
			cmdQueueDesc.NodeMask = 0;
			cmdQueueDesc.Priority = D3D12_COMMAND_QUEUE_PRIORITY_NORMAL;													//プライオリティ特に指定なし
			cmdQueueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;																//コマンドリストと同値
			return device->GetDevice()->CreateCommandQueue(&cmdQueueDesc, IID_PPV_ARGS(&_pCmdQueue));//コマンドキュー生成
		}

		// ==============================================
		// コマンドキューの取得
		// ==============================================
		ID3D12CommandQueue* CommandQueue::Get(void)
		{
			return _pCmdQueue.Get();
		}

		void CommandQueue::End(RHI::APIInterface::ICommandList* iCmdList)
		{
			if (!iCmdList)
			{
				return;
			}

			auto grCmdList = static_cast<RHI::DirectX12::GraphicsCommandList*>(iCmdList);
			if (!grCmdList)
			{
				return;
			}
			auto cmdList = grCmdList->GetQueueList();
			_pCmdQueue.Get()->ExecuteCommandLists(1, &cmdList); //[cite: 5]
		}

		// ==============================================
		// 解放
		// ==============================================
		void CommandQueue::Release(void)
		{
			// ComPtrのため、明示的な解放はしない
			//if (_pCmdQueue.Get())_pCmdQueue->Release();
		}
	}
}

/************************************
* End of file
************************************/