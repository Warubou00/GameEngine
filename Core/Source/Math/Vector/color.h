
/***********************************************************************************
 * @file    color.h
 * @brief   色情報を持つfloat型4つの構造体
 * @details 
 *
 * @author  Keigo Onari
 * @date    2026/09/23
 * @version v_1.0.0
 *
 * @copyright
 * Copyright (c) 2024 Keigo Onari. All rights reserved.
 *
 * @note 名前空間、インクルードガードを変更
 ************************************************************************************/

#pragma once
#ifndef CORE_COLOR_H_
#define CORE_COLOR_H_

#include "../../common.h"

namespace Core
{
	// =============================================================
	// Color
	// =============================================================
	struct CORE_API_CLASS Color
	{
		union {
			float _color[4];
			struct {
				float _r, _g, _b, _a;
			};

			struct {
				float _x, _y, _z, _w;
			};
		};

		// コンストラクタ
		// =============================================================
		Color() :Color(1.0f, 1.0f, 1.0f, 1.0f) {}
		Color(const Color& color) :_r(color._r), _g(color._g), _b(color._b), _a(color._a) {}
		Color(float R, float G, float B, float A) :_r(R), _g(G), _b(B), _a(A) {}
		Color(unsigned int color) {
			_r = static_cast<float>((color & 0xFF0000) >> 16) / 255.0f;
			_g = static_cast<float>((color & 0x00FF00) >> 8) / 255.0f;
			_b = static_cast<float>(color & 0x0000FF) / 255.0f;
			_a = 1.0f;
		}

		// Color + Color
		// =============================================================
		Color operator+(const Color& color)const;
		// Color - Color
		// =============================================================
		Color operator-(const Color& color)const;
		// Color * Color
		// =============================================================
		Color operator*(const Color& color)const;
		// Color / Color
		// =============================================================
		Color operator/(const Color& color)const;

		// Color += Color
		// =============================================================
		Color& operator+=(const Color& color);
		// Color -= Color
		// =============================================================
		Color& operator-=(const Color& color);
		// Color *= Color
		// =============================================================
		Color& operator*=(const Color& color);
		// Color /= Color
		// =============================================================
		Color& operator/=(const Color& color);

		// if(Color == Color)
		// =============================================================
		bool operator== (const Color& color)const;
		// if(Color != Color)
		// =============================================================
		bool operator!= (const Color& color)const;

		// Color = Color
		// =============================================================
		Color& operator= (const Color& color);
		// Color = float
		// =============================================================
		Color& operator= (const float t);

		// Color(0.0f,0.0f,0.0f,0.0f)を取得
		// =============================================================
		static const Color zero;
		// Color(1.0f,1.0f,1.0f,1.0f)を取得
		// =============================================================
		static const Color white;
		// Color(0.0f,0.0f,0.0f,1.0f)を取得
		// =============================================================
		static const Color black;
		// Color(1.0f,0.0f,0.0f,1.0f)を取得
		// =============================================================
		static const Color red;
		// Color(0.0f,1.0f,0.0f,1.0f)を取得
		// =============================================================
		static const Color green;
		// Color(0.0f,0.0f,1.0f,1.0f)を取得
		// =============================================================
		static const Color blue;
		// Color(1.0f,0.0f,1.0f,1.0f)を取得
		// =============================================================
		static const Color mazenta;
		// Color(0.0f,1.0f,1.0f,1.0f)を取得
		// =============================================================
		static const Color syan;
		// Color(1.0f,1.0f,0.0f,1.0f)を取得
		// =============================================================
		static const Color yellow;
		// Color(0.121...f,0.117...f,0.2f,1.0f)を取得
		// =============================================================
		static const Color ARCAEA_COLOR;
	};
}

#endif // CORE_COLOR_H_


/************************************
* End of file
************************************/