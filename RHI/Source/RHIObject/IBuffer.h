
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

#include "../../Renderer/Source/vertex.h"
#include "../../Renderer/Source/camera.h"
#include "../../Renderer/Source/WVP/viewMatrix.h"
#include "../../Renderer/Source/WVP/projectionMatrix.h"

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
            virtual bool UploadVertexData(RHI::ObjectInterface::IDevice* iDevice, RHI::ObjectInterface::ICommandContext* iCommandContext, const std::vector<Renderer::Vertex>& vertices) = 0;
            virtual bool UploadIndexData(RHI::ObjectInterface::IDevice* iDevice, RHI::ObjectInterface::ICommandContext* iCommandContext, const std::vector<uint32_t>& indecies) = 0;
            virtual void UpdateViewProjection(const Renderer::Camera& camera, Renderer::Projection::ProjectionType type) = 0;
        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/


        };
    }
}


#endif // RHI_OBJECT_INTERFACE_BUFFER_H_

/************************************
* End of file
************************************/
