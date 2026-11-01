
/***********************************************************************************
* @file    sceneRenderer.h
* @brief   シーンの描画 (モデル・カメラ・描画リストの管理)
* @details RHI (GPU 層) の上に乗る層。モデルの読み込み・カメラの行列作り・描画リストの作成を受け持つ。
*
*            C# ホスト
*               │ P/Invoke (rendererDll.cpp)
*               ▼
*            Renderer::SceneRenderer   … OBJ の読み込み / カメラ / マテリアル → DrawItem
*               │ RHI::IRenderHardwareInterface (Public/rhi.h)
*               ▼
*            RHI.dll                   … GPU (バッファ / テクスチャ / パイプライン / 描画)
*
*          以前 (Phase 2 まで) はモデル読み込みやカメラを RHI の中で扱っていたため、
*          RHI が Renderer に依存する逆向きの構造になっていた (レビュー 3-2)
*
* @author  Keigo Onari
* @date    2026/11/01
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/

#pragma once
#ifndef RENDERER_SCENE_RENDERER_H_
#define RENDERER_SCENE_RENDERER_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <string>
#include <vector>

#include "common.h"
#include "camera.h"
#include "WVP/projectionMatrix.h"
#include "../../RHI/Source/Public/rhi.h"

namespace Renderer
{
	class SceneRenderer
	{
	public:
		SceneRenderer();
		~SceneRenderer();

		SceneRenderer(const SceneRenderer&) = delete;
		SceneRenderer& operator=(const SceneRenderer&) = delete;

		/***********************************************************************************
		 * @brief RHI を作って初期化する
		 * @return bool 全ての段階に成功したか (各段階の結果は outResult)
		 ************************************************************************************/
		bool Initialize(const RHI::InitializeDesc& desc, RHI::InitializeResult* outResult);

		// GPU の完了を待ってから RHI を破棄する。2 回呼んでもよい
		void Shutdown();

		// 描画先のサイズ変更 (カメラの Projection 行列のアスペクト比も合わせる)
		bool Resize(uint32_t width, uint32_t height);

		/***********************************************************************************
		 * @brief OBJ モデルを読み込んで、描画リストに加える
		 * @details 頂点・インデックス・テクスチャを RHI に登録し、サブメッシュごとに DrawItem を作る。
		 *          GPU への転送は次の Render でまとめて行われる (ここでは待たない)
		 *          読み込み後、CPU 側のメッシュデータは不要なので解放する
		 ************************************************************************************/
		bool LoadModel(const std::wstring& filePath);

		// カメラの設定 (View / Projection 行列を作って RHI に渡す)
		void SetCamera(const Camera& camera, Projection::ProjectionType type = Projection::ProjectionType::PROJECTION_TYPE_3D);

		// 1 フレーム描画する
		bool Render();

		// GPU の処理が全て終わるまで待つ (計測用)
		void WaitForGpu();

		// プロファイラー (RHI の ImGui ウィンドウ) に値を表示する
		void ReportProfilerValue(const char* name, double value);

		bool IsInitialized()const { return _rhi && _rhi->IsInitialized(); }

	private:
		// 行列を作り直して RHI に渡す (カメラ変更・リサイズ時)
		void updateViewProjection();

		RHI::IRenderHardwareInterface* _rhi = nullptr;          // RHI_CreateRenderHardwareInterface で作り、Shutdown で破棄する
		std::vector<RHI::DrawItem>     _drawItems;              // 毎フレーム RHI に渡す描画リスト

		Camera                         _camera         = {};
		Projection::ProjectionType     _projectionType = Projection::ProjectionType::PROJECTION_TYPE_3D;
		bool                           _hasCamera      = false;
	};
}

#endif // RENDERER_SCENE_RENDERER_H_
