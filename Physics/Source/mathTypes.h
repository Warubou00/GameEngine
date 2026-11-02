/***********************************************************************************
* @file    mathTypes.h
* @brief   衝突判定で使う小さな数学 (ベクトル / 回転 / AABB)
* @details 全部 inline。DirectXMath は使わず、値の型 (float 3 つ) で書く
*          (AABB の判定や形の判定は分岐が多く、SIMD のレジスターに出し入れする方が遅くなりやすいため。
*           まとめて速くする所は C3 で計測して SIMD にする)
*
* @author  Keigo Onari
* @date    2026/11/02
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/
#pragma once
#ifndef PHYSICS_MATH_TYPES_H_
#define PHYSICS_MATH_TYPES_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <cmath>
#include <algorithm>

namespace Physics
{
	struct Float3
	{
		float x = 0.0f, y = 0.0f, z = 0.0f;
		Float3() = default;
		constexpr Float3(float x_, float y_, float z_) : x(x_), y(y_), z(z_) {}
	};

	inline Float3 operator+(Float3 a, Float3 b) { return { a.x + b.x, a.y + b.y, a.z + b.z }; }
	inline Float3 operator-(Float3 a, Float3 b) { return { a.x - b.x, a.y - b.y, a.z - b.z }; }
	inline Float3 operator-(Float3 a)           { return { -a.x, -a.y, -a.z }; }
	inline Float3 operator*(Float3 a, float s)  { return { a.x * s, a.y * s, a.z * s }; }
	inline Float3 operator*(float s, Float3 a)  { return { a.x * s, a.y * s, a.z * s }; }
	inline Float3 mul(Float3 a, Float3 b)       { return { a.x * b.x, a.y * b.y, a.z * b.z }; }
	inline float  dot(Float3 a, Float3 b)       { return a.x * b.x + a.y * b.y + a.z * b.z; }
	inline Float3 cross(Float3 a, Float3 b)     { return { a.y * b.z - a.z * b.y, a.z * b.x - a.x * b.z, a.x * b.y - a.y * b.x }; }
	inline float  lengthSq(Float3 a)            { return dot(a, a); }
	inline Float3 minOf(Float3 a, Float3 b)     { return { (std::min)(a.x, b.x), (std::min)(a.y, b.y), (std::min)(a.z, b.z) }; }
	inline Float3 maxOf(Float3 a, Float3 b)     { return { (std::max)(a.x, b.x), (std::max)(a.y, b.y), (std::max)(a.z, b.z) }; }
	inline Float3 absOf(Float3 a)               { return { std::fabs(a.x), std::fabs(a.y), std::fabs(a.z) }; }

	// 回転行列 (列 = ローカルの X / Y / Z 軸のワールドでの向き)
	struct Rotation3
	{
		Float3 axis[3] = { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };

		static Rotation3 FromQuaternion(float x, float y, float z, float w)
		{
			Rotation3 r;
			const float xx = x * x, yy = y * y, zz = z * z;
			const float xy = x * y, xz = x * z, yz = y * z, wx = w * x, wy = w * y, wz = w * z;
			r.axis[0] = { 1.0f - 2.0f * (yy + zz), 2.0f * (xy + wz),        2.0f * (xz - wy) };
			r.axis[1] = { 2.0f * (xy - wz),        1.0f - 2.0f * (xx + zz), 2.0f * (yz + wx) };
			r.axis[2] = { 2.0f * (xz + wy),        2.0f * (yz - wx),        1.0f - 2.0f * (xx + yy) };
			return r;
		}
		Float3 Apply(Float3 v) const { return axis[0] * v.x + axis[1] * v.y + axis[2] * v.z; }                 // ローカル → ワールド
		Float3 Inverse(Float3 v) const { return { dot(axis[0], v), dot(axis[1], v), dot(axis[2], v) }; }      // ワールド → ローカル
	};

	// 軸に沿った箱 (ワールド)
	struct Aabb
	{
		Float3 min;
		Float3 max;

		bool Overlaps(const Aabb& o) const
		{
			return min.x <= o.max.x && max.x >= o.min.x &&
			       min.y <= o.max.y && max.y >= o.min.y &&
			       min.z <= o.max.z && max.z >= o.min.z;
		}
		bool Contains(const Aabb& o) const
		{
			return min.x <= o.min.x && min.y <= o.min.y && min.z <= o.min.z &&
			       max.x >= o.max.x && max.y >= o.max.y && max.z >= o.max.z;
		}
		// 表面積 (BVH の木の組み方の目安)
		float SurfaceArea() const
		{
			const Float3 d = max - min;
			return 2.0f * (d.x * d.y + d.y * d.z + d.z * d.x);
		}
		static Aabb Union(const Aabb& a, const Aabb& b) { return { minOf(a.min, b.min), maxOf(a.max, b.max) }; }
	};
}

#endif // PHYSICS_MATH_TYPES_H_

/************************************
* End of file
************************************/
