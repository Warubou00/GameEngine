
/***********************************************************************************
* @file    scopedTimer.h
* @brief   処理時間の計測 (高速化の効果確認用)
* @details スコープを抜けた時に経過時間を Logger に出す
*
*            {
*                Core::ScopedTimer timer("LoadModel");
*                ...                                   // ← この区間の時間を計る
*            }                                         // → "[Time] LoadModel : 12.345 ms"
*
* @author  Keigo Onari
* @date    2026/11/01
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note
************************************************************************************/

#pragma once
#ifndef CORE_SCOPED_TIMER_H_
#define CORE_SCOPED_TIMER_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <chrono>
#include <cstdio>

#include "logger.h"

namespace Core
{
	class ScopedTimer
	{
	public:
		explicit ScopedTimer(const char* label)
			: _label(label), _start(std::chrono::steady_clock::now()) {}

		~ScopedTimer()
		{
			char message[160] = {};
			sprintf_s(message, "[Time] %s : %.3f ms", _label, ElapsedMs());
			Core::Logger::Log(message);
		}

		// 途中経過 (ミリ秒)
		double ElapsedMs()const
		{
			return std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - _start).count();
		}

		ScopedTimer(const ScopedTimer&) = delete;
		ScopedTimer& operator=(const ScopedTimer&) = delete;

	private:
		const char*                           _label;
		std::chrono::steady_clock::time_point _start;
	};
}

#endif // CORE_SCOPED_TIMER_H_
