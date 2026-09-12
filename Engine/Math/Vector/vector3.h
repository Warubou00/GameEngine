
/***********************************************************************************
 * @file    vector3.h
 * @brief   独自のVector3
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
#ifndef VECTOR3_H_
#define VECTOR3_H_

// =============================================================
// インクルード
// =============================================================
#include <DirectXMath.h>

namespace Engine
{
	// =============================================================
	// 前方宣言
	// =============================================================
	struct Vector2;
	struct Vector4;
	struct VectorEx;

	// =============================================================
	// Vector3
	// =============================================================
	struct Vector3
	{
		union {
			float vec[3];
			struct {
				float x, y, z;
			};
		};

		// =============================================================
		// コンストラクタ
		// =============================================================
		Vector3() = default;
		Vector3(const Vector2& v);
		Vector3(const Vector2& v, const float& t);
		Vector3(const Vector4& v);
		Vector3(const VectorEx& v);
		Vector3(float X, float Y, float Z) :x(X), y(Y), z(Z) {}
		Vector3(float XYZ) :x(XYZ), y(XYZ), z(XYZ) {}

		// ========================================================================================
		// DirectX::XM系との暗黙的変換
		// ========================================================================================
		operator DirectX::XMVECTOR() const {
			return DirectX::XMVectorSet(x, y, z, 0.0f);
		}

		Vector3& operator= (const DirectX::XMVECTOR& vec)
		{
			x = vec.m128_f32[0];
			y = vec.m128_f32[1];
			z = vec.m128_f32[2];
			return *this;
		}

		// ========================================================================================
		// 演算子のオーバーロード
		// ========================================================================================
		// +Vector3
		// =============================================================
		Vector3 operator+ (void)const;
		// -Vector3
		// =============================================================
		Vector3 operator- (void)const;

		// Vector3 + Vector3
		// =============================================================
		Vector3 operator+ (const Vector3& vec)const;
		// Vector3 - Vector3
		// =============================================================
		Vector3 operator- (const Vector3& vec)const;
		// Vector3 * Vector3
		// =============================================================
		Vector3 operator* (const Vector3& vec)const;
		// Vector3 / Vector3
		// =============================================================
		Vector3 operator/ (const Vector3& vec)const;
		// Vector3 + float
		// =============================================================
		Vector3 operator+ (const float& t)const;
		// Vector3 - float
		// =============================================================
		Vector3 operator- (const float& t)const;
		// Vector3 * float
		// =============================================================
		Vector3 operator* (const float& t)const;
		// Vector3 / float
		// =============================================================
		Vector3 operator/ (const float& t)const;
		// Vector3 + Vector2
		// _zは無視
		// =============================================================
		Vector3 operator+ (const Vector2& vec)const;
		// Vector3 - Vector2
		// _zは無視
		// =============================================================
		Vector3 operator- (const Vector2& vec)const;
		// Vector3 * Vector2
		// _zは無視
		// =============================================================
		Vector3 operator* (const Vector2& vec)const;
		// Vector3 / Vector2
		// _zは無視
		// =============================================================
		Vector3 operator/ (const Vector2& vec)const;
		// Vector3 + Vector4
		// 引数の_wは無視
		// =============================================================
		Vector3 operator+ (const Vector4& vec)const;
		// Vector3 - Vector4
		// 引数の_wは無視
		// =============================================================
		Vector3 operator- (const Vector4& vec)const;
		// Vector3 * Vector4
		// 引数の_wは無視
		// =============================================================
		Vector3 operator* (const Vector4& vec)const;
		// Vector3 / Vector4
		// 引数の_wは無視
		// =============================================================
		Vector3 operator/ (const Vector4& vec)const;
		// Vector3 + KOVECTOR
		// 引数の_wFloatは無視
		// =============================================================
		Vector3 operator+ (const VectorEx& vec)const;
		// Vector3 - KOVECTOR
		// 引数の_wFloatは無視
		// =============================================================
		Vector3 operator- (const VectorEx& vec)const;
		// Vector3 * KOVECTOR
		// 引数の_wFloatは無視
		// =============================================================
		Vector3 operator* (const VectorEx& vec)const;
		// Vector3 / KOVECTOR
		// 引数の_wFloatは無視
		// =============================================================
		Vector3 operator/ (const VectorEx& vec)const;

		// Vector3 += Vector3
		// =============================================================
		Vector3& operator+= (const Vector3& vec);
		// Vector3 -= Vector3
		// =============================================================
		Vector3& operator-= (const Vector3& vec);
		// Vector3 *= Vector3
		// =============================================================
		Vector3& operator*= (const Vector3& vec);
		// Vector3 /= Vector3
		// =============================================================
		Vector3& operator/= (const Vector3& vec);
		// Vector3 += float
		// =============================================================
		Vector3& operator+= (const float& t);
		// Vector3 -= float
		// =============================================================
		Vector3& operator-= (const float& t);
		// Vector3 *= float
		// =============================================================
		Vector3& operator*= (const float& t);
		// Vector3 /= float
		// =============================================================
		Vector3& operator/= (const float& t);
		// Vector3 += Vector2
		// _zは無視
		// =============================================================
		Vector3& operator+= (const Vector2& vec);
		// Vector3 -= Vector2
		// _zは無視
		// =============================================================
		Vector3& operator-= (const Vector2& vec);
		// Vector3 *= Vector2
		// _zは無視
		// =============================================================
		Vector3& operator*= (const Vector2& vec);
		// Vector3 /= Vector2
		// _zは無視
		// =============================================================
		Vector3& operator/= (const Vector2& vec);
		// Vector3 += Vector4
		// 引数の_wは無視
		// =============================================================
		Vector3& operator+= (const Vector4& vec);
		// Vector3 -= Vector4
		// 引数の_wは無視
		// =============================================================
		Vector3& operator-= (const Vector4& vec);
		// Vector3 *= Vector4
		// 引数の_wは無視
		// =============================================================
		Vector3& operator*= (const Vector4& vec);
		// Vector3 /= Vector4
		// 引数の_wは無視
		// =============================================================
		Vector3& operator/= (const Vector4& vec);
		// Vector3 += KOVECTOR
		// 引数の_wFloatは無視
		// =============================================================
		Vector3& operator+= (const VectorEx& vec);
		// Vector3 -= KOVECTOR
		// 引数の_wFloatは無視
		// =============================================================
		Vector3& operator-= (const VectorEx& vec);
		// Vector3 *= KOVECTOR
		// 引数の_wFloatは無視
		// =============================================================
		Vector3& operator*= (const VectorEx& vec);
		// Vector3 /= KOVECTOR
		// 引数の_wFloatは無視
		// =============================================================
		Vector3& operator/= (const VectorEx& vec);

		// if(Vector3 == Vector3)
		// =============================================================
		bool operator== (const Vector3& vec)const;
		// if(Vector3 != Vector3)
		// =============================================================
		bool operator!= (const Vector3& vec)const;
		// if(Vector3 == Vector2)
		// _zは比較しない
		// =============================================================
		bool operator== (const Vector2& vec)const;
		// if(Vector3 != Vector2)
		// _zは比較しない
		// =============================================================
		bool operator!= (const Vector2& vec)const;
		// if(Vector3 == Vector4)
		// 引数の_wは比較しない
		// =============================================================
		bool operator== (const Vector4& vec)const;
		// if(Vector3 != Vector4)
		// 引数の_wは比較しない
		// =============================================================
		bool operator!= (const Vector4& vec)const;
		// if(Vector3 == KOVECTOR)
		// 引数の_wFloatは比較しない
		// =============================================================
		bool operator== (const VectorEx& vec)const;
		// if(Vector3 != KOVECTOR)
		// 引数の_wFloatは比較しない
		// =============================================================
		bool operator!= (const VectorEx& vec)const;

		// Vector3 = float
		// _zは0
		// =============================================================
		Vector3& operator= (const float& t);
		// Vector3 = Vector2
		// _zは0
		// =============================================================
		Vector3& operator= (const Vector2& vec);
		// Vector3 = Vector4
		// =============================================================
		Vector3& operator= (const Vector4& vec);
		// Vector3 = KOVECTOR
		// =============================================================
		Vector3& operator= (const VectorEx& vec);

		// Vector3の値全て0.0fを生成
		// =============================================================
		static const Vector3 zero;
		// Vector3の値全て1.0fを生成
		// =============================================================
		static const Vector3 one;
		// _y =  1.0fのVector3を生成
		// =============================================================
		static const Vector3 up;
		// _y = -1.0fのVector3を生成
		// =============================================================
		static const Vector3 down;
		// _x =  1.0fのVector3を生成
		// =============================================================
		static const Vector3 right;
		// _x = -1.0fのVector3を生成
		// =============================================================
		static const Vector3 left;
		// _z =  1.0fのVector3を生成(左手座標系)
		// =============================================================
		static const Vector3 frontLH;
		// _z = -1.0fのVector3を生成(左手座標系)
		// =============================================================
		static const Vector3 backLH;
		// _z = -1.0fのVector3を生成(右手座標系)
		// =============================================================
		static const Vector3 frontRH;
		// _z =  1.0fのVector3を生成(右手座標系)
		// =============================================================
		static const Vector3 backRH;
	};
}

#endif // VECTOR3_H_

/************************************
* End of file
************************************/