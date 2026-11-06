
/***********************************************************************************
* @file    dx12_uploadManager.h
* @brief   CPU → GPU のデータ転送 (アップロード) の管理
* @details COPY キュー + 永続マップしたリングバッファでアップロードをまとめて行う
*
*          以前 (Phase 1 まで) :
*            アップロードのたびに UPLOAD バッファを新規作成 → コピー命令 → 送信 → GPU の完了を CPU で待つ
*            → テクスチャ 1 枚・頂点 1 回ごとに「作成コスト + 完全待ち」が発生していた
*
*          今回 :
*            1. Allocate        : リングバッファから領域を切り出す (作成コストなし、Map 済み)
*            2. GetCommandList  : COPY 用コマンドリストにコピー命令を積む (何回でも積める)
*            3. Flush           : まとめて 1 回だけ COPY キューへ送信 (CPU は待たない)
*            4. 描画キューは GPU 上でアップロード完了を待ってから描画する (ID3D12CommandQueue::Wait)
*            → CPU も描画キューも止まらない。リングの領域は GPU が使い終わったら自動で再利用する
*
*          リングバッファの状態
*
*            [ 空き | 使用中(GPU 転送待ち) | 書き込み中(未送信) | 空き ]
*                   ↑ _tail                                  ↑ _head
*            末尾に入りきらない時は先頭に戻って使う (_head < _tail になる)
*
* @author  Keigo Onari
* @date    2026/11/01
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    このクラスはスレッドセーフではない (メインスレッドから使う)
************************************************************************************/

#pragma once
#ifndef DX12_UPLOAD_MANAGER_H_
#define DX12_UPLOAD_MANAGER_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "dx12_header.h"

#include <deque>
#include <functional>
#include <memory>
#include <vector>

/******************************************************************
* Structure Declarations
******************************************************************/

namespace RHI
{
	namespace DirectX12
	{
		class CommandQueue;
		class CommandAllocator;
		class GraphicsCommandList;
		class Fence;
		class ResourceBuffer;

		class UploadManager
		{
		public:
			// リングバッファの既定サイズ。これより大きいデータは一時バッファを個別に作る
			static constexpr UINT64 DEFAULT_RING_SIZE = 64ull * 1024ull * 1024ull;

			// アップロード先として切り出した領域
			struct Allocation
			{
				uint8_t*        Cpu      = nullptr;   // CPU から書き込むアドレス (Map 済み)
				ID3D12Resource* Resource = nullptr;   // コピー命令のコピー元に指定するリソース
				UINT64          Offset   = 0;         // Resource 内の位置 (コピー命令に渡す)
			};

		public:
			UploadManager();
			~UploadManager();

			UploadManager(const UploadManager&) = delete;
			UploadManager& operator=(const UploadManager&) = delete;

			/***********************************************************************************
			 * @brief 初期化 (COPY キュー・フェンス・リングバッファの作成)
			 ************************************************************************************/
			HRESULT Initialize(ID3D12Device* device, UINT64 ringSize = DEFAULT_RING_SIZE);

			/***********************************************************************************
			 * @brief アップロード用の領域を確保する
			 * @details リングに空きが無ければ、未送信分を送信して古い転送の完了を待ってから確保する。
			 *          リングより大きいデータは一時バッファを作る (転送完了後に自動で破棄)
			 *
			 * @param[in] UINT64 size 必要なサイズ(byte)
			 * @param[in] UINT64 alignment 先頭のアライメント
			 *                   (テクスチャは D3D12_TEXTURE_DATA_PLACEMENT_ALIGNMENT = 512)
			 * @param[out] Allocation& out 確保した領域
			 * @return bool 成功の有無
			 ************************************************************************************/
			bool Allocate(UINT64 size, UINT64 alignment, Allocation& out);

