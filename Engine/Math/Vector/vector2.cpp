/************************************************
* vector2.cpp
* オリジナルVector2クラス
*
* 最終更新日:2026/1/13
* 作成者：Keigo Onari
*************************************************/

// =============================================================
// インクルード
// =============================================================
#include "vector2.h"
#include "vector3.h"
#include "vector4.h"
#include "vectorEx.h"

namespace Engine
{
	// =============================================================
	// Vector2
	// =============================================================

	// コンストラクタ
	// =============================================================
	Vector2::Vector2(const Vector3& v) :x(v.x), y(v.y) {}
	Vector2::Vector2(const Vector4& v) :x(v.x), y(v.y) {}
	Vector2::Vector2(const VectorEx& v) :x(v.xFloat), y(v.yFloat) {}

	// 静的変数の初期化
	// =============================================================
	// Vector2の値全て0.0fを生成
	// =============================================================
	const Vector2 Vector2::zero(0.0f, 0.0f);
	// Vector2の値全て1.0fを生成
	// =============================================================
	const Vector2 Vector2::one(1.0f, 1.0f);
	// _y =  1.0fのVector2を生成
	// =============================================================
	const Vector2 Vector2::up(0.0f, 1.0f);
	// _y = -1.0fのVector2を生成
	// =============================================================
	const Vector2 Vector2::down(0.0f, -1.0f);
	// _x =  1.0fのVector2を生成
	// =============================================================
	const Vector2 Vector2::right(1.0f, 0.0f);
	// _x = -1.0fのVector2を生成
	// =============================================================
	const Vector2 Vector2::left(-1.0f, 0.0f);

	// +Vector2
	// =============================================================
	Vector2 Vector2::operator+ (void)const
	{
		return *this;
	}

	// -Vector2
	// =============================================================
	Vector2 Vector2::operator- (void)const
	{
		return Vector2(-x, -y);
	}

	// Vector2 + Vector2
	// =============================================================
	Vector2 Vector2::operator+ (const Vector2& vec)const
	{
		return Vector2(x + vec.x, y + vec.y);
	}

	// Vector2 - Vector2
	// =============================================================
	Vector2 Vector2::operator- (const Vector2& vec)const
	{
		return Vector2(x - vec.x, y - vec.y);
	}

	// Vector2 * Vector2
	// =============================================================
	Vector2 Vector2::operator* (const Vector2& vec)const
	{
		return Vector2(x * vec.x, y * vec.y);
	}

	// Vector2 / Vector2
	// =============================================================
	Vector2 Vector2::operator/ (const Vector2& vec)const
	{
		return Vector2(x / vec.x, y / vec.y);
	}

	// Vector2 + float
	// =============================================================
	Vector2 Vector2::operator+ (const float& t)const
	{
		return Vector2(x + t, y + t);
	}

	// Vector2 - float
	// =============================================================
	Vector2 Vector2::operator- (const float& t)const
	{
		return Vector2(x - t, y - t);
	}

	// Vector2 * float
	// =============================================================
	Vector2 Vector2::operator* (const float& t)const
	{
		return Vector2(x * t, y * t);
	}
	// Vector2 / float
	// =============================================================
	Vector2 Vector2::operator/ (const float& t)const
	{
		return Vector2(x / t, y / t);
	}

	// Vector2 + Vector3
	// =============================================================
	Vector2 Vector2::operator+ (const Vector3& vec)const
	{
		return Vector2(x + vec.x, y + vec.y);
	}

	// Vector2 - Vector3
	// =============================================================
	Vector2 Vector2::operator- (const Vector3& vec)const
	{
		return Vector2(x - vec.x, y - vec.y);
	}

	// Vector2 * Vector3
	// =============================================================
	Vector2 Vector2::operator* (const Vector3& vec)const
	{
		return Vector2(x * vec.x, y * vec.y);
	}

