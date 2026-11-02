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
#include <utility>
#include "mathTypes.h"

namespace Physics
{
	class DynamicTree
	{
	public:
		static constexpr int NULL_NODE = -1;

		// 葉を作る (tight : 本当の箱 / margin : 太らせる量)。戻り値は葉の番号
		int  CreateProxy(const Aabb& tight, float margin, uint32_t userData);
		void DestroyProxy(int proxy);
		// 動かす。太らせた箱からはみ出した時だけ木を直す (戻り値 : 直したか)
		bool MoveProxy(int proxy, const Aabb& tight, float margin);

		const Aabb& GetFatAabb(int proxy) const { return _nodes[proxy].Box; }
		uint32_t    GetUserData(int proxy) const { return _nodes[proxy].UserData; }
		int         GetHeight() const { return _root == NULL_NODE ? 0 : _nodes[_root].Height; }

		// box と重なる葉ごとに callback(userData) を呼ぶ
		template<typename Callback>
		void Query(const Aabb& box, Callback&& callback) const
		{
			if (_root == NULL_NODE) return;
			// 再帰の代わりに、使い回す配列をスタックにする (毎回の確保を避ける)
			std::vector<int>& stack = _stack;
			stack.clear();
			stack.push_back(_root);
			while (!stack.empty())
			{
				const int id = stack.back();
				stack.pop_back();
				const Node& node = _nodes[id];
				if (!node.Box.Overlaps(box)) continue;
				if (node.IsLeaf())
				{
					callback(node.UserData);
				}
				else
				{
					stack.push_back(node.Child1);
					stack.push_back(node.Child2);
				}
			}
		}

		// 木の中で、太らせた箱が重なる葉の組を全部 (各組 1 回)。callback(userDataA, userDataB)
		template<typename Callback>
		void QueryAllPairs(Callback&& callback) const
		{
			if (_root == NULL_NODE) return;
			collidePairs(_root, _root, *this, callback);
		}

		// この木と other の間で、太らせた箱が重なる葉の組を全部。callback(この木の userData, other の userData)
		template<typename Callback>
		void QueryPairs(const DynamicTree& other, Callback&& callback) const
		{
			if (_root == NULL_NODE || other._root == NULL_NODE) return;
			collidePairs(_root, other._root, other, callback);
		}

	private:
		// 節の組 (a : この木 / b : other) をたどる。同じ木で a == b の時は「その中の組」
		template<typename Callback>
		void collidePairs(int rootA, int rootB, const DynamicTree& other, Callback& callback) const
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
					if (a.IsLeaf()) continue;
					stack.emplace_back(a.Child1, a.Child1);
					stack.emplace_back(a.Child2, a.Child2);
					stack.emplace_back(a.Child1, a.Child2);
					continue;
				}
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
			bool IsLeaf() const { return Child1 == NULL_NODE; }
		};

		std::vector<Node> _nodes;
		int _root     = NULL_NODE;
		int _freeList = NULL_NODE;
		mutable std::vector<int> _stack;
		mutable std::vector<std::pair<int, int>> _pairStack;

		int  allocateNode();
		void freeNode(int id);
		void insertLeaf(int leaf);
		void removeLeaf(int leaf);
		int  balance(int a);
	};
}

#endif // PHYSICS_DYNAMIC_TREE_H_

/************************************
* End of file
************************************/
