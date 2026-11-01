/************************************************
* koVector.cpp
* KOVECTOR
*
* 最終更新日:2023/12/26
* 作成者：Keigo Onari
*************************************************/

// =============================================================
// インクルード
// =============================================================
#include "MyVector.h"
#include "vector2.h"
#include "vector3.h"
#include "vector4.h"

namespace Core
{
	// =============================================================
	// 疑似的DirectX::XMVECTOR
	// =============================================================

	// コンストラクタ
	// =============================================================
	MyVECTOR::MyVECTOR(const Vector2& v) :_xFloat(v._x), _yFloat(v._y), _zFloat(0.0f), _wFloat(0.0f) {}
	MyVECTOR::MyVECTOR(const Vector2& v1, const Vector2& v2) : _xFloat(v1._x), _yFloat(v1._y), _zFloat(v2._x), _wFloat(v2._y) {}
	MyVECTOR::MyVECTOR(const Vector2& v, const float& t1, const float& t2) :_xFloat(v._x), _yFloat(v._y), _zFloat(t1), _wFloat(t2) {}
	MyVECTOR::MyVECTOR(const Vector3& v) :_xFloat(v._x), _yFloat(v._y), _zFloat(v._z), _wFloat(0.0f) {}
	MyVECTOR::MyVECTOR(const Vector3& v, const float& t) : _xFloat(v._x), _yFloat(v._y), _zFloat(v._z), _wFloat(t) {}
	MyVECTOR::MyVECTOR(const Vector4& v) :_xFloat(v._x), _yFloat(v._y), _zFloat(v._z), _wFloat(v._w) {}

	// 静的変数の初期化
	// =============================================================
	// KOVECTORの値全て 0.0fを生成
	// =============================================================
	const MyVECTOR MyVECTOR::zero(0.0f, 0.0f, 0.0f, 0.0f);
	// KOVECTORの値全て 1.0fを生成
	// =============================================================
	const MyVECTOR MyVECTOR::one(1.0f, 1.0f, 1.0f, 1.0f);
	// KOVECTORの値全て-1.0fを生成
	// =============================================================
	const MyVECTOR MyVECTOR::allminus(-1.0f, -1.0f, -1.0f, -1.0f);
	// _yFloat =  1.0fのKOVECTORを生成
	// =============================================================
	const MyVECTOR MyVECTOR::up(0.0f, 1.0f, 0.0f, 0.0f);
	// _yFloat = -1.0fのKOVECTORを生成
	// =============================================================
	const MyVECTOR MyVECTOR::down(0.0f, -1.0f, 0.0f, 0.0f);
	// _xFloat =  1.0fのKOVECTORを生成
	// =============================================================
	const MyVECTOR MyVECTOR::right(1.0f, 0.0f, 0.0f, 0.0f);
	// _xFloat = -1.0fのKOVECTORを生成
	// =============================================================
	const MyVECTOR MyVECTOR::left(-1.0f, 0.0f, 0.0f, 0.0f);
	// _zFloat =  1.0fのKOVECTORを生成(左手座標系)
	// =============================================================
	const MyVECTOR MyVECTOR::frontLH(0.0f, 0.0f, -1.0f, 0.0f);
	// _zFloat = -1.0fのKOVECTORを生成(左手座標系)
	// =============================================================
	const MyVECTOR MyVECTOR::backLH(0.0f, 0.0f, 1.0f, 0.0f);
	// _zFloat = -1.0fのKOVECTORを生成(右手座標系)
	// =============================================================
	const MyVECTOR MyVECTOR::frontRH(0.0f, 0.0f, 1.0f, 0.0f);
	// _zFloat =  1.0fのKOVECTORを生成(右手座標系)
	// =============================================================
	const MyVECTOR MyVECTOR::backRH(0.0f, 0.0f, -1.0f, 0.0f);

	// +KOVECTOR
	// =============================================================
	MyVECTOR MyVECTOR::operator+ (void)const
	{
		return *this;
	}

	// -KOVECTOR
	// =============================================================
	MyVECTOR MyVECTOR::operator- (void)const
	{
		return MyVECTOR(-_xFloat, -_yFloat, -_zFloat, -_wFloat);
	}