	// Vector2 / Vector3
	// =============================================================
	Vector2 Vector2::operator/ (const Vector3& vec)const
	{
		return Vector2(x / vec.x, y / vec.y);
	}

	// Vector2 + Vector4
	// =============================================================
	Vector2 Vector2::operator+ (const Vector4& vec)const
	{
		return Vector2(x + vec.x, y + vec.y);
	}

	// Vector2 - Vector4
	// =============================================================
	Vector2 Vector2::operator- (const Vector4& vec)const
	{
		return Vector2(x - vec.x, y - vec.y);
	}

	// Vector2 * Vector4
	// =============================================================
	Vector2 Vector2::operator* (const Vector4& vec)const
	{
		return Vector2(x * vec.x, y * vec.y);
	}

	// Vector2 / Vector4
	// =============================================================
	Vector2 Vector2::operator/ (const Vector4& vec)const
	{
		return Vector2(x / vec.x, y / vec.y);
	}

	// Vector2 + KOVECTOR
	// =============================================================
	Vector2 Vector2::operator+ (const VectorEx& vec)const
	{
		return Vector2(x + vec.xFloat, y + vec.yFloat);
	}

	// Vector2 - KOVECTOR
	// =============================================================
	Vector2 Vector2::operator- (const VectorEx& vec)const
	{
		return Vector2(x - vec.xFloat, y - vec.yFloat);
	}

	// Vector2 * KOVECTOR
	// =============================================================
	Vector2 Vector2::operator* (const VectorEx& vec)const
	{
		return Vector2(x * vec.xFloat, y * vec.yFloat);
	}

	// Vector2 / KOVECTOR
	// =============================================================
	Vector2 Vector2::operator/ (const VectorEx& vec)const
	{
		return Vector2(x / vec.xFloat, y / vec.yFloat);
	}

	// Vector2 += Vector2
	// =============================================================
	Vector2& Vector2::operator+= (const Vector2& vec)
	{
		x += vec.x;
		y += vec.y;
		return *this;
	}

	// Vector2 -= Vector2
	// =============================================================
	Vector2& Vector2::operator-= (const Vector2& vec)
	{
		x -= vec.x;
		y -= vec.y;
		return *this;
	}

	// Vector2 *= Vector2
	// =============================================================
	Vector2& Vector2::operator*= (const Vector2& vec)
	{
		x *= vec.x;
		y *= vec.y;
		return *this;
	}

	// Vector2 /= Vector2
	// =============================================================
	Vector2& Vector2::operator/= (const Vector2& vec)
	{
		x /= vec.x;
		y /= vec.y;
		return *this;
	}

	// Vector2 += float
	// =============================================================
	Vector2& Vector2::operator+= (const float& t)
	{
		x += t;
		y += t;
		return *this;
	}

	// Vector2 -= float
	// =============================================================
	Vector2& Vector2::operator-= (const float& t)
	{
		x -= t;
		y -= t;
		return *this;
	}

	// Vector2 *= float
	// =============================================================
	Vector2& Vector2::operator*= (const float& t)
	{
		x *= t;
		y *= t;
		return *this;
	}

	// Vector2 /= float
	// =============================================================
	Vector2& Vector2::operator/= (const float& t)
	{
		x /= t;
		y /= t;
		return *this;
	}

	// Vector2 += Vector3
	// =============================================================
	Vector2& Vector2::operator+= (const Vector3& vec)
	{
		x += vec.x;
		y += vec.y;
		return *this;
	}

	// Vector2 -= Vector3
	// =============================================================
	Vector2& Vector2::operator-= (const Vector3& vec)
	{
		x -= vec.x;
		y -= vec.y;
		return *this;
	}

	// Vector2 *= Vector3
	// =============================================================
	Vector2& Vector2::operator*= (const Vector3& vec)
	{
		x *= vec.x;
		y *= vec.y;
		return *this;
	}

