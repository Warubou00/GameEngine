/************************************************
* koMatrix.cpp
* koMatrix
*
* 最終更新日:2026/1/13
* 作成者：Keigo Onari
*************************************************/
#include "matrix.h"
#include <math.h>

namespace Engine
{
	// =============================================================
	// Matrix
	// =============================================================
	
	// 静的変数の初期化
	// =============================================================
	// Matrixの全ての値0.0fを生成
	// =============================================================
	const Matrix Matrix::zero(
		0.0f, 0.0f, 0.0f, 0.0f,
		0.0f, 0.0f, 0.0f, 0.0f,
		0.0f, 0.0f, 0.0f, 0.0f,
		0.0f, 0.0f, 0.0f, 0.0f);
	// Matrixの全ての値1.0fを生成
	// =============================================================
	const Matrix Matrix::one(
		1.0f, 1.0f, 1.0f, 1.0f,
		1.0f, 1.0f, 1.0f, 1.0f,
		1.0f, 1.0f, 1.0f, 1.0f,
		1.0f, 1.0f, 1.0f, 1.0f);
	// Matrixの全ての値-1.0fを生成
	// =============================================================
	const Matrix Matrix::allminus(
		-1.0f, -1.0f, -1.0f, -1.0f,
		-1.0f, -1.0f, -1.0f, -1.0f,
		-1.0f, -1.0f, -1.0f, -1.0f,
		-1.0f, -1.0f, -1.0f, -1.0f
	);
	// Matrixの単位行列を生成
	// =============================================================
	const Matrix Matrix::identity(
		1.0f, 0.0f, 0.0f, 0.0f,
		0.0f, 1.0f, 0.0f, 0.0f,
		0.0f, 0.0f, 1.0f, 0.0f,
		0.0f, 0.0f, 0.0f, 1.0f);


	// +Matrix
	// =============================================================
	Matrix Matrix::operator+ (void)const
	{
		return *this;
	}

	// -Matrix
	// =============================================================
	Matrix Matrix::operator- (void)const
	{
		return Matrix(
			-_m11, -_m12, -_m13, -_m14,
			-_m21, -_m22, -_m23, -_m24,
			-_m31, -_m32, -_m33, -_m34,
			-_m41, -_m42, -_m43, -_m44
			);
	}

	// Matrix + Matrix
	// =============================================================
	Matrix Matrix::operator+ (const Matrix& mat)const
	{
		return Matrix(
			_m11 + mat._m11, _m12 + mat._m12, _m13 + mat._m13, _m14 + mat._m14,
			_m21 + mat._m21, _m22 + mat._m22, _m23 + mat._m23, _m24 + mat._m24,
			_m31 + mat._m31, _m32 + mat._m32, _m33 + mat._m33, _m34 + mat._m34,
			_m41 + mat._m41, _m42 + mat._m42, _m43 + mat._m43, _m44 + mat._m44
		);
	}

	// Matrix - Matrix
	// =============================================================
	Matrix Matrix::operator- (const Matrix& mat)const
	{
		return Matrix(
			_m11 - mat._m11, _m12 - mat._m12, _m13 - mat._m13, _m14 - mat._m14,
			_m21 - mat._m21, _m22 - mat._m22, _m23 - mat._m23, _m24 - mat._m24,
			_m31 - mat._m31, _m32 - mat._m32, _m33 - mat._m33, _m34 - mat._m34,
			_m41 - mat._m41, _m42 - mat._m42, _m43 - mat._m43, _m44 - mat._m44
		);
	}

	// Matrix * Matrix
	// =============================================================
	Matrix Matrix::operator* (const Matrix& mat)const
	{
		return Matrix(
			_m11 * mat._m11, _m12 * mat._m12, _m13 * mat._m13, _m14 * mat._m14,
			_m21 * mat._m21, _m22 * mat._m22, _m23 * mat._m23, _m24 * mat._m24,
			_m31 * mat._m31, _m32 * mat._m32, _m33 * mat._m33, _m34 * mat._m34,
			_m41 * mat._m41, _m42 * mat._m42, _m43 * mat._m43, _m44 * mat._m44
		);
	}

	// Matrix / Matrix
	// =============================================================
	Matrix Matrix::operator/ (const Matrix& mat)const
	{
		return Matrix(
			_m11 / mat._m11, _m12 / mat._m12, _m13 / mat._m13, _m14 / mat._m14,
			_m21 / mat._m21, _m22 / mat._m22, _m23 / mat._m23, _m24 / mat._m24,
			_m31 / mat._m31, _m32 / mat._m32, _m33 / mat._m33, _m34 / mat._m34,
			_m41 / mat._m41, _m42 / mat._m42, _m43 / mat._m43, _m44 / mat._m44
		);
	}

	// Matrix += Matrix
	// =============================================================
	Matrix& Matrix::operator+= (const Matrix& mat)
	{
		for (int y = 0; y < 4; ++y)
		{
			for (int x = 0; x < 4; ++x)
			{
				_mat[y][x] += mat._mat[y][x];
			}
		}

		return *this;
	}

	// Matrix -= Matrix
	// =============================================================
	Matrix& Matrix::operator-= (const Matrix& mat)
	{
		for (int y = 0; y < 4; ++y)
		{
			for (int x = 0; x < 4; ++x)
			{
				_mat[y][x] -= mat._mat[y][x];
			}
		}

		return *this;
	}

	// Matrix *= Matrix
	// =============================================================
	Matrix& Matrix::operator*= (const Matrix& mat)
	{
		for (int y = 0; y < 4; ++y)
		{
			for (int x = 0; x < 4; ++x)
			{
				_mat[y][x] *= mat._mat[y][x];
			}
		}

		return *this;
	}

	// Matrix /= Matrix
	// =============================================================
	Matrix& Matrix::operator/= (const Matrix& mat)
	{
		for (int y = 0; y < 4; ++y)
		{
			for (int x = 0; x < 4; ++x)
			{
				_mat[y][x] /= mat._mat[y][x];
			}
		}

		return *this;
	}

	// if(Matrix == Matrix)
// =============================================================
	bool Matrix::operator== (const Matrix& mat)const
	{
		for (int y = 0; y < 4; ++y)
		{
			for (int x = 0; x < 4; ++x)
			{
				if (_mat[y][x] != mat._mat[y][x]) {
					return false;
				}
			}
		}

		return true;
	}

	// if(Matrix != Matrix)
	// =============================================================
	bool Matrix::operator!= (const Matrix& mat)const
	{
		for (int y = 0; y < 4; ++y)
		{
			for (int x = 0; x < 4; ++x)
			{
				if (_mat[y][x] == mat._mat[y][x]) {
					return false;
				}
			}
		}

		return true;
	}
}