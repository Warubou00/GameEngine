/************************************************
* MyMath.cpp
* 簡易的な数学関数提供
*
* 最終更新日:2023/12/26
* 作成者：Keigo Onari
*************************************************/

// =============================================================
// インクルード
// =============================================================
#include "MyMath.h"

#include <DirectXMath.h>

namespace Core
{
	namespace vector2
	{
		// Vector2からfloat型の長さの2乗を取得
		// =============================================================
		const float& LengthSq(const Vector2& vec)noexcept
		{
			return (vec._x * vec._x + vec._y * vec._y);
		}

		// Vector2の2点間の長さの2乗を取得
		// =============================================================
		const float& LengthSq(const Vector2& vec1, const Vector2& vec2)noexcept
		{
			Vector2 temp = Core::vector2::Subtract(vec2, vec1);
			return Core::vector2::LengthSq(temp);
		}

		// Vector2からfloat型の長さを取得
		// =============================================================
		const float& Length(const Vector2& vec)noexcept
		{
			return sqrtf(vec._x * vec._x + vec._y * vec._y);
		}

		// Vector2の2点間の距離を計算
		// =============================================================
		const float& Length(const Vector2& vec1, const Vector2& vec2)noexcept
		{
			Vector2 temp = Core::vector2::Subtract(vec2, vec1);
			return Core::vector2::Length(temp);
		}

		// Vector2から正規化したVector2型を取得
		// =============================================================
		const Vector2& Normalize(const Vector2& vec)noexcept
		{
			float length = Core::vector2::Length(vec);
			if (length != 0)
				return vec / length;
			return vec;
		}

		// Vector3から正規化したVector2型を取得
		// =============================================================
		const Vector2& Normalize(const Vector3& vec)noexcept
		{
			Vector2 v;
			v = vec;
			float length = Core::vector2::Length(v);
			if (length != 0)
				return v / length;
			return v;
		}

		// Vector4から正規化したVector2型を取得
		// =============================================================
		const Vector2& Normalize(const Vector4& vec)noexcept
		{
			Vector2 v;
			v = vec;
			float length = Core::vector2::Length(v);
			if (length != 0)
				return v / length;
			return v;
		}

		// Vector2からfloat型の内積を取得
		// =============================================================
		const float& Dot(const Vector2& vec1, const Vector2& vec2)noexcept
		{
			return vec1._x * vec2._x + vec1._y * vec2._y;
		}

		// Vector2からVector2型の外積を取得
		// =============================================================
		const float& Cross(const Vector2& vec1, const Vector2& vec2)noexcept
		{
			return vec1._x * vec2._y - vec1._y * vec2._x;
		}

		// Vector2からVector2同士の差分を取得(Vector2型)
		// =============================================================
		const Vector2& Subtract(const Vector2& vec1, const Vector2& vec2)noexcept
		{
			return vec1 - vec2;
		}

		// Vector2間を線形補完して返却
		// =============================================================
		const Vector2& Vec2Lerp(const Vector2& vec1, const Vector2& vec2, float t)noexcept
		{
			return Vector2(vec1._x + t * (vec2._x - vec1._x), vec1._y + t * (vec2._y - vec1._y));
		}
	}

	namespace vector3
	{
		// Vector3からfloat型の長さの2乗を取得
		// =============================================================
		const float& LengthSq(const Vector3& vec)noexcept
		{
			return (vec._x * vec._x + vec._y * vec._y + vec._z * vec._z);
		}

		// Vector3の2点間の長さの2乗を取得
		// =============================================================
		const float& LengthSq(const Vector3& vec1, const Vector3& vec2)noexcept
		{
			Vector3 temp = Core::vector3::Subtract(vec2, vec1);
			return Core::vector3::LengthSq(temp);
		}

		// Vector3からfloat型の長さを取得
		// =============================================================
		const float& Length(const Vector3& vec)noexcept
		{
			return sqrtf(vec._x * vec._x + vec._y * vec._y + vec._z * vec._z);
		}

		// Vector3の2点間の長さを取得
		// =============================================================
		const float& Length(const Vector3& vec1, const Vector3& vec2)noexcept
		{
			Vector3 temp = Core::vector3::Subtract(vec2, vec1);
			return Core::vector3::Length(temp);
		}

		// Vector3から正規化したVector3型を取得
		// =============================================================
		const Vector3& Normalize(const Vector3& vec)noexcept
		{
			float length = Core::vector3::Length(vec);
			if (length != 0)
				return vec / length;
			return vec;
		}

		// Vector4から正規化したVector3型を取得
		// =============================================================
		const Vector3& Normalize(const Vector4& vec)noexcept
		{
			Vector3 v;
			v = vec;
			float length = Core::vector3::Length(v);
			if (length != 0)
				return v / length;
			return v;
		}

		// Vector3からfloat型の内積を取得
		// =============================================================
		const float& Dot(const Vector3& vec1, const Vector3& vec2)noexcept
		{
			return (vec1._x * vec2._x + vec1._y * vec2._y + vec1._z * vec2._z);
		}

		// Vector3からVector3型の外積を取得
		// =============================================================
		const Vector3& Cross(const Vector3& vec1, const Vector3& vec2)noexcept
		{
			Vector3 v;

			v._x = vec1._y * vec2._z - vec1._z * vec2._y;
			v._y = vec1._z * vec2._x - vec1._x * vec2._z;
			v._z = vec1._x * vec2._y - vec1._y * vec2._x;

			return v;
		}

		// Vector3からVector3同士の差分を取得(Vector3型)
		// =============================================================
		const Vector3& Subtract(const Vector3& vec1, const Vector3& vec2)noexcept
		{
			return vec1 - vec2;
		}

		// Vector3間を線形補完してVector2型として返却
		// =============================================================
		const Vector2& Vec2Lerp(const Vector3& vec1, const Vector3& vec2, float t)noexcept
		{
			return Vector2(vec1._x + t * (vec2._x - vec1._x), vec1._y + t * (vec2._y - vec1._y));
		}

		// Vector3間を線形補完してVector3型として返却
		// =============================================================
		const Vector3& Vec3Lerp(const Vector3& vec1, const Vector3& vec2, float t)noexcept
		{
			return Vector3(vec1._x + t * (vec2._x - vec1._x), vec1._y + t * (vec2._y - vec1._y), vec1._z + t * (vec2._z - vec1._z));
		}
	}

