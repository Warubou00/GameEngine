#include "main.h"
#include "../Platform/Source/windowSystem.h"
#include "../Engine/Math/Vector/vector.h"
#include "../Core/Source/Math/Float/float.h"
#include "../RHI/Source/RenderHardwareInterface.h"
#include "../Renderer/Source/meshRenderer.h"
#include "../Core/Source/Math/hash.h"
#include "../Audio/Source/wavSoundLoader.h"
#include "../Audio/Source/XAudio.h"

// ==================================================================
// sdlチェック無視
// ==================================================================
#define _CRT_SECURE_NO_WARNINGS

#define USE_CONSOLE

// ==================================================================
// メモリリークチェック
// ==================================================================
#define _CRTDBG_MAP_ALLOC
#include <stdlib.h>
#include <crtdbg.h>

#ifdef USE_CONSOLE
#include <iostream>
#endif

// ==================================================================
// ライブラリのリンク
// ==================================================================
#pragma comment(lib, "Platform.lib")
#pragma comment(lib, "Core.lib")
#pragma comment(lib, "RHI.lib")
#pragma comment(lib, "Renderer.lib")
#pragma comment(lib, "Audio.lib")


IXAudio2SourceVoice* g_sourceVoice = nullptr;
int g_index = 0;
std::vector<XAUDIO2_BUFFER> g_audioBuffers = {};
bool g_stopFlag = false;

HWND g_hWnd;

void FillNextBuffer()
{
	if (!g_sourceVoice)return;
	if (g_index >= g_audioBuffers.size())
	{
		g_stopFlag = true;
		return;
	}

	if (FAILED(g_sourceVoice->SubmitSourceBuffer(&g_audioBuffers[g_index])))
	{
		std::cout << "Submit失敗" << std::endl;
		g_stopFlag = true;
		return;
	}

	g_index++;
}

#ifdef USE_CONSOLE
int main(char argv, char* args)
#else
int APIENTRY WinMain(HINSTANCE hInstance, HINSTANCE hPrevInstance, LPSTR lpCmdLine, int nCmdShow)
#endif
{
#ifdef USE_CONSOLE
	HINSTANCE hInstance = GetModuleHandle(nullptr);
	int nCmdShow;
#else
	UNREFERENCED_PARAMETER(hPrevInstance);
	UNREFERENCED_PARAMETER(lpCmdLine);
#endif

	Platform::WindowSystem winSystem{};
	// ウインドウ初期化及び生成
	winSystem.InitWindow(
		&g_hWnd,
		hInstance,
		"GraphicEngine",
		500,
		500);

#ifdef USE_CONSOLE

	struct soundCallback : public Audio::XAudio::CallbackMethodsBase
	{
		void OnBufferStart(void* ctx)override
		{
			std::cout << "BufferStart" << g_index << std::endl;
		}

		void OnBufferEnd(void* ctx)override
		{
			FillNextBuffer();
		}
	};

	Audio::XAudio xAudio;
	xAudio.Initialize();

	soundCallback callBack{};
	
	Library::Sound::Loader::WavLoader loader;
	Audio::XAudio::WaveAudioData data{};
	data.pcmData = loader.Load("Resource/bgm7.wav", &data.waveFormat);
	xAudio.ProcessWaveBuffer(data.pcmData, &data.buffer, &data.bufferSize);
	g_audioBuffers = xAudio.CreateAudioBuffers(data.buffer, data.bufferSize, data.waveFormat.nChannels, data.waveFormat.nBlockAlign);
	g_sourceVoice = xAudio.CreateSourceVoiceWAV(data.waveFormat, &callBack);

	FillNextBuffer();

	g_sourceVoice->Start();
#endif


	while (winSystem.DispatchWindowMessage())
	{
		if (g_stopFlag)
			g_sourceVoice->Stop();
	}

	return 0;
}

HWND GethWnd(void)
{
	return g_hWnd;
}