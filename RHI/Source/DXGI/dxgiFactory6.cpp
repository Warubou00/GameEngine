
/***********************************************************************************
* @file    dxgiFactory6.cpp
* @brief   DXGI6クラス
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
#include "dxgiFactory6.h"

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
	namespace DXGI
	{
		DXGIFactory6::DXGIFactory6() = default;
		DXGIFactory6::~DXGIFactory6() = default;

		HRESULT DXGIFactory6::Create(void)
		{
			if (_pDxgi6Factory)
			{
				return S_OK;
			}

#if defined(_DEBUG)
			// デバッグ版はDXGIのデバッグ出力を有効にする (Graphics Toolsが無い環境では失敗するので通常版に落とす)
			if (SUCCEEDED(CreateDXGIFactory2(DXGI_CREATE_FACTORY_DEBUG, IID_PPV_ARGS(&_pDxgi6Factory))))
			{
				return S_OK;
			}
#endif
			// 失敗時は S_FALSE(=成功扱い) ではなく実際のエラーコードを返す
			return CreateDXGIFactory2(0, IID_PPV_ARGS(&_pDxgi6Factory));
		}

		IDXGIFactory6* DXGIFactory6::Get(void)const
		{
			return _pDxgi6Factory.Get();
		}

		IDXGIFactory6* const* DXGIFactory6::GetPointer(void)const
		{
			return _pDxgi6Factory.GetAddressOf();
		}

		bool DXGIFactory6::IsTearingSupported(void)const
		{
			if (!_pDxgi6Factory) return false;

			// IDXGIFactory5 以降の CheckFeatureSupport で確認する (古いOS/ドライバーでは FALSE)
			BOOL allowTearing = FALSE;
			if (FAILED(_pDxgi6Factory->CheckFeatureSupport(DXGI_FEATURE_PRESENT_ALLOW_TEARING, &allowTearing, sizeof(allowTearing))))
			{
				return false;
			}
			return allowTearing == TRUE;
		}

		void DXGIFactory6::Release(void)
		{
			_pDxgi6Factory.Reset();
		}
	}
}

/************************************
* End of file
************************************/
