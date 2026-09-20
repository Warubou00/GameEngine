
/***********************************************************************************
* @file    hlslCompiledState.h
* @brief   hlslのコンパイル後のステータス
* @details
*
* @author  Keigo Onari
* @date    2026/09/19
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note
************************************************************************************/

#pragma once
#ifndef HLSL_COMPILED_STATE_H_
#define HLSL_COMPILED_STATE_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "IShaderCompiledState.h"
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
        class HLSLCompiledState : IShaderCompiledState
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            HLSLCompiledState();
            ~HLSLCompiledState();

            void SetVSBlob(ID3DBlob* blob);
            void SetPSBlob(ID3DBlob* blob);
            void SetGSBlob(ID3DBlob* blob);
            void SetCSBlob(ID3DBlob* blob);
            void SetErrorBlob(ID3DBlob* blob);

            ID3DBlob* GetVSBlob()const;
            ID3DBlob* GetPSBlob()const;
            ID3DBlob* GetGSBlob()const;
            ID3DBlob* GetCSBlob()const;
            ID3DBlob* GetErrorBlob()const;


        public:          /*---クラスメソッド---*/


        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/
            Microsoft::WRL::ComPtr<ID3DBlob> _vsBlob;
            Microsoft::WRL::ComPtr<ID3DBlob> _psBlob;
            Microsoft::WRL::ComPtr<ID3DBlob> _gsBlob;
            Microsoft::WRL::ComPtr<ID3DBlob> _csBlob;
            Microsoft::WRL::ComPtr<ID3DBlob> _errorBlob;
            

        };
    }
}


#endif // HLSL_COMPILED_STATE_H_

/************************************
* End of file
************************************/
