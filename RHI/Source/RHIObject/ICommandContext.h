
/***********************************************************************************
* @file    ICommandContext.h
* @brief   RHI interface command context
* @details 
*
* @author  Keigo Onari
* @date    2026/09/21
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    
************************************************************************************/

#pragma once
#ifndef RHI_OBJECT_INTERFACE_COMMAND_CONTEXT_H_
#define RHI_OBJECT_INTERFACE_COMMAND_CONTEXT_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "IRHIObject.h"

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
    namespace ObjectInterface
    {
        class ICommandContext : public IRHIObject
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            ICommandContext() {};
            virtual ~ICommandContext() = default;


        public:          /*---クラスメソッド---*/


        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/


        };
    }
}


#endif // RHI_OBJECT_INTERFACE_COMMAND_CONTEXT_H_

/************************************
* End of file
************************************/
