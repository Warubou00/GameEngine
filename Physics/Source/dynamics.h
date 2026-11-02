/***********************************************************************************
* @file    dynamics.h
* @brief   剛体の計算に使う数学 (クォータニオン / 3x3 行列 / 柔らかい拘束の係数 / 慣性)
* @details 衝突判定 (C1〜C3) は回転を行列 (Rotation3) で持つだけだったが、
*          剛体は角速度で回転を進めるのでクォータニオンも使う (P1)
*
* @author  Keigo Onari
* @date    2026/11/03
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/
#pragma once
#ifndef PHYSICS_DYNAMICS_H_
#define PHYSICS_DYNAMICS_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <cmath>
#include "mathTypes.h"

namespace Physics
{
	inline float  length(Float3 a)    { return std::sqrt(dot(a, a)); }
	inline Float3 normalize(Float3 a) { const float l = length(a); return l > 1e-12f ? a * (1.0f / l) : Float3(); }

	// ==============================================================
	// クォータニオン (x, y, z, w。C# の System.Numerics.Quaternion と同じ並びと向き)
	// ==============================================================
	struct Quat
	{
		float x = 0.0f, y = 0.0f, z = 0.0f, w = 1.0f;
		Quat() = default;
		constexpr Quat(float x_, float y_, float z_, float w_) : x(x_), y(y_), z(z_), w(w_) {}
		Rotation3 ToRotation() const { return Rotation3::FromQuaternion(x, y, z, w); }
	};

	// a * b : 先に b、次に a で回す (Rotation3 の Apply と同じ向き)
	inline Quat operator*(const Quat& a, const Quat& b)
	{
		return { a.w * b.x + a.x * b.w + a.y * b.z - a.z * b.y,
		         a.w * b.y - a.x * b.z + a.y * b.w + a.z * b.x,
		         a.w * b.z + a.x * b.y - a.y * b.x + a.z * b.w,
		         a.w * b.w - a.x * b.x - a.y * b.y - a.z * b.z };
	}
	inline Quat conjugate(const Quat& q) { return { -q.x, -q.y, -q.z, q.w }; }
	inline Quat normalize(const Quat& q)
	{
		const float l = std::sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w);
		return l > 1e-12f ? Quat(q.x / l, q.y / l, q.z / l, q.w / l) : Quat();
	}
	inline Float3 rotate(const Quat& q, Float3 v)
	{
		// v + 2w (u × v) + 2 u × (u × v)
		const Float3 u(q.x, q.y, q.z);
		const Float3 t = cross(u, v) * 2.0f;
		return v + t * q.w + cross(u, t);
	}
	// 角速度 w (ワールド) で h 秒回す (q + h/2 * (w, 0) * q を正規化。小さな h ではこれで足りる)
	inline Quat integrate(const Quat& q, Float3 w, float h)
	{
		const Quat spin(w.x, w.y, w.z, 0.0f);
		const Quat dq = spin * q;
		const float s = 0.5f * h;
		return normalize(Quat(q.x + dq.x * s, q.y + dq.y * s, q.z + dq.z * s, q.w + dq.w * s));
	}
	// from から to への回転を、角速度 (1 秒で回す量) にする (キネマティックの目標の回転)
	inline Float3 angularVelocityBetween(const Quat& from, const Quat& to, float invDt)
	{
		Quat d = to * conjugate(from);
		if (d.w < 0.0f) d = Quat(-d.x, -d.y, -d.z, -d.w);   // 短い方へ回す
		const float s = std::sqrt(d.x * d.x + d.y * d.y + d.z * d.z);
		if (s < 1e-9f) return {};
		const float angle = 2.0f * std::atan2(s, d.w);
		return Float3(d.x, d.y, d.z) * (angle / s * invDt);
	}

	// ==============================================================
	// 3x3 行列 (m[行][列])
	// ==============================================================
	struct Mat3
	{
		float m[3][3] = {};

		static Mat3 Diagonal(Float3 d)
		{
			Mat3 r;
			r.m[0][0] = d.x; r.m[1][1] = d.y; r.m[2][2] = d.z;
			return r;
		}
		Float3 operator*(Float3 v) const
		{
			return { m[0][0] * v.x + m[0][1] * v.y + m[0][2] * v.z,
			         m[1][0] * v.x + m[1][1] * v.y + m[1][2] * v.z,
			         m[2][0] * v.x + m[2][1] * v.y + m[2][2] * v.z };
		}
		Mat3 operator+(const Mat3& o) const
		{
			Mat3 r;
			for (int i = 0; i < 3; ++i) for (int j = 0; j < 3; ++j) r.m[i][j] = m[i][j] + o.m[i][j];
			return r;
		}
		Mat3 operator*(const Mat3& o) const
		{
			Mat3 r;
			for (int i = 0; i < 3; ++i)
				for (int j = 0; j < 3; ++j)
					r.m[i][j] = m[i][0] * o.m[0][j] + m[i][1] * o.m[1][j] + m[i][2] * o.m[2][j];
			return r;
		}
		// 逆行列 (行列式が 0 に近ければ 0 の行列)
		Mat3 Inverse() const
		{
			const float a = m[0][0], b = m[0][1], c = m[0][2], d = m[1][0], e = m[1][1], f = m[1][2], g = m[2][0], h = m[2][1], i = m[2][2];
			const float A = e * i - f * h, B = -(d * i - f * g), C = d * h - e * g;
			const float det = a * A + b * B + c * C;
			Mat3 r;
			if (std::fabs(det) < 1e-20f) return r;
			const float s = 1.0f / det;
			r.m[0][0] = A * s; r.m[0][1] = -(b * i - c * h) * s; r.m[0][2] = (b * f - c * e) * s;
			r.m[1][0] = B * s; r.m[1][1] = (a * i - c * g) * s;  r.m[1][2] = -(a * f - c * d) * s;
			r.m[2][0] = C * s; r.m[2][1] = -(a * h - b * g) * s; r.m[2][2] = (a * e - b * d) * s;
			return r;
		}
	};

	// [r]x : 外積の行列 ([r]x v = r × v)
	inline Mat3 skew(Float3 r)
	{
		Mat3 s;
		s.m[0][1] = -r.z; s.m[0][2] =  r.y;
		s.m[1][0] =  r.z; s.m[1][2] = -r.x;
		s.m[2][0] = -r.y; s.m[2][1] =  r.x;
		return s;
	}

	// ローカルの対角の慣性 (の逆数) を、回転 R でワールドへ : R diag(d) R^T
	inline Mat3 rotateDiagonal(const Rotation3& r, Float3 d)
	{
		// R[i][k] = ローカルの軸 k のワールドの成分 i
		float R[3][3];
		for (int k = 0; k < 3; ++k)
		{
			R[0][k] = r.axis[k].x; R[1][k] = r.axis[k].y; R[2][k] = r.axis[k].z;
		}
		const float dk[3] = { d.x, d.y, d.z };
		Mat3 out;
		for (int i = 0; i < 3; ++i)
			for (int j = 0; j < 3; ++j)
				out.m[i][j] = R[i][0] * dk[0] * R[j][0] + R[i][1] * dk[1] * R[j][1] + R[i][2] * dk[2] * R[j][2];
		return out;
	}

	// ==============================================================
	// 柔らかい拘束 (Box2D v3 の b2Softness と同じ)
	//   hertz : 戻る速さ (振動数) / zeta : 減衰比 / h : 時間刻み
	//   めり込みを「ばね + ダンパー」で戻すので、硬い押し戻し (Baumgarte) のように跳ねたり震えたりしにくい
	// ==============================================================
	struct Softness
	{
		float BiasRate     = 0.0f;   // 位置の誤差 → 戻す速さ
		float MassScale    = 1.0f;
		float ImpulseScale = 0.0f;
	};

	inline Softness makeSoft(float hertz, float zeta, float h)
	{
		if (hertz == 0.0f) return { 0.0f, 1.0f, 0.0f };
		const float omega = 2.0f * 3.14159265358979f * hertz;
		const float a1 = 2.0f * zeta + h * omega;
		const float a2 = h * omega * a1;
		const float a3 = 1.0f / (1.0f + a2);
		return { omega / a1, a2 * a3, a3 };
	}

	// 法線 n に直交する 2 本の軸 (摩擦の向き)
	inline void tangentBasis(Float3 n, Float3& t1, Float3& t2)
	{
		// Duff ほか (2017) の方法 : 分岐が少なく、どの向きでも長さ 1
		const float sign = std::copysign(1.0f, n.z);
		const float a = -1.0f / (sign + n.z);
		const float b = n.x * n.y * a;
		t1 = Float3(1.0f + sign * n.x * n.x * a, sign * b, -sign * n.x);
		t2 = Float3(b, sign + n.y * n.y * a, -n.y);
	}
}

#endif // PHYSICS_DYNAMICS_H_

/************************************
* End of file
************************************/
