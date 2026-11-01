
/***********************************************************************************
* @file    common.h
* @brief   Renderer common
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
#ifndef RENDERER_COMMON_H_
#define RENDERER_COMMON_H_

/******************************************************************
* Include Headers
******************************************************************/

/******************************************************************
* Macro Definitions
******************************************************************/
#define RENDERER_API_CLASS __declspec(dllexport)
#define RENDERER_API extern "C" RENDERER_API_CLASS

#define RENDERER_RESULT int
#define RENDERER_SUCCEEDED	            (1)	 // ê¨å˜
#define RENDERER_FAILED		            (-1) // îƒópìIÇ»é∏îs
//#define CORE_FAILED_INITIALIZED      (-2) // é∏îs_èâä˙âª
//#define CORE_FAILED_INVALID_ARGUMENT (-3) // é∏îs_ïsìKÇ»à¯êî

/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/



namespace Renderer
{

}


#endif // RENDERER_COMMON_H_

/************************************
* End of file
************************************/
