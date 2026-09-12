/************************************************
* koMath.cpp
* 簡易的な数学関数提供
*
* 最終更新日:2023/12/26
* 作成者：Keigo Onari
*************************************************/

// =============================================================
// インクルード
// =============================================================
#include "math.h"

#include <DirectXMath.h>

namespace Engine
{
	namespace vector2
	{
		// Vector2からfloat型の長さの2乗を取得
		// =============================================================
		const float& LengthSq(const Vector2& vec)noexcept
		{
			return (vec.x * vec.x + vec.y * vec.y);
		}

		// Vector2の2点間の長さの2乗を取得
		// =============================================================
		const float& LengthSq(const Vector2& vec1, const Vector2& vec2)noexcept
		{
			Vector2 temp = vector2::Subtract(vec2, vec1);
			return vector2::LengthSq(temp);
		}

		// Vector2からfloat型の長さを取得
		// =============================================================
		const float& Length(const Vector2& vec)noexcept
		{
			return sqrtf(vec.x * vec.x + vec.y * vec.y);
		}

		// Vector2の2点間の距離を計算
		// =============================================================
		const float& Length(const Vector2& vec1, const Vector2& vec2)noexcept
		{
			Vector2 temp = vector2::Subtract(vec2, vec1);
			return vector2::Length(temp);
		}

		// Vector2から正規化したVector2型を取得
		// =============================================================
		const Vector2& Normalize(const Vector2& vec)noexcept
		{
			float length = vector2::Length(vec);
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
			float length = vector2::Length(v);
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
			float length = vector2::Length(v);
			if (length != 0)
				return v / length;
			return v;
		}

		// Vector2からfloat型の内積を取得
		// =============================================================
		const float& Dot(const Vector2& vec1, const Vector2& vec2)noexcept
		{
			return vec1.x * vec2.x + vec1.y * vec2.y;
		}

		// Vector2からVector2型の外積を取得
		// =============================================================
		const float& Cross(const Vector2& vec1, const Vector2& vec2)noexcept
		{
			return vec1.x * vec2.y - vec1.y * vec2.x;
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
			return Vector2(vec1.x + t * (vec2.x - vec1.x), vec1.y + t * (vec2.y - vec1.y));
		}
	}

	namespace vector3
	{
		// Vector3からfloat型の長さの2乗を取得
		// =============================================================
		const float& LengthSq(const Vector3& vec)noexcept
		{
			return (vec.x * vec.x + vec.y * vec.y + vec.z * vec.z);
		}

		// Vector3の2点間の長さの2乗を取得
		// =============================================================
		const float& LengthSq(const Vector3& vec1, const Vector3& vec2)noexcept
		{
			Vector3 temp = vector3::Subtract(vec2, vec1);
			return vector3::LengthSq(temp);
		}

		// Vector3からfloat型の長さを取得
		// =============================================================
		const float& Length(const Vector3& vec)noexcept
		{
			return sqrtf(vec.x * vec.x + vec.y * vec.y + vec.z * vec.z);
		}

		// Vector3の2点間の長さを取得
		// =============================================================
		const float& Length(const Vector3& vec1, const Vector3& vec2)noexcept
		{
			Vector3 temp = vector3::Subtract(vec2, vec1);
			return vector3::Length(temp);
		}

		// Vector3から正規化したVector3型を取得
		// =============================================================
		const Vector3& Normalize(const Vector3& vec)noexcept
		{
			float length = vector3::Length(vec);
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
			float length = vector3::Length(v);
			if (length != 0)
				return v / length;
			return v;
		}

		// Vector3からfloat型の内積を取得
		// =============================================================
		const float& Dot(const Vector3& vec1, const Vector3& vec2)noexcept
		{
			return (vec1.x * vec2.x + vec1.y * vec2.y + vec1.z * vec2.z);
		}

		// Vector3からVector3型の外積を取得
		// =============================================================
		const Vector3& Cross(const Vector3& vec1, const Vector3& vec2)noexcept
		{
			Vector3 v;

			v.x = vec1.y * vec2.z - vec1.z * vec2.y;
			v.y = vec1.z * vec2.x - vec1.x * vec2.z;
			v.z = vec1.x * vec2.y - vec1.y * vec2.x;

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
			return Vector2(vec1.x + t * (vec2.x - vec1.x), vec1.y + t * (vec2.y - vec1.y));
		}

		// Vector3間を線形補完してVector3型として返却
		// =============================================================
		const Vector3& Vec3Lerp(const Vector3& vec1, const Vector3& vec2, float t)noexcept
		{
			return Vector3(vec1.x + t * (vec2.x - vec1.x), vec1.y + t * (vec2.y - vec1.y), vec1.z + t * (vec2.z - vec1.z));
		}
	}

