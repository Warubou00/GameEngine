
/***********************************************************************************
* @file    viewMatrix.h
* @brief   Renderer view matrix
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
#ifndef RENDERER_VIEW_MATRIX_H_
#define RENDERER_VIEW_MATRIX_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "WVP.h"

#include "../camera.h"

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

    class RENDERER_API_CLASS View : public WVP
    {
    public:
        View() {}
        ~View()override {}

    public:
        DirectX::XMMATRIX GetViewMatrix(
            const Camera& camera
        )
        {
            // 結果用変数宣言
            DirectX::XMMATRIX result;

            // 値をセット
            DirectX::XMVECTOR pos    = DirectX::XMVectorSet(camera.Position._x, camera.Position._y, camera.Position._z, 1.0f);
            DirectX::XMVECTOR target = DirectX::XMVectorSet(camera.Target._x,   camera.Target._y,   camera.Target._z,   1.0f);
            DirectX::XMVECTOR up     = DirectX::XMVectorSet(camera.Up._x,       camera.Up._y,       camera.Up._z,       0.0f);

            // マトリクス生成
            result = DirectX::XMMatrixLookAtLH(pos, target, up);

            // 転置はしない (RHI に渡すと RHI の中でシェーダー用に転置される)
            // 以前はここで転置していたが、行列をシェーダーへ渡す時の都合は RHI の中の話なので RHI に任せた

            // 結果を返却
            return result;
        }
    };

}


#endif // RENDERER_VERTEX_H_

/************************************
* End of file
************************************/
