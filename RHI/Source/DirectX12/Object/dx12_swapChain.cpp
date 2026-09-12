
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

		HRESULT SwapChain::Create(IDXGIFactory6* pDxgiFactory,
			ID3D12CommandQueue* pCmdQueue,
			HWND* hWnd,
			UINT width,
			UINT height)
		{
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

			return pDxgiFactory->CreateSwapChainForHwnd(
				pCmdQueue,
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
