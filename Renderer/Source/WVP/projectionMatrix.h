
/***********************************************************************************
* @file    projectionMatrix.h
* @brief   Renderer projection matrix
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
#ifndef RENDERER_PROJECTION_MATRIX_H_
#define RENDERER_PROJECTION_MATRIX_H_

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

    class RENDERER_API_CLASS Projection : public WVP
    {
    public:
        enum class ProjectionType
        {
            PROJECTION_TYPE_2D,
            PROJECTION_TYPE_3D,
        };

    public:
        Projection() {}
        ~Projection()override {}

    public:
        DirectX::XMMATRIX GetProjectionMatrix(
            float width,
            float height,
            float nearZ,
            float farZ,
            ProjectionType type
        )
        {
            // 結果用変数宣言
            DirectX::XMMATRIX result;

            if (type == ProjectionType::PROJECTION_TYPE_2D)
                result = DirectX::XMMatrixOrthographicLH(width, height, nearZ, farZ);
            else
                result = DirectX::XMMatrixPerspectiveFovLH(1.0f, width / height, nearZ, farZ);
                //result = DirectX::XMMatrixPerspectiveFovLH(DirectX::XM_PIDIV4, width / height, nearZ, farZ);

            // 転置はしない (RHI に渡すと RHI の中でシェーダー用に転置される)
            // 以前はここで転置していたが、行列をシェーダーへ渡す時の都合は RHI の中の話なので RHI に任せた

            // 結果を返却
            return result;
        }
    };

}


#endif // RENDERER_PROJECTION_H_

/************************************
* End of file
************************************/
