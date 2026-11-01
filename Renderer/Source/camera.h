
/***********************************************************************************
* @file    camera.h
* @brief   Renderer camera
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
#ifndef RENDERER_CANERA_H_
#define RENDERER_CANERA_H_

/******************************************************************
* Include Headers
******************************************************************/
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

    struct RENDERER_API_CLASS Camera
    {
        Core::Vector3 Position;
        Core::Vector3 Target;
        Core::Vector3 Up;
    };

}


#endif // RENDERER_CAMERA_H_

/************************************
* End of file
************************************/
