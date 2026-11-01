
/***********************************************************************************
 * @file    MyVector.h
 * @brief   floatを4つを扱うクラス
 * @details
 *
 * @author  Keigo Onari
 * @date    2026/09/23
 * @version v_1.0.0
 *
 * @copyright
 * Copyright (c) 2024 Keigo Onari. All rights reserved.
 *
 * @note
 ************************************************************************************/
#pragma once
#ifndef CORE_MY_VECTOR_H_
#define CORE_MY_VECTOR_H_

// =============================================================
// インクルード
// =============================================================
#include <Windows.h>
#include <DirectXMath.h>
#include <stdint.h>

#include "../../common.h"

#define KOAssert(item, text, windowName)\
{\
    if(!item)\
    {\
        (void)MessageBox(NULL,text, windowName, MB_OK);\
    }\
}


namespace Core
{
	// =============================================================
	// 前方宣言
	// ============================================================= 
	struct Vector2;
	struct Vector3;
	struct Vector4;

	// =============================================================
	// 疑似的DirectX::XMVECTOR
	// ============================================================= 
	struct CORE_API_CLASS MyVECTOR
	{
		union {
			float _vec[4];

			uint32_t _vecInt[4];

			struct
			{
				float _xFloat, _yFloat, _zFloat, _wFloat;
			};

			struct
			{
				int _xInt, _yInt, _zInt, _wInt;
			};

			struct
			{
				char _xChar[4];
				char _yChar[4];
				char _zChar[4];
				char _wChar[4];
			};
		};

		// コンストラクタ
		// =============================================================
		MyVECTOR() = default;
		MyVECTOR(const Vector2& v);
		MyVECTOR(const Vector2& v1, const Vector2& v2);
		MyVECTOR(const Vector2& v, const float& t1, const float& t2);
		MyVECTOR(const Vector3& v);
		MyVECTOR(const Vector3& v, const float& t);
		MyVECTOR(const Vector4& v);
		MyVECTOR(float X, float Y, float Z, float W) :_xFloat(X), _yFloat(Y), _zFloat(Z), _wFloat(W) {}
		MyVECTOR(int X, int Y, int Z, int W) :_xInt(X), _yInt(Y), _zInt(Z), _wInt(W) {}
		MyVECTOR(float XYZWf) :_xFloat(XYZWf), _yFloat(XYZWf), _zFloat(XYZWf), _wFloat(XYZWf) {}
		MyVECTOR(int XYZWi) :_xInt(XYZWi), _yInt(XYZWi), _zInt(XYZWi), _wInt(XYZWi) {}

		// =============================================================
		// DirectX::XM系との暗黙的変換
		// =============================================================
		operator DirectX::XMVECTOR() const {
			return DirectX::XMVectorSet(_xFloat, _yFloat, _zFloat, _wFloat);
		}

		MyVECTOR& operator= (const DirectX::XMVECTOR& vec)
		{
			_xFloat = vec.m128_f32[0];
			_yFloat = vec.m128_f32[1];
			_zFloat = vec.m128_f32[2];
			_wFloat = vec.m128_f32[3];
			return *this;
		}

		// =============================================================
		// 演算子のオーバーロード
		// =============================================================
		// +KOVECTOR
		// =============================================================
		MyVECTOR operator+ (void)const;
		// -KOVECTOR
		// =============================================================
		MyVECTOR operator- (void)const;

		// KOVECTOR + KOVECTOR
		// =============================================================
		MyVECTOR operator+ (const MyVECTOR& vec)const;
		// KOVECTOR - KOVECTOR
		// =============================================================
		MyVECTOR operator- (const MyVECTOR& vec)const;
		// KOVECTOR * KOVECTOR
		// =============================================================
		MyVECTOR operator* (const MyVECTOR& vec)const;
		// KOVECTOR / KOVECTOR
		// =============================================================
		MyVECTOR operator/ (const MyVECTOR& vec)const;
		// KOVECTOR + float
		// =============================================================
		MyVECTOR operator+ (const float& t)const;
		// KOVECTOR - float
		// =============================================================
		MyVECTOR operator- (const float& t)const;
		// KOVECTOR * float
		// =============================================================
		MyVECTOR operator* (const float& t)const;
		// KOVECTOR / float
		// =============================================================
		MyVECTOR operator/ (const float& t)const;
		// KOVECTOR + Vector2
		// _zFloat,_wFloatは無視
		// =============================================================
		MyVECTOR operator+ (const Vector2& vec)const;
		// KOVECTOR - Vector2
		// _zFloat,_wFloatは無視
		// =============================================================
		MyVECTOR operator- (const Vector2& vec)const;
		// KOVECTOR * Vector2
		// _zFloat,_wFloatは無視
		// =============================================================
		MyVECTOR operator* (const Vector2& vec)const;
		// KOVECTOR / Vector2
		// _zFloat,_wFloatは無視
		// =============================================================
		MyVECTOR operator/ (const Vector2& vec)const;
		// KOVECTOR + Vector3
		// =============================================================
		MyVECTOR operator+ (const Vector3& vec)const;
		// KOVECTOR - Vector3
		// _wFloatは無視
		// =============================================================
		MyVECTOR operator- (const Vector3& vec)const;
		// KOVECTOR * Vector3
		// _wFloatは無視
		// =============================================================
		MyVECTOR operator* (const Vector3& vec)const;
		// KOVECTOR / Vector3
		// _wFloatは無視
		// =============================================================
		MyVECTOR operator/ (const Vector3& vec)const;
		// KOVECTOR + Vector4
		// =============================================================
		MyVECTOR operator+ (const Vector4& vec)const;
		// KOVECTOR - Vector4
		// =============================================================
		MyVECTOR operator- (const Vector4& vec)const;
		// KOVECTOR * Vector4
		// =============================================================
		MyVECTOR operator* (const Vector4& vec)const;
		// KOVECTOR / Vector4
		// =============================================================
		MyVECTOR operator/ (const Vector4& vec)const;

