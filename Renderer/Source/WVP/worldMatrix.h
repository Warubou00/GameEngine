
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
            // 結果用変数宣言
            DirectX::XMMATRIX result;

            // 変換
            DirectX::XMMATRIX matScale  = DirectX::XMMatrixScalingFromVector(scale);
            DirectX::XMMATRIX matRotate = DirectX::XMMatrixRotationRollPitchYaw(rotation._x, rotation._y, rotation._z);
            DirectX::XMMATRIX matPos    = DirectX::XMMatrixTranslationFromVector(position);

            // 計算
            result = matScale * matRotate * matPos;

            // 転置はしない (RHI に渡すと RHI の中でシェーダー用に転置される)
            // 以前はここで転置していたが、行列をシェーダーへ渡す時の都合は RHI の中の話なので RHI に任せた

            // 結果を返す
            return result;
        }

        DirectX::XMMATRIX GetZeroPosMatrix()
        {
            return DirectX::XMMatrixIdentity();
        }
    };

}


#endif // RENDERER_VERTEX_H_

/************************************
* End of file
************************************/
