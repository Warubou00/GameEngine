
/***********************************************************************************
* @file    dx12_swapChain.cpp
* @brief   DX12のスワップチェーン
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
#include "dx12_swapChain.h"

// API DirectX12オブジェクトをインクルード
// ========================================
#include "../../DXGI/dxgiFactory6.h"
#include "dx12_device.h"
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
		SwapChain::SwapChain() = default;
		SwapChain::~SwapChain() = default;

		RHI_API_RESULT SwapChain::Initialize(
			RHI::APIInterface::IDevice* iDevice,
			RHI::APIInterface::ICommandQueue* iCmdQueue,
			HWND* hWnd,
			UINT width,
			UINT height
		)
		{
			if (!iDevice || !iCmdQueue)
			{
				return RHI_API_FAILED_INVALID_ARGUMENT;
			}

			// 静的キャスト
			auto device   = static_cast<RHI::DirectX12::Device*>(iDevice);
			auto cmdQueue = static_cast<RHI::DirectX12::CommandQueue*>(iCmdQueue);

			if (!device || !cmdQueue)
			{
				return RHI_API_FAILED_INVALID_ARGUMENT;
			}

			DXGI_SWAP_CHAIN_DESC1 swapchainDesc = {};
			swapchainDesc.Width = width;
			swapchainDesc.Height = height;
			swapchainDesc.Format = DXGI_FORMAT_R8G8B8A8_UNORM;
			swapchainDesc.Stereo = false;
			swapchainDesc.SampleDesc.Count = 1;
			swapchainDesc.SampleDesc.Quality = 0;
			swapchainDesc.BufferUsage = DXGI_USAGE_BACK_BUFFER;
			swapchainDesc.BufferCount = 2;
			swapchainDesc.Scaling = DXGI_SCALING_STRETCH;
			swapchainDesc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
			swapchainDesc.AlphaMode = DXGI_ALPHA_MODE_UNSPECIFIED;
			swapchainDesc.Flags = DXGI_SWAP_CHAIN_FLAG_ALLOW_MODE_SWITCH;

			return device->GetDXGIFactory()->Get()->CreateSwapChainForHwnd(
				cmdQueue->Get(),
				*hWnd,
				&swapchainDesc,
				nullptr,
				nullptr,
				reinterpret_cast<IDXGISwapChain1**>(_pSwapChain.GetAddressOf()));
		}

		IDXGISwapChain4* SwapChain::Get(void)const
		{
			return _pSwapChain.Get();
		}

		UINT SwapChain::GetFrameBufferIndex(void)const
		{
			return _pSwapChain->GetCurrentBackBufferIndex();
		}

		ID3D12Resource* SwapChain::GetCurrentBuffer(void)const
		{
			ID3D12Resource* resource = nullptr;
			auto currentIndex = GetFrameBufferIndex();
			_pSwapChain->GetBuffer(currentIndex, IID_PPV_ARGS(&resource));

			return resource;
		}

		void SwapChain::Present()
		{
			// 9. 画面への表示とインデックス更新
			_pSwapChain->Present(1, 0);
		}

		void SwapChain::Release(void)
		{
			// ComPtrのため明示的な解放はしない
			//if (_pSwapChain.Get())_pSwapChain.Get()->Release();
		}
	}
}

/************************************
* End of file
************************************/
