
/***********************************************************************************
* @file    hlslCompiler.h
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

#pragma once
#ifndef HLSL_COMPILER_H_
#define HLSL_COMPILER_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "hlslHeader.h"

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
        class HLSLCompiler
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            HLSLCompiler();
            ~HLSLCompiler();


        public:          /*---クラスメソッド---*/

            HRESULT CompileShader(
                const std::wstring& fileName,
                const std::string& entryPoint,
                const std::string& target,
                ID3DBlob** outBlob,
                ID3DBlob** errorBlob
            );


        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/


        };
    }
}


#endif // HLSL_COMPILER_H_

/************************************
* End of file
************************************/
