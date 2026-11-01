
/***********************************************************************************
* @file    hlslCompiledState.cpp
* @brief   hlslのコンパイル後のステータス
* @details 
*
* @author  Keigo Onari
* @date    2026/09/16
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
#include "hlslCompiledState.h"

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
	namespace Shader
	{
		HLSLCompiledState::HLSLCompiledState() = default;
		HLSLCompiledState::~HLSLCompiledState() = default;

		void HLSLCompiledState::SetVSBlob(ID3DBlob* blob)
		{
			_vsBlob = blob;
		}

		void HLSLCompiledState::SetPSBlob(ID3DBlob* blob)
		{
			_psBlob = blob;
		}

		void HLSLCompiledState::SetGSBlob(ID3DBlob* blob)
		{
			_gsBlob = blob;
		}

		void HLSLCompiledState::SetCSBlob(ID3DBlob* blob)
		{
			_csBlob = blob;
		}

		void HLSLCompiledState::SetErrorBlob(ID3DBlob* blob)
		{
			_errorBlob = blob;
		}

		ID3DBlob* HLSLCompiledState::GetVSBlob()const
		{
			return _vsBlob.Get();
		}

		ID3DBlob* HLSLCompiledState::GetPSBlob()const
		{
			return _psBlob.Get();
		}

		ID3DBlob* HLSLCompiledState::GetGSBlob()const
		{
			return _gsBlob.Get();
		}

		ID3DBlob* HLSLCompiledState::GetCSBlob()const
		{
			return _csBlob.Get();
		}

		ID3DBlob* HLSLCompiledState::GetErrorBlob()const
		{
			return _errorBlob.Get();
		}
	}
}

/************************************
* End of file
************************************/