	namespace vector4
	{
		// Vector4からfloat型の長さの2乗を取得
		// =============================================================
		const float& LengthSq(const Vector4& vec)noexcept
		{
			return (vec.x * vec.x + vec.y * vec.y + vec.z * vec.z + vec.w * vec.w);
		}

		// Vector4の2点間の長さの2乗を取得
		// =============================================================
		const float& LengthSq(const Vector4& vec1, const Vector4& vec2)noexcept
		{
			Vector4 temp = vector4::Subtract(vec2, vec1);
			return vector4::LengthSq(temp);
		}

		// Vector4からfloat型の長さを取得
		// =============================================================
		const float& Length(const Vector4& vec)noexcept
		{
			return sqrtf(vec.x * vec.x + vec.y * vec.y + vec.z * vec.z + vec.w * vec.w);
		}

		// Vector4の2点間の長さを取得
		// =============================================================
		const float& Length(const Vector4& vec1, const Vector4& vec2)noexcept
		{
			Vector4 temp = vector4::Subtract(vec2, vec1);
			return vector4::Length(temp);
		}

		// Vector4から正規化したVector4型を取得
		// =============================================================
		const Vector4& Normalize(const Vector4& vec)noexcept
		{
			float length = vector4::Length(vec);
			if (length != 0)
				return vec / length;
			return vec;
		}

		// Vector4からfloat型の内積を取得
		// =============================================================
		const float& Dot(const Vector4& vec1, const Vector4 vec2)noexcept
		{
			return (vec1.x * vec2.x + vec1.y * vec2.y + vec1.z * vec2.z + vec1.w * vec2.w);
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
			return Vector2(vec1.x + t * (vec2.x - vec1.x), vec1.y + t * (vec2.y - vec1.y));
		}

		// Vector4間を線形補完してVector3型として返却
		// =============================================================
		const Vector3& Vec3Lerp(const Vector4& vec1, const Vector4& vec2, float t)noexcept
		{
			return Vector3(vec1.x + t * (vec2.x - vec1.x), vec1.y + t * (vec2.y - vec1.y), vec1.z + t * (vec2.z - vec1.z));
		}

		// Vector4間を線形補完してVector3型として返却
		// =============================================================
		const Vector4& Vec4Lerp(const Vector4& vec1, const Vector4& vec2, float t)noexcept
		{
			return Vector4(vec1.x + t * (vec2.x - vec1.x), vec1.y + t * (vec2.y - vec1.y), vec1.z + t * (vec2.z - vec1.z), vec1.w + t * (vec2.w - vec1.w));
		}
	}

	namespace color
	{
		// 基本的なグレースケールの色を取得
		// =============================================================
		const Color& KOColorGrayScale(const Color& color)noexcept
		{
			Color temp;
			temp = (color.a * 0.2126f + color.g * 0.7152f + color.b * 0.0722f) / 3;
			return temp;
		}

		// Rを基調としたグレースケールの色を取得
		// =============================================================
		const Color& KOColorGrayScaleR(const Color& color)noexcept
		{
			return Color(color.r, color.r, color.r, color.a);
		}

		// Gを基調としたグレースケールの色を取得
		// =============================================================
		const Color& KOColorGrayScaleG(const Color& color)noexcept
		{
			return Color(color.g, color.g, color.g, color.a);
		}

		// Bを基調としたグレースケールの色を取得
		// =============================================================
		const Color& KOColorGrayScaleB(const Color& color)noexcept
		{
			return Color(color.b, color.b, color.b, color.a);
		}

		// R,G,Bをそれぞれ反転した色を取得
		// =============================================================
		const Color& KOColorNegapoji(const Color& color)noexcept
		{
			Color temp = color;
			for (int index = 0; index < 4; ++index)
			{
				temp.color[index] = 1.0f - temp.color[index];
			}

			return math::ColorClamp(temp, Color::black, Color::white);
		}

		// セピア調の色を取得
		// =============================================================
		const Color& KOColorSepia(const Color& color)noexcept
		{
			float Y = 0.299f * color.r + 0.587f * color.g + 0.114f * color.b;

			// セピア調ではモノクロ化とは違い、R,G,Bに明るさをそのまま代入はしない
			// 今回の実装では、赤みの成分に0.9、緑に0.7、bに0.4の重みを乗算している
			Color temp = Color();
			temp.r = Y * 0.9f;
			temp.g = Y * 0.7f;
			temp.b = Y * 0.4f;

			return temp;
		}

		// モノクロ調の色を取得
		// =============================================================
		const Color& KOColorMonocrome(const Color& color)noexcept
		{
			// 画像をモノクロに変化させていく
			// ピクセルの明るさを計算する
			float Y = 0.299f * color.r + 0.587 * color.g + 0.114f * color.b;

			Color temp = Color();
			temp = Y;

			return temp;
		}

