/***********************************************************************************
* @file    joints.h
* @brief   剛体を解く時の体の値 (SolverBody) とジョイント (2 つの体の動きを縛る物)
* @details - ジョイントは Joint を継承し、Prepare / WarmStart / Solve の 3 つを書けば足せる
*            (接触と同じ解き方の中で一緒に解かれる。新しい種類を足しても PhysicsWorld は変えなくてよい)
*          - Solve(useBias) : useBias = true の時は位置のずれも戻す (柔らかい拘束)。false の時は速度だけ (戻しすぎた勢いを取る)
*          - 今ある種類 : Distance (距離 / ばね / 縮む・伸びる限り) / Ball (点を合わせる) / Fixed (固く繋ぐ)
*
* @author  Keigo Onari
* @date    2026/11/03
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/
#pragma once
#ifndef PHYSICS_JOINTS_H_
#define PHYSICS_JOINTS_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <memory>
#include "common.h"
#include "dynamics.h"

namespace Physics
{
	// 解いている間の体の値 (1 回の Simulate の間だけ使う)
	struct SolverBody
	{
		Float3 V;          // 速度
		Float3 W;          // 角速度 (ワールド)
		Float3 DP;         // この Simulate の始めからの重心の動き
		Quat   Q;          // 今の回転
		Quat   Q0;         // この Simulate の始めの回転
		Float3 Center0;    // この Simulate の始めの重心
		Float3 InvMass;    // 質量の逆数 (軸ごと。固定した軸は 0。動かない物 / キネマティックは 0)
		Mat3   InvInertia; // 慣性の逆数 (ワールド。固定した軸は 0)
		bool   Dynamic = false;   // 力で動く (false なら接触は速度を書き換えない。並列で同じ物に書かないため)

		Float3 Center() const { return Center0 + DP; }
		void ApplyImpulse(Float3 r, Float3 p)   // 重心から r の所に p をかける
		{
			V = V + mul(InvMass, p);
			W = W + InvInertia * cross(r, p);
		}
		void ApplyAngularImpulse(Float3 l) { W = W + InvInertia * l; }
	};

	struct SolverStep
	{
		float    H      = 0.0f;   // 小さな刻みの時間
		float    InvH   = 0.0f;
		Softness JointSoftness;   // ジョイントの既定の硬さ
	};

	// 体の参照 (剛体なら SolverBody、動かない物ならジョイントが持つ固定の値)
	struct JointBodyRef
	{
		uint32_t   Handle = INVALID_HANDLE;   // INVALID_HANDLE ならワールド
		SolverBody Fixed;                     // 剛体でない時の値 (動かない)
		SolverBody* Body = nullptr;           // Prepare で決める
		Float3     LocalAnchor;               // 重心からの点 (体のローカルの向き)
	};

	class Joint
	{
	public:
		virtual ~Joint() = default;
		// Simulate の始め (体の参照と点は PhysicsWorld が決めてある)
		virtual void Prepare(const SolverStep& step) = 0;
		// 小さな刻みの始め : 前の力をかけ直す (反復が少なくても、積み重なった力がすぐ効く)
		virtual void WarmStart() = 0;
		virtual void Solve(const SolverStep& step, bool useBias) = 0;
		// この Simulate の力の大きさ (N。壊れたかの判定と表示)
		virtual float Force(float invH) const = 0;
		// 今の 2 点の距離
		float CurrentLength() const;

		JointDesc    Desc {};
		JointBodyRef A, B;
		bool         Broken = false;
		float        LastForce = 0.0f;

	protected:
		Float3 anchorA() const { return rotate(A.Body->Q, A.LocalAnchor); }   // 重心からの点 (ワールドの向き。今の回転)
		Float3 anchorB() const { return rotate(B.Body->Q, B.LocalAnchor); }
	};

	// 作る (Desc の種類で選ぶ)。fixedRelative : Fixed の時の「B の回転 × A の回転の逆」(作った時の向きを保つ)
	std::unique_ptr<Joint> CreateJoint(const JointDesc& desc, const Quat& fixedRelative);
}

#endif // PHYSICS_JOINTS_H_

/************************************
* End of file
************************************/
