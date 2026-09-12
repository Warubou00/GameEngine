/************************************************
* vector4.cpp
* オリジナルVector4クラス
*
* 最終更新日:2026/1/13
* 作成者：Keigo Onari
*************************************************/

// ==========================================================
// インクルード
// ==========================================================
#include "vector4.h"
#include "vector2.h"
#include "vector3.h"
#include "vectorEx.h"

namespace Engine
{
	// ==========================================================
	// Vector4
	// ==========================================================

	// ==========================================================
	// コンストラクタ
	// ==========================================================
	Vector4::Vector4(const Vector2& v) :x(v.x), y(v.y), z(0.0f), w(0.0f) {}
	Vector4::Vector4(const Vector2& v1, const Vector2& v2) : x(v1.x), y(v1.y), z(v2.x), w(v2.y) {}
	Vector4::Vector4(const Vector2& v, const float& t1, const float& t2) : x(v.x), y(v.y), z(t1), w(t2) {}
	Vector4::Vector4(const Vector3& v) :x(v.x), y(v.y), z(v.z), w(0.0f) {}
	Vector4::Vector4(const Vector3& v, const float& t) : x(v.x), y(v.y), z(v.z), w(t) {}
	Vector4::Vector4(const VectorEx& v) :x(v.xFloat), y(v.yFloat), z(v.zFloat), w(v.wFloat) {}

	// 静的変数の初期化
	// ==========================================================
	// Vector4の値全て1.0fを生成
	// ==========================================================
	const Vector4 Vector4::zero(0.0f, 0.0f, 0.0f, 0.0f);
	// Vector4の値全て1.0fを生成
	// ==========================================================
	const Vector4 Vector4::one(1.0f, 1.0f, 1.0f, 1.0f);
	// _y =  1.0fのVector4を生成
	// ==========================================================
	const Vector4 Vector4::up(0.0f, 1.0f, 0.0f, 0.0f);
	// _y = -1.0fのVector4を生成
	// ==========================================================
	const Vector4 Vector4::down(0.0f, -1.0f, 0.0f, 0.0f);
	// _x =  1.0fのVector4を生成
	// ==========================================================
	const Vector4 Vector4::right(1.0f, 0.0f, 0.0f, 0.0f);
	// _x = -1.0fのVector4を生成
	// ==========================================================
	const Vector4 Vector4::left(-1.0f, 0.0f, 0.0f, 0.0f);
	// _z =  1.0fのVector4を生成(左手座標系)
	// ==========================================================
	const Vector4 Vector4::frontLH(0.0f, 0.0f,-1.0f, 0.0f);
	// _z = -1.0fのVector4を生成(左手座標系)
	// ==========================================================
	const Vector4 Vector4::backLH(0.0f, 0.0f,  1.0f, 0.0f);
	// _z = -1.0fのVector4を生成(右手座標系)
	// ==========================================================
	const Vector4 Vector4::frontRH(0.0f, 0.0f, 1.0f, 0.0f);
	// _z =  1.0fのVector4を生成(右手座標系)
	// ==========================================================
	const Vector4 Vector4::backRH(0.0f, 0.0f, -1.0f, 0.0f);
	// _w =  1.0fのVector4を生成
	// ==========================================================
	const Vector4 Vector4::plusW(0.0f, 0.0f, 0.0f, 1.0f);
	// _w = -1.0fのVector4を生成
	// ==========================================================
	const Vector4 Vector4::minusW(0.0f, 0.0f, 0.0f, -1.0f);

	// +Vector4
	// ==========================================================
	Vector4 Vector4::operator+ (void)const
	{
		return *this;
	}

	// -Vector4
	// ==========================================================
	Vector4 Vector4::operator- (void)const
	{
		return Vector4(-x, -y, -z, -w);
	}

	// Vector4 + Vector4
	// ==========================================================
	Vector4 Vector4::operator+(const Vector4& vec)const
	{
		return Vector4(x + vec.x, y + vec.y, z + vec.z, w + vec.w);
	}

	// Vector4 - Vector4
	// ==========================================================
	Vector4 Vector4::operator- (const Vector4& vec)const
	{
		return Vector4(x - vec.x, y - vec.y, z - vec.z, w - vec.w);
	}

	// Vector4 * Vector4
	// ==========================================================
	Vector4 Vector4::operator* (const Vector4& vec)const
	{
		return Vector4(x * vec.x, y * vec.y, z * vec.z, w * vec.w);
	}

	// Vector4 / Vector4
	// ==========================================================
	Vector4 Vector4::operator/ (const Vector4& vec)const
	{
		return Vector4(x / vec.x, y / vec.y, z / vec.z, w / vec.w);
	}

	// Vector4 + float
	// ==========================================================
	Vector4 Vector4::operator+ (const float& t)const
	{
		return Vector4(x + t, y + t, z + t, w + t);
	}

	// Vector4 - float
	// ==========================================================
	Vector4 Vector4::operator- (const float& t)const
	{
		return Vector4(x - t, y - t, z - t, w - t);
	}

