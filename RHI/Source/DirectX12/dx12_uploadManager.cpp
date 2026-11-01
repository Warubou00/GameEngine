
/***********************************************************************************
* @file    dx12_uploadManager.cpp
* @brief   CPU → GPU のデータ転送 (アップロード) の管理
* @details
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


/******************************************************************
* Include Headers
******************************************************************/
#include "dx12_uploadManager.h"

#include "Object/dx12_commandQueue.h"
#include "Object/dx12_commandAllocator.h"
#include "Object/dx12_graphicsCommandList.h"
#include "Object/dx12_fence.h"
#include "Object/dx12_resourceBuffer.h"

#include "../../Core/Source/Log/logger.h"

namespace RHI
{
	namespace DirectX12
	{
		namespace
		{
			// 同時に使うアロケーターの上限。これを超えたら一番古い送信の完了を待つ
			constexpr size_t MAX_ALLOCATORS = 8;

			UINT64 alignUp(UINT64 value, UINT64 alignment)
			{
				return (value + alignment - 1) / alignment * alignment;
			}
		}

		UploadManager::UploadManager() = default;
		UploadManager::~UploadManager()
		{
			Release();
		}

		HRESULT UploadManager::Initialize(ID3D12Device* device, UINT64 ringSize)
		{
			if (!device || ringSize == 0 || _queue)
			{
				return E_INVALIDARG;
			}
			_device = device;

			// COPY キュー : 描画キューとは別に動くので、描画中でも転送できる
			_queue = std::make_unique<CommandQueue>();
			RHI_RETURN_IF_FAILED(_queue->Create(device, D3D12_COMMAND_LIST_TYPE_COPY));

			_fence = std::make_unique<Fence>();
			RHI_RETURN_IF_FAILED(_fence->Create(device));

			// 最初のアロケーターとコマンドリスト
			AllocatorEntry entry;
			entry.Allocator = std::make_unique<CommandAllocator>();
			RHI_RETURN_IF_FAILED(entry.Allocator->Create(device, D3D12_COMMAND_LIST_TYPE_COPY));
			_allocators.push_back(std::move(entry));
			_currentAllocator = 0;

			_commandList = std::make_unique<GraphicsCommandList>();
			RHI_RETURN_IF_FAILED(_commandList->Create(device, _allocators[0].Allocator->Get(), D3D12_COMMAND_LIST_TYPE_COPY));
			// 生成直後は記録中なので閉じておく (使う時に beginRecording でリセットする)
			_commandList->Close();
			_isRecording = false;

			// リングバッファ : UPLOAD ヒープに作って、生存中はずっと Map しておく (毎回の Map/Unmap を省く)
			_ring = std::make_unique<ResourceBuffer>();
			RHI_RETURN_IF_FAILED(_ring->Create(device, ringSize, D3D12_HEAP_TYPE_UPLOAD));
			RHI_RETURN_IF_FAILED(_ring->Map());
			_ringCpu  = static_cast<uint8_t*>(_ring->GetMappedData());
			_ringSize = ringSize;
			_head = _tail = 0;
			return S_OK;
		}

		bool UploadManager::Allocate(UINT64 size, UINT64 alignment, Allocation& out)
		{
			if (!_ring || size == 0)
			{
				return false;
			}
			if (alignment == 0) alignment = 1;

			// リングに入りきらない大きさなら一時バッファにする
			if (size + alignment > _ringSize)
			{
				return allocateTemporary(size, out);
			}

			// 空きが無い時は「未送信分を送信 → 一番古い転送の完了を待つ」を繰り返す
			for (;;)
			{
				retireCompleted();

				UINT64 offset = 0;
				if (tryAllocateFromRing(size, alignment, offset))
				{
					out.Cpu      = _ringCpu + offset;
					out.Resource = _ring->Get();
					out.Offset   = offset;
					_currentHasAllocations = true;
					_pendingBytes += size;
					return true;
				}

				if (HasPendingWork())
				{
					Flush();
				}
				if (_submissions.empty())
				{
					// 待つものが無いのに確保できない (断片化) → 一時バッファで逃げる
					return allocateTemporary(size, out);
				}
				_fence->WaitForValue(_submissions.front().FenceValue);
			}
		}

		ID3D12GraphicsCommandList* UploadManager::GetCommandList()
		{
			if (!beginRecording())
			{
				return nullptr;
			}
			return _commandList->GetCmdList();
		}

		UINT64 UploadManager::Flush()
		{
			if (!HasPendingWork())
			{
				// このフレームは何も送っていない (プロファイラーに前回の量が残らないように 0 にする)
				_lastFlushBytes = 0;
				return _lastSubmittedFence;
			}

			// 記録していればコマンドリストを送信する
			if (_isRecording)
			{
				HRESULT hr = _commandList->Close();
				_isRecording = false;
				if (FAILED(hr))
				{
					Core::Logger::Log("[UploadManager] Failed close copy command list.");
				}
				else
				{
					ID3D12CommandList* lists[] = { _commandList->GetCmdList() };
					_queue->Get()->ExecuteCommandLists(1, lists);
				}
			}

			// 完了の目印。コマンドが無くても、確保した領域を回収するためにシグナルはする
			const UINT64 fenceValue = _fence->Signal(_queue->Get());
			_allocators[_currentAllocator].FenceValue = fenceValue;

			Submission submission;
			submission.FenceValue  = fenceValue;
			submission.HeadAfter   = _head;
			submission.TempBuffers = std::move(_currentTempBuffers);
			_submissions.push_back(std::move(submission));

			_currentTempBuffers.clear();
			_currentHasAllocations = false;
			_lastSubmittedFence    = fenceValue;
			_lastFlushBytes        = _pendingBytes;
			_pendingBytes          = 0;
			return fenceValue;
		}

