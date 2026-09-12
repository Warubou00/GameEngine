
/***********************************************************************************
* @file    random.h
* @brief   乱数生成クラス
* @details 
*
* @author  Keigo Onari
* @date    2026/02/03
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    その他
************************************************************************************/

#pragma once
#ifndef RANDOM_H_
#define RANDOM_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>

/******************************************************************
* Macro Definitions
******************************************************************/


/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/



namespace Core
{
    class Random
    {

        /******************************************************************
        * Internal Enumeration Declarations
        ******************************************************************/


        /******************************************************************
        * Internal Structure Declarations
        ******************************************************************/


    private:          /*---コンストラクタ・デストラクタ---*/
        Random();
        ~Random();


    public:          /*---クラスメソッド---*/

        /****************************************************************
        * @brief シードの設定
        *
        * @param[in] uint32_t seed シード値
        * @return 無し
        ****************************************************************/
        static void SetSeed(uint32_t seed)
        {
            _seed = seed;
        }


        /****************************************************************
        * @brief LCGによるランダム関数の実装
        *
        * @return uint32_t 0~符号なし32ビットの最大値 - 1の間で値を返却
        *
        * @reference https://ja.wikipedia.org/wiki/%E7%B7%9A%E5%BD%A2%E5%90%88%E5%90%8C%E6%B3%95
        ****************************************************************/
        static uint32_t LCG(void)
        {
            _seed = (a * _seed + c) % m;
            return _seed;
        }


        /****************************************************************
        * @brief Xorshiftによる32bitシフトのランダム関数の実装
        *
        * @return uint32_t 0~符号なし32ビットの最大値の間で値を返却
        *
        * @reference https://ja.wikipedia.org/wiki/Xorshift
        ****************************************************************/
        static uint32_t Xorshift(void)
        {
            // 0の場合、0が返ってくるので比較的安全と言われている数値をシード値に
            unsigned int state = _seed != 0 ? _seed : xorDefaultSeed;

            state ^= state << 13;
            state ^= state << 17;
            state ^= state << 5;

            return state;
        }


        /****************************************************************
        * @brief MiddleSquareによる32bitシフトのランダム関数の実装
        *
        * @return uint32_t 0~9999の数値で返却
        *
        * @reference https://en.wikipedia.org/wiki/Middle-square_method
        * @memo 現在はあまり良くない乱数生成となっている
        ****************************************************************/
        static uint32_t MiddleSquare(void)
        {
            // オーバーフロー対策で 64bit
            uint64_t squared = (uint64_t)_seed * (uint64_t)_seed;
            uint64_t m2 = (uint64_t)m * 2u;

            if (squared > m2)
            {
                squared %= m;
            }

            // 8桁ゼロ埋め文字列に変換
            char buf[9]; // "XXXXXXXX" + '\0'
            snprintf(buf, sizeof(buf), "%08llu", (unsigned long long)squared);

            // 中央4桁を取得（2〜5文字目）
            char mid[5];
            for (int i = 0; i < 4; ++i)
            {
                mid[i] = buf[i + 2];
            }
            mid[4] = '\0';

            _seed = (uint32_t)atoi(mid);
            return _seed;
        }


    public:          /*---アクセッサー関数群---*/


    private:         /*---メンバ変数---*/

        static uint32_t _seed;

        static constexpr uint32_t a = 1664525u;
        static constexpr uint32_t c = 1013904223u;
        static constexpr uint32_t m = 4294967295u;
        static constexpr uint32_t xorDefaultSeed = 2463534242u;
    };

}


#endif // RANDOM_H_

/************************************
* End of file
************************************/