	// KOVECTOR + KOVECTOR
	// =============================================================
	MyVECTOR MyVECTOR::operator+ (const MyVECTOR& vec)const
	{
		return MyVECTOR(_xFloat + vec._xFloat, _yFloat + vec._yFloat, _zFloat + vec._zFloat, _wFloat + vec._wFloat);
	}

	// KOVECTOR - KOVECTOR
	// =============================================================
	MyVECTOR MyVECTOR::operator- (const MyVECTOR& vec)const
	{
		return MyVECTOR(_xFloat - vec._xFloat, _yFloat - vec._yFloat, _zFloat - vec._zFloat, _wFloat - vec._wFloat);
	}

	// KOVECTOR * KOVECTOR
	// =============================================================
	MyVECTOR MyVECTOR::operator* (const MyVECTOR& vec)const
	{
		return MyVECTOR(_xFloat * vec._xFloat, _yFloat * vec._yFloat, _zFloat * vec._zFloat, _wFloat * vec._wFloat);
	}

	// KOVECTOR / KOVECTOR
	// =============================================================
	MyVECTOR MyVECTOR::operator/ (const MyVECTOR& vec)const
	{
		return MyVECTOR(_xFloat / vec._xFloat, _yFloat / vec._yFloat, _zFloat / vec._zFloat, _wFloat / vec._wFloat);
	}

	// KOVECTOR + float
	// =============================================================
	MyVECTOR MyVECTOR::operator+(const float& t)const
	{
		return MyVECTOR(_xFloat + t, _yFloat + t, _zFloat + t, _wFloat + t);
	}

	// KOVECTOR - float
	// =============================================================
	MyVECTOR MyVECTOR::operator- (const float& t)const
	{
		return MyVECTOR(_xFloat - t, _yFloat - t, _zFloat - t, _wFloat - t);
	}

	// KOVECTOR * float
	// =============================================================
	MyVECTOR MyVECTOR::operator* (const float& t)const
	{
		return MyVECTOR(_xFloat * t, _yFloat * t, _zFloat * t, _wFloat * t);
	}

	// KOVECTOR / float
	// =============================================================
	MyVECTOR MyVECTOR::operator/ (const float& t)const
	{
		return MyVECTOR(_xFloat / t, _yFloat / t, _zFloat / t, _wFloat / t);
	}

	// KOVECTOR + Vector2
	// =============================================================
	MyVECTOR MyVECTOR::operator+ (const Vector2& vec)const
	{
		return MyVECTOR(_xFloat + vec._x, _yFloat + vec._y, _zFloat, _wFloat);
	}

	// KOVECTOR - Vector2
	// =============================================================
	MyVECTOR MyVECTOR::operator- (const Vector2& vec)const
	{
		return MyVECTOR(_xFloat - vec._x, _yFloat - vec._y, _zFloat, _wFloat);
	}

	// KOVECTOR * Vector2
	// =============================================================
	MyVECTOR MyVECTOR::operator* (const Vector2& vec)const
	{
		return MyVECTOR(_xFloat * vec._x, _yFloat * vec._y, _zFloat, _wFloat);
	}

	// KOVECTOR / Vector2
	// =============================================================
	MyVECTOR MyVECTOR::operator/ (const Vector2& vec)const
	{
		return MyVECTOR(_xFloat / vec._x, _yFloat / vec._y, _zFloat, _wFloat);
	}

	// KOVECTOR + Vector3
	// =============================================================
	MyVECTOR MyVECTOR::operator+ (const Vector3& vec)const
	{
		return MyVECTOR(_xFloat + vec._x, _yFloat + vec._y, _zFloat + vec._z, _wFloat);
	}

	// KOVECTOR - Vector3
	// =============================================================
	MyVECTOR MyVECTOR::operator- (const Vector3& vec)const
	{
		return MyVECTOR(_xFloat - vec._x, _yFloat - vec._y, _zFloat - vec._z, _wFloat);
	}

	// KOVECTOR * Vector3
	// =============================================================
	MyVECTOR MyVECTOR::operator* (const Vector3& vec)const
	{
		return MyVECTOR(_xFloat * vec._x, _yFloat * vec._y, _zFloat * vec._z, _wFloat);
	}

