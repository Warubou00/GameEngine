
/***********************************************************************************
* @file    Buffer.h
* @brief   RHI Buffer
* @details 
*
* @author  Keigo Onari
* @date    2026/1/13
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    
************************************************************************************/

#pragma once
#ifndef RHI_BUFFER_H_
#define RHI_BUFFER_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <memory>
#include "IObject.h"

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

        class Buffer : IObject
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            Buffer();
            ~Buffer();


        public:          /*---クラスメソッド---*/

            RHI_RESULT Initialize()override;
            RHI_RESULT Initialize(Device* device, CommandContext* commandContext)override;

            bool CreateVertexBuffer(Device* device);

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/

            bool makeRtv(Device* device, CommandContext* commandContext);
        };
    }
}


#endif // RHI_BUFFER_H_

/************************************
* End of file
************************************/
