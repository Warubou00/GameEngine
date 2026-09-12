/************************************************
* vectorEx.cpp
*
* 最終更新日:2026/1/13
* 作成者：Keigo Onari
*************************************************/

// =============================================================
// インクルード
// =============================================================
#include "vectorEx.h"
#include "vector2.h"
#include "vector3.h"
#include "vector4.h"

namespace Engine
{
	// =============================================================
	// 疑似的DirectX::XMVECTOR
	// =============================================================

	// コンストラクタ
	// =============================================================
	VectorEx::VectorEx(const Vector2& v) :xFloat(v.x), yFloat(v.y), zFloat(0.0f), wFloat(0.0f) {}
	VectorEx::VectorEx(const Vector2& v1, const Vector2& v2) : xFloat(v1.x), yFloat(v1.y), zFloat(v2.x), wFloat(v2.y) {}
	VectorEx::VectorEx(const Vector2& v, const float& t1, const float& t2) :xFloat(v.x), yFloat(v.y), zFloat(t1), wFloat(t2) {}
	VectorEx::VectorEx(const Vector3& v) :xFloat(v.x), yFloat(v.y), zFloat(v.z), wFloat(0.0f) {}
	VectorEx::VectorEx(const Vector3& v, const float& t) : xFloat(v.x), yFloat(v.y), zFloat(v.z), wFloat(t) {}
	VectorEx::VectorEx(const Vector4& v) :xFloat(v.x), yFloat(v.y), zFloat(v.z), wFloat(v.w) {}

	// 静的変数の初期化
	// =============================================================
	// KOVECTORの値全て 0.0fを生成
	// =============================================================
	const VectorEx VectorEx::zero(0.0f, 0.0f, 0.0f, 0.0f);
	// KOVECTORの値全て 1.0fを生成
	// =============================================================
	const VectorEx VectorEx::one(1.0f, 1.0f, 1.0f, 1.0f);
	// KOVECTORの値全て-1.0fを生成
	// =============================================================
	const VectorEx VectorEx::allminus(-1.0f, -1.0f, -1.0f, -1.0f);
	// _yFloat =  1.0fのKOVECTORを生成
	// =============================================================
	const VectorEx VectorEx::up(0.0f, 1.0f, 0.0f, 0.0f);
	// _yFloat = -1.0fのKOVECTORを生成
	// =============================================================
	const VectorEx VectorEx::down(0.0f, -1.0f, 0.0f, 0.0f);
	// _xFloat =  1.0fのKOVECTORを生成
	// =============================================================
	const VectorEx VectorEx::right(1.0f, 0.0f, 0.0f, 0.0f);
	// _xFloat = -1.0fのKOVECTORを生成
	// =============================================================
	const VectorEx VectorEx::left(-1.0f, 0.0f, 0.0f, 0.0f);
	// _zFloat =  1.0fのKOVECTORを生成(左手座標系)
	// =============================================================
	const VectorEx VectorEx::frontLH(0.0f, 0.0f, -1.0f, 0.0f);
	// _zFloat = -1.0fのKOVECTORを生成(左手座標系)
	// =============================================================
	const VectorEx VectorEx::backLH(0.0f, 0.0f, 1.0f, 0.0f);
	// _zFloat = -1.0fのKOVECTORを生成(右手座標系)
	// =============================================================
	const VectorEx VectorEx::frontRH(0.0f, 0.0f, 1.0f, 0.0f);
	// _zFloat =  1.0fのKOVECTORを生成(右手座標系)
	// =============================================================
	const VectorEx VectorEx::backRH(0.0f, 0.0f, -1.0f, 0.0f);

	// +KOVECTOR
	// =============================================================
	VectorEx VectorEx::operator+ (void)const
	{
		return *this;
	}

	// -KOVECTOR
	// =============================================================
	VectorEx VectorEx::operator- (void)const
	{
		return VectorEx(-xFloat, -yFloat, -zFloat, -wFloat);
	}

