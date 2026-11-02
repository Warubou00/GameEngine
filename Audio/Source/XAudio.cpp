/***********************************************************************************
* @file    XAudio.cpp
* @brief   XAudio2 の管理 (サウンドの読み込み / ボイスの作成・再生・破棄)
* @details 再生のしかた : 読み込んだ PCM 全体を 1 つの XAUDIO2_BUFFER にして渡す (ループは LoopCount)
*          以前は 1024 フレームごとに細かいバッファに分け、OnBufferEnd (XAudio2 のスレッド) で次を 1 つずつ
*          渡していた。キューに 1 つしか無いので、コールバックが遅れると音が途切れる上、
*          コールバックの中で std::cout をしていた (オーディオのスレッドを止める)
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


/******************************************************************
* Include Headers
******************************************************************/
#include "XAudio.h"
#include "wavSoundLoader.h"
#include "../../Core/Source/Log/logger.h"

#include <algorithm>
#include <string>

namespace Audio
{
	bool XAudio::Initialize()
	{
		if (IsAvailable()) return true;

		// COMライブラリの初期化
		// C# のメインスレッドは [STAThread] なので RPC_E_CHANGED_MODE が返る (COM は初期化済み。XAudio2 はそのまま使える)
		// 以前は結果を見ずに、失敗時にも CoUninitialize していた (初期化していない COM を終了してしまう)
		const HRESULT comResult = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
		_comInitialized = SUCCEEDED(comResult);

		// XAudio2 初期化
		HRESULT hr = XAudio2Create(&_pXAudio2, 0, XAUDIO2_DEFAULT_PROCESSOR);
		if (FAILED(hr))
		{
			Core::Logger::Log("[Audio] XAudio2Create failed. Sound is disabled.");
			_pXAudio2 = nullptr;
			return false;
		}

		hr = _pXAudio2->CreateMasteringVoice(&_pMasterVoice);
		if (FAILED(hr))
		{
			// 音の出る機器が無い時など。以前は IXAudio2 を解放せずに残していた
			Core::Logger::Log("[Audio] CreateMasteringVoice failed (no audio device?). Sound is disabled.");
			_pMasterVoice = nullptr;
			_pXAudio2->Release();
			_pXAudio2 = nullptr;
			return false;
		}
		return true;
	}

	void XAudio::Shutdown()
	{
		// ボイスは PCM (_sounds) を指しているので、サウンドより先に壊す
		// DestroyVoice は XAudio2 のスレッドがそのボイスを使い終わるまで待つので、その後は PCM を解放してよい
		for (uint32_t i = 0; i < _voices.size(); i++)
		{
			if (_voices[i].alive) destroyVoiceSlot(i);
		}
		_voices.clear();
		_freeVoices.clear();

		if (_pMasterVoice)
		{
			_pMasterVoice->DestroyVoice();
			_pMasterVoice = nullptr;
		}
		// 以前は Release しておらず、XAudio2 のスレッドが残ったままだった
		if (_pXAudio2)
		{
			_pXAudio2->Release();
			_pXAudio2 = nullptr;
		}

		_sounds.clear();
		_freeSounds.clear();

		if (_comInitialized)
		{
			CoUninitialize();
			_comInitialized = false;
		}
	}

	void XAudio::SetMasterVolume(float volume)
	{
		if (_pMasterVoice) _pMasterVoice->SetVolume((std::max)(volume, 0.0f));
	}

	// ==========================================================================
	// サウンド
	// ==========================================================================

	uint32_t XAudio::LoadSound(const wchar_t* filePath)
	{
		if (!filePath) return INVALID_HANDLE;

		Library::Sound::Loader::WavLoader loader;
		Library::Sound::Loader::SoundData data;
		if (!loader.Load(filePath, &data)) return INVALID_HANDLE;

		uint32_t index;
		if (!_freeSounds.empty())
		{
			index = _freeSounds.back();
			_freeSounds.pop_back();
		}
		else
		{
			if (_sounds.size() >= 0xFFFF)
			{
				Core::Logger::Log("[Audio] Too many sounds (65535).");
				return INVALID_HANDLE;
			}
			index = static_cast<uint32_t>(_sounds.size());
			_sounds.emplace_back();
		}

		SoundSlot& slot = _sounds[index];
		slot.data  = std::move(data);
		slot.alive = true;
		return makeHandle(index, slot.generation);
	}

