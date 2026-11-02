/***********************************************************************************
* @file    jobSystem.h
* @brief   衝突判定の並列化 (常駐のワーカースレッドで ParallelFor)
* @details - スレッドは作った時から待っていて、ParallelFor のたびに起こす (毎回作らない)
*          - 呼んだスレッドも一緒に働く。仕事は batch 個ずつ取り合う (重さが偏っても均される)
*          - 数が batch 以下か、ワーカーが 0 なら、呼んだスレッドだけで行う (起こす手間の方が大きい)
*          - fn(begin, end) は別の範囲を同時に呼ぶので、範囲ごとに別の所へ書くこと
*          - 仕事の後、スレッドは 100 µs ほど回して次の仕事を待ってから眠る (P1。剛体を解く時は ParallelFor が続けて来る)
*
* @author  Keigo Onari
* @date    2026/11/03
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/
#pragma once
#ifndef PHYSICS_JOB_SYSTEM_H_
#define PHYSICS_JOB_SYSTEM_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <atomic>
#include <condition_variable>
#include <cstdint>
#include <functional>
#include <mutex>
#include <thread>
#include <vector>

namespace Physics
{
	class JobSystem
	{
	public:
		~JobSystem() { SetWorkers(0); }

		// 追加のスレッドの数 (0 = 呼んだスレッドだけ)。作り直すので Step の外で呼ぶ
		void     SetWorkers(uint32_t count);
		uint32_t Workers() const { return static_cast<uint32_t>(_threads.size()); }

		template<typename Fn>
		void ParallelFor(uint32_t count, uint32_t batch, Fn&& fn)
		{
			if (count == 0) return;
			if (_threads.empty() || count <= batch)
			{
				fn(0u, count);
				return;
			}
			const std::function<void(uint32_t, uint32_t)> job = fn;
			run(count, batch, job);
		}

	private:
		void run(uint32_t count, uint32_t batch, const std::function<void(uint32_t, uint32_t)>& job);
		void work();
		void workerLoop(uint64_t seen);

		std::vector<std::thread> _threads;
		std::mutex               _mutex;
		std::condition_variable  _wake;
		std::condition_variable  _done;
		const std::function<void(uint32_t, uint32_t)>* _job = nullptr;
		uint32_t                 _count = 0;
		uint32_t                 _batch = 1;
		std::atomic<uint32_t>    _next{ 0 };
		std::atomic<uint32_t>    _pending{ 0 };      // まだ終わっていないワーカー
		std::atomic<uint64_t>    _generation{ 0 };   // 仕事の番号 (ワーカーは変わったら起きる)
		bool                     _quit = false;
		std::atomic<bool>        _quitFlag{ false }; // 回して待っている間に見る
	};
}

#endif // PHYSICS_JOB_SYSTEM_H_

/************************************
* End of file
************************************/
