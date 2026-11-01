
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
			if (!device || !cmdAllocator || _pCmdList)
			{
				return E_INVALIDARG;
			}

			// CreateCommandList直後は記録中(Open)状態で返ってくる
			RHI_RETURN_IF_FAILED(device->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, cmdAllocator, nullptr, IID_PPV_ARGS(&_pCmdList)));
			_isRecording = true;
			return S_OK;
		}

		// ==============================================
		// 記録の開始
		// ==============================================
		HRESULT GraphicsCommandList::Reset(ID3D12CommandAllocator* cmdAllocator, ID3D12PipelineState* pso)
		{
			if (!_pCmdList || !cmdAllocator)
			{
				return E_INVALIDARG;
			}

			RHI_RETURN_IF_FAILED(_pCmdList->Reset(cmdAllocator, pso));
			_isRecording = true;
			return S_OK;
		}

		// ==============================================
		// 記録の終了
		// ==============================================
		HRESULT GraphicsCommandList::Close(void)
		{
			if (!_pCmdList)
			{
				return E_FAIL;
			}

			// 二重Closeはエラーになるため、記録中でなければ何もしない
			if (!_isRecording)
			{
				return S_OK;
			}

			_isRecording = false;
			return _pCmdList->Close();
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
			// 上位版が取れなかった時点で打ち切る
			HRESULT hr = asCmdList4();
			if (FAILED(hr)) return hr;
			hr = asCmdList6();
			if (FAILED(hr)) return hr;
			return asCmdList7();
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
			_pCmdList7.Reset();
			_pCmdList6.Reset();
			_pCmdList4.Reset();
			_pCmdList.Reset();
			_isRecording = false;
		}
	}
}

/************************************
* End of file
************************************/