		// Rの値のみ取得
		// =============================================================
		const Color& KOColorRScale(const Color& color)noexcept
		{
			return Color(color.r, 0.0f, 0.0f, color.a);
		}

		// Gの値のみ取得
		// =============================================================
		const Color& KOColorGScale(const Color& color)noexcept
		{
			return Color(0.0f, color.g, 0.0f, color.a);
		}

		// Bの値のみ取得
		// =============================================================
		const Color& KOCOlorBScale(const Color& color)noexcept
		{
			return Color(0.0f, 0.0f, color.b, color.a);
		}

		// 0.0f～1.0fの値に収めて取得
		// =============================================================
		const Color& KOColorSaturate(const Color& color)noexcept
		{
			return math::ColorClamp(color, Color::white, Color::black);
		}
	}

	namespace vectorEx
	{
		// KOVECTORを返り値に引数をセット
		// =============================================================
		const VectorEx& KOVectorSet(const float& X, const float& Y, const float& Z, const float& W)noexcept
		{
			return VectorEx(X, Y, Z, W);
		}

		// KOVECTORから_xFloat,_yFloatを取得しfloat型で長さを取得
		// =============================================================
		const float& KOVector2Length(const VectorEx& vec)noexcept
		{
			return sqrtf(vec.xFloat * vec.xFloat + vec.yFloat * vec.yFloat);
		}

		// KOVECTORから_xFloat,_yFloat,_zFloatを取得しfloat型で長さを取得
		// =============================================================
		const float& KOVector3Length(const VectorEx& vec)noexcept
		{
			return sqrtf(vec.xFloat * vec.xFloat + vec.yFloat * vec.yFloat + vec.zFloat * vec.zFloat);
		}

		// KOVECTORから値全てを取得しfloat型で長さを取得
		// =============================================================
		const float& KOVector4Length(const VectorEx& vec)noexcept
		{
			return sqrtf(vec.xFloat * vec.xFloat + vec.yFloat * vec.yFloat + vec.zFloat * vec.zFloat + vec.wFloat * vec.wFloat);
		}

		// KOVECTORから_xFloat,_yFloatを取得しKOVECTOR型で正規化を取得
		// =============================================================
		const VectorEx& KOVector2Normalize(const VectorEx& vec)noexcept
		{
			float length = vectorEx::KOVector2Length(vec);
			if (length != 0)
				return vec / length;
			return vec;
		}

		// KOVECTORから_xFloat,_yFloat, _zFloatを取得しKOVECTOR型で正規化を取得
		// =============================================================
		const VectorEx& KOVector3Normalize(const VectorEx& vec)noexcept
		{
			float length = vectorEx::KOVector3Length(vec);
			if (length != 0)
				return vec / length;
			return vec;
		}

		// KOVECTORから値全てを取得しKOVECTOR型で正規化を取得
		// =============================================================
		const VectorEx& KOVector4Normalize(const VectorEx& vec)noexcept
		{
			float length = vectorEx::KOVector4Length(vec);
			if (length != 0)
				return vec / length;
			return vec;
		}

		// 2値のKOVECTORからvec1からvec2を引いた差分をKOVECTOR型で取得
		// =============================================================
		const VectorEx& KOVectorSubtract(const VectorEx& vec1, const VectorEx& vec2)noexcept
		{
			return vec1 - vec2;
		}

		// KOVECTORから_xFloat,_yFloatを取得しKOVECTOR型で内積を取得
		// =============================================================
		const VectorEx& KOVector2Dot(const VectorEx& vec1, const VectorEx& vec2)noexcept
		{
			float t = vec1.xFloat * vec2.xFloat + vec1.yFloat * vec2.yFloat;
			VectorEx vec;
			vec = t;
			return vec;
		}

		// KOVECTORから_xFloat,_yFloat,_zFloatを取得しKOVECTOR型で内積を取得
		// =============================================================
		const VectorEx& KOVector3Dot(const VectorEx& vec1, const VectorEx& vec2)noexcept
		{
			float t = vec1.xFloat * vec2.xFloat + vec1.yFloat * vec2.yFloat + vec1.zFloat * vec2.zFloat;
			VectorEx vec;
			vec = t;
			return vec;
		}

		// KOVECTORから全ての値を取得しKOVECTOR型で内積を取得
		// =============================================================
		const VectorEx& KOVector4Dot(const VectorEx& vec1, const VectorEx& vec2)noexcept
		{
			float t = vec1.xFloat * vec2.xFloat + vec1.yFloat * vec2.yFloat + vec1.zFloat * vec2.zFloat + vec1.wFloat * vec2.wFloat;
			VectorEx vec;
			vec = t;
			return vec;
		}

