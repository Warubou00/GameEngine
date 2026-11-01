/************************************************
* vector3.cpp
* オリジナルVector3クラス
*
* 最終更新日:2023/12/26
* 作成者：Keigo Onari
* KOVECTORについては全てfloat型を参照
*************************************************/

// =============================================================
// インクルード
// =============================================================
#include "vector3.h"
#include "vector2.h"
#include "vector4.h"

#if 0
#include "MyVector.h"
#endif

namespace Core
{
	// =============================================================
	// Vector3
	// =============================================================

	// コンストラクタ
	// =============================================================
	Vector3::Vector3(const Vector2& v) :_x(v._x), _y(v._y), _z(0.0f) {}
	Vector3::Vector3(const Vector2& v, const float& t) : _x(v._x), _y(v._y), _z(t) {}
	Vector3::Vector3(const Vector4& v) :_x(v._x), _y(v._y), _z(v._z) {}

#if 0
	Vector3::Vector3(const KOVECTOR& v) :_x(v._xFloat), _y(v._yFloat), _z(v._zFloat) {}
#endif

	// 静的変数の初期化
	// =============================================================
	// Vector3の値全て0.0fを生成
	// =============================================================
	const Vector3 Vector3::zero(0.0f, 0.0f, 0.0f);
	// Vector3の値全て1.0fを生成
	// =============================================================
	const Vector3 Vector3::one(1.0f, 1.0f, 1.0f);
	// _y =  1.0fのVector3を生成
	// =============================================================
	const Vector3 Vector3::up(0.0f, 1.0f, 0.0f);
	// _y = -1.0fのVector3を生成
	// =============================================================
	const Vector3 Vector3::down(0.0f, -1.0f, 0.0f);
	// _x =  1.0fのVector3を生成
	// =============================================================
	const Vector3 Vector3::right(1.0f, 0.0f, 0.0f);
	// _x = -1.0fのVector3を生成
	// =============================================================
	const Vector3 Vector3::left(-1.0f, 0.0f, 0.0f);
	// _z =  1.0fのVector3を生成(左手座標系)
	// =============================================================
	const Vector3 Vector3::frontLH(0.0f, 0.0f, 1.0f);
	// _z = -1.0fのVector3を生成(左手座標系)
	// =============================================================
	const Vector3 Vector3::backLH(0.0f, 0.0f, -1.0f);
	// _z = -1.0fのVector3を生成(右手座標系)
	// =============================================================
	const Vector3 Vector3::frontRH(0.0f, 0.0f,-1.0f);
	// _z =  1.0fのVector3を生成(右手座標系)
	// =============================================================
	const Vector3 Vector3::backRH(0.0f, 0.0f,  1.0f);

	// +Vector3
	// =============================================================
	Vector3 Vector3::operator+ (void)const
	{
		return *this;
	}

	// -Vector3
	// =============================================================
	Vector3 Vector3::operator- (void)const
	{
		return Vector3(-_x, -_y, -_z);
	}

	// Vector3 + Vector3
	// =============================================================
	Vector3 Vector3::operator+(const Vector3& vec)const
	{
		return Vector3(_x + vec._x, _y + vec._y, _z + vec._z);
	}

	// Vector3 - Vector3
	// =============================================================
	Vector3 Vector3::operator- (const Vector3& vec)const
	{
		return Vector3(_x - vec._x, _y - vec._y, _z - vec._z);
	}

	// Vector3 * Vector3
	// =============================================================
	Vector3 Vector3::operator* (const Vector3& vec)const
	{
		return Vector3(_x * vec._x, _y * vec._y, _z * vec._z);
	}

	// Vector3 / Vector3
	// =============================================================
	Vector3 Vector3::operator/ (const Vector3& vec)const
	{
		return Vector3(_x / vec._x, _y / vec._y, _z / vec._z);
	}

	// Vector3 + float
	// =============================================================
	Vector3 Vector3::operator+ (const float& t)const
	{
		return Vector3(_x + t, _y + t, _z + t);
	}

	// Vector3 - float
	// =============================================================
	Vector3 Vector3::operator- (const float& t)const
	{
		return Vector3(_x - t, _y - t, _z - t);
	}
	
	// Vector3 * float
	// =============================================================
	Vector3 Vector3::operator* (const float& t)const
	{
		return Vector3(_x * t, _y * t, _z * t);
	}
	
	// Vector3 / float
	// =============================================================
	Vector3 Vector3::operator/ (const float& t)const
	{
		return Vector3(_x / t, _y / t, _z / t);
	}

	// Vector3 + Vector2
	// =============================================================
	Vector3 Vector3::operator+ (const Vector2& vec)const
	{
		return Vector3(_x + vec._x, _y + vec._y, _z);
	}

