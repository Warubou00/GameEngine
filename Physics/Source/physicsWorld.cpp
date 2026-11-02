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
		body.Mask        = desc.CollideMask;
		body.Shape       = static_cast<ShapeType>(desc.Shape <= 2 ? desc.Shape : 0);
		body.LocalCenter = Float3(desc.Center[0], desc.Center[1], desc.Center[2]);
		body.Size        = Float3(desc.Size[0], desc.Size[1], desc.Size[2]);
		updateShape(body, desc.Position, desc.Rotation, desc.Scale);
		insertProxy(index);

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

		// 重なっていた相手には「離れた」を送る (次の Step のイベントに入る)
		for (auto it = _overlaps.begin(); it != _overlaps.end();)
		{
			const uint32_t a = static_cast<uint32_t>(*it >> 32), b = static_cast<uint32_t>(*it & 0xFFFFFFFFu);
			if (a == index || b == index)
			{
				_pendingEvents.push_back({ handleOf(a), handleOf(b), 1u, 0u });
				it = _overlaps.erase(it);
			}
			else
			{
				++it;
			}
		}
		removeProxy(index);
		if (!body->IsStatic) _dynamicCount--;
		body->Alive = false;
		_freeBodies.push_back(index);
		_stats.Bodies--;
	}

	void PhysicsWorld::updateShape(Body& body, const float position[3], const float rotation[4], const float scale[3])
	{
		// 大きさを形に掛ける (球は一番大きい軸 / カプセルの太さは X と Z の大きい方 / 高さは Y)
		const Float3 s(std::fabs(scale[0]), std::fabs(scale[1]), std::fabs(scale[2]));
		WorldShape& w = body.World;
		w.Type     = body.Shape;
		w.Rotation = Rotation3::FromQuaternion(rotation[0], rotation[1], rotation[2], rotation[3]);
		w.Center   = Float3(position[0], position[1], position[2]) + w.Rotation.Apply(mul(body.LocalCenter, Float3(scale[0], scale[1], scale[2])));
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
		body.Proxy = tree.CreateProxy(body.Box, marginOf(body), index);
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
		for (uint32_t i = 0; i < count; ++i)
		{
			const BodyTransform& t = items[i];
			Body* body = find(t.Handle);
			if (!body) continue;
			updateShape(*body, t.Position, t.Rotation, t.Scale);

			// 木の葉 : 太らせた箱からはみ出した時だけ直す
			DynamicTree& tree = body->IsStatic ? _staticTree : _dynamicTree;
			tree.MoveProxy(body->Proxy, body->Box, marginOf(*body));

			const uint32_t index = t.Handle & 0xFFFFu;
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
		// 方法を変えたら、次の Step は全部を調べ直す (BVH は動いた物しか調べないので)
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
		_candidates.push_back(pairKey(a, b));
	}

	// ==============================================================
	// ブロードフェーズ
	// ==============================================================

	void PhysicsWorld::findPairsBvh()
	{
		// ==============================================================
		// 多く動いた時 : 木の節の組を 1 回たどって、全部の組を探す (動く物の木の中 + 動く物の木 × 動かない物の木)
		//   1 個ずつ問い合わせると、木の上の方の節を動いた物の数だけたどり直す (1 万個で 1 回 約 100 節)
		//   以前 (最初の実装) は常に 1 個ずつ問い合わせていた : 1 万個が全部動くと 23 ms (ClaudLog の C1)
		// ==============================================================
		_fullPass = _dynamicCount > 64 && _moved.size() * 4 >= _dynamicCount;
		if (_fullPass)
		{
			_dynamicTree.QueryAllPairs([&](uint32_t a, uint32_t b) { testPair(a, b); });
			_dynamicTree.QueryPairs(_staticTree, [&](uint32_t a, uint32_t b) { testPair(a, b); });
			return;
		}

		// 少し動いた時 : 動いた体だけを問い合わせる。両方動いた組は、番号の大きい方からだけ数える (二重に数えない)
		for (const uint32_t index : _moved)
		{
			const Body& body = _bodies[index];
			if (!body.Alive) continue;
			if (!body.IsStatic)
			{
				_dynamicTree.Query(body.Box, [&](uint32_t other)
				{
					if (other == index) return;
					if (_bodies[other].MovedStep == _stepIndex && other < index) return;
					testPair(index, other);
				});
				_staticTree.Query(body.Box, [&](uint32_t other)
				{
					testPair(index, other);   // 動いた動かない物は、動く物の木だけに問い合わせる (下) ので二重にならない
				});
			}
			else
			{
				_dynamicTree.Query(body.Box, [&](uint32_t other)
				{
					if (_bodies[other].MovedStep == _stepIndex) return;   // 動いた動く物の方が数える
					testPair(index, other);
				});
			}
		}
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
		_candidates.clear();
		_found.clear();
		_stats.MovedBodies = static_cast<uint32_t>(_moved.size());

		// 1. ブロードフェーズ (BVH は動いた物だけ。比べる用の方法は毎回全部)
		_fullPass = false;
		switch (_mode)
		{
		case Broadphase::Bvh:   findPairsBvh();   break;
		case Broadphase::Brute: findPairsBrute(); break;
		case Broadphase::Grid:  findPairsGrid();  break;
		}
		const bool incremental = _mode == Broadphase::Bvh && !_fullPass;   // 全部を調べ直した時は、前の結果と全部比べる
		const Clock::time_point broad = Clock::now();

		// 2. ナローフェーズ : 候補の形が本当に重なっているか
		for (const uint64_t key : _candidates)
		{
			const uint32_t a = static_cast<uint32_t>(key >> 32), b = static_cast<uint32_t>(key & 0xFFFFFFFFu);
			if (Overlap(_bodies[a].World, _bodies[b].World)) _found.push_back(key);
		}
		const Clock::time_point narrow = Clock::now();

		// 3. イベント
		emitEvents(incremental);

		_moved.clear();
		_stepIndex++;
		if (_stepIndex == 0) _stepIndex = 1;   // 0 は「動いていない」の印にしないので飛ばす

		_stats.Candidates    = static_cast<uint32_t>(_candidates.size());
		_stats.Overlaps      = static_cast<uint32_t>(_overlaps.size());
		_stats.BroadphaseMs  = elapsedMs(start, broad);
		_stats.NarrowphaseMs = elapsedMs(broad, narrow);
		_stats.StepMs        = elapsedMs(start, Clock::now());
		_stats.Broadphase    = static_cast<uint32_t>(_mode);
		return static_cast<uint32_t>(_events.size());
	}

	void PhysicsWorld::emitEvents(bool incremental)
	{
		std::sort(_found.begin(), _found.end());
		_found.erase(std::unique(_found.begin(), _found.end()), _found.end());
		auto isFound = [&](uint64_t key) { return std::binary_search(_found.begin(), _found.end(), key); };

		// 離れた : 前に重なっていた組のうち、今回の結果に無い物
		//   BVH は動いた体を含む組だけ調べ直したので、両方とも動いていない組は重なったまま
		_exits.clear();
		for (const uint64_t key : _overlaps)
		{
			const uint32_t a = static_cast<uint32_t>(key >> 32), b = static_cast<uint32_t>(key & 0xFFFFFFFFu);
			if (incremental && _bodies[a].MovedStep != _stepIndex && _bodies[b].MovedStep != _stepIndex) continue;
			if (!isFound(key)) _exits.push_back(key);
		}
		for (const uint64_t key : _exits)
		{
			_overlaps.erase(key);
			_events.push_back({ handleOf(static_cast<uint32_t>(key >> 32)), handleOf(static_cast<uint32_t>(key & 0xFFFFFFFFu)), 1u, 0u });
		}

		// 重なり始めた : 今回の結果のうち、前に無かった物
		for (const uint64_t key : _found)
		{
			if (_overlaps.insert(key).second)
			{
				_events.push_back({ handleOf(static_cast<uint32_t>(key >> 32)), handleOf(static_cast<uint32_t>(key & 0xFFFFFFFFu)), 0u, 0u });
			}
		}
	}
}

/************************************
* End of file
************************************/
