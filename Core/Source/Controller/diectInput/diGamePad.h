
/***********************************************************************************
* @file    diGamePad.h
* @brief   DirectInputに対応しているコントローラーとの接続をするクラス
* @details 2026/11/02 Core.dll に入れる時に直した所 (以前は〜だった)
*          - 以前は _gamePadDevice[] を初期化しておらず、パッドが 4 個未満だと Release が不定のポインタを解放して落ちた
*          - 以前は列挙のコールバックで数を調べず、5 個目のパッドで配列の外へ書いていた
*          - 以前は XInput のパッド (Xbox コントローラー) も DirectInput で数えていた。XInput 側で読むので除く
*          - 以前は入力の状態 (_currDijs) が全パッドで 1 つで、スティックはどの番号を指定しても最後のパッドの値だった
*          - 以前は Acquire を DIERR_INPUTLOST の間ずっと繰り返していて、抜いたパッドでフレームが止まることがあった。
*            1 フレーム 1 回だけ試し、だめなら未接続として扱う (状態は 0)
*          - 以前は GetDeviceState に失敗しても前の値を元にボタンを作っていた
*          - 以前は Release で _pDigpb (キーコンフィグの状態) を解放していなかった
*          - 以前はキーコンフィグの更新で全パッドを回し、パッド 2 の今の値とパッド 1 の前の値を比べていた
*          - 右スティック (Z / Rz)・アナログトリガー (Rx / Ry)・十字キー (POV)・L3 / R3 を足した
*
* @author  Keigo Onari
* @date    2024/3/3
* @version v_1.1.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    Switch のコントローラーは入力の並びが違うため使用は控える (キーコンフィグで合わせる)
************************************************************************************/

#pragma once
#ifndef DIRECT_INPUT_GAMEPAD_H_
#define DIRECT_INPUT_GAMEPAD_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "di.h"
#include "diGamePadButton.h"

/******************************************************************
* Macro Definitions
******************************************************************/
#define MAX_GAME_PAD (4)
#define BUTTON_X		0x00000010l	// Ｘボタン
#define BUTTON_A		0x00000020l	// Ａボタン
#define BUTTON_B		0x00000040l	// Ｂボタン
#define BUTTON_Y		0x00000080l	// Ｙボタン
#define BUTTON_L		0x00000100l	// Ｌボタン
#define BUTTON_R		0x00000200l	// Ｒボタン
#define BUTTON_L2		0x00000400l	// L2ボタン
#define BUTTON_R2		0x00000800l	// R2ボタン
#define BUTTON_SELECT	0x00001000l	// Selectボタン(-)
#define BUTTON_START	0x00002000l	// Startボタン(+)
#define BUTTON_L3		0x00004000l	// Lスティック押し込み
#define BUTTON_R3		0x00008000l	// Rスティック押し込み
#define BUTTON_UP		0x00000001l	// 十字キー上 (POV)
#define BUTTON_DOWN		0x00000002l	// 十字キー下 (POV)
#define BUTTON_LEFT		0x00000004l	// 十字キー左 (POV)
#define BUTTON_RIGHT	0x00000008l	// 十字キー右 (POV)


namespace ko
{


	/***********************************************************************************
	 * @brief   DirectInputのGamePadを持つクラス
	 ************************************************************************************/
	class DirectInputGamePad
	{

	private:          /*---コンストラクタ---*/
		DirectInputGamePad() {}
		~DirectInputGamePad() {}		// COM の解放は Release で


	public:          /*---クラスメソッド---*/


		/***********************************************************************************
		 * @brief 自身のインスタンス取得
		 * @details 自身のインスタンス取得
		 *
		 * @return DirectInputGamePad* 自身のインスタンス
		 ************************************************************************************/
		static DirectInputGamePad* getInstance(void);


		/***********************************************************************************
		 * @brief 初期化
		 * @details 繋がっているパッドを列挙する (XInput のパッドは除く)。DirectInput::Init の後に呼ぶ。
		 *          もう一度呼ぶと、今のパッドを解放して列挙し直す (抜き差しの反映。数十 ms かかるので毎フレームは呼ばない)
		 *
		 * @return HRESULT 成功の有無
		 ************************************************************************************/
		HRESULT Init(void);


		/***********************************************************************************
		 * @brief 更新
		 * @details 更新
		 *
		 * @return 無し
		 ************************************************************************************/
		void Update(void);


		/***********************************************************************************
		 * @brief キーコンフィグ時の更新処理
		 * @details キーコンフィグを行う際に Update の代わりに呼ぶ。A → B → X → Y → L → L2 → R → R2 → Start → Select の順に押す
		 *
		 * @param[int] index キーコンフィグをするパッドの番号
		 * @return 無し
		 ************************************************************************************/
		void UpdateSetKeyConfig(int index);


