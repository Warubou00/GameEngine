/***********************************************************************************
* @file    common.h
* @brief   Physics.dll の共通定義 (export の指定 / ハンドル / C# とやり取りする構造体)
* @details C# 側は GameEngine.Runtime/Native/PhysicsNativeMethods.cs (並びを合わせること)
*
* @author  Keigo Onari
* @date    2026/11/02
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/
#pragma once
#ifndef PHYSICS_COMMON_H_
#define PHYSICS_COMMON_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <cstdint>

/******************************************************************
* Macro Definitions
******************************************************************/
// Physics プロジェクトでは PHYSICS_EXPORTS を定義してビルドする (dllexport)
#ifdef PHYSICS_EXPORTS
#define PHYSICS_API_CLASS __declspec(dllexport)
#else
#define PHYSICS_API_CLASS __declspec(dllimport)
#endif
#define PHYSICS_API extern "C" PHYSICS_API_CLASS

namespace Physics
{
	// 無効なハンドル (C# の PhysicsNativeMethods.InvalidHandle と同じ)
	constexpr uint32_t INVALID_HANDLE = 0xFFFFFFFFu;

	// コライダーの形
	enum class ShapeType : uint32_t
	{
		Sphere  = 0,   // Size.x = 半径
		Box     = 1,   // Size = 半分の大きさ (中心から面まで)
		Capsule = 2,   // Size.x = 半径 / Size.y = 円柱の半分の高さ (ローカルの Y 方向)
		Mesh    = 3,   // 三角形のメッシュ (BodyDesc.Mesh。動かない物だけ)
	};

	// 重なりを探す方法 (比べるため。既定は BVH)
	enum class Broadphase : uint32_t
	{
		Bvh   = 0,   // 動的 AABB ツリー (動かない物と動く物で 2 本)。動いた物だけ調べる
		Brute = 1,   // 総当たり (全部の組)
		Grid  = 2,   // 均一グリッド (毎フレーム作り直す)
	};

	// 体を作る時の値 (C# の PhysicsNativeMethods.BodyDesc と同じ並び : 96byte)
	struct BodyDesc
	{
		uint32_t Shape;          // ShapeType
		uint32_t Layer;          // 0〜31
		uint32_t CollideMask;    // 重なりを調べる相手のレイヤー (ビット)
		uint32_t IsStatic;       // 1 : 動かない物 (動かない物どうしは調べない)
		float    Center[3];      // 形の中心 (ローカル。Transform の位置からのずれ。大きさも掛かる)
		uint32_t Mesh;           // Physics_CreateMesh のハンドル (Shape = Mesh の時。以前は詰め物)
		float    Size[3];        // ShapeType を参照
		float    Friction;       // 摩擦 (0〜。組の値は 2 つの体の積の平方根。P1。以前は詰め物)
		float    Position[3];
		float    Restitution;    // 反発 (0〜1。組の値は大きい方。P1。以前は詰め物)
		float    Rotation[4];    // クォータニオン (x, y, z, w)
		float    Scale[3];
		uint32_t Flags;          // BodyFlags (P1。以前は詰め物)
	};
	static_assert(sizeof(BodyDesc) == 96, "BodyDesc must be 96 bytes (same as C#)");

	// 体の印 (BodyDesc.Flags)
	enum BodyFlags : uint32_t
	{
		BODY_TRIGGER = 1u,   // 重なりのイベントだけ (押し戻さない。Unity の isTrigger)
	};

	// ==============================================================
	// 剛体 (P1)
	// ==============================================================

	// 動き方
	enum class MotionType : uint32_t
	{
		None      = 0,   // 剛体なし (衝突判定だけ。Transform で動かす)
		Dynamic   = 1,   // 力と接触で動く
		Kinematic = 2,   // 速度 (か目標の位置) で動く。押されない (質量が無限)
	};

	// 軸の固定 (BodyDynamics.Locks。ワールドの軸。Unity の RigidbodyConstraints と同じ)
	enum LockFlags : uint32_t
	{
		LOCK_POSITION_X = 1u << 0, LOCK_POSITION_Y = 1u << 1, LOCK_POSITION_Z = 1u << 2,
		LOCK_ROTATION_X = 1u << 3, LOCK_ROTATION_Y = 1u << 4, LOCK_ROTATION_Z = 1u << 5,
	};

	// 剛体の値 (C# の PhysicsNativeMethods.BodyDynamics と同じ並び : 32byte)
	struct BodyDynamics
	{
		uint32_t Motion;          // MotionType
		float    Mass;            // kg (0 以下なら 1)。慣性は形と質量から求める
		float    LinearDamping;   // 速度の減衰 (1 秒あたり。空気抵抗の代わり)
		float    AngularDamping;
		float    GravityScale;    // 重力の倍率 (0 で無重力)
		uint32_t Locks;           // LockFlags
		float    InertiaScale;    // 慣性の倍率 (0 以下なら 1。大きいほど回りにくい)
		uint32_t Padding;
	};
	static_assert(sizeof(BodyDynamics) == 32, "BodyDynamics must be 32 bytes (same as C#)");

