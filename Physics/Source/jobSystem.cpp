/***********************************************************************************
* @file    jobSystem.cpp
* @brief   衝突判定の並列化。説明は jobSystem.h
*
* @author  Keigo Onari
* @date    2026/11/03
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/

/******************************************************************
* Include Headers
******************************************************************/
#include "jobSystem.h"
#include <algorithm>

namespace Physics
{
	void JobSystem::SetWorkers(uint32_t count)
	{
		if (count == _threads.size()) return;
		{
			std::lock_guard<std::mutex> lock(_mutex);
			_quit = true;
		}
		_wake.notify_all();
		for (std::thread& thread : _threads) thread.join();
		_threads.clear();
		_quit = false;
		// 今の仕事の番号を渡す (スレッドが動き出す前に ParallelFor が来ても、その仕事を見落とさない)
		const uint64_t generation = _generation;
		for (uint32_t i = 0; i < count; ++i) _threads.emplace_back([this, generation] { workerLoop(generation); });
	}

	void JobSystem::run(uint32_t count, uint32_t batch, const std::function<void(uint32_t, uint32_t)>& job)
	{
		{
			std::lock_guard<std::mutex> lock(_mutex);
			_job     = &job;
			_count   = count;
			_batch   = (std::max)(1u, batch);
			_next.store(0, std::memory_order_relaxed);
			_pending = static_cast<uint32_t>(_threads.size());
			_generation++;
		}
		_wake.notify_all();
		work();   // 呼んだスレッドも働く
		std::unique_lock<std::mutex> lock(_mutex);
		_done.wait(lock, [this] { return _pending == 0; });
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
			{
				std::unique_lock<std::mutex> lock(_mutex);
				_wake.wait(lock, [&] { return _quit || _generation != seen; });
				if (_quit) return;
				seen = _generation;
			}
			work();
			{
				std::lock_guard<std::mutex> lock(_mutex);
				if (--_pending == 0) _done.notify_one();
			}
		}
	}
}

/************************************
* End of file
************************************/
