/***********************************************************************************
* @file    physicsWorld.h
* @brief   衝突判定のワールド (体の表 / ブロードフェーズ / 重なりの記録とイベント)
* @details 1 回の Step の流れ
*            1. 動いた体の箱 (AABB) を作り直す (SetTransforms で受け取った物だけ)
*            2. ブロードフェーズ : 箱が重なっている組 (候補) を探す
*                 BVH   : 動かない物の木と動く物の木の 2 本。動いた体だけを両方の木に問い合わせる
//                         (動いていない物どうしの組は、前の結果のまま)
*                         動いた物が動く物の 1/4 以上の時は、1 個ずつ問い合わせずに、木の節の組を 1 回たどって全部の組を探す
*                 Brute : 全部の組 (比べる用)
*                 Grid  : 動く物を均一なグリッドに入れて、同じマスの物どうし (比べる用)
*            3. ナローフェーズ : 候補の形の接触 (法線 / めり込み量 / 接触点) を求める (contacts.cpp)
*               重なっている組の接触はこの表に残す (動いていない組は前の接触のまま。物理 (P1) が使う)
*               以前 (C1) は「重なっているか」だけだった
*            4. 前の Step の「重なっている組」と比べて、重なり始め (Enter) / 離れた (Exit) をイベントにする
*          体はハンドル (番号 16bit + 世代 16bit) で渡す。古いハンドルは世代が違うので何もしない
*
* @author  Keigo Onari
* @date    2026/11/02
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/
#pragma once
#ifndef PHYSICS_WORLD_H_
#define PHYSICS_WORLD_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <memory>
#include <vector>
#include <unordered_map>
#include <unordered_set>
#include "common.h"
#include "shapes.h"
#include "contacts.h"
#include "meshShape.h"
#include "dynamicTree.h"

namespace Physics
{
	class PhysicsWorld
	{
	public:
		uint32_t CreateBody(const BodyDesc& desc);
		void     DestroyBody(uint32_t handle);
		void     SetTransforms(const BodyTransform* items, uint32_t count);
		void     SetBroadphase(Broadphase mode);

		// 重なりを調べ直して、イベントを作る (戻り値 : イベントの数)
		uint32_t Step();
		const std::vector<CollisionEvent>& GetEvents() const { return _events; }
		const PhysicsStats& GetStats() const { return _stats; }

		// メッシュ (動かない物の形)。同じメッシュを複数の体で使える
		uint32_t CreateMesh(const float* positions, uint32_t vertexCount, const uint32_t* indices, uint32_t indexCount);
		void     DestroyMesh(uint32_t mesh);

		// 問い合わせ (Step の後の位置で答える)
		bool     Raycast(const Ray& ray, RaycastHit& hit) const;
		uint32_t OverlapShape(const WorldShape& shape, uint32_t layerMask, uint32_t* out, uint32_t capacity) const;

		// 今重なっている組の接触点 (out が nullptr なら数だけ)
		uint32_t GetContacts(ContactOut* out, uint32_t capacity) const;

	private:
		struct Body
		{
			bool       Alive    = false;
			uint16_t   Version  = 0;
			bool       IsStatic = false;
			uint32_t   Layer    = 0;
			uint32_t   Mask     = 0xFFFFFFFFu;
			ShapeType  Shape    = ShapeType::Sphere;
			Float3     LocalCenter;
			Float3     Size;
			WorldShape World;
			Aabb       Box;
			int        Proxy     = DynamicTree::NULL_NODE;
			uint32_t   MovedStep = 0;   // この Step で動いたか (= _stepIndex)
			std::shared_ptr<const TriangleMesh> MeshData;   // メッシュの形 (共有)
			std::unique_ptr<MeshInstance>       MeshInst;   // この体のワールドの三角形と BVH
		};

		std::vector<Body>     _bodies;
		std::vector<uint32_t> _freeBodies;
		std::vector<uint32_t> _moved;        // 前の Step から動いた体 (番号)
		DynamicTree           _staticTree;
		DynamicTree           _dynamicTree;
		Broadphase            _mode = Broadphase::Bvh;
		uint32_t              _stepIndex = 1;
		uint32_t              _dynamicCount = 0;   // 動く物の数 (動いた割合で、木の調べ方を選ぶ)
		bool                  _fullPass = false;   // この Step は全部の組を調べ直したか (イベントを全部と比べる)

		// 今重なっている組 (番号の小さい方 << 32 | 大きい方) と、その接触 (法線は小さい番号 → 大きい番号)
		std::unordered_set<uint64_t> _overlaps;
		std::unordered_map<uint64_t, Manifold> _manifolds;
		std::vector<Manifold>        _candidateManifolds;   // ナローフェーズの結果 (_candidates と同じ並び。使い回す)

		// メッシュ (番号 + 1 をハンドルにする。0 は無し)
		std::vector<std::shared_ptr<TriangleMesh>> _meshes;
		std::vector<uint64_t>        _candidates;  // この Step で箱が重なった組 (形の判定をする。使い回す)
		std::vector<uint64_t>        _found;       // この Step で重なっていた組 (使い回す)
		std::vector<uint64_t>        _exits;
		std::vector<CollisionEvent>  _events;
		std::vector<CollisionEvent>  _pendingEvents;   // 体を消した時の「離れた」(次の Step で返す)
		PhysicsStats                 _stats = {};

		// Grid の作業用
		struct GridEntry { uint64_t Cell; uint32_t Body; };
		std::vector<GridEntry> _grid;

		static uint64_t pairKey(uint32_t a, uint32_t b) { return a < b ? (uint64_t(a) << 32) | b : (uint64_t(b) << 32) | a; }
		uint32_t handleOf(uint32_t index) const { return (uint32_t(_bodies[index].Version) << 16) | index; }
		Body*    find(uint32_t handle);
		void     updateShape(Body& body, const float position[3], const float rotation[4], const float scale[3]);
		float    marginOf(const Body& body) const;
		bool     canCollide(const Body& a, const Body& b) const;
		void     testPair(uint32_t a, uint32_t b);   // 箱と形を調べて、重なっていれば _found へ
		void     insertProxy(uint32_t index);
		void     removeProxy(uint32_t index);

		void     findPairsBvh();
		void     findPairsBrute();
		void     findPairsGrid();
		void     emitEvents(bool incremental);
	};
}

#endif // PHYSICS_WORLD_H_

/************************************
* End of file
************************************/
