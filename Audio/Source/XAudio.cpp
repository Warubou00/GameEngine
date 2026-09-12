
/***********************************************************************************
* @file    XAudio.cpp
* @brief   
* @details 
*
* @author  Keigo Onari
* @date    2026/01/29
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    
************************************************************************************/


/******************************************************************
* Include Headers
******************************************************************/
#include "XAudio.h"

/******************************************************************
* Macro Definitions
******************************************************************/


/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/



namespace Audio
{
	bool XAudio::Initialize()
	{
        // COMライブラリの初期化
        (void)CoInitializeEx(NULL, COINIT_MULTITHREADED);

        // XAudio2 初期化
        if (FAILED(XAudio2Create(&_pXAudio2, 0)))
        {
            std::cout << "XAudio2の初期化に失敗しました\n";

            // COMライブラリの終了処理
            CoUninitialize();

            return false;
        }

        if (FAILED(_pXAudio2->CreateMasteringVoice(&_pMasterVoice)))
        {
            std::cout << "Mastering Voice作成失敗\n";

            // COMライブラリの終了処理
            CoUninitialize();
            return false;
        }

		return true;
	}

    IXAudio2SourceVoice* XAudio::CreateSourceVoiceWAV(const WAVEFORMATEX& waveFormat, XAudio::CallbackMethodsBase* callback)
    {
        IXAudio2SourceVoice* pSourceVoice = nullptr;
        if (FAILED(_pXAudio2->CreateSourceVoice(&pSourceVoice, &waveFormat, 0, XAUDIO2_DEFAULT_FREQ_RATIO, callback)))
        {
            std::cout << "Source Voice作成失敗\n";

            // COMライブラリの終了処理
            CoUninitialize();
            return nullptr;
        }

        return pSourceVoice;
    }

    std::vector<XAUDIO2_BUFFER> XAudio::CreateAudioBuffers(BYTE* pAudioData, DWORD totalDataSizeBytes, int channels, WORD nBlockAlign)
    {
        std::vector<XAUDIO2_BUFFER> buffers;

        // 1サンプル(1要素)あたりのバイト数 (16bit = 2byte)
        const int bytesPerSample = sizeof(short);

        // 1フレームあたりのバイト数 (ステレオなら 2byte * 2ch = 4byte)
        const int bytesPerFrame = bytesPerSample * channels;

        // 1ブロックあたりのフレーム数
        const int framesPerBuffer = 1024;

        // 1ブロックあたりのバイトサイズ計算
        const DWORD bufferSize = framesPerBuffer * bytesPerFrame;

        DWORD currentOffset = 0;

        // データを走査してバッファ構造体を作成
        while (currentOffset < totalDataSizeBytes)
        {
            // 今回のチャンクサイズを計算 (末尾の端数処理)
            DWORD dataSize = bufferSize;
            if (currentOffset + bufferSize >= totalDataSizeBytes) {
                dataSize = totalDataSizeBytes - currentOffset;
            }

            // XAUDIO2_BUFFER の設定
            XAUDIO2_BUFFER buffer = { 0 };
            buffer.pAudioData = pAudioData + currentOffset; // ポインタをずらす
            buffer.AudioBytes = dataSize;                   // サイズを設定
            buffer.Flags = 0; // 必要に応じて XAUDIO2_END_OF_STREAM などを設定

            // リストに追加
            buffers.push_back(buffer);

            // オフセットを進める
            currentOffset += dataSize;
        }

        // 最後のバッファに「ストリーム終了」フラグを立てておく
        if (!buffers.empty()) {
            buffers.back().Flags = XAUDIO2_END_OF_STREAM;
        }

        return buffers;
    }

    void XAudio::ProcessWaveBuffer(std::vector<short>& data, BYTE** outFrontPointer, DWORD* outBufferSize)
    {
        *outFrontPointer = reinterpret_cast<BYTE*>(data.data());
        *outBufferSize = static_cast<DWORD>(data.size() * sizeof(short));
    }
}

/************************************
* End of file
************************************/
