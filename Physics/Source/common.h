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
		float    Padding0;
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
	};
	static_assert(sizeof(PhysicsStats) == 32, "PhysicsStats must be 32 bytes (same as C#)");
}

#endif // PHYSICS_COMMON_H_

/************************************
* End of file
************************************/
