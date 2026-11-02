/***********************************************************************************
* @file    meshShape.h
* @brief   メッシュコライダー (動かない物だけ。地面や岩を形どおりに当てる)
* @details TriangleMesh : 読み込んだ形 (ローカルの頂点とインデックス)。同じモデルの体どうしで共有する
*          MeshInstance : 体ごとのワールドの三角形と、その BVH (三角形 1 つを葉にした動的 AABB ツリー)
*                         動かない物なので、作った時 (と動かされた時) に 1 回だけ作る。
*                         判定のたびにローカルへ戻す計算が要らない (大きさが不均一でもそのまま使える)
*
* @author  Keigo Onari
* @date    2026/11/02
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/
#pragma once
#ifndef PHYSICS_MESH_SHAPE_H_
#define PHYSICS_MESH_SHAPE_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <memory>
#include <vector>
#include "mathTypes.h"
#include "dynamicTree.h"

namespace Physics
{
	struct TriangleMesh
	{
		std::vector<Float3>   Positions;
		std::vector<uint32_t> Indices;    // 3 つで三角形 1 つ
		uint32_t TriangleCount() const { return static_cast<uint32_t>(Indices.size() / 3); }
	};

	struct MeshInstance
	{
		std::shared_ptr<const TriangleMesh> Source;
		std::vector<Float3> World;    // ワールドの頂点 (Source の Positions と同じ並び)
		std::vector<Float3> Normals;  // 三角形ごとの法線 (長さ 1。面積 0 の三角形は 0)
		DynamicTree         Tree;     // 葉 = 三角形の番号
		Aabb                Box;

		// 位置 / 回転 / 大きさを掛けたワールドの三角形と BVH を作り直す
		void Build(const Float3& position, const Rotation3& rotation, const Float3& scale);

		void Triangle(uint32_t t, Float3& a, Float3& b, Float3& c) const
		{
			const uint32_t* idx = &Source->Indices[t * 3];
			a = World[idx[0]]; b = World[idx[1]]; c = World[idx[2]];
		}
	};
}

#endif // PHYSICS_MESH_SHAPE_H_

/************************************
* End of file
************************************/