		// KOVECTORから_xFloat,_yFloatを取得しKOVECTOR型で外積を取得
		// =============================================================
		const VectorEx& KOVector2Cross(const VectorEx& vec1, const VectorEx& vec2)noexcept
		{
			VectorEx vec;
			float t = vec1.xFloat * vec2.yFloat - vec1.yFloat * vec2.xFloat;
			vec = t;
			return vec;
		}

		// KOVECTORから_xFloat,_yFloat,_zFloatを取得しKOVECTOR型で外積を取得
		// =============================================================
		const VectorEx& KOVector3Cross(const VectorEx& vec1, const VectorEx& vec2)noexcept
		{
			VectorEx v;

			v.xFloat = vec1.yFloat * vec2.zFloat - vec1.zFloat * vec2.yFloat;
			v.yFloat = vec1.zFloat * vec2.xFloat - vec1.xFloat * vec2.zFloat;
			v.zFloat = vec1.xFloat * vec2.yFloat - vec1.yFloat * vec2.xFloat;

			return v;
		}

		// KOVECTORから全値を取得しKOVECTOR型で外積を取得
		// =============================================================
		const VectorEx& KOVector4Cross(const VectorEx& vec1, const VectorEx& vec2, const VectorEx& vec3)noexcept
		{
			VectorEx v;

			v.xFloat = vec1.yFloat * (vec2.zFloat * vec3.wFloat - vec3.zFloat * vec2.wFloat)
				- vec1.zFloat * (vec2.yFloat * vec3.wFloat - vec3.yFloat * vec2.wFloat)
				+ vec1.wFloat * (vec2.yFloat * vec3.zFloat - vec3.yFloat * vec2.zFloat);

			v.yFloat = vec1.xFloat * (vec3.zFloat * vec2.wFloat - vec2.zFloat * vec3.wFloat)
				- vec1.zFloat * (vec3.xFloat * vec2.wFloat - vec2.xFloat * vec3.wFloat)
				+ vec1.wFloat * (vec3.xFloat * vec2.zFloat - vec2.xFloat * vec3.zFloat);

			v.zFloat = vec1.xFloat * (vec2.yFloat * vec3.wFloat - vec3.yFloat * vec2.wFloat)
				- vec1.yFloat * (vec2.xFloat * vec3.wFloat - vec3.xFloat * vec2.wFloat)
				+ vec1.wFloat * (vec2.xFloat * vec3.yFloat - vec3.xFloat * vec2.yFloat);

			v.wFloat = vec1.xFloat * (vec3.yFloat * vec2.zFloat - vec2.yFloat * vec3.zFloat)
				- vec1.yFloat * (vec3.xFloat * vec2.zFloat - vec2.xFloat * vec3.zFloat)
				+ vec1.zFloat * (vec3.xFloat * vec2.yFloat - vec2.xFloat * vec3.yFloat);

			return v;
		}

		// KOVECTORからX成分をfloat型で取得
		// =============================================================
		const float& KOVectorGetXF(const VectorEx& vec)noexcept
		{
			return vec.xFloat;
		}

		// KOVECTORからY成分をfloat型で取得
		// =============================================================
		const float& KOVectorGetYF(const VectorEx& vec)noexcept
		{
			return vec.yFloat;
		}

		// KOVECTORからZ成分をfloat型で取得
		// =============================================================
		const float& KOVectorGetZF(const VectorEx& vec)noexcept
		{
			return vec.zFloat;
		}

		// KOVECTORからZ成分をfloat型で取得
		// =============================================================
		const float& KOVectorGetWF(const VectorEx& vec)noexcept
		{
			return vec.wFloat;
		}

		// KOVECTORから否定を計算(DirectX::XMVectorNegate()に準拠)
		// =============================================================
		const VectorEx& KOVectorNegate(const VectorEx& vec)noexcept
		{
			__m128 changeVec;
			__m128 temp;

			for (int index = 0; index < 4; ++index)
			{
				changeVec.m128_f32[index] = vec.vec[index];
			}

			// SIMD命令
			temp = _mm_setzero_ps();

			__m128 resultM = _mm_sub_ps(temp, changeVec);

			VectorEx resultVec;
			for (int index = 0; index < 4; ++index)
			{
				resultVec.vec[index] = resultM.m128_f32[index];
			}

			return resultVec;
		}

		// Vector4から否定を計算(DirectX::XMVectorNegate()に準拠)
		// =============================================================
		const VectorEx& KOVectorNegate(const Vector4& vec)noexcept
		{
			VectorEx v;
			v = vec;
			v = vectorEx::KOVectorNegate(v);
			return v;
		}

