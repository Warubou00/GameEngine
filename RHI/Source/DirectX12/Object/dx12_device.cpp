
/***********************************************************************************
* @file    dx12_device.cpp
* @brief   DX12のオブジェクト
* @details 
*
* @author  Keigo Onari
* @date    2026/1/13
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
#include "dx12_device.h"

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
		//フィーチャレベル列挙
		D3D_FEATURE_LEVEL levels[] = {
			D3D_FEATURE_LEVEL_12_1,
			D3D_FEATURE_LEVEL_12_0,
			D3D_FEATURE_LEVEL_11_1,
			D3D_FEATURE_LEVEL_11_0,
		};

		Device::Device() : _isRaytracingSupported(false), _isRaytracingSupported_1_1(false), _isMeshShaderSupported(false) {}
		Device::~Device() = default;

		HRESULT Device::Create(IDXGIAdapter* adapter)
		{
			//Direct3Dデバイスの初期化
			D3D_FEATURE_LEVEL featureLevel;
			for (auto l : levels) {
				if (D3D12CreateDevice(adapter, l, IID_PPV_ARGS(&_pDevice)) == S_OK) {
					featureLevel = l;
					return S_OK;
				}
			}

			return E_INVALIDARG;
		}

		HRESULT Device::TryVersionUp()
		{
			if (!_pDevice)
			{
				// TODO : 例外処理 -> nullptr
				return E_NOTIMPL;
			}

			return asDevice();
		}

		HRESULT Device::TryVersionUp5()
		{
			if (!_pDevice)
			{
				// TODO : 例外処理 -> nullptr
				return E_NOTIMPL;
			}

			return asDevice5();
		}

		HRESULT Device::TryVersionUp9()
		{
			if (!_pDevice)
			{
				// TODO : 例外処理 -> nullptr
				return E_NOTIMPL;
			}

			// 5も前提に...
			asDevice5();
			return asDevice9();
		}

		ID3D12Device* Device::GetDevice()const
		{
			return _pDevice.Get();
		}

		ID3D12Device5* Device::GetDevice5()const
		{
			return _pDevice5.Get();
		}

		ID3D12Device9* Device::GetDevice9()const
		{
			return _pDevice9.Get();
		}

		void Device::Release(void)
		{
			// ComPtrなので明示的な解放は避ける
			//if (_pDevice.Get())_pDevice.Get()->Release();
		}
		
		HRESULT Device::asDevice()
		{
			// TODO : hrの例外処理

			HRESULT hr = S_OK;

			hr = asDevice5();
			hr = asDevice9();

			return hr;
		}

		HRESULT Device::asDevice5()
		{
			// ID3D12Device5で使えるか試みる
			if (SUCCEEDED(_pDevice.As(&_pDevice5)))
			{
				// デバイスオプションの取得
				D3D12_FEATURE_DATA_D3D12_OPTIONS5 option5 = {};
				if (SUCCEEDED(_pDevice5->CheckFeatureSupport(D3D12_FEATURE_D3D12_OPTIONS5,
					&option5,
					sizeof(option5)
				)))
				{
					// レイトレをサポートしているかチェック
					if (option5.RaytracingTier != D3D12_RAYTRACING_TIER_NOT_SUPPORTED)
					{
						if (option5.RaytracingTier == D3D12_RAYTRACING_TIER_1_1)
						{
							_isRaytracingSupported_1_1 = true;
						}
						_isRaytracingSupported = true;

						return S_OK;
					}
				}
			}

			return E_INVALIDARG;
		}

		HRESULT Device::asDevice9()
		{
			// ID3D12Device9で使えるか試みる
			if (SUCCEEDED(_pDevice.As(&_pDevice9)))
			{
				// デバイスオプションの取得
				D3D12_FEATURE_DATA_D3D12_OPTIONS9 option9 = {};
				if (SUCCEEDED(_pDevice9->CheckFeatureSupport(D3D12_FEATURE_D3D12_OPTIONS9,
					&option9,
					sizeof(option9)
				)))
				{
					// MeshShaderをサポートしているかチェック
					if (option9.MeshShaderPipelineStatsSupported)
					{
						_isMeshShaderSupported = true;
						return S_OK;
					}
				}
			}

			return E_INVALIDARG;
		}
	}
}

/************************************
* End of file
************************************/
