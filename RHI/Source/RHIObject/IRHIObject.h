
/***********************************************************************************
* @file    IRHIObject.h
* @brief   RHIObjectのインターフェイス
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
#ifndef RHI_OBJECT_INTERFACE_H_
#define RHI_OBJECT_INTERFACE_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <cstdint>
#include <memory>
#include "../common.h"

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
        class Device;
        class CommandContext;
        class Buffer;
        class Texture;

        struct ObjectInfo
        {
            /******************************
            * SwapChain
            ******************************/
            void*    HWnd;
            uint32_t Width;
            uint32_t Height;
            uint32_t EnabledVSync;
        };

        class IRHIObject
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            IRHIObject() {};
            virtual ~IRHIObject() = default;


        public:          /*---クラスメソッド---*/


        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/


        };
    }
}


#endif // RHI_OBJECT_INTERFACE_H_

/************************************
* End of file
************************************/