		// まぁとりあえずすげぇ関数(DirectX::XMVectorSelect()に準拠)
		// =============================================================
		const VectorEx& KOVectorSelect(const VectorEx& vec1, const VectorEx& vec2, const VectorEx& controllVec)noexcept
		{
			__m128 tempVec1;
			__m128 tempVec2;
			__m128 tempControllVec;

			for (int index = 0; index < 4; ++index)
			{
				tempVec1.m128_f32[index] = vec1.vec[index];
				tempVec2.m128_f32[index] = vec2.vec[index];
				tempControllVec.m128_u32[index] = controllVec.vecInt[index];
			}

			// SIMD命令
			__m128 m1 = _mm_andnot_ps(tempControllVec, tempVec1);
			__m128 m2 = _mm_and_ps(tempVec2, tempControllVec);
			__m128 m3 = _mm_or_ps(m1, m2);

			VectorEx resultVec;
			for (int index = 0; index < 4; ++index)
			{
				resultVec.vec[index] = m3.m128_f32[index];
			}
			return resultVec;
		}

		// 全要素が等しいか検出
		// =============================================================
		const bool& KOVector4Equal(VectorEx& vec1, VectorEx& vec2)noexcept
		{
			return vec1 == vec2;
		}

		// 3要素が等しいか検出
		// =============================================================
		const bool& KOVector3Equal(const VectorEx& vec1, const VectorEx& vec2)noexcept
		{
			Vector3 v1, v2;
			v1 = vec1;
			v2 = vec2;

			return v1 == v2;
		}

		// 2要素が等しいか検出
		// =============================================================
		const bool& KOVector2Equal(const VectorEx& vec1, const VectorEx& vec2)noexcept
		{
			Vector2 v1, v2;
			v1 = vec1;
			v2 = vec2;

			return v1 == v2;
		}

		// Vector3型からVector3型の正規化ベクトルを取得
		// =============================================================
		const Vector3& KOVector3Normalize(const Vector3& vec)noexcept
		{
			Vector3 v;
			v = vector3::Normalize(vec);
			return v;
		}

		// Vector4型からVector3型の正規化ベクトルを取得
		// =============================================================
		const Vector3& KOVector3Normalize(const Vector4& vec)noexcept
		{
			Vector3 v;
			v = vector3::Normalize(vec);
			return v;
		}

		// Vector4型からVector4型の正規化ベクトルを取得
		// =============================================================
		const Vector4& KOVector4Normalize(const Vector4& vec)noexcept
		{
			VectorEx v;
			v = vec;
			v = vectorEx::KOVector4Normalize(v);

			Vector4 v4;
			v4.x = v.xFloat;
			v4.y = v.yFloat;
			v4.z = v.zFloat;
			v4.w = v.wFloat;

			return v4;
		}

		// Vector3型からVector4型の正規化ベクトルを取得
		// =============================================================
		const Vector4& KOVector4Normalize(const Vector3& vec)noexcept
		{
			VectorEx v;
			v = vec;
			v = vectorEx::KOVector4Normalize(v);

			Vector4 v4;
			v4.x = v.xFloat;
			v4.y = v.yFloat;
			v4.z = v.zFloat;
			v4.w = v.wFloat;
			return v4;
		}

		// Vector4同士の差分をvector4型で取得
		// =============================================================
		const Vector4& KOVectorSubtract(const Vector4& vec1, const Vector4& vec2)noexcept
		{
			return vec1 - vec2;
		}

		// Vector4同士の差分をVector3型で取得
		// =============================================================
		const Vector3& KOVectorSubtractVec3(const Vector4& vec1, const Vector4& vec2)noexcept
		{
			Vector3 v1, v2, v3;
			v1 = vec1;
			v2 = vec2;
			v3 = v1 - v2;
			return v3;
		}

		// Vector3同士でVector3の内積を取得
		// =============================================================
		const Vector3& KOVector3Dot(const Vector3& vec1, const Vector3& vec2)noexcept
		{
			VectorEx v1, v2;
			v1 = vec1;
			v2 = vec2;
			v1 = vectorEx::KOVector3Dot(v1, v2);

			Vector3 v3;
			v3.x = v1.xFloat;
			v3.y = v1.yFloat;
			v3.z = v1.zFloat;
			return v3;
		}

		// Vector4同士でVector3の内積を取得
		// =============================================================
		const Vector3& KOVector3Dot(const Vector4& vec1, const Vector4& vec2)noexcept
		{
			VectorEx v1, v2;
			v1 = vec1;
			v2 = vec2;
			v1 = vectorEx::KOVector3Dot(v1, v2);

			Vector3 v3;
			v3.x = v1.xFloat;
			v3.y = v1.yFloat;
			v3.z = v1.zFloat;
			return v3;
		}

