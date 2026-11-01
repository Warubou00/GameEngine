
/***********************************************************************************
 * @file    vector4.h
 * @brief   独自のVector4
 * @details
 *
 * @author  Keigo Onari
 * @date    2026/09/23
 * @version v_1.0.0
 *
 * @copyright
 * Copyright (c) 2024 Keigo Onari. All rights reserved.
 *
 * @note  名前空間、インクルードガードを変更
 ************************************************************************************/
#pragma once
#ifndef CORE_VECTOR4_H_
#define CORE_VECTOR4_H_

// ==========================================================
// インクルード
// ==========================================================
#include <DirectXMath.h>

#include "../../common.h"

namespace Core
{
	// ==========================================================
	// 前方宣言
	// ========================================================== 
	struct Vector2;
	struct Vector3;
#if 0
	struct KOVECTOR;
#endif

	// ==========================================================
	// Vector4
	// ========================================================== 
	struct CORE_API_CLASS Vector4
	{
		union {
			float _vec[4];
			struct {
				float _x, _y, _z, _w;
			};

			struct {
				float _r, _g, _b, _a;
			};
		};

		// コンストラクタ
		// ==========================================================
		Vector4() = default;
		Vector4(const Vector2& v);
		Vector4(const Vector2& v1, const Vector2& v2);
		Vector4(const Vector2& v, const float& t1, const float& t2);
		Vector4(const Vector3& v);
		Vector4(const Vector3& v, const float& t);
		Vector4(float X, float Y, float Z, float W) :_x(X), _y(Y), _z(Z), _w(W) {}
		Vector4(float XYZW) :_x(XYZW), _y(XYZW), _z(XYZW), _w(XYZW) {}
#if 0
		Vector4(const KOVECTOR& v);
#endif

		// ==========================================================
		// DirectX::XM系との暗黙的変換
		// ==========================================================
		operator DirectX::XMVECTOR() const {
			return DirectX::XMVectorSet(_x, _y, _z, _w);
		}

		Vector4& operator= (const DirectX::XMVECTOR& vec)
		{
			_x = vec.m128_f32[0];
			_y = vec.m128_f32[1];
			_z = vec.m128_f32[2];
			_w = vec.m128_f32[3];
			return *this;
		}

		// ==========================================================
		// 演算子のオーバーロード
		// ========================================================== 
		// +Vector4
		// ==========================================================
		Vector4 operator+ (void)const;
		// -Vector4
		// ==========================================================
		Vector4 operator- (void)const;

		// Vector4 + Vector4
		// ==========================================================
		Vector4 operator+ (const Vector4& vec)const;
		// Vector4 - Vector4
		// ==========================================================
		Vector4 operator- (const Vector4& vec)const;
		// Vector4 * Vector4
		// ==========================================================
		Vector4 operator* (const Vector4& vec)const;
		// Vector4 / Vector4
		// ==========================================================
		Vector4 operator/ (const Vector4& vec)const;
		// Vector4 + float
		// ==========================================================
		Vector4 operator+ (const float& t)const;
		// Vector4 - float
		// ==========================================================
		Vector4 operator- (const float& t)const;
		// Vector4 * float
		// ==========================================================
		Vector4 operator* (const float& t)const;
		// Vector4 / float
		// ==========================================================
		Vector4 operator/ (const float& t)const;
		// Vector4 + Vector2
		// _z,_wは無視
		// ==========================================================
		Vector4 operator+ (const Vector2& vec)const;
		// Vector4 - Vector2
		// _z,_wは無視
		// ==========================================================
		Vector4 operator- (const Vector2& vec)const;
		// Vector4 * Vector2
		// _z,_wは無視
		// ==========================================================
		Vector4 operator* (const Vector2& vec)const;
		// Vector4 / Vector2
		// _z,_wは無視
		// ==========================================================
		Vector4 operator/ (const Vector2& vec)const;
		// Vector4 + Vector3
		// _wは無視
		// ==========================================================
		Vector4 operator+ (const Vector3& vec)const;
		// Vector4 - Vector3
		// _wは無視
		// ==========================================================
		Vector4 operator- (const Vector3& vec)const;
		// Vector4 * Vector3
		// _wは無視
		// ==========================================================
		Vector4 operator* (const Vector3& vec)const;
		// Vector4 / Vector3
		// _wは無視
		// ==========================================================
		Vector4 operator/ (const Vector3& vec)const;

#if 0
		// Vector4 + KOVECTOR
		// ==========================================================
		Vector4 operator+ (const KOVECTOR& vec)const;
		// Vector4 - KOVECTOR
		// ==========================================================
		Vector4 operator- (const KOVECTOR& vec)const;
		// Vector4 * KOVECTOR
		// ==========================================================
		Vector4 operator* (const KOVECTOR& vec)const;
		// Vector4 / KOVECTOR
		// ==========================================================
		Vector4 operator/ (const KOVECTOR& vec)const;
#endif

