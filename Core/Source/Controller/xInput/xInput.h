/***********************************************************************************
* @file    xInput.h
* @brief   XInputライブラリを使ったコントローラーを接続するクラス
* @details 2026/11/02 Core.dll に入れる時に直した所 (以前は〜だった)
*          - 以前はこのファイルに古い版のクラス定義がもう 1 つ続けて書かれていた (同じガードで読まれない)。消した
*          - 以前は ../vector/vector2.h を読んでいたが Core には無い (Math/Vector/vector2.h)。float 2 つで持つ
*          - 以前は未接続のスロットにも毎フレーム XInputGetState / XInputSetState を呼んでいた
*            (未接続のスロットの XInputGetState は重い)。未接続のスロットは RECONNECT_INTERVAL_MS ごとに 1 回だけ調べる
*          - 以前は振動を毎フレーム全スロットに送っていた。値が変わった時だけ送る
*          - 以前は振動の強さ 0 でも ((0 + 1) * 256) - 1 = 255 になり、止まらずに弱く震えていた
*          - 以前は右スティックの Y の符号を左スティック (sThumbLY) で見ていた
*          - 以前はデッドゾーンを超えた所で 0 → 0.07 に飛んでいた。デッドゾーンの外を 0 ～ 1 に広げ直す
*          - 以前は padNo の範囲を調べていなかった
*
* @author  Keigo Onari
* @date    2024/7/25
* @version v_1.1.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note
* <ボタン一覧>
* XINPUT_GAMEPAD_DPAD_UP			十字キー上
* XINPUT_GAMEPAD_DPAD_DOWN			十字キー下
* XINPUT_GAMEPAD_DPAD_LEFT			十字キー左
* XINPUT_GAMEPAD_DPAD_RIGHT			十字キー右
* XINPUT_GAMEPAD_START
* XINPUT_GAMEPAD_BACK
* XINPUT_GAMEPAD_LEFT_THUMB			Lスティック押し込み
* XINPUT_GAMEPAD_RIGHT_THUMB		Rスティック押し込み
* XINPUT_GAMEPAD_LEFT_SHOULDER		Lボタン
* XINPUT_GAMEPAD_RIGHT_SHOULDER		Rボタン
* XINPUT_GAMEPAD_A
* XINPUT_GAMEPAD_B
* XINPUT_GAMEPAD_X
* XINPUT_GAMEPAD_Y
* <スティックの範囲>
* X軸 左-1.0　右 1.0
* Y軸 上 1.0　下-1.0
* <トリガーの範囲>
*      0 ～ 255
* <バイブレーションの範囲>
*      0 ～ 255
************************************************************************************/

#pragma once
#ifndef XINPUT_H_
#define XINPUT_H_

/******************************************************************
* Include Headers
******************************************************************/
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <windows.h>
#include <Xinput.h>

/******************************************************************
* Link Library
******************************************************************/
#pragma comment (lib, "xinput.lib")

/******************************************************************
* Macro Definitions
******************************************************************/
#define MAX_CONTROLLER_NUM (4)

// XInputのボタン再定義
#define GAMEPAD_UP     XINPUT_GAMEPAD_DPAD_UP
#define GAMEPAD_DOWN   XINPUT_GAMEPAD_DPAD_DOWN
#define GAMEPAD_LEFT   XINPUT_GAMEPAD_DPAD_LEFT
#define GAMEPAD_RIGHT  XINPUT_GAMEPAD_DPAD_RIGHT
#define GAMEPAD_START  XINPUT_GAMEPAD_START
#define GAMEPAD_SELECT XINPUT_GAMEPAD_BACK
#define GAMEPAD_L2     XINPUT_GAMEPAD_LEFT_THUMB
#define GAMEPAD_R2     XINPUT_GAMEPAD_RIGHT_THUMB
#define GAMEPAD_L1     XINPUT_GAMEPAD_LEFT_SHOULDER
#define GAMEPAD_R1     XINPUT_GAMEPAD_RIGHT_SHOULDER
#define GAMEPAD_A      XINPUT_GAMEPAD_A
#define GAMEPAD_B      XINPUT_GAMEPAD_B
#define GAMEPAD_X      XINPUT_GAMEPAD_X
#define GAMEPAD_Y      XINPUT_GAMEPAD_Y


namespace ko
{

	/***********************************************************************************
	 * @brief   XInputのコントローラーを扱うクラス (全て静的)
	 ************************************************************************************/
	class XInput
	{
	public:          /*---コンストラクタ---*/
		XInput() = delete;


	public:          /*---クラスメソッド---*/

		/***********************************************************************************
		 * @brief 初期化
		 * @details 全スロットを「すぐに調べる」状態にする
		 *
		 * @return 無し
		 * @note 静的
		 ************************************************************************************/
		static void Init(void);


		/***********************************************************************************
		 * @brief 更新
		 * @details 接続中のスロットは毎回、未接続のスロットは RECONNECT_INTERVAL_MS ごとに読む
		 *
		 * @param[ULONGLONG] nowMs 今の時刻 (GetTickCount64)
		 * @return 無し
		 * @note 静的
		 ************************************************************************************/
		static void Update(ULONGLONG nowMs);


		/***********************************************************************************
		 * @brief 終了
		 * @details 振動を止めて状態を消す
		 *
		 * @return 無し
		 * @note 静的
		 ************************************************************************************/
		static void End(void);


	public:          /*---アクセッサー関数群---*/

