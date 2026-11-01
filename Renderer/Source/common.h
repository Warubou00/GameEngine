
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
// Renderer プロジェクトでは RENDERER_EXPORTS を定義してビルドする (dllexport)。使う側では dllimport になる (3-8)
#ifdef RENDERER_EXPORTS
#define RENDERER_API_CLASS __declspec(dllexport)
#else
#define RENDERER_API_CLASS __declspec(dllimport)
#endif
#define RENDERER_API extern "C" RENDERER_API_CLASS

#define RENDERER_RESULT int
#define RENDERER_SUCCEEDED	            (1)	 // 成功
#define RENDERER_FAILED		            (-1) // 汎用的な失敗
//#define CORE_FAILED_INITIALIZED      (-2) // 失敗_初期化
//#define CORE_FAILED_INVALID_ARGUMENT (-3) // 失敗_不適な引数

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