	// Vector4 * float
	// ==========================================================
	Vector4 Vector4::operator* (const float& t)const
	{
		return Vector4(x * t, y * t, z * t, w * t);
	}

	// Vector4 / float
	// ==========================================================
	Vector4 Vector4::operator/ (const float& t)const
	{
		return Vector4(x / t, y / t, z / t, w / t);
	}

	// Vector4 + Vector2
	// ==========================================================
	Vector4 Vector4::operator+ (const Vector2& vec)const
	{
		return Vector4(x + vec.x, y + vec.y, z, w);
	}

	// Vector4 - Vector2
	// ==========================================================
	Vector4 Vector4::operator- (const Vector2& vec)const
	{
		return Vector4(x - vec.x, y - vec.y, z, w);
	}

	// Vector4 * Vector2
	// ==========================================================
	Vector4 Vector4::operator* (const Vector2& vec)const
	{
		return Vector4(x * vec.x, y * vec.y, z, w);
	}

	// Vector4 / Vector2
	// ==========================================================
	Vector4 Vector4::operator/ (const Vector2& vec)const
	{
		return Vector4(x / vec.x, y / vec.y, z, w);
	}

	// Vector4 + Vector3
	// ==========================================================
	Vector4 Vector4::operator+ (const Vector3& vec)const
	{
		return Vector4(x + vec.x, y + vec.y, z + vec.z, w);
	}

	// Vector4 - Vector3
	// ==========================================================
	Vector4 Vector4::operator- (const Vector3& vec)const
	{
		return Vector4(x - vec.x, y - vec.y, z - vec.z, w);
	}

	// Vector4 * Vector3
	// ==========================================================
	Vector4 Vector4::operator* (const Vector3& vec)const
	{
		return Vector4(x * vec.x, y * vec.y, z * vec.z, w);
	}

	// Vector4 / Vector3
	// ==========================================================
	Vector4 Vector4::operator/ (const Vector3& vec)const
	{
		return Vector4(x / vec.x, y / vec.y, z / vec.z, w);
	}

	// Vector4 + KOVECTOR
	// ==========================================================
	Vector4 Vector4::operator+ (const VectorEx& vec)const
	{
		return Vector4(x + vec.xFloat, y + vec.yFloat, z + vec.zFloat, w + vec.wFloat);
	}

	// Vector4 - KOVECTOR
	// ==========================================================
	Vector4 Vector4::operator- (const VectorEx& vec)const
	{
		return Vector4(x - vec.xFloat, y - vec.yFloat, z - vec.zFloat, w - vec.wFloat);
	}

	// Vector4 * KOVECTOR
	// ==========================================================
	Vector4 Vector4::operator* (const VectorEx& vec)const
	{
		return Vector4(x * vec.xFloat, y * vec.yFloat, z * vec.zFloat, w * vec.wFloat);
	}

	// Vector4 / KOVECTOR
	// ==========================================================
	Vector4 Vector4::operator/ (const VectorEx& vec)const
	{
		return Vector4(x / vec.xFloat, y / vec.yFloat, z /vec.zFloat, w / vec.wFloat);
	}

	// Vector4 += Vector4
	// ==========================================================
	Vector4& Vector4::operator+= (const Vector4& vec)
	{
		x += vec.x;
		y += vec.y;
		z += vec.z;
		w += vec.w;
		return *this;
	}

	// Vector4 -= Vector4
	// ==========================================================
	Vector4& Vector4::operator-= (const Vector4& vec)
	{
		x -= vec.x;
		y -= vec.y;
		z -= vec.z;
		w -= vec.w;
		return *this;
	}

	// Vector4 *= Vector4
	// ==========================================================
	Vector4& Vector4::operator*= (const Vector4& vec)
	{
		x *= vec.x;
		y *= vec.y;
		z *= vec.z;
		w *= vec.w;
		return *this;
	}

	// Vector4 /= Vector4
	// ==========================================================
	Vector4& Vector4::operator/= (const Vector4& vec)
	{
		x /= vec.x;
		y /= vec.y;
		z /= vec.z;
		w /= vec.w;
		return *this;
	}

	// Vector4 += float
	// ==========================================================
	Vector4& Vector4::operator+= (const float& t)
	{
		x += t;
		y += t;
		z += t;
		w += t;
		return *this;
	}

	// Vector4 -= float
	// ==========================================================
	Vector4& Vector4::operator-= (const float& t)
	{
		x -= t;
		y -= t;
		z -= t;
		w -= t;
		return *this;
	}

	// Vector4 *= float
	// ==========================================================
	Vector4& Vector4::operator*= (const float& t)
	{
		x *= t;
		y *= t;
		z *= t;
		w *= t;
		return *this;
	}

	// Vector4 /= float
	// ==========================================================
	Vector4& Vector4::operator/= (const float& t)
	{
		x /= t;
		y /= t;
		z /= t;
		w /= t;
		return *this;
	}

	// Vector4 += Vector2
	// ==========================================================
	Vector4& Vector4::operator+= (const Vector2& vec)
	{
		x += vec.x;
		y += vec.y;
		return *this;
	}