		/***********************************************************************************
		 * @brief キーコンフィグを始める
		 * @details 以前は Init で 1 回だけ始まっていた (_isInitGamePad の既定が true)
		 *
		 * @return 無し
		 ************************************************************************************/
		void BeginKeyConfig(void);


		/***********************************************************************************
		 * @brief 解放
		 * @details 解放
		 *
		 * @return 無し
		 ************************************************************************************/
		void Release(void);

	public:          /*---アクセッサー関数群---*/

		/***********************************************************************************
		 * @brief アナログパッド(左スティック)のX軸取得
		 *
		 * @param[int] index GamePadの番号
		 * @return FLOAT -1.0f ~ 1.0fで返却
		 ************************************************************************************/
		FLOAT GetAnalogPadX(int index);


		/***********************************************************************************
		 * @brief アナログパッド(左スティック)のY軸取得
		 *
		 * @param[int] index GamePadの番号
		 * @return FLOAT -1.0f(下) ~ 1.0f(上)で返却
		 ************************************************************************************/
		FLOAT GetAnalogPadY(int index);


		/***********************************************************************************
		 * @brief 右スティックのX軸取得 (Z 軸)
		 *
		 * @param[int] index GamePadの番号
		 * @return FLOAT -1.0f ~ 1.0fで返却 (軸が無いパッドは 0)
		 ************************************************************************************/
		FLOAT GetAnalogPadRX(int index);


		/***********************************************************************************
		 * @brief 右スティックのY軸取得 (Rz 軸)
		 *
		 * @param[int] index GamePadの番号
		 * @return FLOAT -1.0f(下) ~ 1.0f(上)で返却 (軸が無いパッドは 0)
		 ************************************************************************************/
		FLOAT GetAnalogPadRY(int index);


		/***********************************************************************************
		 * @brief トリガーの値 (Rx = L2 / Ry = R2)
		 * @details アナログの軸が無いパッドは、L2 / R2 ボタンを押していれば 1
		 *
		 * @param[int] index GamePadの番号
		 * @param[bool] right 右 (R2) なら true
		 * @return FLOAT 0.0f ~ 1.0fで返却
		 ************************************************************************************/
		FLOAT GetTrigger(int index, bool right);


		/***********************************************************************************
		 * @brief 全ボタンの押下状態 (BUTTON_* のビット)
		 *
		 * @param[int] index GamePadの番号
		 * @return DWORD ボタンのビット
		 ************************************************************************************/
		DWORD GetButtons(int index)const { return isValid(index) ? _padState[index] : 0; }


		/***********************************************************************************
		 * @brief 連続したボタンの押し状況を取得
		 * @details 連続したボタンの押し状況を取得
		 *
		 * @param[int] index GamePadの番号
		 * @param[DWORD] button ボタンの種類
		 * @return bool 押されているかどうかの有無を返却
		 ************************************************************************************/
		bool GetButton(int index, DWORD button);


		/***********************************************************************************
		 * @brief 瞬間のボタンの押し状況を取得
		 * @details 瞬間のボタンの押し状況を取得
		 *
		 * @param[int] index GamePadの番号
		 * @param[DWORD] button ボタンの種類
		 * @return bool 押されているかどうかの有無を返却
		 ************************************************************************************/
		bool GetButtonDown(int index, DWORD button);


		/***********************************************************************************
		 * @brief 読めているか (抜かれると false。挿し直したら Init で列挙し直す)
		 *
		 * @param[int] index GamePadの番号
		 * @return bool 読めていれば true
		 ************************************************************************************/
		bool IsConnected(int index)const { return isValid(index) && index < _gamePadNum && _connected[index]; }


		/***********************************************************************************
		 * @brief GamePadのデバイスの取得
		 * @details GamePadのデバイスの取得
		 *
		 * @param[int] index GamePadの番号
		 * @return IDirectInputDevice8* 列挙されたGamePadデバイスの取得
		 ************************************************************************************/
		IDirectInputDevice8* GetPadDevice(int index)const { return isValid(index) ? _gamePadDevice[index] : nullptr; }


		/***********************************************************************************
		 * @brief 現在の入力情報の取得 (キーコンフィグ中のパッド)
		 *
		 * @return const DIJOYSTATE 入力情報を格納する構造体
		 ************************************************************************************/
		const DIJOYSTATE& GetCurrentDirectInputJoyState(void)const { return _currDijs[_configPad < 0 ? 0 : _configPad]; }


