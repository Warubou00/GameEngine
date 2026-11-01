
/***********************************************************************************
* @file    rendererDll.cpp
* @brief   Renderer.dll が C# ホストへ export する関数
* @details C# から呼ぶ入口はこの DLL だけ (Phase 3 で RHI.dll から移した)。
*          構造体の並びは C# 側 (RHI.cs の RHIInfo / RHIInitializedInfo) と一致させること
*
* @author  Keigo Onari
* @date    2026/11/01
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/


/******************************************************************
* Include Headers
******************************************************************/
#include "common.h"
#include "sceneRenderer.h"
#include "camera.h"

#include "../../Core/Source/Log/logger.h"
#include "../../Core/Source/Log/scopedTimer.h"

namespace
{
	// C# の RHIInfo と同じ並び (IntPtr / int / int / int)
	struct WindowInfo
	{
		void*    HWnd;
		uint32_t Width;
		uint32_t Height;
		uint32_t EnableVSync;
	};

	// C# の RHIInitializedInfo と同じ並び (int × 5)。RHI::InitializeResult もこの並び
	static_assert(sizeof(RHI::InitializeResult) == sizeof(int) * 5, "InitializeResult must match C# RHIInitializedInfo");

	Renderer::SceneRenderer* g_renderer = nullptr;
}

extern "C"
{
	RENDERER_API void Renderer_SetLogCallback(LogCallback callback, WLogCallback wcallback)
	{
		// Logger は Core.dll にあるので、RHI.dll と Renderer.dll の両方のログがここに流れる
		Core::Logger::Initialize(callback, wcallback);
	}

	RENDERER_API void Renderer_Initialize(RHI::InitializeResult* outResult, const WindowInfo* windowInfo)
	{
		if (!windowInfo) return;

		if (g_renderer == nullptr)
			g_renderer = new Renderer::SceneRenderer();

		RHI::InitializeDesc desc;
		desc.WindowHandle = windowInfo->HWnd;
		desc.Width        = windowInfo->Width;
		desc.Height       = windowInfo->Height;
		desc.EnableVSync  = windowInfo->EnableVSync != 0;

		RHI::InitializeResult result;
		g_renderer->Initialize(desc, &result);
		if (outResult) *outResult = result;
	}

	RENDERER_API void Renderer_Render()
	{
		if (g_renderer == nullptr)
			return;

		g_renderer->Render();
	}

	// ウィンドウのクライアント領域のサイズが変わった時に呼ぶ (最小化中の 0 は無視される)
	RENDERER_API bool Renderer_Resize(uint32_t width, uint32_t height)
	{
		if (g_renderer == nullptr)
			return false;

		return g_renderer->Resize(width, height);
	}

	// テスト用 : box.obj を読み込み、指定位置から原点を見るカメラを置く
	RENDERER_API void Renderer_TestLoadModel(float cameraPosX, float cameraPosY, float cameraPosZ)
	{
		if (g_renderer == nullptr)
			return;

		double loadMs = 0.0;
		{
			// 高速化の効果確認用 : 読み込み開始 → GPU へのアップロード完了までの時間
			// (計測のために GPU の完了を待っている。通常の読み込みでは待たない)
			Core::ScopedTimer timer("LoadModel (until GPU upload complete)");
			g_renderer->LoadModel(L"../Model/box.obj");
			g_renderer->WaitForGpu();
			loadMs = timer.ElapsedMs();
		}
		// ログだけでなくプロファイラーにも出す
		g_renderer->ReportProfilerValue("LoadModel (ms)", loadMs);

		Renderer::Camera camera;
		camera.Position = Core::Vector3(cameraPosX, cameraPosY, cameraPosZ);
		camera.Target   = Core::Vector3(0.0f, 0.0f, 0.0f);
		camera.Up       = Core::Vector3(0.0f, 1.0f, 0.0f);
		g_renderer->SetCamera(camera);
	}

	// テスト用 : 読み込んだモデルを格子状に count 個並べる (描画数を増やして CPU / GPU の負荷を見る)
	RENDERER_API void Renderer_TestSpawnGrid(uint32_t count)
	{
		if (g_renderer == nullptr)
			return;

		g_renderer->SpawnTestGrid(count);
	}

	// ExecuteIndirect で描くか (false なら描画ごとに DrawIndexedInstanced)
	RENDERER_API void Renderer_SetIndirectDraw(bool enabled)
	{
		if (g_renderer == nullptr)
			return;

		g_renderer->SetIndirectDrawEnabled(enabled);
	}

	// インスタンシングするか (同じメッシュを 1 回の描画にまとめる)
	RENDERER_API void Renderer_SetInstancing(bool enabled)
	{
		if (g_renderer == nullptr)
			return;

		g_renderer->SetInstancingEnabled(enabled);
	}

	// GPU カリングするか (ExecuteIndirect の時だけ効く)
	RENDERER_API void Renderer_SetGpuCulling(bool enabled)
	{
		if (g_renderer == nullptr)
			return;

		g_renderer->SetGpuCullingEnabled(enabled);
	}

	// 負荷テスト用 : 格子の中から前を見るカメラ (カリングの効果を測る)
	RENDERER_API void Renderer_TestInsideView()
	{
		if (g_renderer == nullptr)
			return;

		g_renderer->SetTestInsideView();
	}

	RENDERER_API void Renderer_CleanUp()
	{
		if (g_renderer == nullptr)
			return;

		g_renderer->Shutdown();
		delete g_renderer;
		g_renderer = nullptr;
	}
}

/************************************
* End of file
************************************/
