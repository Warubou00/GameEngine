
/***********************************************************************************
* @file    dx12_graphicsCommandList.cpp
* @brief   DX12のグラフィックコマンドリスト
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
		GraphicsCommandList::GraphicsCommandList() {}
		GraphicsCommandList::~GraphicsCommandList() {}

		// ==============================================
		// コマンドリストの作成
		// ==============================================
		HRESULT GraphicsCommandList::Create(ID3D12Device* device, ID3D12CommandAllocator* cmdAllocator)
		{
			if (!device || !cmdAllocator)
			{
				// TODO : 例外処理 -> nullptr

				return E_NOTIMPL;
			}
			return device->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, cmdAllocator, nullptr, IID_PPV_ARGS(&_pCmdList));
		}

		HRESULT GraphicsCommandList::TryVersionUp()
		{
			if (!_pCmdList)
			{
				// TODO : 例外処理 -> nullptr

				return E_NOTIMPL;
			}

			return asCmdList();
		}

		HRESULT GraphicsCommandList::TryVersionUp4()
		{
			if (!_pCmdList)
			{
				// TODO : 例外処理 -> nullptr

				return E_NOTIMPL;
			}

			return asCmdList4();
		}

		HRESULT GraphicsCommandList::TryVersionUp6()
		{
			if (!_pCmdList)
			{
				// TODO : 例外処理 -> nullptr

				return E_NOTIMPL;
			}

			// 4も前提に...
			asCmdList4();
			return asCmdList6();
		}

		HRESULT GraphicsCommandList::TryVersionUp7()
		{
			if (!_pCmdList)
			{
				// TODO : 例外処理 -> nullptr

				return E_NOTIMPL;
			}

			// 4,6も前提に...
			asCmdList4();
			asCmdList6();
			return asCmdList7();
		}

		// ==============================================
		// コマンドリストの取得
		// ==============================================
		ID3D12GraphicsCommandList* GraphicsCommandList::GetCmdList(void)const
		{
			return _pCmdList.Get();
		}

		ID3D12GraphicsCommandList4* GraphicsCommandList::GetCmdList4(void)const
		{
			return _pCmdList4.Get();
		}

		ID3D12GraphicsCommandList6* GraphicsCommandList::GetCmdList6(void)const
		{
			return _pCmdList6.Get();
		}

		ID3D12GraphicsCommandList7* GraphicsCommandList::GetCmdList7(void)const
		{
			return _pCmdList7.Get();
		}

		HRESULT GraphicsCommandList::asCmdList()
		{
			HRESULT hr = S_OK;

			hr = asCmdList4();
			hr = asCmdList6();
			hr = asCmdList7();

			return hr;
		}

		HRESULT GraphicsCommandList::asCmdList4()
		{
			return _pCmdList.As(&_pCmdList4);
		}

		HRESULT GraphicsCommandList::asCmdList6()
		{
			return _pCmdList.As(&_pCmdList6);
		}

		HRESULT GraphicsCommandList::asCmdList7()
		{
			return _pCmdList.As(&_pCmdList7);
		}

		// ==============================================
		// 解放
		// ==============================================
		void GraphicsCommandList::Release(void)
		{
			// ComPtrなので明示的な解放は避ける
			//if (_pCmdList.Get())_pCmdList->Release();
		}
	}
}

/************************************
* End of file
************************************/