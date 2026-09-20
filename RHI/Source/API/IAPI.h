
/***********************************************************************************
* @file    IFence.h
* @brief   フェンスクラス
* @details
*
* @author  Keigo Onari
* @date    2026/09/20
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note
************************************************************************************/

#pragma once
#ifndef API_INTERFACE_H_
#define API_INTERFACE_H_

/******************************************************************
* Include Headers
******************************************************************/


/******************************************************************
* Macro Definitions
******************************************************************/
#define RHI_API_RESULT int

#define RHI_API_SUCCEEDED	            (1)	 // 成功
#define RHI_API_FAILED		            (-1) // 汎用的な失敗
#define RHI_API_FAILED_INITIALIZED      (-2) // 失敗_初期化
#define RHI_API_FAILED_INVALID_ARGUMENT (-3) // 失敗_不適な引数

/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/



namespace RHI
{
    namespace APIInterface
    {

    }
}


#endif // API_INTERFACE_H_

/************************************
* End of file
************************************/
