/************************************************
* xInput.cpp
* XInputクラス
*
* 最終更新日:2026/11/02 (Core.dll へ入れる時の修正。内容は xInput.h)
* 作成者：Keigo Onari
*************************************************/

// =============================================================
// インクルード
// =============================================================
#include "xInput.h"
#include <math.h>

// =============================================================
// マクロ定義
// =============================================================
// 以前は左右とも 2200 だった。XInput の推奨値 (XINPUT_GAMEPAD_LEFT/RIGHT_THUMB_DEADZONE = 7849 / 8689) より小さく、
// 古いパッドでは触っていないのに少し動く。左右とも推奨値に合わせる
#define LEFT_DEAD_ZONE  (XINPUT_GAMEPAD_LEFT_THUMB_DEADZONE)
#define RIGHT_DEAD_ZONE (XINPUT_GAMEPAD_RIGHT_THUMB_DEADZONE)

namespace ko
{
	XInput::CONTROLLER_STATE XInput::_controllers[MAX_CONTROLLER_NUM] = { };

	namespace
	{
		// =============================================================
		// スティックの値 (-32768 ～ 32767) を、円形のデッドゾーンを除いて -1 ～ 1 にする
		// 以前はデッドゾーンの中を 0 にするだけで、外側は元の値のまま割っていた (端で 0 → 0.07 に飛ぶ)
		// =============================================================
		void ApplyDeadZone(SHORT rawX, SHORT rawY, float deadZone, float& outX, float& outY)
		{
			const float x = (float)rawX;
			const float y = (float)rawY;
			float magnitude = sqrtf(x * x + y * y);
			if (magnitude <= deadZone)
			{
				outX = 0.0f;
				outY = 0.0f;
				return;
			}

			// 向きはそのまま、長さだけ deadZone ～ 32767 を 0 ～ 1 に広げ直す
			const float clamped = magnitude > 32767.0f ? 32767.0f : magnitude;
			const float scale = (clamped - deadZone) / (32767.0f - deadZone) / magnitude;
			outX = x * scale;
			outY = y * scale;
		}

		// 0 ～ 255 → 0 ～ 65535。0 は 0 (以前は ((speed + 1) * 256) - 1 で、0 でも 255 になっていた)
		WORD ToMotorSpeed(int speed)
		{
			if (speed <= 0) return 0;
			if (speed >= 255) return 65535;
			return (WORD)(speed * 257);
		}
	}

	// =============================================================
	// 初期化(コントローラー有効化)
	// =============================================================
	void XInput::Init(void)
	{
		// 以前は XInputEnable(TRUE) を呼んでいた。XInputEnable は「ウィンドウが裏に回った時に振動を止める」ための関数で、
		// 呼ばなくても有効 (XInput 1.4 は既定で TRUE)。終了時の XInputEnable(FALSE) は他のアプリの振動まで止めかねないので呼ばない
		for (auto& controller : _controllers)
			controller = CONTROLLER_STATE();
	}

	// =============================================================
	// 更新処理
	// =============================================================
	void XInput::Update(ULONGLONG nowMs)
	{
		for (DWORD i = 0; i < MAX_CONTROLLER_NUM; i++)
		{
			CONTROLLER_STATE& controller = _controllers[i];

			// 未接続のスロットは間隔を空けて調べる (未接続の XInputGetState は接続中より桁違いに遅い)
			if (!controller.connected && nowMs < controller.nextPollMs)
				continue;

			//正常：ERROR_SUCCESS　0
			//接続無し：ERROR_DEVICE_NOT_CONNECTED 1167
			const DWORD result = XInputGetState(i, &controller.state);
			if (result != ERROR_SUCCESS)
			{
				// 抜けた / 元から無い。前の値が残らないように消す (押しっぱなし・振動の指定も残さない。挿し直した時に急に震えない)
				const ULONGLONG next = nowMs + RECONNECT_INTERVAL_MS + i * (RECONNECT_INTERVAL_MS / MAX_CONTROLLER_NUM);
				controller = CONTROLLER_STATE();
				controller.nextPollMs = next;
				continue;
			}

			if (!controller.connected)
			{
				// 繋がった瞬間は、前のフレームのボタンを今と同じにする (挿した時に押した扱いにしない)
				controller.connected = true;
				controller.lastButtons = controller.state.Gamepad.wButtons;
			}

			// 振動は値が変わった時だけ送る (以前は毎フレーム全スロットに送っていた)
			if (controller.vibrationDirty)
			{
				XInputSetState(i, &controller.vibration);
				controller.vibrationDirty = false;
			}

			//トリガー作成
			const WORD buttons = controller.state.Gamepad.wButtons;
			controller.trigger = (WORD)((controller.lastButtons ^ buttons) & buttons);
			controller.lastButtons = buttons;

			//スティック情報の作成
			ApplyDeadZone(controller.state.Gamepad.sThumbLX, controller.state.Gamepad.sThumbLY, (float)LEFT_DEAD_ZONE, controller.leftX, controller.leftY);
			ApplyDeadZone(controller.state.Gamepad.sThumbRX, controller.state.Gamepad.sThumbRY, (float)RIGHT_DEAD_ZONE, controller.rightX, controller.rightY);
		}
	}

