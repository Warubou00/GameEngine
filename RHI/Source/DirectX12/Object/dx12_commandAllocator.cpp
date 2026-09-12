
/***********************************************************************************
* @file    dx12_commandAllocator.cpp
* @brief   DX12のコマンド亜ロケーター
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
#include "dx12_commandAllocator.h"

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
		CommandAllocator::CommandAllocator() {}
		CommandAllocator::~CommandAllocator() {}

		// ==============================================
		// コマンドアロケータの作成
		// ==============================================
		HRESULT CommandAllocator::Create(ID3D12Device* device)
		{
			if (!device)
			{
				// TODO : 例外処理 -> nullptr
				return E_NOTIMPL;
			}
			return device->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&_pCmdAllocator));
		}

		// ==============================================
		// コマンドアロケータの取得
		// ==============================================
		ID3D12CommandAllocator* CommandAllocator::Get(void)
		{
			return _pCmdAllocator.Get();
		}

		// ==============================================
		// 解放
		// ==============================================
		void CommandAllocator::Release(void)
		{
			// ComPtrなので明示的な解放は避ける
			//if (_pCmdAllocator.Get())_pCmdAllocator->Release();
		}

	}
}

/************************************
* End of file
************************************/