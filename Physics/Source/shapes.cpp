/***********************************************************************************
* @file    shapes.cpp
* @brief   形の箱 (AABB) / 重なりの判定。説明は shapes.h
*
* @author  Keigo Onari
* @date    2026/11/02
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/

/******************************************************************
* Include Headers
******************************************************************/
#include "shapes.h"

namespace Physics
{
	namespace
	{
		// カプセルの芯の線分の両端
		void capsuleSegment(const WorldShape& s, Float3& p0, Float3& p1)
		{
			const Float3 up = s.Rotation.axis[1] * s.HalfHeight;
			p0 = s.Center - up;
			p1 = s.Center + up;
		}

		// 点 p と線分 [a, b] の一番近い点
		Float3 closestOnSegment(Float3 p, Float3 a, Float3 b)
		{
			const Float3 ab = b - a;
			const float  len = lengthSq(ab);
			if (len <= 1e-12f) return a;
			const float t = std::clamp(dot(p - a, ab) / len, 0.0f, 1.0f);
			return a + ab * t;
		}

		// 2 つの線分の一番近い点どうしの距離² (Real-Time Collision Detection 5.1.9)
		float segmentSegmentDistanceSq(Float3 p1, Float3 q1, Float3 p2, Float3 q2)
		{
			const Float3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
			const float a = lengthSq(d1), e = lengthSq(d2), f = dot(d2, r);
			float s = 0.0f, t = 0.0f;
			if (a <= 1e-12f && e <= 1e-12f) return lengthSq(r);
			if (a <= 1e-12f)
			{
				t = std::clamp(f / e, 0.0f, 1.0f);
			}
			else
			{
				const float c = dot(d1, r);
				if (e <= 1e-12f)
				{
					s = std::clamp(-c / a, 0.0f, 1.0f);
				}
				else
				{
					const float b = dot(d1, d2);
					const float denom = a * e - b * b;
					s = denom != 0.0f ? std::clamp((b * f - c * e) / denom, 0.0f, 1.0f) : 0.0f;
					t = (b * s + f) / e;
					if (t < 0.0f)      { t = 0.0f; s = std::clamp(-c / a, 0.0f, 1.0f); }
					else if (t > 1.0f) { t = 1.0f; s = std::clamp((b - c) / a, 0.0f, 1.0f); }
				}
			}
			return lengthSq((p1 + d1 * s) - (p2 + d2 * t));
		}

		// 箱の中で p に一番近い点
		Float3 closestOnBox(Float3 p, const WorldShape& box)
		{
			const Float3 local = box.Rotation.Inverse(p - box.Center);
			const Float3 clamped(std::clamp(local.x, -box.HalfExtents.x, box.HalfExtents.x),
			                     std::clamp(local.y, -box.HalfExtents.y, box.HalfExtents.y),
			                     std::clamp(local.z, -box.HalfExtents.z, box.HalfExtents.z));
			return box.Center + box.Rotation.Apply(clamped);
		}

		// ==============================================================
		// GJK (Gilbert-Johnson-Keerthi) : A と B の差 (A の点 - B の点 の集まり) が原点を含めば重なっている
		//   1. 差の形の「方向 d に一番遠い点」を足していき、原点を囲む単体 (点 / 線 / 三角形 / 四面体) を作ろうとする
		//   2. 新しく足した点が原点の向こうに届かなければ、原点は差の形の外 = 重なっていない
		//   3. 四面体が原点を囲めば重なっている
		//   単体の更新は、原点に一番近い部分だけを残して、次に探す方向をそこから原点へ向ける
		// ==============================================================
		struct Simplex
		{
			Float3 p[4];
			int    count = 0;
		};

		bool sameDirection(Float3 a, Float3 b) { return dot(a, b) > 0.0f; }

		bool line(Simplex& s, Float3& d)
		{
			const Float3 a = s.p[0], b = s.p[1];
			const Float3 ab = b - a, ao = -a;
			if (sameDirection(ab, ao))
			{
				d = cross(cross(ab, ao), ab);
				if (lengthSq(d) < 1e-12f) return true;   // 原点が線の上
			}
			else
			{
				s.p[0] = a; s.count = 1;
				d = ao;
			}
			return false;
		}

		bool triangle(Simplex& s, Float3& d)
		{
			const Float3 a = s.p[0], b = s.p[1], c = s.p[2];
			const Float3 ab = b - a, ac = c - a, ao = -a;
			const Float3 abc = cross(ab, ac);
			if (sameDirection(cross(abc, ac), ao))
			{
				if (sameDirection(ac, ao)) { s.p[0] = a; s.p[1] = c; s.count = 2; d = cross(cross(ac, ao), ac); }
				else { s.p[0] = a; s.p[1] = b; s.count = 2; return line(s, d); }
			}
			else if (sameDirection(cross(ab, abc), ao))
			{
				s.p[0] = a; s.p[1] = b; s.count = 2;
				return line(s, d);
			}
			else
			{
				const float side = dot(abc, ao);
				if (std::fabs(side) < 1e-12f) return true;   // 原点が三角形の上
				if (side > 0.0f) { d = abc; }
				else { s.p[0] = a; s.p[1] = c; s.p[2] = b; d = -abc; }
			}
			return false;
		}

