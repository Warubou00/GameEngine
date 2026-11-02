/***********************************************************************************
* @file    gamePad.cpp
* @brief   XInput と DirectInput のパッドを 1 つの並び (最大 4 台) にまとめるクラス
* @details 内容は gamePad.h
*
* @author  Keigo Onari
* @date    2026/11/02
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/

/******************************************************************
* Include Headers
******************************************************************/
#include "gamePad.h"
#include "xInput/xInput.h"
#include "diectInput/di.h"
#include "diectInput/diGamePad.h"
#include "../Log/logger.h"
#include <cstdio>
#include <atomic>
#include <thread>

namespace ko
{
	namespace
	{
		enum PadType : uint32_t
		{
			PAD_NONE = 0,
			PAD_XINPUT = 1,
			PAD_DIRECTINPUT = 2,
		};

		bool g_initialized = false;

		// DirectInput のパッドを読めるか (メインスレッドだけが触る)。列挙中は false
		bool g_directInputReady = false;

		// 列挙は別のスレッドで行う (パッドが 0 台でも 130 ～ 180 ms かかり、メインスレッドで行うとその間フレームが止まる)
		// 列挙中は、メインスレッドは DirectInput / DirectInputGamePad に触らない (終わったら join してから使う)
		std::thread       g_scanThread;
		std::atomic<bool> g_scanDone{ false };
		bool   g_scanCreated = false;		// DirectInput8Create できたか
		double g_scanCreateMs = 0.0;		// DirectInput8Create にかかった時間 (2 回目以降は 0)
		double g_scanEnumMs = 0.0;			// 列挙にかかった時間
		int    g_scanCount = 0;				// 何回目の列挙か

		// 前の Update で決めた番号 → DirectInput のパッドの番号 (-1 = DirectInput ではない)
		int g_slotToDirectInput[GAMEPAD_MAX_PADS] = { -1, -1, -1, -1 };

		// キーコンフィグ中の DirectInput のパッドの番号 (-1 = していない)
		int g_configDirectInputPad = -1;

		float Clamp01(float value)
		{
			return value < 0.0f ? 0.0f : (value > 1.0f ? 1.0f : value);
		}

		// DirectInput のボタン (BUTTON_*) → XInput の並び
		uint32_t DirectInputToXInputButtons(DWORD di)
		{
			uint32_t result = 0;
			if (di & BUTTON_UP)     result |= XINPUT_GAMEPAD_DPAD_UP;
			if (di & BUTTON_DOWN)   result |= XINPUT_GAMEPAD_DPAD_DOWN;
			if (di & BUTTON_LEFT)   result |= XINPUT_GAMEPAD_DPAD_LEFT;
			if (di & BUTTON_RIGHT)  result |= XINPUT_GAMEPAD_DPAD_RIGHT;
			if (di & BUTTON_START)  result |= XINPUT_GAMEPAD_START;
			if (di & BUTTON_SELECT) result |= XINPUT_GAMEPAD_BACK;
			if (di & BUTTON_L3)     result |= XINPUT_GAMEPAD_LEFT_THUMB;
			if (di & BUTTON_R3)     result |= XINPUT_GAMEPAD_RIGHT_THUMB;
			if (di & BUTTON_L)      result |= XINPUT_GAMEPAD_LEFT_SHOULDER;
			if (di & BUTTON_R)      result |= XINPUT_GAMEPAD_RIGHT_SHOULDER;
			if (di & BUTTON_L2)     result |= GAMEPAD_BUTTON_LEFT_TRIGGER;
			if (di & BUTTON_R2)     result |= GAMEPAD_BUTTON_RIGHT_TRIGGER;
			if (di & BUTTON_A)      result |= XINPUT_GAMEPAD_A;
			if (di & BUTTON_B)      result |= XINPUT_GAMEPAD_B;
			if (di & BUTTON_X)      result |= XINPUT_GAMEPAD_X;
			if (di & BUTTON_Y)      result |= XINPUT_GAMEPAD_Y;
			return result;
		}

		double ElapsedMs(LARGE_INTEGER start)
		{
			LARGE_INTEGER now, frequency;
			QueryPerformanceCounter(&now);
			QueryPerformanceFrequency(&frequency);
			return (double)(now.QuadPart - start.QuadPart) * 1000.0 / (double)frequency.QuadPart;
		}

