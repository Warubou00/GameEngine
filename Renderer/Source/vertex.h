
/***********************************************************************************
* @file    vertex.h
* @brief   Renderer vertex
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
#ifndef RENDERER_VERTEX_H_
#define RENDERER_VERTEX_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <cstddef>      // offsetof
#include "common.h"
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

    // ----------------------------------------------------------
    // 頂点データ
    // RHI/Source/Shader/hlslTemplate.shader の struct Vertex と同じ並び・同じサイズ(56byte)にすること
    // (メガ頂点バッファを StructuredBuffer として読むため、1byteでもずれると全頂点が崩れる)
    // ----------------------------------------------------------
    struct RENDERER_API_CLASS Vertex
    {
        Core::Vector3 Position = Core::Vector3(0.0f);   // offset  0
        float padding1         = 0.0f;                  // offset 12
        Core::Vector3 Normal   = Core::Vector3(0.0f);   // offset 16
        float padding2         = 0.0f;                  // offset 28
        // 以前は既定値が 0 (黒) で、ローダーも設定しないため「頂点カラー × テクスチャ」が常に黒になっていた (1-7)
        Core::Vector4 Color    = Core::Vector4(1.0f);   // offset 32 : 既定は白
        Core::Vector2 UV       = Core::Vector2(0.0f);   // offset 48
    };

    // シェーダー側とのサイズ・並びの食い違いをコンパイル時に検出する
    static_assert(sizeof(Vertex) == 56, "Renderer::Vertex must be 56 bytes (same as HLSL struct Vertex)");
    static_assert(offsetof(Vertex, Normal) == 16, "Vertex::Normal offset must be 16");
    static_assert(offsetof(Vertex, Color)  == 32, "Vertex::Color offset must be 32");
    static_assert(offsetof(Vertex, UV)     == 48, "Vertex::UV offset must be 48");

}


#endif // RENDERER_VERTEX_H_

/************************************
* End of file
************************************/
