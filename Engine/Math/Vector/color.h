
/***********************************************************************************
 * @file    color.h
 * @brief   色情報を持つfloat型4つの構造体
 * @details 
 *
 * @author  Keigo Onari
 * @date    2026/1/13
 * @version v_2.0.0
 *
 * @copyright
 * Copyright (c) 2024 Keigo Onari. All rights reserved.
 *
 * @note    
 ************************************************************************************/

#pragma once
#ifndef COLOR_H_
#define COLOR_H_

namespace Engine
{
	// =============================================================
	// Color
	// =============================================================
	struct Color
	{
		union {
			float color[4];
			struct {
				float r, g, b, a;
			};

			struct {
				float x, y, z, w;
			};
		};

		// コンストラクタ
		// =============================================================
		Color() :Color(1.0f, 1.0f, 1.0f, 1.0f) {}
		Color(const Color& color) :r(color.r), g(color.g), b(color.b), a(color.a) {}
		Color(float R, float G, float B, float A) :r(R), g(G), b(B), a(A) {}
		Color(unsigned int color) {
			r = static_cast<float>((color & 0xFF0000) >> 16) / 255.0f;
			g = static_cast<float>((color & 0x00FF00) >> 8) / 255.0f;
			b = static_cast<float>(color & 0x0000FF) / 255.0f;
			a = 1.0f;
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

#endif // COLOR_H_

/************************************
* End of file
************************************/