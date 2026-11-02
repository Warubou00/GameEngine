/***********************************************************************************
* @file    common.h
* @brief   Audio.dll の共通定義 (export の指定とハンドル)
* @details 以前の Audio は静的ライブラリ (Audio.lib) で、export の指定は無かった
*
* @author  Keigo Onari
* @date    2026/11/02
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/
#pragma once
#ifndef AUDIO_COMMON_H_
#define AUDIO_COMMON_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <cstdint>

/******************************************************************
* Macro Definitions
******************************************************************/
// Audio プロジェクトでは AUDIO_EXPORTS を定義してビルドする (dllexport)。使う側では dllimport になる
#ifdef AUDIO_EXPORTS
#define AUDIO_API_CLASS __declspec(dllexport)
#else
#define AUDIO_API_CLASS __declspec(dllimport)
#endif
#define AUDIO_API extern "C" AUDIO_API_CLASS

namespace Audio
{
	// 無効なハンドル (C# の AudioNativeMethods.InvalidHandle と同じ)
	constexpr uint32_t INVALID_HANDLE = 0xFFFFFFFFu;
}

#endif // AUDIO_COMMON_H_

/************************************
* End of file
************************************/