	namespace vector4
	{
		// Vector4からfloat型の長さの2乗を取得
		// =============================================================
		const float& LengthSq(const Vector4& vec)noexcept
		{
			return (vec._x * vec._x + vec._y * vec._y + vec._z * vec._z + vec._w * vec._w);
		}

		// Vector4の2点間の長さの2乗を取得
		// =============================================================
		const float& LengthSq(const Vector4& vec1, const Vector4& vec2)noexcept
		{
			Vector4 temp = Core::vector4::Subtract(vec2, vec1);
			return Core::vector4::LengthSq(temp);
		}

		// Vector4からfloat型の長さを取得
		// =============================================================
		const float& Length(const Vector4& vec)noexcept
		{
			return sqrtf(vec._x * vec._x + vec._y * vec._y + vec._z * vec._z + vec._w * vec._w);
		}

		// Vector4の2点間の長さを取得
		// =============================================================
		const float& Length(const Vector4& vec1, const Vector4& vec2)noexcept
		{
			Vector4 temp = Core::vector4::Subtract(vec2, vec1);
			return Core::vector4::Length(temp);
		}

		// Vector4から正規化したVector4型を取得
		// =============================================================
		const Vector4& Normalize(const Vector4& vec)noexcept
		{
			float length = Core::vector4::Length(vec);
			if (length != 0)
				return vec / length;
			return vec;
		}

		// Vector4からfloat型の内積を取得
		// =============================================================
		const float& Dot(const Vector4& vec1, const Vector4 vec2)noexcept
		{
			return (vec1._x * vec2._x + vec1._y * vec2._y + vec1._z * vec2._z + vec1._w * vec2._w);
		}

		// Vector4からVector4型の外積を取得
		// 未実装
		// =============================================================
		const Vector4& Cross(const Vector4& vec1, const Vector4 vec2)noexcept
		{
			return Vector4::zero;
		}

		// Vector4からVector4同士の差分を取得(Vector4型)
		// =============================================================
		const Vector4& Subtract(const Vector4& vec1, const Vector4& vec2)noexcept
		{
			return vec1 - vec2;
		}

		// Vector4間を線形補完してVector3型として返却
		// =============================================================
		const Vector2& Vec2Lerp(const Vector4& vec1, const Vector4& vec2, float t)noexcept
		{
			return Vector2(vec1._x + t * (vec2._x - vec1._x), vec1._y + t * (vec2._y - vec1._y));
		}

		// Vector4間を線形補完してVector3型として返却
		// =============================================================
		const Vector3& Vec3Lerp(const Vector4& vec1, const Vector4& vec2, float t)noexcept
		{
			return Vector3(vec1._x + t * (vec2._x - vec1._x), vec1._y + t * (vec2._y - vec1._y), vec1._z + t * (vec2._z - vec1._z));
		}

		// Vector4間を線形補完してVector3型として返却
		// =============================================================
		const Vector4& Vec4Lerp(const Vector4& vec1, const Vector4& vec2, float t)noexcept
		{
			return Vector4(vec1._x + t * (vec2._x - vec1._x), vec1._y + t * (vec2._y - vec1._y), vec1._z + t * (vec2._z - vec1._z), vec1._w + t * (vec2._w - vec1._w));
		}
	}

	namespace color
	{
		// 基本的なグレースケールの色を取得
		// =============================================================
		const Color& ColorGrayScale(const Color& color)noexcept
		{
			Color temp;
			temp = (color._a * 0.2126f + color._g * 0.7152f + color._b * 0.0722f) / 3;
			return temp;
		}

		// Rを基調としたグレースケールの色を取得
		// =============================================================
		const Color& ColorGrayScaleR(const Color& color)noexcept
		{
			return Color(color._r, color._r, color._r, color._a);
		}

		// Gを基調としたグレースケールの色を取得
		// =============================================================
		const Color& ColorGrayScaleG(const Color& color)noexcept
		{
			return Color(color._g, color._g, color._g, color._a);
		}

		// Bを基調としたグレースケールの色を取得
		// =============================================================
		const Color& ColorGrayScaleB(const Color& color)noexcept
		{
			return Color(color._b, color._b, color._b, color._a);
		}

		// R,G,Bをそれぞれ反転した色を取得
		// =============================================================
		const Color& ColorNegapoji(const Color& color)noexcept
		{
			Color temp = color;
			for (int index = 0; index < 4; ++index)
			{
				temp._color[index] = 1.0f - temp._color[index];
			}

			return Core::myMath::ColorClamp(temp, Color::black, Color::white);
		}

		// セピア調の色を取得
		// =============================================================
		const Color& ColorSepia(const Color& color)noexcept
		{
			float Y = 0.299f * color._r + 0.587f * color._g + 0.114f * color._b;

			// セピア調ではモノクロ化とは違い、R,G,Bに明るさをそのまま代入はしない
			// 今回の実装では、赤みの成分に0.9、緑に0.7、bに0.4の重みを乗算している
			Color temp = Color();
			temp._r = Y * 0.9f;
			temp._g = Y * 0.7f;
			temp._b = Y * 0.4f;

			return temp;
		}

		// モノクロ調の色を取得
		// =============================================================
		const Color& ColorMonocrome(const Color& color)noexcept
		{
			// 画像をモノクロに変化させていく
			// ピクセルの明るさを計算する
			float Y = 0.299f * color._r + 0.587 * color._g + 0.114f * color._b;

			Color temp = Color();
			temp = Y;

			return temp;
		}

		// Rの値のみ取得
		// =============================================================
		const Color& ColorRScale(const Color& color)noexcept
		{
			return Color(color._r, 0.0f, 0.0f, color._a);
		}

		// Gの値のみ取得
		// =============================================================
		const Color& ColorGScale(const Color& color)noexcept
		{
			return Color(0.0f, color._g, 0.0f, color._a);
		}

		// Bの値のみ取得
		// =============================================================
		const Color& ColorBScale(const Color& color)noexcept
		{
			return Color(0.0f, 0.0f, color._b, color._a);
		}

		// 0.0f～1.0fの値に収めて取得
		// =============================================================
		const Color& ColorSaturate(const Color& color)noexcept
		{
			return Core::myMath::ColorClamp(color, Color::white, Color::black);
		}
	}

	namespace myVector
	{
		// MyVECTORを返り値に引数をセット
		// =============================================================
		const MyVECTOR& MyVectorSet(const float& X, const float& Y, const float& Z, const float& W)noexcept
		{
			return MyVECTOR(X, Y, Z, W);
		}

