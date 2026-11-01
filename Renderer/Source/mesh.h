
/***********************************************************************************
* @file    mesh.h
* @brief   Renderer mesh
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
#ifndef RENDERER_MESH_H_
#define RENDERER_MESH_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "common.h"
#include <stdint.h>
#include <vector>
#include <memory>

#include "vertex.h"
#include "material.h"

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

    struct RENDERER_API_CLASS Mesh
    {
        std::vector<Vertex>                    Vertices;        // 頂点
        std::vector<std::vector<uint32_t>>     Indecies;        // インデックスデータ

        std::vector<Material*> Materials;                       // マテリアル
    };

}


#endif // RENDERER_MESH_H_

/************************************
* End of file
************************************/
