
/***********************************************************************************
 * @file    matrix.h
 * @brief   マトリクスを扱うクラス
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
#ifndef MATRIX_H_
#define MATRIX_H_

// =============================================================
// インクルード
// =============================================================
#include "vector3.h"
#include "vector4.h"
#include "vectorEx.h"
#include <DirectXMath.h>

namespace Engine
{
	// =============================================================
	// Matrix
	// =============================================================
	struct Matrix
	{
		union {
			DirectX::XMFLOAT4X4 mat;
			float    _mat[4][4];
			Vector4  _vec[4];
			VectorEx _koVec[4];

			struct {
				float _m11, _m12, _m13, _m14;
				float _m21, _m22, _m23, _m24;
				float _m31, _m32, _m33, _m34;
				float _m41, _m42, _m43, _m44;
			};
		};

		// コンストラクタ
		// =============================================================
		Matrix() :Matrix(0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f, 0.0f) {}
		Matrix(float m11, float m12, float m13, float m14,
			float m21, float m22, float m23, float m24,
			float m31, float m32, float m33, float m34,
			float m41, float m42, float m43, float m44)
			: _m11(m11), _m12(m12), _m13(m13), _m14(m14),
			_m21(m21), _m22(m22), _m23(m23), _m24(m24),
			_m31(m31), _m32(m32), _m33(m33), _m34(m34),
			_m41(m41), _m42(m42), _m43(m43), _m44(m44) {}

		// =============================================================
		// DirectX::XM系との暗黙的変換
		// =============================================================
		operator DirectX::XMMATRIX() const {
			return DirectX::XMLoadFloat4x4(&mat);
		}

		// =============================================================
		// 演算子のオーバーロード
		// =============================================================
		// +Matrix
		// =============================================================
		Matrix operator+ (void)const;
		// -Matrix
		// =============================================================
		Matrix operator- (void)const;

		// Matrix + Matrix
		// =============================================================
		Matrix operator+ (const Matrix& mat)const;
		// Matrix - Matrix
		// =============================================================
		Matrix operator- (const Matrix& mat)const;
		// Matrix * Matrix
		// =============================================================
		Matrix operator* (const Matrix& mat)const;
		// Matrix / Matrix
		// =============================================================
		Matrix operator/ (const Matrix& mat)const;

		// Matirx += Matrix
		// =============================================================
		Matrix& operator+= (const Matrix& mat);
		// Matrix -= Matrix
		// =============================================================
		Matrix& operator-= (const Matrix& mat);
		// Matirx *= Matrix
		// =============================================================
		Matrix& operator*= (const Matrix& mat);
		// Matrix /= Matrix
		// =============================================================
		Matrix& operator/= (const Matrix& mat);

		// if(Matrix == Matrix)
		// =============================================================
		bool operator== (const Matrix& mat)const;
		// if(Matrix != Matrix)
		// =============================================================
		bool operator!= (const Matrix& mat)const;

		// Matrixの全ての値0.0fを生成
		// =============================================================
		static const Matrix zero;
		// Matrixの全ての値1.0fを生成
		// =============================================================
		static const Matrix one;
		// Matrixの全ての値-1.0fを生成
		// =============================================================
		static const Matrix allminus;
		// Matrixの単位行列を生成
		// =============================================================
		static const Matrix identity;
	};
}

#endif // MATRIX_H_

/************************************
* End of file
************************************/