	// Vector3 - Vector2
	// =============================================================
	Vector3 Vector3::operator- (const Vector2& vec)const
	{
		return Vector3(_x - vec._x, _y - vec._y, _z);
	}

	// Vector3 * Vector2
	// =============================================================
	Vector3 Vector3::operator* (const Vector2& vec)const
	{
		return Vector3(_x * vec._x, _y * vec._y, _z);
	}

	// Vector3 / Vector2
	// =============================================================
	Vector3 Vector3::operator/ (const Vector2& vec)const
	{
		return Vector3(_x / vec._x, _y / vec._y, _z);
	}

	// Vector3 + Vector4
	// =============================================================
	Vector3 Vector3::operator+ (const Vector4& vec)const
	{
		return Vector3(_x + vec._x, _y + vec._y, _z + vec._z);
	}

	// Vector3 - Vector4
	// =============================================================
	Vector3 Vector3::operator- (const Vector4& vec)const
	{
		return Vector3(_x - vec._x, _y - vec._y, _z - vec._z);
	}

	// Vector3 * Vector4
	// =============================================================
	Vector3 Vector3::operator* (const Vector4& vec)const
	{
		return Vector3(_x * vec._x, _y * vec._y, _z * vec._z);
	}

	// Vector3 / Vector4
	// =============================================================
	Vector3 Vector3::operator/ (const Vector4& vec)const
	{
		return Vector3(_x / vec._x, _y / vec._y, _z / vec._z);
	}

#if 0
	// Vector3 + KOVECTOR
	// =============================================================
	Vector3 Vector3::operator+ (const KOVECTOR& vec)const
	{
		return Vector3(_x + vec._xFloat, _y + vec._yFloat, _z + vec._zFloat);
	}

	// Vector3 - KOVECTOR
	// =============================================================
	Vector3 Vector3::operator- (const KOVECTOR& vec)const
	{
		return Vector3(_x - vec._xFloat, _y - vec._yFloat, _z - vec._zFloat);
	}

	// Vector3 * KOVECTOR
	// =============================================================
	Vector3 Vector3::operator* (const KOVECTOR& vec)const
	{
		return Vector3(_x * vec._xFloat, _y * vec._yFloat, _z * vec._zFloat);
	}

	// Vector3 / KOVECTOR
	// =============================================================
	Vector3 Vector3::operator/ (const KOVECTOR& vec)const
	{
		return Vector3(_x / vec._xFloat, _y / vec._yFloat, _z / vec._zFloat);
	}
#endif

	// Vector3 += Vector3
	// =============================================================
	Vector3& Vector3::operator+= (const Vector3& vec)
	{
		_x += vec._x;
		_y += vec._y;
		_z += vec._z;
		return *this;
	}

	// Vector3 -= Vector3
	// =============================================================
	Vector3& Vector3::operator-= (const Vector3& vec)
	{
		_x -= vec._x;
		_y -= vec._y;
		_z -= vec._z;
		return *this;
	}

	// Vector3 *= Vector3
	// =============================================================
	Vector3& Vector3::operator*= (const Vector3& vec)
	{
		_x *= vec._x;
		_y *= vec._y;
		_z *= vec._z;
		return *this;
	}

	// Vector3 /= Vector3
	// =============================================================
	Vector3& Vector3::operator/= (const Vector3& vec)
	{
		_x /= vec._x;
		_y /= vec._y;
		_z /= vec._z;
		return *this;
	}

	// Vector3 += float
	// =============================================================
	Vector3& Vector3::operator+= (const float& t)
	{
		_x += t;
		_y += t;
		_z += t;
		return *this;
	}

	// Vector3 -= float
	// =============================================================
	Vector3& Vector3::operator-= (const float& t)
	{
		_x -= t;
		_y -= t;
		_z -= t;
		return *this;
	}

	// Vector3 *= float
	// =============================================================
	Vector3& Vector3::operator*= (const float& t)
	{
		_x *= t;
		_y *= t;
		_z *= t;
		return *this;
	}

	// Vector3 /= float
	// =============================================================
	Vector3& Vector3::operator/= (const float& t)
	{
		_x /= t;
		_y /= t;
		_z /= t;
		return *this;
	}

	// Vector3 += Vector2
	// =============================================================
	Vector3& Vector3::operator+= (const Vector2& vec)
	{
		_x += vec._x;
		_y += vec._y;
		return *this;
	}

	// Vector3 -= Vector2
	// =============================================================
	Vector3& Vector3::operator-= (const Vector2& vec)
	{
		_x -= vec._x;
		_y -= vec._y;
		return *this;
	}

	// Vector3 *= Vector2
	// =============================================================
	Vector3& Vector3::operator*= (const Vector2& vec)
	{
		_x *= vec._x;
		_y *= vec._y;
		return *this;
	}

