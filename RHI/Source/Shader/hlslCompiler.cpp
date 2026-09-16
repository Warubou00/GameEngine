
/***********************************************************************************
* @file    hlslCompiler.cpp
* @brief   hlslのコンパイラー
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
#include "hlslCompiler.h"

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
		HLSLCompiler::HLSLCompiler() = default;
		HLSLCompiler::~HLSLCompiler() = default;

		void HLSLCompiler::CompileShader(
			const std::wstring& fileName,
			const std::string& entryPoint,
			const std::string& target,
			ID3DBlob** outBlob,
			ID3DBlob** errorBlob
		)
		{
			// シェーダコンパイル
			UINT flags = 0;
#ifdef _DEBUG
			flags = D3DCOMPILE_DEBUG | D3DCOMPILE_SKIP_OPTIMIZATION;
#else
			flags = D3DCOMPILE_OPTIMIZATION_LEVEL3;
#endif
			(void)D3DCompileFromFile(fileName.c_str(), nullptr, D3D_COMPILE_STANDARD_FILE_INCLUDE, entryPoint.c_str(), target.c_str(), flags /*D3DCOMPILE_ENABLE_STRICTNESS*/, 0, outBlob, errorBlob);

			return;
		}
	}
}

/************************************
* End of file
************************************/
