
/***********************************************************************************
* @file    ICommandQueue.h
* @brief   コマンドキュークラス
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
#ifndef API_INTERFACE_COMMAND_QUEUE_H_
#define API_INTERFACE_COMMAND_QUEUE_H_

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
        class ICommandList;
        
        class ICommandQueue
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            ICommandQueue() {};
            virtual ~ICommandQueue() = default;


        public:          /*---クラスメソッド---*/

            virtual RHI_API_RESULT Initialize(RHI::APIInterface::IDevice* iDevice) = 0;

            virtual void End(RHI::APIInterface::ICommandList* iCmdList) = 0;

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/


        };
    }
}


#endif // API_INTERFACE_COMMAND_QUEUE_H_

/************************************
* End of file
************************************/
