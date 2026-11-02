/***********************************************************************************
* @file    raycast.h
* @brief   レイ (半直線) と形の交差 (Physics_Raycast)
* @details 形ごとに「当たった距離 (0〜maxDistance)」と「当たった面の法線」を求める
*            球       : 2 次方程式
*            箱       : 箱のローカルで 3 組の平面の間 (スラブ) を通る範囲
*            カプセル : 無限の円柱 (線分の範囲だけ) と、両端の球
*            メッシュ : 三角形の BVH をたどり、三角形ごとに Möller-Trumbore
*          レイの始点が形の中にある時は「当たらない」とする (中から外へは当たらない。Unity と同じ)
*
* @author  Keigo Onari
* @date    2026/11/02
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/
#pragma once
#ifndef PHYSICS_RAYCAST_H_
#define PHYSICS_RAYCAST_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "shapes.h"

namespace Physics
{
	// dir は長さ 1。当たれば distance と normal を書いて true (distance は maxDistance より手前の時だけ)
	bool RaycastShape(const WorldShape& shape, const Float3& origin, const Float3& dir, float maxDistance, float& distance, Float3& normal);

	// 箱 (AABB) とレイ : 入る距離 (当たらなければ false)。invDir = 1 / dir
	inline bool RaycastAabb(const Aabb& box, const Float3& origin, const Float3& invDir, float maxDistance, float& enter)
	{
		float t0 = 0.0f, t1 = maxDistance;
		const float o[3] = { origin.x, origin.y, origin.z };
		const float inv[3] = { invDir.x, invDir.y, invDir.z };
		const float mn[3] = { box.min.x, box.min.y, box.min.z };
		const float mx[3] = { box.max.x, box.max.y, box.max.z };
		for (int i = 0; i < 3; ++i)
		{
			float a = (mn[i] - o[i]) * inv[i];
			float b = (mx[i] - o[i]) * inv[i];
			if (a > b) std::swap(a, b);
			t0 = a > t0 ? a : t0;
			t1 = b < t1 ? b : t1;
			if (t0 > t1) return false;
		}
		enter = t0;
		return true;
	}
}

#endif // PHYSICS_RAYCAST_H_

/************************************
* End of file
************************************/
