
/***********************************************************************************
 * @file    vector2.h
 * @brief   独自のVector2
 * @details 
 *
 * @author  Keigo Onari
 * @date    2026/09/23
 * @version v_1.0.0
 *
 * @copyright
 * Copyright (c) 2024 Keigo Onari. All rights reserved.
 *
 * @note 名前空間、インクルードガードを変更
 ************************************************************************************/

#pragma once
#ifndef CORE_VECTOR2_H_
#define CORE_VECTOR2_H_

// =============================================================
// インクルード
// =============================================================
#include <DirectXMath.h>

#include "../../common.h"

namespace Core
{

	// =============================================================
	// 前方宣言
	// =============================================================
	struct Vector3;
	struct Vector4;
#if 0
	struct KOVECTOR;
#endif

	// =============================================================
	// Vector2
	// ============================================================= 
	struct CORE_API_CLASS Vector2
	{
		union {
			float _vec[2];
			struct {
				float _x, _y;
			};
		};

		// =============================================================
		// コンストラクタ
		// =============================================================
		Vector2() = default;
		Vector2(const Vector3& v);
		Vector2(const Vector4& v);
		Vector2(float X, float Y) :_x(X), _y(Y) {}
		Vector2(float XY) : _x(XY), _y(XY) {}
#if 0
		Vector2(const KOVECTOR& v);
#endif

		// =============================================================
		// DirectX::XM系との暗黙的変換
		// =============================================================
		operator DirectX::XMVECTOR() const {
			return DirectX::XMVectorSet(_x, _y, 0.0f, 0.0f);
		}

		Vector2& operator= (const DirectX::XMVECTOR& vec)
		{
			_x = vec.m128_f32[0];
			_y = vec.m128_f32[1];
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

#if 0
		// Vector2 + KOVECTOR
		// =============================================================
		Vector2 operator+ (const KOVECTOR& vec)const;
		// Vector2 - KOVECTOR
		// =============================================================
		Vector2 operator- (const KOVECTOR& vec)const;
		// Vector2 * KOVECTOR
		// =============================================================
		Vector2 operator* (const KOVECTOR& vec)const;
		// Vector2 / KOVECTOR
		// =============================================================
		Vector2 operator/ (const KOVECTOR& vec)const;
#endif

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

#if 0
		// Vector2 += KOVECTOR
		// 引数の_zFloat、_wFloatは無視
		// =============================================================
		Vector2& operator+= (const KOVECTOR& vec);
		// Vector2 -= KOVECTOR
		// 引数の_zFloat、_wFloatは無視
		// =============================================================
		Vector2& operator-= (const KOVECTOR& vec);
		// Vector2 *= KOVECTOR
		// 引数の_zFloat、_wFloatは無視
		// =============================================================
		Vector2& operator*= (const KOVECTOR& vec);
		// Vector2 /= KOVECTOR
		// 引数の_zFloat、_wFloatは無視
		// =============================================================
		Vector2& operator/= (const KOVECTOR& vec);
#endif

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

#if 0
		// if(Vector2 == KOVECTOR)
		// =============================================================
		bool operator== (const KOVECTOR& vec)const;
		// if(Vector2 != KOVECTOR)
		// =============================================================
		bool operator!= (const KOVECTOR& vec)const;
#endif

		// Vector2 = Vector3
		// =============================================================
		Vector2& operator= (const Vector3& vec);
		// Vector2 = Vector4
		// =============================================================
		Vector2& operator= (const Vector4& vec);

#if 0
		// Vector2 = KOVECTOR
		// =============================================================
		Vector2& operator= (const KOVECTOR& vec);
#endif

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

	typedef Vector2 float2;

}

#endif // VECTOR2_H_


/************************************
* End of file
************************************/