	// =============================================================
	// 終了処理
	// =============================================================
	void XInput::End(void)
	{
		// 振動していたパッドだけ止める
		for (DWORD i = 0; i < MAX_CONTROLLER_NUM; i++)
		{
			const XINPUT_VIBRATION& v = _controllers[i].vibration;
			if (_controllers[i].connected && (v.wLeftMotorSpeed != 0 || v.wRightMotorSpeed != 0))
			{
				XINPUT_VIBRATION stop = {};
				XInputSetState(i, &stop);
			}
		}

		for (auto& controller : _controllers)
			controller = CONTROLLER_STATE();
	}

	// =============================================================
	// 接続しているか
	// =============================================================
	bool XInput::IsConnected(int padNo)
	{
		return isValid(padNo) && _controllers[padNo].connected;
	}

	// =============================================================
	// LスティックX軸値取得
	// =============================================================
	float XInput::GetThumbLeftX(int padNo)
	{
		return isValid(padNo) ? _controllers[padNo].leftX : 0.0f;
	}

	// =============================================================
	// LスティックY軸値取得
	// =============================================================
	float XInput::GetThumbLeftY(int padNo)
	{
		return isValid(padNo) ? _controllers[padNo].leftY : 0.0f;
	}

	// =============================================================
	// RスティックX軸値取得
	// =============================================================
	float XInput::GetThumbRightX(int padNo)
	{
		return isValid(padNo) ? _controllers[padNo].rightX : 0.0f;
	}

	// =============================================================
	// RスティックY軸値取得
	// =============================================================
	float XInput::GetThumbRightY(int padNo)
	{
		return isValid(padNo) ? _controllers[padNo].rightY : 0.0f;
	}

	// =============================================================
	// 左側バイブレーション実行
	// =============================================================
	void XInput::SetLeftVibration(int padNo, int speed)
	{
		if (!isValid(padNo)) return;
		SetMotorSpeed(padNo, ToMotorSpeed(speed), _controllers[padNo].vibration.wRightMotorSpeed);
	}

	// =============================================================
	// 右側バイブレーション実行
	// =============================================================
	void XInput::SetRightVibration(int padNo, int speed)
	{
		if (!isValid(padNo)) return;
		SetMotorSpeed(padNo, _controllers[padNo].vibration.wLeftMotorSpeed, ToMotorSpeed(speed));
	}

	// =============================================================
	// 両バイブレーション実行
	// =============================================================
	void XInput::SetVibration(int padNo, int speed)
	{
		SetMotorSpeed(padNo, ToMotorSpeed(speed), ToMotorSpeed(speed));
	}

	// =============================================================
	// モーターの強さを直接指定
	// =============================================================
	void XInput::SetMotorSpeed(int padNo, WORD left, WORD right)
	{
		if (!isValid(padNo)) return;
		XINPUT_VIBRATION& v = _controllers[padNo].vibration;
		if (v.wLeftMotorSpeed == left && v.wRightMotorSpeed == right)
			return;
		v.wLeftMotorSpeed = left;
		v.wRightMotorSpeed = right;
		_controllers[padNo].vibrationDirty = true;
	}

	// =============================================================
	// バイブレーションの停止
	// =============================================================
	void XInput::StopVibration(int padNo)
	{
		SetMotorSpeed(padNo, 0, 0);
	}

	// =============================================================
	// Lトリガー値取得
	// =============================================================
	int XInput::GetLeftTrigger(int padNo)
	{
		return isValid(padNo) ? _controllers[padNo].state.Gamepad.bLeftTrigger : 0;
	}

	// =============================================================
	// Rトリガー値取得
	// =============================================================
	int XInput::GetRightTrigger(int padNo)
	{
		return isValid(padNo) ? _controllers[padNo].state.Gamepad.bRightTrigger : 0;
	}

	// =============================================================
	// 全ボタンの押下状態
	// =============================================================
	WORD XInput::GetButtons(int padNo)
	{
		return isValid(padNo) ? _controllers[padNo].state.Gamepad.wButtons : 0;
	}

	// =============================================================
	// ボタン押下取得
	// =============================================================
	BOOL XInput::IsButtonPressed(int padNo, DWORD button)
	{
		return isValid(padNo) && (button & _controllers[padNo].state.Gamepad.wButtons) != 0;
	}

	// =============================================================
	// ボタン押した瞬間取得
	// =============================================================
	BOOL XInput::IsButtonTriggered(int padNo, DWORD button)
	{
		return isValid(padNo) && (button & _controllers[padNo].trigger) != 0;
	}
}