		// Vector4同士でVector4の内積を取得
		// =============================================================
		const Vector4& KOVector4Dot(const Vector4& vec1, const Vector4& vec2)noexcept
		{
			VectorEx v1, v2;
			v1 = vec1;
			v2 = vec2;
			v1 = vectorEx::KOVector4Dot(v1, v2);

			Vector4 v4;
			v4.x = v1.xFloat;
			v4.y = v1.yFloat;
			v4.z = v1.zFloat;
			v4.w = v1.wFloat;

			return v4;
		}

		// vector3同士でvector4の内積を取得
		// =============================================================
		const Vector4& KOVector4Dot(const Vector3& vec1, const Vector3& vec2)noexcept
		{
			VectorEx v1, v2;
			v1 = vec1;
			v2 = vec2;
			v1 = vectorEx::KOVector4Dot(v1, v2);

			Vector4 v4;
			v4.x = v1.xFloat;
			v4.y = v1.yFloat;
			v4.z = v1.zFloat;
			v4.w = v1.wFloat;

			return v4;
		}

		// Vector3同士でVector3の外積を取得
		// =============================================================
		const Vector3& KOVector3Cross(const Vector3& vec1, const Vector3& vec2)noexcept
		{
			VectorEx v1, v2;
			v1 = vec1;
			v2 = vec2;

			v1 = vectorEx::KOVector3Cross(v1, v2);

			Vector3 v3;
			v3.x = v1.xFloat;
			v3.y = v1.yFloat;
			v3.z = v1.zFloat;
			return v3;
		}

		// Vector4同士でVector3の外積を取得
		// =============================================================
		const Vector3& KOVector3Cross(const Vector4& vec1, const Vector4& vec2)noexcept
		{
			VectorEx v1, v2;
			v1 = vec1;
			v2 = vec2;

			v1 = vectorEx::KOVector3Cross(v1, v2);

			Vector3 v3;
			v3.x = v1.xFloat;
			v3.y = v1.yFloat;
			v3.z = v1.zFloat;
			return v3;
		}

		// Vector4型3つでVector4型の外積を取得
		// =============================================================
		const Vector4& KOVector4Cross(const Vector4& vec1, const Vector4& vec2, const Vector4& vec3)noexcept
		{
			VectorEx v1, v2, v3;
			v1 = vec1;
			v2 = vec2;
			v3 = vec3;
			v1 = vectorEx::KOVector4Cross(v1, v2, v3);

			Vector4 v4;
			v4.x = v1.xFloat;
			v4.y = v1.yFloat;
			v4.z = v1.zFloat;
			v4.w = v1.wFloat;

			return v4;
		}

		// Vector3型3つでVector4型の外積を取得
		// =============================================================
		const Vector4& KOVector4Cross(const Vector3& vec1, const Vector3& vec2, const Vector3& vec3)noexcept
		{
			VectorEx v1, v2, v3;
			v1 = vec1;
			v2 = vec2;
			v3 = vec3;
			v1 = vectorEx::KOVector4Cross(v1, v2, v3);

			Vector4 v4;
			v4.x = v1.xFloat;
			v4.y = v1.yFloat;
			v4.z = v1.zFloat;
			v4.w = v1.wFloat;

			return v4;
		}

		// 3要素が等しいか検出
		// =============================================================
		const bool& KOVector3Equal(Vector4& vec1, Vector4& vec2)noexcept
		{
			Vector3 v1, v2;
			v1 = vec1;
			v2 = vec2;
			return v1 == v2;
		}

		// 2要素が等しいか検出
		// =============================================================
		const bool& KOVector2Equal(Vector4& vec1, Vector4& vec2)noexcept
		{
			Vector2 v1, v2;
			v1 = vec1;
			v2 = vec2;
			return v1 == v2;
		}

		// 2要素が等しいか検出
		// =============================================================
		const bool& KOVector2Equal(Vector3& vec1, Vector3& vec2)noexcept
		{
			Vector2 v1, v2;
			v1 = vec1;
			v2 = vec2;
			return v1 == v2;
		}
	}