	// Vector3 /= Vector2
	// =============================================================
	Vector3& Vector3::operator/= (const Vector2& vec)
	{
		_x /= vec._x;
		_y /= vec._y;
		return *this;
	}

	// Vector3 += Vector4
	// =============================================================
	Vector3& Vector3::operator+= (const Vector4& vec)
	{
		_x += vec._x;
		_y += vec._y;
		_z += vec._z;
		return *this;
	}

	// Vector3 -= Vector4
	// =============================================================
	Vector3& Vector3::operator-= (const Vector4& vec)
	{
		_x -= vec._x;
		_y -= vec._y;
		_z -= vec._z;
		return *this;
	}

	// Vector3 *= Vector4
	// =============================================================
	Vector3& Vector3::operator*= (const Vector4& vec)
	{
		_x *= vec._x;
		_y *= vec._y;
		_z *= vec._z;
		return *this;
	}

	// Vector3 /= Vector4
	// =============================================================
	Vector3& Vector3::operator/= (const Vector4& vec)
	{
		_x /= vec._x;
		_y /= vec._y;
		_z /= vec._z;
		return *this;
	}

#if 0
	// Vector3 += KOVECTOR
	// =============================================================
	Vector3& Vector3::operator+= (const KOVECTOR& vec)
	{
		_x += vec._xFloat;
		_y += vec._yFloat;
		_z += vec._zFloat;
		return *this;
	}

	// Vector3 -= KOVECTOR
	// =============================================================
	Vector3& Vector3::operator-= (const KOVECTOR& vec)
	{
		_x -= vec._xFloat;
		_y -= vec._yFloat;
		_z -= vec._zFloat;
		return *this;
	}

	// Vector3 *= KOVECTOR
	// =============================================================
	Vector3& Vector3::operator*= (const KOVECTOR& vec)
	{
		_x *= vec._xFloat;
		_y *= vec._yFloat;
		_z *= vec._zFloat;
		return *this;
	}

	// Vector3 /= KOVECTOR
	// =============================================================
	Vector3& Vector3::operator/= (const KOVECTOR& vec)
	{
		_x /= vec._xFloat;
		_y /= vec._yFloat;
		_z /= vec._zFloat;
		return *this;
	}
#endif

	// if(Vector3 == Vector3)
	// =============================================================
	bool Vector3::operator== (const Vector3& vec)const
	{
		return (_x == vec._x && _y == vec._y && _z == vec._z);
	}

	// if(Vector3 != Vector3)
	// =============================================================
	bool Vector3::operator!= (const Vector3& vec)const
	{
		return (_x != vec._x && _y != vec._y && _z != vec._z);
	}

	// if(Vector3 == Vector2)
	// =============================================================
	bool Vector3::operator== (const Vector2& vec)const
	{
		return (_x == vec._x && _y == vec._y);
	}

	// if(Vector3 != Vector2)
	// =============================================================
	bool Vector3::operator!= (const Vector2& vec)const
	{
		return (_x != vec._x && _y != vec._y);
	}

	// if(Vector3 == Vector4)
	// =============================================================
	bool Vector3::operator== (const Vector4& vec)const
	{
		return (_x == vec._x && _y == vec._y && _z == vec._z);
	}

	// if(Vector3 != Vector4)
	// =============================================================
	bool Vector3::operator!= (const Vector4& vec)const
	{
		return (_x != vec._x && _y != vec._y && _z != vec._z);
	}

#if 0
	// if(Vector3 == KOVECTOR)
	// =============================================================
	bool Vector3::operator== (const KOVECTOR& vec)const
	{
		return (_x == vec._xFloat && _y == vec._yFloat && _z == vec._zFloat);
	}

	// if(Vector3 != KOVECTOR)
	// =============================================================
	bool Vector3::operator!= (const KOVECTOR& vec)const
	{
		return (_x != vec._xFloat && _y != vec._yFloat && _z != vec._zFloat);
	}
#endif

	// Vector3 = float
	// _zは0
	// =============================================================
	Vector3& Vector3::operator= (const float& t)
	{
		_x = t;
		_y = t;
		_z = t;
		return *this;
	}

	// Vector3 = Vector2
	// =============================================================
	Vector3& Vector3::operator= (const Vector2& vec)
	{
		_x = vec._x;
		_y = vec._y;
		_z = 0.0f;
		return *this;
	}

	// Vector3 = Vector4
	// =============================================================
	Vector3& Vector3::operator= (const Vector4& vec)
	{
		_x = vec._x;
		_y = vec._y;
		_z = vec._z;
		return *this;
	}

#if 0
	// Vector3 = KOVECTOR
	// =============================================================
	Vector3& Vector3::operator= (const KOVECTOR& vec)
	{
		_x = vec._xFloat;
		_y = vec._yFloat;
		_z = vec._zFloat;
		return *this;
	}
#endif
}