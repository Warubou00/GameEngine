
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
		Adapter::Adapter() = default;
		Adapter::~Adapter() = default;


		HRESULT Adapter::EnumerateAdaptersFromGpuName(IDXGIFactory6* pFactory, const std::wstring& enumeratingGPU)
		{
			if (!pFactory)return E_INVALIDARG;

			Release();

			// アダプターの列挙
			for (int i = 0; pFactory->EnumAdapters(i, &_pAdapter) != DXGI_ERROR_NOT_FOUND; ++i) {
				_pAdapters.push_back(_pAdapter.Get());
			}

			// アダプターに引数の名前があるか走査
			for (auto adpt : _pAdapters) {
				DXGI_ADAPTER_DESC adesc = {};
				adpt->GetDesc(&adesc);
				std::wstring strDesc = adesc.Description;
				if (strDesc.find(enumeratingGPU) != std::string::npos) {
					_pAdapter = adpt;
					break;
				}
			}

			return S_OK;
		}

		HRESULT Adapter::EnumerateAdapterFromHighPerformance(IDXGIFactory6* pFactory)
		{
			return pFactory->EnumAdapterByGpuPreference(
				0,											// 0番目
				DXGI_GPU_PREFERENCE_HIGH_PERFORMANCE,		// 性能のいいやつ
				IID_PPV_ARGS(&_pAdapter)
			);
		}

		HRESULT Adapter::TryVersionUp()
		{
			if (!_pAdapter)
			{
				// TODO : 例外処理 -> nullptr

				return E_NOTIMPL;
			}
			return asAdapter();
		}

		HRESULT Adapter::TryVersionUp1()
		{
			if (!_pAdapter)
			{
				// TODO : 例外処理 -> nullptr

				return E_NOTIMPL;
			}
			return asAdapter1();
		}

		HRESULT Adapter::TryVersionUp3()
		{
			if (!_pAdapter)
			{
				// TODO : 例外処理 -> nullptr

				return E_NOTIMPL;
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

		void Adapter::Release(void)
		{
			// ComPtrのため明示的な解放は避ける
			//if (_pAdapter.Get())_pAdapter.Get()->Release();

			if (_pAdapters.size() > 0)
			{
				for (auto adpt : _pAdapters)
				{
					adpt->Release();
				}

				_pAdapters.clear();
			}
		}

		HRESULT Adapter::asAdapter()
		{
			// TODO : 例外処理

			HRESULT hr = S_OK;

			hr = asAdapter1();
			hr = asAdapter3();

			return hr;
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
