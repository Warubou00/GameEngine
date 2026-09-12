/************************************************
* color.cpp
* 色rgbaを提供(Vector4と同義)
*
* 最終更新日:2026/1/13
* 作成者：Keigo Onari
*************************************************/

// =============================================================
// インクルード
// =============================================================
#include "color.h"

namespace Engine
{
	// =============================================================
	// Color
	// =============================================================

	// 静的変数の初期化
	// ============================================================= 
	// Color(0.0f,0.0f,0.0f,0.0f)を取得
	// =============================================================
	const Color Color::zero = Color(0.0f, 0.0f, 0.0f, 0.0f);
	// =============================================================
	// Color(1.0f,1.0f,1.0f,1.0f)を取得
	// =============================================================
	const Color Color::white = Color(1.0f, 1.0f, 1.0f, 1.0f);
	// Color(0.0f,0.0f,0.0f,1.0f)を取得
	// =============================================================
	const Color Color::black = Color(0.0f, 0.0f, 0.0f, 1.0f);
	// Color(1.0f,0.0f,0.0f,1.0f)を取得
	// =============================================================
	const Color Color::red = Color(1.0f, 0.0f, 0.0f, 1.0f);
	// Color(0.0f,1.0f,0.0f,1.0f)を取得
	// =============================================================
	const Color Color::green = Color(0.0f, 1.0f, 0.0f, 1.0f);
	// Color(0.0f,0.0f,1.0f,1.0f)を取得
	// =============================================================
	const Color Color::blue = Color(0.0f, 0.0f, 1.0f, 1.0f);
	// Color(1.0f,0.0f,1.0f,1.0f)を取得
	// =============================================================
	const Color Color::mazenta = Color(1.0f, 0.0f, 1.0f, 1.0f);
	// Color(0.0f,1.0f,1.0f,1.0f)を取得
	// =============================================================
	const Color Color::syan = Color(0.0f, 1.0f, 1.0f, 1.0f);
	// Color(1.0f,1.0f,0.0f,1.0f)を取得
	// =============================================================
	const Color Color::yellow = Color(1.0f, 1.0f, 0.0f, 1.0f);
	// Color(0.121...f,0.117...f,0.2f,1.0f)を取得
	// =============================================================
	const Color Color::ARCAEA_COLOR = Color(0x1F / 0xFF, 0x1E / 0xFF, 0x33 / 0xFF, 1.0f);

	// Color + Color
	// =============================================================
	Color Color::operator+(const Color& color)const
	{
		return Color(x + color.x, y + color.y, z + color.z, w + color.w);
	}

	// Color - Color
	// =============================================================
	Color Color::operator-(const Color& color)const
	{
		return Color(x - color.x, y - color.y, z - color.z, w - color.w);
	}

	// Color * Color
	// =============================================================
	Color Color::operator*(const Color& color)const
	{
		return Color(x * color.x, y * color.y, z * color.z, w * color.w);
	}

	// Color / Color
	// =============================================================
	Color Color::operator/(const Color& color)const
	{
		return Color(x / color.x, y / color.y, z / color.z, w / color.w);
	}

	// Color += Color
	// =============================================================
	Color& Color::operator+=(const Color& color)
	{
		x += color.x;
		y += color.y;
		z += color.z;
		w += color.w;
		return *this;
	}

	// Color -= Color
	// =============================================================
	Color& Color::operator-=(const Color& color)
	{
		x -= color.x;
		y -= color.y;
		z -= color.z;
		w -= color.w;
		return *this;
	}

	// Color *= Color
	// =============================================================
	Color& Color::operator*=(const Color& color)
	{
		x *= color.x;
		y *= color.y;
		z *= color.z;
		w *= color.w;
		return *this;
	}

	// Color /= Color
	// =============================================================
	Color& Color::operator/=(const Color& color)
	{
		x /= color.x;
		y /= color.y;
		z /= color.z;
		w /= color.w;
		return *this;
	}

	// if(Color == Color)
	// =============================================================
	bool Color::operator== (const Color& color)const
	{
		return (x == color.x && y == color.y && z == color.z && w == color.w);
	}

	// if(Color != Color)
	// =============================================================
	bool Color::operator!= (const Color& color)const
	{
		return (x != color.x && y != color.y && z != color.z && w != color.w);
	}

	// Color = Color
	// =============================================================
	Color& Color::operator= (const Color& color)
	{
		r = color.r;
		g = color.g;
		b = color.b;
		a = color.a;
		return *this;
	}

	// Color = float
	// =============================================================
	Color& Color::operator= (const float t)
	{
		a = t;
		g = t;
		b = t;
		return *this;
	}
}