
/***********************************************************************************
* @file    IFence.h
* @brief   フェンスクラス
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
#ifndef API_INTERFACE_FENCE_H_
#define API_INTERFACE_FENCE_H_

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
        class ISwapChain;

        class IFence
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            IFence() {};
            virtual ~IFence() = default;


        public:          /*---クラスメソッド---*/
            virtual RHI_API_RESULT Initialize(IDevice* iDevice, ISwapChain* iSwapChain) = 0;
            virtual void WaitForSignal(ICommandQueue* iCommandQueue) = 0;

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/


        };
    }
}


#endif // API_INTERFACE_FENCE_H_

/************************************
* End of file
************************************/