		// =============================================================
		// 列挙のスレッド (DirectInput8Create → パッドの列挙)
		// =============================================================
		void ScanDirectInput(void)
		{
			LARGE_INTEGER start;
			QueryPerformanceCounter(&start);
			g_scanCreateMs = 0.0;
			if (!DirectInput::getInstance()->GetDevice())
			{
				// HINSTANCE は exe のもの (DirectInput8Create はバージョンの確認にだけ使う)
				DirectInput::getInstance()->Init(GetModuleHandle(nullptr));
				g_scanCreateMs = ElapsedMs(start);
				QueryPerformanceCounter(&start);
			}
			g_scanCreated = DirectInput::getInstance()->GetDevice() != nullptr;
			if (g_scanCreated)
				DirectInputGamePad::getInstance()->Init();
			g_scanEnumMs = ElapsedMs(start);
			g_scanDone.store(true, std::memory_order_release);
		}

		// 列挙を始める (列挙中なら何もしない)
		bool StartScan(void)
		{
			if (g_scanThread.joinable())
				return false;

			// 番号が変わるかもしれないのでキーコンフィグは止める (状態 _pDigpb は次の BeginKeyConfig / Release で消える)
			if (g_configDirectInputPad >= 0)
			{
				DirectInputGamePad::getInstance()->SetInitGamePadFlag(false);
				g_configDirectInputPad = -1;
			}
			for (int& slot : g_slotToDirectInput) slot = -1;

			g_directInputReady = false;
			g_scanDone.store(false, std::memory_order_relaxed);
			g_scanThread = std::thread(ScanDirectInput);
			return true;
		}

		// 列挙が終わっていたら受け取る (wait = true なら終わるまで待つ)
		void FinishScan(bool wait)
		{
			if (!g_scanThread.joinable())
				return;
			if (!wait && !g_scanDone.load(std::memory_order_acquire))
				return;

			g_scanThread.join();
			g_directInputReady = g_scanCreated;
			g_scanCount++;

			char message[160];
			if (g_scanCreated)
			{
				sprintf_s(message, "[Controller] DirectInput %s : %d pad(s) (create %.2f ms + enumerate %.2f ms, worker thread)",
					g_scanCount == 1 ? "init" : "rescan", DirectInputGamePad::getInstance()->GetPadDeviceNum(), g_scanCreateMs, g_scanEnumMs);
			}
			else
			{
				sprintf_s(message, "[Controller] DirectInput8Create failed. Only XInput pads are used.");
			}
			Core::Logger::Log(message);
		}
	}

	// =============================================================
	// 初期化
	// =============================================================
	bool GamePad::Initialize(void)
	{
		if (g_initialized)
			return true;

		XInput::Init();

		for (int& slot : g_slotToDirectInput) slot = -1;
		g_configDirectInputPad = -1;
		g_scanCount = 0;
		g_initialized = true;

		// DirectInput は別のスレッドで用意する (終わった後の Update から DirectInput のパッドが出てくる)
		StartScan();
		return true;
	}

	// =============================================================
	// 終了 (振動を止めてから DirectInput を解放する)
	// =============================================================
	void GamePad::Shutdown(void)
	{
		if (!g_initialized)
			return;

		XInput::End();
		FinishScan(true);	// 列挙中なら終わるまで待つ (列挙のスレッドがデバイスを作っている最中に解放しない)
		DirectInputGamePad::getInstance()->Release();
		DirectInput::getInstance()->Release();
		g_directInputReady = false;
		g_configDirectInputPad = -1;
		g_initialized = false;
	}

