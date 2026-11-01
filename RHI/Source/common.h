
/***********************************************************************************
* @file    common.h
* @brief   汎用的なヘッダー
* @details
*
* @author  Keigo Onari
* @date    2026/09/13
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    その他
************************************************************************************/

#pragma once
#ifndef RHI_COMMON_H_
#define RHI_COMMON_H_

/******************************************************************
* Include Headers
******************************************************************/
#define CONSOLE_DEBUG
#ifdef  CONSOLE_DEBUG
#include <iostream>
#endif
/******************************************************************
* Macro Definitions
******************************************************************/
// RHI_API (dllexport / dllimport の切り替え) は公開ヘッダー側で定義する
#include "Public/rhiExport.h"

#define RHI_RESULT int
#define RHI_SUCCEEDED	             (1)  // 成功
#define RHI_FAILED		             (-1) // 汎用的な失敗
#define RHI_FAILED_INITIALIZED       (-2) // 失敗_初期化
#define RHI_FAILED_INVALID_ARGUMENT  (-3) // 失敗_不適な引数
#define RHI_FAILED_CREATE_ANY_OBJECT (-4) // 失敗_オブジェクト生成
#define RHI_FAILED_COMPILED_SHADER   (-5) // 失敗_シェーダーコンパイル

/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/



#endif // RHI_COMMON_H_

/************************************
* End of file
************************************/
