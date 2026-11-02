
/***********************************************************************************
* @file    diGamePadButtonConfigData.h
* @brief   DirectInputのGamePadの初期化時に使用するバッファを持つクラス
* @details 
*
* @author  Keigo Onari
* @date    2024/3/3
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    
************************************************************************************/

#pragma once
#ifndef DIRECT_INPUT_GAMEPAD_KEY_CONFIG_DATA_H_
#define DIRECT_INPUT_GAMEPAD_KEY_CONFIG_DATA_H_

/******************************************************************
* Include Headers
******************************************************************/


/******************************************************************
* Macro Definitions
******************************************************************/
// 以前は 10 (L3 / R3 が無かった)。L3 / R3 はキーコンフィグの順番には入れず、既定の割り当てだけ持つ
#define DI_BUTTON_NUM (12)

/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/



namespace ko
{

	/******************************************************************
	* Structure Declarations
	******************************************************************/
	enum DirectInputButtonType
	{
		_BUTTON_A = 0,				// A
		_BUTTON_B,					// B
		_BUTTON_X,					// X
		_BUTTON_Y,					// Y
		_BUTTON_R,					// R1
		_BUTTON_R_TRIGGER,			// R2
		_BUTTON_L,					// L1
		_BUTTON_L_TRIGGER,			// L2
		_BUTTON_START,				// Start
		_BUTTON_SELECT,				// Select
		_BUTTON_L_THUMB,			// L3 (Lスティック押し込み)
		_BUTTON_R_THUMB,			// R3 (Rスティック押し込み)

		// その他キーについては実装していない
	};

    class DirectInputGamePadButtonConfigData
    {

        /******************************************************************
        * Internal Enumeration Declarations
        ******************************************************************/


        /******************************************************************
        * Internal Structure Declarations
        ******************************************************************/


    private:          /*---コンストラクタ---*/
		// 以前は全部 0 で、キーコンフィグをしないと全てのボタンが rgbButtons[0] (1 つ目のボタン) に割り当たっていた。
		// 既定は多くの HID パッド (DualShock 4 など) の並び : □ ✕ ○ △ L1 R1 L2 R2 Share Options L3 R3
		DirectInputGamePadButtonConfigData() { ResetToDefault(); }
        ~DirectInputGamePadButtonConfigData() {}


    public:          /*---クラスメソッド---*/

		/***********************************************************************************
		 * @brief 自身のインスタンスポインタを返す
		 *
		 * @return DirectInputGamePadButtonConfigData* 自身のインスタンス
		 ************************************************************************************/
		static DirectInputGamePadButtonConfigData* getInstance();

    public:          /*---アクセッサー関数群---*/

		/***********************************************************************************
		 * @brief ボタンと入力を結びつける
		 * @details ボタンと入力を結びつける
		 *
		 * @param[DirectInputButtonType] type ボタンのタイプ
		 * @return 無し
		 ************************************************************************************/
		void Set(DirectInputButtonType type, int keyValue) { _index[type] = keyValue; }

		/***********************************************************************************
		 * @brief 既定の割り当てに戻す
		 *
		 * @return 無し
		 ************************************************************************************/
		void ResetToDefault(void)
		{
			_index[_BUTTON_X] = 0;  _index[_BUTTON_A] = 1;  _index[_BUTTON_B] = 2;  _index[_BUTTON_Y] = 3;
			_index[_BUTTON_L] = 4;  _index[_BUTTON_R] = 5;  _index[_BUTTON_L_TRIGGER] = 6; _index[_BUTTON_R_TRIGGER] = 7;
			_index[_BUTTON_SELECT] = 8; _index[_BUTTON_START] = 9; _index[_BUTTON_L_THUMB] = 10; _index[_BUTTON_R_THUMB] = 11;
		}

		/***********************************************************************************
		 * @brief 初期化の入力を受け付ける関数
		 * @details 初期化の入力を受け付ける関数
		 *
		 * @param[int] index 入力された添え字
		 * @return const int 入力情報を返す
		 ************************************************************************************/
		const int Get(int index)const { return _index[index]; }


		/***********************************************************************************
		 * @brief 初期化の入力を受け付ける関数
		 * @details 初期化の入力を受け付ける関数
		 *
		 * @param[DirectInputButtonType] type ボタンのタイプ
		 * @return const int 入力情報を返す
		 ************************************************************************************/
		const int Get(DirectInputButtonType type)const { return _index[type]; }

    private:         /*---メンバ変数---*/
		int _index[DI_BUTTON_NUM] = {};
    };

}


#endif // DIRECT_INPUT_GAMEPAD_KEY_CONFIG_DATA_H_

/************************************
* End of file
************************************/