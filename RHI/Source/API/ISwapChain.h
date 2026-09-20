
/***********************************************************************************
* @file    IResource.h
* @brief   リソースクラス
* @details
*
* @author  Keigo Onari
* @date    2026/09/20
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note
************************************************************************************/

#pragma once
#ifndef API_INTERFACE_SWAP_CHAIN_H_
#define API_INTERFACE_SWAP_CHAIN_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "IAPI.h"

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
    namespace APIInterface
    {
        class IDevice;
        class ICommandQueue;

        class ISwapChain
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            ISwapChain() {};
            virtual ~ISwapChain() = default;


        public:          /*---クラスメソッド---*/

            virtual RHI_API_RESULT Initialize(IDevice* iDevice, ICommandQueue* iCommandQueue, HWND* hWnd, UINT width, UINT height) = 0;

            virtual void Present(void) = 0;

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/


        };
    }
}


#endif // API_INTERFACE_SWAP_CHAIN_H_

/************************************
* End of file
************************************/