	// KOVECTOR + KOVECTOR
	// =============================================================
	VectorEx VectorEx::operator+ (const VectorEx& vec)const
	{
		return VectorEx(xFloat + vec.xFloat, yFloat + vec.yFloat, zFloat + vec.zFloat, wFloat + vec.wFloat);
	}

	// KOVECTOR - KOVECTOR
	// =============================================================
	VectorEx VectorEx::operator- (const VectorEx& vec)const
	{
		return VectorEx(xFloat - vec.xFloat, yFloat - vec.yFloat, zFloat - vec.zFloat, wFloat - vec.wFloat);
	}

	// KOVECTOR * KOVECTOR
	// =============================================================
	VectorEx VectorEx::operator* (const VectorEx& vec)const
	{
		return VectorEx(xFloat * vec.xFloat, yFloat * vec.yFloat, zFloat * vec.zFloat, wFloat * vec.wFloat);
	}

	// KOVECTOR / KOVECTOR
	// =============================================================
	VectorEx VectorEx::operator/ (const VectorEx& vec)const
	{
		return VectorEx(xFloat / vec.xFloat, yFloat / vec.yFloat, zFloat / vec.zFloat, wFloat / vec.wFloat);
	}

	// KOVECTOR + float
	// =============================================================
	VectorEx VectorEx::operator+(const float& t)const
	{
		return VectorEx(xFloat + t, yFloat + t, zFloat + t, wFloat + t);
	}

	// KOVECTOR - float
	// =============================================================
	VectorEx VectorEx::operator- (const float& t)const
	{
		return VectorEx(xFloat - t, yFloat - t, zFloat - t, wFloat - t);
	}

	// KOVECTOR * float
	// =============================================================
	VectorEx VectorEx::operator* (const float& t)const
	{
		return VectorEx(xFloat * t, yFloat * t, zFloat * t, wFloat * t);
	}

	// KOVECTOR / float
	// =============================================================
	VectorEx VectorEx::operator/ (const float& t)const
	{
		return VectorEx(xFloat / t, yFloat / t, zFloat / t, wFloat / t);
	}

	// KOVECTOR + Vector2
	// =============================================================
	VectorEx VectorEx::operator+ (const Vector2& vec)const
	{
		return VectorEx(xFloat + vec.x, yFloat + vec.y, zFloat, wFloat);
	}

	// KOVECTOR - Vector2
	// =============================================================
	VectorEx VectorEx::operator- (const Vector2& vec)const
	{
		return VectorEx(xFloat - vec.x, yFloat - vec.y, zFloat, wFloat);
	}

	// KOVECTOR * Vector2
	// =============================================================
	VectorEx VectorEx::operator* (const Vector2& vec)const
	{
		return VectorEx(xFloat * vec.x, yFloat * vec.y, zFloat, wFloat);
	}

	// KOVECTOR / Vector2
	// =============================================================
	VectorEx VectorEx::operator/ (const Vector2& vec)const
	{
		return VectorEx(xFloat / vec.x, yFloat / vec.y, zFloat, wFloat);
	}

	// KOVECTOR + Vector3
	// =============================================================
	VectorEx VectorEx::operator+ (const Vector3& vec)const
	{
		return VectorEx(xFloat + vec.x, yFloat + vec.y, zFloat + vec.z, wFloat);
	}

	// KOVECTOR - Vector3
	// =============================================================
	VectorEx VectorEx::operator- (const Vector3& vec)const
	{
		return VectorEx(xFloat - vec.x, yFloat - vec.y, zFloat - vec.z, wFloat);
	}

	// KOVECTOR * Vector3
	// =============================================================
	VectorEx VectorEx::operator* (const Vector3& vec)const
	{
		return VectorEx(xFloat * vec.x, yFloat * vec.y, zFloat * vec.z, wFloat);
	}

