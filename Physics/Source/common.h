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
		float    Padding1;
		float    Position[3];
		float    Padding2;
		float    Rotation[4];    // クォータニオン (x, y, z, w)
		float    Scale[3];
		float    Padding3;
	};
	static_assert(sizeof(BodyDesc) == 96, "BodyDesc must be 96 bytes (same as C#)");

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
	};
	static_assert(sizeof(PhysicsStats) == 48, "PhysicsStats must be 48 bytes (same as C#)");
}

#endif // PHYSICS_COMMON_H_

/************************************
* End of file
************************************/
