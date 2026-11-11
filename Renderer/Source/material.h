
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
        bool DoubleSided;               // 裏面も描く (PMX の材質のフラグ)
        bool Transparent;               // 半透明として描く (不透明な物の後に、ブレンドして描く。PMX は全部)
        uint32_t TextureIndex;          // テクスチャインデックス(SRVIndex)
        std::wstring Name;              // v1.5 : 材質の名前 (OBJ の newmtl / PMX の材質名。マテリアルのアセットの名前に使う)
    };

}


#endif // RENDERER_MATERIAL_H_

/************************************
* End of file
************************************/
