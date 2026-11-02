
/***********************************************************************************
* @file    rendererDll.cpp
* @brief   Renderer.dll が C# のエンジン (GameEngine.Runtime) へ export する関数
* @details C# から呼ぶ入口はこの DLL だけ。C# 側の宣言は GameEngine.Runtime/Native/NativeMethods.cs
*          - モデル・描画オブジェクトはハンドル (uint32) でやり取りする。ポインタは C# に渡さない
*          - 1 フレームに何度も使う操作は「まとめて」渡す (P/Invoke の回数を増やさない)
*          - 構造体の並びは C# 側と一致させること
*          以前は Renderer_TestLoadModel / Renderer_TestSpawnGrid / Renderer_TestGroundView などの
*          テスト専用の関数で、配置やカメラを C++ に直書きしていた (エンジン化 E1 で削除)
*
* @author  Keigo Onari
* @date    2026/11/01
* @version v_1.1.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/


/******************************************************************
* Include Headers
******************************************************************/
#include "common.h"
#include "sceneRenderer.h"

#include "../../Core/Source/Log/logger.h"

namespace
{
	// C# の Native.WindowDesc と同じ並び (IntPtr / uint / uint / uint)
	struct WindowInfo
	{
		void*    HWnd;
		uint32_t Width;
		uint32_t Height;
		uint32_t EnableVSync;
	};

	// C# の Native.InitializeResult と同じ並び (int × 5)。RHI::InitializeResult もこの並び
	static_assert(sizeof(RHI::InitializeResult) == sizeof(int) * 5, "InitializeResult must match C# Native.InitializeResult");
	static_assert(sizeof(Renderer::CameraDesc) == sizeof(float) * 12 + sizeof(uint32_t), "CameraDesc must match C# Native.CameraDesc");

	Renderer::SceneRenderer* g_renderer = nullptr;
}

extern "C"
{
	// ==============================================================
	// 初期化 / 終了 / 画面
	// ==============================================================

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

	RENDERER_API void Renderer_CleanUp()
	{
		if (g_renderer == nullptr)
			return;

		g_renderer->Shutdown();
		delete g_renderer;
		g_renderer = nullptr;
	}

	// ウィンドウのクライアント領域のサイズが変わった時に呼ぶ (最小化中の 0 は無視される)
	RENDERER_API bool Renderer_Resize(uint32_t width, uint32_t height)
	{
		return g_renderer ? g_renderer->Resize(width, height) : false;
	}

	// 1 フレーム描画する (描画オブジェクトの位置やカメラは、先に設定しておく)
	RENDERER_API void Renderer_Render()
	{
		if (g_renderer) g_renderer->Render();
	}

	// GPU の処理 (描画 + 転送) が全て終わるまで待つ (読み込み時間の計測など)
	RENDERER_API void Renderer_WaitForGpu()
	{
		if (g_renderer) g_renderer->WaitForGpu();
	}

	// ==============================================================
	// モデル / 描画オブジェクト
	// ==============================================================

	// OBJ モデルを読み込む。戻り値 = モデルのハンドル (失敗時 0xFFFFFFFF)
	RENDERER_API uint32_t Renderer_LoadModel(const wchar_t* filePath, uint32_t* outSubMeshCount)
	{
		if (outSubMeshCount) *outSubMeshCount = 0;
		if (g_renderer == nullptr || filePath == nullptr) return Renderer::INVALID_HANDLE;
		return g_renderer->LoadModel(filePath, outSubMeshCount);
	}

	// モデルを置く描画オブジェクトを count 個まとめて作る (原点に置かれる)
	RENDERER_API void Renderer_CreateRenderObjects(uint32_t model, uint32_t count, uint32_t* outHandles)
	{
		if (g_renderer) g_renderer->CreateRenderObjects(model, count, outHandles);
	}

	// 描画オブジェクトをまとめて削除する
	RENDERER_API void Renderer_DestroyRenderObjects(const uint32_t* handles, uint32_t count)
	{
		if (g_renderer) g_renderer->DestroyRenderObjects(handles, count);
	}

	// 描画オブジェクトのワールド行列をまとめて設定する (動いたものだけ。matrices = 16 float × count)
	RENDERER_API void Renderer_SetRenderObjectTransforms(const uint32_t* handles, const float* matrices, uint32_t count)
	{
		if (g_renderer) g_renderer->SetRenderObjectTransforms(handles, matrices, count);
	}

	// ==============================================================
	// カメラ / 設定 / プロファイラー
	// ==============================================================

	RENDERER_API void Renderer_SetCamera(const Renderer::CameraDesc* camera)
	{
		if (g_renderer && camera) g_renderer->SetCamera(*camera);
	}

	// ExecuteIndirect で描くか (false なら描画ごとに DrawIndexedInstanced)
	RENDERER_API void Renderer_SetIndirectDraw(bool enabled)
	{
		if (g_renderer) g_renderer->SetIndirectDrawEnabled(enabled);
	}

	// インスタンシングするか (同じメッシュを 1 回の描画にまとめる)
	RENDERER_API void Renderer_SetInstancing(bool enabled)
	{
		if (g_renderer) g_renderer->SetInstancingEnabled(enabled);
	}

	// GPU カリングするか (ExecuteIndirect の時だけ効く)
	RENDERER_API void Renderer_SetGpuCulling(bool enabled)
	{
		if (g_renderer) g_renderer->SetGpuCullingEnabled(enabled);
	}

	// オクルージョンカリングするか (GPU カリングの時だけ効く)
	RENDERER_API void Renderer_SetOcclusionCulling(bool enabled)
	{
		if (g_renderer) g_renderer->SetOcclusionCullingEnabled(enabled);
	}

	// プロファイラー (F1) に任意の値を表示する。C# の処理時間などに使う
	RENDERER_API void Renderer_ReportProfilerValue(const char* name, double value)
	{
		if (g_renderer && name) g_renderer->ReportProfilerValue(name, value);
	}
}

/************************************
* End of file
************************************/
