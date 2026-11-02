
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