	void XAudio::DestroySound(uint32_t sound)
	{
		SoundSlot* slot = findSound(sound);
		if (!slot) return;

		// このサウンドの PCM を指しているボイスを先に壊す
		for (uint32_t i = 0; i < _voices.size(); i++)
		{
			if (_voices[i].alive && _voices[i].sound == sound) destroyVoiceSlot(i);
		}

		const uint32_t index = sound & 0xFFFF;
		slot->data       = {};
		slot->alive      = false;
		slot->generation = nextGeneration(slot->generation);
		_freeSounds.push_back(index);
	}

	float XAudio::GetSoundLength(uint32_t sound) const
	{
		const SoundSlot* slot = findSound(sound);
		if (!slot) return 0.0f;
		const auto* format = reinterpret_cast<const WAVEFORMATEX*>(slot->data.format.data());
		return format->nAvgBytesPerSec > 0
			? static_cast<float>(slot->data.dataSize) / static_cast<float>(format->nAvgBytesPerSec)
			: 0.0f;
	}

	// ==========================================================================
	// ボイス
	// ==========================================================================

	uint32_t XAudio::CreateVoice(uint32_t sound)
	{
		const SoundSlot* soundSlot = findSound(sound);
		if (!soundSlot || !IsAvailable()) return INVALID_HANDLE;

		// コールバックは渡さない (再生の終わりは GetPlaying で調べる)。以前は CallbackMethodsBase を渡し、
		// その中で std::cout をしていた
		IXAudio2SourceVoice* source = nullptr;
		const auto* format = reinterpret_cast<const WAVEFORMATEX*>(soundSlot->data.format.data());
		const HRESULT hr = _pXAudio2->CreateSourceVoice(&source, format, 0, XAUDIO2_DEFAULT_FREQ_RATIO, nullptr);
		if (FAILED(hr))
		{
			// 以前はここで CoUninitialize していた (XAudio2 を使っている途中で COM を終了してしまう)
			char message[128];
			sprintf_s(message, "[Audio] CreateSourceVoice failed (hr = 0x%08X).", static_cast<unsigned>(hr));
			Core::Logger::Log(message);
			return INVALID_HANDLE;
		}

		uint32_t index;
		if (!_freeVoices.empty())
		{
			index = _freeVoices.back();
			_freeVoices.pop_back();
		}
		else
		{
			if (_voices.size() >= 0xFFFF)
			{
				source->DestroyVoice();
				Core::Logger::Log("[Audio] Too many voices (65535).");
				return INVALID_HANDLE;
			}
			index = static_cast<uint32_t>(_voices.size());
			_voices.emplace_back();
		}

		VoiceSlot& slot = _voices[index];
		slot.source  = source;
		slot.sound   = sound;
		slot.alive   = true;
		slot.loop    = false;
		slot.started = false;
		return makeHandle(index, slot.generation);
	}

	void XAudio::DestroyVoice(uint32_t voice)
	{
		if (findVoice(voice)) destroyVoiceSlot(voice & 0xFFFF);
	}

	bool XAudio::Play(uint32_t voice)
	{
		VoiceSlot* slot = findVoice(voice);
		if (!slot) return false;

		// 再生中なら止めてキューを空にしてから、最初から入れ直す
		if (slot->started || isQueued(*slot))
		{
			slot->source->Stop(0);
			slot->source->FlushSourceBuffers();
			// Stop / Flush は XAudio2 のスレッドで次の処理の時に反映されるので、今鳴っているバッファが
			// キューに残ることがある。その時はボイスを作り直す (残ったまま入れると、前の続きの後に鳴ってしまう)
			if (isQueued(*slot))
			{
				const uint32_t index = voice & 0xFFFF;
				const auto* format = reinterpret_cast<const WAVEFORMATEX*>(findSound(slot->sound)->data.format.data());
				IXAudio2SourceVoice* source = nullptr;
				if (FAILED(_pXAudio2->CreateSourceVoice(&source, format, 0, XAUDIO2_DEFAULT_FREQ_RATIO, nullptr))) return false;
				_voices[index].source->DestroyVoice();
				_voices[index].source = source;
				slot = &_voices[index];
			}
		}

		if (!submit(*slot)) return false;
		if (FAILED(slot->source->Start(0))) return false;
		slot->started = true;
		return true;
	}

