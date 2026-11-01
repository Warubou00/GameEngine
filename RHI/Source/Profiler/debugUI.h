
/***********************************************************************************
* @file    debugUI.h
* @brief   ImGui の組み込み (初期化 / 入力 / 描画)
* @details ImGui はグローバルな状態 (コンテキスト) を持つので、RHI.dll の中だけで使う
*          (複数の DLL から使うと DLL ごとに別の状態になってしまうため)
*
*          入力 : C# (WinForms) のウィンドウのメッセージ処理をサブクラス化して ImGui に渡す
*                 (C# 側の変更なしで ImGui がマウス / キーボードを受け取れる)
*          F1   : 表示 / 非表示の切り替え。非表示の間は ImGui の処理自体を丸ごと省く (負荷ゼロ)
*
* @author  Keigo Onari
* @date    2026/11/01
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/

#pragma once
#ifndef RHI_DEBUG_UI_H_
#define RHI_DEBUG_UI_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <cstdint>
#include "../DirectX12/dx12_header.h"

namespace RHI
{
	namespace DirectX12
	{
		class DescriptorHeapManager;
	}

	class DebugUI
	{
	public:
		DebugUI();
		~DebugUI();

		DebugUI(const DebugUI&) = delete;
		DebugUI& operator=(const DebugUI&) = delete;

		/***********************************************************************************
		 * @brief ImGui の初期化
		 * @details フォントのテクスチャ用に SRV ヒープの枠を 1 つ使う (シーンと同じヒープなので切り替え不要)
		 *
		 * @param[in] UINT frameCount フレームの数 (ImGui の頂点バッファもフレームごとに持たせる)
		 * @param[in] DXGI_FORMAT rtvFormat 描画先 (バックバッファ) のフォーマット
		 ************************************************************************************/
		bool Initialize(HWND hwnd, ID3D12Device* device, DirectX12::DescriptorHeapManager* heapManager,
		                UINT frameCount, DXGI_FORMAT rtvFormat);

		void Shutdown();

		bool IsInitialized()const { return _initialized; }
		bool IsVisible()const     { return _initialized && _visible; }
		void SetVisible(bool visible) { _visible = visible; }

		// ImGui のフレーム開始 (この後 ImGui::Begin などで UI を作る)
		void BeginFrame();

		/***********************************************************************************
		 * @brief UI を確定して描画命令を積む
		 * @details 呼ぶ前に描画先をバックバッファだけにしておくこと
		 *          (ImGui の PSO は深度バッファ無しで作られているため、深度が付いたままだとエラーになる)
		 ************************************************************************************/
		void EndFrame(ID3D12GraphicsCommandList* cmdList);

	private:
		static LRESULT CALLBACK wndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam);

		HWND                              _hwnd         = nullptr;
		DirectX12::DescriptorHeapManager* _heapManager  = nullptr;   // 所有しない
		uint32_t                          _fontSrvIndex = 0xFFFFFFFFu;
		bool                              _initialized  = false;
		bool                              _visible      = true;
		bool                              _inFrame      = false;
	};
}

#endif // RHI_DEBUG_UI_H_
