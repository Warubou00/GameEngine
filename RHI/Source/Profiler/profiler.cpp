
/***********************************************************************************
* @file    profiler.cpp
* @brief   プロファイラー (ImGui のウィンドウに計測値を表示する)
* @details
*
* @author  Keigo Onari
* @date    2026/11/01
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/


/******************************************************************
* Include Headers
******************************************************************/
#include "profiler.h"

#include <algorithm>
#include <cfloat>       // FLT_MAX
#include <dxgi1_6.h>

#include "../../../ImGui/imgui.h"
#include "../../../Core/Source/Log/logger.h"

namespace RHI
{
	namespace
	{
		constexpr double AVERAGE_WINDOW_SEC = 0.5;   // FPS などを平均する時間

		// 1234567 → "1.18 MB" のように読みやすくする
		void formatBytes(char* out, size_t size, uint64_t bytes)
		{
			const double kb = 1024.0, mb = kb * 1024.0, gb = mb * 1024.0;
			if      (bytes >= static_cast<uint64_t>(gb)) sprintf_s(out, size, "%.2f GB", bytes / gb);
			else if (bytes >= static_cast<uint64_t>(mb)) sprintf_s(out, size, "%.2f MB", bytes / mb);
			else if (bytes >= static_cast<uint64_t>(kb)) sprintf_s(out, size, "%.1f KB", bytes / kb);
			else                                         sprintf_s(out, size, "%llu B", bytes);
		}

		std::string toUtf8(const std::wstring& w)
		{
			if (w.empty()) return std::string();
			const int len = WideCharToMultiByte(CP_UTF8, 0, w.c_str(), static_cast<int>(w.size()), nullptr, 0, nullptr, nullptr);
			std::string s(static_cast<size_t>(len), '\0');
			WideCharToMultiByte(CP_UTF8, 0, w.c_str(), static_cast<int>(w.size()), &s[0], len, nullptr, nullptr);
			return s;
		}

		// 表の 1 行 (名前 | 値)
		void row(const char* name, const char* fmt, double value)
		{
			ImGui::TableNextRow();
			ImGui::TableSetColumnIndex(0); ImGui::TextUnformatted(name);
			ImGui::TableSetColumnIndex(1); ImGui::Text(fmt, value);
		}
		void rowText(const char* name, const char* text)
		{
			ImGui::TableNextRow();
			ImGui::TableSetColumnIndex(0); ImGui::TextUnformatted(name);
			ImGui::TableSetColumnIndex(1); ImGui::TextUnformatted(text);
		}
	}

	Profiler::Profiler() = default;
	Profiler::~Profiler() = default;

	void Profiler::Initialize(IDXGIAdapter3* adapter, const std::wstring& gpuName)
	{
		_adapter     = adapter;
		_gpuName     = toUtf8(gpuName);
		_windowStart = std::chrono::steady_clock::now();
		_startTime   = _windowStart;
		updateVideoMemory();
	}

