
/***********************************************************************************
* @file    dxgiFactory6.cpp
* @brief   DXGI6ƒNƒ‰ƒX
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
	namespace DirectX
	{
		DXGIFactory6::DXGIFactory6() = default;
		DXGIFactory6::~DXGIFactory6() = default;

		HRESULT DXGIFactory6::Create(void)
		{
			if (FAILED(CreateDXGIFactory2(DXGI_CREATE_FACTORY_DEBUG, IID_PPV_ARGS(&_pDxgi6Factory)))) {
				if (FAILED(CreateDXGIFactory2(0, IID_PPV_ARGS(&_pDxgi6Factory)))) {
					return S_FALSE;
				}
			}
		}

		IDXGIFactory6* DXGIFactory6::Get(void)const
		{
			return _pDxgi6Factory.Get();
		}

		IDXGIFactory6* const* DXGIFactory6::GetPointer(void)const
		{
			return _pDxgi6Factory.GetAddressOf();
		}

		void DXGIFactory6::Release(void)
		{
			// ComPtr‚Ì‚½‚ß”jŠü‚Í”ð‚¯‚é
			//if (_pDxgi6Factory.Get())_pDxgi6Factory.Get()->Release();
		}
	}
}

/************************************
* End of file
************************************/
