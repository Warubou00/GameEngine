/***********************************************************************************
* @file    crashHandler.h
* @brief   クラッシュレポート (v1.8) : C++ 側 (ネイティブ) のクラッシュを受け取り、ミニダンプと原因のテキストを書く
* @details - SetUnhandledExceptionFilter で、どこにも受け取られなかった例外 (アクセス違反 / 0 除算 / 不正な命令 等) を受け取る
*          - C# から呼んだ C++ の中で落ちた時は .NET が FailFast で終わらせ、上のフィルターが呼ばれないので、
*            ベクター例外ハンドラーでも受け取る (落ちた場所がネイティブの DLL の時だけ。C# の null 参照等は .NET が例外にするので除く)
*          - ダンプは起動時に作っておいた別のスレッドで書く (落ちたスレッドはスタックが残っていないことがある : スタックオーバーフロー)
*          - 書く物 : <root>/Crash_日時/crash.dmp (MiniDumpWriteDump) / native.txt (例外 / モジュール + オフセット / スタック)
*                     / log.txt (C# が書いているこのセッションのログの写し)
*          - C# の例外 (0xE0434352) は C# の CrashReporter が書くので、ここでは書かない
*          C# 側は GameEngine.Runtime/CrashReporter.cs (Crash_* は crashDll.cpp)
*
* @author  Keigo Onari
* @date    2026/11/12
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/

#pragma once
#ifndef CORE_CRASH_HANDLER_H_
#define CORE_CRASH_HANDLER_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <cstdint>

namespace ko
{
	class CrashHandler
	{
	public:
		// 受け取りを始める。root = レポートを置くフォルダ / sessionLog = 写すログのファイル (null 可) / appName = レポートに書く名前
		// 2 回目以降は設定 (フォルダ / ログ) だけ替える
		static bool Install(const wchar_t* root, const wchar_t* sessionLog, const wchar_t* appName);

		// 受け取りを止める (元のフィルターに戻し、書くスレッドを終える)
		static void Uninstall();

		// レポートに書く情報 (今のシーン 等)。同じ key なら上書き。value が null なら消す
		static void SetInfo(const wchar_t* key, const wchar_t* value);

		// C# がレポートを書いた後に呼ぶ (この後の例外ではもう書かない。同じクラッシュで 2 つ作らないため)
		static void SetHandled();

		// 書いたレポートのフォルダ (まだなら 0 を返す)。戻り値 = 文字数
		static int32_t GetReportFolder(wchar_t* buffer, int32_t length);

		// 確認用にわざと落とす。1 = このスレッドでアクセス違反 / 2 = 別のスレッドでアクセス違反 / 3 = スタックオーバーフロー
		static void Test(int32_t kind);
	};
}

#endif // CORE_CRASH_HANDLER_H_

/************************************
* End of file
************************************/
