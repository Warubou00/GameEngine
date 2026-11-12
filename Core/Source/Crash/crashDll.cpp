/***********************************************************************************
* @file    crashDll.cpp
* @brief   Core.dll がクラッシュレポート (v1.8) について C# のエンジンへ export する関数
* @details C# 側の宣言は GameEngine.Runtime/CrashReporter.cs (CrashReporter.Install が Crash_Install を呼ぶ)
*          文字列は UTF-16 (C# の string をそのまま)
*
* @author  Keigo Onari
* @date    2026/11/12
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/


/******************************************************************
* Include Headers
******************************************************************/
#include "../common.h"
#include "crashHandler.h"

extern "C"
{
	// 受け取りを始める (root = レポートのフォルダ / sessionLog = 写すログ (null 可) / appName = レポートに書く名前)
	CORE_API bool Crash_Install(const wchar_t* root, const wchar_t* sessionLog, const wchar_t* appName)
	{
		return ko::CrashHandler::Install(root, sessionLog, appName);
	}

	CORE_API void Crash_Uninstall()
	{
		ko::CrashHandler::Uninstall();
	}

	// レポートに書く情報 (value = null で消す)
	CORE_API void Crash_SetInfo(const wchar_t* key, const wchar_t* value)
	{
		ko::CrashHandler::SetInfo(key, value);
	}

	// C# がレポートを書いた (この後の例外ではもう書かない)
	CORE_API void Crash_SetHandled()
	{
		ko::CrashHandler::SetHandled();
	}

	// C++ が書いたレポートのフォルダ (まだなら 0)。戻り値 = 文字数
	CORE_API int32_t Crash_GetReportFolder(wchar_t* buffer, int32_t length)
	{
		return ko::CrashHandler::GetReportFolder(buffer, length);
	}

	// 確認用にわざと落とす (1 = アクセス違反 / 2 = 別のスレッドでアクセス違反 / 3 = スタックオーバーフロー)
	CORE_API void Crash_Test(int32_t kind)
	{
		ko::CrashHandler::Test(kind);
	}
}

/************************************
* End of file
************************************/
