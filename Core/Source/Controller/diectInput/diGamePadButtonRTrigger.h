
/***********************************************************************************
* @file    diGamePadButtonRTrigger.h
* @brief   DirectInputのGamePadのRトリガーを検知するクラス
* @details 初期化で使用する
*
* @author  Keigo Onari
* @date    20243/3
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note
************************************************************************************/

#pragma once
#ifndef DIRECT_INPUT_GAMEPAD_BUTTON_R_TRIGGER_H_
#define DIRECT_INPUT_GAMEPAD_BUTTON_R_TRIGGER_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "diGamePadButton.h"

/******************************************************************
* Macro Definitions
******************************************************************/


/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/



namespace ko
{

    class DirecInputGamePad;


    /***********************************************************************************
     * @brief   DirectInputのRトリガーを初期化するクラス
     ************************************************************************************/
    class DirectInputGamePadButtonRTrigger : public DirectInputGamePadButton
    {

        /******************************************************************
        * Internal Enumeration Declarations
        ******************************************************************/


        /******************************************************************
        * Internal Structure Declarations
        ******************************************************************/


    public:          /*---コンストラクタ---*/
        DirectInputGamePadButtonRTrigger() {}
        ~DirectInputGamePadButtonRTrigger() {}


    public:          /*---クラスメソッド---*/


        /***********************************************************************************
         * @brief 初期化の入力を受け付ける関数
         * @details 初期化の入力を受け付ける関数
         *
         * @param[DirectInputGamePad*] pInstance 実行元のクラスインスタンスのポインタ
         * @return 無し
         ************************************************************************************/
        void Check(DirectInputGamePad* pInstance)override;

    public:          /*---アクセッサー関数群---*/


    private:         /*---メンバ変数---*/


    };

}


#endif // DIRECT_INPUT_GAMEPAD_BUTTON_R_TRIGGER_H_

/************************************
* End of file
************************************/