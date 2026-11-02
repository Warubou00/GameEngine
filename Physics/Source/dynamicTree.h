/***********************************************************************************
* @file    dynamicTree.h
* @brief   動的 AABB ツリー (BVH)。重なっていそうな組を速く探す (ブロードフェーズ)
* @details Box2D の b2DynamicTree と同じ考え方を 3 次元にしたもの
*          - 葉 = 体 1 つ。葉の箱は少し太らせて (Fat AABB) 持つので、少し動いただけなら木を直さない
*          - 入れる時は「表面積の増え方が一番小さい所」へ下りていく (表面積 = 当たりそうな確率の目安)
*          - 入れた後は高さの差が 1 を超えた所を回転して直す (木が偏ってたどる数が増えないように)
*          - 問い合わせは箱が重なる枝だけをたどる (全部 × 全部 を調べない)
*          - 木の中 / 2 本の木の間で重なる葉の組を、節の組をたどって 1 回で全部探せる (QueryAllPairs / QueryPairs)
*            多くの物が動いた時は、1 個ずつ問い合わせるより速い (同じ節を何度もたどらない)
*          - 節ごとに「この枝の中のレイヤー (Layers)」と「この枝の中の物が当たる相手のレイヤー (Masks)」を
*            ビットの OR で持つ (C3)。当たりえないレイヤーの枝は、箱を調べる前にまるごと飛ばせる
*            (Unity のレイヤーの衝突の表と同じ考え方を、木の中でも使う)
*          - 多くの葉が動いた時は、葉を 1 つずつ入れ直さず、中の節を上から作り直す (Rebuild。C3)
*          - Query / RayCast は呼んだ所のスタックを使うので、同じ木を並列に問い合わせてよい (C3)
*            (木を直す関数と同時には呼ばないこと)
*
* @author  Keigo Onari
* @date    2026/11/02
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/
#pragma once
#ifndef PHYSICS_DYNAMIC_TREE_H_
#define PHYSICS_DYNAMIC_TREE_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <cstdint>
#include <vector>
#include "jobSystem.h"
#include <utility>
#include "mathTypes.h"

namespace Physics
{
	class DynamicTree
	{
	public:
		static constexpr int NULL_NODE = -1;

		// 葉を作る (tight : 本当の箱 / margin : 太らせる量)。戻り値は葉の番号
		// layers : この葉のレイヤー (ビット) / masks : 当たる相手のレイヤー (ビット)
		int  CreateProxy(const Aabb& tight, float margin, uint32_t userData, uint32_t layers = ~0u, uint32_t masks = ~0u);
		// 葉のレイヤー / 当たる相手を変える (上の節のビットも直す)
		void SetProxyFilter(int proxy, uint32_t layers, uint32_t masks);
		// 太らせた箱からはみ出した葉の箱を新しくし、上の節の箱を広げるだけ (木の形は変えない。C3)
		// 問い合わせはそのまま正しく答える (上の節が少し大きいだけ)。後で ReinsertProxy か Rebuild で形を直す
		// 戻り値 : はみ出したか
		// predict : 動いた向きに、さらに広げる量 (前の位置からのずれ × 倍率。Box2D の aabbMultiplier と同じ考え方)
		//           まっすぐ動く物は、しばらく太らせた箱の中に収まる (木を直す回数が減る)
		bool EnlargeProxy(int proxy, const Aabb& tight, float margin, const Float3& predict = Float3());
		// 葉を抜いて入れ直す (箱は EnlargeProxy で新しくしてある)
		void ReinsertProxy(int proxy);
		// 葉はそのまま (番号も変えない) で、中の節を全部作り直す (上から、長い軸の真ん中で 2 つに分ける)
		// 多くの葉が動いた時は、1 つずつ入れ直すより速く、木の質も良い
		// jobs があれば、上の数段を分けた後の枝を並列に作る
		// morton : 葉を Morton 符号 (中心の座標のビットを交互に並べた数) の順に 1 回だけ並べ、数の真ん中で分ける (速い)
		//          false なら、節ごとに一番広がった軸の真ん中で分ける (nth_element。少し質が良いが遅い)
		void Rebuild(JobSystem* jobs = nullptr, bool morton = true);
		// 葉の太らせた箱
		const Aabb& FatBox(int proxy) const { return _nodes[proxy].Box; }
		void DestroyProxy(int proxy);
		// 動かす。太らせた箱からはみ出した時だけ木を直す (戻り値 : 直したか)
		bool MoveProxy(int proxy, const Aabb& tight, float margin);

		const Aabb& GetFatAabb(int proxy) const { return _nodes[proxy].Box; }
		uint32_t    GetUserData(int proxy) const { return _nodes[proxy].UserData; }
		int         GetHeight() const { return _root == NULL_NODE ? 0 : _nodes[_root].Height; }

		// box と重なる葉ごとに callback(userData) を呼ぶ
		// queryLayers : 問い合わせる物のレイヤー / queryMask : 当たる相手のレイヤー。どちらかで当たりえない枝は飛ばす
		template<typename Callback>
		void Query(const Aabb& box, Callback&& callback, uint32_t queryLayers = ~0u, uint32_t queryMask = ~0u) const
		{
			if (_root == NULL_NODE) return;
			// 再帰の代わりに、関数の中の配列をスタックにする (毎回の確保を避ける)
			// 以前は木が持つ配列を使い回していたので、同じ木 (メッシュの三角形の木など) を並列に問い合わせられなかった
			NodeStack stack;
			stack.Push(_root);
			while (!stack.Empty())
			{
				const int id = stack.Pop();
				const Node& node = _nodes[id];
				if ((node.Layers & queryMask) == 0 || (node.Masks & queryLayers) == 0) continue;   // レイヤーで当たりえない枝
				if (!node.Box.Overlaps(box)) continue;
				if (node.IsLeaf())
				{
					callback(node.UserData);
				}
				else
				{
					stack.Push(node.Child1);
					stack.Push(node.Child2);
				}
			}
		}

		// レイが通る葉ごとに callback(userData, limit) を呼ぶ。callback は当たった距離で limit を縮める
		// (近い当たりが見つかると、それより遠い枝はたどらない)。invDir = 1 / 向き
		template<typename Callback>
		void RayCast(const Float3& origin, const Float3& invDir, float maxDistance, Callback&& callback, uint32_t layerMask = ~0u) const
		{
			if (_root == NULL_NODE) return;
			float limit = maxDistance;
			NodeStack stack;
			stack.Push(_root);
			while (!stack.Empty())
			{
				const int id = stack.Pop();
				const Node& node = _nodes[id];
				if ((node.Layers & layerMask) == 0) continue;   // レイが当たるレイヤーが無い枝
				if (!rayHitsBox(node.Box, origin, invDir, limit)) continue;
				if (node.IsLeaf()) callback(node.UserData, limit);
				else
				{
					stack.Push(node.Child1);
					stack.Push(node.Child2);
				}
			}
		}

		static bool rayHitsBox(const Aabb& box, const Float3& o, const Float3& inv, float limit)
		{
			float t0 = 0.0f, t1 = limit;
			const float bmin[3] = { box.min.x, box.min.y, box.min.z }, bmax[3] = { box.max.x, box.max.y, box.max.z };
			const float oo[3] = { o.x, o.y, o.z }, ii[3] = { inv.x, inv.y, inv.z };
			for (int i = 0; i < 3; ++i)
			{
				float a = (bmin[i] - oo[i]) * ii[i], b = (bmax[i] - oo[i]) * ii[i];
				if (a > b) std::swap(a, b);
				if (a > t0) t0 = a;
				if (b < t1) t1 = b;
				if (t0 > t1) return false;
			}
			return true;
		}

		// 木の中で、太らせた箱が重なる葉の組を全部 (各組 1 回)。callback(userDataA, userDataB)
		// layerPruning : レイヤーのビットで枝の組を飛ばすか (比べる用に切れる)
		template<typename Callback>
		void QueryAllPairs(Callback&& callback, bool layerPruning = true) const
		{
			if (_root == NULL_NODE) return;
			collidePairs(_root, _root, *this, callback, layerPruning);
		}

		// この木と other の間で、太らせた箱が重なる葉の組を全部。callback(この木の userData, other の userData)
		template<typename Callback>
		void QueryPairs(const DynamicTree& other, Callback&& callback, bool layerPruning = true) const
		{
			if (_root == NULL_NODE || other._root == NULL_NODE) return;
			collidePairs(_root, other._root, other, callback, layerPruning);
		}

	private:
		// 節の組 (a : この木 / b : other) をたどる。同じ木で a == b の時は「その中の組」
		template<typename Callback>
		void collidePairs(int rootA, int rootB, const DynamicTree& other, Callback& callback, bool layerPruning) const
		{
			const bool self = &other == this;
			std::vector<std::pair<int, int>>& stack = _pairStack;
			stack.clear();
			stack.emplace_back(rootA, rootB);
			while (!stack.empty())
			{
				const auto [ia, ib] = stack.back();
				stack.pop_back();
				const Node& a = _nodes[ia];
				const Node& b = other._nodes[ib];
				if (self && ia == ib)
				{
					// 同じ節 : 子どうしの組と、それぞれの子の中の組
					// (この枝の中に、互いに当たるレイヤーの組が 1 つも無ければ飛ばす)
					if (a.IsLeaf() || (layerPruning && (a.Layers & a.Masks) == 0)) continue;
					stack.emplace_back(a.Child1, a.Child1);
					stack.emplace_back(a.Child2, a.Child2);
					stack.emplace_back(a.Child1, a.Child2);
					continue;
				}
				// 2 つの枝の中の物が、どの組でもレイヤーで当たりえなければ飛ばす
				// (A のどれかが B のどれかと当たるには、A の Layers と B の Masks、B の Layers と A の Masks の両方が重なる必要がある)
				if (layerPruning && ((a.Layers & b.Masks) == 0 || (b.Layers & a.Masks) == 0)) continue;
				if (!a.Box.Overlaps(b.Box)) continue;
				if (a.IsLeaf() && b.IsLeaf())
				{
					callback(a.UserData, b.UserData);
				}
				else if (b.IsLeaf() || (!a.IsLeaf() && a.Box.SurfaceArea() >= b.Box.SurfaceArea()))
				{
					// 大きい方を分ける
					stack.emplace_back(a.Child1, ib);
					stack.emplace_back(a.Child2, ib);
				}
				else
				{
					stack.emplace_back(ia, b.Child1);
					stack.emplace_back(ia, b.Child2);
				}
			}
		}

		struct Node
		{
			Aabb     Box;
			uint32_t UserData = 0;
			int      Parent   = NULL_NODE;   // 空いている時は次の空き (Next)
			int      Child1   = NULL_NODE;
			int      Child2   = NULL_NODE;
			int      Height   = -1;          // 葉 = 0 / 空き = -1
			uint32_t Layers   = ~0u;         // この枝の中のレイヤー (OR)
			uint32_t Masks    = ~0u;         // この枝の中の物が当たる相手のレイヤー (OR)
			bool IsLeaf() const { return Child1 == NULL_NODE; }
		};

		// 問い合わせのスタック : 256 までは関数の中の配列 (木の高さは 1 万個で 20 前後なので、普通はこれで足りる)
		struct NodeStack
		{
			int              Local[256];
			std::vector<int> Heap;   // あふれた分
			int              Count = 0;
			void Push(int v)
			{
				if (Count < 256)                                Local[Count] = v;
				else if (Heap.size() <= size_t(Count - 256))    Heap.push_back(v);
				else                                            Heap[Count - 256] = v;
				++Count;
			}
			int  Pop()         { --Count; return Count < 256 ? Local[Count] : Heap[Count - 256]; }
			bool Empty() const { return Count == 0; }
		};

		std::vector<Node> _nodes;
		int _root     = NULL_NODE;
		int _freeList = NULL_NODE;
		mutable std::vector<std::pair<int, int>> _pairStack;

		int  allocateNode();
		void freeNode(int id);
		void insertLeaf(int leaf);
		void removeLeaf(int leaf);
		int  balance(int a);
		void refit(int id);   // 子から箱 / 高さ / レイヤーのビットを作り直す
		// Rebuild の作業用 : 葉の箱の中心 (min + max) と番号を並べた物 (節を何度も読みに行かず、続いたメモリで分ける)
		struct BuildItem { float Center[3]; int Leaf; uint32_t Code; };
		std::vector<BuildItem> _buildItems;
		std::vector<BuildItem> _buildTemp;    // Morton 符号で並べる時の作業用 (基数ソート)
		bool                   _buildMorton = true;
		void sortMorton();
		std::vector<int>       _buildPool;    // 中の節に使う番号 (先に全部取って小さい順に並べる。枝ごとに使う所が決まるので並列に作れる)
		struct BuildTask { BuildItem* Items; int Count; const int* Pool; int Parent; bool First; };
		std::vector<BuildTask> _buildTasks;   // 並列に作る枝
		std::vector<int>       _buildTop;     // 上の数段の節 (並列の後に、下から箱を作り直す)
		static int splitMedian(BuildItem* items, int count);   // 一番広がった軸の真ん中で分ける (戻り値 : 前半の数)
		static int splitMorton(const BuildItem* items, int count);   // Morton の順の物を、違う一番上のビットで分ける
		// items[0..count) の枝を作る。pool[0..count-1) の節を使う (自分 = pool[0]、前半 = pool[1..half)、後半 = pool[half..))
		int  buildRange(BuildItem* items, int count, const int* pool);
		int  splitTop(BuildItem* items, int count, const int* pool, int depth);   // 上の depth 段だけ分け、残りを _buildTasks へ
	};
}

#endif // PHYSICS_DYNAMIC_TREE_H_

/************************************
* End of file
************************************/
