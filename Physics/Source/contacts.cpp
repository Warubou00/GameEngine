/***********************************************************************************
* @file    contacts.cpp
* @brief   接触の情報 (法線 / めり込み量 / 接触点)。説明は contacts.h
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
#include "contacts.h"
#include "meshShape.h"

namespace Physics
{
	// ==============================================================
	// Manifold
	// ==============================================================

	void Manifold::Add(const Float3& position, const Float3& normal, float depth)
	{
		depth = (std::max)(depth, 0.0f);
		// ほぼ同じ場所の点 (メッシュの隣り合う三角形の辺など) はまとめる (深い方を残す)
		for (int i = 0; i < Count; ++i)
		{
			if (lengthSq(Points[i].Position - position) < 1e-6f && dot(Points[i].Normal, normal) > 0.99f)
			{
				if (depth > Points[i].Depth) Points[i] = { position, normal, depth };
				return;
			}
		}
		if (Count < MAX_POINTS)
		{
			Points[Count++] = { position, normal, depth };
			return;
		}
		// 一杯なら、一番浅い点より深い時だけ入れ替える (深い所を残す)
		int shallowest = 0;
		for (int i = 1; i < Count; ++i)
		{
			if (Points[i].Depth < Points[shallowest].Depth) shallowest = i;
		}
		if (depth > Points[shallowest].Depth) Points[shallowest] = { position, normal, depth };
	}

	void Manifold::Flip()
	{
		for (int i = 0; i < Count; ++i) Points[i].Normal = -Points[i].Normal;
	}

	namespace
	{
		constexpr float EPSILON = 1e-6f;

		Float3 normalize(Float3 v, Float3 fallback = Float3(0, 1, 0))
		{
			const float len = std::sqrt(lengthSq(v));
			return len > EPSILON ? v * (1.0f / len) : fallback;
		}

		void capsuleSegment(const WorldShape& s, Float3& p0, Float3& p1)
		{
			const Float3 up = s.Rotation.axis[1] * s.HalfHeight;
			p0 = s.Center - up;
			p1 = s.Center + up;
		}

		// ==============================================================
		// 点が 5 つ以上ある時、面積が一番大きくなる 4 点を残す (P2)
		//   1. 一番深い点 / 2. 1 から一番遠い点 / 3. 1-2 と三角形の面積が一番大きい点 / 4. 三角形の外で面積が一番増える点
		//   以前は「一番浅い点と入れ替え」(入れた順で決まる) だったので、止まった箱 (深さがほぼ同じ) で
		//   刻みごとに選ぶ点が変わり、前の力積の引き継ぎが外れて、ゆっくりずれ続けていた (積んだ山が眠れなかった)
		// ==============================================================
		void addReduced(Manifold& out, const Float3* points, const float* depths, int count, Float3 n)
		{
			if (count <= Manifold::MAX_POINTS)
			{
				for (int i = 0; i < count; ++i) out.Add(points[i], n, depths[i]);
				return;
			}
			int chosen[4];
			chosen[0] = 0;
			for (int i = 1; i < count; ++i) if (depths[i] > depths[chosen[0]] + 1e-5f) chosen[0] = i;
			float best = -1.0f;
			for (int i = 0; i < count; ++i)
			{
				const float d = lengthSq(points[i] - points[chosen[0]]);
				if (d > best) { best = d; chosen[1] = i; }
			}
			auto area = [&](int a, int b, int c) { return lengthSq(cross(points[b] - points[a], points[c] - points[a])); };
			best = -1.0f;
			for (int i = 0; i < count; ++i)
			{
				const float s = area(chosen[0], chosen[1], i);
				if (s > best) { best = s; chosen[2] = i; }
			}
			best = -1.0f;
			for (int i = 0; i < count; ++i)
			{
				if (i == chosen[0] || i == chosen[1] || i == chosen[2]) continue;
				const float s = area(chosen[0], chosen[1], i) + area(chosen[1], chosen[2], i) + area(chosen[2], chosen[0], i);
				if (s > best) { best = s; chosen[3] = i; }
			}
			for (int k = 0; k < 4; ++k) out.Add(points[chosen[k]], n, depths[chosen[k]]);
		}

		Float3 closestOnSegment(Float3 p, Float3 a, Float3 b)
		{
			const Float3 ab = b - a;
			const float  len = lengthSq(ab);
			if (len <= EPSILON) return a;
			return a + ab * std::clamp(dot(p - a, ab) / len, 0.0f, 1.0f);
		}

		// 2 つの線分の一番近い点 (Real-Time Collision Detection 5.1.9)
		void closestSegmentSegment(Float3 p1, Float3 q1, Float3 p2, Float3 q2, Float3& c1, Float3& c2)
		{
			const Float3 d1 = q1 - p1, d2 = q2 - p2, r = p1 - p2;
			const float a = lengthSq(d1), e = lengthSq(d2), f = dot(d2, r);
			float s = 0.0f, t = 0.0f;
			if (a <= EPSILON && e <= EPSILON) { c1 = p1; c2 = p2; return; }
			if (a <= EPSILON)
			{
				t = std::clamp(f / e, 0.0f, 1.0f);
			}
			else
			{
				const float c = dot(d1, r);
				if (e <= EPSILON)
				{
					s = std::clamp(-c / a, 0.0f, 1.0f);
				}
				else
				{
					const float b = dot(d1, d2);
					const float denom = a * e - b * b;
					s = denom > EPSILON ? std::clamp((b * f - c * e) / denom, 0.0f, 1.0f) : 0.0f;
					t = (b * s + f) / e;
					if (t < 0.0f)      { t = 0.0f; s = std::clamp(-c / a, 0.0f, 1.0f); }
					else if (t > 1.0f) { t = 1.0f; s = std::clamp((b - c) / a, 0.0f, 1.0f); }
				}
			}
			c1 = p1 + d1 * s;
			c2 = p2 + d2 * t;
		}

		// 三角形の上で p に一番近い点 (Real-Time Collision Detection 5.1.5)
		Float3 closestOnTriangle(Float3 p, Float3 a, Float3 b, Float3 c)
		{
			const Float3 ab = b - a, ac = c - a, ap = p - a;
			const float d1 = dot(ab, ap), d2 = dot(ac, ap);
			if (d1 <= 0.0f && d2 <= 0.0f) return a;
			const Float3 bp = p - b;
			const float d3 = dot(ab, bp), d4 = dot(ac, bp);
			if (d3 >= 0.0f && d4 <= d3) return b;
			const float vc = d1 * d4 - d3 * d2;
			if (vc <= 0.0f && d1 >= 0.0f && d3 <= 0.0f) return a + ab * (d1 / (d1 - d3));
			const Float3 cp = p - c;
			const float d5 = dot(ab, cp), d6 = dot(ac, cp);
			if (d6 >= 0.0f && d5 <= d6) return c;
			const float vb = d5 * d2 - d1 * d6;
			if (vb <= 0.0f && d2 >= 0.0f && d6 <= 0.0f) return a + ac * (d2 / (d2 - d6));
			const float va = d3 * d6 - d5 * d4;
			if (va <= 0.0f && (d4 - d3) >= 0.0f && (d5 - d6) >= 0.0f) return b + (c - b) * ((d4 - d3) / ((d4 - d3) + (d5 - d6)));
			const float denom = 1.0f / (va + vb + vc);
			return a + ab * (vb * denom) + ac * (vc * denom);
		}

		Float3 closestOnBox(Float3 p, const WorldShape& box)
		{
			const Float3 local = box.Rotation.Inverse(p - box.Center);
			const Float3 clamped(std::clamp(local.x, -box.HalfExtents.x, box.HalfExtents.x),
			                     std::clamp(local.y, -box.HalfExtents.y, box.HalfExtents.y),
			                     std::clamp(local.z, -box.HalfExtents.z, box.HalfExtents.z));
			return box.Center + box.Rotation.Apply(clamped);
		}

		float axisOf(const Float3& v, int i) { return i == 0 ? v.x : i == 1 ? v.y : v.z; }

		// ==============================================================
		// 芯が点 / 線分の形どうし (球 / カプセル)
		// ==============================================================
		bool roundRound(const WorldShape& a, const WorldShape& b, Manifold& out)
		{
			Float3 a0 = a.Center, a1 = a.Center, b0 = b.Center, b1 = b.Center;
			if (a.Type == ShapeType::Capsule) capsuleSegment(a, a0, a1);
			if (b.Type == ShapeType::Capsule) capsuleSegment(b, b0, b1);
			Float3 pa, pb;
			closestSegmentSegment(a0, a1, b0, b1, pa, pb);
			const float r = a.Radius + b.Radius;
			const Float3 d = pb - pa;
			const float distSq = lengthSq(d);
			if (distSq > r * r) return false;
			const float dist = std::sqrt(distSq);
			const Float3 n = normalize(d, normalize(b.Center - a.Center));
			const float depth = r - dist;
			out.Add(pa + n * (a.Radius - depth * 0.5f), n, depth);   // 2 つの表面の真ん中

			// カプセルどうしが平行に並んでいる時は、重なっている範囲の両端も入れる (横に寝たカプセルが安定する)
			if (a.Type == ShapeType::Capsule && b.Type == ShapeType::Capsule)
			{
				const Float3 da = normalize(a1 - a0), db = normalize(b1 - b0);
				if (std::fabs(dot(da, db)) > 0.98f)
				{
					for (const Float3& end : { b0, b1 })
					{
						const Float3 q = closestOnSegment(end, a0, a1);
						const Float3 e = end - q;
						const float ed = std::sqrt(lengthSq(e));
						if (ed < r && ed > EPSILON) out.Add(q + e * (a.Radius / ed), e * (1.0f / ed), r - ed);
					}
				}
			}
			return true;
		}

		// ==============================================================
		// 箱 (A) と球 (B)
		// ==============================================================
		bool boxSphere(const WorldShape& box, const WorldShape& sphere, Manifold& out)
		{
			const Float3 local = box.Rotation.Inverse(sphere.Center - box.Center);
			const Float3 e = box.HalfExtents;
			const Float3 clamped(std::clamp(local.x, -e.x, e.x), std::clamp(local.y, -e.y, e.y), std::clamp(local.z, -e.z, e.z));
			const Float3 d = local - clamped;
			const float distSq = lengthSq(d);
			if (distSq > EPSILON)
			{
				if (distSq > sphere.Radius * sphere.Radius) return false;
				const float dist = std::sqrt(distSq);
				const Float3 n = box.Rotation.Apply(d * (1.0f / dist));
				out.Add(box.Center + box.Rotation.Apply(clamped), n, sphere.Radius - dist);
				return true;
			}
			// 中心が箱の中 : 一番近い面から押し出す
			int axis = 0;
			float best = e.x - std::fabs(local.x);
			for (int i = 1; i < 3; ++i)
			{
				const float distance = axisOf(e, i) - std::fabs(axisOf(local, i));
				if (distance < best) { best = distance; axis = i; }
			}
			const float sign = axisOf(local, axis) >= 0.0f ? 1.0f : -1.0f;
			const Float3 n = box.Rotation.axis[axis] * sign;
			out.Add(sphere.Center - n * best, n, sphere.Radius + best);
			return true;
		}

		// ==============================================================
		// 箱 (A) とカプセル (B)
		// ==============================================================
		bool boxCapsule(const WorldShape& box, const WorldShape& capsule, Manifold& out)
		{
			Float3 p0, p1;
			capsuleSegment(capsule, p0, p1);
			const float r = capsule.Radius;

			// 線分の上で箱に一番近い点 : 距離は t について凸なので、黄金分割で探す
			auto distSqAt = [&](float t)
			{
				const Float3 p = p0 + (p1 - p0) * t;
				return lengthSq(p - closestOnBox(p, box));
			};
			float lo = 0.0f, hi = 1.0f;
			constexpr float GOLDEN = 0.6180340f;
			float x1 = hi - GOLDEN * (hi - lo), x2 = lo + GOLDEN * (hi - lo);
			float f1 = distSqAt(x1), f2 = distSqAt(x2);
			for (int i = 0; i < 24; ++i)
			{
				if (f1 < f2) { hi = x2; x2 = x1; f2 = f1; x1 = hi - GOLDEN * (hi - lo); f1 = distSqAt(x1); }
				else         { lo = x1; x1 = x2; f1 = f2; x2 = lo + GOLDEN * (hi - lo); f2 = distSqAt(x2); }
			}
			float t = 0.5f * (lo + hi);
			if (distSqAt(0.0f) < distSqAt(t)) t = 0.0f;
			if (distSqAt(1.0f) < distSqAt(t)) t = 1.0f;
			const Float3 p = p0 + (p1 - p0) * t;
			const Float3 q = closestOnBox(p, box);
			const float distSq = lengthSq(p - q);

			if (distSq > 1e-8f)
			{
				if (distSq > r * r) return false;
				const float dist = std::sqrt(distSq);
				const Float3 n = (p - q) * (1.0f / dist);
				out.Add(q, n, r - dist);
				// 箱に寝ている時は両端も (1 点だと寝たカプセルが転がる)
				for (const Float3& end : { p0, p1 })
				{
					const Float3 qe = closestOnBox(end, box);
					const Float3 de = end - qe;
					const float  ds = lengthSq(de);
					if (ds > 1e-8f && ds < r * r)
					{
						const float d = std::sqrt(ds);
						const Float3 ne = de * (1.0f / d);
						if (dot(ne, n) > 0.95f) out.Add(qe, ne, r - d);
					}
				}
				return true;
			}

			// 芯が箱に刺さっている : 箱の 3 軸と (線分 × 箱の軸) の 3 軸で、一番浅く抜ける向きを探す
			const Float3 seg = p1 - p0;
			Float3 axes[6] = { box.Rotation.axis[0], box.Rotation.axis[1], box.Rotation.axis[2] };
			int axisCount = 3;
			for (int i = 0; i < 3; ++i)
			{
				const Float3 c = cross(seg, box.Rotation.axis[i]);
				if (lengthSq(c) > 1e-6f) axes[axisCount++] = normalize(c);
			}
			float bestDepth = FLT_MAX;
			Float3 bestNormal(0, 1, 0);
			for (int i = 0; i < axisCount; ++i)
			{
				const Float3 L = axes[i];
				const float boxR = box.HalfExtents.x * std::fabs(dot(box.Rotation.axis[0], L)) +
				                   box.HalfExtents.y * std::fabs(dot(box.Rotation.axis[1], L)) +
				                   box.HalfExtents.z * std::fabs(dot(box.Rotation.axis[2], L));
				const float s0 = dot(p0 - box.Center, L), s1 = dot(p1 - box.Center, L);
				const float minS = (std::min)(s0, s1) - r, maxS = (std::max)(s0, s1) + r;
				const float pushPositive = boxR - minS;   // カプセルを +L へ押し出す量
				const float pushNegative = maxS + boxR;   // -L へ
				if (pushPositive < bestDepth) { bestDepth = pushPositive; bestNormal = L; }
				if (pushNegative < bestDepth) { bestDepth = pushNegative; bestNormal = -L; }
			}
			// 接触点 : 法線と逆の向きに一番深い端
			const Float3 deep = dot(p0, bestNormal) < dot(p1, bestNormal) ? p0 : p1;
			out.Add(deep - bestNormal * r, bestNormal, bestDepth);
			return true;
		}

		// ==============================================================
		// 箱どうし (分離軸)
		// ==============================================================
		struct BoxData
		{
			Float3 c, u[3];
			float  e[3];
		};

		BoxData toBox(const WorldShape& s)
		{
			BoxData b;
			b.c = s.Center;
			for (int i = 0; i < 3; ++i) b.u[i] = s.Rotation.axis[i];
			b.e[0] = s.HalfExtents.x; b.e[1] = s.HalfExtents.y; b.e[2] = s.HalfExtents.z;
			return b;
		}

		// 参照する面 (ref の axis 番目、外向き refN) に、相手 (inc) の一番向かい合う面を切り取って接触点を作る
		void boxFaceContacts(const BoxData& ref, int axis, const Float3& refN, const BoxData& inc, bool refIsA, Manifold& out)
		{
			// 相手の面 : 法線が refN と一番逆向きの面
			int k = 0;
			float bestDot = 0.0f;
			for (int i = 0; i < 3; ++i)
			{
				const float d = dot(inc.u[i], refN);
				if (std::fabs(d) > std::fabs(bestDot)) { bestDot = d; k = i; }
			}
			const float sign = bestDot > 0.0f ? -1.0f : 1.0f;
			const Float3 faceCenter = inc.c + inc.u[k] * (sign * inc.e[k]);
			const int k1 = (k + 1) % 3, k2 = (k + 2) % 3;
			const Float3 a1 = inc.u[k1] * inc.e[k1], a2 = inc.u[k2] * inc.e[k2];

			Float3 poly[16] = { faceCenter + a1 + a2, faceCenter - a1 + a2, faceCenter - a1 - a2, faceCenter + a1 - a2 };
			int count = 4;

			// 参照面の 4 つの横の面で切る (Sutherland-Hodgman)
			const int r1 = (axis + 1) % 3, r2 = (axis + 2) % 3;
			const Float3 planes[4] = { ref.u[r1], -ref.u[r1], ref.u[r2], -ref.u[r2] };
			const float  offsets[4] = { dot(ref.u[r1], ref.c) + ref.e[r1], -dot(ref.u[r1], ref.c) + ref.e[r1],
			                            dot(ref.u[r2], ref.c) + ref.e[r2], -dot(ref.u[r2], ref.c) + ref.e[r2] };
			for (int p = 0; p < 4 && count > 0; ++p)
			{
				Float3 next[16];
				int nextCount = 0;
				for (int i = 0; i < count; ++i)
				{
					const Float3 s = poly[i], e = poly[(i + 1) % count];
					const float ds = dot(planes[p], s) - offsets[p];
					const float de = dot(planes[p], e) - offsets[p];
					if (ds <= 0.0f && nextCount < 16) next[nextCount++] = s;
					if ((ds < 0.0f) != (de < 0.0f) && nextCount < 16)
					{
						next[nextCount++] = s + (e - s) * (ds / (ds - de));
					}
				}
				count = nextCount;
				for (int i = 0; i < count; ++i) poly[i] = next[i];
			}

			// 参照面より下 (めり込んでいる) 点だけ。法線は A → B
			const Float3 refFace = ref.c + refN * ref.e[axis];
			const Float3 n = refIsA ? refN : -refN;
			Float3 points[16];
			float  depths[16];
			int    candidates = 0;
			for (int i = 0; i < count; ++i)
			{
				const float separation = dot(poly[i] - refFace, refN);
				if (separation > 0.0f) continue;
				points[candidates] = poly[i] - refN * (separation * 0.5f);
				depths[candidates] = -separation;
				candidates++;
			}
			addReduced(out, points, depths, candidates, n);
		}

		bool boxBox(const WorldShape& sa, const WorldShape& sb, Manifold& out)
		{
			const BoxData A = toBox(sa), B = toBox(sb);
			float R[3][3], AbsR[3][3];
			for (int i = 0; i < 3; ++i)
				for (int j = 0; j < 3; ++j)
				{
					R[i][j]    = dot(A.u[i], B.u[j]);
					AbsR[i][j] = std::fabs(R[i][j]) + 1e-6f;   // 平行な辺の外積が 0 になるのを避ける
				}
			const Float3 tWorld = B.c - A.c;
			const float t[3] = { dot(tWorld, A.u[0]), dot(tWorld, A.u[1]), dot(tWorld, A.u[2]) };

			float faceDepth = FLT_MAX, edgeDepth = FLT_MAX;
			int   faceAxis = -1, edgeA = -1, edgeB = -1;
			Float3 faceNormal, edgeNormal;

			// A の面
			for (int i = 0; i < 3; ++i)
			{
				const float ra = A.e[i];
				const float rb = B.e[0] * AbsR[i][0] + B.e[1] * AbsR[i][1] + B.e[2] * AbsR[i][2];
				const float overlap = ra + rb - std::fabs(t[i]);
				if (overlap < 0.0f) return false;
				if (overlap < faceDepth) { faceDepth = overlap; faceAxis = i; faceNormal = A.u[i] * (t[i] >= 0.0f ? 1.0f : -1.0f); }
			}
			// B の面
			for (int j = 0; j < 3; ++j)
			{
				const float ra = A.e[0] * AbsR[0][j] + A.e[1] * AbsR[1][j] + A.e[2] * AbsR[2][j];
				const float rb = B.e[j];
				const float d  = dot(tWorld, B.u[j]);
				const float overlap = ra + rb - std::fabs(d);
				if (overlap < 0.0f) return false;
				if (overlap < faceDepth) { faceDepth = overlap; faceAxis = 3 + j; faceNormal = B.u[j] * (d >= 0.0f ? 1.0f : -1.0f); }
			}
			// 辺 × 辺
			for (int i = 0; i < 3; ++i)
			{
				for (int j = 0; j < 3; ++j)
				{
					const Float3 L = cross(A.u[i], B.u[j]);
					const float len = std::sqrt(lengthSq(L));
					if (len < 1e-4f) continue;   // ほぼ平行 (面の軸で調べてある)
					const int i1 = (i + 1) % 3, i2 = (i + 2) % 3, j1 = (j + 1) % 3, j2 = (j + 2) % 3;
					const float ra = A.e[i1] * AbsR[i2][j] + A.e[i2] * AbsR[i1][j];
					const float rb = B.e[j1] * AbsR[i][j2] + B.e[j2] * AbsR[i][j1];
					const float d  = t[i2] * R[i1][j] - t[i1] * R[i2][j];
					const float overlap = (ra + rb - std::fabs(d)) / len;
					if (overlap < 0.0f) return false;
					if (overlap < edgeDepth)
					{
						edgeDepth = overlap; edgeA = i; edgeB = j;
						edgeNormal = L * (1.0f / len);
						if (dot(edgeNormal, tWorld) < 0.0f) edgeNormal = -edgeNormal;
					}
				}
			}

			// 面を優先する (辺がはっきり浅い時だけ辺。わずかな差で面と辺が入れ替わると接触点が飛ぶため)
			if (edgeA >= 0 && edgeDepth < faceDepth * 0.95f - 1e-3f)
			{
				// 辺どうし : 法線の向きに一番出ている A の辺と、逆向きに一番出ている B の辺の、一番近い点
				Float3 pa = A.c, pb = B.c;
				for (int k = 0; k < 3; ++k)
				{
					if (k != edgeA) pa = pa + A.u[k] * (dot(A.u[k], edgeNormal) > 0.0f ? A.e[k] : -A.e[k]);
					if (k != edgeB) pb = pb + B.u[k] * (dot(B.u[k], edgeNormal) < 0.0f ? B.e[k] : -B.e[k]);
				}
				Float3 ca, cb;
				closestSegmentSegment(pa - A.u[edgeA] * A.e[edgeA], pa + A.u[edgeA] * A.e[edgeA],
				                      pb - B.u[edgeB] * B.e[edgeB], pb + B.u[edgeB] * B.e[edgeB], ca, cb);
				out.Add((ca + cb) * 0.5f, edgeNormal, edgeDepth);
				return true;
			}

			if (faceAxis < 3) boxFaceContacts(A, faceAxis, faceNormal, B, true, out);
			else              boxFaceContacts(B, faceAxis - 3, -faceNormal, A, false, out);   // B の面 : 外向き = A の側
			if (out.Count == 0) out.Add((A.c + B.c) * 0.5f, faceNormal, faceDepth);           // 切り取りで点が残らなかった (ほぼ接触)
			return true;
		}

		// ==============================================================
		// メッシュ (A) と 凸の形 (B)
		// ==============================================================
		void triangleSphere(const Float3& a, const Float3& b, const Float3& c, const Float3& triN, const WorldShape& s, Manifold& out)
		{
			const Float3 q = closestOnTriangle(s.Center, a, b, c);
			const Float3 d = s.Center - q;
			const float distSq = lengthSq(d);
			if (distSq > s.Radius * s.Radius) return;
			const float dist = std::sqrt(distSq);
			Float3 n = dist > EPSILON ? d * (1.0f / dist) : triN;
			out.Add(q, n, s.Radius - dist);
		}

		void triangleCapsule(const Float3& a, const Float3& b, const Float3& c, const Float3& triN, const WorldShape& cap, Manifold& out)
		{
			Float3 p0, p1;
			capsuleSegment(cap, p0, p1);
			const float r = cap.Radius;

			// 線分と三角形の一番近い点 : 両端 → 三角形 / 三角形の 3 辺 → 線分 / 線分が三角形を貫く
			Float3 bestP = p0, bestQ = closestOnTriangle(p0, a, b, c);
			float bestSq = lengthSq(bestP - bestQ);
			auto consider = [&](const Float3& p, const Float3& q)
			{
				const float d = lengthSq(p - q);
				if (d < bestSq) { bestSq = d; bestP = p; bestQ = q; }
			};
			consider(p1, closestOnTriangle(p1, a, b, c));
			const Float3 edges[3][2] = { { a, b }, { b, c }, { c, a } };
			for (const auto& e : edges)
			{
				Float3 cs, ct;
				closestSegmentSegment(p0, p1, e[0], e[1], cs, ct);
				consider(cs, ct);
			}
			const float d0 = dot(p0 - a, triN), d1 = dot(p1 - a, triN);
			if ((d0 < 0.0f) != (d1 < 0.0f))
			{
				const Float3 hit = p0 + (p1 - p0) * (d0 / (d0 - d1));
				if (lengthSq(closestOnTriangle(hit, a, b, c) - hit) < 1e-10f) consider(hit, hit);
			}
			if (bestSq > r * r) return;

			const float dist = std::sqrt(bestSq);
			// 法線 : 三角形から線分へ (刺さっている時は三角形の法線を、カプセルの中心の側へ)
			Float3 n = dist > EPSILON ? (bestP - bestQ) * (1.0f / dist) : (dot(cap.Center - a, triN) >= 0.0f ? triN : -triN);
			out.Add(bestQ, n, r - dist);

			// 寝ている時は両端も (三角形の上にある端だけ)
			for (const Float3& end : { p0, p1 })
			{
				const Float3 q = closestOnTriangle(end, a, b, c);
				const Float3 d = end - q;
				const float ds = lengthSq(d);
				if (ds > 1e-8f && ds < r * r)
				{
					const float de = std::sqrt(ds);
					const Float3 ne = d * (1.0f / de);
					if (dot(ne, n) > 0.95f) out.Add(q, ne, r - de);
				}
			}
		}

		void triangleBox(const Float3& a, const Float3& b, const Float3& c, const Float3& triN, const WorldShape& box, Manifold& out)
		{
			// 分離軸 13 本 : 三角形の法線 / 箱の 3 軸 / 三角形の 3 辺 × 箱の 3 軸
			const Float3 verts[3] = { a, b, c };
			const Float3 edges[3] = { b - a, c - b, a - c };
			Float3 axes[13];
			int axisCount = 0;
			if (lengthSq(triN) > 0.5f) axes[axisCount++] = triN;
			for (int i = 0; i < 3; ++i) axes[axisCount++] = box.Rotation.axis[i];
			for (int e = 0; e < 3; ++e)
				for (int i = 0; i < 3; ++i)
				{
					const Float3 L = cross(edges[e], box.Rotation.axis[i]);
					if (lengthSq(L) > 1e-8f) axes[axisCount++] = normalize(L);
				}

			float bestDepth = FLT_MAX;
			Float3 bestN;
			for (int k = 0; k < axisCount; ++k)
			{
				const Float3 L = axes[k];
				const float boxC = dot(box.Center, L);
				const float boxR = box.HalfExtents.x * std::fabs(dot(box.Rotation.axis[0], L)) +
				                   box.HalfExtents.y * std::fabs(dot(box.Rotation.axis[1], L)) +
				                   box.HalfExtents.z * std::fabs(dot(box.Rotation.axis[2], L));
				float triMin = dot(verts[0], L), triMax = triMin;
				for (int v = 1; v < 3; ++v) { const float p = dot(verts[v], L); triMin = (std::min)(triMin, p); triMax = (std::max)(triMax, p); }
				const float pushPositive = triMax - (boxC - boxR);   // 箱を +L へ押し出す量
				const float pushNegative = (boxC + boxR) - triMin;   // -L へ
				if (pushPositive < 0.0f || pushNegative < 0.0f) return;   // 離れている
				const float depth = (std::min)(pushPositive, pushNegative);
				const Float3 n = pushPositive <= pushNegative ? L : -L;
				// 三角形の表へ押し出す向きを少し優先 (わずかな差で横や裏へ押し出すと、地面の継ぎ目で引っかかる)
				const float bias = (k == 0 && dot(n, triN) > 0.0f) ? 0.95f : 1.0f;
				if (depth * bias < bestDepth) { bestDepth = depth * bias; bestN = n; }
			}
			// 実際の深さに戻す (優先のための係数を外す)
			if (dot(bestN, triN) > 0.999f) bestDepth /= 0.95f;

			// 接触点 : 箱の角のうち、三角形の側 (法線と逆) にめり込んでいる物。無ければ一番深い角
			Float3 corners[8];
			for (int i = 0; i < 8; ++i)
			{
				corners[i] = box.Center + box.Rotation.axis[0] * ((i & 1) ? box.HalfExtents.x : -box.HalfExtents.x)
				                        + box.Rotation.axis[1] * ((i & 2) ? box.HalfExtents.y : -box.HalfExtents.y)
				                        + box.Rotation.axis[2] * ((i & 4) ? box.HalfExtents.z : -box.HalfExtents.z);
			}
			const float planeD = dot(a, bestN);
			int deepest = 0;
			float deepestValue = FLT_MAX;
			bool any = false;
			const bool faceAxis = dot(bestN, triN) > 0.999f || dot(bestN, triN) < -0.999f;
			for (int i = 0; i < 8; ++i)
			{
				const float s = dot(corners[i], bestN) - planeD;   // 三角形の平面からの距離 (法線の向きが +)
				if (s < deepestValue) { deepestValue = s; deepest = i; }
				if (faceAxis && s < 0.0f)
				{
					// 三角形の中に投影される角だけ (外の角は隣の三角形が受け持つ)
					const Float3 projected = corners[i] - bestN * s;
					if (lengthSq(closestOnTriangle(projected, a, b, c) - projected) < 1e-8f)
					{
						out.Add(projected, bestN, -s);
						any = true;
					}
				}
			}
			if (!any) out.Add(corners[deepest] + bestN * (bestDepth * 0.5f), bestN, bestDepth);
		}

		bool meshConvex(const WorldShape& meshShape, const WorldShape& convex, Manifold& out)
		{
			const MeshInstance& mesh = *meshShape.Mesh;
			const Aabb box = convex.ComputeAabb();
			mesh.Tree.Query(box, [&](uint32_t t)
			{
				Float3 a, b, c;
				mesh.Triangle(t, a, b, c);
				const Float3& n = mesh.Normals[t];
				switch (convex.Type)
				{
				case ShapeType::Sphere:  triangleSphere(a, b, c, n, convex, out);  break;
				case ShapeType::Capsule: triangleCapsule(a, b, c, n, convex, out); break;
				case ShapeType::Box:     triangleBox(a, b, c, n, convex, out);     break;
				default: break;
				}
			});
			return out.Count > 0;
		}
	}

	bool Collide(const WorldShape& a, const WorldShape& b, Manifold& out)
	{
		out.Count = 0;
		const ShapeType ta = a.Type, tb = b.Type;

		// メッシュは A に置く (メッシュどうしは動かない物どうしなので来ない)
		if (ta == ShapeType::Mesh && a.Mesh && tb != ShapeType::Mesh) return meshConvex(a, b, out);
		if (tb == ShapeType::Mesh && b.Mesh && ta != ShapeType::Mesh)
		{
			const bool hit = meshConvex(b, a, out);
			out.Flip();
			return hit;
		}
		if (ta == ShapeType::Mesh || tb == ShapeType::Mesh) return false;

		if (ta != ShapeType::Box && tb != ShapeType::Box) return roundRound(a, b, out);
		if (ta == ShapeType::Box && tb == ShapeType::Box) return boxBox(a, b, out);

		// 箱を A に置いて求め、逆だったら法線を返す
		const bool boxFirst = ta == ShapeType::Box;
		const WorldShape& box   = boxFirst ? a : b;
		const WorldShape& other = boxFirst ? b : a;
		const bool hit = other.Type == ShapeType::Sphere ? boxSphere(box, other, out) : boxCapsule(box, other, out);
		if (!boxFirst) out.Flip();
		return hit;
	}
}

/************************************
* End of file
************************************/
