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
#include "dynamics.h"
#include "joints.h"
#include <unordered_map>

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

		// ---------- 剛体 (P1。solver.cpp) ----------
		// 体を剛体にする / 値を変える (Motion = None で剛体でなくなる)。動かない物 (IsStatic) は剛体にできない
		void     SetBodyDynamics(uint32_t handle, const BodyDynamics& dynamics);
		// 力 / 力積 / 速度 / キネマティックの目標 (まとめて)
		void     ApplyCommands(const BodyCommand* commands, uint32_t count);
		// dt 秒進める : 重なりを調べ (Step と同じ。イベントも作る)、接触とジョイントを解いて剛体を動かす
		uint32_t Simulate(float dt);
		// 剛体の今の状態 (out が nullptr なら数だけ)
		uint32_t GetBodyStates(BodyState* out, uint32_t capacity) const;
		void     SetSolverSettings(const SolverSettings& settings) { _solver = settings; }
		const SolverSettings& GetSolverSettings() const { return _solver; }

		// ジョイント (ハンドル = 番号 16bit + 世代 16bit)
		uint32_t CreateJoint(const JointDesc& desc);
		void     DestroyJoint(uint32_t handle);
		bool     GetJointInfo(uint32_t handle, JointInfo& info) const;

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
			// 形の判定で毎回見る剛体の値は前の方に置く (後ろの剛体の値と別のキャッシュの行にならないように)
			MotionType Motion    = MotionType::None;   // P1
			bool       IsTrigger = false;   // 重なりのイベントだけ (押し戻さない。P1)
			std::shared_ptr<const TriangleMesh> MeshData;   // メッシュの形 (共有)
			std::unique_ptr<MeshInstance>       MeshInst;   // この体のワールドの三角形と BVH

			// ---------- 剛体 (P1) ----------
			// 剛体だけが使う値は _rigidData (RigidSlot の場所) に分けた (体の表を小さく保ち、衝突判定だけの時に遅くしない)
			float      Friction    = 0.5f;
			float      Restitution = 0.0f;
			Float3     Position;              // Transform の位置 / 回転 / 大きさ (最後に受け取った / 動かした値)
			Quat       Rotation;
			Float3     Scale = Float3(1.0f, 1.0f, 1.0f);
			int        Solver    = -1;        // _solverBodies の場所 (剛体でなければ -1)
			int        RigidSlot = -1;        // _rigidBodies / _rigidData の場所
		};

		// 剛体の値 (Dynamic / Kinematic の体だけ。_rigidBodies と同じ並び)
		struct RigidData
		{
			BodyDynamics Dynamics {};
			float      InvMass = 0.0f;
			Float3     InvInertiaLocal;       // ローカルの対角の慣性の逆数
			Float3     Velocity;              // 重心の速度
			Float3     AngularVelocity;       // ワールド
			Float3     Force;                 // 次の Simulate でかける力 (終わったら 0)
			Float3     Torque;
			bool       HasTarget = false;     // キネマティックの目標
			bool       ClearVelocityAfterStep = false;   // 目標へ動いた後は止まる
			Float3     TargetPosition;
			Quat       TargetRotation;
			Float3     LinearMask  = Float3(1.0f, 1.0f, 1.0f);   // 固定した軸は 0
			Float3     AngularMask = Float3(1.0f, 1.0f, 1.0f);
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
			uint8_t  Physical = 0;       // 剛体の接触として解く (少し離れていても点がある。P1)
			Manifold Contact;            // 剛体の組は、少し離れた点 (深さが負) も入る
		};
		// 前の Simulate の力積 (次の始めにかけ直す。_pairs と同じ並び。組の表を小さく保つため分けた)
		struct PairImpulse
		{
			float  Normal[Manifold::MAX_POINTS] = {};
			Float3 Friction[Manifold::MAX_POINTS];
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
		std::vector<PairImpulse>     _pairImpulses;
		PairMap                      _pairMap;
		uint32_t                     _touchingCount = 0;

		// メッシュ (番号 + 1 をハンドルにする。0 は無し)
		std::vector<std::shared_ptr<TriangleMesh>> _meshes;
		std::vector<CollisionEvent>  _enterEvents; // 作業用 (重なり始めた。離れたの後に並べる)
		std::vector<CollisionEvent>  _events;
		std::vector<CollisionEvent>  _pendingEvents;   // 体を消した時の「離れた」(次の Step で返す)
		PhysicsStats                 _stats = {};

		// ---------- 剛体 (P1) ----------
		// 1 本の向き (法線 / 摩擦の 2 本) の拘束。回転の項は始めに 1 回だけ求めておく
		//   (慣性の逆数と点の位置は Simulate の間変えないので、毎回の 3x3 の掛け算と外積が要らない)
		struct ContactAxis
		{
			Float3 Dir;                  // 向き (A → B)
			Float3 RA, RB;               // r × Dir
			Float3 IA, IB;               // 慣性の逆数 × (r × Dir)
			float  Mass    = 0.0f;       // 有効な質量
			float  Impulse = 0.0f;       // 積み重ねた力積
		};
		struct ContactPointConstraint
		{
			Float3 RA, RB;               // 重心から点 (Simulate の始め。ワールドの向き)
			ContactAxis N, T1, T2;
			float  AdjustedSeparation = 0.0f;   // 今の離れ具合 = dot(d, n) + これ
			float  MaxNormalImpulse = 0.0f;
			float  RelativeVelocity = 0.0f;     // 始めの法線の向きの速さ (反発に使う)
		};
		struct ContactConstraint
		{
			SolverBody* A = nullptr;
			SolverBody* B = nullptr;
			uint32_t    Pair = 0;
			int         Count = 0;
			float       Friction = 0.0f, Restitution = 0.0f;
			Softness    Soft;
			ContactPointConstraint Points[Manifold::MAX_POINTS];
		};
		SolverSettings                     _solver {};
		std::vector<uint32_t>              _rigidBodies;      // Dynamic / Kinematic の体 (番号)
		std::vector<RigidData>             _rigidData;        // _rigidBodies と同じ並び
		std::vector<SolverBody>            _solverBodies;
		std::vector<ContactConstraint>     _contacts;
		// 色分け : 同じ色の接触は、動く体を共有しない (並列に解いても同じ体に書かない。Box2D v3 の graph coloring)
		static constexpr int               MAX_COLORS = 24;
		std::vector<ContactConstraint>     _coloredContacts;  // 作業用
		std::vector<uint64_t>              _colorBits;        // 色ごとの「使った体」(ビット)
		std::vector<uint8_t>               _contactColor;
		uint32_t                           _colorStart[MAX_COLORS + 2] = {};   // 色 k の接触 = [start[k], start[k + 1])。最後 (MAX_COLORS) はあふれた分
		std::vector<BodyTransform>         _poseBuffer;       // 動かした体を SetTransforms と同じ道で木へ
		SolverBody                         _staticBody;       // 動かない物 (質量が無限)
		std::vector<std::unique_ptr<Joint>> _joints;          // 空きは nullptr
		std::vector<uint16_t>              _jointVersions;
		std::vector<uint32_t>              _freeJoints;
		std::unordered_map<uint64_t, int>  _noCollidePairs;   // ジョイントで繋いで、押し戻さない組 (数)
		uint32_t                           _activeJoints = 0;

		bool  isPhysicalPair(const Body& a, const Body& b) const
		{
			return !a.IsTrigger && !b.IsTrigger && (a.Motion == MotionType::Dynamic || b.Motion == MotionType::Dynamic);
		}
		void  updateMass(Body& body);
		void  collidePhysical(Pair& pair, PairImpulse& impulse, const Body& a, const Body& b) const;   // 剛体の組の形の判定 (少し先の点も)
		RigidData&       rigidOf(const Body& body)       { return _rigidData[body.RigidSlot]; }
		const RigidData& rigidOf(const Body& body) const { return _rigidData[body.RigidSlot]; }
		void  setMotion(uint32_t index, MotionType motion);
		void  solveRigid(float dt);
		void  prepareContacts(const SolverStep& step);
		void  colorContacts();
		template<typename Fn> void forEachColor(Fn&& fn);   // 色ごとに並列 (最後のあふれた分は順番に)
		void  warmStartContacts(uint32_t begin, uint32_t end);
		void  solveContacts(const SolverStep& step, bool useBias, uint32_t begin, uint32_t end);
		void  applyRestitution();
		void  storeImpulses();
		bool  prepareJoint(Joint& joint);
		void  releaseJointPair(const Joint& joint);
		Joint* findJoint(uint32_t handle) const;
		SolverBody* solverOf(const Body& body) { return body.Solver >= 0 ? &_solverBodies[body.Solver] : &_staticBody; }
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