		// MyVECTORから_xFloat,_yFloatを取得しfloat型で長さを取得
		// =============================================================
		const float& MyVector2Length(const MyVECTOR& vec)noexcept
		{
			return sqrtf(vec._xFloat * vec._xFloat + vec._yFloat * vec._yFloat);
		}

		// MyVECTORから_xFloat,_yFloat,_zFloatを取得しfloat型で長さを取得
		// =============================================================
		const float& MyVector3Length(const MyVECTOR& vec)noexcept
		{
			return sqrtf(vec._xFloat * vec._xFloat + vec._yFloat * vec._yFloat + vec._zFloat * vec._zFloat);
		}

		// MyVECTORから値全てを取得しfloat型で長さを取得
		// =============================================================
		const float& MyVector4Length(const MyVECTOR& vec)noexcept
		{
			return sqrtf(vec._xFloat * vec._xFloat + vec._yFloat * vec._yFloat + vec._zFloat * vec._zFloat + vec._wFloat * vec._wFloat);
		}

		// MyVECTORから_xFloat,_yFloatを取得しMyVECTOR型で正規化を取得
		// =============================================================
		const MyVECTOR& MyVector2Normalize(const MyVECTOR& vec)noexcept
		{
			float length = Core::myVector::MyVector2Length(vec);
			if (length != 0)
				return vec / length;
			return vec;
		}

		// MyVECTORから_xFloat,_yFloat, _zFloatを取得しMyVECTOR型で正規化を取得
		// =============================================================
		const MyVECTOR& MyVector3Normalize(const MyVECTOR& vec)noexcept
		{
			float length = Core::myVector::MyVector3Length(vec);
			if (length != 0)
				return vec / length;
			return vec;
		}

		// MyVECTORから値全てを取得しMyVECTOR型で正規化を取得
		// =============================================================
		const MyVECTOR& MyVector4Normalize(const MyVECTOR& vec)noexcept
		{
			float length = Core::myVector::MyVector4Length(vec);
			if (length != 0)
				return vec / length;
			return vec;
		}

		// 2値のMyVECTORからvec1からvec2を引いた差分をMyVECTOR型で取得
		// =============================================================
		const MyVECTOR& MyVectorSubtract(const MyVECTOR& vec1, const MyVECTOR& vec2)noexcept
		{
			return vec1 - vec2;
		}

		// MyVECTORから_xFloat,_yFloatを取得しMyVECTOR型で内積を取得
		// =============================================================
		const MyVECTOR& MyVector2Dot(const MyVECTOR& vec1, const MyVECTOR& vec2)noexcept
		{
			float t = vec1._xFloat * vec2._xFloat + vec1._yFloat * vec2._yFloat;
			MyVECTOR vec;
			vec = t;
			return vec;
		}

		// MyVECTORから_xFloat,_yFloat,_zFloatを取得しMyVECTOR型で内積を取得
		// =============================================================
		const MyVECTOR& MyVector3Dot(const MyVECTOR& vec1, const MyVECTOR& vec2)noexcept
		{
			float t = vec1._xFloat * vec2._xFloat + vec1._yFloat * vec2._yFloat + vec1._zFloat * vec2._zFloat;
			MyVECTOR vec;
			vec = t;
			return vec;
		}

		// MyVECTORから全ての値を取得しMyVECTOR型で内積を取得
		// =============================================================
		const MyVECTOR& MyVector4Dot(const MyVECTOR& vec1, const MyVECTOR& vec2)noexcept
		{
			float t = vec1._xFloat * vec2._xFloat + vec1._yFloat * vec2._yFloat + vec1._zFloat * vec2._zFloat + vec1._wFloat * vec2._wFloat;
			MyVECTOR vec;
			vec = t;
			return vec;
		}

		// MyVECTORから_xFloat,_yFloatを取得しMyVECTOR型で外積を取得
		// =============================================================
		const MyVECTOR& MyVector2Cross(const MyVECTOR& vec1, const MyVECTOR& vec2)noexcept
		{
			MyVECTOR vec;
			float t = vec1._xFloat * vec2._yFloat - vec1._yFloat * vec2._xFloat;
			vec = t;
			return vec;
		}

		// MyVECTORから_xFloat,_yFloat,_zFloatを取得しMyVECTOR型で外積を取得
		// =============================================================
		const MyVECTOR& MyVector3Cross(const MyVECTOR& vec1, const MyVECTOR& vec2)noexcept
		{
			MyVECTOR v;

			v._xFloat = vec1._yFloat * vec2._zFloat - vec1._zFloat * vec2._yFloat;
			v._yFloat = vec1._zFloat * vec2._xFloat - vec1._xFloat * vec2._zFloat;
			v._zFloat = vec1._xFloat * vec2._yFloat - vec1._yFloat * vec2._xFloat;

			return v;
		}

		// MyVECTORから全値を取得しMyVECTOR型で外積を取得
		// =============================================================
		const MyVECTOR& MyVector4Cross(const MyVECTOR& vec1, const MyVECTOR& vec2, const MyVECTOR& vec3)noexcept
		{
			MyVECTOR v;

			v._xFloat = vec1._yFloat * (vec2._zFloat * vec3._wFloat - vec3._zFloat * vec2._wFloat)
				- vec1._zFloat * (vec2._yFloat * vec3._wFloat - vec3._yFloat * vec2._wFloat)
				+ vec1._wFloat * (vec2._yFloat * vec3._zFloat - vec3._yFloat * vec2._zFloat);

			v._yFloat = vec1._xFloat * (vec3._zFloat * vec2._wFloat - vec2._zFloat * vec3._wFloat)
				- vec1._zFloat * (vec3._xFloat * vec2._wFloat - vec2._xFloat * vec3._wFloat)
				+ vec1._wFloat * (vec3._xFloat * vec2._zFloat - vec2._xFloat * vec3._zFloat);

			v._zFloat = vec1._xFloat * (vec2._yFloat * vec3._wFloat - vec3._yFloat * vec2._wFloat)
				- vec1._yFloat * (vec2._xFloat * vec3._wFloat - vec3._xFloat * vec2._wFloat)
				+ vec1._wFloat * (vec2._xFloat * vec3._yFloat - vec3._xFloat * vec2._yFloat);

			v._wFloat = vec1._xFloat * (vec3._yFloat * vec2._zFloat - vec2._yFloat * vec3._zFloat)
				- vec1._yFloat * (vec3._xFloat * vec2._zFloat - vec2._xFloat * vec3._zFloat)
				+ vec1._zFloat * (vec3._xFloat * vec2._yFloat - vec2._xFloat * vec3._yFloat);

			return v;
		}

