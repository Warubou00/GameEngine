
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
	// v1.5 : 読み込んだモデルのサブメッシュのマテリアル (C# の NativeMethods.ModelMaterialInfo と同じ並び : 28byte)
	struct ModelMaterialInfo
	{
		float    Color[4]  = { 1.0f, 1.0f, 1.0f, 1.0f };
		float    Metallic  = 0.0f;
		float    Roughness = 0.6f;
		uint32_t Flags     = 0;   // 1 半透明 / 2 両面
	};
	static_assert(sizeof(ModelMaterialInfo) == 28, "ModelMaterialInfo must match C#");

	struct CameraDesc
	{
		float    Position[3];
		float    Forward[3];       // 見ている向き (正規化していなくてよい)
		float    Up[3];
		float    FieldOfViewY;     // 縦の視野角 (ラジアン)
		float    NearZ;
		float    FarZ;
		uint32_t Orthographic;     // 1 : 平行投影 (幅 / 高さ = 描画先のピクセル数)
		float    Aspect;           // v1.5 : 幅 / 高さ (0 = 画面の比。カメラの範囲 Viewport Rect の比)
		uint32_t CullingMask;      // v1.5 : 写すレイヤーのビット (0xFFFFFFFF = 全部)
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
		/***********************************************************************************
		 * @brief 頂点とインデックスの配列からモデルを作る (SH1 : プリミティブ / コードで作る形)
		 * @details サブメッシュ 1 つ、白い不透明のマテリアル。name はモデルの名前 ("builtin:Sphere" など。空にしない)
		 *          衝突判定のメッシュ用の形も残す (LoadModel と同じ)。読み直し (ReloadModel) はできない
		 * @return uint32_t モデルのハンドル (失敗時 INVALID_HANDLE)
		 ************************************************************************************/
		uint32_t CreateMesh(const RHI::Vertex* vertices, uint32_t vertexCount, const uint32_t* indices, uint32_t indexCount, const std::wstring& name);

		// AN2 : サブメッシュ 1 つ分 (C# の ModelSubMeshDesc と同じ並び : 40 byte)
		struct ModelSubMeshDesc
		{
			uint32_t FirstIndex;   // indices の何番目から
			uint32_t IndexCount;
			float    Color[4];     // テクスチャがあれば掛ける色 (a = 不透明度)
			float    Metallic;
			float    Roughness;
			uint32_t Flags;        // bit0 : 半透明 / bit1 : 両面
			uint32_t Padding;
		};
		/***********************************************************************************
		 * @brief 頂点 / インデックス / サブメッシュ (マテリアル付き) からモデルを作る (AN2 : FBX のモデル。C# が読んで作った形)
		 * @details 頂点は全部で 1 回、サブメッシュごとにインデックスの範囲とマテリアル。texturePaths[i] はサブメッシュ i のテクスチャ
		 *          (空 / nullptr なら無し。読み込みは LoadModel と同じくまとめて)。replace が有効な番号なら、その番号のまま入れ替える
		 *          (置いてある物は次のフレームから新しい形。読み直しの代わり)。name は空にしない
		 * @return uint32_t モデルのハンドル (失敗時 INVALID_HANDLE。入れ替えに失敗した時は前の物のまま)
		 ************************************************************************************/
		uint32_t CreateModel(const RHI::Vertex* vertices, uint32_t vertexCount, const uint32_t* indices, uint32_t indexCount,
		                     const ModelSubMeshDesc* subMeshes, uint32_t subMeshCount, const wchar_t* const* texturePaths,
		                     const std::wstring& name, uint32_t replace, uint32_t* outSubMeshCount);
		// ---------- AN4 : GPU スキニング ----------
		// モデルに骨と重みを付ける (頂点の数と同じ数)。boneCount = 骨の行列の数。置いてある物の曲げ方も作り直す
		bool SetModelSkin(uint32_t model, const RHI::SkinVertex* vertices, uint32_t count, uint32_t boneCount);
		// 描画オブジェクトを曲げる / 曲げない (モデルに骨が無ければ何もしない)
		void SetObjectsSkinned(const uint32_t* handles, uint32_t count, bool skinned);
		// 姿勢 : オブジェクトごとに 骨の行列 (1 本 12 個) を boneCounts[i] 本、境界球 (4 個) を 1 個
		void SetSkinPoses(const uint32_t* handles, uint32_t count, const float* matrices, const uint32_t* boneCounts, const float* spheres);
		void GetSkinStats(uint32_t* outInstances, uint32_t* outVertices, float* outGpuMs)const;

		// ---------- プレビューの窓 (AN5) ----------
		// 描画オブジェクトを視点 view (0 = シーン / 1〜 = プレビュー) に出す
		void SetObjectsView(const uint32_t* handles, uint32_t count, uint32_t view);
		void SetPreviewFrame(uint32_t view, const RHI::PreviewFrame& frame, const RHI::DebugLineVertex* lines, uint32_t lineVertexCount,
		                     uint32_t depthLineVertexCount);
		uint32_t GetRenderTargetTexture(uint32_t target)const;
		// 確認用 : 描画リストの数 (view 0 = シーン / 1〜 = 窓) / 生きている描画オブジェクトの数
		uint32_t GetDrawItemCount(uint32_t view);
		uint32_t GetRenderObjectCount()const { return _aliveRenderObjects; }
		// 確認用 : 描画オブジェクトの曲げた頂点を読み戻す (Request の数フレーム後に Read)
		void     RequestSkinReadback(uint32_t handle);
		uint32_t ReadSkinReadback(RHI::Vertex* outVertices, uint32_t maxCount);

		// RHI をそのまま使う機能 (SH2 : 自分のシェーダー / 3D テクスチャ)。初期化前は nullptr
		RHI::IRenderHardwareInterface* GetRHI()const { return IsInitialized() ? _rhi : nullptr; }
		// モデルの形 (位置とインデックス) を写す。positions / indices が nullptr なら数だけ返す (衝突判定のメッシュ用)
		bool GetModelGeometry(uint32_t model, float* positions, uint32_t maxVertices, uint32_t* indices, uint32_t maxIndices,
		                      uint32_t* outVertexCount, uint32_t* outIndexCount) const;
		// v1.5 : サブメッシュ s の読み込んだマテリアル (色 / 金属 / 粗さ / 印 / テクスチャのフルパス / 名前)。C# がマテリアルのアセットを作る
		bool GetModelMaterialInfo(uint32_t model, uint32_t subMesh, ModelMaterialInfo* info, std::wstring& texture, std::wstring& name) const;

		// ---------- ED5 : エディターの読み直し / 解放 / マテリアル (GPU を待つ物があるので、毎フレーム呼ぶ物ではない) ----------
		// 同じファイルを読み直す (番号は同じまま。置いてある物は次のフレームから新しい形で描かれる)。読めなければ前の物のまま
		bool     ReloadModel(uint32_t model, uint32_t* outSubMeshCount);
		// モデルを解放する (頂点 / インデックス / マテリアル / テクスチャを返す)。置いてある物があれば解放しない (false)
		bool     UnloadModel(uint32_t model);
		// そのテクスチャを使っているモデルを読み直す。戻り値 = 読み直した数
		uint32_t ReloadModelsUsingTexture(const std::wstring& texturePath);
		// マテリアルのアセット用のテクスチャ (0 = 読めなかった / 既定の白)
		uint32_t LoadTexture(const std::wstring& filePath);
		void     ReleaseTexture(uint32_t texture);
		uint32_t CreateMaterial(const RHI::MaterialDesc& desc);
		void     UpdateMaterial(uint32_t material, const RHI::MaterialDesc& desc);
		void     DestroyMaterial(uint32_t material);
		// 描画オブジェクトのマテリアルを変える (INVALID_HANDLE = モデルのマテリアルに戻す)。全部のサブメッシュに使う
		void     SetRenderObjectMaterial(uint32_t object, uint32_t material);
		uint64_t GetMeshBytesUsed() const;
		uint32_t GetLoadedModelCount() const;
		bool     ReloadShaders() { return IsInitialized() && _rhi->ReloadShaders(); }

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
		// v1.5 : 複数のカメラ (描画パスの SetCamera(番号) で切り替える。0 番がフレームの始めのカメラ)
		void SetCameras(const CameraDesc* cameras, uint32_t count);
		// v1.5 : 物ごとの印 (RHI::OBJECT_FLAG_*) と、レイヤーのビット (カメラの Culling Mask と比べる)
		void SetRenderObjectFlags(uint32_t object, uint32_t flags, uint32_t layerMask);
		// v1.5 : サブメッシュごとのマテリアル (materials[s] が s 番のサブメッシュ。0xFFFFFFFF = モデルのマテリアル)
		void SetRenderObjectMaterials(uint32_t object, const uint32_t* materials, uint32_t count);

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
		// エディター (ED3) : シーンを描く大きさ / UI
		bool     SetRenderSize(uint32_t width, uint32_t height);
		void     SetUIDrawData(const RHI::UIVertex* vertices, uint32_t vertexCount, const uint16_t* indices, uint32_t indexCount,
		                       const RHI::UIDrawCommand* commands, uint32_t commandCount);
		uint32_t CreateUITexture(uint32_t width, uint32_t height, const uint8_t* rgba);
		// GPU パーティクル (RHI に渡すだけ)
		void SetParticleCapacity(uint32_t capacity);
		void SetParticleEmitters(const RHI::ParticleEmitterData* emitters, uint32_t count, const RHI::ParticleFrameSettings& settings);
		RHI::ParticleStats GetParticleStats() const;
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
		void SetProfilerVisible(bool visible) { if (_rhi) _rhi->SetProfilerVisible(visible); }   // ED3 (エディターは最初は隠す)
		bool IsProfilerVisible() const { return _rhi && _rhi->IsProfilerVisible(); }

		bool IsInitialized()const { return _rhi && _rhi->IsInitialized(); }

	private:
		// モデル : サブメッシュごとの DrawItem の元 (メッシュの範囲とマテリアル。Object は未設定)
		struct ModelResource
		{
			std::vector<RHI::DrawItem> SubMeshes;
			// 衝突判定のメッシュ用に残す CPU 側の形 (位置 xyz と、全サブメッシュをつないだインデックス)
			// 以前は GPU へ送った後に CPU 側のメッシュを全部捨てていた
			std::vector<float>    Positions;
			std::vector<uint32_t> Indices;
			// ED5 : 読み直し / 解放のために、使った物を覚える (Path が空 = 空いている番号)
			std::wstring                     Path;
			RHI::VertexRange                 Vertices;
			std::vector<RHI::IndexRange>     IndexRanges;
			std::vector<RHI::MaterialHandle> Materials;
			std::vector<RHI::TextureHandle>  Textures;
			std::vector<std::wstring>        TexturePaths;
			// v1.5 : サブメッシュごとの読み込んだマテリアル (SubMeshes と同じ並び)
			struct SubMeshMaterial
			{
				ModelMaterialInfo Info;
				std::wstring      Texture;   // フルパス (無ければ空)
				std::wstring      Name;
			};
			std::vector<SubMeshMaterial>     SubMeshMaterials;
			// AN4 : 骨と重み (曲げるモデルだけ)
			RHI::SkinRange                   Skin;
			uint32_t                         SkinBones = 0;
		};

		// 描画オブジェクト : どのモデルを、どの RHI のオブジェクト (ワールド行列) で描くか
		struct RenderObject
		{
			uint32_t          Model = INVALID_HANDLE;   // INVALID_HANDLE なら空き
			RHI::ObjectHandle Object;                   // 全サブメッシュで共有する
			uint32_t          MaterialOverride = INVALID_HANDLE;   // ED5 : マテリアルのアセット (INVALID_HANDLE ならモデルのマテリアル)
			RHI::SkinHandle   Skin;                                // AN4 : 曲げる物 (有効なら、その物専用の頂点の場所で描く)
			uint32_t          View = 0;                            // AN5 : 0 = シーン / 1〜 = プレビューの窓 (シーンには描かない)
			std::vector<uint32_t> MaterialOverrides;               // v1.5 : サブメッシュごと (無い所は MaterialOverride か、モデルのマテリアル)
			RHI::ObjectConstants  Constants;                       // v1.5 : 最後に送った行列と印 (印だけ変えた時にも行列を送り直すため)
		};

		// テクスチャのファイルが無ければ、拡張子を変えて探す (見つからなければそのまま返す)
		static std::wstring resolveTexturePath(const std::wstring& path);

		// 行列を作り直して RHI に渡す (カメラ変更・リサイズ時)
		void updateViewProjection();

		// 描画オブジェクトから描画リストを作り直す (増減した時だけ)
		void rebuildDrawList();

		bool loadModelResource(const std::wstring& filePath, ModelResource& model);
		void releaseModelResource(ModelResource& model);

		RHI::IRenderHardwareInterface* _rhi = nullptr;          // RHI_CreateRenderHardwareInterface で作り、Shutdown で破棄する

		std::vector<ModelResource>     _models;
		static ModelResource::SubMeshMaterial subMeshMaterialOf(const RHI::MaterialDesc& desc, const std::wstring& texture, const std::wstring& name);   // v1.5
		std::vector<uint32_t>          _freeModels;             // 解放したモデルの番号 (ED5)
		std::vector<RenderObject>      _renderObjects;
		std::vector<uint32_t>          _freeRenderObjects;      // 削除されて使い回せるハンドル
		uint32_t                       _aliveRenderObjects = 0;

		std::vector<RHI::DrawItem>     _drawItems;              // 毎フレーム RHI に渡す描画リスト
		std::vector<RHI::DrawItem>     _previewItems[RHI::MAX_PREVIEW_VIEWS];   // AN5 : 窓ごとの描画リスト (0 番は使わない)
		bool                           _drawListDirty = false;  // 描画オブジェクトが増減した

		CameraDesc                     _camera    = {};
		std::vector<CameraDesc>        _cameras;                // v1.5 : 全部のカメラ (0 番 = _camera)
		bool                           _hasCamera = false;
	};
}

#endif // RENDERER_SCENE_RENDERER_H_