	namespace matrix
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
			return matrix::KOMatrixScaling(scale.x, scale.y, scale.z);
		}

		// 回転行列を生成
		// =============================================================
		const Matrix& KOMatrixYawPitchRoll(float rotX, float rotY, float rotZ)noexcept
		{
			Matrix result;

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
			return matrix::KOMatrixYawPitchRoll(rot.x, rot.y, rot.z);
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
			return matrix::KOMatrixTranslation(offset.x, offset.y, offset.z);
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
		const Matrix& KOMatrixLookToLH(const VectorEx& eyePosition, const VectorEx& eyeDirection, const VectorEx& upDirection)noexcept
		{
			bool item = !vectorEx::KOVector3Equal(eyeDirection, VectorEx::zero);
			VectorExAssert(item, "eyeDirection has become all 0.0f.", "CreateViewMatrix");
			if (!item)
			{
				return Matrix::allminus;
			}

			item = !vectorEx::KOVector3Equal(upDirection, VectorEx::zero);
			VectorExAssert(item, "upDirection has become all 0.0f.", "CreateViewMatrix");
			if (!item)
			{
				return Matrix::allminus;
			}

			VectorEx R2 = vectorEx::KOVector3Normalize(eyeDirection);

			VectorEx R0 = vectorEx::KOVector3Cross(upDirection, R2);
			R0 = vectorEx::KOVector3Normalize(R0);

			VectorEx R1 = vectorEx::KOVector3Cross(R2, R0);

			VectorEx NegEyePosition = vectorEx::KOVectorNegate(eyePosition);

			VectorEx D0 = vectorEx::KOVector3Dot(R0, NegEyePosition);
			VectorEx D1 = vectorEx::KOVector3Dot(R1, NegEyePosition);
			VectorEx D2 = vectorEx::KOVector3Dot(R2, NegEyePosition);

			VectorEx tempSelect1110;
			tempSelect1110.vecInt[0] = 0xFFFFFFFF;
			tempSelect1110.vecInt[1] = 0xFFFFFFFF;
			tempSelect1110.vecInt[2] = 0xFFFFFFFF;
			tempSelect1110.vecInt[3] = 0;

			Matrix m;
			m._koVec[0] = vectorEx::KOVectorSelect(D0, R0, tempSelect1110);
			m._koVec[1] = vectorEx::KOVectorSelect(D1, R1, tempSelect1110);
			m._koVec[2] = vectorEx::KOVectorSelect(D2, R2, tempSelect1110);
			m._koVec[3] = vectorEx::KOVectorSet(0.0f, 0.0f, 0.0f, 1.0f);

			m = KOMatrixTranspose(m);

			return m;
		}

		// Vector4から視点の向きを利用した左手座標系ビュー行列を生成
		// =============================================================
		const Matrix& KOMatrixLookToLH(const Vector4& eyePosition, const Vector4& eyeDirection, const Vector4& upDirection)noexcept
		{
			VectorEx tempEyePosition;
			VectorEx tempEyeDirection;
			VectorEx tempUpDirection;
			tempEyePosition = eyePosition;
			tempEyeDirection = eyeDirection;
			tempUpDirection = upDirection;
			return matrix::KOMatrixLookToLH(tempEyePosition, tempEyeDirection, tempUpDirection);
		}

		// KOVECTORから注視点を利用した左手座標系ビュー行列を生成
		// =============================================================
		const Matrix& KOMatrixLookAtLH(const VectorEx& eyePosition, const VectorEx& focusPosition, const VectorEx& upDirection)noexcept
		{
			VectorEx eyeDirection = vectorEx::KOVectorSubtract(focusPosition, eyePosition);
			return matrix::KOMatrixLookToLH(eyePosition, eyeDirection, upDirection);
		}

		// Vector4から注視点を利用した左手座標系ビュー行列を生成
		// =============================================================
		const Matrix& KOMatrixLookAtLH(const Vector4& eyePosition, const Vector4& focusPosition, const Vector4& upDirection)noexcept
		{
			Vector4 eyeDirection = vectorEx::KOVectorSubtract(focusPosition, eyePosition);
			return matrix::KOMatrixLookToLH(eyePosition, eyeDirection, upDirection);
		}

		// KOVECTORから視点の向きを利用した右手座標系ビュー行列を生成
		// =============================================================
		const Matrix& KOMatrixLookToRH(const VectorEx& eyePosition, const VectorEx& eyeDirection, const VectorEx& upDirection)noexcept
		{
			VectorEx NegEyeDirection = vectorEx::KOVectorNegate(eyeDirection);
			return matrix::KOMatrixLookToLH(eyePosition, NegEyeDirection, upDirection);
		}

		// KOVECTORから視点の向きを利用した右手座標系ビュー行列を生成
		// =============================================================
		const Matrix& KOMatrixLookToRH(const Vector4& eyePosition, const Vector4& eyeDirection, const Vector4& upDirection)noexcept
		{
			VectorEx tempEyePosition;
			VectorEx tempEyeDirection;
			VectorEx tempUpDirection;
			tempEyePosition = eyePosition;
			tempEyeDirection = eyeDirection;
			tempUpDirection = upDirection;
			return matrix::KOMatrixLookToRH(tempEyePosition, tempEyeDirection, tempUpDirection);
		}

		// KOVECTORから注視点を利用した右手座標系ビュー行列を生成
		// =============================================================
		const Matrix& KOMatrixLookAtRH(const VectorEx& eyePosition, const VectorEx& focusPosition, const VectorEx& upDirection)noexcept
		{
			VectorEx NegEyeDirection = vectorEx::KOVectorSubtract(eyePosition, focusPosition);
			return matrix::KOMatrixLookToLH(eyePosition, NegEyeDirection, upDirection);
		}

		// Vector4から注視点を利用した右手座標系ビュー行列を生成
		// =============================================================
		const Matrix& KOMatrixLookAtRH(const Vector4& eyePosition, const Vector4& focusPosition, const Vector4& upDirection)noexcept
		{
			VectorEx tempEyePosition;
			VectorEx tempFocusPosition;
			VectorEx tempUpDirection;
			tempEyePosition = eyePosition;
			tempFocusPosition = focusPosition;
			tempUpDirection = upDirection;
			return matrix::KOMatrixLookAtRH(tempEyePosition, tempFocusPosition, tempUpDirection);
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

	namespace math
	{
		// 各要素をmin～maxに収めて取得
		// =============================================================
		const Color& ColorClamp(const Color& color, const Color& min, const Color& max)noexcept
		{
			return math::ColorMax(min, math::ColorMin(color, max));
		}

		// 各要素の小さい値の方で色を取得
		// 同値はcolor1の要素を返却
		// =============================================================
		const Color& ColorMin(const Color& color1, const Color& color2)noexcept
		{
			return Color(
				color1.r <= color2.r ? color1.r : color2.r,
				color1.g <= color2.g ? color1.g : color2.g,
				color1.b <= color2.b ? color1.b : color2.b,
				color1.a <= color2.a ? color1.a : color2.a
			);
		}

		// 各要素の大きい値の方で色を取得
		// 同値はcolor1の要素を返却
		// =============================================================
		const Color& ColorMax(const Color& color1, const Color& color2)noexcept
		{
			return Color(
				color1.r >= color2.r ? color1.r : color2.r,
				color1.g >= color2.g ? color1.g : color2.g,
				color1.b >= color2.b ? color1.b : color2.b,
				color1.a >= color2.a ? color1.a : color2.a
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
				vec4_1.x <= vec4_2.x ? vec4_1.x : vec4_2.x,
				vec4_1.y <= vec4_2.y ? vec4_1.y : vec4_2.y,
				vec4_1.z <= vec4_2.z ? vec4_1.z : vec4_2.z,
				vec4_1.w <= vec4_2.w ? vec4_1.w : vec4_2.w
				);
		}

		// 各要素の大きい値の方で色を取得
		// 同値はcolor1の要素を返却
		// =============================================================
		const Vector4& Vec4Max(const Vector4& vec4_1, const Vector4& vec4_2)noexcept
		{
			return Vector4(
				vec4_1.x >= vec4_2.x ? vec4_1.x : vec4_2.x,
				vec4_1.y >= vec4_2.y ? vec4_1.y : vec4_2.y,
				vec4_1.z >= vec4_2.z ? vec4_1.z : vec4_2.z,
				vec4_1.w >= vec4_2.w ? vec4_1.w : vec4_2.w
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

		// viewMatrixからカメラの回転角を求める
		// =============================================================
		const Vector3& ExtractRotationAnglesFromViewMatrix(const Matrix& viewMatrix) {
			
			Matrix view = viewMatrix;
			Vector3 resultRot = Vector3::zero;

			// ビューマトリックスから座標と注視点を取得
			VectorEx eye = vectorEx::KOVectorSet(view._m41, view._m42, view._m43, 1.0f);
			VectorEx focus = vectorEx::KOVectorSet(view._m31 + view._m41, view._m32 + view._m42, view._m33 + view._m43, 1.0f);

			// ビューマトリックスの前方ベクトルからYaw（Y軸回りの回転角）とPitch（X軸回りの回転角）を求める
			VectorEx forward = vectorEx::KOVector3Normalize(focus - eye);
			resultRot.x = asinf(-vectorEx::KOVectorGetYF(forward));
			resultRot.y = atan2f(vectorEx::KOVectorGetXF(forward), vectorEx::KOVectorGetZF(forward));

			// ビューマトリックスの右方向ベクトルからRoll（Z軸回りの回転角）を求める
			VectorEx right = vectorEx::KOVector3Cross(vectorEx::KOVectorSet(0.0f, 1.0f, 0.0f, 0.0f), forward);
			right = vectorEx::KOVector3Normalize(right);
			resultRot.z = atan2f(vectorEx::KOVectorGetYF(right), vectorEx::KOVectorGetXF(right));

			return resultRot;
		}


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