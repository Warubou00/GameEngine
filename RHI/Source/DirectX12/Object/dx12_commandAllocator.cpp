
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
		HRESULT CommandAllocator::Create(ID3D12Device* device, D3D12_COMMAND_LIST_TYPE type)
		{
			if (!device || _pCmdAllocator)
			{
				return E_INVALIDARG;
			}
			return device->CreateCommandAllocator(type, IID_PPV_ARGS(&_pCmdAllocator));
		}

		// ==============================================
		// コマンドアロケータの取得
		// ==============================================
		ID3D12CommandAllocator* CommandAllocator::Get(void)const
		{
			return _pCmdAllocator.Get();
		}

		// ==============================================
		// コマンドアロケータのリセット
		// ==============================================
		HRESULT CommandAllocator::Reset(void)
		{
			if (!_pCmdAllocator)
			{
				return E_FAIL;
			}
			return _pCmdAllocator->Reset();
		}

		// ==============================================
		// 解放
		// ==============================================
		void CommandAllocator::Release(void)
		{
			_pCmdAllocator.Reset();
		}

	}
}

/************************************
* End of file
************************************/