	void Profiler::AddFrame(const FrameStats& stats)
	{
		_latest = stats;

		// 起動から 1 秒以降の合計 (LogSummary 用)
		if (std::chrono::duration<double>(std::chrono::steady_clock::now() - _startTime).count() >= 1.0)
		{
			_totalFrames++;
			_totalFrameMs  += stats.FrameMs;
			_totalRecordMs += stats.SceneRecordMs;
			_totalUiMs     += stats.UiMs;
			if (stats.GpuValid) { _totalGpuMs += stats.GpuTotalMs; _totalGpuFrames++; }
			_lastDrawCalls = stats.DrawCalls;
			_lastIndirect  = stats.IndirectDraw;
			_lastInstancing = stats.Instancing;
			_lastInstances  = stats.Instances;
			_lastCulling    = stats.GpuCulling;
			_lastOcclusion  = stats.OcclusionCulling;
			if (stats.VisibleValid) { _lastVisible = stats.VisibleInstances; _lastVisibleDraws = stats.VisibleDraws; }
		}

		// グラフの履歴
		_frameMsHistory[_historyOffset] = static_cast<float>(stats.FrameMs);
		_gpuMsHistory[_historyOffset]   = stats.GpuValid ? static_cast<float>(stats.GpuTotalMs) : 0.0f;
		_historyOffset = (_historyOffset + 1) % HISTORY_SIZE;

		// 平均 / 最小 / 最大
		if (_windowFrames == 0)
		{
			_windowMinMs = _windowMaxMs = stats.FrameMs;
		}
		_windowFrames++;
		_windowSumMs += stats.FrameMs;
		_windowMinMs  = (std::min)(_windowMinMs, stats.FrameMs);
		_windowMaxMs  = (std::max)(_windowMaxMs, stats.FrameMs);

		const auto now = std::chrono::steady_clock::now();
		const double elapsed = std::chrono::duration<double>(now - _windowStart).count();
		if (elapsed >= AVERAGE_WINDOW_SEC && _windowFrames > 0)
		{
			_avgFps     = _windowFrames / elapsed;
			_avgFrameMs = _windowSumMs / _windowFrames;
			_minFrameMs = _windowMinMs;
			_maxFrameMs = _windowMaxMs;

			_windowFrames = 0;
			_windowSumMs  = 0.0;
			_windowStart  = now;

			// VRAM の問い合わせはカーネルを呼ぶので、毎フレームではなくここでまとめて行う
			updateVideoMemory();
		}
	}

	void Profiler::SetValue(const char* name, double value)
	{
		if (!name) return;
		for (auto& v : _values)
		{
			if (v.first == name) { v.second = value; return; }
		}
		_values.emplace_back(name, value);
	}

	void Profiler::LogSummary()const
	{
		if (_totalFrames == 0) return;

		const double n = static_cast<double>(_totalFrames);
		char message[320] = {};
		sprintf_s(message,
			"[Profiler] Summary (%llu frames) : %.1f FPS / frame %.3f ms / record scene %.3f ms / UI %.3f ms / GPU %.3f ms / draws %u, objects %u, visible %u (%s%s%s%s)",
			_totalFrames, 1000.0 / (_totalFrameMs / n), _totalFrameMs / n, _totalRecordMs / n, _totalUiMs / n,
			_totalGpuFrames ? _totalGpuMs / static_cast<double>(_totalGpuFrames) : 0.0,
			_lastCulling ? _lastVisibleDraws : _lastDrawCalls, _lastInstances, _lastCulling ? _lastVisible : _lastInstances,
			_lastIndirect ? "ExecuteIndirect" : "direct", _lastInstancing ? " + instancing" : "", _lastCulling ? " + GPU culling" : "",
			_lastOcclusion ? " + occlusion" : "");
		Core::Logger::Log(message);
	}

	void Profiler::SetInfo(const char* name, const std::string& text)
	{
		if (!name) return;
		for (auto& info : _infos)
		{
			if (info.first == name) { info.second = text; return; }
		}
		_infos.emplace_back(name, text);
	}

	void Profiler::updateVideoMemory()
	{
		if (!_adapter) return;

		// LOCAL = GPU 専用のメモリ (VRAM)。Budget は OS がこのアプリに使わせてよいと判断している量
		DXGI_QUERY_VIDEO_MEMORY_INFO info = {};
		if (SUCCEEDED(_adapter->QueryVideoMemoryInfo(0, DXGI_MEMORY_SEGMENT_GROUP_LOCAL, &info)))
		{
			_vramUsage  = info.CurrentUsage;
			_vramBudget = info.Budget;
		}
	}