			/***********************************************************************************
			 * @brief コピー命令を積むコマンドリスト (COPY 型)
			 * @details 記録中でなければ記録を開始してから返す。失敗時は nullptr
			 ************************************************************************************/
			ID3D12GraphicsCommandList* GetCommandList();

			/***********************************************************************************
			 * @brief 積んだコピー命令を COPY キューへ送信する (CPU は待たない)
			 *
			 * @return UINT64 最後に送信した分のフェンス値 (一度も送信していなければ 0)
			 *                描画キューはこの値を ID3D12CommandQueue::Wait で待つ
			 ************************************************************************************/
			UINT64 Flush();

			/***********************************************************************************
			 * @brief 送信して、全ての転送が終わるまで CPU で待つ (終了処理・計測用)
			 ************************************************************************************/
			void WaitIdle();

			ID3D12Fence* GetFence()const;

			/***********************************************************************************
			 * @brief コピー命令を COPY キューへ送る直前に呼ぶ関数 (AN4)
			 * @details 描画キューが同じバッファに明示的なバリアを張っている間は、COPY キューで書けない。
			 *          描画側がその完了を CPU で待つために使う
			 ************************************************************************************/
			void SetBeforeSubmit(std::function<void()> callback) { _beforeSubmit = std::move(callback); }

			// 未送信のコピー命令・確保があるか
			bool HasPendingWork()const { return _isRecording || _currentHasAllocations; }

			// ---------- プロファイラー用 ----------
			// 直前の Flush で送った量 (byte)
			UINT64 GetLastFlushBytes()const { return _lastFlushBytes; }
			// リングバッファのうち、まだ GPU の転送が終わっていない (再利用できない) 量
			UINT64 GetRingUsedBytes()const;
			UINT64 GetRingSize()const { return _ringSize; }

			void Release();

		private:
			// コマンドアロケーターとそれを使った送信のフェンス値 (GPU が使い終わるまで再利用できない)
			struct AllocatorEntry
			{
				std::unique_ptr<CommandAllocator> Allocator;
				UINT64                            FenceValue = 0;
			};

			// 送信 1 回分
			struct Submission
			{
				UINT64                                       FenceValue = 0;
				UINT64                                       HeadAfter  = 0;   // 送信時点の _head (完了したら _tail をここまで進める)
				std::vector<std::unique_ptr<ResourceBuffer>> TempBuffers;      // リングに入らなかった一時バッファ
			};

			ID3D12Device*                                _device = nullptr;    // 所有しない
			std::unique_ptr<CommandQueue>                _queue;               // COPY キュー
			std::unique_ptr<Fence>                       _fence;
			std::unique_ptr<GraphicsCommandList>         _commandList;         // COPY 型
			std::vector<AllocatorEntry>                  _allocators;
			size_t                                       _currentAllocator = 0;
			bool                                         _isRecording      = false;
			std::function<void()>                        _beforeSubmit;        // 送信の直前に呼ぶ (AN4)

			// リングバッファ
			std::unique_ptr<ResourceBuffer>              _ring;
			uint8_t*                                     _ringCpu  = nullptr;
			UINT64                                       _ringSize = 0;
			UINT64                                       _head     = 0;
			UINT64                                       _tail     = 0;
			bool                                         _currentHasAllocations = false;

			std::deque<Submission>                       _submissions;         // 送信済みで完了待ちのもの (古い順)
			std::vector<std::unique_ptr<ResourceBuffer>> _currentTempBuffers;  // 未送信の一時バッファ
			UINT64                                       _lastSubmittedFence = 0;
			UINT64                                       _pendingBytes       = 0;   // 未送信の確保量 (プロファイラー用)
			UINT64                                       _lastFlushBytes     = 0;

			void retireCompleted();
			bool tryAllocateFromRing(UINT64 size, UINT64 alignment, UINT64& outOffset);
			bool allocateTemporary(UINT64 size, Allocation& out);
			bool beginRecording();
		};
	}
}

#endif // DX12_UPLOAD_MANAGER_H_
