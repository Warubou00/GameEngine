
/***********************************************************************************
* @file    sceneRenderer.h
* @brief   シーンの描画 (モデル・描画オブジェクト・カメラ・描画リストの管理)
* @details RHI (GPU 層) の上に乗る層。C# のエンジンから「ハンドル」で操作される。
*
*            C# エンジン (GameEngine.Runtime : ECS / 入力 / カメラ / スクリプト)
*               │ P/Invoke (rendererDll.cpp。ハンドルと、まとめて渡す関数だけ)
*               ▼
*            Renderer::SceneRenderer   … モデル (メッシュ + テクスチャ) / 描画オブジェクト / カメラ → DrawItem
*               │ RHI::IRenderHardwareInterface (Public/rhi.h)
*               ▼
*            RHI.dll                   … GPU (バッファ / テクスチャ / パイプライン / GPU 駆動の描画)
*
*          以前は「最後に読み込んだモデル 1 個・カメラ 1 個」を中で抱えていて、負荷テストの格子や
*          テスト用のカメラも C++ に直書きだった (Renderer_TestXXX)。
*          今はモデルや描画オブジェクトを何個でも作れ、配置やカメラは C# が決める (エンジン化 E1)
*
* @author  Keigo Onari
* @date    2026/11/01
* @version v_1.1.0
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
#include "../../RHI/Source/Public/rhi.h"

namespace Renderer
{
	// ハンドルが無効な時の値 (C# の Native.InvalidHandle と同じ)
	constexpr uint32_t INVALID_HANDLE = 0xFFFFFFFFu;

	// ==========================================================================
	// カメラ (C# の Native.CameraDesc と同じ並び : float ×12 + uint)
	// 行列は C++ 側で作る (アスペクト比は描画先のサイズから決めるので、リサイズしても C# は何もしなくてよい)
	// 座標系は左手系 (+X 右 / +Y 上 / +Z 奥)
	// ==========================================================================
	struct CameraDesc
	{
		float    Position[3];
		float    Forward[3];       // 見ている向き (正規化していなくてよい)
		float    Up[3];
		float    FieldOfViewY;     // 縦の視野角 (ラジアン)
		float    NearZ;
		float    FarZ;
		uint32_t Orthographic;     // 1 : 平行投影 (幅 / 高さ = 描画先のピクセル数)
	};

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

		// ==========================================================
		// モデル (メッシュ + テクスチャ。描画オブジェクトの元)
		// ==========================================================

		/***********************************************************************************
		 * @brief モデルを読み込む (拡張子で選ぶ : .obj / .pmx)
		 * @details 頂点・インデックス・テクスチャ・マテリアルを RHI に登録する。まだ描画はされない (CreateRenderObjects で置く)
		 *          GPU への転送は次の Render でまとめて行われる (ここでは待たない)
		 *          テクスチャのファイルが無い時は、同じ名前で拡張子の違う画像を探す (.png / .jpg など)
		 * @return uint32_t モデルのハンドル (失敗時 INVALID_HANDLE)。outSubMeshCount にサブメッシュの数
		 ************************************************************************************/
		uint32_t LoadModel(const std::wstring& filePath, uint32_t* outSubMeshCount);

		// ==========================================================
		// 描画オブジェクト (モデル 1 個を 1 か所に置いたもの)
		// ==========================================================

		/***********************************************************************************
		 * @brief 描画オブジェクトをまとめて作る (原点に置かれる。位置は SetRenderObjectTransforms で)
		 * @details RHI のオブジェクト (ワールド行列の置き場) を 1 個作り、全サブメッシュで共有する
		 *          (以前はサブメッシュごとに作り、テクスチャもオブジェクトが持っていた。今はマテリアルが持つ)
		 * @param[out] outHandles count 個のハンドル (作れなかったものは INVALID_HANDLE)
		 ************************************************************************************/
		void CreateRenderObjects(uint32_t model, uint32_t count, uint32_t* outHandles);

		// 描画オブジェクトをまとめて削除する (ハンドルは使い回される)
		void DestroyRenderObjects(const uint32_t* handles, uint32_t count);

		/***********************************************************************************
		 * @brief 描画オブジェクトのワールド行列をまとめて設定する
		 * @details 動いたものだけを渡せばよい。matrices は 16 float (行優先、行ベクトル v * M) × count
		 *          (C# の System.Numerics.Matrix4x4 / DirectXMath の XMFLOAT4X4 と同じ並び)
		 *          以前は C# から 1 個ずつ設定する手段が無かった (C++ の負荷テストの中で置いていた)
		 ************************************************************************************/
		void SetRenderObjectTransforms(const uint32_t* handles, const float* matrices, uint32_t count);

		// ==========================================================
		// カメラ / 描画
		// ==========================================================

		// カメラの設定 (View / Projection 行列を作って RHI に渡す)
		void SetCamera(const CameraDesc& camera);

		/***********************************************************************************
		 * @brief 1 フレーム描画する (描画オブジェクトが増減していたら、描画リストを作り直してから渡す)
		 * @param commands C# の描画パスが組み立てた命令 (描画先 / クリア / シーン / 全画面)
		 *        以前は引数が無く、シーンをバックバッファに 1 回描くだけだった
		 ************************************************************************************/
		bool Render(const RHI::RenderCommand* commands, uint32_t commandCount);

		// 描画パス用 (RHI に渡すだけ)
		RHI::RenderTargetHandle     CreateRenderTarget(const RHI::RenderTargetDesc& desc);
		void                        DestroyRenderTarget(RHI::RenderTargetHandle handle);
		uint32_t                    GetMaxSampleCount() const;
		RHI::FullscreenShaderHandle LoadFullscreenShader(const wchar_t* filePath, const char* entry);
		void SetLights(const RHI::LightData* lights, uint32_t count);
		void SetLightingSettings(const RHI::LightingSettings& settings);
		void SetShadowSettings(const RHI::ShadowSettings& settings);
		void SetDebugLines(const RHI::DebugLineVertex* vertices, uint32_t count);
		void SetProfilerInfo(const char* name, const char* text);

		// GPU の処理が全て終わるまで待つ (計測用)
		void WaitForGpu();

		// ExecuteIndirect / インスタンシング / GPU カリング / オクルージョンカリング (RHI に渡すだけ)
		void SetIndirectDrawEnabled(bool enabled);
		void SetInstancingEnabled(bool enabled);
		void SetGpuCullingEnabled(bool enabled);
		void SetOcclusionCullingEnabled(bool enabled);

		// プロファイラー (RHI の ImGui ウィンドウ) に値を表示する
		void ReportProfilerValue(const char* name, double value);

		bool IsInitialized()const { return _rhi && _rhi->IsInitialized(); }

	private:
		// モデル : サブメッシュごとの DrawItem の元 (メッシュの範囲とマテリアル。Object は未設定)
		struct ModelResource
		{
			std::vector<RHI::DrawItem> SubMeshes;
		};

		// 描画オブジェクト : どのモデルを、どの RHI のオブジェクト (ワールド行列) で描くか
		struct RenderObject
		{
			uint32_t          Model = INVALID_HANDLE;   // INVALID_HANDLE なら空き
			RHI::ObjectHandle Object;                   // 全サブメッシュで共有する
		};

		// テクスチャのファイルが無ければ、拡張子を変えて探す (見つからなければそのまま返す)
		static std::wstring resolveTexturePath(const std::wstring& path);

		// 行列を作り直して RHI に渡す (カメラ変更・リサイズ時)
		void updateViewProjection();

		// 描画オブジェクトから描画リストを作り直す (増減した時だけ)
		void rebuildDrawList();

		RHI::IRenderHardwareInterface* _rhi = nullptr;          // RHI_CreateRenderHardwareInterface で作り、Shutdown で破棄する

		std::vector<ModelResource>     _models;
		std::vector<RenderObject>      _renderObjects;
		std::vector<uint32_t>          _freeRenderObjects;      // 削除されて使い回せるハンドル
		uint32_t                       _aliveRenderObjects = 0;

		std::vector<RHI::DrawItem>     _drawItems;              // 毎フレーム RHI に渡す描画リスト
		bool                           _drawListDirty = false;  // 描画オブジェクトが増減した

		CameraDesc                     _camera    = {};
		bool                           _hasCamera = false;
	};
}

#endif // RENDERER_SCENE_RENDERER_H_
