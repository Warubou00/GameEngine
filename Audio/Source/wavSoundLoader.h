/***********************************************************************************
* @file    wavSoundLaoder.h
* @brief   WAVファイル読み込み
* @details 対応形式 : リニア PCM (8 / 16 / 24 / 32bit)、IEEE float (32bit)、WAVE_FORMAT_EXTENSIBLE (中身が PCM / float)
*          以前は 16bit を前提に std::vector<short> へ読み込み、WAVE_FORMAT_EXTENSIBLE (24bit やマルチチャンネルの
*          wav で使われる) は読めなかった
*
* @author  Keigo Onari
* @date    2025/09/05
* @version v_1.1.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    その他
************************************************************************************/
#pragma once
#ifndef _WAV_SOUND_LOADER_H_
// 以前は #define _WAV_SOUND_LAODER_H_ と綴りが違い、インクルードガードが効いていなかった (#pragma once で助かっていた)
#define _WAV_SOUND_LOADER_H_

#include "soundLoaderBase.h"

namespace Library
{
    namespace Sound
    {
        namespace Loader
        {
            /// <summary>
            /// wavファイルのローダー
            ///   ファイル全体を 1 回の ReadFile で読み、メモリ上でチャンクを解析する
            ///   (以前は std::ifstream でチャンクごとに read / seekg していた)
            /// </summary>
            class WavLoader : public SoundLoaderBase
            {
            public:
                /// <summary>
                /// Wavファイルの読み込み
                /// </summary>
                /// <param name="filePath">パス (UTF-16。日本語のパスも読める)</param>
                /// <param name="outData">結果</param>
                /// <returns>成功したか</returns>
                bool Load(const std::wstring& filePath, SoundData* outData) override;

                /// <summary>
                /// メモリ上の wav を解析する (data->bytes にファイルの中身が入っている前提)
                /// </summary>
                static bool Parse(SoundData* data);
            };
        }
    }
}

#endif // _WAV_SOUND_LOADER_H_

// ==============================
// end of file
// ==============================
