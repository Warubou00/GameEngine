
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
	// commands : C# の描画パス (RenderPipeline) が組み立てた命令の列 (RHI::RenderCommand × commandCount)
	// 以前は引数が無く、シーンをバックバッファに描くだけだった
	RENDERER_API void Renderer_Render(const RHI::RenderCommand* commands, uint32_t commandCount)
	{
		if (g_renderer) g_renderer->Render(commands, commandCount);
	}

	// ==========================================================================
	// 描画パス (レンダーターゲット / 全画面のシェーダー) とライト
	// ==========================================================================

	// レンダーターゲットを作る。戻り値はハンドル (失敗時 INVALID_HANDLE)
	RENDERER_API uint32_t Renderer_CreateRenderTarget(const RHI::RenderTargetDesc* desc)
	{
		return (g_renderer && desc) ? g_renderer->CreateRenderTarget(*desc).Index : Renderer::INVALID_HANDLE;
	}

	RENDERER_API void Renderer_DestroyRenderTarget(uint32_t handle)
	{
		RHI::RenderTargetHandle target;
		target.Index = handle;
		if (g_renderer) g_renderer->DestroyRenderTarget(target);
	}

	// 確認用の線 (2 頂点で 1 本)。毎フレーム呼ぶ (0 本で消える)
	RENDERER_API void Renderer_SetDebugLines(const RHI::DebugLineVertex* vertices, uint32_t count)
	{
		if (g_renderer) g_renderer->SetDebugLines(vertices, vertices ? count : 0);
	}

	// 影 (平行光源 1 つのシャドーマップ)。変わった時だけ呼べばよい
	RENDERER_API void Renderer_SetShadowSettings(const RHI::ShadowSettings* settings)
	{
		if (g_renderer && settings) g_renderer->SetShadowSettings(*settings);
	}

	// MSAA で使えるサンプル数の上限 (1 なら MSAA は使えない)
	RENDERER_API uint32_t Renderer_GetMaxSampleCount()
	{
		return g_renderer ? g_renderer->GetMaxSampleCount() : 1;
	}

	// 全画面のシェーダー (filePath の VSFullscreen と entry)。戻り値はハンドル (失敗時 INVALID_HANDLE)
	RENDERER_API uint32_t Renderer_LoadFullscreenShader(const wchar_t* filePath, const char* entry)
	{
		return (g_renderer && filePath && entry) ? g_renderer->LoadFullscreenShader(filePath, entry).Index : Renderer::INVALID_HANDLE;
	}

	// ライト (変わった時だけ呼べばよい)
	RENDERER_API void Renderer_SetLights(const RHI::LightData* lights, uint32_t count)
	{
		if (g_renderer) g_renderer->SetLights(lights, count);
	}

	RENDERER_API void Renderer_SetLightingSettings(const RHI::LightingSettings* settings)
	{
		if (g_renderer && settings) g_renderer->SetLightingSettings(*settings);
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

	// モデルの形 (位置 xyz とインデックス) を写す (衝突判定のメッシュ用)。positions / indices が nullptr なら数だけ
	RENDERER_API bool Renderer_GetModelGeometry(uint32_t model, float* positions, uint32_t maxVertices, uint32_t* indices, uint32_t maxIndices,
	                                            uint32_t* outVertexCount, uint32_t* outIndexCount)
	{
		if (outVertexCount) *outVertexCount = 0;
		if (outIndexCount)  *outIndexCount  = 0;
		return g_renderer ? g_renderer->GetModelGeometry(model, positions, maxVertices, indices, maxIndices, outVertexCount, outIndexCount) : false;
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

	// プロファイラー (F1) に任意の文字列を表示する (例 : 描画パイプラインの名前)
	RENDERER_API void Renderer_SetProfilerInfo(const char* name, const char* text)
	{
		if (g_renderer && name && text) g_renderer->SetProfilerInfo(name, text);
	}
}

/************************************
* End of file
************************************/
