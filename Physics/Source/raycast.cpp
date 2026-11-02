/***********************************************************************************
* @file    raycast.cpp
* @brief   レイと形の交差。説明は raycast.h
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
#include <cfloat>
#include "raycast.h"
#include "meshShape.h"

namespace Physics
{
	namespace
	{
		// 球 : |o + d t - c|² = r² の小さい方の解
		bool raySphere(const Float3& c, float r, const Float3& o, const Float3& d, float maxDistance, float& distance, Float3& normal)
		{
			const Float3 m = o - c;
			const float b  = dot(m, d);
			const float cc = lengthSq(m) - r * r;
			if (cc <= 0.0f) return false;            // 始点が中
			if (b > 0.0f) return false;              // 離れていく向き
			const float disc = b * b - cc;
			if (disc < 0.0f) return false;
			const float t = -b - std::sqrt(disc);
			if (t > maxDistance) return false;
			distance = (std::max)(t, 0.0f);
			normal = (o + d * distance - c) * (1.0f / r);
			return true;
		}

		bool rayBox(const WorldShape& box, const Float3& o, const Float3& d, float maxDistance, float& distance, Float3& normal)
		{
			// ローカルで AABB と同じ方法 (入った面の軸を覚える)
			const Float3 lo = box.Rotation.Inverse(o - box.Center);
			const Float3 ld = box.Rotation.Inverse(d);
			const float  e[3] = { box.HalfExtents.x, box.HalfExtents.y, box.HalfExtents.z };
			const float  oo[3] = { lo.x, lo.y, lo.z };
			const float  dd[3] = { ld.x, ld.y, ld.z };
			float t0 = -FLT_MAX, t1 = FLT_MAX;
			int axis = 0;
			float sign = 1.0f;
			for (int i = 0; i < 3; ++i)
			{
				if (std::fabs(dd[i]) < 1e-9f)
				{
					if (oo[i] < -e[i] || oo[i] > e[i]) return false;   // 平行で外
					continue;
				}
				const float inv = 1.0f / dd[i];
				float a = (-e[i] - oo[i]) * inv, b = (e[i] - oo[i]) * inv;
				float s = -1.0f;   // a で入る面は -e 側
				if (a > b) { std::swap(a, b); s = 1.0f; }
				if (a > t0) { t0 = a; axis = i; sign = s; }
				if (b < t1) t1 = b;
				if (t0 > t1) return false;
			}
			if (t0 < 0.0f || t0 > maxDistance) return false;   // 始点が中 (t0 < 0) / 遠い
			distance = t0;
			normal = box.Rotation.axis[axis] * sign;
			return true;
		}

		bool rayCapsule(const WorldShape& cap, const Float3& o, const Float3& d, float maxDistance, float& distance, Float3& normal)
		{
			const Float3 axis = cap.Rotation.axis[1];
			const Float3 p0 = cap.Center - axis * cap.HalfHeight, p1 = cap.Center + axis * cap.HalfHeight;
			const float r = cap.Radius;

			// 始点がカプセルの中なら当たらない
			{
				const Float3 ab = p1 - p0;
				const float len = lengthSq(ab);
				const float s = len > 1e-12f ? std::clamp(dot(o - p0, ab) / len, 0.0f, 1.0f) : 0.0f;
				if (lengthSq(o - (p0 + ab * s)) <= r * r) return false;
			}

			bool hit = false;
			float best = maxDistance;
			// 円柱の側面 : 軸に垂直な成分で 2 次方程式
			const Float3 m  = o - p0;
			const Float3 dp = d - axis * dot(d, axis);
			const Float3 mp = m - axis * dot(m, axis);
			const float a = lengthSq(dp);
			if (a > 1e-12f)
			{
				const float b = dot(mp, dp);
				const float c = lengthSq(mp) - r * r;
				const float disc = b * b - a * c;
				if (disc >= 0.0f)
				{
					const float t = (-b - std::sqrt(disc)) / a;
					if (t >= 0.0f && t <= best)
					{
						const float h = dot(m + d * t, axis);
						if (h >= 0.0f && h <= 2.0f * cap.HalfHeight)
						{
							best = t; hit = true;
							normal = (mp + dp * t) * (1.0f / r);
						}
					}
				}
			}
			// 両端の球
			for (const Float3& end : { p0, p1 })
			{
				float t;
				Float3 n;
				if (raySphere(end, r, o, d, best, t, n) && t <= best) { best = t; normal = n; hit = true; }
			}
			if (hit) distance = best;
			return hit;
		}

		// 三角形 (両面) : Möller-Trumbore
		bool rayTriangle(const Float3& a, const Float3& b, const Float3& c, const Float3& o, const Float3& d, float maxDistance, float& t)
		{
			const Float3 e1 = b - a, e2 = c - a;
			const Float3 p = cross(d, e2);
			const float det = dot(e1, p);
			if (std::fabs(det) < 1e-12f) return false;
			const float inv = 1.0f / det;
			const Float3 s = o - a;
			const float u = dot(s, p) * inv;
			if (u < 0.0f || u > 1.0f) return false;
			const Float3 q = cross(s, e1);
			const float v = dot(d, q) * inv;
			if (v < 0.0f || u + v > 1.0f) return false;
			t = dot(e2, q) * inv;
			return t >= 0.0f && t <= maxDistance;
		}

		bool rayMesh(const MeshInstance& mesh, const Float3& o, const Float3& d, float maxDistance, float& distance, Float3& normal)
		{
			const Float3 invDir(1.0f / (std::fabs(d.x) > 1e-12f ? d.x : 1e-12f), 1.0f / (std::fabs(d.y) > 1e-12f ? d.y : 1e-12f),
			                    1.0f / (std::fabs(d.z) > 1e-12f ? d.z : 1e-12f));
			bool hit = false;
			float best = maxDistance;
			mesh.Tree.RayCast(o, invDir, best, [&](uint32_t tri, float& limit)
			{
				Float3 a, b, c;
				mesh.Triangle(tri, a, b, c);
				float t;
				if (rayTriangle(a, b, c, o, d, limit, t) && t < best)
				{
					best = t; limit = t; hit = true;
					const Float3& n = mesh.Normals[tri];
					normal = dot(n, d) <= 0.0f ? n : -n;   // レイの側を向ける
				}
			});
			if (hit) distance = best;
			return hit;
		}
	}

	bool RaycastShape(const WorldShape& shape, const Float3& origin, const Float3& dir, float maxDistance, float& distance, Float3& normal)
	{
		switch (shape.Type)
		{
		case ShapeType::Sphere:  return raySphere(shape.Center, shape.Radius, origin, dir, maxDistance, distance, normal);
		case ShapeType::Box:     return rayBox(shape, origin, dir, maxDistance, distance, normal);
		case ShapeType::Capsule: return rayCapsule(shape, origin, dir, maxDistance, distance, normal);
		case ShapeType::Mesh:    return shape.Mesh ? rayMesh(*shape.Mesh, origin, dir, maxDistance, distance, normal) : false;
		}
		return false;
	}
}

/************************************
* End of file
************************************/
