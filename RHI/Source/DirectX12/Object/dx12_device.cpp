
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
		namespace
		{
			//フィーチャレベル列挙 (高い順に試す)
			constexpr D3D_FEATURE_LEVEL FEATURE_LEVELS[] = {
				D3D_FEATURE_LEVEL_12_1,
				D3D_FEATURE_LEVEL_12_0,
				D3D_FEATURE_LEVEL_11_1,
				D3D_FEATURE_LEVEL_11_0,
			};
		}

		Device::Device() : _isRaytracingSupported(false), _isRaytracingSupported_1_1(false), _isMeshShaderSupported(false) {}
		Device::~Device() = default;

		HRESULT Device::Create(IDXGIAdapter* adapter)
		{
			if (!adapter || _pDevice)
			{
				return E_INVALIDARG;
			}

			//Direct3Dデバイスの初期化
			HRESULT hr = E_FAIL;
			for (auto l : FEATURE_LEVELS) {
				hr = D3D12CreateDevice(adapter, l, IID_PPV_ARGS(&_pDevice));
				if (SUCCEEDED(hr)) {
					_featureLevel = l;
					return S_OK;
				}
			}

			// 最後に失敗した理由を返す
			return hr;
		}

		HRESULT Device::TryVersionUp()
		{
			if (!_pDevice)
			{
				return E_FAIL;
			}

			return asDevice();
		}

		HRESULT Device::TryVersionUp5()
		{
			if (!_pDevice)
			{
				return E_FAIL;
			}

			HRESULT hr = asDevice5();
			checkFeatureSupport();
			return hr;
		}

		HRESULT Device::TryVersionUp9()
		{
			if (!_pDevice)
			{
				return E_FAIL;
			}

			// 5も前提に...
			asDevice5();
			HRESULT hr = asDevice9();
			checkFeatureSupport();
			return hr;
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
			_pDevice9.Reset();
			_pDevice5.Reset();
			_pDevice.Reset();
		}

		HRESULT Device::asDevice()
		{
			// インターフェイスの取得と機能の有無は別物なので分けて扱う
			// (以前はレイトレ非対応GPUだと Device5 が取れていても失敗扱いになっていた)
			HRESULT hr = asDevice5();
			if (SUCCEEDED(hr))
			{
				hr = asDevice9();
			}
			checkFeatureSupport();
			return hr;
		}

		HRESULT Device::asDevice5()
		{
			// ID3D12Device5で使えるか試みる
			return _pDevice.As(&_pDevice5);
		}

		HRESULT Device::asDevice9()
		{
			// ID3D12Device9で使えるか試みる
			return _pDevice.As(&_pDevice9);
		}

		void Device::checkFeatureSupport()
		{
			_isRaytracingSupported     = false;
			_isRaytracingSupported_1_1 = false;
			_isMeshShaderSupported     = false;

			if (!_pDevice) return;

			// レイトレをサポートしているかチェック
			D3D12_FEATURE_DATA_D3D12_OPTIONS5 option5 = {};
			if (SUCCEEDED(_pDevice->CheckFeatureSupport(D3D12_FEATURE_D3D12_OPTIONS5, &option5, sizeof(option5))))
			{
				_isRaytracingSupported     = option5.RaytracingTier >= D3D12_RAYTRACING_TIER_1_0;
				_isRaytracingSupported_1_1 = option5.RaytracingTier >= D3D12_RAYTRACING_TIER_1_1;
			}

			// MeshShaderをサポートしているかチェック
			// OPTIONS9.MeshShaderPipelineStatsSupported は「統計クエリ対応」なので判定には使えない。正しくは OPTIONS7.MeshShaderTier
			D3D12_FEATURE_DATA_D3D12_OPTIONS7 option7 = {};
			if (SUCCEEDED(_pDevice->CheckFeatureSupport(D3D12_FEATURE_D3D12_OPTIONS7, &option7, sizeof(option7))))
			{
				_isMeshShaderSupported = option7.MeshShaderTier != D3D12_MESH_SHADER_TIER_NOT_SUPPORTED;
			}
		}
	}
}

/************************************
* End of file
************************************/
