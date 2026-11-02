/***********************************************************************************
* @file    meshShape.cpp
* @brief   メッシュコライダー。説明は meshShape.h
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
#include "meshShape.h"

namespace Physics
{
	void MeshInstance::Build(const Float3& position, const Rotation3& rotation, const Float3& scale)
	{
		const TriangleMesh& mesh = *Source;
		World.resize(mesh.Positions.size());
		Box = { Float3(FLT_MAX, FLT_MAX, FLT_MAX), Float3(-FLT_MAX, -FLT_MAX, -FLT_MAX) };
		for (size_t i = 0; i < mesh.Positions.size(); ++i)
		{
			World[i] = position + rotation.Apply(mul(mesh.Positions[i], scale));
			Box.min = minOf(Box.min, World[i]);
			Box.max = maxOf(Box.max, World[i]);
		}

		// 三角形ごとの法線と BVH (木は作り直す。動かない物なので作る時だけ)
		Tree = DynamicTree();
		const uint32_t count = mesh.TriangleCount();
		Normals.resize(count);
		for (uint32_t t = 0; t < count; ++t)
		{
			Float3 a, b, c;
			Triangle(t, a, b, c);
			const Float3 n = cross(b - a, c - a);
			const float len = std::sqrt(lengthSq(n));
			Normals[t] = len > 1e-12f ? n * (1.0f / len) : Float3();
			const Aabb box = { minOf(minOf(a, b), c), maxOf(maxOf(a, b), c) };
			Tree.CreateProxy(box, 0.0f, t);   // 動かないので太らせない
		}
	}
}

/************************************
* End of file
************************************/