		// MyVECTORからX成分をfloat型で取得
		// =============================================================
		const float& MyVectorGetXF(const MyVECTOR& vec)noexcept
		{
			return vec._xFloat;
		}

		// MyVECTORからY成分をfloat型で取得
		// =============================================================
		const float& MyVectorGetYF(const MyVECTOR& vec)noexcept
		{
			return vec._yFloat;
		}

		// MyVECTORからZ成分をfloat型で取得
		// =============================================================
		const float& MyVectorGetZF(const MyVECTOR& vec)noexcept
		{
			return vec._zFloat;
		}

		// MyVECTORからZ成分をfloat型で取得
		// =============================================================
		const float& MyVectorGetWF(const MyVECTOR& vec)noexcept
		{
			return vec._wFloat;
		}

#if 0
		// MyVECTORから否定を計算(DirectX::XMVectorNegate()に準拠)
		// =============================================================
		const MyVECTOR& MyVectorNegate(const MyVECTOR& vec)noexcept
		{
			__m128 changeVec;
			__m128 temp;

			for (int index = 0; index < 4; ++index)
			{
				changeVec.m128_f32[index] = vec._vec[index];
			}

			// SIMD命令
			temp = _mm_setzero_ps();

			__m128 resultM = _mm_sub_ps(temp, changeVec);

			MyVECTOR resultVec;
			for (int index = 0; index < 4; ++index)
			{
				resultVec._vec[index] = resultM.m128_f32[index];
			}

			return resultVec;
		}

		// Vector4から否定を計算(DirectX::XMVectorNegate()に準拠)
		// =============================================================
		const MyVECTOR& MyVectorNegate(const Vector4& vec)noexcept
		{
			MyVECTOR v;
			v = vec;
			v = Core::myVector::KOVectorNegate(v);
			return v;
		}

		// まぁとりあえずすげぇ関数(DirectX::XMVectorSelect()に準拠)
		// =============================================================
		const MyVECTOR& MyVectorSelect(const MyVECTOR& vec1, const MyVECTOR& vec2, const MyVECTOR& controllVec)noexcept
		{
			__m128 tempVec1;
			__m128 tempVec2;
			__m128 tempControllVec;

			for (int index = 0; index < 4; ++index)
			{
				tempVec1.m128_f32[index] = vec1._vec[index];
				tempVec2.m128_f32[index] = vec2._vec[index];
				tempControllVec.m128_u32[index] = controllVec._vecInt[index];
			}

			// SIMD命令
			__m128 m1 = _mm_andnot_ps(tempControllVec, tempVec1);
			__m128 m2 = _mm_and_ps(tempVec2, tempControllVec);
			__m128 m3 = _mm_or_ps(m1, m2);

			MyVECTOR resultVec;
			for (int index = 0; index < 4; ++index)
			{
				resultVec._vec[index] = m3.m128_f32[index];
			}
			return resultVec;
		}
#endif

		// 全要素が等しいか検出
		// =============================================================
		const bool& MyVector4Equal(MyVECTOR& vec1, MyVECTOR& vec2)noexcept
		{
			return vec1 == vec2;
		}

		// 3要素が等しいか検出
		// =============================================================
		const bool& MyVector3Equal(const MyVECTOR& vec1, const MyVECTOR& vec2)noexcept
		{
			Vector3 v1, v2;
			v1 = vec1;
			v2 = vec2;

			return v1 == v2;
		}

		// 2要素が等しいか検出
		// =============================================================
		const bool& MyVector2Equal(const MyVECTOR& vec1, const MyVECTOR& vec2)noexcept
		{
			Vector2 v1, v2;
			v1 = vec1;
			v2 = vec2;

			return v1 == v2;
		}

		// Vector3型からVector3型の正規化ベクトルを取得
		// =============================================================
		const Vector3& MyVector3Normalize(const Vector3& vec)noexcept
		{
			Vector3 v;
			v = Core::vector3::Normalize(vec);
			return v;
		}

		// Vector4型からVector3型の正規化ベクトルを取得
		// =============================================================
		const Vector3& MyVector3Normalize(const Vector4& vec)noexcept
		{
			Vector3 v;
			v = Core::vector3::Normalize(vec);
			return v;
		}

		// Vector4型からVector4型の正規化ベクトルを取得
		// =============================================================
		const Vector4& MyVector4Normalize(const Vector4& vec)noexcept
		{
			MyVECTOR v;
			v = vec;
			v = Core::myVector::MyVector4Normalize(v);

			Vector4 v4;
			v4._x = v._xFloat;
			v4._y = v._yFloat;
			v4._z = v._zFloat;
			v4._w = v._wFloat;

			return v4;
		}

		// Vector3型からVector4型の正規化ベクトルを取得
		// =============================================================
		const Vector4& MyVector4Normalize(const Vector3& vec)noexcept
		{
			MyVECTOR v;
			v = vec;
			v = Core::myVector::MyVector4Normalize(v);

			Vector4 v4;
			v4._x = v._xFloat;
			v4._y = v._yFloat;
			v4._z = v._zFloat;
			v4._w = v._wFloat;
			return v4;
		}

		// Vector4同士の差分をvector4型で取得
		// =============================================================
		const Vector4& MyVectorSubtract(const Vector4& vec1, const Vector4& vec2)noexcept
		{
			return vec1 - vec2;
		}

		// Vector4同士の差分をVector3型で取得
		// =============================================================
		const Vector3& MyVectorSubtractVec3(const Vector4& vec1, const Vector4& vec2)noexcept
		{
			Vector3 v1, v2, v3;
			v1 = vec1;
			v2 = vec2;
			v3 = v1 - v2;
			return v3;
		}

		// Vector3同士でVector3の内積を取得
		// =============================================================
		const Vector3& MyVector3Dot(const Vector3& vec1, const Vector3& vec2)noexcept
		{
			MyVECTOR v1, v2;
			v1 = vec1;
			v2 = vec2;
			v1 = Core::myVector::MyVector3Dot(v1, v2);

			Vector3 v3;
			v3._x = v1._xFloat;
			v3._y = v1._yFloat;
			v3._z = v1._zFloat;
			return v3;
		}

