
/***********************************************************************************
 * @file    vectorEx.h
 * @brief   floatを4つを扱うクラス
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
#ifndef VECTOR_EX_H_
#define VECTOR_EX_H_

// =============================================================
// インクルード
// =============================================================
#include <Windows.h>
#include <DirectXMath.h>

#define VectorExAssert(item, text, windowName)\
{\
    if(!item)\
    {\
        (void)MessageBox(NULL,text, windowName, MB_OK);\
    }\
}
namespace Engine
{

	typedef unsigned int uint32_t_ex;

	// =============================================================
	// 前方宣言
	// ============================================================= 
	struct Vector2;
	struct Vector3;
	struct Vector4;

	// =============================================================
	// 疑似的DirectX::XMVECTOR
	// ============================================================= 
	struct VectorEx
	{
		union {
			float vec[4];

			uint32_t_ex vecInt[4];

			struct
			{
				float xFloat, yFloat, zFloat, wFloat;
			};

			struct
			{
				int xInt, yInt, zInt, wInt;
			};

			struct
			{
				char xChar[4];
				char yChar[4];
				char zChar[4];
				char wChar[4];
			};
		};

		// コンストラクタ
		// =============================================================
		VectorEx() = default;
		VectorEx(const Vector2& v);
		VectorEx(const Vector2& v1, const Vector2& v2);
		VectorEx(const Vector2& v, const float& t1, const float& t2);
		VectorEx(const Vector3& v);
		VectorEx(const Vector3& v, const float& t);
		VectorEx(const Vector4& v);
		VectorEx(float X, float Y, float Z, float W) :xFloat(X), yFloat(Y), zFloat(Z), wFloat(W) {}
		VectorEx(int X, int Y, int Z, int W) :xInt(X), yInt(Y), zInt(Z), wInt(W) {}
		VectorEx(float XYZWf) :xFloat(XYZWf), yFloat(XYZWf), zFloat(XYZWf), wFloat(XYZWf) {}
		VectorEx(int XYZWi) :xInt(XYZWi), yInt(XYZWi), zInt(XYZWi), wInt(XYZWi) {}

		// =============================================================
		// DirectX::XM系との暗黙的変換
		// =============================================================
		operator DirectX::XMVECTOR() const {
			return DirectX::XMVectorSet(xFloat, yFloat, zFloat, wFloat);
		}

		VectorEx& operator= (const DirectX::XMVECTOR& vec)
		{
			xFloat = vec.m128_f32[0];
			yFloat = vec.m128_f32[1];
			zFloat = vec.m128_f32[2];
			wFloat = vec.m128_f32[3];
			return *this;
		}

		// =============================================================
		// 演算子のオーバーロード
		// =============================================================
		// +KOVECTOR
		// =============================================================
		VectorEx operator+ (void)const;
		// -KOVECTOR
		// =============================================================
		VectorEx operator- (void)const;

		// KOVECTOR + KOVECTOR
		// =============================================================
		VectorEx operator+ (const VectorEx& vec)const;
		// KOVECTOR - KOVECTOR
		// =============================================================
		VectorEx operator- (const VectorEx& vec)const;
		// KOVECTOR * KOVECTOR
		// =============================================================
		VectorEx operator* (const VectorEx& vec)const;
		// KOVECTOR / KOVECTOR
		// =============================================================
		VectorEx operator/ (const VectorEx& vec)const;
		// KOVECTOR + float
		// =============================================================
		VectorEx operator+ (const float& t)const;
		// KOVECTOR - float
		// =============================================================
		VectorEx operator- (const float& t)const;
		// KOVECTOR * float
		// =============================================================
		VectorEx operator* (const float& t)const;
		// KOVECTOR / float
		// =============================================================
		VectorEx operator/ (const float& t)const;
		// KOVECTOR + Vector2
		// _zFloat,_wFloatは無視
		// =============================================================
		VectorEx operator+ (const Vector2& vec)const;
		// KOVECTOR - Vector2
		// _zFloat,_wFloatは無視
		// =============================================================
		VectorEx operator- (const Vector2& vec)const;
		// KOVECTOR * Vector2
		// _zFloat,_wFloatは無視
		// =============================================================
		VectorEx operator* (const Vector2& vec)const;
		// KOVECTOR / Vector2
		// _zFloat,_wFloatは無視
		// =============================================================
		VectorEx operator/ (const Vector2& vec)const;
		// KOVECTOR + Vector3
		// =============================================================
		VectorEx operator+ (const Vector3& vec)const;
		// KOVECTOR - Vector3
		// _wFloatは無視
		// =============================================================
		VectorEx operator- (const Vector3& vec)const;
		// KOVECTOR * Vector3
		// _wFloatは無視
		// =============================================================
		VectorEx operator* (const Vector3& vec)const;
		// KOVECTOR / Vector3
		// _wFloatは無視
		// =============================================================
		VectorEx operator/ (const Vector3& vec)const;
		// KOVECTOR + Vector4
		// =============================================================
		VectorEx operator+ (const Vector4& vec)const;
		// KOVECTOR - Vector4
		// =============================================================
		VectorEx operator- (const Vector4& vec)const;
		// KOVECTOR * Vector4
		// =============================================================
		VectorEx operator* (const Vector4& vec)const;
		// KOVECTOR / Vector4
		// =============================================================
		VectorEx operator/ (const Vector4& vec)const;

		// KOVECTOR += KOVECTOR
		// =============================================================
		VectorEx& operator+= (const VectorEx& vec);
		// KOVECTOR -= KOVECTOR
		// =============================================================
		VectorEx& operator-= (const VectorEx& vec);
		// KOVECTOR *= KOVECTOR
		// =============================================================
		VectorEx& operator*= (const VectorEx& vec);
		// KOVECTOR /= KOVECTOR
		// =============================================================
		VectorEx& operator/= (const VectorEx& vec);
		// KOVECTOR += Vector4
		// =============================================================
		VectorEx& operator+= (const Vector4& vec);
		// KOVECTOR -= Vector4
		// =============================================================
		VectorEx& operator-= (const Vector4& vec);
		// KOVECTOR *= Vector4
		// =============================================================
		VectorEx& operator*= (const Vector4& vec);
		// KOVECTOR /= Vector4
		// =============================================================
		VectorEx& operator/= (const Vector4& vec);
		// KOVECTOR += float
		// =============================================================
		VectorEx& operator+= (const float& t);
		// KOVECTOR -= float
		// =============================================================
		VectorEx& operator-= (const float& t);
		// KOVECTOR *= float
		// =============================================================
		VectorEx& operator*= (const float& t);
		// KOVECTOR /= float
		// =============================================================
		VectorEx& operator/= (const float& t);

		// if(KOVECTOR == KOVECTOR)
		// =============================================================
		bool operator== (const VectorEx& vec);
		// if(KOVECTOR != KOVECTOR)
		// =============================================================
		bool operator!= (const VectorEx& vec);
		// if(KOVECTOR == Vector2)
		// _zFloat,_wFloatは比較しない
		// =============================================================
		bool operator== (const Vector2& vec);
		// if(KOVECTOR != Vector2)
		// _zFloat,_wFloatは比較しない
		// =============================================================
		bool operator!= (const Vector2& vec);
		// if(KOVECTOR == Vector3)
		// _wFloatは比較しない
		// =============================================================
		bool operator== (const Vector3& vec);
		// if(KOVECTOR != Vector3)
		// _wFloatは比較しない
		// =============================================================
		bool operator!= (const Vector3& vec);
		// if(KOVECTOR == Vecror4)
		// =============================================================
		bool operator== (const Vector4& vec);
		// if(KOVECTOR != Vecror4)
		// =============================================================
		bool operator!= (const Vector4& vec);

		// KOVECTOR = t
		// =============================================================
		VectorEx& operator= (const float& t);
		// KOVECTOR = Vector2
		// _zFloat,_wFloatは0
		// =============================================================
		VectorEx& operator= (const Vector2& vec);
		// KOVECTOR = Vector3
		// _wFloatは0
		// =============================================================
		VectorEx& operator= (const Vector3& vec);
		// KOVECTOR = Vector4
		// =============================================================
		VectorEx& operator= (const Vector4& vec);

		// KOVECTORの値全て 0.0fを生成
		// =============================================================
		static const VectorEx zero;
		// KOVECTORの値全て 1.0fを生成
		// =============================================================
		static const VectorEx one;
		// KOVECTORの値全て-1.0fを生成
		// =============================================================
		static const VectorEx allminus;
		// _yFloat =  1.0fのKOVECTORを生成
		// =============================================================
		static const VectorEx up;
		// _yFloat = -1.0fのKOVECTORを生成
		// =============================================================
		static const VectorEx down;
		// _xFloat =  1.0fのKOVECTORを生成
		// =============================================================
		static const VectorEx right;
		// _xFloat = -1.0fのKOVECTORを生成
		// =============================================================
		static const VectorEx left;
		// _zFloat =  1.0fのKOVECTORを生成(左手座標系)
		// =============================================================
		static const VectorEx frontLH;
		// _zFloat = -1.0fのKOVECTORを生成(左手座標系)
		// =============================================================
		static const VectorEx backLH;
		// _zFloat = -1.0fのKOVECTORを生成(右手座標系)
		// =============================================================
		static const VectorEx frontRH;
		// _zFloat =  1.0fのKOVECTORを生成(右手座標系)
		// =============================================================
		static const VectorEx backRH;
	};
}

#endif // VECTOR_EX_H_

/************************************
* End of file
************************************/