/************************************************
* vector3.cpp
* オリジナルVector3クラス
*
* 最終更新日:2023/12/26
* 作成者：Keigo Onari
*************************************************/

// =============================================================
// インクルード
// =============================================================
#include "vector3.h"
#include "vector2.h"
#include "vector4.h"
#include "vectorEx.h"

namespace Engine
{
	// =============================================================
	// Vector3
	// =============================================================

	// コンストラクタ
	// =============================================================
	Vector3::Vector3(const Vector2& v) :x(v.x), y(v.y), z(0.0f) {}
	Vector3::Vector3(const Vector2& v, const float& t) : x(v.x), y(v.y), z(t) {}
	Vector3::Vector3(const Vector4& v) :x(v.x), y(v.y), z(v.z) {}
	Vector3::Vector3(const VectorEx& v) :x(v.xFloat), y(v.yFloat), z(v.zFloat) {}

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
		return Vector3(-x, -y, -z);
	}

	// Vector3 + Vector3
	// =============================================================
	Vector3 Vector3::operator+(const Vector3& vec)const
	{
		return Vector3(x + vec.x, y + vec.y, z + vec.z);
	}

	// Vector3 - Vector3
	// =============================================================
	Vector3 Vector3::operator- (const Vector3& vec)const
	{
		return Vector3(x - vec.x, y - vec.y, z - vec.z);
	}

	// Vector3 * Vector3
	// =============================================================
	Vector3 Vector3::operator* (const Vector3& vec)const
	{
		return Vector3(x * vec.x, y * vec.y, z * vec.z);
	}

	// Vector3 / Vector3
	// =============================================================
	Vector3 Vector3::operator/ (const Vector3& vec)const
	{
		return Vector3(x / vec.x, y / vec.y, z / vec.z);
	}

	// Vector3 + float
	// =============================================================
	Vector3 Vector3::operator+ (const float& t)const
	{
		return Vector3(x + t, y + t, z + t);
	}

	// Vector3 - float
	// =============================================================
	Vector3 Vector3::operator- (const float& t)const
	{
		return Vector3(x - t, y - t, z - t);
	}
	
	// Vector3 * float
	// =============================================================
	Vector3 Vector3::operator* (const float& t)const
	{
		return Vector3(x * t, y * t, z * t);
	}
	
	// Vector3 / float
	// =============================================================
	Vector3 Vector3::operator/ (const float& t)const
	{
		return Vector3(x / t, y / t, z / t);
	}

	// Vector3 + Vector2
	// =============================================================
	Vector3 Vector3::operator+ (const Vector2& vec)const
	{
		return Vector3(x + vec.x, y + vec.y, z);
	}

	// Vector3 - Vector2
	// =============================================================
	Vector3 Vector3::operator- (const Vector2& vec)const
	{
		return Vector3(x - vec.x, y - vec.y, z);
	}

	// Vector3 * Vector2
	// =============================================================
	Vector3 Vector3::operator* (const Vector2& vec)const
	{
		return Vector3(x * vec.x, y * vec.y, z);
	}

	// Vector3 / Vector2
	// =============================================================
	Vector3 Vector3::operator/ (const Vector2& vec)const
	{
		return Vector3(x / vec.x, y / vec.y, z);
	}

	// Vector3 + Vector4
	// =============================================================
	Vector3 Vector3::operator+ (const Vector4& vec)const
	{
		return Vector3(x + vec.x, y + vec.y, z + vec.z);
	}

	// Vector3 - Vector4
	// =============================================================
	Vector3 Vector3::operator- (const Vector4& vec)const
	{
		return Vector3(x - vec.x, y - vec.y, z - vec.z);
	}

	// Vector3 * Vector4
	// =============================================================
	Vector3 Vector3::operator* (const Vector4& vec)const
	{
		return Vector3(x * vec.x, y * vec.y, z * vec.z);
	}

	// Vector3 / Vector4
	// =============================================================
	Vector3 Vector3::operator/ (const Vector4& vec)const
	{
		return Vector3(x / vec.x, y / vec.y, z / vec.z);
	}

	// Vector3 + KOVECTOR
	// =============================================================
	Vector3 Vector3::operator+ (const VectorEx& vec)const
	{
		return Vector3(x + vec.xFloat, y + vec.yFloat, z + vec.zFloat);
	}

	// Vector3 - KOVECTOR
	// =============================================================
	Vector3 Vector3::operator- (const VectorEx& vec)const
	{
		return Vector3(x - vec.xFloat, y - vec.yFloat, z - vec.zFloat);
	}

	// Vector3 * KOVECTOR
	// =============================================================
	Vector3 Vector3::operator* (const VectorEx& vec)const
	{
		return Vector3(x * vec.xFloat, y * vec.yFloat, z * vec.zFloat);
	}

	// Vector3 / KOVECTOR
	// =============================================================
	Vector3 Vector3::operator/ (const VectorEx& vec)const
	{
		return Vector3(x / vec.xFloat, y / vec.yFloat, z / vec.zFloat);
	}

	// Vector3 += Vector3
	// =============================================================
	Vector3& Vector3::operator+= (const Vector3& vec)
	{
		x += vec.x;
		y += vec.y;
		z += vec.z;
		return *this;
	}

	// Vector3 -= Vector3
	// =============================================================
	Vector3& Vector3::operator-= (const Vector3& vec)
	{
		x -= vec.x;
		y -= vec.y;
		z -= vec.z;
		return *this;
	}

	// Vector3 *= Vector3
	// =============================================================
	Vector3& Vector3::operator*= (const Vector3& vec)
	{
		x *= vec.x;
		y *= vec.y;
		z *= vec.z;
		return *this;
	}

	// Vector3 /= Vector3
	// =============================================================
	Vector3& Vector3::operator/= (const Vector3& vec)
	{
		x /= vec.x;
		y /= vec.y;
		z /= vec.z;
		return *this;
	}

	// Vector3 += float
	// =============================================================
	Vector3& Vector3::operator+= (const float& t)
	{
		x += t;
		y += t;
		z += t;
		return *this;
	}

	// Vector3 -= float
	// =============================================================
	Vector3& Vector3::operator-= (const float& t)
	{
		x -= t;
		y -= t;
		z -= t;
		return *this;
	}

	// Vector3 *= float
	// =============================================================
	Vector3& Vector3::operator*= (const float& t)
	{
		x *= t;
		y *= t;
		z *= t;
		return *this;
	}

	// Vector3 /= float
	// =============================================================
	Vector3& Vector3::operator/= (const float& t)
	{
		x /= t;
		y /= t;
		z /= t;
		return *this;
	}

	// Vector3 += Vector2
	// =============================================================
	Vector3& Vector3::operator+= (const Vector2& vec)
	{
		x += vec.x;
		y += vec.y;
		return *this;
	}

	// Vector3 -= Vector2
	// =============================================================
	Vector3& Vector3::operator-= (const Vector2& vec)
	{
		x -= vec.x;
		y -= vec.y;
		return *this;
	}

	// Vector3 *= Vector2
	// =============================================================
	Vector3& Vector3::operator*= (const Vector2& vec)
	{
		x *= vec.x;
		y *= vec.y;
		return *this;
	}

	// Vector3 /= Vector2
	// =============================================================
	Vector3& Vector3::operator/= (const Vector2& vec)
	{
		x /= vec.x;
		y /= vec.y;
		return *this;
	}

	// Vector3 += Vector4
	// =============================================================
	Vector3& Vector3::operator+= (const Vector4& vec)
	{
		x += vec.x;
		y += vec.y;
		z += vec.z;
		return *this;
	}

	// Vector3 -= Vector4
	// =============================================================
	Vector3& Vector3::operator-= (const Vector4& vec)
	{
		x -= vec.x;
		y -= vec.y;
		z -= vec.z;
		return *this;
	}

	// Vector3 *= Vector4
	// =============================================================
	Vector3& Vector3::operator*= (const Vector4& vec)
	{
		x *= vec.x;
		y *= vec.y;
		z *= vec.z;
		return *this;
	}

	// Vector3 /= Vector4
	// =============================================================
	Vector3& Vector3::operator/= (const Vector4& vec)
	{
		x /= vec.x;
		y /= vec.y;
		z /= vec.z;
		return *this;
	}

	// Vector3 += KOVECTOR
	// =============================================================
	Vector3& Vector3::operator+= (const VectorEx& vec)
	{
		x += vec.xFloat;
		y += vec.yFloat;
		z += vec.zFloat;
		return *this;
	}

	// Vector3 -= KOVECTOR
	// =============================================================
	Vector3& Vector3::operator-= (const VectorEx& vec)
	{
		x -= vec.xFloat;
		y -= vec.yFloat;
		z -= vec.zFloat;
		return *this;
	}

	// Vector3 *= KOVECTOR
	// =============================================================
	Vector3& Vector3::operator*= (const VectorEx& vec)
	{
		x *= vec.xFloat;
		y *= vec.yFloat;
		z *= vec.zFloat;
		return *this;
	}

	// Vector3 /= KOVECTOR
	// =============================================================
	Vector3& Vector3::operator/= (const VectorEx& vec)
	{
		x /= vec.xFloat;
		y /= vec.yFloat;
		z /= vec.zFloat;
		return *this;
	}

	// if(Vector3 == Vector3)
	// =============================================================
	bool Vector3::operator== (const Vector3& vec)const
	{
		return (x == vec.x && y == vec.y && z == vec.z);
	}

	// if(Vector3 != Vector3)
	// =============================================================
	bool Vector3::operator!= (const Vector3& vec)const
	{
		return (x != vec.x && y != vec.y && z != vec.z);
	}

	// if(Vector3 == Vector2)
	// =============================================================
	bool Vector3::operator== (const Vector2& vec)const
	{
		return (x == vec.x && y == vec.y);
	}

	// if(Vector3 != Vector2)
	// =============================================================
	bool Vector3::operator!= (const Vector2& vec)const
	{
		return (x != vec.x && y != vec.y);
	}

	// if(Vector3 == Vector4)
	// =============================================================
	bool Vector3::operator== (const Vector4& vec)const
	{
		return (x == vec.x && y == vec.y && z == vec.z);
	}

	// if(Vector3 != Vector4)
	// =============================================================
	bool Vector3::operator!= (const Vector4& vec)const
	{
		return (x != vec.x && y != vec.y && z != vec.z);
	}

	// if(Vector3 == KOVECTOR)
	// =============================================================
	bool Vector3::operator== (const VectorEx& vec)const
	{
		return (x == vec.xFloat && y == vec.yFloat && z == vec.zFloat);
	}

	// if(Vector3 != KOVECTOR)
	// =============================================================
	bool Vector3::operator!= (const VectorEx& vec)const
	{
		return (x != vec.xFloat && y != vec.yFloat && z != vec.zFloat);
	}

	// Vector3 = float
	// _zは0
	// =============================================================
	Vector3& Vector3::operator= (const float& t)
	{
		x = t;
		y = t;
		z = t;
		return *this;
	}

	// Vector3 = Vector2
	// =============================================================
	Vector3& Vector3::operator= (const Vector2& vec)
	{
		x = vec.x;
		y = vec.y;
		z = 0.0f;
		return *this;
	}

	// Vector3 = Vector4
	// =============================================================
	Vector3& Vector3::operator= (const Vector4& vec)
	{
		x = vec.x;
		y = vec.y;
		z = vec.z;
		return *this;
	}

	// Vector3 = KOVECTOR
	// =============================================================
	Vector3& Vector3::operator= (const VectorEx& vec)
	{
		x = vec.xFloat;
		y = vec.yFloat;
		z = vec.zFloat;
		return *this;
	}
}