	void XAudio::Stop(uint32_t voice)
	{
		VoiceSlot* slot = findVoice(voice);
		if (!slot) return;
		slot->source->Stop(0);
		slot->source->FlushSourceBuffers();
		slot->started = false;
	}

	void XAudio::SetVolume(uint32_t voice, float volume)
	{
		if (VoiceSlot* slot = findVoice(voice)) slot->source->SetVolume((std::max)(volume, 0.0f));
	}

	void XAudio::SetLoop(uint32_t voice, bool loop)
	{
		VoiceSlot* slot = findVoice(voice);
		if (!slot || slot->loop == loop) return;
		slot->loop = loop;
		// ループ中に切った時は、今の周の終わりで止まるようにする (付けた時は次の Play から)
		if (!loop && slot->started) slot->source->ExitLoop(0);
	}

	void XAudio::GetPlaying(const uint32_t* voices, uint32_t count, uint8_t* outPlaying)
	{
		if (!voices || !outPlaying) return;
		for (uint32_t i = 0; i < count; i++)
		{
			VoiceSlot* slot = findVoice(voices[i]);
			bool playing = false;
			if (slot && slot->started)
			{
				playing = isQueued(*slot);
				if (!playing)
				{
					// 鳴り終わった。ボイスは Start のままなので止めておく (次の Play で入れ直す)
					slot->source->Stop(0);
					slot->started = false;
				}
			}
			outPlaying[i] = playing ? 1 : 0;
		}
	}

	// ==========================================================================
	// 内部処理
	// ==========================================================================

	XAudio::SoundSlot* XAudio::findSound(uint32_t handle)
	{
		const uint32_t index = handle & 0xFFFF;
		if (handle == INVALID_HANDLE || index >= _sounds.size()) return nullptr;
		SoundSlot& slot = _sounds[index];
		return (slot.alive && slot.generation == (handle >> 16)) ? &slot : nullptr;
	}

	const XAudio::SoundSlot* XAudio::findSound(uint32_t handle) const
	{
		return const_cast<XAudio*>(this)->findSound(handle);
	}

	XAudio::VoiceSlot* XAudio::findVoice(uint32_t handle)
	{
		const uint32_t index = handle & 0xFFFF;
		if (handle == INVALID_HANDLE || index >= _voices.size()) return nullptr;
		VoiceSlot& slot = _voices[index];
		return (slot.alive && slot.generation == (handle >> 16)) ? &slot : nullptr;
	}

	void XAudio::destroyVoiceSlot(uint32_t index)
	{
		VoiceSlot& slot = _voices[index];
		// DestroyVoice は XAudio2 のスレッドが使い終わるまで待つ (この後に PCM を解放しても安全)
		if (slot.source) slot.source->DestroyVoice();
		slot.source     = nullptr;
		slot.sound      = INVALID_HANDLE;
		slot.alive      = false;
		slot.started    = false;
		slot.generation = nextGeneration(slot.generation);
		_freeVoices.push_back(index);
	}

	bool XAudio::submit(VoiceSlot& voice)
	{
		const SoundSlot* sound = findSound(voice.sound);
		if (!sound) return false;

		// PCM 全体を 1 つのバッファで渡す (以前は 1024 フレームごとに分けて 1 つずつ渡していた)
		XAUDIO2_BUFFER buffer{};
		buffer.Flags      = XAUDIO2_END_OF_STREAM;
		buffer.AudioBytes = sound->data.dataSize;
		buffer.pAudioData = sound->data.Pcm();
		buffer.LoopCount  = voice.loop ? XAUDIO2_LOOP_INFINITE : 0;   // LoopBegin / LoopLength が 0 なら全体をループ

		const HRESULT hr = voice.source->SubmitSourceBuffer(&buffer);
		if (FAILED(hr))
		{
			char message[128];
			sprintf_s(message, "[Audio] SubmitSourceBuffer failed (hr = 0x%08X).", static_cast<unsigned>(hr));
			Core::Logger::Log(message);
			return false;
		}
		return true;
	}

	bool XAudio::isQueued(const VoiceSlot& voice) const
	{
		// XAUDIO2_VOICE_NOSAMPLESPLAYED : 再生位置を計算しない分だけ軽い
		XAUDIO2_VOICE_STATE state{};
		voice.source->GetState(&state, XAUDIO2_VOICE_NOSAMPLESPLAYED);
		return state.BuffersQueued > 0;
	}
}

/************************************
* End of file
************************************/
