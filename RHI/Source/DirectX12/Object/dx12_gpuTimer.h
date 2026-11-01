
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
			static constexpr UINT TIMESTAMPS_PER_FRAME = 4;

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
			bool ReadIntervals(UINT frameIndex, UINT count, double* outMs);

			void Release();

		private:
			Microsoft::WRL::ComPtr<ID3D12QueryHeap> _queryHeap;
			Microsoft::WRL::ComPtr<ID3D12Resource>  _readback;          // READBACK ヒープ (CPU から読める)
			UINT64                                  _frequency  = 0;    // 1 秒あたりのカウント数
			UINT                                    _frameCount = 0;
			UINT                                    _resolvedCount[8] = {};   // フレームごとに Resolve した数 (0 = 未計測)
		};
	}
}

#endif // DX12_GPU_TIMER_H_