	// KOVECTOR / Vector3
	// =============================================================
	VectorEx VectorEx::operator/ (const Vector3& vec)const
	{
		return VectorEx(xFloat / vec.x, yFloat / vec.y, zFloat / vec.z, wFloat);
	}

	// KOVECTOR + Vector4
	// =============================================================
	VectorEx VectorEx::operator+ (const Vector4& vec)const
	{
		return VectorEx(xFloat + vec.x, yFloat + vec.y, zFloat + vec.z, wFloat + vec.w);
	}

	// KOVECTOR - Vector4
	// =============================================================
	VectorEx VectorEx::operator- (const Vector4& vec)const
	{
		return VectorEx(xFloat - vec.x, yFloat - vec.y, zFloat - vec.z, wFloat - vec.w);
	}

	// KOVECTOR * Vector4
	// =============================================================
	VectorEx VectorEx::operator* (const Vector4& vec)const
	{
		return VectorEx(xFloat * vec.x, yFloat * vec.y, zFloat * vec.z, wFloat * vec.w);
	}

	// KOVECTOR / Vector4
	// =============================================================
	VectorEx VectorEx::operator/ (const Vector4& vec)const
	{
		return VectorEx(xFloat / vec.x, yFloat / vec.y, zFloat / vec.z, wFloat / vec.w);
	}

	// KOVECTOR += KOVECTOR
	// =============================================================
	VectorEx& VectorEx::operator+= (const VectorEx& vec)
	{
		xFloat += vec.xFloat;
		yFloat += vec.yFloat;
		zFloat += vec.zFloat;
		wFloat += vec.wFloat;
		return *this;
	}

	// KOVECTOR -= KOVECTOR
	// =============================================================
	VectorEx& VectorEx::operator-= (const VectorEx& vec)
	{
		xFloat -= vec.xFloat;
		yFloat -= vec.yFloat;
		zFloat -= vec.zFloat;
		wFloat -= vec.wFloat;
		return *this;
	}

	// KOVECTOR *= KOVECTOR
	// =============================================================
	VectorEx& VectorEx::operator*= (const VectorEx& vec)
	{
		xFloat *= vec.xFloat;
		yFloat *= vec.yFloat;
		zFloat *= vec.zFloat;
		wFloat *= vec.wFloat;
		return *this;
	}

	// KOVECTOR /= KOVECTOR
	// =============================================================
	VectorEx& VectorEx::operator/= (const VectorEx& vec)
	{
		xFloat /= vec.xFloat;
		yFloat /= vec.yFloat;
		zFloat /= vec.zFloat;
		wFloat /= vec.wFloat;
		return *this;
	}

	// KOVECTOR += Vector4
	// =============================================================
	VectorEx& VectorEx::operator+= (const Vector4& vec)
	{
		xFloat += vec.x;
		yFloat += vec.y;
		zFloat += vec.z;
		wFloat += vec.w;
		return *this;
	}

	// KOVECTOR -= Vector4
	// =============================================================
	VectorEx& VectorEx::operator-= (const Vector4& vec)
	{
		xFloat -= vec.x;
		yFloat -= vec.y;
		zFloat -= vec.z;
		wFloat -= vec.w;
		return *this;
	}

	// KOVECTOR *= Vector4
	// =============================================================
	VectorEx& VectorEx::operator*= (const Vector4& vec)
	{
		xFloat *= vec.x;
		yFloat *= vec.y;
		zFloat *= vec.z;
		wFloat *= vec.w;
		return *this;
	}

	// KOVECTOR /= Vector4
	// =============================================================
	VectorEx& VectorEx::operator/= (const Vector4& vec)
	{
		xFloat /= vec.x;
		yFloat /= vec.y;
		zFloat /= vec.z;
		wFloat /= vec.w;
		return *this;
	}

