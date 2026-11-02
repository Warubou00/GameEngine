/***********************************************************************************
* @file    shapes.h
* @brief   ワールドに置いた形 (球 / 箱 / カプセル) と、重なっているかの判定 (ナローフェーズ)
* @details C1 は「重なっているか」だけ (接触点・法線・めり込み量は C2)
*          - 球 / カプセル どうしは、中心 (点 / 線分) の距離と半径の和を比べる (速くて正確)
*          - 球と箱は、箱の中で球の中心に一番近い点を求める
*          - 箱を含むそれ以外は GJK (2 つの形の差 (ミンコフスキー差) が原点を含むか。凸な形なら何でも同じ方法で判定できる)
*
* @author  Keigo Onari
* @date    2026/11/02
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/
#pragma once
#ifndef PHYSICS_SHAPES_H_
#define PHYSICS_SHAPES_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "common.h"
#include "mathTypes.h"

namespace Physics
{
	// ワールドに置いた形 (大きさ・回転を掛けた後)
	struct WorldShape
	{
		ShapeType Type = ShapeType::Sphere;
		Float3    Center;       // ワールドの中心
		Rotation3 Rotation;     // 箱 / カプセルの向き
		Float3    HalfExtents;  // 箱 : 半分の大きさ
		float     Radius     = 0.0f;   // 球 / カプセル
		float     HalfHeight = 0.0f;   // カプセル : 円柱の半分の高さ (ローカルの Y)

		Aabb ComputeAabb() const;
		// 方向 d に一番遠い点 (GJK で使う)
		Float3 Support(Float3 d) const;
	};

	// 重なっているか (接していても重なり)
	bool Overlap(const WorldShape& a, const WorldShape& b);
}

#endif // PHYSICS_SHAPES_H_

/************************************
* End of file
************************************/
