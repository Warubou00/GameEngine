
/***********************************************************************************
* @file    ITexture.h
* @brief   RHI interface texture
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
#ifndef RHI_OBJECT_INTERFACE_TEXTURE_H_
#define RHI_OBJECT_INTERFACE_TEXTURE_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "IRHIObject.h"
#include "TextureHandle.h"

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
        class IDevice;
        class ICommandContext;
        class IBuffer;

        class ITexture : public IRHIObject
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            ITexture() {};
            virtual ~ITexture() = default;


        public:          /*---クラスメソッド---*/

            virtual TextureHandle LoadTextureFromFile(
                RHI::ObjectInterface::IDevice* iDevice,
                RHI::ObjectInterface::ICommandContext* iCommandContext,
                RHI::ObjectInterface::IBuffer* iBuffer,
                const wchar_t* filePath) = 0;

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/


        };
    }
}


#endif // RHI_OBJECT_INTERFACE_TEXTURE_H_

/************************************
* End of file
************************************/
