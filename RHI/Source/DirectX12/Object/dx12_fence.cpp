
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

// API DirectX12オブジェクトをインクルード
// ========================================
#include "dx12_device.h"
#include "dx12_swapChain.h"
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
		Fence::Fence() {}
		Fence::~Fence() {}

		void Fence::WaitForSignal(RHI::APIInterface::ICommandQueue* iCommandQueue)
		{
			auto cmdQueue = static_cast<CommandQueue*>(iCommandQueue);
			
			// ==============================================================
			// フェンスを用いたGPU待機の同期処理
			// ==============================================================
			const UINT64 currentFenceValue = _fenceVal;
			cmdQueue->Get()->Signal(_pFence.Get(), currentFenceValue);
			_fenceVal++;

			if (_pFence->GetCompletedValue() < currentFenceValue)
			{
				_pFence->SetEventOnCompletion(currentFenceValue, _fenceEvent);
				WaitForSingleObject(_fenceEvent, INFINITE);
			}
		}

		// ==============================================
		// フェンスの作成
		// ==============================================
		RHI_API_RESULT Fence::Initialize(RHI::APIInterface::IDevice* iDevice, RHI::APIInterface::ISwapChain* iSwapChain)
		{
			if (!iDevice || !iSwapChain)
			{// 例外処理 nullptr
				return RHI_API_FAILED_INVALID_ARGUMENT;
			}

			auto device = static_cast<RHI::DirectX12::Device*>(iDevice);
			auto swapChain = static_cast<RHI::DirectX12::SwapChain*>(iSwapChain);

			if (!device || !swapChain)
			{
				return RHI_API_FAILED_INVALID_ARGUMENT;
			}

			if (FAILED(device->GetDevice()->CreateFence(_fenceVal, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&_pFence))))
			{
				return E_INVALIDARG;
			}

			// ==============================================================
			// フェンスとイベントの初期化
			// ==============================================================
			_fenceVal = 1;
			_fenceEvent = CreateEvent(nullptr, FALSE, FALSE, nullptr);

			// 最初のフレームインデックスを取得
			_frameBufferIndex = swapChain->Get()->GetCurrentBackBufferIndex();

			return S_OK;
		}

		// ==============================================
		// フェンスオブジェクトの取得
		// ==============================================
		ID3D12Fence* Fence::Get(void)const
		{
			return _pFence.Get();
		}

		// ==============================================
		// 解放
		// ==============================================
		void Fence::Release(void)
		{
			// ComPtrのため明示的な解放は避ける
			//if (_pFence.Get())_pFence.Get()->Release();
		}
	}
}

/************************************
* End of file
************************************/