	// 剛体への命令 (C# の PhysicsNativeMethods.BodyCommand と同じ並び : 144byte)。まとめて送る
	enum CommandFlags : uint32_t
	{
		COMMAND_FORCE            = 1u << 0,   // Force / Torque を次の Simulate の間かける (重心に)
		COMMAND_IMPULSE          = 1u << 1,   // Impulse / AngularImpulse を今かける (速度が変わる)
		COMMAND_SET_VELOCITY     = 1u << 2,   // Velocity にする
		COMMAND_SET_ANGULAR      = 1u << 3,   // AngularVelocity にする
		COMMAND_KINEMATIC_TARGET = 1u << 4,   // キネマティック : 次の Simulate で TargetPosition / TargetRotation へ動く
	};
	struct BodyCommand
	{
		uint32_t Handle;
		uint32_t Flags;              // CommandFlags
		uint32_t Padding0;
		uint32_t Padding1;
		float    Force[3];           float Padding2;   // N (ワールド)
		float    Torque[3];          float Padding3;   // N·m (ワールド)
		float    Impulse[3];         float Padding4;   // N·s
		float    AngularImpulse[3];  float Padding5;
		float    Velocity[3];        float Padding6;   // m/s
		float    AngularVelocity[3]; float Padding7;   // rad/s (ワールド)
		float    TargetPosition[3];  float Padding8;   // Transform の位置
		float    TargetRotation[4];
	};
	static_assert(sizeof(BodyCommand) == 144, "BodyCommand must be 144 bytes (same as C#)");

	// 剛体の今の状態 (C# の PhysicsNativeMethods.BodyState と同じ並び : 96byte)
	struct BodyState
	{
		uint32_t Handle;
		uint32_t Flags;              // 0 (P2 で眠り)
		uint32_t Padding0;
		uint32_t Padding1;
		float    Position[3];        float Padding2;   // Transform の位置
		float    Rotation[4];
		float    Velocity[3];        float Padding3;
		float    AngularVelocity[3]; float Padding4;
		float    CenterOfMass[3];    float Padding5;   // ワールドの重心 (力を点にかける時に使う)
	};
	static_assert(sizeof(BodyState) == 96, "BodyState must be 96 bytes (same as C#)");

	// 解き方の設定 (C# の PhysicsNativeMethods.SolverSettings と同じ並び : 64byte)
	struct SolverSettings
	{
		float    Gravity[3];
		uint32_t SubSteps;               // 1 回の Simulate を何回に分けるか (既定 4。多いほど積み重ねが安定)
		float    ContactHertz;           // めり込みを戻す速さ (既定 30)
		float    ContactDampingRatio;    // 既定 10 (跳ねずに戻る)
		float    ContactPushMaxVelocity; // めり込みを戻す速度の上限 m/s (既定 3)
		float    RestitutionThreshold;   // この速さ (m/s) より遅くぶつかった時は跳ねない (既定 1)
		float    SpeculativeDistance;    // 離れていても、この距離までは接触として扱う (既定 0.02。すり抜けと震えを減らす)
		float    JointHertz;             // ジョイントの硬さ (既定 60)
		float    JointDampingRatio;      // 既定 2 (ジョイントは少し粘る)
		float    MaxLinearSpeed;         // 速度の上限 m/s (既定 400)
		uint32_t Iterations;             // 1 つの小さな刻みで解く回数 (既定 1)
		uint32_t RelaxIterations;        // 押し戻しの勢いを取る回数 (既定 1)
		uint32_t Padding0;
		uint32_t Padding1;
	};
	static_assert(sizeof(SolverSettings) == 64, "SolverSettings must be 64 bytes (same as C#)");

	// ジョイント (C# の PhysicsNativeMethods.JointDesc と同じ並び : 64byte)
	enum class JointType : uint32_t
	{
		Distance = 0,   // 2 点の距離を保つ (MinLength〜MaxLength。Hertz > 0 ならばね)
		Ball     = 1,   // 2 点を合わせる (どの向きにも回る。肩の関節)
		Fixed    = 2,   // 2 つの体を固く繋ぐ (位置も向きも)
	};
	enum JointFlags : uint32_t
	{
		JOINT_COLLIDE_CONNECTED = 1u,   // 繋いだ 2 つの体どうしも押し戻す (既定は押し戻さない)
	};
	struct JointDesc
	{
		uint32_t Type;           // JointType
		uint32_t BodyA;
		uint32_t BodyB;          // INVALID_HANDLE ならワールド (AnchorB はワールドの点)
		uint32_t Flags;          // JointFlags
		float    AnchorA[3];     // A のローカルの点 (Transform の大きさも掛かる)
		float    Length;         // Distance : 保つ長さ (0 以下なら作った時の距離)
		float    AnchorB[3];     // B のローカルの点 (B がワールドならワールドの点)
		float    MinLength;      // Distance : 縮む限り (0 以下なら Length)
		float    MaxLength;      // Distance : 伸びる限り (0 以下なら Length)
		float    Hertz;          // Distance : ばねの振動数 (0 なら硬い棒) / Ball・Fixed : 0 なら既定の硬さ
		float    DampingRatio;   // ばねの減衰比
		float    BreakForce;     // この力 (N) を超えたら壊れる (0 なら壊れない)
	};
	static_assert(sizeof(JointDesc) == 64, "JointDesc must be 64 bytes (same as C#)");

