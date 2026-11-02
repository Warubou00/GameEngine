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
}

/************************************
* End of file
************************************/
