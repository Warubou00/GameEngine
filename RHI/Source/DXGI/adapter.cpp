
/***********************************************************************************
* @file    adapter.cpp
* @brief   Graphicsボードのアダプタークラス
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
#include "adapter.h"

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
	namespace DirectX
	{
		namespace
		{
			bool isSoftwareAdapter(IDXGIAdapter1* adapter)
			{
				DXGI_ADAPTER_DESC1 desc = {};
				if (FAILED(adapter->GetDesc1(&desc))) return true;
				return (desc.Flags & DXGI_ADAPTER_FLAG_SOFTWARE) != 0;
			}
		}

		Adapter::Adapter() = default;
		Adapter::~Adapter() = default;


		HRESULT Adapter::EnumerateAdaptersFromGpuName(IDXGIFactory6* pFactory, const std::wstring& enumeratingGPU)
		{
			if (!pFactory)return E_INVALIDARG;

			Release();

			// アダプターの列挙
			// 以前は ComPtr の operator& で毎回中身を解放しつつ生ポインタを vector に溜め、
			// 最後に vector 側でも Release していたため二重解放になっていた
			Microsoft::WRL::ComPtr<IDXGIAdapter1> adpt;
			for (UINT i = 0; pFactory->EnumAdapters1(i, adpt.ReleaseAndGetAddressOf()) != DXGI_ERROR_NOT_FOUND; ++i)
			{
				if (isSoftwareAdapter(adpt.Get())) continue;

				// アダプターに引数の名前があるか走査
				DXGI_ADAPTER_DESC1 adesc = {};
				adpt->GetDesc1(&adesc);
				std::wstring strDesc = adesc.Description;
				if (strDesc.find(enumeratingGPU) != std::wstring::npos)
				{
					_pAdapter = adpt;
					return S_OK;
				}
			}

			return DXGI_ERROR_NOT_FOUND;
		}

		HRESULT Adapter::EnumerateAdapterFromHighPerformance(IDXGIFactory6* pFactory)
		{
			if (!pFactory)return E_INVALIDARG;

			Release();

			Microsoft::WRL::ComPtr<IDXGIAdapter1> adpt;
			for (UINT i = 0;
				pFactory->EnumAdapterByGpuPreference(
					i,
					DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE,		// 性能のいいやつ順
					IID_PPV_ARGS(adpt.ReleaseAndGetAddressOf())) != DXGI_ERROR_NOT_FOUND;
				++i)
			{
				if (isSoftwareAdapter(adpt.Get())) continue;

				_pAdapter = adpt;
				return S_OK;
			}

			return DXGI_ERROR_NOT_FOUND;
		}

		HRESULT Adapter::TryVersionUp()
		{
			if (!_pAdapter)
			{
				return E_FAIL;
			}
			return asAdapter();
		}

		HRESULT Adapter::TryVersionUp1()
		{
			if (!_pAdapter)
			{
				return E_FAIL;
			}
			return asAdapter1();
		}

		HRESULT Adapter::TryVersionUp3()
		{
			if (!_pAdapter)
			{
				return E_FAIL;
			}

			// 1も前提に...
			asAdapter1();
			return asAdapter3();
		}

		IDXGIAdapter* Adapter::GetAdapter(void)const
		{
			return _pAdapter.Get();
		}

		IDXGIAdapter1* Adapter::GetAdapter1(void)const
		{
			return _pAdapter1.Get();
		}

		IDXGIAdapter3* Adapter::GetAdapter3(void)const
		{
			return _pAdapter3.Get();
		}

		std::wstring Adapter::GetAdapterName(void)const
		{
			if (!_pAdapter)
			{
				return std::wstring();
			}

			DXGI_ADAPTER_DESC adDesc = {};
			if (FAILED(_pAdapter->GetDesc(&adDesc)))
			{
				return std::wstring();
			}
			return std::wstring(adDesc.Description);
		}

		void Adapter::Release(void)
		{
			_pAdapter3.Reset();
			_pAdapter1.Reset();
			_pAdapter.Reset();
		}

		HRESULT Adapter::asAdapter()
		{
			// 1が取れなければ3も取れない
			HRESULT hr = asAdapter1();
			if (FAILED(hr)) return hr;
			return asAdapter3();
		}

		HRESULT Adapter::asAdapter1()
		{
			return _pAdapter.As(&_pAdapter1);
		}

		HRESULT Adapter::asAdapter3()
		{
			return _pAdapter.As(&_pAdapter3);
		}
	}
}

/************************************
* End of file
************************************/
