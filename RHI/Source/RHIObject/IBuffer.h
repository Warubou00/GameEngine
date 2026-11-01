
/***********************************************************************************
* @file    IBuffer.h
* @brief   RHI interface buffer
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
#ifndef RHI_OBJECT_INTERFACE_BUFFER_H_
#define RHI_OBJECT_INTERFACE_BUFFER_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "IRHIObject.h"

#include <vector>

// 頂点などの型は RHI の公開型を使う (以前は Renderer のヘッダーを読んでいて、RHI が Renderer に依存していた)
#include "../Public/rhiTypes.h"

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

        class IBuffer : public IRHIObject
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            IBuffer() {};
            virtual ~IBuffer() = default;


        public:          /*---クラスメソッド---*/

            virtual RHI_RESULT Initialize(RHI::ObjectInterface::IDevice* iDevice, RHI::ObjectInterface::ICommandContext* iCommandContext) = 0;
            virtual RHI::VertexRange UploadVertexData(RHI::ObjectInterface::IDevice* iDevice, RHI::ObjectInterface::ICommandContext* iCommandContext, const RHI::Vertex* vertices, uint32_t count) = 0;
            virtual RHI::IndexRange  UploadIndexData(RHI::ObjectInterface::IDevice* iDevice, RHI::ObjectInterface::ICommandContext* iCommandContext, const uint32_t* indices, uint32_t count) = 0;
            // カメラの行列 (DirectXMath の普通の行列。転置は中で行う)
            virtual void SetViewProjection(const ::DirectX::XMFLOAT4X4& view, const ::DirectX::XMFLOAT4X4& projection) = 0;
        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/


        };
    }
}


#endif // RHI_OBJECT_INTERFACE_BUFFER_H_

/************************************
* End of file
************************************/
