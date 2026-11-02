/***********************************************************************************
* @file    physicsDll.cpp
* @brief   Physics.dll が C# のエンジン (GameEngine.Runtime) へ export する関数
* @details C# 側の宣言は GameEngine.Runtime/Native/PhysicsNativeMethods.cs
*          - 体はハンドル (uint32) でやり取りする。ポインタは C# に渡さない
*          - 動いた体の位置は配列でまとめて 1 回 (SetTransforms)、イベントも配列でまとめて 1 回 (GetEvents)
*          - 呼び出しは C# のメインスレッドから (Audio.dll と同じ形)
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
#include <algorithm>
#include <cstring>
#include "common.h"
#include "physicsWorld.h"

namespace
{
	Physics::PhysicsWorld* g_world = nullptr;
}

extern "C"
{
	PHYSICS_API bool Physics_Initialize()
	{
		if (g_world == nullptr) g_world = new Physics::PhysicsWorld();
		return true;
	}

	PHYSICS_API void Physics_Shutdown()
	{
		delete g_world;
		g_world = nullptr;
	}

	// 体を作る。戻り値はハンドル (失敗時 INVALID_HANDLE : 体の数が 65535 を超えた)
	PHYSICS_API uint32_t Physics_CreateBody(const Physics::BodyDesc* desc)
	{
		return (g_world && desc) ? g_world->CreateBody(*desc) : Physics::INVALID_HANDLE;
	}

	PHYSICS_API void Physics_DestroyBody(uint32_t body)
	{
		if (g_world) g_world->DestroyBody(body);
	}

	// 動いた体の位置 / 回転 / 大きさ (まとめて)
	PHYSICS_API void Physics_SetTransforms(const Physics::BodyTransform* items, uint32_t count)
	{
		if (g_world && items) g_world->SetTransforms(items, count);
	}

	// 重なりを探す方法 (0 BVH / 1 総当たり / 2 グリッド)。比べる用
	PHYSICS_API void Physics_SetBroadphase(uint32_t mode)
	{
		if (g_world && mode <= 2) g_world->SetBroadphase(static_cast<Physics::Broadphase>(mode));
	}

	// レイヤーの衝突の表 (Unity の Layer Collision Matrix と同じ)。a / b は 0〜31
	PHYSICS_API void Physics_SetLayerCollision(uint32_t a, uint32_t b, bool collide)
	{
		if (g_world) g_world->SetLayerCollision(a, b, collide);
	}

	// レイヤーが当たる相手のレイヤー (ビット)
	PHYSICS_API uint32_t Physics_GetLayerCollisionMask(uint32_t layer)
	{
		return g_world ? g_world->GetLayerCollisionMask(layer) : 0u;
	}

	// 比べる用の切り替え (1 : 木の節のレイヤーのビットで枝を飛ばす (既定 1) / 2 : 並列に使うスレッドの数 /
	//                     3 : 多く動いた時に動く物の木を作り直す (既定 1。0 なら 1 つずつ入れ直す) /
	//                     4 : 木の葉が変わった体が動く物の 1/N 以上なら、全部の組を探し直す (既定 4。0 ならいつも) /
	//                     5 : 太らせた箱を動いた向きに広げる倍率 (既定 4。0 で切る) /
	//                     6 : はみ出した葉が動く物の 1/N 以上なら木を作り直す (既定 16) /
	//                     7 : 木の作り直しを Morton 符号の順で行う (既定 1。0 なら節ごとに軸の真ん中で分ける))
	PHYSICS_API void Physics_SetOption(uint32_t option, uint32_t value)
	{
		if (g_world) g_world->SetOption(option, value);
	}

	// 重なりを調べ直す。戻り値はイベントの数 (Physics_GetEvents で受け取る)
	PHYSICS_API uint32_t Physics_Step()
	{
		return g_world ? g_world->Step() : 0;
	}

	// イベントを out へ写す (最大 capacity 個)。戻り値は写した数
	PHYSICS_API uint32_t Physics_GetEvents(Physics::CollisionEvent* out, uint32_t capacity)
	{
		if (!g_world || !out) return 0;
		const auto& events = g_world->GetEvents();
		const uint32_t count = (std::min)(capacity, static_cast<uint32_t>(events.size()));
		if (count > 0) std::memcpy(out, events.data(), sizeof(Physics::CollisionEvent) * count);
		return count;
	}

	PHYSICS_API void Physics_GetStats(Physics::PhysicsStats* out)
	{
		if (g_world && out) *out = g_world->GetStats();
	}

	// ==============================================================
	// メッシュ (動かない物の形)
	// ==============================================================

	// positions : xyz × vertexCount / indices : 3 つで三角形 1 つ。戻り値はハンドル (BodyDesc.Mesh に入れる)
	PHYSICS_API uint32_t Physics_CreateMesh(const float* positions, uint32_t vertexCount, const uint32_t* indices, uint32_t indexCount)
	{
		return g_world ? g_world->CreateMesh(positions, vertexCount, indices, indexCount) : Physics::INVALID_HANDLE;
	}

	PHYSICS_API void Physics_DestroyMesh(uint32_t mesh)
	{
		if (g_world) g_world->DestroyMesh(mesh);
	}

	// ==============================================================
	// 問い合わせ (Step の後の位置で答える)
	// ==============================================================

	// 一番近い当たり。当たらなければ false (hit->Body = INVALID_HANDLE)
	PHYSICS_API bool Physics_Raycast(const Physics::Ray* ray, Physics::RaycastHit* hit)
	{
		if (!g_world || !ray || !hit) return false;
		return g_world->Raycast(*ray, *hit);
	}

	// まとめて (count 本)。戻り値は当たった数
	PHYSICS_API uint32_t Physics_RaycastBatch(const Physics::Ray* rays, uint32_t count, Physics::RaycastHit* hits)
	{
		if (!g_world || !rays || !hits) return 0;
		uint32_t hitCount = 0;
		for (uint32_t i = 0; i < count; ++i)
		{
			if (g_world->Raycast(rays[i], hits[i])) hitCount++;
		}
		return hitCount;
	}

	// 球と重なる体 (out に最大 capacity 個)。戻り値は重なっている全部の数
	PHYSICS_API uint32_t Physics_OverlapSphere(const float center[3], float radius, uint32_t layerMask, uint32_t* out, uint32_t capacity)
	{
		if (!g_world || !center) return 0;
		Physics::WorldShape shape;
		shape.Type   = Physics::ShapeType::Sphere;
		shape.Center = Physics::Float3(center[0], center[1], center[2]);
		shape.Radius = radius;
		return g_world->OverlapShape(shape, layerMask, out, capacity);
	}

	// 回した箱と重なる体
	PHYSICS_API uint32_t Physics_OverlapBox(const float center[3], const float halfExtents[3], const float rotation[4], uint32_t layerMask,
	                                        uint32_t* out, uint32_t capacity)
	{
		if (!g_world || !center || !halfExtents || !rotation) return 0;
		Physics::WorldShape shape;
		shape.Type        = Physics::ShapeType::Box;
		shape.Center      = Physics::Float3(center[0], center[1], center[2]);
		shape.HalfExtents = Physics::Float3(halfExtents[0], halfExtents[1], halfExtents[2]);
		shape.Rotation    = Physics::Rotation3::FromQuaternion(rotation[0], rotation[1], rotation[2], rotation[3]);
		return g_world->OverlapShape(shape, layerMask, out, capacity);
	}

	// 今重なっている組の接触点 (法線は BodyA → BodyB)。out が nullptr なら数だけ返す
	PHYSICS_API uint32_t Physics_GetContacts(Physics::ContactOut* out, uint32_t capacity)
	{
		return g_world ? g_world->GetContacts(out, capacity) : 0;
	}
}

/************************************
* End of file
************************************/