	// KOVECTOR / Vector3
	// =============================================================
	MyVECTOR MyVECTOR::operator/ (const Vector3& vec)const
	{
		return MyVECTOR(_xFloat / vec._x, _yFloat / vec._y, _zFloat / vec._z, _wFloat);
	}

	// KOVECTOR + Vector4
	// =============================================================
	MyVECTOR MyVECTOR::operator+ (const Vector4& vec)const
	{
		return MyVECTOR(_xFloat + vec._x, _yFloat + vec._y, _zFloat + vec._z, _wFloat + vec._w);
	}

	// KOVECTOR - Vector4
	// =============================================================
	MyVECTOR MyVECTOR::operator- (const Vector4& vec)const
	{
		return MyVECTOR(_xFloat - vec._x, _yFloat - vec._y, _zFloat - vec._z, _wFloat - vec._w);
	}

	// KOVECTOR * Vector4
	// =============================================================
	MyVECTOR MyVECTOR::operator* (const Vector4& vec)const
	{
		return MyVECTOR(_xFloat * vec._x, _yFloat * vec._y, _zFloat * vec._z, _wFloat * vec._w);
	}

	// KOVECTOR / Vector4
	// =============================================================
	MyVECTOR MyVECTOR::operator/ (const Vector4& vec)const
	{
		return MyVECTOR(_xFloat / vec._x, _yFloat / vec._y, _zFloat / vec._z, _wFloat / vec._w);
	}

	// KOVECTOR += KOVECTOR
	// =============================================================
	MyVECTOR& MyVECTOR::operator+= (const MyVECTOR& vec)
	{
		_xFloat += vec._xFloat;
		_yFloat += vec._yFloat;
		_zFloat += vec._zFloat;
		_wFloat += vec._wFloat;
		return *this;
	}

	// KOVECTOR -= KOVECTOR
	// =============================================================
	MyVECTOR& MyVECTOR::operator-= (const MyVECTOR& vec)
	{
		_xFloat -= vec._xFloat;
		_yFloat -= vec._yFloat;
		_zFloat -= vec._zFloat;
		_wFloat -= vec._wFloat;
		return *this;
	}

	// KOVECTOR *= KOVECTOR
	// =============================================================
	MyVECTOR& MyVECTOR::operator*= (const MyVECTOR& vec)
	{
		_xFloat *= vec._xFloat;
		_yFloat *= vec._yFloat;
		_zFloat *= vec._zFloat;
		_wFloat *= vec._wFloat;
		return *this;
	}

	// KOVECTOR /= KOVECTOR
	// =============================================================
	MyVECTOR& MyVECTOR::operator/= (const MyVECTOR& vec)
	{
		_xFloat /= vec._xFloat;
		_yFloat /= vec._yFloat;
		_zFloat /= vec._zFloat;
		_wFloat /= vec._wFloat;
		return *this;
	}

	// KOVECTOR += Vector4
	// =============================================================
	MyVECTOR& MyVECTOR::operator+= (const Vector4& vec)
	{
		_xFloat += vec._x;
		_yFloat += vec._y;
		_zFloat += vec._z;
		_wFloat += vec._w;
		return *this;
	}

	// KOVECTOR -= Vector4
	// =============================================================
	MyVECTOR& MyVECTOR::operator-= (const Vector4& vec)
	{
		_xFloat -= vec._x;
		_yFloat -= vec._y;
		_zFloat -= vec._z;
		_wFloat -= vec._w;
		return *this;
	}

	// KOVECTOR *= Vector4
	// =============================================================
	MyVECTOR& MyVECTOR::operator*= (const Vector4& vec)
	{
		_xFloat *= vec._x;
		_yFloat *= vec._y;
		_zFloat *= vec._z;
		_wFloat *= vec._w;
		return *this;
	}

	// KOVECTOR /= Vector4
	// =============================================================
	MyVECTOR& MyVECTOR::operator/= (const Vector4& vec)
	{
		_xFloat /= vec._x;
		_yFloat /= vec._y;
		_zFloat /= vec._z;
		_wFloat /= vec._w;
		return *this;
	}