		/***********************************************************************************
		 * @brief 1フレーム前の入力情報の取得 (キーコンフィグ中のパッド)
		 *
		 * @return const DIJOYSTATE 入力情報を格納する構造体
		 ************************************************************************************/
		const DIJOYSTATE& GetPreviousDirectInputJoyState(void)const { return _prevDijs; }


		/***********************************************************************************
		 * @brief 新規の入力情報のセット
		 * @details 古い状態は delete する (Check の中から呼ばれ、呼んだ側の this も消える。呼んだ後はメンバに触らないこと)
		 *
		 * @return 無し
		 ************************************************************************************/
		void SetNewGamePadButtonState(DirectInputGamePadButton* state) { delete _pDigpb; _pDigpb = state; _configStep++; }


		/***********************************************************************************
		 * @brief 列挙したGamePadのセット
		 * @details 列挙したGamePadのセット
		 *
		 * @param[IDirectInputDevice8*] device GamePadのデバイス
		 * @param[int] index どのデバイス番号に格納するか (0 ~ 3)
		 * @return 無し
		 ************************************************************************************/
		void SetGamePadDevice(IDirectInputDevice8* device, int index) { if (isValid(index)) _gamePadDevice[index] = device; }


		/***********************************************************************************
		 * @brief 現在のGamePadの接続数を取得(初期化時に使用)
		 * @details 現在のGamePadの接続数を取得(初期化時に使用)
		 *
		 * @return const int 接続台数
		 ************************************************************************************/
		int GetPadDeviceNum(void)const { return _gamePadNum; }


		/***********************************************************************************
		 * @brief GamePadの数を増やす(初期化時に使用)
		 * @details GamePadの数が増えた時に呼ぶ
		 *
		 * @return 無し
		 ************************************************************************************/
		void IncrementPadDeviceNum(void) { _gamePadNum++; }


		/***********************************************************************************
		 * @brief キーコンフィグ中かどうか
		 * @details 以前は「初期化が完了したかどうか」で、既定が true (= 起動直後からキーコンフィグ中) だった
		 *
		 * @return bool キーコンフィグ中なら true
		 ************************************************************************************/
		bool GetInitGamePadFlag(void)const { return _isInitGamePad; }


		/***********************************************************************************
		 * @brief キーコンフィグが終わったら false で呼ぶ関数 (DirectInputGamePadButtonEND から)
		 *
		 * @param[bool] flag キーコンフィグ中か
		 ************************************************************************************/
		void SetInitGamePadFlag(bool flag) { _isInitGamePad = flag; }


		/***********************************************************************************
		 * @brief キーコンフィグの何番目か (0 = A を待っている ～ 9 = Select を待っている。10 で終わり)
		 *
		 * @return int 番号
		 ************************************************************************************/
		int GetKeyConfigStep(void)const { return _configStep; }

	private:         /*---メンバ変数---*/
		IDirectInputDevice8* _gamePadDevice[MAX_GAME_PAD] = {};		// GamePadのデバイス
		DWORD				 _padState[MAX_GAME_PAD] = {};			// GamePadのステータスをバイトとして管理
		DWORD				 _padTrigger[MAX_GAME_PAD] = {};		// Trigger判定↑
		bool				 _connected[MAX_GAME_PAD] = {};			// 今のフレームで読めたか
		bool				 _hasRightStick[MAX_GAME_PAD] = {};		// Z / Rz 軸があるか
		bool				 _hasAnalogTrigger[MAX_GAME_PAD] = {};	// Rx / Ry 軸があるか
		int _gamePadNum = 0;										// GamePadの数

		// 以前は DIJOYSTATE2 を 1 つだけ持ち、GetDeviceState には sizeof(DIJOYSTATE) を渡していた (c_dfDIJoystick に合わせて DIJOYSTATE にする)
		DIJOYSTATE		_currDijs[MAX_GAME_PAD] = {};				// GamePadごとのステータス
		DIJOYSTATE		_prevDijs = {};								// キーコンフィグ中のパッドの前のステータス

		DirectInputGamePadButton* _pDigpb = nullptr;
		bool _isInitGamePad = false;
		int  _configPad = 0;
		int  _configStep = 0;


	private:          /*---プライベートクラスメソッド---*/
		static bool isValid(int index) { return index >= 0 && index < MAX_GAME_PAD; }
		HRESULT setPropaty(int index);
		bool readState(int index);
		void releaseDevices(void);

	};

}


#endif // DIRECT_INPUT_GAMEPAD_H_

/************************************
* End of file
************************************/