		// Vector4同士でVector3の内積を取得
		// =============================================================
		const Vector3& MyVector3Dot(const Vector4& vec1, const Vector4& vec2)noexcept
		{
			MyVECTOR v1, v2;
			v1 = vec1;
			v2 = vec2;
			v1 = Core::myVector::MyVector3Dot(v1, v2);

			Vector3 v3;
			v3._x = v1._xFloat;
			v3._y = v1._yFloat;
			v3._z = v1._zFloat;
			return v3;
		}

		// Vector4同士でVector4の内積を取得
		// =============================================================
		const Vector4& MyVector4Dot(const Vector4& vec1, const Vector4& vec2)noexcept
		{
			MyVECTOR v1, v2;
			v1 = vec1;
			v2 = vec2;
			v1 = Core::myVector::MyVector4Dot(v1, v2);

			Vector4 v4;
			v4._x = v1._xFloat;
			v4._y = v1._yFloat;
			v4._z = v1._zFloat;
			v4._w = v1._wFloat;

			return v4;
		}

		// vector3同士でvector4の内積を取得
		// =============================================================
		const Vector4& MyVector4Dot(const Vector3& vec1, const Vector3& vec2)noexcept
		{
			MyVECTOR v1, v2;
			v1 = vec1;
			v2 = vec2;
			v1 = Core::myVector::MyVector4Dot(v1, v2);

			Vector4 v4;
			v4._x = v1._xFloat;
			v4._y = v1._yFloat;
			v4._z = v1._zFloat;
			v4._w = v1._wFloat;

			return v4;
		}

		// Vector3同士でVector3の外積を取得
		// =============================================================
		const Vector3& MyVector3Cross(const Vector3& vec1, const Vector3& vec2)noexcept
		{
			MyVECTOR v1, v2;
			v1 = vec1;
			v2 = vec2;

			v1 = Core::myVector::MyVector3Cross(v1, v2);

			Vector3 v3;
			v3._x = v1._xFloat;
			v3._y = v1._yFloat;
			v3._z = v1._zFloat;
			return v3;
		}

		// Vector4同士でVector3の外積を取得
		// =============================================================
		const Vector3& MyVector3Cross(const Vector4& vec1, const Vector4& vec2)noexcept
		{
			MyVECTOR v1, v2;
			v1 = vec1;
			v2 = vec2;

			v1 = Core::myVector::MyVector3Cross(v1, v2);

			Vector3 v3;
			v3._x = v1._xFloat;
			v3._y = v1._yFloat;
			v3._z = v1._zFloat;
			return v3;
		}

		// Vector4型3つでVector4型の外積を取得
		// =============================================================
		const Vector4& MyVector4Cross(const Vector4& vec1, const Vector4& vec2, const Vector4& vec3)noexcept
		{
			MyVECTOR v1, v2, v3;
			v1 = vec1;
			v2 = vec2;
			v3 = vec3;
			v1 = Core::myVector::MyVector4Cross(v1, v2, v3);

			Vector4 v4;
			v4._x = v1._xFloat;
			v4._y = v1._yFloat;
			v4._z = v1._zFloat;
			v4._w = v1._wFloat;

			return v4;
		}

		// Vector3型3つでVector4型の外積を取得
		// =============================================================
		const Vector4& MyVector4Cross(const Vector3& vec1, const Vector3& vec2, const Vector3& vec3)noexcept
		{
			MyVECTOR v1, v2, v3;
			v1 = vec1;
			v2 = vec2;
			v3 = vec3;
			v1 = Core::myVector::MyVector4Cross(v1, v2, v3);

			Vector4 v4;
			v4._x = v1._xFloat;
			v4._y = v1._yFloat;
			v4._z = v1._zFloat;
			v4._w = v1._wFloat;

			return v4;
		}

		// 3要素が等しいか検出
		// =============================================================
		const bool& MyVector3Equal(Vector4& vec1, Vector4& vec2)noexcept
		{
			Vector3 v1, v2;
			v1 = vec1;
			v2 = vec2;
			return v1 == v2;
		}

		// 2要素が等しいか検出
		// =============================================================
		const bool& MyVector2Equal(Vector4& vec1, Vector4& vec2)noexcept
		{
			Vector2 v1, v2;
			v1 = vec1;
			v2 = vec2;
			return v1 == v2;
		}

		// 2要素が等しいか検出
		// =============================================================
		const bool& MyVector2Equal(Vector3& vec1, Vector3& vec2)noexcept
		{
			Vector2 v1, v2;
			v1 = vec1;
			v2 = vec2;
			return v1 == v2;
		}
	}

#if 0
	namespace koMatrix
	{
		// 単位行列を生成
		// =============================================================
		const Matrix& KOMatrixIdentity(void)noexcept
		{
			return Matrix::identity;
		}

		// スケールの行列を生成
		// =============================================================
		const Matrix& KOMatrixScaling(float scaleX, float scaleY, float scaleZ)noexcept
		{
			return Matrix(scaleX, 0.0f, 0.0f, 0.0f,
				0.0f, scaleY, 0.0f, 0.0f,
				0.0f, 0.0f, scaleZ, 0.0f,
				0.0f, 0.0f, 0.0f, 1.0f);
		}

		// Vector3からスケール行列を生成
		// =============================================================
		const Matrix& KOMatrixScaling(Vector3 scale)noexcept
		{
			return Core::koMatrix::KOMatrixScaling(scale._x, scale._y, scale._z);
		}

		// 回転行列を生成
		// =============================================================
		const Matrix& KOMatrixYawPitchRoll(float rotX, float rotY, float rotZ)noexcept
		{
			Core::Matrix result;

			// 行列の設定
			result._m11 = cosf(rotY) * cosf(rotZ);
			result._m12 = cosf(rotY) * sinf(rotZ);
			result._m13 = -sinf(rotY);
			result._m14 = 0.0f;

			result._m21 = sinf(rotX) * sinf(rotY) * cosf(rotZ) - cosf(rotX) * sinf(rotZ);
			result._m22 = sinf(rotX) * sinf(rotY) * sinf(rotZ) + cosf(rotX) * cosf(rotZ);
			result._m23 = sinf(rotX) * cosf(rotY);
			result._m24 = 0.0f;

			result._m31 = cosf(rotX) * sinf(rotY) * cosf(rotZ) + sinf(rotX) * sinf(rotZ);
			result._m32 = cosf(rotX) * sinf(rotY) * sinf(rotZ) - sinf(rotX) * cosf(rotZ);
			result._m33 = cosf(rotX) * cosf(rotY);
			result._m34 = 0.0f;

			result._m41 = 0.0f;
			result._m42 = 0.0f;
			result._m43 = 0.0f;
			result._m44 = 1.0f;

			return result;
		}

