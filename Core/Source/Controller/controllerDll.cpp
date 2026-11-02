/***********************************************************************************
* @file    controllerDll.cpp
* @brief   Core.dll がコントローラー (ゲームパッド) について C# のエンジン (GameEngine.Runtime) へ export する関数
* @details C# 側の宣言は GameEngine.Runtime/Native/GamepadNativeMethods.cs
*          - 毎フレームの状態は Controller_Update 1 回で、全パッド分を配列で受け取る (P/Invoke の回数を増やさない)
*          - パッドは番号 (0 ～ 3) でやり取りする。XInput / DirectInput の違いは C# に見せない (type で分かるだけ)
*          - 呼び出しは C# のメインスレッドから (Initialize と Shutdown は同じスレッドで)
*          - ログは Core.dll の Logger に出す (Renderer_SetLogCallback で C# のコンソールに流れる)
*          以前のコントローラー (DirectInput / XInput) は C++ のゲームから直接クラスを使う形で、DLL の外へは出ていなかった
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
#include "../common.h"
#include "gamePad.h"

extern "C"
{
	// ==============================================================
	// 初期化 / 終了
	// ==============================================================

	// 戻り値 : 常に true (XInput はすぐ読める。DirectInput は別のスレッドで用意し、終わった後の Controller_Update から出てくる)
	CORE_API bool Controller_Initialize()
	{
		return ko::GamePad::Initialize();
	}

	// 振動を止めてから解放する
	CORE_API void Controller_Shutdown()
	{
		ko::GamePad::Shutdown();
	}

	// ==============================================================
	// 毎フレーム
	// ==============================================================

	// 全パッドを読み、outStates[0 ～ count-1] に入れる (count が 4 より多い分は「繋がっていない」で埋める)
	// 戻り値 : 繋がっているパッドの数
	CORE_API uint32_t Controller_Update(ControllerPadState* outStates, uint32_t count)
	{
		return ko::GamePad::Update(outStates, count);
	}

	// 振動 (0 ～ 1。左 = 低い周波数 / 右 = 高い周波数)。XInput のパッドだけ。送るのは次の Controller_Update
	CORE_API void Controller_SetVibration(uint32_t pad, float left, float right)
	{
		ko::GamePad::SetVibration(pad, left, right);
	}

	// ==============================================================
	// DirectInput
	// ==============================================================

	// パッドを別のスレッドで列挙し直す (抜き差しの反映。100 ms ほどかかり、その間 DirectInput のパッドは出てこない)
	// 戻り値 : 始めたか (列挙中なら false)。台数はログに出る
	CORE_API bool Controller_RescanDevices()
	{
		return ko::GamePad::RescanDirectInput();
	}

	// ボタンの割り当て (キーコンフィグ) を始める。A B X Y L L2 R R2 Start Select の順に押してもらう
	// 戻り値 : 始められたか (pad が DirectInput のパッドでない時は false)
	CORE_API bool Controller_BeginButtonConfig(uint32_t pad)
	{
		return ko::GamePad::BeginButtonConfig(pad);
	}

	// -1 = していない (終わった) / 0 ～ 9 = 次に押してほしいボタンの番号
	CORE_API int32_t Controller_GetButtonConfigStep()
	{
		return ko::GamePad::GetButtonConfigStep();
	}
}

/************************************
* End of file
************************************/
