
/***********************************************************************************
* @file    logger.h
* @brief   ロガー
* @details 
*
* @author  Keigo Onari
* @date    2026/09/16
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    その他
************************************************************************************/

#pragma once
#ifndef LOGGER_H_
#define LOGGER_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "../common.h"

/******************************************************************
* Macro Definitions
******************************************************************/
typedef void (*LogCallback)(const char* message);
typedef void (*WLogCallback)(const wchar_t* message);

/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/



namespace Core
{
    class CORE_API_CLASS Logger
    {

        /******************************************************************
        * Internal Enumeration Declarations
        ******************************************************************/


        /******************************************************************
        * Internal Structure Declarations
        ******************************************************************/


    public:          /*---コンストラクタ・デストラクタ---*/



    public:          /*---クラスメソッド---*/


        /***********************************************************************************
         * @brief 初期化関数
         * @details インスタンス後にこれを先に呼ぶ
         *
         * @param[in] LogCallback ログコールバック関数(const char*)
         * @param[in] WLogCallback ログコールバック関数(const wchar_t*)
         * @return 無し
         ************************************************************************************/
        static void Initialize(LogCallback logCallback, WLogCallback w_logCallback);

        /***********************************************************************************
         * @brief ログ出力(const char*)
         * @details 
         *
         * @param[in] const char* message ログ
         * @return 無し
         ************************************************************************************/
        static void Log(const char* message);

        /***********************************************************************************
         * @brief ログ出力(const wchar_t*)
         * @details 
         *
         * @param[in] const wchar_t* message ログ(2byte)
         * @return 無し
         ************************************************************************************/
        static void Log(const wchar_t* message);

    public:          /*---アクセッサー関数群---*/


    private:         /*---メンバ変数---*/

        static LogCallback _logCallback;
        static WLogCallback _w_logCallback;
        static bool _initialized;
    };
}


#endif // HASH_H_

/************************************
* End of file
************************************/