		// KOVECTOR += KOVECTOR
		// =============================================================
		MyVECTOR& operator+= (const MyVECTOR& vec);
		// KOVECTOR -= KOVECTOR
		// =============================================================
		MyVECTOR& operator-= (const MyVECTOR& vec);
		// KOVECTOR *= KOVECTOR
		// =============================================================
		MyVECTOR& operator*= (const MyVECTOR& vec);
		// KOVECTOR /= KOVECTOR
		// =============================================================
		MyVECTOR& operator/= (const MyVECTOR& vec);
		// KOVECTOR += Vector4
		// =============================================================
		MyVECTOR& operator+= (const Vector4& vec);
		// KOVECTOR -= Vector4
		// =============================================================
		MyVECTOR& operator-= (const Vector4& vec);
		// KOVECTOR *= Vector4
		// =============================================================
		MyVECTOR& operator*= (const Vector4& vec);
		// KOVECTOR /= Vector4
		// =============================================================
		MyVECTOR& operator/= (const Vector4& vec);
		// KOVECTOR += float
		// =============================================================
		MyVECTOR& operator+= (const float& t);
		// KOVECTOR -= float
		// =============================================================
		MyVECTOR& operator-= (const float& t);
		// KOVECTOR *= float
		// =============================================================
		MyVECTOR& operator*= (const float& t);
		// KOVECTOR /= float
		// =============================================================
		MyVECTOR& operator/= (const float& t);

		// if(KOVECTOR == KOVECTOR)
		// =============================================================
		bool operator== (const MyVECTOR& vec);
		// if(KOVECTOR != KOVECTOR)
		// =============================================================
		bool operator!= (const MyVECTOR& vec);
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
		MyVECTOR& operator= (const float& t);
		// KOVECTOR = Vector2
		// _zFloat,_wFloatは0
		// =============================================================
		MyVECTOR& operator= (const Vector2& vec);
		// KOVECTOR = Vector3
		// _wFloatは0
		// =============================================================
		MyVECTOR& operator= (const Vector3& vec);
		// KOVECTOR = Vector4
		// =============================================================
		MyVECTOR& operator= (const Vector4& vec);

		// KOVECTORの値全て 0.0fを生成
		// =============================================================
		static const MyVECTOR zero;
		// KOVECTORの値全て 1.0fを生成
		// =============================================================
		static const MyVECTOR one;
		// KOVECTORの値全て-1.0fを生成
		// =============================================================
		static const MyVECTOR allminus;
		// _yFloat =  1.0fのKOVECTORを生成
		// =============================================================
		static const MyVECTOR up;
		// _yFloat = -1.0fのKOVECTORを生成
		// =============================================================
		static const MyVECTOR down;
		// _xFloat =  1.0fのKOVECTORを生成
		// =============================================================
		static const MyVECTOR right;
		// _xFloat = -1.0fのKOVECTORを生成
		// =============================================================
		static const MyVECTOR left;
		// _zFloat =  1.0fのKOVECTORを生成(左手座標系)
		// =============================================================
		static const MyVECTOR frontLH;
		// _zFloat = -1.0fのKOVECTORを生成(左手座標系)
		// =============================================================
		static const MyVECTOR backLH;
		// _zFloat = -1.0fのKOVECTORを生成(右手座標系)
		// =============================================================
		static const MyVECTOR frontRH;
		// _zFloat =  1.0fのKOVECTORを生成(右手座標系)
		// =============================================================
		static const MyVECTOR backRH;
	};
}

#endif // CORE_MY_VECTOR_H_


/************************************
* End of file
************************************/