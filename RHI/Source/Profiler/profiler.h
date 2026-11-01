
/***********************************************************************************
* @file    profiler.h
* @brief   プロファイラー (ImGui のウィンドウに計測値を表示する)
* @details 表示する内容
*            - FPS / フレーム時間 (平均・最小・最大) とそのグラフ
*            - CPU の内訳 (待ち / シーンの記録 / UI / Present)
*            - GPU の時間 (タイムスタンプクエリで計測したシーン / UI の描画時間)
*            - 描画数 / 三角形数 / オブジェクト数
*            - VRAM の使用量と上限 (OS が割り当てた予算)
*            - アップロード量 / リングバッファの使用量
*            - 画面サイズ / VSync / テアリング
*            - Renderer などから渡された任意の値 (RHI::IRenderHardwareInterface::ReportProfilerValue)
*
*          以前は C# 側でウィンドウのタイトルに FPS だけを出していた
*
* @author  Keigo Onari
* @date    2026/11/01
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/

#pragma once
#ifndef RHI_PROFILER_H_
#define RHI_PROFILER_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <string>
#include <vector>
#include <chrono>

#include "frameStats.h"

struct IDXGIAdapter3;

namespace RHI
{
	class Profiler
	{
	public:
		// グラフに残すフレーム数
		static constexpr int HISTORY_SIZE = 240;

		Profiler();
		~Profiler();

		// adapter は VRAM の使用量を問い合わせるのに使う (nullptr なら VRAM は表示しない)
		void Initialize(IDXGIAdapter3* adapter, const std::wstring& gpuName);

		// 1 フレーム分の計測値を記録する (毎フレーム呼ぶ)
		void AddFrame(const FrameStats& stats);

		// 任意の値を表示に加える (同じ名前なら上書き)
		void SetValue(const char* name, double value);

		// ImGui のウィンドウを作る (DebugUI::BeginFrame と EndFrame の間で呼ぶ)
		void Draw();

	private:
		void updateVideoMemory();

		IDXGIAdapter3* _adapter = nullptr;      // 所有しない
		std::string    _gpuName;

		FrameStats     _latest;                 // 最新の値

		// グラフ用の履歴 (リング)
		float          _frameMsHistory[HISTORY_SIZE] = {};
		float          _gpuMsHistory[HISTORY_SIZE]   = {};
		int            _historyOffset = 0;

		// 0.5 秒ごとの平均 (毎フレームの値は揺れて読めないので)
		std::chrono::steady_clock::time_point _windowStart;
		int            _windowFrames   = 0;
		double         _windowSumMs    = 0.0;
		double         _windowMinMs    = 0.0;
		double         _windowMaxMs    = 0.0;
		double         _avgFps         = 0.0;
		double         _avgFrameMs     = 0.0;
		double         _minFrameMs     = 0.0;
		double         _maxFrameMs     = 0.0;

		// VRAM (0.5 秒ごとに更新)
		uint64_t       _vramUsage  = 0;
		uint64_t       _vramBudget = 0;

		std::vector<std::pair<std::string, double>> _values;
	};
}

#endif // RHI_PROFILER_H_
