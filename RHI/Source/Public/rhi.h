
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

		// ======================================================================
		// データの登録 (GPU への転送は次の Render でまとめて行われる)
		// ======================================================================

		// 頂点をメガ頂点バッファへ (失敗時は IsValid() == false)
		virtual VertexRange UploadVertices(const Vertex* vertices, uint32_t count) = 0;

		// インデックスをメガインデックスバッファへ (値は VertexRange::BaseVertex からの相対)
		virtual IndexRange UploadIndices(const uint32_t* indices, uint32_t count) = 0;

		// 画像ファイルをまとめて読み込む (デコードは並列)。失敗したものは TextureHandle::DEFAULT
		virtual void LoadTextures(const wchar_t* const* filePaths, uint32_t count, TextureHandle* outHandles) = 0;

		// 描画オブジェクト (ワールド行列 / テクスチャの置き場) を作る (削除された番号があれば使い回す)
		virtual ObjectHandle CreateObject() = 0;

		// 描画オブジェクトを削除する。削除したオブジェクトを指す DrawItem は、次の Render から渡さないこと
		virtual void DestroyObject(ObjectHandle object) = 0;

		// オブジェクトの定数を更新する (毎フレーム呼んでもよい。GPU が使用中の領域は書き換えない)
		virtual void UpdateObject(ObjectHandle object, const ObjectConstants& constants) = 0;

		// カメラ (DirectXMath の普通の行列のまま渡す。転置は RHI の中で行う)
		virtual void SetViewProjection(const ::DirectX::XMFLOAT4X4& view, const ::DirectX::XMFLOAT4X4& projection) = 0;

		// ======================================================================
		// 描画
		// ======================================================================

		// 1 フレーム分の描画 (開始 → 描画 → 表示)。GPU の完了は待たない
		virtual bool Render(const DrawItem* items, uint32_t count) = 0;

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
		// プロファイラー (ImGui のウィンドウ。F1 キーでも表示 / 非表示を切り替えられる)
		// ======================================================================

		virtual void SetProfilerVisible(bool visible) = 0;
		virtual bool IsProfilerVisible()const = 0;

		// 任意の値をプロファイラーに表示する (例: "LoadModel (ms)")。同じ名前なら上書き
		virtual void ReportProfilerValue(const char* name, double value) = 0;
	};
}

// RHI.dll が export する関数 (名前が変わらないよう extern "C")
extern "C"
{
	RHI_API RHI::IRenderHardwareInterface* RHI_CreateRenderHardwareInterface();
	RHI_API void                           RHI_DestroyRenderHardwareInterface(RHI::IRenderHardwareInterface* rhi);
}

#endif // RHI_PUBLIC_RHI_H_
