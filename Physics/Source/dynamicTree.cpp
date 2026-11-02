/***********************************************************************************
* @file    dynamicTree.cpp
* @brief   動的 AABB ツリー (BVH)。説明は dynamicTree.h
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
#include "dynamicTree.h"
#include <algorithm>
#include <cfloat>

namespace Physics
{
	namespace
	{
		Aabb fatten(const Aabb& tight, float margin)
		{
			const Float3 m(margin, margin, margin);
			return { tight.min - m, tight.max + m };
		}
	}

	int DynamicTree::allocateNode()
	{
		if (_freeList == NULL_NODE)
		{
			_nodes.emplace_back();
			_nodes.back().Height = 0;
			return static_cast<int>(_nodes.size() - 1);
		}
		const int id = _freeList;
		_freeList = _nodes[id].Parent;
		_nodes[id] = Node();
		_nodes[id].Height = 0;
		return id;
	}

	void DynamicTree::freeNode(int id)
	{
		_nodes[id].Parent = _freeList;
		_nodes[id].Height = -1;
		_freeList = id;
	}

	int DynamicTree::CreateProxy(const Aabb& tight, float margin, uint32_t userData, uint32_t layers, uint32_t masks)
	{
		const int id = allocateNode();
		_nodes[id].Box      = fatten(tight, margin);
		_nodes[id].UserData = userData;
		_nodes[id].Layers   = layers;
		_nodes[id].Masks    = masks;
		insertLeaf(id);
		return id;
	}

	void DynamicTree::SetProxyFilter(int proxy, uint32_t layers, uint32_t masks)
	{
		_nodes[proxy].Layers = layers;
		_nodes[proxy].Masks  = masks;
		for (int index = _nodes[proxy].Parent; index != NULL_NODE; index = _nodes[index].Parent) refit(index);
	}

	void DynamicTree::refit(int id)
	{
		Node& node = _nodes[id];
		const Node& c1 = _nodes[node.Child1];
		const Node& c2 = _nodes[node.Child2];
		node.Box    = Aabb::Union(c1.Box, c2.Box);
		node.Height = 1 + (std::max)(c1.Height, c2.Height);
		node.Layers = c1.Layers | c2.Layers;
		node.Masks  = c1.Masks | c2.Masks;
	}

	void DynamicTree::DestroyProxy(int proxy)
	{
		removeLeaf(proxy);
		freeNode(proxy);
	}

	bool DynamicTree::MoveProxy(int proxy, const Aabb& tight, float margin)
	{
		// 太らせた箱の中なら木はそのまま (小さく動いている間は何もしない)
		if (_nodes[proxy].Box.Contains(tight)) return false;
		removeLeaf(proxy);
		_nodes[proxy].Box = fatten(tight, margin);
		insertLeaf(proxy);
		return true;
	}

	bool DynamicTree::EnlargeProxy(int proxy, const Aabb& tight, float margin, const Float3& predict)
	{
		if (_nodes[proxy].Box.Contains(tight)) return false;
		Aabb box = fatten(tight, margin);
		box.min = box.min + Float3((std::min)(predict.x, 0.0f), (std::min)(predict.y, 0.0f), (std::min)(predict.z, 0.0f));
		box.max = box.max + Float3((std::max)(predict.x, 0.0f), (std::max)(predict.y, 0.0f), (std::max)(predict.z, 0.0f));
		_nodes[proxy].Box = box;
		// 上の節は、新しい箱を含むまで広げる (含んでいれば、そこから上も含んでいる)
		for (int index = _nodes[proxy].Parent; index != NULL_NODE && !_nodes[index].Box.Contains(box); index = _nodes[index].Parent)
			_nodes[index].Box = Aabb::Union(_nodes[index].Box, box);
		return true;
	}

	void DynamicTree::ReinsertProxy(int proxy)
	{
		removeLeaf(proxy);
		insertLeaf(proxy);
	}

	void DynamicTree::Rebuild(JobSystem* jobs, bool morton)
	{
		_buildMorton = morton;
		if (_root == NULL_NODE) return;
		// 葉を集め、中の節は全部空きに戻す
		_buildItems.clear();
		for (int i = 0; i < static_cast<int>(_nodes.size()); ++i)
		{
			const Node& node = _nodes[i];
			if (node.Height < 0) continue;   // 空き
			if (node.IsLeaf())
			{
				const Float3 c = node.Box.min + node.Box.max;
				_buildItems.push_back({ { c.x, c.y, c.z }, i, 0u });
			}
			else
			{
				freeNode(i);
			}
		}
		const int leafCount = static_cast<int>(_buildItems.size());
		if (leafCount == 1)
		{
			_root = _buildItems[0].Leaf;
			_nodes[_root].Parent = NULL_NODE;
			return;
		}

		if (morton) sortMorton();

		// 中の節 (葉の数 - 1) を先に取り、小さい番号の順に並べる (親が子より前に来て、たどる時にメモリが近い)
		_buildPool.clear();
		for (int i = 0; i < leafCount - 1; ++i) _buildPool.push_back(allocateNode());
		std::sort(_buildPool.begin(), _buildPool.end());

		// 2048 個以上で並列にできる時 : 上の 3 段 (8 本の枝) を分けてから、枝を並列に作る
		const bool parallel = jobs && jobs->Workers() > 0 && leafCount >= 2048;
		if (!parallel)
		{
			_root = buildRange(_buildItems.data(), leafCount, _buildPool.data());
		}
		else
		{
			_buildTasks.clear();
			_buildTop.clear();
			_root = splitTop(_buildItems.data(), leafCount, _buildPool.data(), 3);
			jobs->ParallelFor(static_cast<uint32_t>(_buildTasks.size()), 1, [&](uint32_t begin, uint32_t end)
			{
				for (uint32_t t = begin; t < end; ++t)
				{
					const BuildTask& task = _buildTasks[t];
					const int child = buildRange(task.Items, task.Count, task.Pool);
					_nodes[child].Parent = task.Parent;
					if (task.First) _nodes[task.Parent].Child1 = child;
					else            _nodes[task.Parent].Child2 = child;
				}
			});
			// 上の節は、子が全部できてから下から順に (上から取った順の逆)
			for (auto it = _buildTop.rbegin(); it != _buildTop.rend(); ++it) refit(*it);
		}
		_nodes[_root].Parent = NULL_NODE;
	}

	void DynamicTree::sortMorton()
	{
		// 中心を全体の範囲で 0〜1023 にし、x / y / z のビットを交互に並べた 30bit の数にする (近い物は近い数になる)
		float lo[3] = { FLT_MAX, FLT_MAX, FLT_MAX }, hi[3] = { -FLT_MAX, -FLT_MAX, -FLT_MAX };
		for (const BuildItem& item : _buildItems)
		{
			for (int k = 0; k < 3; ++k)
			{
				lo[k] = (std::min)(lo[k], item.Center[k]);
				hi[k] = (std::max)(hi[k], item.Center[k]);
			}
		}
		float scale[3];
		for (int k = 0; k < 3; ++k) scale[k] = 1023.0f / (std::max)(hi[k] - lo[k], 1e-6f);
		auto expand = [](uint32_t v)   // 10bit → 3 ビットおき
		{
			v = (v | (v << 16)) & 0x030000FFu;
			v = (v | (v << 8))  & 0x0300F00Fu;
			v = (v | (v << 4))  & 0x030C30C3u;
			v = (v | (v << 2))  & 0x09249249u;
			return v;
		};
		for (BuildItem& item : _buildItems)
		{
			uint32_t q[3];
			for (int k = 0; k < 3; ++k) q[k] = (std::min)(1023u, static_cast<uint32_t>((item.Center[k] - lo[k]) * scale[k]));
			item.Code = (expand(q[0]) << 2) | (expand(q[1]) << 1) | expand(q[2]);
		}

		// 基数ソート (8bit ずつ 4 回。比べて並べるより速い)
		_buildTemp.resize(_buildItems.size());
		BuildItem* src = _buildItems.data();
		BuildItem* dst = _buildTemp.data();
		const size_t count = _buildItems.size();
		for (int shift = 0; shift < 32; shift += 8)
		{
			uint32_t offsets[256] = {};
			for (size_t i = 0; i < count; ++i) offsets[(src[i].Code >> shift) & 0xFF]++;
			uint32_t sum = 0;
			for (uint32_t& o : offsets) { const uint32_t c = o; o = sum; sum += c; }
			for (size_t i = 0; i < count; ++i) dst[offsets[(src[i].Code >> shift) & 0xFF]++] = src[i];
			std::swap(src, dst);
		}
		// 4 回 (偶数回) なので、結果は _buildItems に戻っている
	}

	int DynamicTree::splitMorton(const BuildItem* items, int count)
	{
		// 最初と最後の符号で、違う一番上のビットが変わる所で分ける (空間を 2 つに割る面で分けるのと同じ。Karras 2012)
		// 全部同じ符号なら数の真ん中
		const uint32_t first = items[0].Code, last = items[count - 1].Code;
		if (first == last) return count / 2;
		uint32_t bit = 31;
		while (((first ^ last) >> bit) == 0) --bit;
		int lo = 0, hi = count - 1;   // items[lo] は bit が 0、items[hi] は 1
		while (hi - lo > 1)
		{
			const int mid = (lo + hi) / 2;
			if ((items[mid].Code >> bit) & 1u) hi = mid;
			else                               lo = mid;
		}
		return hi;
	}

	int DynamicTree::splitMedian(BuildItem* items, int count)
	{
		// 箱の中心が一番広がっている軸で、真ん中 (数が半分になる所) で分ける
		// (中心は min + max で比べる。2 で割らなくても順番は同じ)
		float lo[3] = { FLT_MAX, FLT_MAX, FLT_MAX }, hi[3] = { -FLT_MAX, -FLT_MAX, -FLT_MAX };
		for (int i = 0; i < count; ++i)
		{
			for (int k = 0; k < 3; ++k)
			{
				lo[k] = (std::min)(lo[k], items[i].Center[k]);
				hi[k] = (std::max)(hi[k], items[i].Center[k]);
			}
		}
		const float dx = hi[0] - lo[0], dy = hi[1] - lo[1], dz = hi[2] - lo[2];
		const int axis = dx >= dy && dx >= dz ? 0 : (dy >= dz ? 1 : 2);
		const int half = count / 2;
		std::nth_element(items, items + half, items + count, [axis](const BuildItem& a, const BuildItem& b) { return a.Center[axis] < b.Center[axis]; });
		return half;
	}

	int DynamicTree::buildRange(BuildItem* items, int count, const int* pool)
	{
		if (count == 1) return items[0].Leaf;
		const int half   = _buildMorton ? splitMorton(items, count) : splitMedian(items, count);   // Morton の順なら、もう並んでいる
		const int id     = pool[0];
		const int child1 = buildRange(items, half, pool + 1);
		const int child2 = buildRange(items + half, count - half, pool + half);
		_nodes[id].Child1 = child1;
		_nodes[id].Child2 = child2;
		_nodes[child1].Parent = id;
		_nodes[child2].Parent = id;
		refit(id);
		return id;
	}

	int DynamicTree::splitTop(BuildItem* items, int count, const int* pool, int depth)
	{
		if (count == 1) return items[0].Leaf;
		const int half = _buildMorton ? splitMorton(items, count) : splitMedian(items, count);
		const int id   = pool[0];
		_buildTop.push_back(id);
		auto child = [&](BuildItem* childItems, int childCount, const int* childPool, bool first)
		{
			if (depth > 1 && childCount > 1)
			{
				const int c = splitTop(childItems, childCount, childPool, depth - 1);
				_nodes[c].Parent = id;
				(first ? _nodes[id].Child1 : _nodes[id].Child2) = c;
			}
			else
			{
				_buildTasks.push_back({ childItems, childCount, childPool, id, first });
			}
		};
		child(items, half, pool + 1, true);
		child(items + half, count - half, pool + half, false);
		return id;
	}

	void DynamicTree::insertLeaf(int leaf)
	{
		if (_root == NULL_NODE)
		{
			_root = leaf;
			_nodes[leaf].Parent = NULL_NODE;
			return;
		}

		// 1. 入れる場所 : 「ここで新しい親を作る」と「子へ下りる」の表面積の増え方を比べて、小さい方へ下りる
		const Aabb leafBox = _nodes[leaf].Box;
		int index = _root;
		while (!_nodes[index].IsLeaf())
		{
			const Node& node = _nodes[index];
			const float area         = node.Box.SurfaceArea();
			const float combinedArea = Aabb::Union(node.Box, leafBox).SurfaceArea();
			const float cost            = 2.0f * combinedArea;            // ここに親を作る
			const float inheritanceCost = 2.0f * (combinedArea - area);   // 下りると、ここから上の箱が大きくなる分

			auto childCost = [&](int child)
			{
				const Aabb box = Aabb::Union(leafBox, _nodes[child].Box);
				if (_nodes[child].IsLeaf()) return box.SurfaceArea() + inheritanceCost;
				return (box.SurfaceArea() - _nodes[child].Box.SurfaceArea()) + inheritanceCost;
			};
			const float cost1 = childCost(node.Child1);
			const float cost2 = childCost(node.Child2);
			if (cost < cost1 && cost < cost2) break;
			index = cost1 < cost2 ? node.Child1 : node.Child2;
		}
		const int sibling = index;

		// 2. 兄弟と新しい葉をまとめる親を作る
		const int oldParent = _nodes[sibling].Parent;
		const int newParent = allocateNode();
		_nodes[newParent].Parent = oldParent;
		_nodes[newParent].Child1 = sibling;
		_nodes[newParent].Child2 = leaf;
		refit(newParent);
		_nodes[sibling].Parent = newParent;
		_nodes[leaf].Parent    = newParent;
		if (oldParent != NULL_NODE)
		{
			if (_nodes[oldParent].Child1 == sibling) _nodes[oldParent].Child1 = newParent;
			else                                    _nodes[oldParent].Child2 = newParent;
		}
		else
		{
			_root = newParent;
		}

		// 3. 上へ戻りながら、高さを直して箱を広げる (偏った所は回転する)
		index = _nodes[leaf].Parent;
		while (index != NULL_NODE)
		{
			index = balance(index);
			refit(index);
			index = _nodes[index].Parent;
		}
	}

	void DynamicTree::removeLeaf(int leaf)
	{
		if (leaf == _root)
		{
			_root = NULL_NODE;
			return;
		}
		const int parent      = _nodes[leaf].Parent;
		const int grandParent = _nodes[parent].Parent;
		const int sibling     = _nodes[parent].Child1 == leaf ? _nodes[parent].Child2 : _nodes[parent].Child1;

		if (grandParent != NULL_NODE)
		{
			// 親を消して、兄弟を祖父につなぐ
			if (_nodes[grandParent].Child1 == parent) _nodes[grandParent].Child1 = sibling;
			else                                     _nodes[grandParent].Child2 = sibling;
			_nodes[sibling].Parent = grandParent;
			freeNode(parent);

			int index = grandParent;
			while (index != NULL_NODE)
			{
				index = balance(index);
				refit(index);
				index = _nodes[index].Parent;
			}
		}
		else
		{
			_root = sibling;
			_nodes[sibling].Parent = NULL_NODE;
			freeNode(parent);
		}
	}

	int DynamicTree::balance(int iA)
	{
		// ==============================================================
		// A の子 B / C の高さの差が 2 以上なら回転する (AVL 木と同じ)。戻り値は A の場所に来た節
		//         A                 C
		//       /   \             /   \
		//      B     C    →     A     (F または G の高い方)
		//           / \        / \
		//          F   G      B  (F または G の低い方)
		// ==============================================================
		Node& A = _nodes[iA];
		if (A.IsLeaf() || A.Height < 2) return iA;

		const int iB = A.Child1;
		const int iC = A.Child2;
		const int diff = _nodes[iC].Height - _nodes[iB].Height;

		auto rotate = [&](int iUp, int iOther) -> int   // iUp (高い方の子) を A の場所へ上げる
		{
			Node& up = _nodes[iUp];
			const int iF = up.Child1;
			const int iG = up.Child2;

			up.Child1 = iA;
			up.Parent = A.Parent;
			A.Parent  = iUp;
			if (up.Parent != NULL_NODE)
			{
				if (_nodes[up.Parent].Child1 == iA) _nodes[up.Parent].Child1 = iUp;
				else                               _nodes[up.Parent].Child2 = iUp;
			}
			else
			{
				_root = iUp;
			}

			// F と G の高い方を up に残し、低い方を A へ
			const bool fHigher = _nodes[iF].Height > _nodes[iG].Height;
			const int keep = fHigher ? iF : iG;
			const int give = fHigher ? iG : iF;
			up.Child2 = keep;
			if (A.Child1 == iUp) A.Child1 = give;
			else                 A.Child2 = give;
			_nodes[give].Parent = iA;

			refit(iA);
			refit(iUp);
			(void)iOther;
			return iUp;
		};

		if (diff > 1)  return rotate(iC, iB);
		if (diff < -1) return rotate(iB, iC);
		return iA;
	}
}

/************************************
* End of file
************************************/
