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

	int DynamicTree::CreateProxy(const Aabb& tight, float margin, uint32_t userData)
	{
		const int id = allocateNode();
		_nodes[id].Box      = fatten(tight, margin);
		_nodes[id].UserData = userData;
		insertLeaf(id);
		return id;
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
		_nodes[newParent].Box    = Aabb::Union(leafBox, _nodes[sibling].Box);
		_nodes[newParent].Height = _nodes[sibling].Height + 1;
		_nodes[newParent].Child1 = sibling;
		_nodes[newParent].Child2 = leaf;
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
			Node& node = _nodes[index];
			node.Height = 1 + (std::max)(_nodes[node.Child1].Height, _nodes[node.Child2].Height);
			node.Box    = Aabb::Union(_nodes[node.Child1].Box, _nodes[node.Child2].Box);
			index = node.Parent;
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
				Node& node = _nodes[index];
				node.Box    = Aabb::Union(_nodes[node.Child1].Box, _nodes[node.Child2].Box);
				node.Height = 1 + (std::max)(_nodes[node.Child1].Height, _nodes[node.Child2].Height);
				index = node.Parent;
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

			A.Box    = Aabb::Union(_nodes[A.Child1].Box, _nodes[A.Child2].Box);
			A.Height = 1 + (std::max)(_nodes[A.Child1].Height, _nodes[A.Child2].Height);
			up.Box    = Aabb::Union(A.Box, _nodes[keep].Box);
			up.Height = 1 + (std::max)(A.Height, _nodes[keep].Height);
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
