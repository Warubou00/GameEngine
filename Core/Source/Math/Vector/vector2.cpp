/************************************************
* vector2.cpp
* オリジナルVector2クラス
*
* 最終更新日:2023/12/26
* 作成者：Keigo Onari
* KOVECTORについては全てfloat型を参照
*************************************************/

// =============================================================
// インクルード
// =============================================================
#include "vector2.h"
#include "vector3.h"
#include "vector4.h"

#if 0
#include "MyVector.h"
#endif

namespace Core
{
	// =============================================================
	// Vector2
	// =============================================================

	// コンストラクタ
	// =============================================================
	Vector2::Vector2(const Vector3& v) :_x(v._x), _y(v._y) {}
	Vector2::Vector2(const Vector4& v) :_x(v._x), _y(v._y) {}

#if 0
	Vector2::Vector2(const KOVECTOR& v) :_x(v._xFloat), _y(v._yFloat) {}
#endif

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
		return Vector2(-_x, -_y);
	}

	// Vector2 + Vector2
	// =============================================================
	Vector2 Vector2::operator+ (const Vector2& vec)const
	{
		return Vector2(_x + vec._x, _y + vec._y);
	}

	// Vector2 - Vector2
	// =============================================================
	Vector2 Vector2::operator- (const Vector2& vec)const
	{
		return Vector2(_x - vec._x, _y - vec._y);
	}

	// Vector2 * Vector2
	// =============================================================
	Vector2 Vector2::operator* (const Vector2& vec)const
	{
		return Vector2(_x * vec._x, _y * vec._y);
	}

	// Vector2 / Vector2
	// =============================================================
	Vector2 Vector2::operator/ (const Vector2& vec)const
	{
		return Vector2(_x / vec._x, _y / vec._y);
	}

	// Vector2 + float
	// =============================================================
	Vector2 Vector2::operator+ (const float& t)const
	{
		return Vector2(_x + t, _y + t);
	}

	// Vector2 - float
	// =============================================================
	Vector2 Vector2::operator- (const float& t)const
	{
		return Vector2(_x - t, _y - t);
	}

	// Vector2 * float
	// =============================================================
	Vector2 Vector2::operator* (const float& t)const
	{
		return Vector2(_x * t, _y * t);
	}
	// Vector2 / float
	// =============================================================
	Vector2 Vector2::operator/ (const float& t)const
	{
		return Vector2(_x / t, _y / t);
	}

	// Vector2 + Vector3
	// =============================================================
	Vector2 Vector2::operator+ (const Vector3& vec)const
	{
		return Vector2(_x + vec._x, _y + vec._y);
	}

	// Vector2 - Vector3
	// =============================================================
	Vector2 Vector2::operator- (const Vector3& vec)const
	{
		return Vector2(_x - vec._x, _y - vec._y);
	}

	// Vector2 * Vector3
	// =============================================================
	Vector2 Vector2::operator* (const Vector3& vec)const
	{
		return Vector2(_x * vec._x, _y * vec._y);
	}

	// Vector2 / Vector3
	// =============================================================
	Vector2 Vector2::operator/ (const Vector3& vec)const
	{
		return Vector2(_x / vec._x, _y / vec._y);
	}

	// Vector2 + Vector4
	// =============================================================
	Vector2 Vector2::operator+ (const Vector4& vec)const
	{
		return Vector2(_x + vec._x, _y + vec._y);
	}

	// Vector2 - Vector4
	// =============================================================
	Vector2 Vector2::operator- (const Vector4& vec)const
	{
		return Vector2(_x - vec._x, _y - vec._y);
	}

	// Vector2 * Vector4
	// =============================================================
	Vector2 Vector2::operator* (const Vector4& vec)const
	{
		return Vector2(_x * vec._x, _y * vec._y);
	}

	// Vector2 / Vector4
	// =============================================================
	Vector2 Vector2::operator/ (const Vector4& vec)const
	{
		return Vector2(_x / vec._x, _y / vec._y);
	}

#if 0
	// Vector2 + KOVECTOR
	// =============================================================
	Vector2 Vector2::operator+ (const KOVECTOR& vec)const
	{
		return Vector2(_x + vec._xFloat, _y + vec._yFloat);
	}

	// Vector2 - KOVECTOR
	// =============================================================
	Vector2 Vector2::operator- (const KOVECTOR& vec)const
	{
		return Vector2(_x - vec._xFloat, _y - vec._yFloat);
	}

	// Vector2 * KOVECTOR
	// =============================================================
	Vector2 Vector2::operator* (const KOVECTOR& vec)const
	{
		return Vector2(_x * vec._xFloat, _y * vec._yFloat);
	}

	// Vector2 / KOVECTOR
	// =============================================================
	Vector2 Vector2::operator/ (const KOVECTOR& vec)const
	{
		return Vector2(_x / vec._xFloat, _y / vec._yFloat);
	}