		// Vector3から回転行列を生成
		// =============================================================
		const Matrix& KOMatrixYawPitchRoll(Vector3 rot)noexcept
		{
			return Core::koMatrix::KOMatrixYawPitchRoll(rot._x, rot._y, rot._z);
		}

		// 移動行列を生成
		// =============================================================
		const Matrix& KOMatrixTranslation(float offsetX, float offsetY, float offsetZ)noexcept
		{
			return Matrix(1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, offsetX, offsetY, offsetZ, 1.0f);
		}

		// Vector3から移動行列を生成
		// =============================================================
		const Matrix& KOMatrixTranslation(Vector3 offset)noexcept
		{
			return Core::koMatrix::KOMatrixTranslation(offset._x, offset._y, offset._z);
		}

		// Matrix同士の行列の掛け算を行う
		// =============================================================
		const Matrix& KOMatrixMultiply(const Matrix& m1, const Matrix& m2)noexcept
		{
			Matrix m;

			m._m11 = m1._m11 * m2._m11 + m1._m12 * m2._m21 + m1._m13 * m2._m31 + m1._m14 * m2._m41;
			m._m12 = m1._m11 * m2._m12 + m1._m12 * m2._m22 + m1._m13 * m2._m32 + m1._m14 * m2._m42;
			m._m13 = m1._m11 * m2._m13 + m1._m12 * m2._m23 + m1._m13 * m2._m33 + m1._m14 * m2._m43;
			m._m14 = m1._m11 * m2._m14 + m1._m12 * m2._m24 + m1._m13 * m2._m34 + m1._m14 * m2._m44;

			m._m21 = m1._m21 * m2._m11 + m1._m22 * m2._m21 + m1._m23 * m2._m31 + m1._m24 * m2._m41;
			m._m22 = m1._m21 * m2._m12 + m1._m22 * m2._m22 + m1._m23 * m2._m32 + m1._m24 * m2._m42;
			m._m23 = m1._m21 * m2._m13 + m1._m22 * m2._m23 + m1._m23 * m2._m33 + m1._m24 * m2._m43;
			m._m24 = m1._m21 * m2._m14 + m1._m22 * m2._m24 + m1._m23 * m2._m34 + m1._m24 * m2._m44;

			m._m31 = m1._m31 * m2._m11 + m1._m32 * m2._m21 + m1._m33 * m2._m31 + m1._m34 * m2._m41;
			m._m32 = m1._m31 * m2._m12 + m1._m32 * m2._m22 + m1._m33 * m2._m32 + m1._m34 * m2._m42;
			m._m33 = m1._m31 * m2._m13 + m1._m32 * m2._m23 + m1._m33 * m2._m33 + m1._m34 * m2._m43;
			m._m34 = m1._m31 * m2._m14 + m1._m32 * m2._m24 + m1._m33 * m2._m34 + m1._m34 * m2._m44;

			m._m41 = m1._m41 * m2._m11 + m1._m42 * m2._m21 + m1._m43 * m2._m31 + m1._m44 * m2._m41;
			m._m42 = m1._m41 * m2._m12 + m1._m42 * m2._m22 + m1._m43 * m2._m32 + m1._m44 * m2._m42;
			m._m43 = m1._m41 * m2._m13 + m1._m42 * m2._m23 + m1._m43 * m2._m33 + m1._m44 * m2._m43;
			m._m44 = m1._m41 * m2._m14 + m1._m42 * m2._m24 + m1._m43 * m2._m34 + m1._m44 * m2._m44;

			//for (int y = 0; y < 4; ++y)
			//{
			//	float sum;
			//	for (int x = 0; x < 4; ++x)
			//	{
			//		sum += m1._mat[y][x] * m2._mat[x][y];
			//	}
			//}
			return m;
		}

		// KOVECTORから視点の向きを利用した左手座標系ビュー行列を生成
		// =============================================================
		const Matrix& KOMatrixLookToLH(const KOVECTOR& eyePosition, const KOVECTOR& eyeDirection, const KOVECTOR& upDirection)noexcept
		{
			bool item = !Core::myVector::KOVector3Equal(eyeDirection, KOVECTOR::zero);
			MyAssert(item, "eyeDirection has become all 0.0f.", "CreateViewMatrix");
			if (!item)
			{
				return Matrix::allminus;
			}

			item = !Core::myVector::KOVector3Equal(upDirection, KOVECTOR::zero);
			MyAssert(item, "upDirection has become all 0.0f.", "CreateViewMatrix");
			if (!item)
			{
				return Matrix::allminus;
			}

			KOVECTOR R2 = Core::myVector::KOVector3Normalize(eyeDirection);

			KOVECTOR R0 = Core::myVector::KOVector3Cross(upDirection, R2);
			R0 = Core::myVector::KOVector3Normalize(R0);

			KOVECTOR R1 = Core::myVector::KOVector3Cross(R2, R0);

			KOVECTOR NegEyePosition = Core::myVector::KOVectorNegate(eyePosition);

			KOVECTOR D0 = Core::myVector::KOVector3Dot(R0, NegEyePosition);
			KOVECTOR D1 = Core::myVector::KOVector3Dot(R1, NegEyePosition);
			KOVECTOR D2 = Core::myVector::KOVector3Dot(R2, NegEyePosition);

			KOVECTOR tempSelect1110;
			tempSelect1110._vecInt[0] = 0xFFFFFFFF;
			tempSelect1110._vecInt[1] = 0xFFFFFFFF;
			tempSelect1110._vecInt[2] = 0xFFFFFFFF;
			tempSelect1110._vecInt[3] = 0;

			Matrix m;
			m._koVec[0] = Core::myVector::KOVectorSelect(D0, R0, tempSelect1110);
			m._koVec[1] = Core::myVector::KOVectorSelect(D1, R1, tempSelect1110);
			m._koVec[2] = Core::myVector::KOVectorSelect(D2, R2, tempSelect1110);
			m._koVec[3] = Core::myVector::KOVectorSet(0.0f, 0.0f, 0.0f, 1.0f);

			m = KOMatrixTranspose(m);

			return m;
		}

