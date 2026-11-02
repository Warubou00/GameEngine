/***********************************************************************************
* @file    physicsWorld.h
* @brief   衝突判定のワールド (体の表 / ブロードフェーズ / 重なりの記録とイベント)
* @details 1 回の Step の流れ (C3 で組の表を持ち続ける形にした)
*            0. SetTransforms : 動いた体の形と箱を作り直す (並列)。太らせた箱からはみ出した物だけ、木の葉の箱を広げる
*               太らせた箱は、動いた向きにも (ずれ × 4) 広げる。まっすぐ動く物は、しばらく木を直さなくてよい
*            1. 動く物の木を直す : はみ出した葉が動く物の 1/16 以上なら中の節を全部作り直す (Morton 符号の順。並列)。
*               少なければ 1 つずつ入れ直す
*            2. ブロードフェーズ : 太らせた箱が重なる組を「組の表」に入れる (表は Step をまたいで持つ)
*                 BVH   : 太らせた箱が変わった体だけを、動く物の木 / 動かない物の木に問い合わせる
*                         変わった体が動く物の 1/4 以上なら、木の節の組を 1 回たどって全部の組を探し直す
*                 Brute : 全部の組 (比べる用) / Grid : 動く物を均一なグリッドに入れて、同じマスの物どうし (比べる用)
*               どちらの木も、節にレイヤーのビットを持ち、当たりえないレイヤーの枝を飛ばす (レイヤーの衝突の表)
*            3. ナローフェーズ : 表の組のうち、どちらかが動いた組だけ、形の接触 (法線 / めり込み量 / 接触点) を求める (並列)
*               接触は組が持つ (動いていない組は前の接触のまま。物理 (P1) が使う)
*            4. 形の重なりが変わった組を、重なり始め (Enter) / 離れた (Exit) のイベントにする。太らせた箱が離れた組は表から消す
*          以前 (C1 / C2) は、毎 Step 動いた体から組を探し直し、unordered_set / unordered_map の表と比べていた
*          体はハンドル (番号 16bit + 世代 16bit) で渡す。古いハンドルは世代が違うので何もしない
*
* @author  Keigo Onari
* @date    2026/11/02
* @version v_1.1.0 (2026/11/03 C3 : 組の表 / レイヤーの衝突の表 / 木の作り直し / 並列化)
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
#include <algorithm>
#include "common.h"
#include "shapes.h"
#include "contacts.h"
#include "meshShape.h"
#include "dynamicTree.h"
#include "jobSystem.h"

namespace Physics
{
	class PhysicsWorld
	{
	public:
		PhysicsWorld();
		uint32_t CreateBody(const BodyDesc& desc);
		void     DestroyBody(uint32_t handle);
		void     SetTransforms(const BodyTransform* items, uint32_t count);
		void     SetBroadphase(Broadphase mode);
		// レイヤーの衝突の表 (Unity の Layer Collision Matrix と同じ)。a と b のレイヤーの物どうしを調べるか
		// 体ごとの CollideMask と両方で許された組だけ調べる。表を変えると全部の組を調べ直す
		void     SetLayerCollision(uint32_t a, uint32_t b, bool collide);
		uint32_t GetLayerCollisionMask(uint32_t layer) const { return layer < 32 ? _layerMatrix[layer] : 0u; }
		// 比べる用の切り替え (1 : 木の節のレイヤーのビットで枝を飛ばす)
		void     SetOption(uint32_t option, uint32_t value);

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
			uint32_t   Mask     = 0xFFFFFFFFu;   // 当たる相手のレイヤー (体の CollideMask と レイヤーの表 の両方)
			uint32_t   RequestedMask = 0xFFFFFFFFu;   // 体の CollideMask (表を変えた時に作り直すため)
			ShapeType  Shape    = ShapeType::Sphere;
			Float3     LocalCenter;
			Float3     Size;
			WorldShape World;
			Aabb       Box;
			int        Proxy     = DynamicTree::NULL_NODE;
			uint32_t   MovedStep = 0;   // この Step で動いたか (= _stepIndex)
			uint32_t   EnlargedStep = 0;    // 木の葉 (太らせた箱) が変わった Step (= _stepIndex なら、新しい組を探す)
			std::shared_ptr<const TriangleMesh> MeshData;   // メッシュの形 (共有)
			std::unique_ptr<MeshInstance>       MeshInst;   // この体のワールドの三角形と BVH
		};

		std::vector<Body>     _bodies;
		std::vector<uint32_t> _freeBodies;
		std::vector<uint32_t> _moved;        // 前の Step から動いた体 (番号)
		std::vector<uint32_t> _enlarged;     // 前の Step から、木の葉 (太らせた箱) が変わった体 (番号)
		DynamicTree           _staticTree;
		DynamicTree           _dynamicTree;
		Broadphase            _mode = Broadphase::Bvh;
		uint32_t              _stepIndex = 1;
		uint32_t              _dynamicCount = 0;   // 動く物の数 (動いた割合で、木の調べ方を選ぶ)
		uint32_t              _layerMatrix[32];    // レイヤーごとの「当たる相手のレイヤー」(ビット)
		bool                  _layerPruning = true;
		bool                  _treeRebuild  = true;   // 多く動いた時は木を作り直す (切ると 1 つずつ入れ直す)
		bool                  _forceFull = true;   // 次の Step は全部の組を探し直す (体を作った直後 / 表や方法を変えた時)
		JobSystem             _jobs;                  // 並列化 (形の判定 / 位置の受け取り)
		std::vector<Aabb>     _previousBoxes;         // SetTransforms の作業用 (動く前の箱)
		float                 _predictScale = 4.0f;   // 太らせた箱を、動いた向きに (ずれ × これ) 広げる (0 で切る)
		uint32_t              _fullPassDivisor = 4;    // 木の葉が変わった体が 動く物の 1/N 以上なら、全部の組を探し直す
		uint32_t              _rebuildDivisor  = 16;   // はみ出した葉が 動く物の 1/N 以上なら、木を作り直す (計測で 16 が良かった)
		bool                  _mortonBuild     = true; // 木の作り直しを Morton 符号の順で行う (速い。木の質は軸の真ん中とほぼ同じ)

		// ==============================================================
		// 組の表 (C3) : 太らせた箱が重なっている組を、Step をまたいで持ち続ける (Box2D の pair / Bullet の pair cache と同じ)
		//   新しい組ができるのは、太らせた箱が変わった体 (_enlarged) の所だけなので、毎 Step 探すのはその体だけでよい
		//   形の判定は、表の組のうち、どちらかが動いた組だけ。接触 (法線は小さい番号 → 大きい番号) も組が持つ
		//   以前は毎 Step、動いた体から組を探し直し、unordered_set (重なり) と unordered_map (接触) に入れ直していた
		// ==============================================================
		struct Pair
		{
			uint64_t Key = 0;            // 番号の小さい方 << 32 | 大きい方
			uint32_t A = 0, B = 0;       // 体の番号 (A < B)
			uint32_t SeenStep = 0;       // 全部を探し直した時 : この Step で見つかったか
			uint8_t  Touching = 0;       // 形が重なっている
			uint8_t  IsNew    = 1;       // 表に入ったばかり (必ず形を調べる)
			uint8_t  Action   = 0;       // 作業用 : 0 そのまま / 1 表から消す / 2 形を調べた
			uint8_t  Hit      = 0;       // 作業用 : 形を調べた結果
			Manifold Contact;
		};

		// 組の番号 → _pairs の場所 (開番地法のハッシュ表。消す時は後ろを詰める (墓石を残さない))
		class PairMap
		{
		public:
			static constexpr uint64_t EMPTY = ~0ull;
			uint32_t* Find(uint64_t key)
			{
				if (_keys.empty()) return nullptr;
				for (uint32_t i = hash(key) & _mask;; i = (i + 1) & _mask)
				{
					if (_keys[i] == key)   return &_values[i];
					if (_keys[i] == EMPTY) return nullptr;
				}
			}
			// 無ければ入れる (inserted = true。値は呼んだ側が入れる)
			uint32_t* FindOrInsert(uint64_t key, bool& inserted)
			{
				if ((_count + 1) * 2 > _keys.size()) grow();
				for (uint32_t i = hash(key) & _mask;; i = (i + 1) & _mask)
				{
					if (_keys[i] == key) { inserted = false; return &_values[i]; }
					if (_keys[i] == EMPTY) { _keys[i] = key; _count++; inserted = true; return &_values[i]; }
				}
			}
			void Erase(uint64_t key)
			{
				if (_keys.empty()) return;
				uint32_t i = hash(key) & _mask;
				while (_keys[i] != key) { if (_keys[i] == EMPTY) return; i = (i + 1) & _mask; }
				// 後ろの続きを、元の場所 (hash) から見て i を越えない物だけ前へ詰める
				for (uint32_t j = (i + 1) & _mask; _keys[j] != EMPTY; j = (j + 1) & _mask)
				{
					const uint32_t home = hash(_keys[j]) & _mask;
					const bool stay = i <= j ? (i < home && home <= j) : (i < home || home <= j);
					if (stay) continue;
					_keys[i] = _keys[j]; _values[i] = _values[j];
					i = j;
				}
				_keys[i] = EMPTY;
				_count--;
			}
		private:
			std::vector<uint64_t> _keys;
			std::vector<uint32_t> _values;
			uint32_t _count = 0, _mask = 0;
			static uint32_t hash(uint64_t k) { k ^= k >> 33; k *= 0xFF51AFD7ED558CCDull; k ^= k >> 33; return static_cast<uint32_t>(k); }
			void grow()
			{
				std::vector<uint64_t> keys = std::move(_keys);
				std::vector<uint32_t> values = std::move(_values);
				const size_t size = (std::max)(size_t(64), keys.size() * 2);
				_keys.assign(size, EMPTY);
				_values.assign(size, 0u);
				_mask = static_cast<uint32_t>(size - 1);
				_count = 0;
				for (size_t i = 0; i < keys.size(); ++i)
				{
					if (keys[i] == EMPTY) continue;
					bool inserted;
					*FindOrInsert(keys[i], inserted) = values[i];
				}
			}
		};

		std::vector<Pair>            _pairs;
		PairMap                      _pairMap;
		uint32_t                     _touchingCount = 0;

		// メッシュ (番号 + 1 をハンドルにする。0 は無し)
		std::vector<std::shared_ptr<TriangleMesh>> _meshes;
		std::vector<CollisionEvent>  _enterEvents; // 作業用 (重なり始めた。離れたの後に並べる)
		std::vector<CollisionEvent>  _events;
		std::vector<CollisionEvent>  _pendingEvents;   // 体を消した時の「離れた」(次の Step で返す)
		PhysicsStats                 _stats = {};
		float                        _syncMs = 0.0f;   // SetTransforms の時間 (Step で _stats に移す)

		// Grid の作業用
		struct GridEntry { uint64_t Cell; uint32_t Body; };
		std::vector<GridEntry> _grid;

		static uint64_t pairKey(uint32_t a, uint32_t b) { return a < b ? (uint64_t(a) << 32) | b : (uint64_t(b) << 32) | a; }
		uint32_t handleOf(uint32_t index) const { return (uint32_t(_bodies[index].Version) << 16) | index; }
		Body*    find(uint32_t handle);
		void     updateShape(Body& body, const float position[3], const float rotation[4], const float scale[3]);
		float    marginOf(const Body& body) const;
		bool     canCollide(const Body& a, const Body& b) const;
		void     testPair(uint32_t a, uint32_t b);   // (Brute / Grid) 本当の箱が重なっていれば組の表へ
		void     addPair(uint32_t a, uint32_t b);    // 組の表へ入れる (あれば「見つかった」の印だけ)
		void     noteEnlarged(uint32_t index);       // 木の葉が変わった体として覚える
		const Aabb& fatBoxOf(const Body& body) const { return (body.IsStatic ? _staticTree : _dynamicTree).FatBox(body.Proxy); }
		void     insertProxy(uint32_t index);
		void     markAllMoved();   // 次の Step で全部を調べ直す
		void     fixDynamicTree(); // はみ出した葉を入れ直す / 多ければ木を作り直す
		void     removeProxy(uint32_t index);

		bool     findPairsBvh();   // 戻り値 : 全部の組を探し直したか
		void     findPairsBrute();
		void     findPairsGrid();
		void     updatePairs(bool fullPass, float& narrowMs, float& eventsMs);   // 形の判定とイベント
	};
}

#endif // PHYSICS_WORLD_H_

/************************************
* End of file
************************************/