	// KOVECTOR += float
	// =============================================================
	MyVECTOR& MyVECTOR::operator+=(const float& t)
	{
		_xFloat += t;
		_yFloat += t;
		_zFloat += t;
		_wFloat += t;
		return *this;
	}

	// KOVECTOR -= float
	// =============================================================
	MyVECTOR& MyVECTOR::operator-=(const float& t)
	{
		_xFloat -= t;
		_yFloat -= t;
		_zFloat -= t;
		_wFloat -= t;
		return *this;
	}

	// KOVECTOR *= float
	// =============================================================
	MyVECTOR& MyVECTOR::operator*=(const float& t)
	{
		_xFloat *= t;
		_yFloat *= t;
		_zFloat *= t;
		_wFloat *= t;
		return *this;
	}

	// KOVECTOR /= float
	// =============================================================
	MyVECTOR& MyVECTOR::operator/=(const float& t)
	{
		_xFloat /= t;
		_yFloat /= t;
		_zFloat /= t;
		_wFloat /= t;
		return *this;
	}

	// if(KOVECTOR == KOVECTOR)
	// =============================================================
	bool MyVECTOR::operator== (const MyVECTOR& vec)
	{
		return (_xFloat == vec._xFloat && _yFloat == vec._yFloat && _zFloat == vec._zFloat && _wFloat == vec._wFloat);
	}

	// if(KOVECTOR != KOVECTOR)
	// =============================================================
	bool MyVECTOR::operator!= (const MyVECTOR& vec)
	{
		return (_xFloat != vec._xFloat && _yFloat != vec._yFloat && _zFloat != vec._zFloat && _wFloat != vec._wFloat);
	}

	// if(KOVECTOR == Vector2)
	// =============================================================
	bool MyVECTOR::operator== (const Vector2& vec)
	{
		return (_xFloat == vec._x && _yFloat == vec._y);
	}

	// if(KOVECTOR != Vector2)
	// =============================================================
	bool MyVECTOR::operator!= (const Vector2& vec)
	{
		return (_xFloat != vec._x && _yFloat != vec._y);
	}

	// if(KOVECTOR == Vector3)
	// =============================================================
	bool MyVECTOR::operator== (const Vector3& vec)
	{
		return (_xFloat == vec._x && _yFloat == vec._y && _zFloat == vec._z);
	}

	// if(KOVECTOR != Vector3)
	// =============================================================
	bool MyVECTOR::operator!= (const Vector3& vec)
	{
		return (_xFloat != vec._x && _yFloat != vec._y && _zFloat != vec._z);
	}

	// if(KOVECTOR == Vecror4)
	// =============================================================
	bool MyVECTOR::operator== (const Vector4& vec)
	{
		return (_xFloat == vec._x && _yFloat == vec._y && _zFloat == vec._z && _wFloat == vec._w);
	}

	// if(KOVECTOR != Vecror4)
	// =============================================================
	bool MyVECTOR::operator!= (const Vector4& vec)
	{
		return (_xFloat != vec._x && _yFloat != vec._y && _zFloat != vec._z && _wFloat != vec._w);
	}

	// KOVECTOR = float
	// =============================================================
	MyVECTOR& MyVECTOR::operator=(const float& t)
	{
		_xFloat = t;
		_yFloat = t;
		_zFloat = t;
		_wFloat = t;
		return *this;
	}

	// KOVECTOR = Vector2
	// =============================================================
	MyVECTOR& MyVECTOR::operator= (const Vector2& vec)
	{
		_xFloat = vec._x;
		_yFloat = vec._y;
		_zFloat = 0.0f;
		_wFloat = 0.0f;
		return *this;
	}

	// KOVECTOR = Vector3
	// =============================================================
	MyVECTOR& MyVECTOR::operator=(const Vector3& vec)
	{
		_xFloat = vec._x;
		_yFloat = vec._y;
		_zFloat = vec._z;
		_wFloat = 0.0f;
		return *this;
	}

	// KOVECTOR = Vector4
	// =============================================================
	MyVECTOR& MyVECTOR::operator=(const Vector4& vec)
	{
		_xFloat = vec._x;
		_yFloat = vec._y;
		_zFloat = vec._z;
		_wFloat = vec._w;
		return *this;
	}
}