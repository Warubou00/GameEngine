
/***********************************************************************************
* @file    worldMatrix.h
* @brief   Renderer world matrix
* @details
*
* @author  Keigo Onari
* @date    2026/09/26
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note
************************************************************************************/

#pragma once
#ifndef RENDERER_WORLD_MATRIX_H_
#define RENDERER_WORLD_MATRIX_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "WVP.h"

/******************************************************************
* Macro Definitions
******************************************************************/


/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/



namespace Renderer
{

    class RENDERER_API_CLASS WorldMatrix : public WVP
    {
    public:
        WorldMatrix() {}
        ~WorldMatrix()override {}

    public:
        DirectX::XMMATRIX GetWorldMatrix(
            const Core::Vector3& position,
            const Core::Vector3& rotation,
            const Core::Vector3& scale
        )
        {
            // åãâ ópïœêîêÈåæ
            DirectX::XMMATRIX result;

            // ïœä∑
            DirectX::XMMATRIX matScale  = DirectX::XMMatrixScalingFromVector(scale);
            DirectX::XMMATRIX matRotate = DirectX::XMMatrixRotationQuaternion(rotation);
            DirectX::XMMATRIX matPos    = DirectX::XMMatrixTranslationFromVector(position);

            // åvéZ
            result = matScale * matRotate * matPos;

            result = DirectX::XMMatrixTranspose(result);

            // åãâ Çï‘Ç∑
            return result;
        }

        DirectX::XMMATRIX GetZeroPosMatrix()
        {
            return DirectX::XMMatrixTranspose(DirectX::XMMatrixIdentity());
        }
    };

}


#endif // RENDERER_VERTEX_H_

/************************************
* End of file
************************************/