	// Vector4 -= Vector2
	// ==========================================================
	Vector4& Vector4::operator-= (const Vector2& vec)
	{
		x -= vec.x;
		y -= vec.y;
		return *this;
	}

	// Vector4 *= Vector2
	// ==========================================================
	Vector4& Vector4::operator*= (const Vector2& vec)
	{
		x *= vec.x;
		y *= vec.y;
		return *this;
	}

	// Vector4 /= Vector2
	// ==========================================================
	Vector4& Vector4::operator/= (const Vector2& vec)
	{
		x /= vec.x;
		y /= vec.y;
		return *this;
	}

	// Vector4 += Vector3
	// ==========================================================
	Vector4& Vector4::operator+= (const Vector3& vec)
	{
		x += vec.x;
		y += vec.y;
		z += vec.z;
		return *this;
	}

	// Vector4 -= Vector3
	// ==========================================================
	Vector4& Vector4::operator-= (const Vector3& vec)
	{
		x -= vec.x;
		y -= vec.y;
		z -= vec.z;
		return *this;
	}

	// Vector4 *= Vector3
	// ==========================================================
	Vector4& Vector4::operator*= (const Vector3& vec)
	{
		x *= vec.x;
		y *= vec.y;
		z *= vec.z;
		return *this;
	}

	// Vector4 /= Vector3
	// ==========================================================
	Vector4& Vector4::operator/= (const Vector3& vec)
	{
		x /= vec.x;
		y /= vec.y;
		z /= vec.z;
		return *this;
	}

	// Vector4 += KOVECTOR
	// ==========================================================
	Vector4& Vector4::operator+= (const VectorEx& vec)
	{
		x += vec.xFloat;
		y += vec.yFloat;
		z += vec.zFloat;
		w += vec.wFloat;
		return *this;
	}

	// Vector4 -= KOVECTOR
	// ==========================================================
	Vector4& Vector4::operator-= (const VectorEx& vec)
	{
		x -= vec.xFloat;
		y -= vec.yFloat;
		z -= vec.zFloat;
		w -= vec.wFloat;
		return *this;
	}

	// Vector4 *= KOVECTOR
	// ==========================================================
	Vector4& Vector4::operator*= (const VectorEx& vec)
	{
		x *= vec.xFloat;
		y *= vec.yFloat;
		z *= vec.zFloat;
		w *= vec.wFloat;
		return *this;
	}

	// Vector4 /= KOVECTOR
	// ==========================================================
	Vector4& Vector4::operator/= (const VectorEx& vec)
	{
		x /= vec.xFloat;
		y /= vec.yFloat;
		z /= vec.zFloat;
		w /= vec.wFloat;
		return *this;
	}

	// if(Vector4 == Vector4)
	// ==========================================================
	bool Vector4::operator== (const Vector4& vec)const
	{
		return (x == vec.x && y == vec.y && z == vec.z && w == vec.w);
	}

	// if(Vector4 != Vector4)
	// ==========================================================
	bool Vector4::operator!= (const Vector4& vec)const
	{
		return (x != vec.x && y != vec.y && z != vec.z && w != vec.w);
	}

	// if(Vector4 == Vector2)
	// ==========================================================
	bool Vector4::operator== (const Vector2& vec)const
	{
		return (x == vec.x && y == vec.y);
	}

	// if(Vector4 != Vector2)
	// ==========================================================
	bool Vector4::operator!= (const Vector2& vec)const
	{
		return (x != vec.x && y != vec.y);
	}

	// if(Vector4 == Vector3)
	// ==========================================================
	bool Vector4::operator== (const Vector3& vec)const
	{
		return (x == vec.x && y == vec.y && z == vec.z);
	}

	// if(Vector4 != Vector3)
	// ==========================================================
	bool Vector4::operator!= (const Vector3& vec)const
	{
		return (x != vec.x && y != vec.y && z != vec.z);
	}

	// if(Vector4 == KOVECTOR)
	// ==========================================================
	bool Vector4::operator== (const VectorEx& vec)const
	{
		return (x == vec.xFloat && y == vec.yFloat && z == vec.zFloat && w == vec.wFloat);
	}

	// if(Vector4 != KOVECTOR)
	// ==========================================================
	bool Vector4::operator!= (const VectorEx& vec)const
	{
		return (x != vec.xFloat && y != vec.yFloat && z != vec.zFloat && w != vec.wFloat);
	}

	// Vector4 = Vector2
	// ==========================================================
	Vector4& Vector4::operator= (const Vector2& vec)
	{
		x = vec.x;
		y = vec.y;
		z = 0.0f;
		w = 0.0f;
		return *this;
	}

	// Vector4 = Vector3
	// ==========================================================
	Vector4& Vector4::operator= (const Vector3& vec)
	{
		x = vec.x;
		y = vec.y;
		z = vec.z;
		w = 0.0f;
		return *this;
	}

	// Vector4 = KOVECTOR
	// ==========================================================
	Vector4& Vector4::operator= (const VectorEx& vec)
	{
		x = vec.xFloat;
		y = vec.yFloat;
		z = vec.zFloat;
		w = vec.wFloat;
		return *this;
	}
}