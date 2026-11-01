
/***********************************************************************************
* @file    dx12_fence.cpp
* @brief   DX12のフェンス
* @details
*
* @author  Keigo Onari
* @date    2026/02/03
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
#include "dx12_fence.h"

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
		Fence::Fence() = default;
		Fence::~Fence()
		{
			Release();
		}

		// ==============================================
		// フェンスの作成
		// ==============================================
		HRESULT Fence::Create(ID3D12Device* device)
		{
			if (!device || _pFence)
			{
				return E_INVALIDARG;
			}

			RHI_RETURN_IF_FAILED(device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&_pFence)));
			_nextValue = 1;

			_fenceEvent = CreateEvent(nullptr, FALSE, FALSE, nullptr);
			if (!_fenceEvent)
			{
				_pFence.Reset();
				return HRESULT_FROM_WIN32(GetLastError());
			}
			return S_OK;
		}

		// ==============================================
		// フェンスオブジェクトの取得
		// ==============================================
		ID3D12Fence* Fence::Get(void)const
		{
			return _pFence.Get();
		}

		UINT64 Fence::Signal(ID3D12CommandQueue* cmdQueue)
		{
			if (!cmdQueue || !_pFence)
			{
				return 0;
			}

			const UINT64 value = _nextValue;
			if (FAILED(cmdQueue->Signal(_pFence.Get(), value)))
			{
				return 0;
			}
			_nextValue++;
			return value;
		}

		bool Fence::IsCompleted(UINT64 value)const
		{
			return _pFence && _pFence->GetCompletedValue() >= value;
		}

		HRESULT Fence::WaitForValue(UINT64 value)
		{
			if (!_pFence || !_fenceEvent)
			{
				return E_FAIL;
			}

			if (_pFence->GetCompletedValue() >= value)
			{
				return S_OK;
			}

			RHI_RETURN_IF_FAILED(_pFence->SetEventOnCompletion(value, _fenceEvent));
			WaitForSingleObject(_fenceEvent, INFINITE);
			return S_OK;
		}

		HRESULT Fence::WaitForSignal(RHI::DirectX12::CommandQueue* cmdQueue)
		{
			if (!cmdQueue)
			{
				return E_INVALIDARG;
			}

			const UINT64 value = Signal(cmdQueue->Get());
			if (value == 0)
			{
				return E_FAIL;
			}
			return WaitForValue(value);
		}

		UINT64 Fence::GetLastSignaledValue(void)const
		{
			return _nextValue - 1;
		}

		// ==============================================
		// 解放
		// ==============================================
		void Fence::Release(void)
		{
			if (_fenceEvent)
			{
				CloseHandle(_fenceEvent);
				_fenceEvent = nullptr;
			}
			_pFence.Reset();
		}
	}
}

/************************************
* End of file
************************************/
