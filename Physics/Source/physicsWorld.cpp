/***********************************************************************************
* @file    physicsWorld.cpp
* @brief   衝突判定のワールド。説明は physicsWorld.h
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
#include "physicsWorld.h"
#include "raycast.h"
#include <algorithm>
#include <chrono>

namespace Physics
{
	namespace
	{
		using Clock = std::chrono::steady_clock;
		float elapsedMs(Clock::time_point from, Clock::time_point to)
		{
			return std::chrono::duration<float, std::milli>(to - from).count();
		}

		constexpr uint32_t MAX_BODIES = 0xFFFFu;   // ハンドルの番号は 16bit
	}

	// ==============================================================
	// 体
	// ==============================================================

	PhysicsWorld::PhysicsWorld()
	{
		for (uint32_t& row : _layerMatrix) row = 0xFFFFFFFFu;   // 既定は全部のレイヤーどうしを調べる
		// 並列のスレッド : 論理コアの数 - 1 (呼んだスレッドも働く)。描画や C# の分を残すため 3 本まで
		const uint32_t cores = std::thread::hardware_concurrency();
		_jobs.SetWorkers(cores > 1 ? (std::min)(cores - 1, 3u) : 0u);

		// 剛体の解き方の既定 (Box2D v3 に近い値)
		_solver.Gravity[0] = 0.0f; _solver.Gravity[1] = -9.81f; _solver.Gravity[2] = 0.0f;
		_solver.SubSteps               = 4;
		_solver.ContactHertz           = 30.0f;
		_solver.ContactDampingRatio    = 10.0f;
		_solver.ContactPushMaxVelocity = 3.0f;
		_solver.RestitutionThreshold   = 1.0f;
		_solver.SpeculativeDistance    = 0.02f;
		_solver.JointHertz             = 60.0f;
		_solver.JointDampingRatio      = 2.0f;
		_solver.MaxLinearSpeed         = 400.0f;
		_solver.Iterations             = 1;
		_solver.RelaxIterations        = 1;
	}

	PhysicsWorld::Body* PhysicsWorld::find(uint32_t handle)
	{
		const uint32_t index = handle & 0xFFFFu;
		if (handle == INVALID_HANDLE || index >= _bodies.size()) return nullptr;
		Body& body = _bodies[index];
		return body.Alive && body.Version == (handle >> 16) ? &body : nullptr;
	}

	uint32_t PhysicsWorld::CreateBody(const BodyDesc& desc)
	{
		uint32_t index;
		if (!_freeBodies.empty())
		{
			index = _freeBodies.back();
			_freeBodies.pop_back();
		}
		else
		{
			if (_bodies.size() >= MAX_BODIES) return INVALID_HANDLE;
			index = static_cast<uint32_t>(_bodies.size());
			_bodies.emplace_back();
		}

		Body& body = _bodies[index];
		const uint16_t version = static_cast<uint16_t>(body.Version + 1);   // 番号を使い回すたびに世代を上げる
		body = Body();
		body.Alive       = true;
		body.Version     = version;
		body.IsStatic    = desc.IsStatic != 0;
		body.Layer       = desc.Layer & 31u;
		body.RequestedMask = desc.CollideMask;
		body.Mask        = desc.CollideMask & _layerMatrix[body.Layer];
		body.Shape       = static_cast<ShapeType>(desc.Shape <= 3 ? desc.Shape : 0);
		body.LocalCenter = Float3(desc.Center[0], desc.Center[1], desc.Center[2]);
		if (body.Shape == ShapeType::Mesh)
		{
			// メッシュは動かない物だけ (動く物の三角形を毎フレーム作り直すのは重いため)。無効なメッシュなら球にする
			const uint32_t meshIndex = desc.Mesh - 1;
			if (desc.Mesh != 0 && meshIndex < _meshes.size() && _meshes[meshIndex])
			{
				body.IsStatic = true;
				body.MeshData = _meshes[meshIndex];
				body.MeshInst = std::make_unique<MeshInstance>();
				body.MeshInst->Source = body.MeshData;
			}
			else
			{
				body.Shape = ShapeType::Sphere;
			}
		}
		body.Size        = Float3(desc.Size[0], desc.Size[1], desc.Size[2]);
		body.Friction    = desc.Friction;
		body.Restitution = desc.Restitution;
		body.IsTrigger   = (desc.Flags & BODY_TRIGGER) != 0;
		updateShape(body, desc.Position, desc.Rotation, desc.Scale);
		insertProxy(index);
		noteEnlarged(index);           // 次の Step で、この体の組を探す

		body.MovedStep = _stepIndex;   // 次の Step で重なりを調べる
		_moved.push_back(index);
		_stats.Bodies++;
		if (!body.IsStatic) _dynamicCount++;
		return handleOf(index);
	}

	void PhysicsWorld::DestroyBody(uint32_t handle)
	{
		Body* body = find(handle);
		if (!body) return;
		const uint32_t index = handle & 0xFFFFu;

		// この体の組を表から消す。重なっていた相手には「離れた」を送る (次の Step のイベントに入る)
		for (size_t i = 0; i < _pairs.size();)
		{
			Pair& pair = _pairs[i];
			if (pair.A != index && pair.B != index) { ++i; continue; }
			if (pair.Touching)
			{
				_pendingEvents.push_back({ handleOf(pair.A), handleOf(pair.B), 1u, 0u });
				_touchingCount--;
			}
			_pairMap.Erase(pair.Key);
			if (i + 1 != _pairs.size())
			{
				pair = _pairs.back();   // 最後の組を空いた所へ
				_pairImpulses[i] = _pairImpulses.back();
				*_pairMap.Find(pair.Key) = static_cast<uint32_t>(i);
			}
			_pairs.pop_back();
			_pairImpulses.pop_back();
		}
		removeProxy(index);
		if (!body->IsStatic) _dynamicCount--;
		setMotion(index, MotionType::None);
		// この体に繋いだジョイントは壊れた扱い (C# が後で消す)
		for (const std::unique_ptr<Joint>& joint : _joints)
		{
			if (!joint || joint->Broken || (joint->A.Handle != handle && joint->B.Handle != handle)) continue;
			joint->Broken = true;
			releaseJointPair(*joint);
		}
		body->MeshInst.reset();
		body->MeshData.reset();
		body->Alive = false;
		_freeBodies.push_back(index);
		_stats.Bodies--;
	}

	void PhysicsWorld::updateShape(Body& body, const float position[3], const float rotation[4], const float scale[3])
	{
		// 大きさを形に掛ける (球は一番大きい軸 / カプセルの太さは X と Z の大きい方 / 高さは Y)
		const Float3 s(std::fabs(scale[0]), std::fabs(scale[1]), std::fabs(scale[2]));
		body.Position = Float3(position[0], position[1], position[2]);
		body.Rotation = normalize(Quat(rotation[0], rotation[1], rotation[2], rotation[3]));
		body.Scale    = Float3(scale[0], scale[1], scale[2]);
		WorldShape& w = body.World;
		w.Type     = body.Shape;
		w.Rotation = Rotation3::FromQuaternion(rotation[0], rotation[1], rotation[2], rotation[3]);
		w.Center   = Float3(position[0], position[1], position[2]) + w.Rotation.Apply(mul(body.LocalCenter, Float3(scale[0], scale[1], scale[2])));
		if (body.Shape == ShapeType::Mesh)
		{
			// メッシュ : ワールドの三角形と BVH を作り直す (動かない物なので、作った時と動かされた時だけ)
			body.MeshInst->Build(Float3(position[0], position[1], position[2]), w.Rotation, Float3(scale[0], scale[1], scale[2]));
			w.Mesh   = body.MeshInst.get();
			body.Box = body.MeshInst->Box;
			return;
		}
		switch (body.Shape)
		{
		case ShapeType::Sphere:
			w.Radius = body.Size.x * (std::max)((std::max)(s.x, s.y), s.z);
			break;
		case ShapeType::Box:
			w.HalfExtents = mul(body.Size, s);
			break;
		case ShapeType::Capsule:
			w.Radius     = body.Size.x * (std::max)(s.x, s.z);
			w.HalfHeight = body.Size.y * s.y;
			break;
		}
		body.Box = w.ComputeAabb();
		if (body.Motion == MotionType::Dynamic) updateMass(body);   // 大きさが変わると慣性も変わる
	}

	float PhysicsWorld::marginOf(const Body& body) const
	{
		// 太らせる量 : 箱の一番長い辺の 10% (少し動いただけなら木を直さない)
		const Float3 d = body.Box.max - body.Box.min;
		return (std::max)(0.02f, 0.1f * (std::max)((std::max)(d.x, d.y), d.z));
	}

	void PhysicsWorld::insertProxy(uint32_t index)
	{
		Body& body = _bodies[index];
		DynamicTree& tree = body.IsStatic ? _staticTree : _dynamicTree;
		body.Proxy = tree.CreateProxy(body.Box, marginOf(body), index, 1u << body.Layer, body.Mask);
	}

	void PhysicsWorld::removeProxy(uint32_t index)
	{
		Body& body = _bodies[index];
		if (body.Proxy == DynamicTree::NULL_NODE) return;
		DynamicTree& tree = body.IsStatic ? _staticTree : _dynamicTree;
		tree.DestroyProxy(body.Proxy);
		body.Proxy = DynamicTree::NULL_NODE;
	}

	void PhysicsWorld::SetTransforms(const BodyTransform* items, uint32_t count)
	{
		const Clock::time_point start = Clock::now();
		struct SyncTimer
		{
			Clock::time_point Start; float& Total;
			~SyncTimer() { Total += elapsedMs(Start, Clock::now()); }
		} timer{ start, _syncMs };
		// 1. 形と箱を作り直す (体ごとに別の所へ書くので並列。同じ体を 1 回の呼び出しで 2 回渡さないこと)
		if (_previousBoxes.size() < count) _previousBoxes.resize(count);
		_jobs.ParallelFor(count, 512, [&](uint32_t begin, uint32_t end)
		{
			for (uint32_t i = begin; i < end; ++i)
			{
				const BodyTransform& t = items[i];
				Body* body = find(t.Handle);
				if (!body) continue;
				_previousBoxes[i] = body->Box;
				updateShape(*body, t.Position, t.Rotation, t.Scale);
			}
		});

		// 2. 木の葉 (1 本の木を直すので順番に)
		for (uint32_t i = 0; i < count; ++i)
		{
			const BodyTransform& t = items[i];
			Body* body = find(t.Handle);
			if (!body) continue;
			const Aabb& previous = _previousBoxes[i];
			const uint32_t index = t.Handle & 0xFFFFu;

			// 木の葉 : 太らせた箱からはみ出した時だけ直す
			// 動く物は箱を広げるだけにして、木の形は Step の始めにまとめて直す (fixDynamicTree)
			// 以前は 1 つずつ抜いて入れ直していた (全部動くと、これだけで 1 万個で 1.3 ms)
			// 動く物は、動いた向きにも広げる (前の箱の中心からのずれ × _predictScale。瞬間移動で大きくならないよう、箱の大きさまで)
			bool changed;
			if (body->IsStatic)
			{
				changed = _staticTree.MoveProxy(body->Proxy, body->Box, marginOf(*body));
			}
			else
			{
				const Float3 size  = body->Box.max - body->Box.min;
				const float  limit = (std::max)((std::max)(size.x, size.y), size.z);
				const Float3 shift = (body->Box.min + body->Box.max - previous.min - previous.max) * (0.5f * _predictScale);
				auto clampAxis = [limit](float v) { return (std::max)(-limit, (std::min)(limit, v)); };
				changed = _dynamicTree.EnlargeProxy(body->Proxy, body->Box, marginOf(*body), Float3(clampAxis(shift.x), clampAxis(shift.y), clampAxis(shift.z)));
			}
			if (changed) noteEnlarged(index);
			if (body->MovedStep != _stepIndex)
			{
				body->MovedStep = _stepIndex;
				_moved.push_back(index);
			}
		}
	}

	void PhysicsWorld::SetBroadphase(Broadphase mode)
	{
		_mode = mode;
		markAllMoved();   // 方法を変えたら、次の Step は全部を調べ直す (BVH は動いた物しか調べないので)
	}

	void PhysicsWorld::SetLayerCollision(uint32_t a, uint32_t b, bool collide)
	{
		if (a >= 32 || b >= 32) return;
		if (collide) { _layerMatrix[a] |= 1u << b; _layerMatrix[b] |= 1u << a; }
		else         { _layerMatrix[a] &= ~(1u << b); _layerMatrix[b] &= ~(1u << a); }
		// 体の「当たる相手」と木の節のビットを作り直し、次の Step で全部を調べ直す (外れた組は Exit になる)
		for (Body& body : _bodies)
		{
			if (!body.Alive) continue;
			body.Mask = body.RequestedMask & _layerMatrix[body.Layer];
			(body.IsStatic ? _staticTree : _dynamicTree).SetProxyFilter(body.Proxy, 1u << body.Layer, body.Mask);
		}
		markAllMoved();
	}

	void PhysicsWorld::SetOption(uint32_t option, uint32_t value)
	{
		if (option == 1) _layerPruning = value != 0;
		if (option == 2) _jobs.SetWorkers((std::min)(value, 63u));   // 並列に使う追加のスレッドの数 (0 = 1 本だけ)
		if (option == 3) _treeRebuild  = value != 0;
		if (option == 4) _fullPassDivisor = value;   // 0 : いつも全部を探し直す
		if (option == 6) _rebuildDivisor  = (std::max)(1u, value);
		if (option == 7) _mortonBuild     = value != 0;   // 0 : 節ごとに軸の真ん中で分ける
		if (option == 5) _predictScale    = static_cast<float>(value);   // 動いた向きに広げる倍率 (0 で切る)
	}

	void PhysicsWorld::noteEnlarged(uint32_t index)
	{
		Body& body = _bodies[index];
		if (body.EnlargedStep == _stepIndex) return;
		body.EnlargedStep = _stepIndex;
		_enlarged.push_back(index);
	}

	void PhysicsWorld::fixDynamicTree()
	{
		if (_enlarged.empty()) return;
		// 動く物の 1/16 以上がはみ出したら、中の節を全部作り直す (1 つずつ入れ直すと、入れる場所を探して回転もするので重い)
		// 1 万個 : 作り直し 約 0.6 ms (並列) / 入れ直し 1 つ 約 0.4 µs。境目は計測で決めた (ClaudLog の C3)
		const bool rebuild = _treeRebuild && _enlarged.size() > 64 && _enlarged.size() * _rebuildDivisor >= _dynamicCount;
		if (rebuild)
		{
			_dynamicTree.Rebuild(&_jobs, _mortonBuild);
			return;
		}
		for (const uint32_t index : _enlarged)
		{
			const Body& body = _bodies[index];
			// 消された体 / 動かない物 (動かない物の木は MoveProxy で直してある) は飛ばす
			if (!body.Alive || body.IsStatic || body.EnlargedStep != _stepIndex) continue;
			_dynamicTree.ReinsertProxy(body.Proxy);
		}
	}

	void PhysicsWorld::markAllMoved()
	{
		_forceFull = true;   // 組も全部探し直す
		for (uint32_t i = 0; i < _bodies.size(); ++i)
		{
			if (_bodies[i].Alive && _bodies[i].MovedStep != _stepIndex)
			{
				_bodies[i].MovedStep = _stepIndex;
				_moved.push_back(i);
			}
		}
	}

	bool PhysicsWorld::canCollide(const Body& a, const Body& b) const
	{
		if (a.IsStatic && b.IsStatic) return false;   // 動かない物どうしは調べない
		return (a.Mask & (1u << b.Layer)) != 0 && (b.Mask & (1u << a.Layer)) != 0;
	}

	void PhysicsWorld::testPair(uint32_t a, uint32_t b)
	{
		const Body& A = _bodies[a];
		const Body& B = _bodies[b];
		if (!canCollide(A, B) || !A.Box.Overlaps(B.Box)) return;
		addPair(a, b);
	}

	void PhysicsWorld::addPair(uint32_t a, uint32_t b)
	{
		const uint64_t key = pairKey(a, b);
		bool inserted;
		uint32_t* slot = _pairMap.FindOrInsert(key, inserted);
		if (inserted)
		{
			*slot = static_cast<uint32_t>(_pairs.size());
			Pair pair;
			pair.Key = key;
			pair.A   = static_cast<uint32_t>(key >> 32);
			pair.B   = static_cast<uint32_t>(key & 0xFFFFFFFFu);
			_pairs.push_back(pair);
			_pairImpulses.emplace_back();
		}
		_pairs[*slot].SeenStep = _stepIndex;
	}

	// ==============================================================
	// ブロードフェーズ
	// ==============================================================

	bool PhysicsWorld::findPairsBvh()
	{
		// ==============================================================
		// 新しい組ができうるのは、太らせた箱が変わった体 (_enlarged) の所だけ (組の表は Step をまたいで持つ)
		//   以前 (C1 / C2) は「動いた体」を全部問い合わせていた。太らせた箱の中で動いている間は、探さなくてよい
		// 多く変わった時 : 木の節の組を 1 回たどって、太らせた箱が重なる組を全部探し直す
		//   (動く物の木の中 + 動く物の木 × 動かない物の木)。見つからなかった組は表から消える
		// ==============================================================
		const bool full = _forceFull || _fullPassDivisor == 0 ||
		                  (_dynamicCount > 64 && _enlarged.size() * _fullPassDivisor >= _dynamicCount);
		auto add = [&](uint32_t a, uint32_t b) { if (canCollide(_bodies[a], _bodies[b])) addPair(a, b); };
		if (full)
		{
			_dynamicTree.QueryAllPairs(add, _layerPruning);
			_dynamicTree.QueryPairs(_staticTree, add, _layerPruning);
			return true;
		}

		// 少し変わった時 : 変わった体の太らせた箱で問い合わせる (両方変わった組は 2 回見つかるが、表で 1 つになる)
		for (const uint32_t index : _enlarged)
		{
			const Body& body = _bodies[index];
			if (!body.Alive || body.EnlargedStep != _stepIndex) continue;
			// レイヤーで当たりえない枝は飛ばす (比べる用に切った時は全部たどる)
			const uint32_t layers = _layerPruning ? 1u << body.Layer : ~0u;
			const uint32_t mask   = _layerPruning ? body.Mask : ~0u;
			const Aabb fat = fatBoxOf(body);
			auto visit = [&](uint32_t other) { if (other != index) add(index, other); };
			_dynamicTree.Query(fat, visit, layers, mask);
			if (!body.IsStatic) _staticTree.Query(fat, visit, layers, mask);
		}
		return false;
	}

	void PhysicsWorld::findPairsBrute()
	{
		// 全部の組 (比べる用)
		const uint32_t count = static_cast<uint32_t>(_bodies.size());
		for (uint32_t a = 0; a < count; ++a)
		{
			if (!_bodies[a].Alive) continue;
			for (uint32_t b = a + 1; b < count; ++b)
			{
				if (_bodies[b].Alive) testPair(a, b);
			}
		}
	}

	void PhysicsWorld::findPairsGrid()
	{
		// ==============================================================
		// 均一グリッド (比べる用) : 動く物を、箱が掛かるマスに全部入れて、マスで並べ替え、同じマスの物どうしを調べる
		//   同じ組が複数のマスに入るので、「2 つの箱の重なりの最小の角」が入るマスでだけ数える
		//   マスの大きさ = 動く物の箱の平均の大きさ × 2。動かない物は木に問い合わせる (BVH と同じ)
		// ==============================================================
		Float3 sum;
		uint32_t dynamicCount = 0;
		for (const Body& b : _bodies)
		{
			if (!b.Alive || b.IsStatic) continue;
			sum = sum + (b.Box.max - b.Box.min);
			dynamicCount++;
		}
		if (dynamicCount == 0) return;
		const float cell = (std::max)(0.01f, 2.0f * (sum.x + sum.y + sum.z) / (3.0f * dynamicCount));
		const float inv  = 1.0f / cell;

		auto cellCoord = [&](float v) { return static_cast<int64_t>(std::floor(v * inv)); };
		auto cellKey = [](int64_t x, int64_t y, int64_t z)
		{
			// 1 軸 21bit (±100 万マス) に詰める
			return (uint64_t(x + (1 << 20)) & 0x1FFFFF) | ((uint64_t(y + (1 << 20)) & 0x1FFFFF) << 21) | ((uint64_t(z + (1 << 20)) & 0x1FFFFF) << 42);
		};

		_grid.clear();
		for (uint32_t i = 0; i < _bodies.size(); ++i)
		{
			const Body& b = _bodies[i];
			if (!b.Alive || b.IsStatic) continue;
			const int64_t x0 = cellCoord(b.Box.min.x), x1 = cellCoord(b.Box.max.x);
			const int64_t y0 = cellCoord(b.Box.min.y), y1 = cellCoord(b.Box.max.y);
			const int64_t z0 = cellCoord(b.Box.min.z), z1 = cellCoord(b.Box.max.z);
			for (int64_t z = z0; z <= z1; ++z)
				for (int64_t y = y0; y <= y1; ++y)
					for (int64_t x = x0; x <= x1; ++x)
						_grid.push_back({ cellKey(x, y, z), i });
		}
		std::sort(_grid.begin(), _grid.end(), [](const GridEntry& a, const GridEntry& b) { return a.Cell < b.Cell; });

		for (size_t start = 0; start < _grid.size();)
		{
			size_t end = start + 1;
			while (end < _grid.size() && _grid[end].Cell == _grid[start].Cell) ++end;
			for (size_t i = start; i < end; ++i)
			{
				for (size_t j = i + 1; j < end; ++j)
				{
					const uint32_t a = _grid[i].Body, b = _grid[j].Body;
					const Body& A = _bodies[a];
					const Body& B = _bodies[b];
					if (!A.Box.Overlaps(B.Box)) continue;
					const Float3 corner = maxOf(A.Box.min, B.Box.min);   // 重なりの最小の角が入るマスでだけ数える
					if (cellKey(cellCoord(corner.x), cellCoord(corner.y), cellCoord(corner.z)) != _grid[start].Cell) continue;
					testPair(a, b);
				}
			}
			start = end;
		}

		for (uint32_t i = 0; i < _bodies.size(); ++i)
		{
			const Body& b = _bodies[i];
			if (!b.Alive || b.IsStatic) continue;
			_staticTree.Query(b.Box, [&](uint32_t other) { testPair(i, other); });
		}
	}

	// ==============================================================
	// Step
	// ==============================================================

	uint32_t PhysicsWorld::Step()
	{
		const Clock::time_point start = Clock::now();
		_events.swap(_pendingEvents);   // 体を消した時の「離れた」から始める
		_pendingEvents.clear();
		_stats.MovedBodies = static_cast<uint32_t>(_moved.size());

		// 0. 動く物の木を直す (はみ出した葉)
		fixDynamicTree();
		_stats.TreeMs = elapsedMs(start, Clock::now());

		// 1. ブロードフェーズ : 組の表に新しい組を入れる (Brute / Grid は比べる用で、毎回全部を探し直す)
		bool full = true;
		switch (_mode)
		{
		case Broadphase::Bvh:   full = findPairsBvh(); break;
		case Broadphase::Brute: findPairsBrute();      break;
		case Broadphase::Grid:  findPairsGrid();       break;
		}
		const Clock::time_point broad = Clock::now();

		// 2. ナローフェーズとイベント
		float narrowMs = 0.0f, eventsMs = 0.0f;
		updatePairs(full, narrowMs, eventsMs);

		_moved.clear();
		_enlarged.clear();
		_forceFull = false;
		_stepIndex++;
		if (_stepIndex == 0) _stepIndex = 1;   // 0 は「動いていない」の印にしないので飛ばす

		_stats.Overlaps      = _touchingCount;
		_stats.BroadphaseMs  = elapsedMs(start, broad);
		_stats.NarrowphaseMs = narrowMs;
		_stats.StepMs        = elapsedMs(start, Clock::now());
		_stats.Broadphase    = static_cast<uint32_t>(_mode);
		_stats.EventsMs      = eventsMs;
		_stats.Workers       = _jobs.Workers();
		_stats.SyncMs        = _syncMs;
		_syncMs = 0.0f;
		return static_cast<uint32_t>(_events.size());
	}

	void PhysicsWorld::collidePhysical(Pair& pair, PairImpulse& impulse, const Body& a, const Body& b) const
	{
		// ==============================================================
		// 剛体の組 (P1) : 形を SpeculativeDistance だけ太らせて求め、深さから引く
		//   少し離れた点 (深さが負) も入るので、ぶつかる直前から止め始められる (すり抜け / 着地の震えが減る)
		//   重なっているか (イベント) は本当の深さ (0 以上の点があるか) で決める
		//   前の点に近い点は、前の力積を引き継ぐ (積み重ねた物が少ない反復でも止まる)
		// ==============================================================
		const int oldCount = pair.Physical ? pair.Contact.Count : 0;
		Float3 oldPosition[Manifold::MAX_POINTS], oldFriction[Manifold::MAX_POINTS];
		float  oldNormal[Manifold::MAX_POINTS];
		for (int i = 0; i < oldCount; ++i)
		{
			oldPosition[i] = pair.Contact.Points[i].Position;
			oldNormal[i]   = impulse.Normal[i];
			oldFriction[i] = impulse.Friction[i];
		}

		const float margin = (std::max)(0.0f, _solver.SpeculativeDistance);
		WorldShape sa = a.World, sb = b.World;
		WorldShape& grow = sb.Type != ShapeType::Mesh ? sb : sa;   // メッシュは太らせられないので相手を
		if (grow.Type == ShapeType::Box) grow.HalfExtents = grow.HalfExtents + Float3(margin, margin, margin);
		else                             grow.Radius += margin;
		const Float3 m(margin, margin, margin);
		const Aabb boxA { a.Box.min - m, a.Box.max + m };

		pair.Contact.Count = 0;
		const bool collided = boxA.Overlaps(b.Box) && Collide(sa, sb, pair.Contact) && pair.Contact.Count > 0;
		float deepest = -FLT_MAX;
		if (collided)
		{
			for (int i = 0; i < pair.Contact.Count; ++i)
			{
				pair.Contact.Points[i].Depth -= margin;
				deepest = (std::max)(deepest, pair.Contact.Points[i].Depth);
			}
		}
		else
		{
			pair.Contact.Count = 0;
		}
		pair.Physical = collided ? 1 : 0;
		pair.Hit      = collided && deepest >= 0.0f ? 1 : 0;

		const float tolerance = 0.05f * 0.05f;   // 5 cm 以内なら同じ点
		for (int i = 0; i < pair.Contact.Count; ++i)
		{
			impulse.Normal[i]   = 0.0f;
			impulse.Friction[i] = Float3();
			float best = tolerance;
			for (int j = 0; j < oldCount; ++j)
			{
				const float d = lengthSq(pair.Contact.Points[i].Position - oldPosition[j]);
				if (d < best)
				{
					best = d;
					impulse.Normal[i]   = oldNormal[j];
					impulse.Friction[i] = oldFriction[j];
				}
			}
		}
	}

	void PhysicsWorld::updatePairs(bool fullPass, float& narrowMs, float& eventsMs)
	{
		const Clock::time_point start = Clock::now();

		// ==============================================================
		// 1 回目 : 組ごとに「消す / 形を調べる / そのまま」を決める (組ごとに別の所へ書くので、並列にもできる)
		//   消す : 全部を探し直した時は、見つからなかった組。そうでなければ、太らせた箱が変わって離れた組
		//   形を調べる : 表に入ったばかりか、どちらかが動いた組 (以前 (C2) は箱が重なった組を毎回調べていた)
		// ==============================================================
		_jobs.ParallelFor(static_cast<uint32_t>(_pairs.size()), 256, [&](uint32_t begin, uint32_t end)
		{
			for (uint32_t i = begin; i < end; ++i)
			{
				Pair& pair = _pairs[i];
				const Body& a = _bodies[pair.A];
				const Body& b = _bodies[pair.B];
				const bool remove = fullPass ? pair.SeenStep != _stepIndex
				                             : (a.EnlargedStep == _stepIndex || b.EnlargedStep == _stepIndex) && !fatBoxOf(a).Overlaps(fatBoxOf(b));
				if (remove) { pair.Action = 1; continue; }
				if (pair.IsNew || a.MovedStep == _stepIndex || b.MovedStep == _stepIndex)
				{
					if (isPhysicalPair(a, b)) collidePhysical(pair, _pairImpulses[i], a, b);
					else
					{
						pair.Physical = 0;
						pair.Contact.Count = 0;
						pair.Hit = a.Box.Overlaps(b.Box) && Collide(a.World, b.World, pair.Contact) ? 1 : 0;
					}
					pair.Action = 2;
				}
				else
				{
					pair.Action = 0;
				}
			}
		});
		const Clock::time_point narrow = Clock::now();

		// ==============================================================
		// 2 回目 : イベントと表の片付け (離れた → 重なり始めた の順に返す。以前と同じ)
		// ==============================================================
		_enterEvents.clear();
		uint32_t tested = 0;
		for (size_t i = 0; i < _pairs.size();)
		{
			Pair& pair = _pairs[i];
			if (pair.Action == 1)
			{
				if (pair.Touching)
				{
					_events.push_back({ handleOf(pair.A), handleOf(pair.B), 1u, 0u });
					_touchingCount--;
				}
				_pairMap.Erase(pair.Key);
				if (i + 1 != _pairs.size())
				{
					pair = _pairs.back();   // 最後の組を空いた所へ (その組の Action も済んでいるので、この場所でもう一度見る)
					_pairImpulses[i] = _pairImpulses.back();
					*_pairMap.Find(pair.Key) = static_cast<uint32_t>(i);
				}
				_pairs.pop_back();
				_pairImpulses.pop_back();
				continue;
			}
			if (pair.Action == 2) tested++;
			if (pair.Action == 2 && pair.Hit != pair.Touching)
			{
				if (pair.Hit) { _enterEvents.push_back({ handleOf(pair.A), handleOf(pair.B), 0u, 0u }); _touchingCount++; }
				else          { _events.push_back({ handleOf(pair.A), handleOf(pair.B), 1u, 0u });      _touchingCount--; }
				pair.Touching = pair.Hit;
			}
			pair.IsNew = 0;
			++i;
		}
		_events.insert(_events.end(), _enterEvents.begin(), _enterEvents.end());

		_stats.Candidates = tested;
		narrowMs = elapsedMs(start, narrow);
		eventsMs = elapsedMs(narrow, Clock::now());
	}

	uint32_t PhysicsWorld::CreateMesh(const float* positions, uint32_t vertexCount, const uint32_t* indices, uint32_t indexCount)
	{
		if (!positions || !indices || vertexCount == 0 || indexCount < 3) return INVALID_HANDLE;
		auto mesh = std::make_shared<TriangleMesh>();
		mesh->Positions.resize(vertexCount);
		for (uint32_t i = 0; i < vertexCount; ++i) mesh->Positions[i] = Float3(positions[i * 3], positions[i * 3 + 1], positions[i * 3 + 2]);
		mesh->Indices.reserve(indexCount - indexCount % 3);
		for (uint32_t i = 0; i + 2 < indexCount; i += 3)
		{
			// 範囲外の番号を持つ三角形は捨てる
			if (indices[i] >= vertexCount || indices[i + 1] >= vertexCount || indices[i + 2] >= vertexCount) continue;
			mesh->Indices.push_back(indices[i]);
			mesh->Indices.push_back(indices[i + 1]);
			mesh->Indices.push_back(indices[i + 2]);
		}
		if (mesh->Indices.empty()) return INVALID_HANDLE;
		for (uint32_t i = 0; i < _meshes.size(); ++i)
		{
			if (!_meshes[i]) { _meshes[i] = mesh; return i + 1; }
		}
		_meshes.push_back(mesh);
		return static_cast<uint32_t>(_meshes.size());
	}

	void PhysicsWorld::DestroyMesh(uint32_t mesh)
	{
		// 使っている体はそのまま (形は共有しているので、体が消えるまで残る)
		if (mesh != 0 && mesh - 1 < _meshes.size()) _meshes[mesh - 1].reset();
	}

	// ==============================================================
	// 問い合わせ
	// ==============================================================

	bool PhysicsWorld::Raycast(const Ray& ray, RaycastHit& hit) const
	{
		hit = RaycastHit();
		hit.Body = INVALID_HANDLE;
		Float3 dir(ray.Direction[0], ray.Direction[1], ray.Direction[2]);
		const float len = std::sqrt(lengthSq(dir));
		if (len < 1e-12f || ray.MaxDistance <= 0.0f) return false;
		dir = dir * (1.0f / len);
		const Float3 origin(ray.Origin[0], ray.Origin[1], ray.Origin[2]);
		const Float3 invDir(1.0f / (std::fabs(dir.x) > 1e-12f ? dir.x : 1e-12f), 1.0f / (std::fabs(dir.y) > 1e-12f ? dir.y : 1e-12f),
		                    1.0f / (std::fabs(dir.z) > 1e-12f ? dir.z : 1e-12f));

		// 2 本の木をたどる。近い当たりが見つかったら、それより遠い枝はたどらない
		float best = ray.MaxDistance;
		uint32_t bestBody = INVALID_HANDLE;
		Float3 bestNormal;
		auto visit = [&](uint32_t index, float& limit)
		{
			const Body& body = _bodies[index];
			if (!body.Alive || (ray.LayerMask & (1u << body.Layer)) == 0) return;
			float t;
			Float3 n;
			if (RaycastShape(body.World, origin, dir, limit, t, n) && t < best)
			{
				best = t; limit = t; bestBody = index; bestNormal = n;
			}
		};
		_staticTree.RayCast(origin, invDir, best, visit, ray.LayerMask);
		_dynamicTree.RayCast(origin, invDir, best, visit, ray.LayerMask);
		if (bestBody == INVALID_HANDLE) return false;

		const Float3 point = origin + dir * best;
		hit.Body = handleOf(bestBody);
		hit.Hit  = 1;
		hit.Distance = best;
		hit.Point[0] = point.x; hit.Point[1] = point.y; hit.Point[2] = point.z;
		hit.Normal[0] = bestNormal.x; hit.Normal[1] = bestNormal.y; hit.Normal[2] = bestNormal.z;
		return true;
	}

	uint32_t PhysicsWorld::OverlapShape(const WorldShape& shape, uint32_t layerMask, uint32_t* out, uint32_t capacity) const
	{
		const Aabb box = shape.ComputeAabb();
		uint32_t count = 0;
		auto visit = [&](uint32_t index)
		{
			const Body& body = _bodies[index];
			if (!body.Alive || (layerMask & (1u << body.Layer)) == 0) return;
			if (!body.Box.Overlaps(box) || !Overlap(shape, body.World)) return;
			if (out && count < capacity) out[count] = handleOf(index);
			count++;
		};
		_staticTree.Query(box, visit, ~0u, layerMask);
		_dynamicTree.Query(box, visit, ~0u, layerMask);
		return count;   // 全部の数 (capacity より多いこともある)
	}

	uint32_t PhysicsWorld::GetContacts(ContactOut* out, uint32_t capacity) const
	{
		uint32_t count = 0;
		for (const Pair& pair : _pairs)
		{
			if (!pair.Touching) continue;
			const uint64_t key = pair.Key;
			const Manifold& manifold = pair.Contact;
			const uint32_t a = handleOf(static_cast<uint32_t>(key >> 32)), b = handleOf(static_cast<uint32_t>(key & 0xFFFFFFFFu));
			for (int i = 0; i < manifold.Count; ++i)
			{
				if (manifold.Points[i].Depth < 0.0f) continue;   // 剛体の少し先の点 (まだ触れていない)
				if (out && count < capacity)
				{
					const ContactPoint& p = manifold.Points[i];
					ContactOut& c = out[count];
					c = ContactOut();
					c.BodyA = a; c.BodyB = b; c.Depth = p.Depth;
					c.Position[0] = p.Position.x; c.Position[1] = p.Position.y; c.Position[2] = p.Position.z;
					c.Normal[0] = p.Normal.x; c.Normal[1] = p.Normal.y; c.Normal[2] = p.Normal.z;
				}
				count++;
			}
		}
		return count;
	}
}
