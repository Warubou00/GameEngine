/***********************************************************************************
* @file    wavSoundLaoder.h
* @brief   WAVファイル読み込み
* @details
*
* @author  Keigo Onari
* @date    2025/09/05
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    その他
************************************************************************************/
#pragma once
#ifndef _WAV_SOUND_LOADER_H_
#define _WAV_SOUND_LAODER_H_

#define NOMINMAX
#include <windows.h>
#include <string>
#include <vector>
#include "soundLoaderBase.h"

namespace Library
{
    namespace Sound
    {
        namespace Loader
        {
            /// <summary>
            /// wavファイルのローダー
            /// </summary>
            class WavLoader : SoundLoaderBase
            {
            public:
                /// <summary>
                /// Wavファイルの読み込み
                /// </summary>
                /// <param name="filePath"></param>
                /// <returns></returns>
                bool Load(const std::string& filePath)override;

                /// <summary>
                /// Wavファイルの読み込み(Wavのみ)
                /// </summary>
                /// <param name="filePath"></param>
                /// <param name="wavFormat"></param>
                /// <returns></returns>
                std::vector<short> Load(const std::string& filePath, WAVEFORMATEX* wavFormat);

                ///// <summary>
                ///// フォーマットの取得
                ///// </summary>
                ///// <returns></returns>
                //const WAVEFORMATEX& GetFormat()const;

                ///// <summary>
                ///// PCMデータの取得
                ///// </summary>
                ///// <returns></returns>
                //const std::vector<short>& GetAudioData() const;

                ///// <summary>
                ///// サンプルレートの取得
                ///// </summary>
                ///// <returns></returns>
                //unsigned int GetSampleRate() const;

                ///// <summary>
                ///// チャンネル数の取得
                ///// </summary>
                ///// <returns></returns>
                //unsigned short GetNumChannels() const;

                ///// <summary>
                ///// サンプル単位のビット数
                ///// </summary>
                ///// <returns></returns>
                //unsigned short GetBitsPerSample() const;

            private:

                struct RIFFHeader
                {
                    char chunkID[4];
                    unsigned int chunkSize;
                    char format[4];
                };

                //struct FormatChunk
                //{
                //    //char subchunk1ID[4];
                //    unsigned int fmtChunkSize;
                //    unsigned short audioFormat;
                //    unsigned short numChannels;
                //    unsigned int sampleRate;
                //    unsigned int byteRate;
                //    unsigned short blockAlign;
                //    unsigned short bitsPerSample;
                //};

                //struct DataChunk
                //{
                //    //char subchunk2ID[4];
                //    unsigned int dataChunkSize;
                //};

#pragma pack(push, 1)
                struct ChunkHeader {
                    char id[4];
                    unsigned int size;
                };
#pragma pack(pop)

                ///// <summary>
                ///// RIFFヘッダー
                ///// </summary>
                //RIFFHeader _riffHeader{};

                ///// <summary>
                ///// fmtチャンク
                ///// </summary>
                //FormatChunk _formatChunk{};

                ///// <summary>
                ///// formatチャンク
                ///// </summary>
                //WAVEFORMATEX _format{};

                ///// <summary>
                ///// dataチャンク
                ///// </summary>
                //DataChunk _dataChunk{};
                //std::vector<short> _audioData;

                //const int _CHUNK_ID_SIZE = 4;
                //const unsigned int _LINEAR_PCM_FMT_CHUNK_SIZE = 16U;
                //const std::string _CHUNK_JUNK = "JUNK";
                //const std::string _CHUNK_FMT = "fmt ";
                //const std::string _CHUNK_DATA = "data";

                ///// <summary>
                ///// チャンクのチェック
                ///// </summary>
                ///// <param name="chunk"></param>
                ///// <param name="checkChunk"></param>
                ///// <returns></returns>
                //bool checkChunk(const char* chunk, const char* checkChunk);
            };
        }
    }
}

#endif // _WAV_SOUND_LOADER_H_

// ==============================
// end of file
// ==============================