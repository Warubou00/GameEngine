
/***********************************************************************************
* @file    constant.h
* @brief   Renderer ccnstant
* @details
*
* @author  Keigo Onari
* @date    2026/09/28
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note
************************************************************************************/

#pragma once
#ifndef RENDERER_CONSTANT_BUFFER_DATA_H_
#define RENDERER_CONSTANT_BUFFER_DATA_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <stdint.h>
#include <DirectXMath.h>
#include "common.h"

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

    struct RENDERER_API_CLASS ConstantPerRenderObject
    {
        DirectX::XMMATRIX WorldMatrix;
        uint32_t          TextureID;
    };

}


#endif // RENDERER_CONSTANT_BUFFER_H_

/************************************
* End of file
************************************/
