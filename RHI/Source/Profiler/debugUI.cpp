
/***********************************************************************************
* @file    debugUI.cpp
* @brief   ImGui の組み込み (初期化 / 入力 / 描画)
* @details
*
* @author  Keigo Onari
* @date    2026/11/01
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/


/******************************************************************
* Include Headers
******************************************************************/
#include "debugUI.h"

#include "../DirectX12/dx12_descriptorHeapManager.h"
#include "../../../Core/Source/Log/logger.h"

#include "../../../ImGui/imgui.h"
#include "../../../ImGui/imgui_impl_win32.h"
#include "../../../ImGui/imgui_impl_dx12.h"

// imgui_impl_win32.h では #if 0 で囲まれているので自分で宣言する
extern IMGUI_IMPL_API LRESULT ImGui_ImplWin32_WndProcHandler(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam);

namespace RHI
{
	namespace
	{
		// ウィンドウプロシージャから自分を見つけるため (ウィンドウは 1 つ)
		DebugUI* g_instance     = nullptr;
		// 元のウィンドウプロシージャ (WinForms のもの)。DLL が生きている間は保持し続ける
		WNDPROC  g_originalProc = nullptr;
	}

	DebugUI::DebugUI() = default;
	DebugUI::~DebugUI()
	{
		Shutdown();
	}

	bool DebugUI::Initialize(HWND hwnd, ID3D12Device* device, DirectX12::DescriptorHeapManager* heapManager,
	                         UINT frameCount, DXGI_FORMAT rtvFormat)
	{
		if (_initialized || !hwnd || !device || !heapManager)
		{
			return false;
		}

		// フォントのテクスチャ用の SRV の枠
		_fontSrvIndex = heapManager->AllocateSrv();
		if (_fontSrvIndex == DirectX12::DescriptorHeapManager::INVALID_INDEX)
		{
			Core::Logger::Log("[DebugUI] SRV heap is full.");
			return false;
		}
		_heapManager = heapManager;

		IMGUI_CHECKVERSION();
		ImGui::CreateContext();
		ImGuiIO& io = ImGui::GetIO();
		io.ConfigFlags |= ImGuiConfigFlags_NavEnableKeyboard;
		// ウィンドウの位置・大きさは実行フォルダの imgui.ini に保存される
		ImGui::StyleColorsDark();

		if (!ImGui_ImplWin32_Init(hwnd))
		{
			Core::Logger::Log("[DebugUI] Failed ImGui_ImplWin32_Init.");
			ImGui::DestroyContext();
			return false;
		}

		// シーンと同じシェーダーから見えるヒープを使う (描画中にヒープを切り替えずに済む)
		if (!ImGui_ImplDX12_Init(device, static_cast<int>(frameCount), rtvFormat, heapManager->GetSrvHeap(),
		                         heapManager->GetSrvCpuHandle(_fontSrvIndex), heapManager->GetSrvGpuHandle(_fontSrvIndex)))
		{
			Core::Logger::Log("[DebugUI] Failed ImGui_ImplDX12_Init.");
			ImGui_ImplWin32_Shutdown();
			ImGui::DestroyContext();
			return false;
		}

		// ウィンドウのメッセージを横取りして ImGui に渡す (C# 側の変更なしで入力を受け取れる)
		_hwnd       = hwnd;
		g_instance  = this;
		g_originalProc = reinterpret_cast<WNDPROC>(SetWindowLongPtrW(hwnd, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(&DebugUI::wndProc)));

		_initialized = true;
		_visible     = true;
		Core::Logger::Log("[DebugUI] ImGui initialized. (F1 : show / hide profiler)");
		return true;
	}

	void DebugUI::Shutdown()
	{
		if (!_initialized) return;

		// メッセージの横取りをやめる (自分が一番外側の時だけ戻す。後から誰かが重ねていたら触らない)
		if (_hwnd && IsWindow(_hwnd) &&
			GetWindowLongPtrW(_hwnd, GWLP_WNDPROC) == reinterpret_cast<LONG_PTR>(&DebugUI::wndProc) && g_originalProc)
		{
			SetWindowLongPtrW(_hwnd, GWLP_WNDPROC, reinterpret_cast<LONG_PTR>(g_originalProc));
		}
		g_instance = nullptr;

		// ※呼ぶ前に GPU の完了を待っておくこと (ImGui の頂点バッファ / フォントを GPU が使っている可能性がある)
		ImGui_ImplDX12_Shutdown();
		ImGui_ImplWin32_Shutdown();
		ImGui::DestroyContext();

		if (_heapManager && _fontSrvIndex != DirectX12::DescriptorHeapManager::INVALID_INDEX)
		{
			_heapManager->FreeSrv(_fontSrvIndex);
		}
		_fontSrvIndex = DirectX12::DescriptorHeapManager::INVALID_INDEX;
		_heapManager  = nullptr;
		_hwnd         = nullptr;
		_initialized  = false;
		_inFrame      = false;
	}

	void DebugUI::BeginFrame()
	{
		if (!IsVisible() || _inFrame) return;

		ImGui_ImplDX12_NewFrame();
		ImGui_ImplWin32_NewFrame();   // 画面サイズ・マウス位置などを取る
		ImGui::NewFrame();
		_inFrame = true;
	}

	void DebugUI::EndFrame(ID3D12GraphicsCommandList* cmdList)
	{
		if (!_inFrame) return;
		_inFrame = false;

		ImGui::Render();
		if (cmdList)
		{
			ImGui_ImplDX12_RenderDrawData(ImGui::GetDrawData(), cmdList);
		}
	}

	LRESULT CALLBACK DebugUI::wndProc(HWND hwnd, UINT msg, WPARAM wParam, LPARAM lParam)
	{
		if (g_instance && g_instance->_initialized)
		{
			// F1 : プロファイラーの表示 / 非表示 (非表示中も効くようにここで見る)
			if (msg == WM_KEYDOWN && wParam == VK_F1 && (lParam & (1 << 30)) == 0)   // 押しっぱなしの連続入力は無視
			{
				g_instance->_visible = !g_instance->_visible;
			}

			// 表示中だけ ImGui に入力を渡す
			if (g_instance->_visible && ImGui_ImplWin32_WndProcHandler(hwnd, msg, wParam, lParam))
			{
				return TRUE;
			}
		}

		// 残りは元のウィンドウプロシージャ (WinForms) へ
		return g_originalProc ? CallWindowProcW(g_originalProc, hwnd, msg, wParam, lParam) : DefWindowProcW(hwnd, msg, wParam, lParam);
	}
}

/************************************
* End of file
************************************/
