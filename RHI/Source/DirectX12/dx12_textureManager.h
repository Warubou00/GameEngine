
/***********************************************************************************
* @file    dx12_textureManager.h
* @brief   DX12のテクスチャ括管理
* @details 
*
* @author  Keigo Onari
* @date    2026/04/24
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    
************************************************************************************/

#pragma once
#ifndef DX12_TEXTURE_MANAGER_H_
#define DX12_TEXTURE_MANAGER_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "dx12_header.h"
#include <vector>
#include <string>
#include "../TextureHandle.h"

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
        class TextureManager
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            TextureManager() {};
            ~TextureManager() {};

        public:          /*---クラスメソッド---*/
            /***********************************************************************************
             * @brief 生成関数
             * @details 
             *
             * @param[in] IDXGIFactory6* pDxgiFactory DXGIFactory6のポインタ
             * @param[in] ID3D12CommandQueue* pCmdQueue コマンドキュー
             * @param[in] HWND* hWnd ウインドウハンドルのポインタ
             * @param[in] UINT width 幅
             * @param[in] UINT height 高さ
             * @return HRESULT 成功の有無
             ************************************************************************************/
            HRESULT Create(ID3D12Device* device);

            TextureHandle ReadTexture(const std::wstring& fileName);

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/


        };
    }
}


#endif // DX12_TEXTURE_MANAGER_H_

/************************************
* End of file
************************************/