		// Vector4 += Vector4
		// ==========================================================
		Vector4& operator+= (const Vector4& vec);
		// Vector4 -= Vector4
		// ==========================================================
		Vector4& operator-= (const Vector4& vec);
		// Vector4 *= Vector4
		// ==========================================================
		Vector4& operator*= (const Vector4& vec);
		// Vector4 /= Vector4
		// ==========================================================
		Vector4& operator/= (const Vector4& vec);
		// Vector4 += float
		// ==========================================================
		Vector4& operator+= (const float& t);
		// Vector4 -= float
		// ==========================================================
		Vector4& operator-= (const float& t);
		// Vector4 *= float
		// ==========================================================
		Vector4& operator*= (const float& t);
		// Vector4 /= float
		// ==========================================================
		Vector4& operator/= (const float& t);
		// Vector4 += Vector2
		// ==========================================================
		Vector4& operator+= (const Vector2& vec);
		// Vector4 -= Vector2
		// ==========================================================
		Vector4& operator-= (const Vector2& vec);
		// Vector4 *= Vector2
		// ==========================================================
		Vector4& operator*= (const Vector2& vec);
		// Vector4 /= Vector2
		// ==========================================================
		Vector4& operator/= (const Vector2& vec);
		// Vector4 += Vector3
		// ==========================================================
		Vector4& operator+= (const Vector3& vec);
		// Vector4 -= Vector3
		// ==========================================================
		Vector4& operator-= (const Vector3& vec);
		// Vector4 *= Vector3
		// ==========================================================
		Vector4& operator*= (const Vector3& vec);
		// Vector4 /= Vector3
		// ==========================================================
		Vector4& operator/= (const Vector3& vec);

#if 0
		// Vector4 += KOVECTOR
		// ==========================================================
		Vector4& operator+= (const KOVECTOR& vec);
		// Vector4 -= KOVECTOR
		// ==========================================================
		Vector4& operator-= (const KOVECTOR& vec);
		// Vector4 *= KOVECTOR
		// ==========================================================
		Vector4& operator*= (const KOVECTOR& vec);
		// Vector4 /= KOVECTOR
		// ==========================================================
		Vector4& operator/= (const KOVECTOR& vec);
#endif

		// if(Vector4 == Vector4)
		// ==========================================================
		bool operator== (const Vector4& vec)const;
		// if(Vector4 != Vector4)
		// ==========================================================
		bool operator!= (const Vector4& vec)const;
		// if(Vector4 == Vector2)
		// 引数の_z,_wは無視
		// ==========================================================
		bool operator== (const Vector2& vec)const;
		// if(Vector4 != Vector2)
		// 引数の_z,_wは無視
		// ==========================================================
		bool operator!= (const Vector2& vec)const;
		// if(Vector4 == Vector3)
		// 引数の_wは無視
		// ==========================================================
		bool operator== (const Vector3& vec)const;
		// if(Vector4 != Vector3)
		// 引数の_wは無視
		// ==========================================================
		bool operator!= (const Vector3& vec)const;

#if 0
		// if(Vector4 == KOVECTOR)
		// ==========================================================
		bool operator== (const KOVECTOR& vec)const;
		// if(Vector4 != KOVECTOR)
		// ===============================
		bool operator!= (const KOVECTOR& vec)const;
#endif

		// Vector4 = Vector2
		// _z,_w は0
		// ==========================================================
		Vector4& operator= (const Vector2& vec);
		// Vector4 = Vector3
		// _w は0
		// ==========================================================
		Vector4& operator= (const Vector3& vec);

#if 0
		// Vector4 = KOVECTOR
		// ==========================================================
		Vector4& operator= (const KOVECTOR& vec);
#endif

		// Vector4の値全て0.0fを生成
		// ==========================================================
		static const Vector4 zero;
		// Vector4の値全て1.0fを生成
		// ==========================================================
		static const Vector4 one;
		// _y =  1.0fのVector4を生成
		// ==========================================================
		static const Vector4 up;
		// _y = -1.0fのVector4を生成
		// ==========================================================
		static const Vector4 down;
		// _x =  1.0fのVector4を生成
		// ==========================================================
		static const Vector4 right;
		// _x = -1.0fのVector4を生成
		// ==========================================================
		static const Vector4 left;
		// _z =  1.0fのVector4を生成(左手座標系)
		// ==========================================================
		static const Vector4 frontLH;
		// _z = -1.0fのVector4を生成(左手座標系)
		// ==========================================================
		static const Vector4 backLH;
		// _z = -1.0fのVector4を生成(右手座標系)
		// ==========================================================
		static const Vector4 frontRH;
		// _z =  1.0fのVector4を生成(右手座標系)
		// ==========================================================
		static const Vector4 backRH;
		// _w =  1.0fのVector4を生成
		// ==========================================================
		static const Vector4 plusW;
		// _w = -1.0fのVector4を生成
		// ==========================================================
		static const Vector4 minusW;
	};

	typedef Vector4 float4;
}

#endif // CORE_VECTOR4_H_


/************************************
* End of file
************************************/