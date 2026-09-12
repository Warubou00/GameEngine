
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
		HRESULT CommandQueue::Create(ID3D12Device* device)
		{
			D3D12_COMMAND_QUEUE_DESC cmdQueueDesc = {};
			cmdQueueDesc.Flags = D3D12_COMMAND_QUEUE_FLAG_NONE;																//タイムアウトなし
			cmdQueueDesc.NodeMask = 0;
			cmdQueueDesc.Priority = D3D12_COMMAND_QUEUE_PRIORITY_NORMAL;													//プライオリティ特に指定なし
			cmdQueueDesc.Type = D3D12_COMMAND_LIST_TYPE_DIRECT;																//コマンドリストと同値
			return device->CreateCommandQueue(&cmdQueueDesc, IID_PPV_ARGS(&_pCmdQueue));//コマンドキュー生成
		}

		// ==============================================
		// コマンドキューの取得
		// ==============================================
		ID3D12CommandQueue* CommandQueue::Get(void)
		{
			return _pCmdQueue.Get();
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