	// KOVECTOR += float
	// =============================================================
	VectorEx& VectorEx::operator+=(const float& t)
	{
		xFloat += t;
		yFloat += t;
		zFloat += t;
		wFloat += t;
		return *this;
	}

	// KOVECTOR -= float
	// =============================================================
	VectorEx& VectorEx::operator-=(const float& t)
	{
		xFloat -= t;
		yFloat -= t;
		zFloat -= t;
		wFloat -= t;
		return *this;
	}

	// KOVECTOR *= float
	// =============================================================
	VectorEx& VectorEx::operator*=(const float& t)
	{
		xFloat *= t;
		yFloat *= t;
		zFloat *= t;
		wFloat *= t;
		return *this;
	}

	// KOVECTOR /= float
	// =============================================================
	VectorEx& VectorEx::operator/=(const float& t)
	{
		xFloat /= t;
		yFloat /= t;
		zFloat /= t;
		wFloat /= t;
		return *this;
	}

	// if(KOVECTOR == KOVECTOR)
	// =============================================================
	bool VectorEx::operator== (const VectorEx& vec)
	{
		return (xFloat == vec.xFloat && yFloat == vec.yFloat && zFloat == vec.zFloat && wFloat == vec.wFloat);
	}

	// if(KOVECTOR != KOVECTOR)
	// =============================================================
	bool VectorEx::operator!= (const VectorEx& vec)
	{
		return (xFloat != vec.xFloat && yFloat != vec.yFloat && zFloat != vec.zFloat && wFloat != vec.wFloat);
	}

	// if(KOVECTOR == Vector2)
	// =============================================================
	bool VectorEx::operator== (const Vector2& vec)
	{
		return (xFloat == vec.x && yFloat == vec.y);
	}

	// if(KOVECTOR != Vector2)
	// =============================================================
	bool VectorEx::operator!= (const Vector2& vec)
	{
		return (xFloat != vec.x && yFloat != vec.y);
	}

	// if(KOVECTOR == Vector3)
	// =============================================================
	bool VectorEx::operator== (const Vector3& vec)
	{
		return (xFloat == vec.x && yFloat == vec.y && zFloat == vec.z);
	}

	// if(KOVECTOR != Vector3)
	// =============================================================
	bool VectorEx::operator!= (const Vector3& vec)
	{
		return (xFloat != vec.x && yFloat != vec.y && zFloat != vec.z);
	}

	// if(KOVECTOR == Vecror4)
	// =============================================================
	bool VectorEx::operator== (const Vector4& vec)
	{
		return (xFloat == vec.x && yFloat == vec.y && zFloat == vec.z && wFloat == vec.w);
	}

	// if(KOVECTOR != Vecror4)
	// =============================================================
	bool VectorEx::operator!= (const Vector4& vec)
	{
		return (xFloat != vec.x && yFloat != vec.y && zFloat != vec.z && wFloat != vec.w);
	}

	// KOVECTOR = float
	// =============================================================
	VectorEx& VectorEx::operator=(const float& t)
	{
		xFloat = t;
		yFloat = t;
		zFloat = t;
		wFloat = t;
		return *this;
	}

	// KOVECTOR = Vector2
	// =============================================================
	VectorEx& VectorEx::operator= (const Vector2& vec)
	{
		xFloat = vec.x;
		yFloat = vec.y;
		zFloat = 0.0f;
		wFloat = 0.0f;
		return *this;
	}

	// KOVECTOR = Vector3
	// =============================================================
	VectorEx& VectorEx::operator=(const Vector3& vec)
	{
		xFloat = vec.x;
		yFloat = vec.y;
		zFloat = vec.z;
		wFloat = 0.0f;
		return *this;
	}

	// KOVECTOR = Vector4
	// =============================================================
	VectorEx& VectorEx::operator=(const Vector4& vec)
	{
		xFloat = vec.x;
		yFloat = vec.y;
		zFloat = vec.z;
		wFloat = vec.w;
		return *this;
	}
}