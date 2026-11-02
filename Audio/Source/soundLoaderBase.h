/***********************************************************************************
* @file    soundLoaderBase.h
* @brief   サウンドローダーの親クラス
* @details 読み込んだ結果は SoundData (XAudio2 にそのまま渡せる形式 + PCM) で返す
*          以前は Load(const std::string&) が bool を返すだけで結果の受け取り方が決まっておらず、
*          WavLoader は private 継承・仮想デストラクタ無しだったため、親クラスとしては使えなかった
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
#ifndef _SOUND_LOADER_H_
#define _SOUND_LOADER_H_

#include <cstdint>
#include <string>
#include <vector>

namespace Library
{
	namespace Sound
	{
		namespace Loader
		{
			/// <summary>
			/// 読み込んだサウンド
			///   format : WAVEFORMATEX (+ 拡張部分) のバイト列。XAudio2 の CreateSourceVoice にそのまま渡す
			///   bytes  : ファイル全体。PCM は bytes[dataOffset] から dataSize バイト (別の配列へコピーしない)
			/// </summary>
			struct SoundData
			{
				std::vector<uint8_t> format;
				std::vector<uint8_t> bytes;
				uint32_t dataOffset = 0;
				uint32_t dataSize   = 0;

				const uint8_t* Pcm() const { return bytes.data() + dataOffset; }
			};

			/// <summary>
			/// サウンドのローダー親クラス
			/// </summary>
			class SoundLoaderBase
			{
			public:
				// 以前は仮想デストラクタが無く、親クラスのポインタで delete すると未定義動作だった
				virtual ~SoundLoaderBase() = default;

				/// <summary>
				/// 読み込み。失敗時は false (理由はログに出す)
				/// </summary>
				virtual bool Load(const std::wstring& filePath, SoundData* outData) = 0;
			};
		}
	}
}

#endif // _SOUND_LOADER_H_

// ==============================
// end of file
// ==============================
