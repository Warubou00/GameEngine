
/***********************************************************************************
 * @file    vector2.h
 * @brief   独自のVector2
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
#ifndef VECTOR2_H_
#define VECTOR2_H_

// =============================================================
// インクルード
// =============================================================
#include <DirectXMath.h>

namespace Engine
{
	// =============================================================
	// 前方宣言
	// =============================================================
	struct Vector3;
	struct Vector4;
	struct VectorEx;

	// =============================================================
	// Vector2
	// ============================================================= 
	struct Vector2
	{
		union {
			float vec[2];
			struct {
				float x, y;
			};
		};

		// =============================================================
		// コンストラクタ
		// =============================================================
		Vector2() = default;
		Vector2(const Vector3& v);
		Vector2(const Vector4& v);
		Vector2(const VectorEx& v);
		Vector2(float X, float Y) :x(X), y(Y) {}
		Vector2(float XY) : x(XY), y(XY) {}

		// =============================================================
		// DirectX::XM系との暗黙的変換
		// =============================================================
		operator DirectX::XMVECTOR() const {
			return DirectX::XMVectorSet(x, y, 0.0f, 0.0f);
		}

		Vector2& operator= (const DirectX::XMVECTOR& vec)
		{
			x = vec.m128_f32[0];
			y = vec.m128_f32[1];
			return *this;
		}

		// =============================================================
		// 演算子のオーバーロード
		// ============================================================= 
		// +Vector2
		// =============================================================
		Vector2 operator+ (void)const;
		// -Vector2
		// =============================================================
		Vector2 operator- (void)const;

		// Vector2 + Vector2
		// =============================================================
		Vector2 operator+ (const Vector2& vec)const;
		// Vector2 - Vector2
		// =============================================================
		Vector2 operator- (const Vector2& vec)const;
		// Vector2 * Vector2
		// =============================================================
		Vector2 operator* (const Vector2& vec)const;
		// Vector2 / Vector2
		// =============================================================
		Vector2 operator/ (const Vector2& vec)const;
		// Vector2 + float
		// =============================================================
		Vector2 operator+ (const float& t)const;
		// Vector2 - float
		// =============================================================
		Vector2 operator- (const float& t)const;
		// Vector2 * float
		// =============================================================
		Vector2 operator* (const float& t)const;
		// Vector2 / float
		// =============================================================
		Vector2 operator/ (const float& t)const;
		// Vector2 + Vector3
		// =============================================================
		Vector2 operator+ (const Vector3& vec)const;
		// Vector2 - Vector3
		// =============================================================
		Vector2 operator- (const Vector3& vec)const;
		// Vector2 * Vector3
		// =============================================================
		Vector2 operator* (const Vector3& vec)const;
		// Vector2 / Vector3
		// =============================================================
		Vector2 operator/ (const Vector3& vec)const;
		// Vector2 + Vector4
		// =============================================================
		Vector2 operator+ (const Vector4& vec)const;
		// Vector2 - Vector4
		// =============================================================
		Vector2 operator- (const Vector4& vec)const;
		// Vector2 * Vector4
		// =============================================================
		Vector2 operator* (const Vector4& vec)const;
		// Vector2 / Vector4
		// =============================================================
		Vector2 operator/ (const Vector4& vec)const;
		// Vector2 + KOVECTOR
		// =============================================================
		Vector2 operator+ (const VectorEx& vec)const;
		// Vector2 - KOVECTOR
		// =============================================================
		Vector2 operator- (const VectorEx& vec)const;
		// Vector2 * KOVECTOR
		// =============================================================
		Vector2 operator* (const VectorEx& vec)const;
		// Vector2 / KOVECTOR
		// =============================================================
		Vector2 operator/ (const VectorEx& vec)const;

		// Vector2 += Vector2
		// =============================================================
		Vector2& operator+= (const Vector2& vec);
		// Vector2 -= Vector2
		// =============================================================
		Vector2& operator-= (const Vector2& vec);
		// Vector2 *= Vector2
		// =============================================================
		Vector2& operator*= (const Vector2& vec);
		// Vector2 /= Vector2
		// =============================================================
		Vector2& operator/= (const Vector2& vec);
		// Vector2 += float
		// =============================================================
		Vector2& operator+= (const float& t);
		// Vector2 -= float
		// =============================================================
		Vector2& operator-= (const float& t);
		// Vector2 *= float
		// =============================================================
		Vector2& operator*= (const float& t);
		// Vector2 /= float
		// =============================================================
		Vector2& operator/= (const float& t);
		// Vector2 += Vector3
		// 引数の_zは無視
		// =============================================================
		Vector2& operator+= (const Vector3& vec);
		// Vector2 -= Vector3
		// 引数の_zは無視
		// =============================================================
		Vector2& operator-= (const Vector3& vec);
		// Vector2 *= Vector3
		// 引数の_zは無視
		// =============================================================
		Vector2& operator*= (const Vector3& vec);
		// Vector2 /= Vector3
		// 引数の_zは無視
		// =============================================================
		Vector2& operator/= (const Vector3& vec);
		// Vector2 += Vector4
		// 引数の_z、_wは無視
		// =============================================================
		Vector2& operator+= (const Vector4& vec);
		// Vector2 -= Vector4
		// 引数の_z、_wは無視
		// =============================================================
		Vector2& operator-= (const Vector4& vec);
		// Vector2 *= Vector4
		// 引数の_z、_wは無視
		// =============================================================
		Vector2& operator*= (const Vector4& vec);
		// Vector2 /= Vector4
		// 引数の_z、_wは無視
		// =============================================================
		Vector2& operator/= (const Vector4& vec);
		// Vector2 += KOVECTOR
		// 引数の_zFloat、_wFloatは無視
		// =============================================================
		Vector2& operator+= (const VectorEx& vec);
		// Vector2 -= KOVECTOR
		// 引数の_zFloat、_wFloatは無視
		// =============================================================
		Vector2& operator-= (const VectorEx& vec);
		// Vector2 *= KOVECTOR
		// 引数の_zFloat、_wFloatは無視
		// =============================================================
		Vector2& operator*= (const VectorEx& vec);
		// Vector2 /= KOVECTOR
		// 引数の_zFloat、_wFloatは無視
		// =============================================================
		Vector2& operator/= (const VectorEx& vec);


		// if(Vector2 == Vector2)
		// =============================================================
		bool operator== (const Vector2& vec)const;
		// if(Vector2 != Vector2)
		// =============================================================
		bool operator!= (const Vector2& vec)const;
		// if(Vector2 == Vector3)
		// =============================================================
		bool operator== (const Vector3& vec)const;
		// if(Vector2 != Vector3)
		// =============================================================
		bool operator!= (const Vector3& vec)const;
		// if(Vector2 == Vector4)
		// =============================================================
		bool operator== (const Vector4& vec)const;
		// if(Vector2 != Vector4)
		// =============================================================
		bool operator!= (const Vector4& vec)const;
		// if(Vector2 == KOVECTOR)
		// =============================================================
		bool operator== (const VectorEx& vec)const;
		// if(Vector2 != KOVECTOR)
		// =============================================================
		bool operator!= (const VectorEx& vec)const;

		// Vector2 = Vector3
		// =============================================================
		Vector2& operator= (const Vector3& vec);
		// Vector2 = Vector4
		// =============================================================
		Vector2& operator= (const Vector4& vec);
		// Vector2 = KOVECTOR
		// =============================================================
		Vector2& operator= (const VectorEx& vec);

		// Vector2の値全て0.0fを生成
		// =============================================================
		static const Vector2 zero;
		// Vector2の値全て1.0fを生成
		// =============================================================
		static const Vector2 one;
		// _y =  1.0fのVector2を生成
		// =============================================================
		static const Vector2 up;
		// _y = -1.0fのVector2を生成
		// =============================================================
		static const Vector2 down;
		// _x =  1.0fのVector2を生成
		// =============================================================
		static const Vector2 right;
		// _x = -1.0fのVector2を生成
		// =============================================================
		static const Vector2 left;
	};
}

#endif // VECTOR2_H_

/************************************
* End of file
************************************/