		// Vector4から視点の向きを利用した左手座標系ビュー行列を生成
		// =============================================================
		const Matrix& KOMatrixLookToLH(const Vector4& eyePosition, const Vector4& eyeDirection, const Vector4& upDirection)noexcept
		{
			KOVECTOR tempEyePosition;
			KOVECTOR tempEyeDirection;
			KOVECTOR tempUpDirection;
			tempEyePosition = eyePosition;
			tempEyeDirection = eyeDirection;
			tempUpDirection = upDirection;
			return Core::koMatrix::KOMatrixLookToLH(tempEyePosition, tempEyeDirection, tempUpDirection);
		}

		// KOVECTORから注視点を利用した左手座標系ビュー行列を生成
		// =============================================================
		const Matrix& KOMatrixLookAtLH(const KOVECTOR& eyePosition, const KOVECTOR& focusPosition, const KOVECTOR& upDirection)noexcept
		{
			KOVECTOR eyeDirection = Core::myVector::KOVectorSubtract(focusPosition, eyePosition);
			return Core::koMatrix::KOMatrixLookToLH(eyePosition, eyeDirection, upDirection);
		}

		// Vector4から注視点を利用した左手座標系ビュー行列を生成
		// =============================================================
		const Matrix& KOMatrixLookAtLH(const Vector4& eyePosition, const Vector4& focusPosition, const Vector4& upDirection)noexcept
		{
			Vector4 eyeDirection = Core::myVector::KOVectorSubtract(focusPosition, eyePosition);
			return Core::koMatrix::KOMatrixLookToLH(eyePosition, eyeDirection, upDirection);
		}

		// KOVECTORから視点の向きを利用した右手座標系ビュー行列を生成
		// =============================================================
		const Matrix& KOMatrixLookToRH(const KOVECTOR& eyePosition, const KOVECTOR& eyeDirection, const KOVECTOR& upDirection)noexcept
		{
			KOVECTOR NegEyeDirection = Core::myVector::KOVectorNegate(eyeDirection);
			return Core::koMatrix::KOMatrixLookToLH(eyePosition, NegEyeDirection, upDirection);
		}

		// KOVECTORから視点の向きを利用した右手座標系ビュー行列を生成
		// =============================================================
		const Matrix& KOMatrixLookToRH(const Vector4& eyePosition, const Vector4& eyeDirection, const Vector4& upDirection)noexcept
		{
			KOVECTOR tempEyePosition;
			KOVECTOR tempEyeDirection;
			KOVECTOR tempUpDirection;
			tempEyePosition = eyePosition;
			tempEyeDirection = eyeDirection;
			tempUpDirection = upDirection;
			return Core::koMatrix::KOMatrixLookToRH(tempEyePosition, tempEyeDirection, tempUpDirection);
		}

		// KOVECTORから注視点を利用した右手座標系ビュー行列を生成
		// =============================================================
		const Matrix& KOMatrixLookAtRH(const KOVECTOR& eyePosition, const KOVECTOR& focusPosition, const KOVECTOR& upDirection)noexcept
		{
			KOVECTOR NegEyeDirection = Core::myVector::KOVectorSubtract(eyePosition, focusPosition);
			return Core::koMatrix::KOMatrixLookToLH(eyePosition, NegEyeDirection, upDirection);
		}

		// Vector4から注視点を利用した右手座標系ビュー行列を生成
		// =============================================================
		const Matrix& KOMatrixLookAtRH(const Vector4& eyePosition, const Vector4& focusPosition, const Vector4& upDirection)noexcept
		{
			KOVECTOR tempEyePosition;
			KOVECTOR tempFocusPosition;
			KOVECTOR tempUpDirection;
			tempEyePosition = eyePosition;
			tempFocusPosition = focusPosition;
			tempUpDirection = upDirection;
			return Core::koMatrix::KOMatrixLookAtRH(tempEyePosition, tempFocusPosition, tempUpDirection);
		}

		// 逆行列を生成
		// =============================================================
		const Matrix& KOMatrixInverse(const Matrix& mat)noexcept
		{
			Matrix temp;

			for (int y = 0; y < 4; ++y)
			{
				for (int x = 0; x < 4; ++x)
				{
					if (mat._mat[y][x] != 0)
						temp._mat[y][x] = 1 / mat._mat[y][x];
				}
			}

			return temp;
		}

		// 透視投影の行列を生成
		// =============================================================
		const Matrix& KOMatrixPerspectiveFovLH(float fovAngleY, float aspectRatio, float nearZ, float farZ)noexcept
		{
			float tanHalfFovY = tanf(fovAngleY * 0.5f);

			float scaleY = 1.0f / tanHalfFovY;
			float scaleX = scaleY / aspectRatio;

			float rangeInv = 1.0f / (farZ - nearZ);

			return Matrix(scaleX, 0.0f, 0.0f, 0.0f, 0.0f, scaleY, 0.0f, 0.0f, 0.0f, 0.0f, farZ * rangeInv, 1.0f, 0.0f, 0.0f, -nearZ * farZ * rangeInv, 0.0f);
		}

		// 平行投影の行列を生成
		// =============================================================
		const Matrix& KOMatrixOrthographicOffCenterLH(float viewLeft, float viewRight, float viewBottom, float viewTop, float nearZ, float farZ)noexcept
		{
			float invWidth = 1.0f / (viewRight - viewLeft);
			float invHeight = 1.0f / (viewTop - viewBottom);
			float invDepth = 1.0f / (farZ - nearZ);

			return Matrix(
				2.0f * invWidth, 0.0f, 0.0f, 0.0f,
				0.0f, 2.0f * invHeight, 0.0f, 0.0f,
				0.0f, 0.0f, invDepth, 0.0f,
				(viewLeft + viewRight) * -invWidth, (viewTop + viewBottom) * -invHeight, nearZ * -invDepth, 1.0f
			);
		}