		/***********************************************************************************
		 * @brief 接続しているか
		 *
		 * @param[int] padNo 接続されているコントローラーの番号 (0 ～ 3)
		 * @return bool 接続中なら true
		 ************************************************************************************/
		static bool IsConnected(int padNo);

		/***********************************************************************************
		 * @brief LスティックX軸値取得
		 *
		 * @param[int] padNo 接続されているコントローラーの番号
		 * @return float -1.0f(左) ～ 1.0f(右)
		 ************************************************************************************/
		static float GetThumbLeftX(int padNo);

		/***********************************************************************************
		 * @brief LスティックY軸値取得
		 *
		 * @param[int] padNo 接続されているコントローラーの番号
		 * @return float -1.0f(下) ～ 1.0f(上)
		 ************************************************************************************/
		static float GetThumbLeftY(int padNo);

		/***********************************************************************************
		 * @brief RスティックX軸値取得
		 *
		 * @param[int] padNo 接続されているコントローラーの番号
		 * @return float -1.0f(左) ～ 1.0f(右)
		 ************************************************************************************/
		static float GetThumbRightX(int padNo);

		/***********************************************************************************
		 * @brief RスティックY軸値取得
		 *
		 * @param[int] padNo 接続されているコントローラーの番号
		 * @return float -1.0f(下) ～ 1.0f(上)
		 ************************************************************************************/
		static float GetThumbRightY(int padNo);

		/***********************************************************************************
		 * @brief 左側バイブレーション実行
		 *
		 * @param[int] padNo 接続されているコントローラーの番号
		 * @param[int] speed 強さ (0 ～ 255。0 で止まる)
		 ************************************************************************************/
		static void SetLeftVibration(int padNo, int speed);

		/***********************************************************************************
		 * @brief 右側バイブレーション実行
		 *
		 * @param[int] padNo 接続されているコントローラーの番号
		 * @param[int] speed 強さ (0 ～ 255。0 で止まる)
		 ************************************************************************************/
		static void SetRightVibration(int padNo, int speed);

		/***********************************************************************************
		 * @brief 両側バイブレーション実行
		 *
		 * @param[int] padNo 接続されているコントローラーの番号
		 * @param[int] speed 強さ (0 ～ 255。0 で止まる)
		 ************************************************************************************/
		static void SetVibration(int padNo, int speed);

		/***********************************************************************************
		 * @brief モーターの強さを直接指定 (0 ～ 65535)
		 * @details 送るのは次の Update (値が変わった時だけ)
		 *
		 * @param[int] padNo 接続されているコントローラーの番号
		 * @param[WORD] left 左 (低い周波数のモーター)
		 * @param[WORD] right 右 (高い周波数のモーター)
		 ************************************************************************************/
		static void SetMotorSpeed(int padNo, WORD left, WORD right);

		/***********************************************************************************
		 * @brief バイブレーション停止
		 *
		 * @param[int] padNo 接続されているコントローラーの番号
		 ************************************************************************************/
		static void StopVibration(int padNo);

		/***********************************************************************************
		 * @brief Lトリガー値取得
		 *
		 * @param[int] padNo 接続されているコントローラーの番号
		 * @return int 0 ～ 255
		 ************************************************************************************/
		static int GetLeftTrigger(int padNo);

		/***********************************************************************************
		 * @brief Rトリガー値取得
		 *
		 * @param[int] padNo 接続されているコントローラーの番号
		 * @return int 0 ～ 255
		 ************************************************************************************/
		static int GetRightTrigger(int padNo);

		/***********************************************************************************
		 * @brief 全ボタンの押下状態 (XINPUT_GAMEPAD_* のビット)
		 *
		 * @param[int] padNo 接続されているコントローラーの番号
		 * @return WORD ボタンのビット
		 ************************************************************************************/
		static WORD GetButtons(int padNo);

		/***********************************************************************************
		 * @brief ボタン押下取得
		 *
		 * @param[int] padNo 接続されているコントローラーの番号
		 * @param[DWORD] button ボタンの種類
		 * @return BOOL 押しているか
		 ************************************************************************************/
		static BOOL IsButtonPressed(int padNo, DWORD button);

		/***********************************************************************************
		 * @brief ボタン押した瞬間取得
		 *
		 * @param[int] padNo 接続されているコントローラーの番号
		 * @param[DWORD] button ボタンの種類
		 * @return BOOL 押した瞬間か
		 ************************************************************************************/
		static BOOL IsButtonTriggered(int padNo, DWORD button);

	public:          /*---定数---*/
		// 未接続のスロットを調べ直す間隔 (ミリ秒)。スロットごとにずらして、同じフレームに重ならないようにする
		static constexpr ULONGLONG RECONNECT_INTERVAL_MS = 1000;

	private:
		struct CONTROLLER_STATE
		{
			XINPUT_STATE state = {};
			WORD  lastButtons = 0;
			WORD  trigger = 0;
			bool  connected = false;
			bool  vibrationDirty = false;		// 次の Update で XInputSetState を呼ぶか
			ULONGLONG nextPollMs = 0;			// 未接続の時、次に調べる時刻
			XINPUT_VIBRATION vibration = {};
			float leftX = 0.0f, leftY = 0.0f;
			float rightX = 0.0f, rightY = 0.0f;
		};

		static CONTROLLER_STATE _controllers[MAX_CONTROLLER_NUM];

	private:          /*---プライベートクラスメソッド---*/
		static bool isValid(int padNo) { return padNo >= 0 && padNo < MAX_CONTROLLER_NUM; }
	};

}


#endif // XINPUT_H_

/************************************
* End of file
************************************/
