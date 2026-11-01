
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
		namespace
		{
			constexpr UINT SWAP_CHAIN_FLAGS = DXGI_SWAP_CHAIN_FLAG_ALLOW_MODE_SWITCH;
		}

		SwapChain::SwapChain() = default;
		SwapChain::~SwapChain()
		{
			Release();
		}

		HRESULT SwapChain::Create(IDXGIFactory6* pDxgiFactory,
			ID3D12CommandQueue* pCmdQueue,
			HWND* hWnd,
			UINT width,
			UINT height)
		{
			if (!pDxgiFactory || !pCmdQueue || !hWnd || !*hWnd || width == 0 || height == 0 || _pSwapChain)
			{
				return E_INVALIDARG;
			}

			DXGI_SWAP_CHAIN_DESC1 swapchainDesc = {};
			swapchainDesc.Width = width;
			swapchainDesc.Height = height;
			swapchainDesc.Format = FORMAT;
			swapchainDesc.Stereo = false;
			swapchainDesc.SampleDesc.Count = 1;
			swapchainDesc.SampleDesc.Quality = 0;
			swapchainDesc.BufferUsage = DXGI_USAGE_RENDER_TARGET_OUTPUT;               // 描画先として使う
			swapchainDesc.BufferCount = BUFFER_COUNT;                                  // バックバッファーの数
			swapchainDesc.Scaling = DXGI_SCALING_STRETCH;
			swapchainDesc.SwapEffect = DXGI_SWAP_EFFECT_FLIP_DISCARD;
			swapchainDesc.AlphaMode = DXGI_ALPHA_MODE_UNSPECIFIED;
			swapchainDesc.Flags = SWAP_CHAIN_FLAGS;

			// CreateSwapChainForHwnd が返すのは IDXGISwapChain1。
			// SwapChain4 の格納先へ reinterpret_cast で直接書くのは型違いなので、1で受けてから As する
			Microsoft::WRL::ComPtr<IDXGISwapChain1> swapChain1;
			RHI_RETURN_IF_FAILED(pDxgiFactory->CreateSwapChainForHwnd(
				pCmdQueue,
				*hWnd,
				&swapchainDesc,
				nullptr,
				nullptr,
				&swapChain1));
			RHI_RETURN_IF_FAILED(swapChain1.As(&_pSwapChain));

			_width  = width;
			_height = height;
			return acquireBackBuffers();
		}

		HRESULT SwapChain::Resize(UINT width, UINT height)
		{
			if (!_pSwapChain) return E_FAIL;

			// 最小化中はサイズ0が来るので無視
			if (width == 0 || height == 0) return S_OK;
			if (width == _width && height == _height) return S_OK;

			// バックバッファへの参照が残っているとResizeBuffersが失敗する
			releaseBackBuffers();
			RHI_RETURN_IF_FAILED(_pSwapChain->ResizeBuffers(BUFFER_COUNT, width, height, FORMAT, SWAP_CHAIN_FLAGS));

			_width  = width;
			_height = height;
			return acquireBackBuffers();
		}

		HRESULT SwapChain::Present(UINT syncInterval)
		{
			if (!_pSwapChain) return E_FAIL;
			return _pSwapChain->Present(syncInterval, 0);
		}

		IDXGISwapChain4* SwapChain::Get(void)const
		{
			return _pSwapChain.Get();
		}

		UINT SwapChain::GetCurrentBackBufferIndex(void)const
		{
			return _pSwapChain ? _pSwapChain->GetCurrentBackBufferIndex() : 0;
		}

		ID3D12Resource* SwapChain::GetBufferFromIndex(UINT index)const
		{
			// 添え字オーバー
			if (index >= BUFFER_COUNT)return nullptr;

			// 以前は GetBuffer で毎回参照カウントを増やしたまま返していたためリークしていた
			return _backBuffers[index].Get();
		}

		ID3D12Resource* SwapChain::GetBackBuffer(void)const
		{
			return GetBufferFromIndex(GetCurrentBackBufferIndex());
		}

		UINT SwapChain::GetBufferCount(void)const
		{
			return BUFFER_COUNT;
		}

		void SwapChain::Release(void)
		{
			releaseBackBuffers();
			_pSwapChain.Reset();
			_width  = 0;
			_height = 0;
		}

		HRESULT SwapChain::acquireBackBuffers()
		{
			for (UINT i = 0; i < BUFFER_COUNT; ++i)
			{
				HRESULT hr = _pSwapChain->GetBuffer(i, IID_PPV_ARGS(&_backBuffers[i]));
				if (FAILED(hr))
				{
					releaseBackBuffers();
					return hr;
				}
			}
			return S_OK;
		}

		void SwapChain::releaseBackBuffers()
		{
			for (auto& buffer : _backBuffers)
			{
				buffer.Reset();
			}
		}
	}
}

/************************************
* End of file
************************************/
