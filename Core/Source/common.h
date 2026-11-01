
/***********************************************************************************
* @file    common.h
* @brief  汎用ヘッダー
* @details
*
* @author  Keigo Onari
* @date    2026/02/02
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    その他
************************************************************************************/

#pragma once
#ifndef CORE_COMMON_H_
#define CORE_COMMON_H_

/******************************************************************
* Include Headers
******************************************************************/


/******************************************************************
* Macro Definitions
******************************************************************/
#define CORE_API_CLASS __declspec(dllexport)
#define CORE_API extern "C" CORE_API_CLASS

#define CORE_RESULT int
#define CORE_SUCCEEDED	            (1)	 // 成功
#define CORE_FAILED		            (-1) // 汎用的な失敗
//#define CORE_FAILED_INITIALIZED      (-2) // 失敗_初期化
//#define CORE_FAILED_INVALID_ARGUMENT (-3) // 失敗_不適な引数

/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/



#endif // CORE_COMMON_H_

/************************************
* End of file
************************************/
