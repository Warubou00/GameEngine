
/***********************************************************************************
* @file    rhi.h
* @brief   RHI の公開インターフェイス (RHI.dll を使う側が include するのはこのファイルだけ)
* @details 抽象クラスと、それを作る / 壊す関数だけを export する。
*          - 具象クラス (DirectX12 の実装) は RHI.dll の外に出ない
*          - 仮想関数の呼び出しだけなので、クラスそのものを dllexport しなくてよい
*            (STL のメンバーを持つクラスを export した時の C4251 警告や、ABI の問題を避けられる)
*
*          使い方 (Renderer 側)
*            RHI::IRenderHardwareInterface* rhi = RHI_CreateRenderHardwareInterface();
*            rhi->Initialize(desc, &result);
*            auto vertices = rhi->UploadVertices(...);      // 頂点を GPU へ
*            auto object   = rhi->CreateObject();           // ワールド行列などの置き場 (DestroyObject で削除)
*            rhi->UpdateObject(object, constants);
*            rhi->SetViewProjection(view, projection);
*            rhi->Render(drawItems, count);                  // 毎フレーム
*            RHI_DestroyRenderHardwareInterface(rhi);
*
* @author  Keigo Onari
* @date    2026/11/01
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/

#pragma once
#ifndef RHI_PUBLIC_RHI_H_
#define RHI_PUBLIC_RHI_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "rhiExport.h"
#include "rhiTypes.h"

namespace RHI
{
	// v1.17 (S4) : デコード済みの画像 (中身は RHI の中。DecodeTextures が作り、CreateDecodedTextures / ReleaseDecodedTextures が解放する)
	struct DecodedTextures;

	class IRenderHardwareInterface
	{
	public:
		virtual ~IRenderHardwareInterface() = default;

		// ======================================================================
		// 初期化 / 終了
		// ======================================================================

		/***********************************************************************************
		 * @brief 初期化
		 * @return bool 全ての段階に成功したか (各段階の結果は outResult)
		 ************************************************************************************/
		virtual bool Initialize(const InitializeDesc& desc, InitializeResult* outResult) = 0;

		// GPU の完了を待ってから全て破棄する。2 回呼んでもよい
		virtual void Shutdown() = 0;

		virtual bool IsInitialized()const = 0;

		// ======================================================================
		// 画面
		// ======================================================================

		// 描画先のサイズ変更 (ウィンドウのクライアント領域のサイズ。0 = 最小化中は無視)
		virtual bool Resize(uint32_t width, uint32_t height) = 0;

		virtual uint32_t GetWidth()const  = 0;
		virtual uint32_t GetHeight()const = 0;

		// シーンを描く大きさ (ED3)。0 x 0 なら画面 (スワップチェーン) と同じ (以前と同じ)
		// 決めると、シーンはその大きさのテクスチャ (UI_TEXTURE_VIEW) に描かれ、画面には UI だけを描く
		// (C# の描画パスが「画面 (BACK_BUFFER)」に描いた物は、そのテクスチャに入る)。GetWidth / GetHeight もこの大きさを返す
		virtual bool SetRenderSize(uint32_t width, uint32_t height) = 0;

		// ======================================================================
		// データの登録 (GPU への転送は次の Render でまとめて行われる)
		// ======================================================================

		// 頂点をメガ頂点バッファへ (失敗時は IsValid() == false)
		virtual VertexRange UploadVertices(const Vertex* vertices, uint32_t count) = 0;

		// インデックスをメガインデックスバッファへ (値は VertexRange::BaseVertex からの相対)
		virtual IndexRange UploadIndices(const uint32_t* indices, uint32_t count) = 0;

		// 画像ファイルをまとめて読み込む (デコードは並列)。失敗したものは TextureHandle::DEFAULT
		virtual void LoadTextures(const wchar_t* const* filePaths, uint32_t count, TextureHandle* outHandles) = 0;

		// ---------- v1.17 (S4) : 画像の読み込みを 2 つに分ける (モデルの非同期の読み込み用) ----------
		// LoadTextures の中身と同じ 2 段 : 1. デコード (CPU だけ) → 2. GPU のテクスチャを作る (メインスレッド)
		// DecodeTextures         : どのスレッドから呼んでもよい (ファイル → デコード / DDS のキャッシュ。並列)。戻り値は必ず次のどちらかに渡す
		// CreateDecodedTextures  : メインスレッドで呼ぶ。outHandles に decoded と同じ数のハンドル (失敗した物は DEFAULT)。decoded は解放される
		// ReleaseDecodedTextures : 使わなかった時に解放する (どのスレッドでも)
		virtual DecodedTextures* DecodeTextures(const wchar_t* const* filePaths, uint32_t count) = 0;
		virtual void CreateDecodedTextures(DecodedTextures* decoded, TextureHandle* outHandles) = 0;
		virtual void ReleaseDecodedTextures(DecodedTextures* decoded) = 0;

		// マテリアル (テクスチャ / 色 / ブレンドの方法) を作る。DrawItem::Material に入れて使う
		// 満杯なら MaterialHandle::DEFAULT (白・不透明)
		virtual MaterialHandle CreateMaterial(const MaterialDesc& desc) = 0;

		// ---------- ED5 : エディターの読み直し / 解放 ----------
		// マテリアルの値を変える (次のフレームから。GPU を待たない)
		virtual void UpdateMaterial(MaterialHandle material, const MaterialDesc& desc) = 0;
		// 使い終わった頂点 / インデックス / マテリアル / テクスチャをまとめて返す
		// GPU の完了を待ってから返すので、毎フレーム呼ぶ物ではない (エディターの読み直し / 解放用)。null / 0 個は飛ばす
		virtual void ReleaseResources(const VertexRange* vertices, uint32_t vertexCount, const IndexRange* indices, uint32_t indexCount,
		                              const MaterialHandle* materials, uint32_t materialCount, const TextureHandle* textures, uint32_t textureCount) = 0;
		// ---------- AN4 : GPU スキニング ----------
		// 頂点ごとの骨と重みを登録する (モデルごと。頂点の数と同じ数)。失敗時は IsValid() == false
		virtual SkinRange  UploadSkin(const SkinVertex* vertices, uint32_t count) = 0;
		// 返す (GPU を待つ。使っている曲げる物を先に消すこと)
		virtual void       ReleaseSkin(SkinRange range) = 0;
		// 曲げる物を作る : source (休みの姿勢の頂点) と同じ数の頂点の場所をメガ頂点バッファに取り、そこへ曲げた頂点を書く
		//   boneCount 本の行列は最初は単位行列 (= 休みの姿勢)。描く時は DrawItem::BaseVertex を GetSkinOutput の物にする
		virtual SkinHandle CreateSkinInstance(VertexRange source, SkinRange skin, uint32_t boneCount) = 0;
		virtual VertexRange GetSkinOutput(SkinHandle instance)const = 0;
		// 骨の行列 (1 本 12 個 : 行ベクトルの行列 M の 3 列 = (M11, M21, M31, M41), (M12, ...), (M13, ...))。次の描画の前に曲げる
		virtual void       SetSkinPose(SkinHandle instance, const float* matrices, uint32_t boneCount) = 0;
		// カリングの境界球 (曲げた形の空間で 中心 xyz と半径)。今の球から出る時だけ広げる (描画の引数を作り直すので、毎回は変えない)
		virtual void       SetSkinBounds(SkinHandle instance, const float* sphere) = 0;
		// 消す (GPU を待つ)
		virtual void       DestroySkinInstance(SkinHandle instance) = 0;
		// 前のフレームで曲げた物の数と頂点の数 / GPU の時間 (ms。FRAME_COUNT フレーム前の値)
		virtual void       GetSkinStats(uint32_t* outInstances, uint32_t* outVertices, float* outGpuMs)const = 0;
		// 確認用 : 曲げた頂点を読み戻す。Request の次のフレームの頭で写し、Read は GPU を待って読む (戻り値 = 読めた数。まだなら 0)
		virtual void       RequestSkinReadback(SkinHandle instance) = 0;
		virtual uint32_t   ReadSkinReadback(Vertex* outVertices, uint32_t maxCount) = 0;

		// ---------- プレビューの窓 (AN5) ----------
		// 視点 view (1〜) の物 (描画リスト。シーンの物と同じ形) を決める。描画オブジェクトが増減した時だけ呼べばよい
		virtual void       SetPreviewItems(uint32_t view, const DrawItem* items, uint32_t count) = 0;
		// 視点 view のカメラ / 光 / 線 (depthLineCount 本目までは深度で隠れる線、残りは一番上に描く線。2 頂点で 1 本)。毎フレーム
		virtual void       SetPreviewFrame(uint32_t view, const PreviewFrame& frame, const DebugLineVertex* lines, uint32_t lineVertexCount,
		                                   uint32_t depthLineVertexCount) = 0;
		// レンダーターゲットを UI のテクスチャとして使う時の番号 (UIDrawCommand.Texture。無ければ 0xFFFFFFFF)
		virtual uint32_t   GetRenderTargetTexture(RenderTargetHandle target)const = 0;

		// シェーダーを読み直す (GPU を待ってからコンパイルし直し、PSO は次に使う時に作る)
		// コンパイルに失敗したシェーダーは前の物を使い続ける。戻り値 = 全部成功したか
		virtual bool ReloadShaders() = 0;
		// 使っている頂点 / インデックスの量 (byte。返した分は除く)
		virtual uint64_t GetMeshBytesUsed()const = 0;

		// 描画オブジェクト (ワールド行列の置き場) を作る (削除された番号があれば使い回す)
		virtual ObjectHandle CreateObject() = 0;

		// 描画オブジェクトを削除する。削除したオブジェクトを指す DrawItem は、次の Render から渡さないこと
		virtual void DestroyObject(ObjectHandle object) = 0;

		// オブジェクトの定数を更新する (毎フレーム呼んでもよい。GPU が使用中の領域は書き換えない)
		virtual void UpdateObject(ObjectHandle object, const ObjectConstants& constants) = 0;

		// カメラ (DirectXMath の普通の行列のまま渡す。転置は RHI の中で行う)
		virtual void SetViewProjection(const ::DirectX::XMFLOAT4X4& view, const ::DirectX::XMFLOAT4X4& projection) = 0;

		// v1.5 : 複数のカメラ (最大 MAX_CAMERAS)。描画パスの SetCamera(番号) で切り替える。0 番はフレームの始めのカメラにもなる
		virtual void SetCameraViews(const CameraView* views, uint32_t count) = 0;

		// ======================================================================
		// 描画
		// ======================================================================

		// ======================================================================
		// 描画パス (レンダーターゲット / 全画面のシェーダー / ライト)
		// ======================================================================

		// レンダーターゲットを作る (画面に合わせる物はリサイズで自動で作り直す)
		virtual RenderTargetHandle CreateRenderTarget(const RenderTargetDesc& desc) = 0;
		virtual void               DestroyRenderTarget(RenderTargetHandle handle) = 0;
		// MSAA で使えるサンプル数の上限 (HDR / 環境光 / 深度の形式がどれも対応している数。最大 8)
		virtual uint32_t           GetMaxSampleCount() const = 0;

		// 全画面のシェーダーを読み込む (ファイルの VSFullscreen と entry のピクセルシェーダー)。失敗時は無効なハンドル
		virtual FullscreenShaderHandle LoadFullscreenShader(const wchar_t* filePath, const char* entry) = 0;

		// ライト (変わった時だけ呼べばよい) / ライティングの方法と環境光
		virtual void SetLights(const LightData* lights, uint32_t count) = 0;
		virtual void SetLightingSettings(const LightingSettings& settings) = 0;
		// 影 (変わった時だけ呼べばよい。ライトの視点の行列は毎フレーム RHI が作る)
		virtual void SetShadowSettings(const ShadowSettings& settings) = 0;
		// 確認用の線 (毎フレーム呼ぶ。0 本で消える)。描くのはこのフレームの描画の最後
		virtual void SetDebugLines(const DebugLineVertex* vertices, uint32_t count) = 0;
		// v2.2 : 確認用の塗りつぶしの三角形 (次のフレームの DrawDebugTriangles で描く。3 頂点で 1 枚)
		virtual void SetDebugTriangles(const DebugLineVertex* vertices, uint32_t count) = 0;

		// UI (毎フレーム呼ぶ。0 個で消える)。描くのはこのフレームの一番最後 (画面へ。プロファイラーの下)
		virtual void SetUIDrawData(const UIVertex* vertices, uint32_t vertexCount, const uint16_t* indices, uint32_t indexCount,
		                           const UIDrawCommand* commands, uint32_t commandCount) = 0;
		// UI のテクスチャ (RGBA8。フォントなど)。戻り値はテクスチャの番号 (失敗時 0xFFFFFFFF)
		virtual uint32_t CreateUITexture(uint32_t width, uint32_t height, const uint8_t* rgba) = 0;
		// v2.3 : UI のテクスチャを返す (番号はすぐ使えなくなる。実体は GPU が使い終わってから解放。GPU を待たない)。知らない番号なら false
		virtual bool ReleaseUITexture(uint32_t texture) = 0;

		// ---------- SH2 : 自分のシェーダー / 3D テクスチャ ----------
		// 自分のシェーダーを作る (source はシーンのシェーダーの後ろに付けてコンパイルされる。エントリーは PSCustom)
		// 戻り値は番号 (失敗時 0。error にコンパイラーの文を入れる (errorCapacity バイトまで。終わりは 0))
		virtual uint32_t CreateCustomShader(const char* source, const CustomShaderSettings& settings, char* error, uint32_t errorCapacity) = 0;
		// 同じ番号のまま中身を替える (失敗したら前の物のまま false)
		virtual bool     UpdateCustomShader(uint32_t shader, const char* source, const CustomShaderSettings& settings, char* error, uint32_t errorCapacity) = 0;
		// 消す (GPU を待つ。使っているマテリアルは先に外すこと)
		virtual void     DestroyCustomShader(uint32_t shader) = 0;
		// マテリアルに自分のシェーダーとパラメーター (float4 × paramCount) を付ける。shader = 0 で外す (共通のシェーダーに戻る)
		virtual void     SetMaterialShader(MaterialHandle material, uint32_t shader, const float* params, uint32_t paramCount) = 0;
		// 3D テクスチャ (channels 1 = R8 / 4 = RGBA8。data は x → y → z の順)。返すのは ReleaseResources (2D と同じ)。失敗時 DEFAULT
		virtual TextureHandle CreateTexture3D(uint32_t width, uint32_t height, uint32_t depth, uint32_t channels, const uint8_t* data) = 0;
		// 最後のフレームで自分のシェーダーで描いた数 (確認用)
		virtual uint32_t GetCustomDrawCount()const = 0;

		/***********************************************************************************
		 * @brief 1 フレーム分の描画 (開始 → 描画パスの命令を順に実行 → UI → 表示)。GPU の完了は待たない
		 * @details items は描くシーン (DrawScene で使う)。commands は C# が組み立てた描画パス
		 *          以前は「シーンをバックバッファに 1 回描く」だけだった
		 ************************************************************************************/
		virtual bool Render(const DrawItem* items, uint32_t count, const RenderCommand* commands, uint32_t commandCount) = 0;

		// GPU の処理 (描画 + 転送) が全て終わるまで待つ (計測・終了処理用)
		virtual void WaitForGpu() = 0;

		// ExecuteIndirect で描くか (既定 : true)。false なら描画ごとに DrawIndexedInstanced を呼ぶ
		// 描画数が多いほど ExecuteIndirect の方が CPU が軽い。使えない環境では自動で通常の描画になる
		virtual void SetIndirectDrawEnabled(bool enabled) = 0;
		virtual bool IsIndirectDrawEnabled()const = 0;

		// インスタンシングするか (既定 : true)。同じメッシュ (頂点 / インデックスの範囲が同じ) の DrawItem を
		// 1 回の描画にまとめる。オブジェクトごとの行列・テクスチャは違っていてよい
		virtual void SetInstancingEnabled(bool enabled) = 0;
		virtual bool IsInstancingEnabled()const = 0;

		// GPU カリング (視錐台カリング) をするか (既定 : true)。カメラに映らないオブジェクトを GPU が除いてから描く
		// ExecuteIndirect の時だけ効く。メッシュの大きさは頂点を渡した時に RHI が計算するので、Renderer 側の準備は不要
		virtual void SetGpuCullingEnabled(bool enabled) = 0;
		virtual bool IsGpuCullingEnabled()const = 0;

		// オクルージョンカリングをするか (既定 : true)。手前の物に完全に隠れている物を除く (2 段階 : 遅れ無し)
		// GPU カリングの時だけ効く
		virtual void SetOcclusionCullingEnabled(bool enabled) = 0;
		virtual bool IsOcclusionCullingEnabled()const = 0;

		// ======================================================================
		// GPU パーティクル
		// ======================================================================

		// パーティクルの最大数 (2 の累乗に切り上げ。0 で使わない)。GPU の完了を待ってからバッファを作り直す (生きている物は消える)
		virtual void SetParticleCapacity(uint32_t capacity) = 0;
		// エミッター (毎フレーム。出す数は C# が決める)。描くのは C# の描画パスの UpdateParticles / DrawParticles
		virtual void SetParticleEmitters(const ParticleEmitterData* emitters, uint32_t count, const ParticleFrameSettings& settings) = 0;
		virtual ParticleStats GetParticleStats()const = 0;

		// ======================================================================
		// プロファイラー (ImGui のウィンドウ。F1 キーでも表示 / 非表示を切り替えられる)
		// ======================================================================

		virtual void SetProfilerVisible(bool visible) = 0;
		virtual bool IsProfilerVisible()const = 0;

		// 任意の値をプロファイラーに表示する (例: "LoadModel (ms)")。同じ名前なら上書き
		virtual void ReportProfilerValue(const char* name, double value) = 0;

		// 任意の文字列をプロファイラーに表示する (例: "Render pipeline" → "Deferred / PBR")
		virtual void SetProfilerInfo(const char* name, const char* text) = 0;

		// v1.10 : 計測値を外から読む (エディターが自分の窓に出す)。値 / 文字列のポインタは次に値が足されるまで有効
		// F1 の ImGui の窓を使わない (false : F1 も入力の横取りもしない。エディターは自分の窓で出すので切る)
		virtual void SetProfilerOverlayEnabled(bool enabled) = 0;
		virtual bool GetProfilerFrame(ProfilerFrameData& out)const = 0;
		// ReportProfilerValue などで足された値。戻り値 = 全部の数 (capacity より多くても数は返す)。並びは足された順で変わらない
		virtual uint32_t GetProfilerValues(double* values, uint32_t capacity)const = 0;
		virtual const char* GetProfilerValueName(uint32_t index)const = 0;
		virtual uint32_t GetProfilerInfoCount()const = 0;
		virtual bool GetProfilerInfo(uint32_t index, const char** name, const char** text)const = 0;
		virtual const char* GetGpuName()const = 0;

		// ======================================================================
		// v1.10 P2 : 描画パス (RenderCommandType::BeginPass / EndPass) の名前と時間
		// ======================================================================

		// 名前を登録して番号をもらう (同じ名前なら同じ番号)。BeginPass の Arg0 に入れる
		virtual uint32_t RegisterPassName(const char* name) = 0;
		virtual const char* GetPassName(uint32_t id)const = 0;
		// パスごとに GPU の時間を計るか (タイムスタンプを書く。プロファイラーの窓が開いている間だけ true にする)
		// PIX / RenderDoc のイベント (BeginEvent / EndEvent) と CPU の時間は、この値に関係なく毎フレーム
		virtual void SetPassTimingEnabled(bool enabled) = 0;
		// 最後に読めたフレームのパスの時間 (始まった順)。戻り値 = 全部の数
		virtual uint32_t GetPassTimings(PassTiming* out, uint32_t capacity)const = 0;
		// レンダーターゲットに名前を付ける (D3D12 の SetName。PIX / RenderDoc / デバッグレイヤーの表示。作り直しても残る)
		virtual void SetRenderTargetName(RenderTargetHandle handle, const char* name) = 0;

		// ======================================================================
		// v1.12 P3 : Render Graph Viewer (パスごとのリソースの読み書き / バリア)
		// ======================================================================

		// 記録するか (エディターの Render Graph Viewer / Memory の窓が開いている間だけ true)
		virtual void SetRenderGraphCapture(bool enabled) = 0;
		// 最後に記録したフレーム。各配列の数を *outCount に入れる (capacity を超えた分は写さない)。戻り値 = 記録があるか
		virtual bool GetRenderGraph(RenderGraphPass* passes, uint32_t passCapacity, uint32_t* passCount,
		                            RenderGraphResource* resources, uint32_t resourceCapacity, uint32_t* resourceCount,
		                            RenderGraphAccess* accesses, uint32_t accessCapacity, uint32_t* accessCount)const = 0;
		// レンダーターゲットの名前 (SetRenderTargetName の物。UTF-8。無ければ空)
		virtual uint32_t GetRenderTargetName(RenderTargetHandle handle, char* out, uint32_t size)const = 0;
		// UI (ImGui) で中身を見るレンダーターゲット (毎フレーム、UI の前に読める状態にする。INVALID で止める)
		virtual void SetDebugReadTarget(RenderTargetHandle handle) = 0;

		// ======================================================================
		// v1.13 P4 : Frame Debugger
		// ======================================================================

		// 毎フレーム設定する (Enabled の間は ExecuteIndirect / GPU カリングを使わず 1 個ずつ描き、イベントを数える)
		virtual void SetFrameDebugger(const FrameDebugSettings& settings) = 0;
		// 最後のフレームのイベント (止めた時は止めた所まで) とパス。戻り値 = 記録があるか
		virtual bool GetFrameDebugEvents(FrameDebugEvent* events, uint32_t eventCapacity, uint32_t* eventCount,
		                                 RenderGraphPass* passes, uint32_t passCapacity, uint32_t* passCount,
		                                 FrameDebugResult* result)const = 0;
	};
}

// RHI.dll が export する関数 (名前が変わらないよう extern "C")
extern "C"
{
	RHI_API RHI::IRenderHardwareInterface* RHI_CreateRenderHardwareInterface();
	RHI_API void                           RHI_DestroyRenderHardwareInterface(RHI::IRenderHardwareInterface* rhi);
}

#endif // RHI_PUBLIC_RHI_H_