		bool tetrahedron(Simplex& s, Float3& d)
		{
			const Float3 a = s.p[0], b = s.p[1], c = s.p[2], dd = s.p[3];
			const Float3 ab = b - a, ac = c - a, ad = dd - a, ao = -a;
			const Float3 abc = cross(ab, ac), acd = cross(ac, ad), adb = cross(ad, ab);
			if (sameDirection(abc, ao)) { s.p[0] = a; s.p[1] = b; s.p[2] = c; s.count = 3; return triangle(s, d); }
			if (sameDirection(acd, ao)) { s.p[0] = a; s.p[1] = c; s.p[2] = dd; s.count = 3; return triangle(s, d); }
			if (sameDirection(adb, ao)) { s.p[0] = a; s.p[1] = dd; s.p[2] = b; s.count = 3; return triangle(s, d); }
			return true;   // どの面の外でもない = 原点を囲んだ
		}

		bool gjk(const WorldShape& a, const WorldShape& b)
		{
			Float3 d = a.Center - b.Center;
			if (lengthSq(d) < 1e-12f) d = Float3(1, 0, 0);

			Simplex s;
			s.p[0] = a.Support(d) - b.Support(-d);
			s.count = 1;
			d = -s.p[0];
			for (int i = 0; i < 64; ++i)
			{
				if (lengthSq(d) < 1e-12f) return true;   // 原点に届いた
				const Float3 p = a.Support(d) - b.Support(-d);
				if (dot(p, d) < 0.0f) return false;      // 方向 d に原点を越えられない = 離れている
				// 新しい点を先頭に (a = 一番新しい点)
				for (int k = s.count; k > 0; --k) s.p[k] = s.p[k - 1];
				s.p[0] = p;
				s.count++;
				bool contains = false;
				switch (s.count)
				{
				case 2: contains = line(s, d);        break;
				case 3: contains = triangle(s, d);    break;
				case 4: contains = tetrahedron(s, d); break;
				}
				if (contains) return true;
			}
			return true;   // 反復の上限 (ほぼ接している) : 重なりとして扱う
		}
	}

	Aabb WorldShape::ComputeAabb() const
	{
		Float3 extent;
		switch (Type)
		{
		case ShapeType::Sphere:
			extent = Float3(Radius, Radius, Radius);
			break;
		case ShapeType::Box:
		{
			// 回した箱を囲む箱 : 各軸に、3 つの軸の長さ × 半分の大きさ の絶対値を足す
			const Float3 x = absOf(Rotation.axis[0]) * HalfExtents.x;
			const Float3 y = absOf(Rotation.axis[1]) * HalfExtents.y;
			const Float3 z = absOf(Rotation.axis[2]) * HalfExtents.z;
			extent = x + y + z;
			break;
		}
		case ShapeType::Capsule:
		{
			const Float3 up = absOf(Rotation.axis[1]) * HalfHeight;
			extent = up + Float3(Radius, Radius, Radius);
			break;
		}
		}
		return { Center - extent, Center + extent };
	}

	Float3 WorldShape::Support(Float3 d) const
	{
		switch (Type)
		{
		case ShapeType::Sphere:
		{
			const float len = std::sqrt(lengthSq(d));
			return len > 1e-12f ? Center + d * (Radius / len) : Center;
		}
		case ShapeType::Box:
		{
			const Float3 local = Rotation.Inverse(d);
			const Float3 corner(local.x >= 0.0f ? HalfExtents.x : -HalfExtents.x,
			                    local.y >= 0.0f ? HalfExtents.y : -HalfExtents.y,
			                    local.z >= 0.0f ? HalfExtents.z : -HalfExtents.z);
			return Center + Rotation.Apply(corner);
		}
		case ShapeType::Capsule:
		{
			const Float3 up = Rotation.axis[1] * HalfHeight;
			const Float3 end = dot(d, up) >= 0.0f ? Center + up : Center - up;
			const float len = std::sqrt(lengthSq(d));
			return len > 1e-12f ? end + d * (Radius / len) : end;
		}
		}
		return Center;
	}

	bool Overlap(const WorldShape& a, const WorldShape& b)
	{
		const ShapeType ta = a.Type, tb = b.Type;

		// 球 / カプセル どうし : 芯 (点 / 線分) の距離 <= 半径の和
		if (ta != ShapeType::Box && tb != ShapeType::Box)
		{
			const float r = a.Radius + b.Radius;
			if (ta == ShapeType::Sphere && tb == ShapeType::Sphere)
			{
				return lengthSq(a.Center - b.Center) <= r * r;
			}
			Float3 a0 = a.Center, a1 = a.Center, b0 = b.Center, b1 = b.Center;
			if (ta == ShapeType::Capsule) capsuleSegment(a, a0, a1);
			if (tb == ShapeType::Capsule) capsuleSegment(b, b0, b1);
			if (ta == ShapeType::Sphere) return lengthSq(a.Center - closestOnSegment(a.Center, b0, b1)) <= r * r;
			if (tb == ShapeType::Sphere) return lengthSq(b.Center - closestOnSegment(b.Center, a0, a1)) <= r * r;
			return segmentSegmentDistanceSq(a0, a1, b0, b1) <= r * r;
		}

		// 球と箱 : 箱の中で球の中心に一番近い点までの距離 <= 半径
		if (ta == ShapeType::Sphere && tb == ShapeType::Box) return lengthSq(a.Center - closestOnBox(a.Center, b)) <= a.Radius * a.Radius;
		if (ta == ShapeType::Box && tb == ShapeType::Sphere) return lengthSq(b.Center - closestOnBox(b.Center, a)) <= b.Radius * b.Radius;

		// 箱どうし / 箱とカプセル : GJK
		return gjk(a, b);
	}
}

/************************************
* End of file
************************************/
