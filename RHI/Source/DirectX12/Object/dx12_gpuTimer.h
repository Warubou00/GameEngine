
/***********************************************************************************
* @file    dx12_gpuTimer.h
* @brief   GPU の処理時間の計測 (タイムスタンプクエリ)
* @details コマンドリストの中に「この時点の GPU の時刻」を書き込ませ、後で差を取って時間にする
*
*            [フレーム N の記録]  Begin ─ (シーン) ─ Mark(1) ─ (ImGui) ─ End → Resolve (読み戻し用バッファへ)
*            [フレーム N+FRAME_COUNT の開始時] フェンスでフレーム N の完了を確認済みなので結果を読む
*
*          GPU の結果は FRAME_COUNT フレーム遅れて読める (CPU は GPU の完了を待たない)
*
* @author  Keigo Onari
* @date    2026/11/01
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/

#pragma once
#ifndef DX12_GPU_TIMER_H_
#define DX12_GPU_TIMER_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "../dx12_header.h"

namespace RHI
{
	namespace DirectX12
	{
		class GpuTimer
		{
		public:
			// 1 フレームで書けるタイムスタンプの数 (開始 / 区切り / 終了)
			// (以前は 4。GPU パーティクルの 更新 / ソート / 描画 の区切りを足した : CommandContext の TIMESTAMP_PARTICLE_*)
			// (AN4 : 8 → 10。GPU スキニングの始め / 終わりを足した : TIMESTAMP_SKIN_BEGIN)
			// (v1.10 P2 : 10 → 10 + PASS_TIMESTAMPS。描画パス (BeginPass / EndPass) の始め / 終わりを足した。
			//   パスの分は 10 番から。読み戻しは別の範囲 (ResolveRange / ReadTicks) で行う)
			static constexpr UINT FIXED_TIMESTAMPS     = 10;
			static constexpr UINT PASS_TIMESTAMPS      = 256;   // 1 フレームの描画パス 128 個分
			static constexpr UINT TIMESTAMPS_PER_FRAME = FIXED_TIMESTAMPS + PASS_TIMESTAMPS;

			GpuTimer();
			~GpuTimer();

			GpuTimer(const GpuTimer&) = delete;
			GpuTimer& operator=(const GpuTimer&) = delete;

			/***********************************************************************************
			 * @brief 初期化
			 * @param[in] ID3D12CommandQueue* queue 計測するキュー (時刻の単位 = このキューの周波数)
			 * @param[in] UINT frameCount フレームの数 (CommandContext::FRAME_COUNT)
			 ************************************************************************************/
			HRESULT Initialize(ID3D12Device* device, ID3D12CommandQueue* queue, UINT frameCount);

			// index 番目のタイムスタンプを書き込む (0 = 開始)
			void Timestamp(ID3D12GraphicsCommandList* cmdList, UINT frameIndex, UINT index);

			// このフレームで書いたタイムスタンプを読み戻し用バッファへ移す (コマンドリストを閉じる前に呼ぶ)
			void Resolve(ID3D12GraphicsCommandList* cmdList, UINT frameIndex, UINT count);

			/***********************************************************************************
			 * @brief 結果を読む (そのフレームの GPU 処理が終わっていること = FrameContext の完了待ちの後)
			 * @param[out] double* outMs 各区間の時間 (ミリ秒)。outMs[i] = タイムスタンプ i → i+1
			 * @return bool 有効な結果があったか (まだ一度も計測していないフレームなら false)
			 ************************************************************************************/
			bool ReadIntervals(UINT frameIndex, UINT count, double* outMs) { return ReadIntervalsFrom(frameIndex, 0, count, outMs); }
			// first 番目のタイムスタンプからの区間 (outMs[i] = タイムスタンプ first+i → first+i+1)。GPU パーティクルの区間に使う
			bool ReadIntervalsFrom(UINT frameIndex, UINT first, UINT count, double* outMs);

			// v1.10 P2 : first 番目から count 個を読み戻し用バッファへ移す (描画パスの分。count = 0 なら「このフレームは無し」と覚えるだけ)
			void ResolveRange(ID3D12GraphicsCommandList* cmdList, UINT frameIndex, UINT first, UINT count);
			// v1.10 P2 : ResolveRange で移したタイムスタンプをそのまま読む (範囲の外なら false)
			bool ReadTicks(UINT frameIndex, UINT first, UINT count, UINT64* outTicks);
			UINT64 GetFrequency()const { return _frequency; }

			void Release();

		private:
			Microsoft::WRL::ComPtr<ID3D12QueryHeap> _queryHeap;
			Microsoft::WRL::ComPtr<ID3D12Resource>  _readback;          // READBACK ヒープ (CPU から読める)
			UINT64                                  _frequency  = 0;    // 1 秒あたりのカウント数
			UINT                                    _frameCount = 0;
			UINT                                    _resolvedCount[8] = {};   // フレームごとに Resolve した数 (0 = 未計測)
			UINT                                    _rangeFirst[8]    = {};   // v1.10 P2 : フレームごとに ResolveRange した範囲
			UINT                                    _rangeCount[8]    = {};
		};
	}
}

#endif // DX12_GPU_TIMER_H_