	// ジョイントの状態 (C# の PhysicsNativeMethods.JointInfo と同じ並び : 16byte)
	struct JointInfo
	{
		uint32_t Broken;         // 1 : 壊れた (もう働かない)
		float    Force;          // 前の Simulate でかかった力 (N)
		float    Length;         // 今の 2 点の距離
		float    Padding;
	};
	static_assert(sizeof(JointInfo) == 16, "JointInfo must be 16 bytes (same as C#)");

	// 動いた体の位置 (C# の PhysicsNativeMethods.BodyTransform と同じ並び : 48byte)
	struct BodyTransform
	{
		uint32_t Handle;
		float    Position[3];
		float    Rotation[4];
		float    Scale[3];
		float    Padding;
	};
	static_assert(sizeof(BodyTransform) == 48, "BodyTransform must be 48 bytes (same as C#)");

	// 重なりのイベント (C# の PhysicsNativeMethods.CollisionEvent と同じ並び : 16byte)
	struct CollisionEvent
	{
		uint32_t BodyA;
		uint32_t BodyB;
		uint32_t Type;           // 0 : 重なり始めた (Enter) / 1 : 離れた (Exit)
		uint32_t Padding;
	};
	static_assert(sizeof(CollisionEvent) == 16, "CollisionEvent must be 16 bytes (same as C#)");

	// レイ (C# の PhysicsNativeMethods.Ray と同じ並び : 32byte)
	struct Ray
	{
		float    Origin[3];
		float    MaxDistance;
		float    Direction[3];   // 長さ 1 でなくてよい (中で直す)
		uint32_t LayerMask;      // 当てるレイヤー (ビット)
	};
	static_assert(sizeof(Ray) == 32, "Ray must be 32 bytes (same as C#)");

	// レイの当たり (C# の PhysicsNativeMethods.RaycastHit と同じ並び : 48byte)
	struct RaycastHit
	{
		uint32_t Body;           // 当たらなければ INVALID_HANDLE
		uint32_t Hit;            // 1 : 当たった
		float    Distance;
		float    Padding0;
		float    Point[3];
		float    Padding1;
		float    Normal[3];
		float    Padding2;
	};
	static_assert(sizeof(RaycastHit) == 48, "RaycastHit must be 48 bytes (same as C#)");

	// 接触点 (C# の PhysicsNativeMethods.ContactPoint と同じ並び : 48byte)。法線は BodyA → BodyB
	struct ContactOut
	{
		uint32_t BodyA;
		uint32_t BodyB;
		float    Depth;
		float    Padding0;
		float    Position[3];
		float    Padding1;
		float    Normal[3];
		float    Padding2;
	};
	static_assert(sizeof(ContactOut) == 48, "ContactOut must be 48 bytes (same as C#)");

	// 1 回の Step の計測 (C# の PhysicsNativeMethods.PhysicsStats と同じ並び : 32byte)
	struct PhysicsStats
	{
		uint32_t Bodies;         // 体の数
		uint32_t MovedBodies;    // この Step で動いた体
		uint32_t Candidates;     // AABB が重なった組 (形の判定をした数)
		uint32_t Overlaps;       // 今重なっている組
		float    BroadphaseMs;   // 候補を探す時間
		float    NarrowphaseMs;  // 形の判定の時間
		float    StepMs;         // Step 全体
		uint32_t Broadphase;     // Broadphase
		float    SyncMs;         // 前の Step から、動いた体の位置を受け取った時間 (SetTransforms。木を直す分)
		float    EventsMs;       // 重なりの記録とイベントの時間
		uint32_t Workers;        // 並列に使ったスレッドの数 (0 = 1 本)
		float    TreeMs;         // 動く物の木を直した時間 (Step の始め。入れ直し / 作り直し。BroadphaseMs に含む)
		float    SolverMs;       // 剛体の接触とジョイントを解いた時間 (Simulate。P1)
		uint32_t RigidBodies;    // 剛体 (Dynamic / Kinematic) の数
		uint32_t ContactConstraints;   // 解いた接触の組
		uint32_t Joints;         // 働いているジョイント
	};
	static_assert(sizeof(PhysicsStats) == 64, "PhysicsStats must be 64 bytes (same as C#)");
}

#endif // PHYSICS_COMMON_H_

/************************************
* End of file
************************************/
