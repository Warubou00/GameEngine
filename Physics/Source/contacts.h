/***********************************************************************************
* @file    contacts.h
* @brief   接触の情報 (法線 / めり込み量 / 接触点) を求める (ナローフェーズ)
* @details 法線は A → B の向き (B を法線の向きに depth だけ動かすと離れる)。接触点は最大 4 つ、1 点ごとに法線を持つ
*          (メッシュは三角形ごとに向きが違うため)。物理 (P1) はこの点ごとに押し戻す
*          形の組ごとの方法
*            球 / カプセル どうし : 芯 (点 / 線分) の一番近い点どうし
*            箱と球              : 箱の中の一番近い点 (中心が箱の中なら一番近い面)
*            箱とカプセル         : 線分の上で箱に一番近い点 (箱に寝ていれば両端も)。芯が箱に刺さっていたら分離軸
*            箱どうし             : 分離軸 (15 本) で一番浅い軸。面なら相手の面を切り取って最大 4 点 / 辺どうしなら 1 点
*            メッシュと球 / カプセル / 箱 : 重なる三角形ごとに上と同じ考え方で求め、深い順に 4 点まで残す
*
* @author  Keigo Onari
* @date    2026/11/02
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/
#pragma once
#ifndef PHYSICS_CONTACTS_H_
#define PHYSICS_CONTACTS_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "shapes.h"

namespace Physics
{
	struct ContactPoint
	{
		Float3 Position;
		Float3 Normal;     // A → B
		float  Depth = 0;  // めり込み量 (0 以上)
	};

	struct Manifold
	{
		static constexpr int MAX_POINTS = 4;
		ContactPoint Points[MAX_POINTS];
		int Count = 0;

		void Add(const Float3& position, const Float3& normal, float depth);
		void Flip();   // A と B を入れ替える (法線を逆に)
	};

	// 2 つの形の接触を求める (重なっていなければ false。接しているだけ (深さ 0) は true)
	bool Collide(const WorldShape& a, const WorldShape& b, Manifold& out);
}

#endif // PHYSICS_CONTACTS_H_

/************************************
* End of file
************************************/
