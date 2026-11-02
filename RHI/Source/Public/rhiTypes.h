
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
	};

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
	};

	struct RenderTargetHandle
	{
		static constexpr uint32_t INVALID     = 0xFFFFFFFFu;
		static constexpr uint32_t BACK_BUFFER = 0xFFFFFFFEu;   // 画面 (スワップチェーン)
		static constexpr uint32_t SCENE_DEPTH = 0xFFFFFFFDu;   // シーンの深度 (GPU カリングのオクルージョンもこれを使う)
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
	};

	// シーンのどの物を描くか
	enum class RenderQueue : uint32_t
	{
		Opaque      = 0,   // 不透明 (GPU カリング / オクルージョンはここで行う)
		Transparent = 1,   // 半透明 (Opaque の後に描くこと)
	};

	enum class RenderCommandType : uint32_t
	{
		SetRenderTargets = 0,   // Colors[0..ColorCount) と Depth を描画先にする
		ClearColor       = 1,   // 描画先の色を ClearColor で消す
		ClearDepth       = 2,   // 描画先の深度を ClearDepth で消す
		DrawScene        = 3,   // Arg0 = ScenePass / Arg1 = RenderQueue
		DrawFullscreen   = 4,   // Arg0 = シェーダー / Arg1 = ブレンド (0 なし / 1 加算 / 2 アルファ) / Inputs / Params
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
