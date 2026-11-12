/***********************************************************************************
* @file    audioDll.cpp
* @brief   Audio.dll が C# のエンジン (GameEngine.Runtime) へ export する関数
* @details C# 側の宣言は GameEngine.Runtime/Native/AudioNativeMethods.cs
*          - サウンド / ボイスはハンドル (uint32) でやり取りする。ポインタは C# に渡さない
*          - 毎フレーム調べる「再生中か」は配列でまとめて渡す (P/Invoke の回数を増やさない)
*          - 呼び出しは C# のメインスレッドから (Initialize と Shutdown は同じスレッドで)
*          - ログは Core.dll の Logger に出す (Renderer_SetLogCallback で C# のコンソールに流れる)
*          以前の Audio は静的ライブラリ (Audio.lib) で、C++ の Engine/main.cpp からクラスを直接使っていた
*
* @author  Keigo Onari
* @date    2026/11/02
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/


/******************************************************************
* Include Headers
******************************************************************/
#include "common.h"
#include "XAudio.h"

namespace
{
	Audio::XAudio* g_audio = nullptr;
}

extern "C"
{
	// ==============================================================
	// 初期化 / 終了
	// ==============================================================

	// 戻り値 : 再生できるか。false でもサウンドの読み込みはできる (再生の関数は何もしない)
	AUDIO_API bool Audio_Initialize()
	{
		if (g_audio == nullptr)
			g_audio = new Audio::XAudio();
		return g_audio->Initialize();
	}

	AUDIO_API void Audio_Shutdown()
	{
		if (g_audio == nullptr)
			return;

		g_audio->Shutdown();
		delete g_audio;
		g_audio = nullptr;
	}

	AUDIO_API bool Audio_IsAvailable()
	{
		return g_audio ? g_audio->IsAvailable() : false;
	}

	// v1.8 : 音の処理を止める / 再開する (ウィンドウが後ろにある間。再開すると続きから鳴る)
	AUDIO_API void Audio_Suspend()
	{
		if (g_audio) g_audio->Suspend();
	}

	AUDIO_API void Audio_Resume()
	{
		if (g_audio) g_audio->Resume();
	}

	// 全体の音量 (1 = そのまま)
	AUDIO_API void Audio_SetMasterVolume(float volume)
	{
		if (g_audio) g_audio->SetMasterVolume(volume);
	}

	// ==============================================================
	// サウンド (読み込んだ wav。同じサウンドを複数のボイスで鳴らせる)
	// ==============================================================

	// 戻り値はハンドル (失敗時 INVALID_HANDLE)。outLengthSeconds に長さ (秒) を返す
	AUDIO_API uint32_t Audio_LoadSound(const wchar_t* filePath, float* outLengthSeconds)
	{
		const uint32_t handle = g_audio ? g_audio->LoadSound(filePath) : Audio::INVALID_HANDLE;
		if (outLengthSeconds) *outLengthSeconds = g_audio ? g_audio->GetSoundLength(handle) : 0.0f;
		return handle;
	}

	// このサウンドを使っているボイスも破棄される
	AUDIO_API void Audio_DestroySound(uint32_t sound)
	{
		if (g_audio) g_audio->DestroySound(sound);
	}

	// ==============================================================
	// ボイス (AudioSource 1 つに 1 個)
	// ==============================================================

	// 戻り値はハンドル (失敗時 / 再生できない環境では INVALID_HANDLE)
	AUDIO_API uint32_t Audio_CreateVoice(uint32_t sound)
	{
		return g_audio ? g_audio->CreateVoice(sound) : Audio::INVALID_HANDLE;
	}

	AUDIO_API void Audio_DestroyVoice(uint32_t voice)
	{
		if (g_audio) g_audio->DestroyVoice(voice);
	}

	// 最初から再生する (再生中でも最初に戻る)
	AUDIO_API bool Audio_Play(uint32_t voice)
	{
		return g_audio ? g_audio->Play(voice) : false;
	}

	AUDIO_API void Audio_Stop(uint32_t voice)
	{
		if (g_audio) g_audio->Stop(voice);
	}

	// 音量 (1 = そのまま、0 = 無音)
	AUDIO_API void Audio_SetVolume(uint32_t voice, float volume)
	{
		if (g_audio) g_audio->SetVolume(voice, volume);
	}

	// ループ。再生中に切ると今の周の終わりで止まる。再生中に付けた時は次の Play から
	AUDIO_API void Audio_SetLoop(uint32_t voice, bool loop)
	{
		if (g_audio) g_audio->SetLoop(voice, loop);
	}

	// 再生中か (outPlaying[i] = 0 / 1)。まとめて調べる
	AUDIO_API void Audio_GetPlaying(const uint32_t* voices, uint32_t count, uint8_t* outPlaying)
	{
		if (g_audio) g_audio->GetPlaying(voices, count, outPlaying);
	}
}

/************************************
* End of file
************************************/