	void Profiler::Draw(bool* indirectDraw, bool* instancing, bool* gpuCulling, bool* occlusion)
	{
		ImGui::SetNextWindowPos(ImVec2(10.0f, 10.0f), ImGuiCond_FirstUseEver);
		ImGui::SetNextWindowBgAlpha(0.85f);
		// 中身に合わせて大きさを自動で変える (以前は固定サイズで、項目が増えると下が切れていた)
		// グラフの幅は -1 (ウィンドウ幅いっぱい) なので、最小幅だけ決めておく
		ImGui::SetNextWindowSizeConstraints(ImVec2(380.0f, 0.0f), ImVec2(FLT_MAX, FLT_MAX));
		if (!ImGui::Begin("Profiler", nullptr, ImGuiWindowFlags_AlwaysAutoResize))
		{
			ImGui::End();
			return;
		}

		const FrameStats& s = _latest;

		// ---------- 概要 ----------
		ImGui::Text("%.1f FPS  (%.2f ms)", _avgFps, _avgFrameMs);
		ImGui::SameLine();
		ImGui::TextDisabled("min %.2f / max %.2f", _minFrameMs, _maxFrameMs);
		if (!_gpuName.empty()) ImGui::TextDisabled("%s", _gpuName.c_str());
		ImGui::TextDisabled("%ux%u  VSync %s  Tearing %s  Frames in flight %u",
			s.Width, s.Height, s.VSync ? "ON" : "OFF", s.Tearing ? "ON" : "OFF", s.FramesInFlight);
		for (const auto& info : _infos)
		{
			ImGui::TextDisabled("%s : %s", info.first.c_str(), info.second.c_str());
		}

		// ---------- グラフ ----------
		float maxMs = 1.0f;
		for (int i = 0; i < HISTORY_SIZE; ++i) maxMs = (std::max)(maxMs, (std::max)(_frameMsHistory[i], _gpuMsHistory[i]));
		maxMs *= 1.2f;

		char overlay[64];
		sprintf_s(overlay, "CPU frame %.2f ms", s.FrameMs);
		ImGui::PlotLines("##frame", _frameMsHistory, HISTORY_SIZE, _historyOffset, overlay, 0.0f, maxMs, ImVec2(-1.0f, 60.0f));
		sprintf_s(overlay, "GPU %.3f ms", s.GpuTotalMs);
		ImGui::PlotLines("##gpu", _gpuMsHistory, HISTORY_SIZE, _historyOffset, overlay, 0.0f, maxMs, ImVec2(-1.0f, 60.0f));

		const ImGuiTableFlags tableFlags = ImGuiTableFlags_RowBg | ImGuiTableFlags_BordersInnerV | ImGuiTableFlags_SizingStretchProp;

		// ---------- CPU ----------
		if (ImGui::CollapsingHeader("CPU (ms)", ImGuiTreeNodeFlags_DefaultOpen) && ImGui::BeginTable("cpu", 2, tableFlags))
		{
			row("Frame",          "%.3f", s.FrameMs);
			row("  Wait (latency / fence)", "%.3f", s.WaitMs);
			row("  Record scene", "%.3f", s.SceneRecordMs);
			row("  UI (ImGui)",   "%.3f", s.UiMs);
			row("  Present",      "%.3f", s.PresentMs);
			ImGui::EndTable();
		}

		// ---------- GPU ----------
		if (ImGui::CollapsingHeader("GPU (ms)", ImGuiTreeNodeFlags_DefaultOpen) && ImGui::BeginTable("gpu", 2, tableFlags))
		{
			if (s.GpuValid)
			{
				row("Total",       "%.3f", s.GpuTotalMs);
				row("  Scene",     "%.3f", s.GpuSceneMs);
				row("  UI (ImGui)","%.3f", s.GpuUiMs);
			}
			else
			{
				rowText("Total", "(measuring...)");
			}
			ImGui::EndTable();
		}

		// ---------- 描画 ----------
		const bool drawOpen = ImGui::CollapsingHeader("Draw", ImGuiTreeNodeFlags_DefaultOpen);
		if (drawOpen && indirectDraw)
		{
			// ExecuteIndirect の ON / OFF (その場で切り替えて CPU 時間を比べられる)
			ImGui::Checkbox("ExecuteIndirect", indirectDraw);
			ImGui::SameLine();
			if (s.IndirectDraw) ImGui::TextDisabled(s.IndirectArgsReused ? "(args reused)" : "(args written)");
			else                ImGui::TextDisabled("(direct draw)");
		}
		if (drawOpen && instancing)
		{
			// インスタンシングの ON / OFF (同じメッシュをまとめるかどうか。描画数と GPU 時間を比べられる)
			ImGui::Checkbox("Instancing", instancing);
		}
		if (drawOpen && gpuCulling)
		{
			// GPU カリングの ON / OFF (ExecuteIndirect の時だけ効く)
			// (SameLine は後ろに表示する物がある時だけ。無いと次の表が同じ行に並んでしまう)
			ImGui::Checkbox("GPU culling", gpuCulling);
			if (s.GpuCulling)      { ImGui::SameLine(); ImGui::TextDisabled("(frustum)"); }
			else if (*gpuCulling)  { ImGui::SameLine(); ImGui::TextDisabled("(needs ExecuteIndirect)"); }
		}
		if (drawOpen && occlusion)
		{
			// オクルージョンカリングの ON / OFF (GPU カリングの時だけ効く。前のフレームの深度 (Hi-Z) を使う)
			ImGui::Checkbox("Occlusion (Hi-Z)", occlusion);
			if (s.OcclusionCulling)                 { ImGui::SameLine(); ImGui::TextDisabled("(previous frame depth)"); }
			else if (*occlusion && !s.GpuCulling)   { ImGui::SameLine(); ImGui::TextDisabled("(needs GPU culling)"); }
		}
		if (drawOpen && ImGui::BeginTable("draw", 2, tableFlags))
		{
			row("Draw calls", "%.0f", static_cast<double>(s.DrawCalls));
			row("Instances",  "%.0f", static_cast<double>(s.Instances));
			if (s.GpuCulling && s.VisibleValid)
			{
				// GPU カリング後に実際に描いた数 (詰め直した後の描画数 / 映ったオブジェクトの数)
				row("Draw calls (GPU)", "%.0f", static_cast<double>(s.VisibleDraws));
				row("Visible (GPU)",    "%.0f", static_cast<double>(s.VisibleInstances));
			}
			row("Triangles",  "%.0f", static_cast<double>(s.Triangles));
			row("Objects",    "%.0f", static_cast<double>(s.Objects));
			ImGui::EndTable();
		}

		// ---------- メモリ ----------
		if (ImGui::CollapsingHeader("Memory", ImGuiTreeNodeFlags_DefaultOpen) && ImGui::BeginTable("mem", 2, tableFlags))
		{
			char a[32], b[32], text[80];
			formatBytes(a, sizeof(a), _vramUsage);
			formatBytes(b, sizeof(b), _vramBudget);
			sprintf_s(text, "%s / %s", a, b);
			rowText("VRAM (usage / budget)", _adapter ? text : "(unavailable)");

			formatBytes(a, sizeof(a), s.UploadBytes);
			rowText("Upload (this frame)", a);
			formatBytes(a, sizeof(a), s.UploadRingUsed);
			formatBytes(b, sizeof(b), s.UploadRingSize);
			sprintf_s(text, "%s / %s", a, b);
			rowText("Upload ring", text);
			ImGui::EndTable();

			if (_vramBudget > 0)
			{
				ImGui::ProgressBar(static_cast<float>(static_cast<double>(_vramUsage) / static_cast<double>(_vramBudget)), ImVec2(-1.0f, 0.0f), "VRAM");
			}
		}

		// ---------- 外から渡された値 ----------
		if (!_values.empty() && ImGui::CollapsingHeader("Values", ImGuiTreeNodeFlags_DefaultOpen) && ImGui::BeginTable("values", 2, tableFlags))
		{
			for (const auto& v : _values)
			{
				row(v.first.c_str(), "%.3f", v.second);
			}
			ImGui::EndTable();
		}

		ImGui::Separator();
		ImGui::TextDisabled("F1 : show / hide");
		ImGui::End();
	}
}

/************************************
* End of file
************************************/