		// 平行投影の行列を生成(Part2)
		// =============================================================
		const Matrix& KOMatrixOrthographicLH(float viewWidth, float viewHeight, float nearZ, float farZ)noexcept
		{
			float invWidth = 1.0f / viewWidth;
			float invHeight = 1.0f / viewHeight;
			float invDepth = 1.0f / (farZ - nearZ);

			return Matrix(
				2.0f * invWidth, 0.0f, 0.0f, 0.0f,
				0.0f, 2.0f * invHeight, 0.0f, 0.0f,
				0.0f, 0.0f, invDepth, 0.0f,
				0.0f, 0.0f, -nearZ * invDepth, 1.0f
			);
		}

		// 転置行列を生成
		// =============================================================
		const Matrix& KOMatrixTranspose(const Matrix& mat)noexcept
		{
			Matrix m;

			for (int y = 0; y < 4; ++y)
			{
				for (int x = 0; x < 4; ++x)
				{
					m._mat[y][x] = mat._mat[x][y];
				}
			}

			return m;
		}
	}
#endif

	namespace myMath
	{
		// 各要素をmin～maxに収めて取得
		// =============================================================
		const Color& ColorClamp(const Color& color, const Color& min, const Color& max)noexcept
		{
			return Core::myMath::ColorMax(min, Core::myMath::ColorMin(color, max));
		}

		// 各要素の小さい値の方で色を取得
		// 同値はcolor1の要素を返却
		// =============================================================
		const Color& ColorMin(const Color& color1, const Color& color2)noexcept
		{
			return Color(
				color1._r <= color2._r ? color1._r : color2._r,
				color1._g <= color2._g ? color1._g : color2._g,
				color1._b <= color2._b ? color1._b : color2._b,
				color1._a <= color2._a ? color1._a : color2._a
			);
		}

		// 各要素の大きい値の方で色を取得
		// 同値はcolor1の要素を返却
		// =============================================================
		const Color& ColorMax(const Color& color1, const Color& color2)noexcept
		{
			return Color(
				color1._r >= color2._r ? color1._r : color2._r,
				color1._g >= color2._g ? color1._g : color2._g,
				color1._b >= color2._b ? color1._b : color2._b,
				color1._a >= color2._a ? color1._a : color2._a
			);
		}

		// 各要素をmin～maxに収めて取得
		// =============================================================
		const Vector4& Vec4Clamp(const Vector4& vec4, const Vector4& minVec, const Vector4& maxVec)noexcept
		{
			return Vec4Max(minVec, Vec4Min(vec4, maxVec));
		}

		// 各要素の小さい値の方で色を取得
		// 同値はcolor1の要素を返却
		// =============================================================
		const Vector4& Vec4Min(const Vector4& vec4_1, const Vector4& vec4_2)noexcept
		{
			return Vector4(
				vec4_1._x <= vec4_2._x ? vec4_1._x : vec4_2._x,
				vec4_1._y <= vec4_2._y ? vec4_1._y : vec4_2._y,
				vec4_1._z <= vec4_2._z ? vec4_1._z : vec4_2._z,
				vec4_1._w <= vec4_2._w ? vec4_1._w : vec4_2._w
				);
		}

		// 各要素の大きい値の方で色を取得
		// 同値はcolor1の要素を返却
		// =============================================================
		const Vector4& Vec4Max(const Vector4& vec4_1, const Vector4& vec4_2)noexcept
		{
			return Vector4(
				vec4_1._x >= vec4_2._x ? vec4_1._x : vec4_2._x,
				vec4_1._y >= vec4_2._y ? vec4_1._y : vec4_2._y,
				vec4_1._z >= vec4_2._z ? vec4_1._z : vec4_2._z,
				vec4_1._w >= vec4_2._w ? vec4_1._w : vec4_2._w
			);
		}

		// ディグリー角からラジアンへ
		// =============================================================
		const float& DegToRad(const float& deg)noexcept
		{
			return deg * (PI / 180.0f);
		}

		// ラジアンをディグリーへ
		// =============================================================
		const float& RadToDeg(const float& rad)noexcept
		{
			return rad / (PI / 180.0f);
		}

#if 0
		// viewMatrixからカメラの回転角を求める
		// =============================================================
		const Vector3& ExtractRotationAnglesFromViewMatrix(const Matrix& viewMatrix) {
			
			Matrix view = viewMatrix;
			Vector3 resultRot = Vector3::zero;

			// ビューマトリックスから座標と注視点を取得
			KOVECTOR eye = myVector::KOVectorSet(view._m41, view._m42, view._m43, 1.0f);
			KOVECTOR focus = myVector::KOVectorSet(view._m31 + view._m41, view._m32 + view._m42, view._m33 + view._m43, 1.0f);

			// ビューマトリックスの前方ベクトルからYaw（Y軸回りの回転角）とPitch（X軸回りの回転角）を求める
			KOVECTOR forward = myVector::KOVector3Normalize(focus - eye);
			resultRot._x = asinf(-myVector::KOVectorGetYF(forward));
			resultRot._y = atan2f(myVector::KOVectorGetXF(forward), myVector::KOVectorGetZF(forward));

			// ビューマトリックスの右方向ベクトルからRoll（Z軸回りの回転角）を求める
			KOVECTOR right = myVector::KOVector3Cross(myVector::KOVectorSet(0.0f, 1.0f, 0.0f, 0.0f), forward);
			right = myVector::KOVector3Normalize(right);
			resultRot._z = atan2f(myVector::KOVectorGetYF(right), myVector::KOVectorGetXF(right));

			return resultRot;
		}
#endif


		// forwardを求める
		// =============================================================
		const Vector3& CalculateForward(float yaw, float pitch) {
			float cosPitch = cosf(pitch);
			return Vector3(
				cosf(yaw) * cosPitch,
				sinf(pitch),
				sinf(yaw) * cosPitch
			);
		}

		// rightを求める
		// =============================================================
		const Vector3& CalculateRight(float yaw) {
			return Vector3(
				sinf(yaw),
				0.0f,
				-cosf(yaw)
			);
		}

		// upを求める
		// =============================================================
		const Vector3& CalculateUp(float yaw, float pitch, float roll) {
			float cosYaw = cosf(yaw);
			float sinYaw = sinf(yaw);
			float cosPitch = cosf(pitch);
			float sinPitch = sinf(pitch);
			float cosRoll = cosf(roll);
			float sinRoll = sinf(roll);

			return Vector3(
				-cosYaw * sinRoll - sinYaw * sinPitch * cosRoll,
				cosPitch * cosRoll,
				-sinYaw * sinRoll + cosYaw * sinPitch * cosRoll
			);
		}
	}
}