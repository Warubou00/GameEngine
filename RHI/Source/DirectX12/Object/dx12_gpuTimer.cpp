
/***********************************************************************************
* @file    dx12_gpuTimer.cpp
* @brief   GPU の処理時間の計測 (タイムスタンプクエリ)
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
#include "dx12_gpuTimer.h"

namespace RHI
{
	namespace DirectX12
	{
		GpuTimer::GpuTimer() = default;
		GpuTimer::~GpuTimer()
		{
			Release();
		}

		HRESULT GpuTimer::Initialize(ID3D12Device* device, ID3D12CommandQueue* queue, UINT frameCount)
		{
			if (!device || !queue || frameCount == 0 || frameCount > _countof(_resolvedCount) || _queryHeap)
			{
				return E_INVALIDARG;
			}
			_frameCount = frameCount;

			// タイムスタンプの単位 (Hz)。キューごとに違うことがあるので、計測するキューから取る
			RHI_RETURN_IF_FAILED(queue->GetTimestampFrequency(&_frequency));

			// クエリ (タイムスタンプの書き込み先) : フレームの数 × 1 フレーム分
			D3D12_QUERY_HEAP_DESC heapDesc = {};
			heapDesc.Type  = D3D12_QUERY_HEAP_TYPE_TIMESTAMP;
			heapDesc.Count = TIMESTAMPS_PER_FRAME * frameCount;
			RHI_RETURN_IF_FAILED(device->CreateQueryHeap(&heapDesc, IID_PPV_ARGS(&_queryHeap)));

			// 読み戻し用バッファ : READBACK ヒープは COPY_DEST 状態のまま使う
			D3D12_HEAP_PROPERTIES heapProps = {};
			heapProps.Type = D3D12_HEAP_TYPE_READBACK;

			D3D12_RESOURCE_DESC desc = {};
			desc.Dimension        = D3D12_RESOURCE_DIMENSION_BUFFER;
			desc.Width            = sizeof(UINT64) * heapDesc.Count;
			desc.Height           = 1;
			desc.DepthOrArraySize = 1;
			desc.MipLevels        = 1;
			desc.Format           = DXGI_FORMAT_UNKNOWN;
			desc.SampleDesc.Count = 1;
			desc.Layout           = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;

			RHI_RETURN_IF_FAILED(device->CreateCommittedResource(
				&heapProps, D3D12_HEAP_FLAG_NONE, &desc, D3D12_RESOURCE_STATE_COPY_DEST, nullptr, IID_PPV_ARGS(&_readback)));

			for (auto& count : _resolvedCount) count = 0;
			return S_OK;
		}

		void GpuTimer::Timestamp(ID3D12GraphicsCommandList* cmdList, UINT frameIndex, UINT index)
		{
			if (!_queryHeap || !cmdList || frameIndex >= _frameCount || index >= TIMESTAMPS_PER_FRAME) return;

			// TIMESTAMP は Begin が無く EndQuery だけで書き込む
			cmdList->EndQuery(_queryHeap.Get(), D3D12_QUERY_TYPE_TIMESTAMP, frameIndex * TIMESTAMPS_PER_FRAME + index);
		}

		void GpuTimer::Resolve(ID3D12GraphicsCommandList* cmdList, UINT frameIndex, UINT count)
		{
			if (!_queryHeap || !cmdList || frameIndex >= _frameCount || count == 0 || count > TIMESTAMPS_PER_FRAME) return;

			const UINT first = frameIndex * TIMESTAMPS_PER_FRAME;
			cmdList->ResolveQueryData(_queryHeap.Get(), D3D12_QUERY_TYPE_TIMESTAMP, first, count,
				_readback.Get(), sizeof(UINT64) * first);
			_resolvedCount[frameIndex] = count;
		}

		bool GpuTimer::ReadIntervalsFrom(UINT frameIndex, UINT firstIndex, UINT count, double* outMs)
		{
			// 以前は先頭 (0 番) からの区間だけを読む ReadIntervals だった (GPU パーティクルの区間を足した)
			if (!_readback || !outMs || frameIndex >= _frameCount || _frequency == 0) return false;

			const UINT resolved = _resolvedCount[frameIndex];
			if (resolved < 2 || firstIndex + count + 1 > resolved) return false;

			// 読む範囲だけを指定して Map する (CPU が読む範囲をドライバーに伝える)
			const UINT first = frameIndex * TIMESTAMPS_PER_FRAME;
			D3D12_RANGE readRange = { sizeof(UINT64) * first, sizeof(UINT64) * (first + resolved) };
			void* mapped = nullptr;
			if (FAILED(_readback->Map(0, &readRange, &mapped)) || !mapped) return false;

			const UINT64* ticks = static_cast<const UINT64*>(mapped) + first + firstIndex;
			for (UINT i = 0; i < count; ++i)
			{
				const UINT64 begin = ticks[i];
				const UINT64 end   = ticks[i + 1];
				outMs[i] = (end > begin) ? static_cast<double>(end - begin) * 1000.0 / static_cast<double>(_frequency) : 0.0;
			}

			// CPU からは書き込んでいないので、書き込み範囲は空
			D3D12_RANGE writeRange = { 0, 0 };
			_readback->Unmap(0, &writeRange);
			return true;
		}

		void GpuTimer::Release()
		{
			_readback.Reset();
			_queryHeap.Reset();
			_frequency  = 0;
			_frameCount = 0;
		}
	}
}

/************************************
* End of file
************************************/
