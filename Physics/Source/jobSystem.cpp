/***********************************************************************************
* @file    jobSystem.cpp
* @brief   衝突判定の並列化。説明は jobSystem.h
*
* @author  Keigo Onari
* @date    2026/11/03
* @version v_1.1.0 (P1 : 仕事の後に少しの間スレッドを回して待つ。剛体を解く時は ParallelFor が続けて来るため)
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/

/******************************************************************
* Include Headers
******************************************************************/
#include "jobSystem.h"
#include <algorithm>
#include <chrono>
#include <immintrin.h>

namespace Physics
{
	namespace
	{
		// 仕事が無くなってから眠るまで回して待つ時間 (起こす手間 (数十 µs) を、続けて来る仕事で払わない)
		constexpr auto SPIN_TIME = std::chrono::microseconds(100);
	}

	void JobSystem::SetWorkers(uint32_t count)
	{
		if (count == _threads.size()) return;
		{
			std::lock_guard<std::mutex> lock(_mutex);
			_quit = true;
			_quitFlag.store(true);
		}
		_wake.notify_all();
		for (std::thread& thread : _threads) thread.join();
		_threads.clear();
		_quit = false;
		_quitFlag.store(false);
		// 今の仕事の番号を渡す (スレッドが動き出す前に ParallelFor が来ても、その仕事を見落とさない)
		const uint64_t generation = _generation.load();
		for (uint32_t i = 0; i < count; ++i) _threads.emplace_back([this, generation] { workerLoop(generation); });
	}

	void JobSystem::run(uint32_t count, uint32_t batch, const std::function<void(uint32_t, uint32_t)>& job)
	{
		_job   = &job;
		_count = count;
		_batch = (std::max)(1u, batch);
		_next.store(0, std::memory_order_relaxed);
		_pending.store(static_cast<uint32_t>(_threads.size()), std::memory_order_relaxed);
		_generation.fetch_add(1, std::memory_order_release);   // ここより前に書いた値は、番号を見たスレッドから見える
		{
			// 眠っているスレッドを起こす (待つ条件は鍵の中で見るので、起こし損ねない)
			std::lock_guard<std::mutex> lock(_mutex);
		}
		_wake.notify_all();

		work();   // 呼んだスレッドも働く

		// 終わりを待つ (少しの間は回して待ち、それでも終わらなければ眠る)
		const auto start = std::chrono::steady_clock::now();
		while (_pending.load(std::memory_order_acquire) != 0)
		{
			if (std::chrono::steady_clock::now() - start < SPIN_TIME) { _mm_pause(); continue; }
			std::unique_lock<std::mutex> lock(_mutex);
			_done.wait(lock, [this] { return _pending.load(std::memory_order_acquire) == 0; });
		}
		_job = nullptr;
	}

	void JobSystem::work()
	{
		for (;;)
		{
			const uint32_t begin = _next.fetch_add(_batch, std::memory_order_relaxed);
			if (begin >= _count) return;
			(*_job)(begin, (std::min)(begin + _batch, _count));
		}
	}

	void JobSystem::workerLoop(uint64_t seen)
	{
		for (;;)
		{
			// 次の仕事 : 少しの間は回して待つ
			const auto start = std::chrono::steady_clock::now();
			while (_generation.load(std::memory_order_acquire) == seen && !_quitFlag.load(std::memory_order_relaxed))
			{
				if (std::chrono::steady_clock::now() - start >= SPIN_TIME) break;
				_mm_pause();
			}
			{
				std::unique_lock<std::mutex> lock(_mutex);
				_wake.wait(lock, [&] { return _quit || _generation.load(std::memory_order_acquire) != seen; });
				if (_quit) return;
				seen = _generation.load(std::memory_order_acquire);
			}
			work();
			if (_pending.fetch_sub(1, std::memory_order_acq_rel) == 1)
			{
				std::lock_guard<std::mutex> lock(_mutex);
				_done.notify_one();
			}
		}
	}
}

/************************************
* End of file
************************************/
