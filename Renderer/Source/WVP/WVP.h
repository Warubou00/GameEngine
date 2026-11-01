
/***********************************************************************************
* @file    WVP.h
* @brief   Renderer world view projection
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
#ifndef RENDERER_WORLD_VIEW_PROJECTION_H_
#define RENDERER_WORLD_VIEW_PROJECTION_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <DirectXMath.h>

#include "../common.h"
#include "../../../Core/Source/Math/Vector/vector.h"

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
	class WVP
	{
	public:
		WVP() {}
		virtual ~WVP() = default;
	};
}


#endif // RENDERER_WORLD_VIEW_PROJECTION_H_

/************************************
* End of file
************************************/