#endif

	// Vector2 += Vector2
	// =============================================================
	Vector2& Vector2::operator+= (const Vector2& vec)
	{
		_x += vec._x;
		_y += vec._y;
		return *this;
	}

	// Vector2 -= Vector2
	// =============================================================
	Vector2& Vector2::operator-= (const Vector2& vec)
	{
		_x -= vec._x;
		_y -= vec._y;
		return *this;
	}

	// Vector2 *= Vector2
	// =============================================================
	Vector2& Vector2::operator*= (const Vector2& vec)
	{
		_x *= vec._x;
		_y *= vec._y;
		return *this;
	}

	// Vector2 /= Vector2
	// =============================================================
	Vector2& Vector2::operator/= (const Vector2& vec)
	{
		_x /= vec._x;
		_y /= vec._y;
		return *this;
	}

	// Vector2 += float
	// =============================================================
	Vector2& Vector2::operator+= (const float& t)
	{
		_x += t;
		_y += t;
		return *this;
	}

	// Vector2 -= float
	// =============================================================
	Vector2& Vector2::operator-= (const float& t)
	{
		_x -= t;
		_y -= t;
		return *this;
	}

	// Vector2 *= float
	// =============================================================
	Vector2& Vector2::operator*= (const float& t)
	{
		_x *= t;
		_y *= t;
		return *this;
	}

	// Vector2 /= float
	// =============================================================
	Vector2& Vector2::operator/= (const float& t)
	{
		_x /= t;
		_y /= t;
		return *this;
	}

	// Vector2 += Vector3
	// =============================================================
	Vector2& Vector2::operator+= (const Vector3& vec)
	{
		_x += vec._x;
		_y += vec._y;
		return *this;
	}

	// Vector2 -= Vector3
	// =============================================================
	Vector2& Vector2::operator-= (const Vector3& vec)
	{
		_x -= vec._x;
		_y -= vec._y;
		return *this;
	}

	// Vector2 *= Vector3
	// =============================================================
	Vector2& Vector2::operator*= (const Vector3& vec)
	{
		_x *= vec._x;
		_y *= vec._y;
		return *this;
	}

	// Vector2 /= Vector3
	// =============================================================
	Vector2& Vector2::operator/= (const Vector3& vec)
	{
		_x /= vec._x;
		_y /= vec._y;
		return *this;
	}

	// Vector2 += Vector4
	// =============================================================
	Vector2& Vector2::operator+= (const Vector4& vec)
	{
		_x += vec._x;
		_y += vec._y;
		return *this;
	}

	// Vector2 -= Vector4
	// =============================================================
	Vector2& Vector2::operator-= (const Vector4& vec)
	{
		_x -= vec._x;
		_y -= vec._y;
		return *this;
	}

	// Vector2 *= Vector4
	// =============================================================
	Vector2& Vector2::operator*= (const Vector4& vec)
	{
		_x *= vec._x;
		_y *= vec._y;
		return *this;
	}

	// Vector2 /= Vector4
	// =============================================================
	Vector2& Vector2::operator/= (const Vector4& vec)
	{
		_x /= vec._x;
		_y /= vec._y;
		return *this;
	}

#if 0
	// Vector2 += KOVECTOR
	// =============================================================
	Vector2& Vector2::operator+= (const KOVECTOR& vec)
	{
		_x += vec._xFloat;
		_y += vec._yFloat;
		return *this;
	}

	// Vector2 -= KOVECTOR
	// =============================================================
	Vector2& Vector2::operator-= (const KOVECTOR& vec)
	{
		_x -= vec._xFloat;
		_y -= vec._yFloat;
		return *this;
	}

	// Vector2 *= KOVECTOR
	// =============================================================
	Vector2& Vector2::operator*= (const KOVECTOR& vec)
	{
		_x *= vec._xFloat;
		_y *= vec._yFloat;
		return *this;
	}

	// Vector2 /= KOVECTOR
	// =============================================================
	Vector2& Vector2::operator/= (const KOVECTOR& vec)
	{
		_x /= vec._xFloat;
		_y /= vec._yFloat;
		return *this;
	}
#endif

	// if(Vector2 == Vector2)
	// =============================================================
	bool Vector2::operator== (const Vector2& vec)const
	{
		return (_x == vec._x && _y == vec._y);
	}

	// if(Vector2 != Vector2)
	// =============================================================
	bool Vector2::operator!= (const Vector2& vec)const
	{
		return (_x != vec._x && _y != vec._y);
	}

	// if(Vector2 == Vector3)
	// =============================================================
	bool Vector2::operator== (const Vector3& vec)const
	{
		return (_x == vec._x && _y == vec._y);
	}

	// if(Vector2 != Vector3)
	// =============================================================
	bool Vector2::operator!= (const Vector3& vec)const
	{
		return (_x != vec._x && _y != vec._y);
	}

	// if(Vector2 == Vector4)
	// =============================================================
	bool Vector2::operator== (const Vector4& vec)const
	{
		return (_x == vec._x && _y == vec._y);
	}
	
	// if(Vector2 != Vector4)
	// =============================================================
	bool Vector2::operator!= (const Vector4& vec)const
	{
		return (_x != vec._x && _y != vec._y);
	}
	
#if 0
	// if(Vector2 == KOVECTOR)
	// =============================================================
	bool Vector2::operator== (const KOVECTOR& vec)const
	{
		return (_x == vec._xFloat && _y == vec._yFloat);
	}
	
	// if(Vector2 != KOVECTOR)
	// =============================================================
	bool Vector2::operator!= (const KOVECTOR& vec)const
	{
		return (_x != vec._xFloat && _y != vec._yFloat);
	}
#endif

	// Vector2 = Vector3
	// =============================================================
	Vector2& Vector2::operator= (const Vector3& vec)
	{
		_x = vec._x;
		_y = vec._y;
		return *this;
	}

	// Vector2 = Vector4
	// =============================================================
	Vector2& Vector2::operator= (const Vector4& vec)
	{
		_x = vec._x;
		_y = vec._y;
		return *this;
	}

#if 0
	// Vector2 = KOVECTOR
	// =============================================================
	Vector2& Vector2::operator= (const KOVECTOR& vec)
	{
		_x = vec._xFloat;
		_y = vec._yFloat;
		return *this;
	}
#endif
}