	// =============================================================
	// 全パッドを読む
	// =============================================================
	uint32_t GamePad::Update(ControllerPadState* outStates, uint32_t count)
	{
		ControllerPadState states[GAMEPAD_MAX_PADS] = {};
		uint32_t connectedCount = 0;

		if (g_initialized)
		{
			XInput::Update(GetTickCount64());
			FinishScan(false);

			DirectInputGamePad* di = DirectInputGamePad::getInstance();
			if (g_directInputReady)
			{
				// キーコンフィグ中はそのパッドだけ読む (押したボタンがゲームの操作にならないよう、DirectInput のパッドは入力なしで返す)
				if (g_configDirectInputPad >= 0)
				{
					di->UpdateSetKeyConfig(g_configDirectInputPad);
					if (!di->GetInitGamePadFlag())
					{
						g_configDirectInputPad = -1;
						Core::Logger::Log("[Controller] DirectInput button config finished.");
					}
				}
				else
				{
					di->Update();
				}
			}

			// XInput のパッドは自分の番号へ
			for (int i = 0; i < GAMEPAD_MAX_PADS; i++)
			{
				g_slotToDirectInput[i] = -1;
				if (!XInput::IsConnected(i))
					continue;

				ControllerPadState& s = states[i];
				s.connected = 1;
				s.type = PAD_XINPUT;
				s.buttons = XInput::GetButtons(i);
				s.leftX = XInput::GetThumbLeftX(i);
				s.leftY = XInput::GetThumbLeftY(i);
				s.rightX = XInput::GetThumbRightX(i);
				s.rightY = XInput::GetThumbRightY(i);
				s.leftTrigger = XInput::GetLeftTrigger(i) / 255.0f;
				s.rightTrigger = XInput::GetRightTrigger(i) / 255.0f;
				if (XInput::GetLeftTrigger(i) > XINPUT_GAMEPAD_TRIGGER_THRESHOLD)  s.buttons |= GAMEPAD_BUTTON_LEFT_TRIGGER;
				if (XInput::GetRightTrigger(i) > XINPUT_GAMEPAD_TRIGGER_THRESHOLD) s.buttons |= GAMEPAD_BUTTON_RIGHT_TRIGGER;
			}

			// DirectInput のパッドは空いている番号へ
			if (g_directInputReady)
			{
				int slot = 0;
				for (int d = 0; d < di->GetPadDeviceNum(); d++)
				{
					if (!di->IsConnected(d))
						continue;
					while (slot < GAMEPAD_MAX_PADS && states[slot].connected)
						slot++;
					if (slot >= GAMEPAD_MAX_PADS)
						break;

					g_slotToDirectInput[slot] = d;
					ControllerPadState& s = states[slot];
					s.connected = 1;
					s.type = PAD_DIRECTINPUT;
					if (g_configDirectInputPad < 0)
					{
						s.buttons = DirectInputToXInputButtons(di->GetButtons(d));
						s.leftX = di->GetAnalogPadX(d);
						s.leftY = di->GetAnalogPadY(d);
						s.rightX = di->GetAnalogPadRX(d);
						s.rightY = di->GetAnalogPadRY(d);
						s.leftTrigger = Clamp01(di->GetTrigger(d, false));
						s.rightTrigger = Clamp01(di->GetTrigger(d, true));
					}
				}
			}

			for (const ControllerPadState& s : states)
				connectedCount += s.connected;
		}

		if (outStates)
		{
			const uint32_t n = count < GAMEPAD_MAX_PADS ? count : GAMEPAD_MAX_PADS;
			for (uint32_t i = 0; i < n; i++)
				outStates[i] = states[i];
			for (uint32_t i = n; i < count; i++)
				outStates[i] = ControllerPadState();
		}
		return connectedCount;
	}

	// =============================================================
	// 振動 (XInput だけ)
	// =============================================================
	void GamePad::SetVibration(uint32_t pad, float left, float right)
	{
		// 繋がっていない番号 / DirectInput のパッドは何もしない
		if (!g_initialized || pad >= GAMEPAD_MAX_PADS || !XInput::IsConnected((int)pad))
			return;
		XInput::SetMotorSpeed((int)pad, (WORD)(Clamp01(left) * 65535.0f + 0.5f), (WORD)(Clamp01(right) * 65535.0f + 0.5f));
	}

	// =============================================================
	// DirectInput のパッドを列挙し直す
	// =============================================================
	bool GamePad::RescanDirectInput(void)
	{
		// DirectInput8Create に失敗していた時も、もう一度試す
		if (!g_initialized)
			return false;
		return StartScan();
	}

	// =============================================================
	// DirectInput のキーコンフィグ
	// =============================================================
	bool GamePad::BeginButtonConfig(uint32_t pad)
	{
		if (!g_initialized || !g_directInputReady || pad >= GAMEPAD_MAX_PADS || g_slotToDirectInput[pad] < 0)
			return false;

		g_configDirectInputPad = g_slotToDirectInput[pad];
		DirectInputGamePad::getInstance()->BeginKeyConfig();
		return true;
	}

	int32_t GamePad::GetButtonConfigStep(void)
	{
		if (g_configDirectInputPad < 0)
			return -1;
		const int step = DirectInputGamePad::getInstance()->GetKeyConfigStep();
		return step < 10 ? step : 9;	// 10 (END) は次の Update で終わる
	}
}

/************************************
* End of file
************************************/