		void UploadManager::WaitIdle()
		{
			if (!_fence || !_queue)
			{
				return;
			}
			const UINT64 value = Flush();
			if (value != 0)
			{
				_fence->WaitForValue(value);
			}
			retireCompleted();
		}

		UINT64 UploadManager::GetRingUsedBytes()const
		{
			if (_submissions.empty() && !_currentHasAllocations) return 0;
			// 折り返していなければ [_tail, _head)、折り返していれば [_tail, 末尾) + [0, _head)
			return (_head >= _tail) ? (_head - _tail) : (_ringSize - _tail + _head);
		}

		ID3D12Fence* UploadManager::GetFence()const
		{
			return _fence ? _fence->Get() : nullptr;
		}

		void UploadManager::Release()
		{
			// GPU が転送中のリソースを消さないよう、全て終わるのを待ってから破棄する
			WaitIdle();

			if (_commandList) _commandList->Close();
			_submissions.clear();
			_currentTempBuffers.clear();
			_commandList.reset();
			_allocators.clear();
			if (_ring) _ring->Unmap();
			_ring.reset();
			_ringCpu = nullptr;
			_fence.reset();
			_queue.reset();
			_device = nullptr;
		}

		void UploadManager::retireCompleted()
		{
			// 完了した送信の分だけ、リングの使用中領域の先頭(_tail)を進める
			while (!_submissions.empty() && _fence->IsCompleted(_submissions.front().FenceValue))
			{
				_tail = _submissions.front().HeadAfter;
				_submissions.pop_front();   // 一時バッファもここで破棄される
			}
		}

		bool UploadManager::tryAllocateFromRing(UINT64 size, UINT64 alignment, UINT64& outOffset)
		{
			// 使用中の領域が無ければ先頭から使い直す (断片化をリセット)
			const bool isEmpty = _submissions.empty() && !_currentHasAllocations;
			if (isEmpty)
			{
				_head = _tail = 0;
			}

			if (isEmpty || _head >= _tail)
			{
				// [_head, 末尾) に入るか
				const UINT64 start = alignUp(_head, alignment);
				if (start + size <= _ringSize)
				{
					outOffset = start;
					_head     = start + size;
					return true;
				}

				// 先頭に戻って [0, _tail) に入るか
				// (_head == _tail は「空」と区別できなくなるので、ぴったりは使わない)
				if (!isEmpty && size < _tail)
				{
					outOffset = 0;
					_head     = size;
					return true;
				}
				return false;
			}

			// 折り返し中 (_head < _tail) : [_head, _tail) に入るか
			const UINT64 start = alignUp(_head, alignment);
			if (start + size < _tail)
			{
				outOffset = start;
				_head     = start + size;
				return true;
			}
			return false;
		}

		bool UploadManager::allocateTemporary(UINT64 size, Allocation& out)
		{
			auto buffer = std::make_unique<ResourceBuffer>();
			if (FAILED(buffer->Create(_device, size, D3D12_HEAP_TYPE_UPLOAD)) || FAILED(buffer->Map()))
			{
				Core::Logger::Log("[UploadManager] Failed create temporary upload buffer.");
				return false;
			}

			out.Cpu      = static_cast<uint8_t*>(buffer->GetMappedData());
			out.Resource = buffer->Get();
			out.Offset   = 0;

			// 送信して GPU が使い終わるまで保持する
			_currentTempBuffers.push_back(std::move(buffer));
			_currentHasAllocations = true;
			_pendingBytes += size;
			return true;
		}

		bool UploadManager::beginRecording()
		{
			if (!_commandList)
			{
				return false;
			}
			if (_isRecording)
			{
				return true;
			}

			// GPU が使い終わったアロケーターを探す。無ければ作る (上限を超えたら一番古いものを待つ)
			size_t index = _allocators.size();
			for (size_t i = 0; i < _allocators.size(); ++i)
			{
				if (_fence->IsCompleted(_allocators[i].FenceValue))
				{
					index = i;
					break;
				}
			}

			if (index == _allocators.size())
			{
				if (_allocators.size() < MAX_ALLOCATORS)
				{
					AllocatorEntry entry;
					entry.Allocator = std::make_unique<CommandAllocator>();
					if (FAILED(entry.Allocator->Create(_device, D3D12_COMMAND_LIST_TYPE_COPY)))
					{
						return false;
					}
					_allocators.push_back(std::move(entry));
				}
				else
				{
					// 一番古い (フェンス値が最小の) ものを待つ
					index = 0;
					for (size_t i = 1; i < _allocators.size(); ++i)
					{
						if (_allocators[i].FenceValue < _allocators[index].FenceValue) index = i;
					}
					_fence->WaitForValue(_allocators[index].FenceValue);
				}
			}

			_currentAllocator = index;
			if (FAILED(_allocators[index].Allocator->Reset()) ||
				FAILED(_commandList->Reset(_allocators[index].Allocator->Get(), nullptr)))
			{
				Core::Logger::Log("[UploadManager] Failed reset copy command list.");
				return false;
			}
			_isRecording = true;
			return true;
		}
	}
}

/************************************
* End of file
************************************/
