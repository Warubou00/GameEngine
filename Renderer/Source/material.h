
/***********************************************************************************
* @file    material.h
* @brief   Renderer material
* @details
*
* @author  Keigo Onari
* @date    2026/09/23
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note
************************************************************************************/

#pragma once
#ifndef RENDERER_MATERIAL_H_
#define RENDERER_MATERIAL_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "common.h"
#include <vector>
#include <string>

#include "../../Core/Source/Math/Vector/vector.h"

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

    struct RENDERER_API_CLASS Material
    {
        Core::Vector4 Ambient;          // 環境光
        Core::Vector4 Diffuse;          // 面の色
        Core::Vector4 Specular;         // 反射光
        std::wstring TexturePath;       // テクスチャのパス
        bool UseTexture;                // テクスチャの使用の有無
        uint32_t TextureIndex;          // テクスチャインデックス(SRVIndex)
    };

}


#endif // RENDERER_MATERIAL_H_

/************************************
* End of file
************************************/
