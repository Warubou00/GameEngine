
/***********************************************************************************
* @file    rhiTypes.h
* @brief   RHI の公開データ型 (RHI を使う側と共有する型)
* @details DirectX12 の型はここに出さない。RHI の外 (Renderer など) はこの型とハンドルだけでやり取りする
*
* @author  Keigo Onari
* @date    2026/11/01
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/

#pragma once
#ifndef RHI_PUBLIC_TYPES_H_
#define RHI_PUBLIC_TYPES_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <cstddef>
#include <cstdint>
#include <DirectXMath.h>

#include "../../../Core/Source/Math/Vector/vector.h"

namespace RHI
{
	// ==========================================================================
	// 結果コード (C# 側の RHI_RESULT と同じ値)
	// ==========================================================================
	namespace Result
	{
		constexpr int Succeeded          = 1;
		constexpr int Failed             = -1;
		constexpr int FailedInitialized  = -2;
		constexpr int FailedInvalidArg   = -3;
		constexpr int FailedCreateObject = -4;
		constexpr int FailedCompileShader= -5;
		constexpr int NotInitialized     = -1;
	}

	// ==========================================================================
	// 頂点
	// RHI/Source/Shader/hlslTemplate.shader の struct Vertex と同じ並び・同じサイズ(56byte)にすること
	// (メガ頂点バッファを StructuredBuffer として読むため、1byteでもずれると全頂点が崩れる)
	// 以前は Renderer 側で定義していたが、シェーダーと対になる型なので RHI に移した
	// ==========================================================================
	struct Vertex
	{
		Core::Vector3 Position = Core::Vector3(0.0f);   // offset  0
		float padding1         = 0.0f;                  // offset 12
		Core::Vector3 Normal   = Core::Vector3(0.0f);   // offset 16
		float padding2         = 0.0f;                  // offset 28
		Core::Vector4 Color    = Core::Vector4(1.0f);   // offset 32 : 既定は白
		Core::Vector2 UV       = Core::Vector2(0.0f);   // offset 48
	};

	// シェーダー側とのサイズ・並びの食い違いをコンパイル時に検出する
	static_assert(sizeof(Vertex) == 56, "RHI::Vertex must be 56 bytes (same as HLSL struct Vertex)");
	static_assert(offsetof(Vertex, Normal) == 16, "Vertex::Normal offset must be 16");
	static_assert(offsetof(Vertex, Color)  == 32, "Vertex::Color offset must be 32");
	static_assert(offsetof(Vertex, UV)     == 48, "Vertex::UV offset must be 48");

	// ==========================================================================
	// オブジェクトごとの定数 (シェーダーの struct ObjectData と同じ並び)
	// 行列は DirectXMath の普通の行列のまま渡す (転置は RHI の中で行う)
	// 以前はテクスチャの番号も持っていたが、マテリアル (MaterialHandle) に移した。
	// オブジェクトは「置く場所」だけになり、1 つのモデルの全サブメッシュで 1 個を共有できる
	// ==========================================================================
	struct ObjectConstants
	{
		::DirectX::XMFLOAT4X4 World = {};   // ワールド行列
		// v1.5 : 物ごとの印 (OBJECT_FLAG_*) と、写すカメラを選ぶレイヤーのビット (1 << レイヤー)。以前は行列だけ (64byte)
		uint32_t Flags     = 0;
		uint32_t LayerMask = 1;
		uint32_t Padding[2] = {};
	};
	constexpr uint32_t OBJECT_FLAG_NO_CAST_SHADOWS    = 1u;   // 影を落とさない (影の視点のカリングで外す)
	constexpr uint32_t OBJECT_FLAG_NO_RECEIVE_SHADOWS = 2u;   // 影を受けない (シェーダーがシャドーマップを読まない)

	// v1.5 : 複数のカメラ。SetCameraViews で全部を渡し、描画パスの SetCamera で切り替える
	constexpr uint32_t MAX_CAMERAS = 8;
	struct CameraView
	{
		::DirectX::XMFLOAT4X4 View       = {};   // 普通の行列 (転置は RHI の中で行う)
		::DirectX::XMFLOAT4X4 Projection = {};
		uint32_t CullingMask = 0xFFFFFFFFu;     // 写すレイヤー (ObjectConstants::LayerMask と & して 0 なら写さない)
	};

	// ==========================================================================
	// ハンドル (RHI 内部の資源を番号で指す。ポインタを外に出さない)
	// ==========================================================================
	constexpr uint32_t INVALID_HANDLE_INDEX = 0xFFFFFFFFu;

	// テクスチャ。0 は既定テクスチャ(白 1x1) で、読み込みに失敗した時もこれになる
	struct TextureHandle
	{
		static constexpr uint32_t DEFAULT = 0;
		uint32_t Index = DEFAULT;
	};

	// 描画するオブジェクト (オブジェクトごとの定数 = ワールド行列やテクスチャ を持つ)
	struct ObjectHandle
	{
		uint32_t Index = INVALID_HANDLE_INDEX;
		bool IsValid()const { return Index != INVALID_HANDLE_INDEX; }
	};

	// ==========================================================================
	// マテリアル (テクスチャ・色・ブレンドの方法)
	//   描き方 (ブレンド) ごとに PSO を分けて、まとめて描く : 不透明 → 半透明 の順
	// ==========================================================================
	enum class BlendMode : uint32_t
	{
		Opaque     = 0,   // 不透明 (先に描く。GPU カリング / オクルージョンの対象)
		AlphaBlend = 1,   // 半透明 (不透明の後に、DrawItem の順番で描く。深度は書く = MMD と同じ)
	};

	struct MaterialDesc
	{
		TextureHandle Texture;                          // 既定 (0) は白 1x1
		float         Color[4] = { 1.0f, 1.0f, 1.0f, 1.0f };   // テクスチャ × 頂点の色 × この色
		BlendMode     Blend    = BlendMode::Opaque;
		float         Metallic  = 0.0f;   // PBR : 金属らしさ (0 = 非金属)
		float         Roughness = 0.6f;   // PBR : 表面の粗さ (0 = つるつる / 1 = ざらざら)
		// 裏面も描く (葉や布など厚みの無い物)。false なら不透明な物は裏面を描かない (裏面カリング)
		// 半透明は描く順番を崩せないので、この値に関係なく両面を描く
		bool          DoubleSided = false;
		// ※自分のシェーダー (SH2) はここに入れない (色などを UpdateMaterial で送り直すたびに外れないよう、SetMaterialShader で別に持つ)
	};

	// ==========================================================================
	// 自分のシェーダー (SH2)。マテリアルに付けると、その物はまとめて描く道 (GPU カリング + ExecuteIndirect) から外れ、
	// DrawCustom でこのシェーダーで 1 個ずつ描く。頂点シェーダーはシーンと同じ (VSMain)、ピクセルシェーダーだけ自分の物
	//   ソースはシーンのシェーダー (hlslTemplate.shader) の後ろに付けてコンパイルする (同じ構造体 / 関数 / バインドが使える)
	//   エントリーは PSCustom。値 (パラメーター) は g_CustomData[] (float4) から読む (C# の生成するコードが読み方を書く)
	// ==========================================================================
	enum class CustomBlend : uint32_t
	{
		Opaque        = 0,   // 上書き
		Alpha         = 1,   // アルファブレンド (色 × α + 下 × (1 - α))
		Additive      = 2,   // 足す (色 × α + 下)
		Premultiplied = 3,   // 乗算済み (色 + 下 × (1 - α))
	};
	enum class CustomCull : uint32_t { Back = 0, Front = 1, None = 2 };

	struct CustomShaderSettings
	{
		CustomBlend Blend       = CustomBlend::Opaque;
		CustomCull  Cull        = CustomCull::Back;
		uint32_t    DepthWrite  = 1;   // 深度を書くか (半透明の雲などは 0)
		uint32_t    Transparent = 0;   // 1 : 半透明の列 (不透明の後に、遠い順に描く)
		uint32_t    ReadDepth   = 0;   // 1 : シーンの深度をテクスチャとして読む (SH3 : 雲が物に埋まる所)。深度のテストはしない (シェーダーが比べる)
		uint32_t    Padding[3]  = {};
	};
	static_assert(sizeof(CustomShaderSettings) == 32, "CustomShaderSettings must be 32 bytes (same as C#)");

	constexpr uint32_t MAX_CUSTOM_PARAMS = 64;   // マテリアル 1 つのパラメーター (float4) の数の上限

	struct MaterialHandle
	{
		static constexpr uint32_t DEFAULT = 0;          // 既定のマテリアル (白・不透明)
		uint32_t Index = DEFAULT;
	};

	// メガ頂点バッファ内の範囲
	struct VertexRange
	{
		uint32_t BaseVertex = 0;
		uint32_t Count      = 0;
		bool IsValid()const { return Count != 0; }
	};

	// ==========================================================================
	// AN4 : GPU スキニング
	//   SkinVertex : 頂点ごとの骨 (番号 16bit × 4 / 重み 16bit (0〜1) × 4)。skinning.shader の SkinVertex と同じ並び (16 byte)
	//   SkinRange  : 骨と重みのバッファの中の範囲 (モデルごと)
	//   SkinHandle : 曲げる物 1 つ (その物専用の頂点の場所と、骨の行列)
	// ==========================================================================
	struct SkinVertex
	{
		uint32_t Bones01   = 0;
		uint32_t Bones23   = 0;
		uint32_t Weights01 = 0;
		uint32_t Weights23 = 0;
	};
	static_assert(sizeof(SkinVertex) == 16, "RHI::SkinVertex must be 16 bytes (same as HLSL)");

	struct SkinRange
	{
		uint32_t Base  = 0;
		uint32_t Count = 0;
		bool IsValid()const { return Count != 0; }
	};

	struct SkinHandle
	{
		uint32_t Index = UINT32_MAX;
		bool IsValid()const { return Index != UINT32_MAX; }
	};

	// メガインデックスバッファ内の範囲
	struct IndexRange
	{
		uint32_t FirstIndex = 0;
		uint32_t Count      = 0;
		bool IsValid()const { return Count != 0; }
	};

	// ==========================================================================
	// 描画 1 回分
	// ==========================================================================
	struct DrawItem
	{
		uint32_t     BaseVertex = 0;   // VertexRange::BaseVertex
		uint32_t     FirstIndex = 0;   // IndexRange::FirstIndex
		uint32_t       IndexCount = 0;   // IndexRange::Count
		ObjectHandle   Object;           // ワールド行列
		MaterialHandle Material;         // テクスチャ / 色 / ブレンドの方法 (以前はオブジェクトがテクスチャを持っていた)
	};

	// ==========================================================================
	// 描画パス (C# が毎フレーム組み立てて、Render に渡す)
	//   C# のレンダーパイプライン (Forward / Deferred / ポストエフェクト) は、
	//   「描画先を決める → 消す → シーンを描く / 全画面を描く」を RenderCommand の列として並べる
	// ==========================================================================

	// レンダーターゲットの形式 (C# の RenderTextureFormat と同じ番号)
	enum class RenderTargetFormat : uint32_t
	{
		RGBA8      = 0,   // 色 (0〜1)
		RGBA8_SRGB = 1,   // 色 (書く時に sRGB に、読む時に線形に自動で変換。アルベドなど)
		RGBA16F    = 2,   // HDR の色 / 法線など
		RG16F      = 3,
		R16F       = 4,
		R32F       = 5,
		R8         = 6,   // 1 成分 (遮蔽など)
		RGB10A2    = 7,
		R11G11B10F = 8,   // HDR の色 (アルファ無し。RGBA16F の半分の大きさ。環境光など)
		Depth32    = 9,   // 深度だけ (シャドーマップ)。描画先の Depth に指定し、後で SRV として読む (MSAA は不可)
	};

	struct RenderTargetDesc
	{
		RenderTargetFormat Format = RenderTargetFormat::RGBA8;
		float    Scale  = 1.0f;   // 画面の大きさに対する倍率 (0 なら Width / Height で固定の大きさ)。リサイズで自動で作り直す
		uint32_t Width  = 0;
		uint32_t Height = 0;
		float    ClearColor[4] = { 0.0f, 0.0f, 0.0f, 0.0f };   // 最適化クリア値 (同じ色で消すと速い)
		// MSAA のサンプル数 (1 / 2 / 4 / 8)。2 以上なら、描画先にした後に全画面のシェーダーで読む時、
		// RHI が 1 サンプルの写しへ自動で解決 (Resolve) する。シーンの深度 (SCENE_DEPTH) も描画先と同じサンプル数になる
		// 対応していない数は、対応している一番近い数に下げる (GetMaxSampleCount)
		uint32_t SampleCount = 1;
		// 配列の枚数 (Depth32 だけ。1 〜 MAX_DEPTH_SLICES)。カスケードのシャドーマップを 1 枚の Texture2DArray にする
		// 描画先にする時は SetRenderTargets の Arg0 で枚目を選ぶ
		uint32_t ArraySize   = 1;
	};
	static constexpr uint32_t MAX_DEPTH_SLICES = 4;

	struct RenderTargetHandle
	{
		static constexpr uint32_t INVALID     = 0xFFFFFFFFu;
		static constexpr uint32_t BACK_BUFFER = 0xFFFFFFFEu;   // 画面 (スワップチェーン)
		static constexpr uint32_t SCENE_DEPTH = 0xFFFFFFFDu;   // シーンの深度 (GPU カリングのオクルージョンもこれを使う)
		// v1.4 : 全画面シェーダーの入力に、レンダーターゲットではない普通のテクスチャ (CreateUITexture などの番号) を渡す印。
		//        下の 30 bit がテクスチャの番号 (g_Textures の番号)。Skybox の空の画像に使う
		static constexpr uint32_t TEXTURE_INPUT = 0x40000000u;
		uint32_t Index = INVALID;
	};

	// 全画面に描くシェーダー (ライティング / ポストエフェクト)。LoadFullscreenShader で読み込む
	struct FullscreenShaderHandle
	{
		uint32_t Index = INVALID_HANDLE_INDEX;
		bool IsValid()const { return Index != INVALID_HANDLE_INDEX; }
	};

	// シーンを描く時のシェーダー
	enum class ScenePass : uint32_t
	{
		Forward = 0,   // ライティングまで 1 回で描く (HDR の色)
		GBuffer = 1,   // ディファード用 : アルベド / 法線 / 金属・粗さ を書く (半透明は描けないので Forward で)
		ForwardSplitAmbient = 2,   // Forward + SSAO 用 : RT0 にライトの光、RT1 に環境光を分けて書く (後で 環境光 × 遮蔽 を足す)
		Shadow  = 3,   // シャドーマップ : 深度だけ (ライトの視点。DrawScene の Inputs[0] で視点を選ぶ。半透明はアルファテスト)
	};

	// シーンのどの物を描くか
	enum class RenderQueue : uint32_t
	{
		Opaque      = 0,   // 不透明 (GPU カリング / オクルージョンはここで行う)
		Transparent = 1,   // 半透明 (Opaque の後に描くこと)
	};

	enum class RenderCommandType : uint32_t
	{
		SetRenderTargets = 0,   // Colors[0..ColorCount) と Depth を描画先にする (Arg0 = 深度の配列の何枚目か)
		ClearColor       = 1,   // 描画先の色を ClearColor で消す
		ClearDepth       = 2,   // 描画先の深度を ClearDepth で消す
		DrawScene        = 3,   // Arg0 = ScenePass / Arg1 = RenderQueue / Inputs[0] = 視点 (0 カメラ / 1〜 影のカスケード)
		DrawFullscreen   = 4,   // Arg0 = シェーダー / Arg1 = ブレンド (0 なし / 1 加算 / 2 アルファ) / Inputs / Params
		UpdateParticles  = 5,   // GPU パーティクルの発生 / 更新 (/ ソート)。不透明の後に置く (衝突でシーンの深度を読む)。1 フレームに 1 回
		DrawParticles    = 6,   // GPU パーティクルを今の描画先へ描く (深度はシェーダーがシーンの深度を読んで比べる。描画先に深度は付けない)
		DrawCustom       = 7,   // 自分のシェーダーのマテリアルの物を今の描画先へ描く (SH2)。Arg1 = RenderQueue / Params[0] = 時間 (秒)
		                        //   Arg0 bit0 = シーンの深度を読む物だけを描く (SH3。深度は描画先から外し、テクスチャとして渡す)
		DrawPreview      = 8,   // プレビューの窓 (AN5) : Arg0 = 視点の番号 (1〜) の物と線を今の描画先へ描く (色 RGBA8 1 枚 + 深度 Depth32)
		                        //   描いた色の描画先は、この後 UI が読めるようにする (UI の Texture に GetRenderTargetTexture の番号)
		SetCamera        = 9,   // v1.5 : Arg0 = カメラの番号 (SetCameraViews の何番目か)。この後の描画はそのカメラで (影のカスケード / カリングも)
		SetViewport      = 10,  // v1.5 : Params[0..3] = x / y / 幅 / 高さ (ピクセル)。次の SetRenderTargets までの描画を、その範囲だけにする
	};

	constexpr uint32_t MAX_PASS_COLOR_TARGETS = 4;
	constexpr uint32_t MAX_PASS_INPUTS        = 8;
	constexpr uint32_t MAX_PASS_PARAMS        = 8;

	// 描画パスの命令 1 個 (C# の Native.RenderCommand と同じ並び : 120byte)
	struct RenderCommand
	{
		RenderCommandType Type = RenderCommandType::SetRenderTargets;
		uint32_t ColorCount = 0;
		uint32_t Colors[MAX_PASS_COLOR_TARGETS] = {};      // RenderTargetHandle::Index
		uint32_t Depth = RenderTargetHandle::INVALID;
		uint32_t Arg0  = 0;
		uint32_t Arg1  = 0;
		float    ClearColor[4] = {};
		float    ClearDepth    = 1.0f;
		uint32_t Inputs[MAX_PASS_INPUTS] = {};             // 全画面シェーダーが読むテクスチャ (RenderTargetHandle::Index)
		float    Params[MAX_PASS_PARAMS] = {};             // 全画面シェーダーに渡す値
	};
	static_assert(sizeof(RenderCommand) == 120, "RenderCommand must be 120 bytes (same as C#)");

	// ==========================================================================
	// ライト / ライティング
	// ==========================================================================
	enum class LightType : uint32_t
	{
		Directional = 0,   // 太陽 (向きだけ)
		Point       = 1,   // 点光源 (位置と届く距離)
	};

	// シェーダーの struct LightData と同じ並び (48byte)
	struct LightData
	{
		float     Position[3] = {};
		float     Range       = 10.0f;
		float     Direction[3] = { 0.0f, -1.0f, 0.0f };   // 光が進む向き (Directional)
		LightType Type        = LightType::Directional;
		float     Color[3]    = { 1.0f, 1.0f, 1.0f };
		float     Intensity   = 1.0f;
	};
	static_assert(sizeof(LightData) == 48, "LightData must be 48 bytes (same as HLSL)");

	enum class LightingModel : uint32_t
	{
		Unlit   = 0,   // ライト無し (テクスチャの色そのまま)
		Lambert = 1,   // 拡散反射だけ
		PBR     = 2,   // 物理ベース (GGX / 金属・粗さ)
	};

	struct LightingSettings
	{
		LightingModel Model      = LightingModel::PBR;
		float         Ambient[3] = { 0.25f, 0.27f, 0.30f };   // 環境光 (空の明るさ)
	};

	// ==========================================================================
	// 影 (平行光源 1 つのシャドーマップ)
	//   ライトの視点の行列は RHI がカメラに合わせて毎フレーム作る (C# は設定とシャドーマップを渡すだけ)
	//   視点の番号 : 1 〜 CascadeCount (DrawScene の Inputs[0])。0 はカメラ
	// ==========================================================================
	static constexpr uint32_t MAX_SHADOW_CASCADES = 4;

	// ==========================================================================
	// 確認用の線 (C# の DebugDraw。2 頂点で 1 本)。トーンマップの後に、物に隠れずに描く
	// ==========================================================================
	struct DebugLineVertex
	{
		float    Position[3];
		uint32_t Color;          // R8G8B8A8 (下位バイトが R)
	};
	static_assert(sizeof(DebugLineVertex) == 16, "DebugLineVertex must be 16 bytes (same as C#)");
	static constexpr uint32_t MAX_DEBUG_LINE_VERTICES = 256 * 1024;

	// ==============================================================
	// UI (エディターの ImGui など。ED3)。全部描いた後に、画面 (スワップチェーン) へ一番上に描く
	//   頂点 / インデックス / 描画の命令は ImGui の ImDrawVert / ImDrawIdx / ImDrawCmd と同じ考え方 (C# がまとめて渡す)
	// ==============================================================
	struct UIVertex
	{
		float    Position[2];    // 画面のピクセル
		float    UV[2];
		uint32_t Color;          // R8G8B8A8 (下位バイトが R)
	};
	static_assert(sizeof(UIVertex) == 20, "UIVertex must be 20 bytes (same as ImDrawVert / C#)");

	struct UIDrawCommand
	{
		uint32_t IndexCount;
		uint32_t IndexOffset;    // インデックスの何個目から
		uint32_t VertexOffset;   // インデックスに足す頂点の番号
		uint32_t Texture;        // CreateUITexture の番号 / UI_TEXTURE_VIEW = シーンの描画の結果
		float    Clip[4];        // 切り取る四角 (左 / 上 / 右 / 下。ピクセル)
	};
	static_assert(sizeof(UIDrawCommand) == 32, "UIDrawCommand must be 32 bytes (same as C#)");

	// シーンを描いたテクスチャ (SetRenderSize で大きさを決めた時。エディターのシーンビュー)
	static constexpr uint32_t UI_TEXTURE_VIEW = 0xFFFFFFF0u;

	// ==============================================================
	// プレビューの窓 (AN5)。描画オブジェクトに視点の番号 (0 = シーン / 1〜 = プレビュー) を付け、
	// シーンの描画には 0 の物だけ、DrawPreview には その番号の物だけを描く
	// ==============================================================
	static constexpr uint32_t MAX_PREVIEW_VIEWS = 16;   // 視点の番号は 1 〜 MAX_PREVIEW_VIEWS - 1

	// preview.shader の cbuffer PreviewFrame と同じ並び (112 byte)
	struct PreviewFrame
	{
		float    ViewProjection[16];          // 行ベクトル (v × M) の行列 (C# の Matrix4x4 のまま。RHI が転置する)
		float    LightDirection[3] = { -0.4f, -0.8f, 0.45f };   // 光が進む向き
		uint32_t Flags             = 0;       // PreviewFlags
		float    CameraPosition[3] = {};
		float    Padding0          = 0.0f;
		float    WireColor[4]      = { 0.1f, 0.1f, 0.1f, 1.0f };
	};
	static_assert(sizeof(PreviewFrame) == 112, "PreviewFrame must be 112 bytes (same as C# / preview.shader)");

	enum PreviewFlags : uint32_t
	{
		PREVIEW_SHADED    = 1u << 0,   // 面を描く
		PREVIEW_WIREFRAME = 1u << 1,   // 線 (ワイヤーフレーム) を重ねる
	};

	struct ShadowSettings
	{
		uint32_t Enabled      = 0;
		uint32_t LightIndex   = 0;                          // SetLights で渡したライトの番号 (平行光源であること)
		uint32_t ShadowMap    = RenderTargetHandle::INVALID;   // Depth32 のレンダーターゲット
		uint32_t CascadeCount = 1;                          // 1 〜 MAX_SHADOW_CASCADES
		float    Distance     = 30.0f;                      // 影を描く距離 (カメラからの奥行き)
		float    NormalBias   = 1.0f;                       // 法線方向へずらす量 (シャドーマップの 1 texel の何倍か)
		float    Softness     = 1.0f;                       // PCF で読む間隔 (texel)
		float    SplitLambda  = 0.75f;                      // カスケードの分け方 (0 = 均等 / 1 = 対数)
		float    CascadeBlend = 0.1f;                       // 段の境目をなめらかにつなぐ幅 (段の奥行きに対する割合。0 なら切り替えるだけ)
		uint32_t Flags        = 0;                          // ShadowFlags
		uint32_t FrozenMask   = 0;                          // このフレームは行列を作り直さない (描き直さない) カスケード (ビット)
		uint32_t Padding      = 0;
	};
	static_assert(sizeof(ShadowSettings) == 48, "ShadowSettings must be 48 bytes (same as C#)");

	enum ShadowFlags : uint32_t
	{
		SHADOW_FLAG_UNION_CULLING = 1,   // 全カスケードを囲む箱で 1 回だけカリングする (既定は段ごと)
		SHADOW_FLAG_SHOW_CASCADES = 2,   // 確認用 : 段ごとに色を付ける
	};

	// ==========================================================================
	// GPU パーティクル (発生 / 更新 / 生死の管理 / 描画を GPU だけで行う)
	//   CPU (C# の ParticleSystem) は毎フレーム、エミッターの置き場所と「このフレームに出す数」を送るだけ。
	//   パーティクル 1 個 1 個のデータは GPU の中にしか無い (CPU は読まない)
	//   参考 : CAPCOM「MH:W | GPU Particle」(GCC2018)。ClaudLog/Engine_GpuParticles_Design_2026-11-03.txt
	// ==========================================================================
	static constexpr uint32_t MAX_PARTICLE_EMITTERS = 1024;
	static constexpr uint32_t MAX_PARTICLE_CAPACITY = 1u << 21;   // 約 210 万個 (パーティクル 64byte × 容量 = 128MB)

	// 出す場所の形 (ParticleEmitterData::Shape の下位 8bit)
	enum class ParticleSpawnShape : uint32_t
	{
		Point  = 0,   // エミッターの位置から
		Sphere = 1,   // 半径 ShapeRadius の球の中
		Shell  = 2,   // 半径 ShapeRadius の球の表面
		Box    = 3,   // 半辺 ShapeRadius の立方体の中
		Disc   = 4,   // 半径 ShapeRadius の円 (エミッターの向きに垂直な面)
	};

	// 見た目の形 (ParticleEmitterData::Shape の 8〜15bit)
	enum class ParticleSprite : uint32_t
	{
		SoftCircle = 0,   // ふちがぼやけた丸 (煙 / 光)
		Spark      = 1,   // 芯の明るい点 (火花)
		Ring       = 2,   // 輪
	};

	enum ParticleEmitterFlags : uint32_t
	{
		PARTICLE_EMITTER_ADDITIVE = 1,   // 加算 (光る物。並び順に関係なく正しく重なる)。無ければ半透明 (アルファブレンド)
		PARTICLE_EMITTER_COLLIDE  = 2,   // シーンの深度で跳ね返る (ParticleFrameSettings の衝突が有効な時)
		PARTICLE_EMITTER_STRETCH  = 4,   // 速度の向きに伸ばす (StretchScale)。回転はしない
	};

	// エミッター 1 個分 (C# の ParticleSystem が毎フレーム送る。シェーダーの struct Emitter と同じ並び : 160byte)
	struct ParticleEmitterData
	{
		float    Position[3]     = {};
		uint32_t SpawnCount      = 0;      // このフレームに出す数
		float    PrevPosition[3] = {};     // 前のフレームの位置 (動いている時は、この間に散らして出す)
		uint32_t SpawnOffset     = 0;      // RHI が書く (前のエミッターまでの SpawnCount の合計)
		float    Direction[3]    = { 0.0f, 1.0f, 0.0f };   // 出す向き (正規化済み)
		float    ConeAngle       = 0.3f;   // 向きのばらつき (円錐の半角。ラジアン。π で全方向)
		float    SpeedMin        = 1.0f;
		float    SpeedMax        = 2.0f;
		float    LifeMin         = 1.0f;   // 寿命 (秒)
		float    LifeMax         = 2.0f;
		float    Gravity[3]      = { 0.0f, -9.8f, 0.0f };   // 加速度 (m/s^2)
		float    Drag            = 0.0f;   // 空気抵抗 (1 秒あたりに速度が e^-Drag 倍)
		float    ColorStart[4]   = { 1.0f, 1.0f, 1.0f, 1.0f };   // 線形の色 (HDR。1 を超えてよい) とアルファ
		float    ColorEnd[4]     = { 1.0f, 1.0f, 1.0f, 0.0f };
		float    SizeStart       = 0.1f;   // 大きさ (四角の半分の幅。ワールド)
		float    SizeEnd         = 0.1f;
		uint32_t Shape           = 0;      // 下位 8bit : ParticleSpawnShape / 8〜15bit : ParticleSprite
		float    ShapeRadius     = 0.0f;
		uint32_t Flags           = 0;      // ParticleEmitterFlags
		float    Restitution     = 0.5f;   // 跳ね返りの強さ (0〜1)
		float    AngularSpeed    = 0.0f;   // 回転の速さの最大 (ラジアン / 秒。向きはランダム)
		uint32_t Seed            = 0;      // 乱数の種 (エミッターごとに違う値)
		float    Turbulence      = 0.0f;   // 乱れ (ふらふら揺れる強さ。m/s^2)
		float    StretchScale    = 0.0f;   // 速度で伸ばす長さ (秒。速さ × これ だけ伸びる)
		float    Padding[2]      = {};
	};
	static_assert(sizeof(ParticleEmitterData) == 160, "ParticleEmitterData must be 160 bytes (same as HLSL / C#)");

	enum ParticleFrameFlags : uint32_t
	{
		PARTICLE_FRAME_COLLISION = 1,   // シーンの深度との衝突 (PARTICLE_EMITTER_COLLIDE のエミッターだけ)
		PARTICLE_FRAME_SORT      = 2,   // 奥から手前へ並べ替えて描く (半透明を正しく重ねる)
	};

	// フレームごとの設定 (C# の Native.ParticleFrameSettings と同じ並び : 32byte)
	struct ParticleFrameSettings
	{
		float    DeltaTime          = 0.0f;   // このフレームの経過時間 (秒)
		uint32_t Flags              = 0;      // ParticleFrameFlags
		float    CollisionThickness = 0.5f;   // 深度の面の後ろ、どこまでを「当たった」とみなすか (ワールド)
		float    SoftDistance       = 0.3f;   // 面に近づくと薄くする距離 (ソフトパーティクル。0 なら切る)
		float    Time               = 0.0f;   // 起動からの時間 (乱れの模様を動かす)
		uint32_t Padding[3]         = {};
	};
	static_assert(sizeof(ParticleFrameSettings) == 32, "ParticleFrameSettings must be 32 bytes (same as C#)");

	// 計測値 (C# の Native.ParticleStats と同じ並び : 36byte。以前は 32byte)。GPU の値は FRAME_COUNT フレーム前のもの
	struct ParticleStats
	{
		uint32_t Capacity  = 0;
		uint32_t Alive     = 0;      // 生きている数
		uint32_t Free      = 0;      // 空きの数 (Capacity - Alive)
		uint32_t Emitters  = 0;
		float    UpdateMs  = 0.0f;   // GPU : 発生 + 更新 (+ ソート)
		float    SortMs    = 0.0f;   // GPU : ソートだけ (UpdateMs に含む)
		float    DrawMs    = 0.0f;   // GPU : 描画
		uint32_t Valid     = 0;      // 1 : GPU の値が読めた
		uint32_t Visible   = 0;      // 生きている物のうち画面に映った数 (描いた数)
	};
	static_assert(sizeof(ParticleStats) == 36, "ParticleStats must be 36 bytes (same as C#)");

	// ==========================================================================
	// 初期化
	// ==========================================================================
	struct InitializeDesc
	{
		void*    WindowHandle = nullptr;   // HWND
		uint32_t Width        = 0;         // 描画領域の幅 (ウィンドウのクライアント領域)
		uint32_t Height       = 0;
		bool     EnableVSync  = true;
	};

	// 各段階の初期化結果 (Result の値)。失敗した段階より後は NotInitialized のまま
	struct InitializeResult
	{
		int Device         = Result::NotInitialized;
		int CommandContext = Result::NotInitialized;
		int Buffer         = Result::NotInitialized;
		int Texture        = Result::NotInitialized;
		int Pipeline       = Result::NotInitialized;
	};
}

#endif // RHI_PUBLIC_TYPES_H_