	// Vector2 /= Vector3
	// =============================================================
	Vector2& Vector2::operator/= (const Vector3& vec)
	{
		x /= vec.x;
		y /= vec.y;
		return *this;
	}

	// Vector2 += Vector4
	// =============================================================
	Vector2& Vector2::operator+= (const Vector4& vec)
	{
		x += vec.x;
		y += vec.y;
		return *this;
	}

	// Vector2 -= Vector4
	// =============================================================
	Vector2& Vector2::operator-= (const Vector4& vec)
	{
		x -= vec.x;
		y -= vec.y;
		return *this;
	}

	// Vector2 *= Vector4
	// =============================================================
	Vector2& Vector2::operator*= (const Vector4& vec)
	{
		x *= vec.x;
		y *= vec.y;
		return *this;
	}

	// Vector2 /= Vector4
	// =============================================================
	Vector2& Vector2::operator/= (const Vector4& vec)
	{
		x /= vec.x;
		y /= vec.y;
		return *this;
	}

	// Vector2 += KOVECTOR
	// =============================================================
	Vector2& Vector2::operator+= (const VectorEx& vec)
	{
		x += vec.xFloat;
		y += vec.yFloat;
		return *this;
	}

	// Vector2 -= KOVECTOR
	// =============================================================
	Vector2& Vector2::operator-= (const VectorEx& vec)
	{
		x -= vec.xFloat;
		y -= vec.yFloat;
		return *this;
	}

	// Vector2 *= KOVECTOR
	// =============================================================
	Vector2& Vector2::operator*= (const VectorEx& vec)
	{
		x *= vec.xFloat;
		y *= vec.yFloat;
		return *this;
	}

	// Vector2 /= KOVECTOR
	// =============================================================
	Vector2& Vector2::operator/= (const VectorEx& vec)
	{
		x /= vec.xFloat;
		y /= vec.yFloat;
		return *this;
	}

	// if(Vector2 == Vector2)
	// =============================================================
	bool Vector2::operator== (const Vector2& vec)const
	{
		return (x == vec.x && y == vec.y);
	}

	// if(Vector2 != Vector2)
	// =============================================================
	bool Vector2::operator!= (const Vector2& vec)const
	{
		return (x != vec.x && y != vec.y);
	}

	// if(Vector2 == Vector3)
	// =============================================================
	bool Vector2::operator== (const Vector3& vec)const
	{
		return (x == vec.x && y == vec.y);
	}

	// if(Vector2 != Vector3)
	// =============================================================
	bool Vector2::operator!= (const Vector3& vec)const
	{
		return (x != vec.x && y != vec.y);
	}

	// if(Vector2 == Vector4)
	// =============================================================
	bool Vector2::operator== (const Vector4& vec)const
	{
		return (x == vec.x && y == vec.y);
	}
	
	// if(Vector2 != Vector4)
	// =============================================================
	bool Vector2::operator!= (const Vector4& vec)const
	{
		return (x != vec.x && y != vec.y);
	}
	
	// if(Vector2 == KOVECTOR)
	// =============================================================
	bool Vector2::operator== (const VectorEx& vec)const
	{
		return (x == vec.xFloat && y == vec.yFloat);
	}
	
	// if(Vector2 != KOVECTOR)
	// =============================================================
	bool Vector2::operator!= (const VectorEx& vec)const
	{
		return (x != vec.xFloat && y != vec.yFloat);
	}

	// Vector2 = Vector3
	// =============================================================
	Vector2& Vector2::operator= (const Vector3& vec)
	{
		x = vec.x;
		y = vec.y;
		return *this;
	}

	// Vector2 = Vector4
	// =============================================================
	Vector2& Vector2::operator= (const Vector4& vec)
	{
		x = vec.x;
		y = vec.y;
		return *this;
	}

	// Vector2 = KOVECTOR
	// =============================================================
	Vector2& Vector2::operator= (const VectorEx& vec)
	{
		x = vec.xFloat;
		y = vec.yFloat;
		return *this;
	}
}