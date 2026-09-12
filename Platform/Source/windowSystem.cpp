#include "windowSystem.h"

namespace Platform
{
	/// <summary>
	/// ウインドウプロシージャ
	/// </summary>
	/// <param name="hWnd"></param>
	/// <param name="msg"></param>
	/// <param name="wParam"></param>
	/// <param name="lParam"></param>
	/// <returns></returns>
	LRESULT CALLBACK MsgProc(HWND hWnd, UINT msg, WPARAM wParam, LPARAM lParam)
	{
		// ImGuiは一旦無効
		//if (ImGui_ImplWin32_WndProcHandler(hWnd, msg, wParam, lParam))
		//	return true;

		switch (msg)
		{
		case WM_DESTROY:
			PostQuitMessage(0);
			break;

		case WM_KEYDOWN:
			switch (wParam)
			{
			case VK_ESCAPE:					// [ESC]キーが押された
				DestroyWindow(hWnd);		// ウィンドウを破棄するよう指示する
				break;
			case VK_F1:
				//g_MouseX = g_WindowSizeX * 0.5f;
				//g_MouseY = g_WindowSizeY * 0.5f;
				//SetCursorPos(g_MouseX, g_MouseY);
				break;
			}

			break;
		case WM_MOUSEMOVE:
			//g_MouseX = GET_X_LPARAM(lParam) * SCREEN_WIDTH / g_WindowSizeX;
			//g_MouseY = GET_Y_LPARAM(lParam) * SCREEN_HEIGHT / g_WindowSizeY;

			break;
		case WM_SIZE:
			//g_WindowSizeX = static_cast<UINT>(LOWORD(lParam));
			//g_WindowSizeY = static_cast<UINT>(HIWORD(lParam));
			break;
			//case WM_SYSCOMMAND: // altキーを無効化
			//	switch (wParam)
			//	{
			//	case SC_KEYMENU:
			//		SetActiveWindow(hWnd);
			//		return 0;
			//	}

		default:
			return DefWindowProc(hWnd, msg, wParam, lParam);
		}

		return 0;
	}

	/***********************************************************************************
	 * @brief 初期化
	 * @details 初期化
	 *
	 * @param[HWND*] hWnd ウインドウハンドル
	 * @param[HINSTANCE] hInstance アプリケーションハンドル
	 * @param[const TCHAR*] appName アプリ名
	 * @param[int] width ウインドウ幅
	 * @oaram[int] height ウインドウ高さ
	 * @param[int] 水平方向の位置の場所
	 * @param[int] 垂直方向の位置の場所
	 * @return bool 成功の有無
	 ************************************************************************************/
	bool WindowSystem::InitWindow(HWND* hWnd, HINSTANCE hInstance, const TCHAR* appName, int width, int height, int x, int y)
	{
		WNDCLASSEX wc =
		{
			sizeof(WNDCLASSEX),
			CS_CLASSDC,
			MsgProc,
			0,
			0,
			GetModuleHandle(NULL),
			NULL,
			LoadCursor(NULL, IDC_ARROW),
			(HBRUSH)(COLOR_WINDOW + 1),
			NULL,
			appName,
			NULL
		};

		wc.hInstance = hInstance;

		//ウィンドウクラスの登録。
		RegisterClassEx(&wc);

		// ウィンドウの作成。
		*hWnd = CreateWindow(
			appName,
			appName,
			WS_OVERLAPPEDWINDOW,
			x,
			y,
			width + GetSystemMetrics(SM_CXDLGFRAME) * 2,
			height + GetSystemMetrics(SM_CXDLGFRAME) * 2 + GetSystemMetrics(SM_CYCAPTION),
			NULL,
			NULL,
			hInstance,
			NULL
		);

		ShowWindow(*hWnd, SW_SHOW);

		return true;
	}

	/***********************************************************************************
	 * @brief メッセージ処理
	 * @details ゲームループ内の繰り返し条件処理として呼ぶ
	 *
	 * @return bool 続けるか否か
	 ************************************************************************************/
	bool WindowSystem::DispatchWindowMessage()
	{
		MSG msg = { 0 };
		while (WM_QUIT != msg.message) {
			//ウィンドウからのメッセージを受け取る。
			if (PeekMessage(&msg, NULL, 0, 0, PM_REMOVE))
			{
				TranslateMessage(&msg);
				DispatchMessage(&msg);
			}
			else {
				//ウィンドウメッセージが空になった。
				break;
			}
		}
		return msg.message != WM_QUIT;
	}
}