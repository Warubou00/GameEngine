
/***********************************************************************************
* @file    IObject.h
* @brief   RHI IObject
* @details 
*
* @author  Keigo Onari
* @date    2026/4/27
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    
************************************************************************************/

#pragma once
#ifndef RHI_INTERFACE_OBJECT_H_
#define RHI_INTERFACE_OBJECT_H_

/******************************************************************
* Include Headers
******************************************************************/


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

        class IObject
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            IObject();
            ~IObject();


        public:          /*---クラスメソッド---*/

            virtual bool Initialize() = 0;
            virtual bool Initialize(Device* device);
            virtual bool Initialize(CommandContext* commandContext);
            virtual bool Initialize(Device* device, CommandContext* commandContext);
            virtual bool Initialize(Buffer* buffer);
            virtual bool Initialize(Texture* texture);
            virtual bool Initialize(Device* device = nullptr, CommandContext* commandContext = nullptr, Buffer* buffer = nullptr, Texture* texture = nullptr);
        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/


        };
    }
}


#endif // RHI_INTERFACE_OBJECT_H_

/************************************
* End of file
************************************/
