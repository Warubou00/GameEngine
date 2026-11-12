/***********************************************************************************
* @file    XAudio.h
* @brief   XAudio2 の管理 (サウンドの読み込み / ボイスの作成・再生・破棄)
* @details Audio.dll の中身。C# からは audioDll.cpp の C ABI 経由で使う
*          - サウンド (読み込んだ PCM) とボイス (再生する物。AudioSource 1 つに 1 個) はハンドル (uint32) で扱う
*          - ハンドルは「番号 (下位 16bit) + 世代 (上位 16bit)」。破棄した後の古いハンドルは無視される
*          - 呼び出しは 1 つのスレッド (C# のメインスレッド) から行う。XAudio2 のコールバックは使わない
*          以前は Initialize / CreateSourceVoiceWAV / CreateAudioBuffers だけで、ボイスやバッファの寿命は
*          使う側 (Engine/main.cpp) が管理し、IXAudio2 / マスターボイスは解放されていなかった
*
* @author  Keigo Onari
* @date    2026/01/29
* @version v_1.1.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note
************************************************************************************/

#pragma once
#ifndef XAUDIO_H_
#define XAUDIO_H_

/******************************************************************
* Include Headers
******************************************************************/
#define NOMINMAX
#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <xaudio2.h>
#include <vector>
#include "common.h"
#include "soundLoaderBase.h"
// 以前は winmm.lib もリンクしていたが、使っていないので外した
#pragma comment(lib, "xaudio2.lib")

namespace Audio
{
    class XAudio
    {
    public:          /*---コンストラクタ・デストラクタ---*/
        XAudio() = default;
        ~XAudio() { Shutdown(); }   // 以前は空で、何も解放していなかった
        XAudio(const XAudio&) = delete;
        XAudio& operator=(const XAudio&) = delete;

    public:          /*---クラスメソッド---*/

        /***********************************************************************************
         * @brief 初期化 (XAudio2 とマスターボイス)
         * @details 失敗しても (音の出る機器が無い等) サウンドの読み込みはできる。再生だけができない
         * @return bool 再生できる状態になったか
         ************************************************************************************/
        bool Initialize();

        /***********************************************************************************
         * @brief 終了 (全てのボイス → マスターボイス → XAudio2 の順に解放)
         ************************************************************************************/
        void Shutdown();

        bool IsAvailable() const { return _pXAudio2 != nullptr && _pMasterVoice != nullptr; }
        void SetMasterVolume(float volume);

        /***********************************************************************************
         * @brief 音の処理を止める / 再開する (v1.8 : ウィンドウが後ろにある間、XAudio2 の処理のスレッドを止めて CPU を休ませる)
         * @details StopEngine / StartEngine。ボイスの再生位置はそのまま残り、再開するとその続きから鳴る
         ************************************************************************************/
        void Suspend();
        void Resume();

        /***********************************************************************************
         * @brief サウンド (wav) の読み込み
         * @return サウンドのハンドル (失敗時 INVALID_HANDLE)
         ************************************************************************************/
        uint32_t LoadSound(const wchar_t* filePath);

        /***********************************************************************************
         * @brief サウンドの破棄 (このサウンドを使っているボイスも破棄する)
         ************************************************************************************/
        void DestroySound(uint32_t sound);

        // サウンドの長さ (秒。無効なハンドルなら 0)
        float GetSoundLength(uint32_t sound) const;

        /***********************************************************************************
         * @brief ボイスの作成 (サウンドの形式でソースボイスを作る。再生はしない)
         * @return ボイスのハンドル (失敗時 / 再生できない環境では INVALID_HANDLE)
         ************************************************************************************/
        uint32_t CreateVoice(uint32_t sound);
        void DestroyVoice(uint32_t voice);

        // 最初から再生する (再生中でも最初に戻る)
        bool Play(uint32_t voice);
        void Stop(uint32_t voice);
        void SetVolume(uint32_t voice, float volume);
        // 再生中にループを切ると、今の周の終わりで止まる。再生中にループを付けた時は次の Play から
        void SetLoop(uint32_t voice, bool loop);
        // 再生中か (最後まで鳴り終わったら false)。まとめて調べる (outPlaying は 0 / 1)
        void GetPlaying(const uint32_t* voices, uint32_t count, uint8_t* outPlaying);

    private:         /*---内部処理---*/
        struct SoundSlot
        {
            Library::Sound::Loader::SoundData data;
            uint16_t generation = 1;
            bool     alive      = false;
        };

        struct VoiceSlot
        {
            IXAudio2SourceVoice* source = nullptr;
            uint32_t sound      = INVALID_HANDLE;
            uint16_t generation = 1;
            bool     alive      = false;
            bool     loop       = false;
            bool     started    = false;   // Play してから Stop / 鳴り終わりまで
        };

        static uint32_t makeHandle(uint32_t index, uint16_t generation) { return (static_cast<uint32_t>(generation) << 16) | index; }
        static uint16_t nextGeneration(uint16_t generation) { return generation >= 0x7FFF ? 1 : static_cast<uint16_t>(generation + 1); }

        SoundSlot*       findSound(uint32_t handle);
        const SoundSlot* findSound(uint32_t handle) const;
        VoiceSlot*       findVoice(uint32_t handle);
        void destroyVoiceSlot(uint32_t index);
        bool submit(VoiceSlot& voice);
        bool isQueued(const VoiceSlot& voice) const;

    private:         /*---メンバ変数---*/
        IXAudio2* _pXAudio2 = nullptr;
        IXAudio2MasteringVoice* _pMasterVoice = nullptr;
        bool _comInitialized = false;   // CoInitializeEx が成功した時だけ CoUninitialize する

        // 空いた番号は使い回す。std::vector が伸びても、SoundData の中の配列 (PCM) の場所は変わらない
        std::vector<SoundSlot> _sounds;
        std::vector<VoiceSlot> _voices;
        std::vector<uint32_t>  _freeSounds;
        std::vector<uint32_t>  _freeVoices;
    };
}

#endif // XAUDIO_H_

/************************************
* End of file
************************************/
