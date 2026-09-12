
/***********************************************************************************
 * @file    vector4.h
 * @brief   独自のVector4
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
#ifndef VECTOR4_H_
#define VECTOR4_H_

// ==========================================================
// インクルード
// ==========================================================
#include <DirectXMath.h>

namespace Engine
{
	// ==========================================================
	// 前方宣言
	// ========================================================== 
	struct Vector2;
	struct Vector3;
	struct VectorEx;

	// ==========================================================
	// Vector4
	// ========================================================== 
	struct Vector4
	{
		union {
			float vec[4];
			struct {
				float x, y, z, w;
			};

			struct {
				float r, g, b, a;
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
		Vector4(const VectorEx& v);
		Vector4(float X, float Y, float Z, float W) :x(X), y(Y), z(Z), w(W) {}
		Vector4(float XYZW) :x(XYZW), y(XYZW), z(XYZW), w(XYZW) {}

		// ==========================================================
		// DirectX::XM系との暗黙的変換
		// ==========================================================
		operator DirectX::XMVECTOR() const {
			return DirectX::XMVectorSet(x, y, z, w);
		}

		Vector4& operator= (const DirectX::XMVECTOR& vec)
		{
			x = vec.m128_f32[0];
			y = vec.m128_f32[1];
			z = vec.m128_f32[2];
			w = vec.m128_f32[3];
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
		// Vector4 + KOVECTOR
		// ==========================================================
		Vector4 operator+ (const VectorEx& vec)const;
		// Vector4 - KOVECTOR
		// ==========================================================
		Vector4 operator- (const VectorEx& vec)const;
		// Vector4 * KOVECTOR
		// ==========================================================
		Vector4 operator* (const VectorEx& vec)const;
		// Vector4 / KOVECTOR
		// ==========================================================
		Vector4 operator/ (const VectorEx& vec)const;

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
		// Vector4 += KOVECTOR
		// ==========================================================
		Vector4& operator+= (const VectorEx& vec);
		// Vector4 -= KOVECTOR
		// ==========================================================
		Vector4& operator-= (const VectorEx& vec);
		// Vector4 *= KOVECTOR
		// ==========================================================
		Vector4& operator*= (const VectorEx& vec);
		// Vector4 /= KOVECTOR
		// ==========================================================
		Vector4& operator/= (const VectorEx& vec);

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
		// if(Vector4 == KOVECTOR)
		// ==========================================================
		bool operator== (const VectorEx& vec)const;
		// if(Vector4 != KOVECTOR)
		// ===============================
		bool operator!= (const VectorEx& vec)const;

		// Vector4 = Vector2
		// _z,_w は0
		// ==========================================================
		Vector4& operator= (const Vector2& vec);
		// Vector4 = Vector3
		// _w は0
		// ==========================================================
		Vector4& operator= (const Vector3& vec);
		// Vector4 = KOVECTOR
		// ==========================================================
		Vector4& operator= (const VectorEx& vec);

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
}

#endif // VECTOR4_H_

/************************************
* End of file
************************************/