/***********************************************************************************
* @file    gamePad.h
* @brief   XInput と DirectInput のパッドを 1 つの並び (最大 4 台) にまとめるクラス
* @details C# (GameEngine.Runtime の Gamepad) へは controllerDll.cpp から渡す
*          - 番号の割り当て : XInput のユーザー番号 0 ～ 3 が繋がっていればその番号。
*            空いている番号に、DirectInput のパッドを列挙した順に入れる (XInput のパッドが繋がると後ろへずれることがある)
*          - ボタンのビットは XInput (XINPUT_GAMEPAD_*) と同じ並び。L2 / R2 を 0x0400 / 0x0800 (XInput では空き) に入れる
*          - 振動は XInput だけ (DirectInput のフォースフィードバックは扱わない)
*          - DirectInput のパッドの抜き差しは RescanDirectInput を呼んだ時に反映する
*          - DirectInput の列挙は別のスレッドで行う (パッドが 0 台でも 130 ～ 180 ms かかる。列挙中の数フレームは DirectInput のパッドが出てこない)
*
* @author  Keigo Onari
* @date    2026/11/02
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/
#pragma once
#ifndef CONTROLLER_GAMEPAD_H_
#define CONTROLLER_GAMEPAD_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <cstdint>

/******************************************************************
* Macro Definitions
******************************************************************/
#define GAMEPAD_MAX_PADS (4)

// L2 / R2 をボタンとして見る時のビット (XInput のボタンでは使っていない所)
#define GAMEPAD_BUTTON_LEFT_TRIGGER  (0x0400u)
#define GAMEPAD_BUTTON_RIGHT_TRIGGER (0x0800u)

/******************************************************************
* Structure Declarations
******************************************************************/
// C# の GamepadNativeMethods.PadState と同じ並び (36 バイト)
struct ControllerPadState
{
	uint32_t connected;		// 1 = 繋がっている
	uint32_t type;			// 0 = 無し / 1 = XInput / 2 = DirectInput
	uint32_t buttons;		// XINPUT_GAMEPAD_* + GAMEPAD_BUTTON_*_TRIGGER
	float    leftX;			// -1 (左) ～ 1 (右)
	float    leftY;			// -1 (下) ～ 1 (上)
	float    rightX;
	float    rightY;
	float    leftTrigger;	// 0 ～ 1
	float    rightTrigger;
};

namespace ko
{
	/***********************************************************************************
	 * @brief   パッドをまとめて読むクラス (全て静的。呼ぶのは 1 つのスレッドから)
	 ************************************************************************************/
	class GamePad
	{
	public:
		GamePad() = delete;

		// XInput はすぐ使える。DirectInput は別のスレッドで用意する (作れなかったら使わない)
		static bool Initialize(void);
		static void Shutdown(void);

		// 全パッドを読み、outStates[0 ～ count-1] に入れる。戻り値は繋がっている数
		static uint32_t Update(ControllerPadState* outStates, uint32_t count);

		// 振動 (0 ～ 1)。XInput のパッドだけ。送るのは次の Update
		static void SetVibration(uint32_t pad, float left, float right);

		// DirectInput のパッドを別のスレッドで列挙し直す (列挙中は DirectInput のパッドが出てこない)。戻り値は始めたか (列挙中なら false)
		static bool RescanDirectInput(void);

		// DirectInput のキーコンフィグ (pad は GamePad の番号。DirectInput のパッドでなければ false)
		static bool BeginButtonConfig(uint32_t pad);
		static int32_t GetButtonConfigStep(void);	// -1 = していない / 0 ～ 9 = 待っているボタン (A B X Y L L2 R R2 Start Select)
	};
}

#endif // CONTROLLER_GAMEPAD_H_

/************************************
* End of file
************************************/
