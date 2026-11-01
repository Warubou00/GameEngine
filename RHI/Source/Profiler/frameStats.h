
/***********************************************************************************
* @file    frameStats.h
* @brief   1 フレーム分の計測値 (プロファイラーに表示する)
* @details CommandContext (待ち時間 / Present / GPU / 描画数) と
*          RenderHardwareInterface (フレーム時間 / 記録時間) が書き込み、Profiler が表示する
*
* @author  Keigo Onari
* @date    2026/11/01
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/

#pragma once
#ifndef RHI_FRAME_STATS_H_
#define RHI_FRAME_STATS_H_

#include <cstdint>

namespace RHI
{
	struct FrameStats
	{
		// ---------- CPU (ミリ秒) ----------
		double   FrameMs       = 0.0;   // 前のフレームの開始からこのフレームの開始まで (= 1 / FPS)
		double   WaitMs        = 0.0;   // RenderStart での待ち (表示待ち + GPU が FrameContext を使い終わるまで)
		double   SceneRecordMs = 0.0;   // シーンの描画命令を積むのにかかった時間
		double   UiMs          = 0.0;   // ImGui (プロファイラー) の構築 + 描画命令
		double   PresentMs     = 0.0;   // Present の呼び出しにかかった時間

		// ---------- GPU (ミリ秒) : FRAME_COUNT フレーム前の値 ----------
		bool     GpuValid      = false;
		double   GpuSceneMs    = 0.0;   // シーンの描画 (クリア含む)
		double   GpuUiMs       = 0.0;   // ImGui の描画
		double   GpuTotalMs    = 0.0;

		// ---------- 数 ----------
		uint32_t DrawCalls     = 0;     // DrawIndexedInstanced の回数 (シーン)
		uint32_t Instances     = 0;     // 描いたオブジェクトの数 (インスタンシングでまとめる前の数)
		bool     Instancing    = false; // 同じメッシュを 1 回の描画にまとめたか
		bool     GpuCulling    = false; // GPU カリングをしたか
		uint32_t VisibleInstances = 0;  // GPU カリングで映った数 (FRAME_COUNT フレーム前の値)
		bool     VisibleValid  = false;
		uint64_t Triangles     = 0;
		uint32_t Objects       = 0;     // 登録されている描画オブジェクトの数
		bool     IndirectDraw  = false; // ExecuteIndirect で描いたか
		bool     IndirectArgsReused = false; // 引数バッファを書き直さずに使えたか (描画リストが前回と同じ)

		// ---------- 転送 ----------
		uint64_t UploadBytes    = 0;    // このフレームで COPY キューへ送った量
		uint64_t UploadRingUsed = 0;    // アップロード用リングバッファの使用中の量
		uint64_t UploadRingSize = 0;

		// ---------- 画面 ----------
		uint32_t Width         = 0;
		uint32_t Height        = 0;
		bool     VSync         = false;
		bool     Tearing       = false;
		uint32_t FramesInFlight = 0;
	};
}

#endif // RHI_FRAME_STATS_H_
