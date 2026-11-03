
/***********************************************************************************
* @file    CommandContext_Particles.cpp
* @brief   RHI CommandContext : GPU パーティクル
* @details 発生 / 更新 / 生死の管理 / ソート / 描画の引数を全部 GPU で行う。CPU はエミッターと「出す数」を送るだけ
*          (パーティクル 1 個 1 個のデータは GPU の中にしか無く、CPU は読まない)
*
*          描画パスの命令 (C# の RenderPipeline が積む)
*            UpdateParticles : エミッターを VRAM へ写す → Begin → Emit → Simulate (DispatchIndirect) → Finish → (ソート)
*            DrawParticles   : ExecuteIndirect (DrawInstanced 1 回 = 頂点 6 個 × 生きている数)
*          シェーダー : particleSim.shader / particleDraw.shader
*          参考 : CAPCOM「MH:W | GPU Particle」(GCC2018)。設計は ClaudLog/Engine_GpuParticles_Design_2026-11-03.txt
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
#include "CommandContext.h"

#include "Device.h"
#include "Pipeline.h"
#include "Buffer.h"

#include "../Object/dx12_device.h"
#include "../Object/dx12_graphicsCommandList.h"
#include "../Object/dx12_resourceBuffer.h"
#include "../Object/dx12_gpuTimer.h"
#include "../dx12_descriptorHeapManager.h"

#include <algorithm>
#include <cstring>

#include "../../Core/Source/Log/logger.h"

namespace RHI
{
	namespace ObjectInterface
	{
		namespace
		{
			D3D12_RESOURCE_BARRIER particleTransition(ID3D12Resource* resource, D3D12_RESOURCE_STATES before, D3D12_RESOURCE_STATES after)
			{
				D3D12_RESOURCE_BARRIER barrier = {};
				barrier.Type                   = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
				barrier.Transition.pResource   = resource;
				barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
				barrier.Transition.StateBefore = before;
				barrier.Transition.StateAfter  = after;
				return barrier;
			}

			// 前の Dispatch の UAV の書き込みが終わってから次を始める (どの UAV も : pResource = nullptr)
			void uavBarrier(ID3D12GraphicsCommandList* cmdList)
			{
				D3D12_RESOURCE_BARRIER barrier = {};
				barrier.Type          = D3D12_RESOURCE_BARRIER_TYPE_UAV;
				barrier.UAV.pResource = nullptr;
				cmdList->ResourceBarrier(1, &barrier);
			}

			// particleSim.shader の cbuffer ParticleConstants と同じ並び (ルート定数 16 個)
			struct ParticleConstants
			{
				float    DeltaTime;
				uint32_t EmitterCount;
				uint32_t SpawnRequested;
				uint32_t Capacity;
				uint32_t FrameSeed;
				uint32_t SimFlags;
				float    CollisionThickness;
				float    Time;
				uint32_t SortK;
				uint32_t SortJ;
				uint32_t SortLevels;
				uint32_t Padding0;
				float    Padding1[4];
			};
			static_assert(sizeof(ParticleConstants) == Pipeline::PARTICLE_CONSTANT_COUNT * sizeof(uint32_t), "ParticleConstants size mismatch");
			constexpr UINT SORT_CONSTANT_OFFSET = offsetof(ParticleConstants, SortK) / sizeof(uint32_t);

			constexpr UINT64 PARTICLE_STRIDE      = 64;     // 1 個の大きさ (16byte × 4 枚。シェーダーの PARTICLE_LOAD)
			constexpr UINT64 DRAW_DATA_STRIDE     = 32;     // 描画用 (シェーダーの storeDrawData)
			constexpr UINT64 COUNTERS_SIZE        = 256;
			constexpr UINT64 ARGS_SIZE            = 256;
			constexpr UINT64 ARGS_DRAW_OFFSET     = 16;     // particleSim.shader の ARGS_DRAW
			constexpr UINT64 READBACK_STRIDE      = 64;     // フレームごとの読み戻しの領域
			constexpr UINT64 READBACK_SIZE        = 24;     // 数 6 個 (生存数 / 出力 / 空き / 出した数 / 出し始め / 映った数)

			uint32_t nextPowerOfTwo(uint32_t value)
			{
				uint32_t result = 1;
				while (result < value && result < 0x80000000u) result <<= 1;
				return result;
			}
		}

		// ==============================================================
		// 作成 / 破棄
		// ==============================================================

		void CommandContext::SetParticleCapacity(uint32_t capacity)
		{
			// 2 の累乗に切り上げる (ソートが 2 の累乗の長さで並べるため)。ソートの 1 回の Dispatch (1024 個) より小さくはしない
			if (capacity > 0)
			{
				capacity = (std::min)(capacity, RHI::MAX_PARTICLE_CAPACITY);
				capacity = (std::max)(nextPowerOfTwo(capacity), Pipeline::PARTICLE_SORT_GROUP);
			}
			if (capacity == _particleCapacity) return;

			// 使用中のバッファを消さないよう、GPU の完了を待ってから作り直す (フレームの記録の外で呼ばれる)
			WaitForGpu();
			releaseParticleBuffers();
			if (capacity == 0) return;
			if (!createParticleBuffers(capacity))
			{
				Core::Logger::Log("[CommandContext] Failed create particle buffers. GPU particles disabled.");
				releaseParticleBuffers();
			}
		}

		bool CommandContext::createParticleBuffers(uint32_t capacity)
		{
			ID3D12Device* device = _device->GetDevice()->GetDevice();
			auto make = [&](ParticleBuffer& target, UINT64 size, D3D12_RESOURCE_FLAGS flags) -> bool
			{
				target.Resource = std::make_unique<DirectX12::ResourceBuffer>();
				target.State    = D3D12_RESOURCE_STATE_COMMON;
				return SUCCEEDED(target.Resource->Create(device, size, D3D12_HEAP_TYPE_DEFAULT, flags));
			};
			const auto uav = D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS;
			if (!make(_particleData[0],  capacity * PARTICLE_STRIDE, uav))                      return false;
			if (!make(_particleData[1],  capacity * PARTICLE_STRIDE, uav))                      return false;
			if (!make(_particleKeys,     capacity * sizeof(uint32_t) * 2, uav))                 return false;
			if (!make(_particleSorted,   capacity * sizeof(uint32_t) * 2, uav))                 return false;
			if (!make(_particleDrawData, capacity * DRAW_DATA_STRIDE, uav))                      return false;
			if (!make(_particleCounters, COUNTERS_SIZE, uav))                                   return false;
			if (!make(_particleArgs,     ARGS_SIZE, uav))                                       return false;
			if (!make(_particleEmitters, sizeof(RHI::ParticleEmitterData) * RHI::MAX_PARTICLE_EMITTERS, D3D12_RESOURCE_FLAG_NONE)) return false;
			// デバッグレイヤー / PIX で見分けられるように名前を付ける
			_particleData[0].Resource->Get()->SetName(L"Particles0");
			_particleData[1].Resource->Get()->SetName(L"Particles1");
			_particleKeys.Resource->Get()->SetName(L"ParticleKeys");
			_particleSorted.Resource->Get()->SetName(L"ParticleSorted");
			_particleDrawData.Resource->Get()->SetName(L"ParticleDrawData");
			_particleCounters.Resource->Get()->SetName(L"ParticleCounters");
			_particleArgs.Resource->Get()->SetName(L"ParticleArgs");
			_particleEmitters.Resource->Get()->SetName(L"ParticleEmitters");
			if (!make(_particleSortBins, sizeof(uint32_t) * Pipeline::PARTICLE_SORT_BUCKETS, uav)) return false;
			_particleSortBins.Resource->Get()->SetName(L"ParticleSortBins");

			_particleEmitterUpload = std::make_unique<DirectX12::ResourceBuffer>();
			if (FAILED(_particleEmitterUpload->Create(device, sizeof(RHI::ParticleEmitterData) * RHI::MAX_PARTICLE_EMITTERS * FRAME_COUNT,
				D3D12_HEAP_TYPE_UPLOAD)) || FAILED(_particleEmitterUpload->Map()))
			{
				return false;
			}
			_particleQuadIndices = std::make_unique<DirectX12::ResourceBuffer>();
			if (FAILED(_particleQuadIndices->Create(device, 16, D3D12_HEAP_TYPE_UPLOAD)) || FAILED(_particleQuadIndices->Map())) return false;
			static const uint16_t QUAD_INDICES[6] = { 0, 1, 2, 0, 2, 3 };
			std::memcpy(_particleQuadIndices->GetMappedData(), QUAD_INDICES, sizeof(QUAD_INDICES));
			_particleQuadIndices->Unmap();

			_particleReadback = std::make_unique<DirectX12::ResourceBuffer>();
			if (FAILED(_particleReadback->Create(device, READBACK_STRIDE * FRAME_COUNT, D3D12_HEAP_TYPE_READBACK))) return false;

			_particleCapacity    = capacity;
			_particleNeedInit    = true;    // 最初の UpdateParticles で空きリストを作る
			_particleInput       = 0;
			_particleEverUpdated = false;
			_particleAliveRead   = 0xFFFFFFFFu;
			for (uint32_t i = 0; i < FRAME_COUNT; ++i)
			{
				_particleReadbackRecorded[i] = false;
				_particleTimed[i]            = false;
				_particleSpawnHistory[i]     = 0;
			}
			_particleStats          = RHI::ParticleStats();
			_particleStats.Capacity = capacity;
			_particleStats.Free     = capacity;

			char message[128] = {};
			sprintf_s(message, "[CommandContext] GPU particles : capacity %u (%.1f MB)", capacity,
				static_cast<double>(capacity * (PARTICLE_STRIDE * 2 + DRAW_DATA_STRIDE + sizeof(uint32_t) * 4)) / (1024.0 * 1024.0));
			Core::Logger::Log(message);
			return true;
		}

		void CommandContext::releaseParticleBuffers()
		{
			for (ParticleBuffer* target : { &_particleData[0], &_particleData[1], &_particleKeys, &_particleSorted, &_particleDrawData,
			                                &_particleCounters, &_particleArgs, &_particleEmitters, &_particleSortBins })
			{
				target->Resource.reset();
				target->State = D3D12_RESOURCE_STATE_COMMON;
			}
			if (_particleEmitterUpload) _particleEmitterUpload->Unmap();
			_particleEmitterUpload.reset();
			_particleReadback.reset();
			_particleQuadIndices.reset();
			_particleCapacity    = 0;
			_particleEverUpdated = false;
			_particleStats       = RHI::ParticleStats();
		}

		void CommandContext::transitionParticle(ParticleBuffer& target, D3D12_RESOURCE_STATES after, D3D12_RESOURCE_BARRIER* barriers, UINT& count)
		{
			if (!target.Resource || target.State == after) return;
			barriers[count++] = particleTransition(target.Resource->Get(), target.State, after);
			target.State = after;
		}

		// ==============================================================
		// エミッター (毎フレーム C# から)
		// ==============================================================

		void CommandContext::SetParticleEmitters(const RHI::ParticleEmitterData* emitters, uint32_t count, const RHI::ParticleFrameSettings& settings)
		{
			count = (std::min)(emitters ? count : 0u, RHI::MAX_PARTICLE_EMITTERS);
			_particleEmitterData.assign(emitters, emitters + count);
			_particleSettings = settings;

			// 発生のスレッド → エミッター の対応 : 前のエミッターまでの出す数の合計 (CSParticleEmit が二分探索する)
			// 容量を超えて頼まれても、出せるのは空きの数まで (GPU の Begin が決める)
			uint64_t total = 0;
			_particleHasAlpha = false;
			for (auto& emitter : _particleEmitterData)
			{
				// 半透明のエミッターがある時だけソートする (加算だけなら並び順に関係なく同じ色になる : 資料の高速化案)
				// 外したエミッター (出す数 0) も、出た物が残っている間は送られてくるので数に入る
				if ((emitter.Flags & RHI::PARTICLE_EMITTER_ADDITIVE) == 0) _particleHasAlpha = true;
				emitter.SpawnOffset = static_cast<uint32_t>((std::min)(total, static_cast<uint64_t>(0xFFFFFFFFu)));
				total += emitter.SpawnCount;
			}
			_particleSpawnRequested = static_cast<uint32_t>((std::min)(total, static_cast<uint64_t>(_particleCapacity)));
		}

		// ==============================================================
		// 読み戻し (RenderStart : このフレームの FrameContext を GPU が使い終わった後)
		// ==============================================================

		void CommandContext::readParticleResults()
		{
			// バッファは前のフレームの ExecuteCommandLists の完了で COMMON に戻っている
			for (ParticleBuffer* target : { &_particleData[0], &_particleData[1], &_particleKeys, &_particleSorted, &_particleDrawData,
			                                &_particleCounters, &_particleArgs, &_particleEmitters, &_particleSortBins })
			{
				target->State = D3D12_RESOURCE_STATE_COMMON;
			}
			_particleUpdated = false;
			_particleDrawn   = false;
			if (_particleCapacity == 0) return;

			// FRAME_COUNT フレーム前の生存数 / 空きの数
			if (_particleReadbackRecorded[_frameIndex] && _particleReadback)
			{
				void* mapped = nullptr;
				const D3D12_RANGE readRange = { READBACK_STRIDE * _frameIndex, READBACK_STRIDE * _frameIndex + READBACK_SIZE };
				if (SUCCEEDED(_particleReadback->Get()->Map(0, &readRange, &mapped)))
				{
					const uint32_t* counters = reinterpret_cast<const uint32_t*>(static_cast<const uint8_t*>(mapped) + readRange.Begin);
					_particleAliveRead     = counters[0];   // Finish の後 : 入力の数 = 生き残った数
					_particleStats.Alive   = counters[0];
					_particleStats.Free    = counters[2];   // Finish が書く (容量 - 生き残った数)
					_particleStats.Visible = counters[5];   // 画面に映った数 (描いた数)
					_particleStats.Valid   = 1;
					const D3D12_RANGE noWrite = { 0, 0 };
					_particleReadback->Get()->Unmap(0, &noWrite);
				}
			}
			_particleReadbackRecorded[_frameIndex] = false;

			// GPU の時間 : 3 更新の始め → 4 ソートの始め → 5 更新の終わり → 6 描画の始め → 7 描画の終わり
			if (_particleTimed[_frameIndex] && _gpuTimer)
			{
				double intervals[TIMESTAMP_PARTICLE_COUNT - 1] = {};
				if (_gpuTimer->ReadIntervalsFrom(_frameIndex, TIMESTAMP_PARTICLE_BEGIN, TIMESTAMP_PARTICLE_COUNT - 1, intervals))
				{
					_particleStats.UpdateMs = static_cast<float>(intervals[0] + intervals[1]);
					_particleStats.SortMs   = static_cast<float>(intervals[1]);
					_particleStats.DrawMs   = static_cast<float>(intervals[3]);
				}
			}
			_particleTimed[_frameIndex] = false;
			_particleStats.Emitters = static_cast<uint32_t>(_particleEmitterData.size());
		}

		void CommandContext::finishParticleTimestamps()
		{
			// 更新したのに描かなかったフレームは、描画の区切り (6 / 7) を今書いて区間 0 にする (欠けたまま読み戻さない)
			if (!_gpuTimer || !_particleTimed[_frameIndex] || _particleDrawn) return;
			auto cmdList = _frameCommandList->GetCmdList();
			_gpuTimer->Timestamp(cmdList, _frameIndex, TIMESTAMP_PARTICLE_BEGIN + 3);
			_gpuTimer->Timestamp(cmdList, _frameIndex, TIMESTAMP_PARTICLE_BEGIN + 4);
		}

		// ==============================================================
		// 命令 UpdateParticles
		// ==============================================================

		void CommandContext::updateParticles(Buffer* buffer)
		{
			// 1 フレームに 1 回だけ (2 回目以降は何もしない)
			if (_particleUpdated || _particleCapacity == 0 || !_currentPipeline->IsParticleAvailable()) return;
			_particleUpdated = true;

			auto cmdList     = _frameCommandList->GetCmdList();
			auto heapManager = buffer->GetDescHeapManager();
			if (_gpuTimer)
			{
				_gpuTimer->Timestamp(cmdList, _frameIndex, TIMESTAMP_PARTICLE_BEGIN + 0);
				_particleTimed[_frameIndex] = true;
			}

			const uint32_t emitterCount = static_cast<uint32_t>(_particleEmitterData.size());
			const uint32_t spawn        = _particleSpawnRequested;
			_particleSpawnHistory[_frameIndex] = spawn;

			// ----------------------------------------------------------
			// 衝突 : シーンの深度 (不透明を描いた後) をコンピュートで読む
			//   深度を描画先に付けたままでは読めないので一度外し、終わったら付け直す (この後に半透明を描くことがある)
			//   MSAA の時は 1 サンプルへ解決した深度を読む (MSAA の深度は付けたままでよい)
			// ----------------------------------------------------------
			D3D12_GPU_DESCRIPTOR_HANDLE depthHandle = heapManager->GetSrvGpuHandle(Buffer::SRV_INDEX_TEXTURE_BEGIN);   // 使わない時は既定テクスチャ (読まない)
			const bool collision = (_particleSettings.Flags & RHI::PARTICLE_FRAME_COLLISION) != 0 && _depthBuffer && ensureDepthSrv(buffer);
			const bool rebindDepth = collision && _boundDepth == RHI::RenderTargetHandle::SCENE_DEPTH && !_boundDepthMsaa;
			if (collision)
			{
				if (rebindDepth) cmdList->OMSetRenderTargets(_boundColorCount, _boundColorCount > 0 ? _boundRtvs : nullptr, FALSE, nullptr);
				if (_msaaDepthDirty) resolveSceneDepth(buffer);
				transitionDepth(D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
				_sceneDepthReadThisFrame = true;
				depthHandle = heapManager->GetSrvGpuHandle(_depthSrvIndex);
			}

			// ----------------------------------------------------------
			// 1. エミッターを VRAM へ (このフレームの UPLOAD の領域 → DEFAULT)
			//    UPLOAD を直接読ませると、パーティクルの数だけ PCIe 越しに読むことになるので写す
			// ----------------------------------------------------------
			D3D12_RESOURCE_BARRIER barriers[10] = {};
			UINT barrierCount = 0;
			const UINT64 uploadOffset = static_cast<UINT64>(_frameIndex) * RHI::MAX_PARTICLE_EMITTERS * sizeof(RHI::ParticleEmitterData);
			if (emitterCount > 0)
			{
				std::memcpy(static_cast<uint8_t*>(_particleEmitterUpload->GetMappedData()) + uploadOffset,
					_particleEmitterData.data(), emitterCount * sizeof(RHI::ParticleEmitterData));
				transitionParticle(_particleEmitters, D3D12_RESOURCE_STATE_COPY_DEST, barriers, barrierCount);
				cmdList->ResourceBarrier(barrierCount, barriers);
				barrierCount = 0;
				cmdList->CopyBufferRegion(_particleEmitters.Resource->Get(), 0, _particleEmitterUpload->Get(), uploadOffset,
					emitterCount * sizeof(RHI::ParticleEmitterData));
			}

			// 2. コンピュートが書く物を UAV へ、エミッターを読む状態へ
			ParticleBuffer& dataIn  = _particleData[_particleInput];
			ParticleBuffer& dataOut = _particleData[_particleInput ^ 1];
			const D3D12_RESOURCE_STATES uavState = D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
			transitionParticle(_particleEmitters, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, barriers, barrierCount);
			transitionParticle(dataIn,            uavState, barriers, barrierCount);
			transitionParticle(dataOut,           uavState, barriers, barrierCount);
			transitionParticle(_particleKeys,     uavState, barriers, barrierCount);
			transitionParticle(_particleSorted,   uavState, barriers, barrierCount);
			transitionParticle(_particleDrawData, uavState, barriers, barrierCount);
			transitionParticle(_particleCounters, uavState, barriers, barrierCount);
			transitionParticle(_particleArgs,     uavState, barriers, barrierCount);
			transitionParticle(_particleSortBins, uavState, barriers, barrierCount);
			if (barrierCount > 0) cmdList->ResourceBarrier(barrierCount, barriers);
			barrierCount = 0;

			// 3. ルート引数 (このフレームの間は変えない。ソートの段だけルート定数を書き換える)
			ParticleConstants constants = {};
			constants.DeltaTime          = _particleSettings.DeltaTime;
			constants.EmitterCount       = emitterCount;
			constants.SpawnRequested     = spawn;
			constants.Capacity           = _particleCapacity;
			constants.FrameSeed          = ++_particleFrameSeed * 0x9E3779B9u;
			constants.SimFlags           = collision ? RHI::PARTICLE_FRAME_COLLISION : 0u;
			constants.CollisionThickness = _particleSettings.CollisionThickness;
			constants.Time               = _particleSettings.Time;

			cmdList->SetComputeRootSignature(_currentPipeline->GetParticleRootSignature());
			cmdList->SetComputeRootConstantBufferView(Pipeline::PARTICLE_PARAM_FRAME, buffer->GetViewConstants(_frameIndex, 0));
			cmdList->SetComputeRoot32BitConstants(Pipeline::PARTICLE_PARAM_CONSTANTS, Pipeline::PARTICLE_CONSTANT_COUNT, &constants, 0);
			cmdList->SetComputeRootShaderResourceView(Pipeline::PARTICLE_PARAM_EMITTERS,   _particleEmitters.Resource->GetGPUVirtualAddress());
			cmdList->SetComputeRootUnorderedAccessView(Pipeline::PARTICLE_PARAM_PARTICLES_IN,  dataIn.Resource->GetGPUVirtualAddress());
			cmdList->SetComputeRootUnorderedAccessView(Pipeline::PARTICLE_PARAM_PARTICLES_OUT, dataOut.Resource->GetGPUVirtualAddress());
			cmdList->SetComputeRootUnorderedAccessView(Pipeline::PARTICLE_PARAM_SORTED,        _particleSorted.Resource->GetGPUVirtualAddress());
			cmdList->SetComputeRootUnorderedAccessView(Pipeline::PARTICLE_PARAM_KEYS,          _particleKeys.Resource->GetGPUVirtualAddress());
			cmdList->SetComputeRootUnorderedAccessView(Pipeline::PARTICLE_PARAM_COUNTERS,  _particleCounters.Resource->GetGPUVirtualAddress());
			cmdList->SetComputeRootUnorderedAccessView(Pipeline::PARTICLE_PARAM_ARGS,      _particleArgs.Resource->GetGPUVirtualAddress());
			cmdList->SetComputeRootDescriptorTable(Pipeline::PARTICLE_PARAM_DEPTH, depthHandle);
			cmdList->SetComputeRootUnorderedAccessView(Pipeline::PARTICLE_PARAM_SORT_BINS, _particleSortBins.Resource->GetGPUVirtualAddress());
			cmdList->SetComputeRootUnorderedAccessView(Pipeline::PARTICLE_PARAM_DRAW_DATA, _particleDrawData.Resource->GetGPUVirtualAddress());

			// 4. 作り直した直後 : 数を 0 に (資料の Clear System。以前は空きリストも作っていた)
			if (_particleNeedInit)
			{
				cmdList->SetPipelineState(_currentPipeline->GetParticlePipeline(Pipeline::PARTICLE_KERNEL_INIT));
				cmdList->Dispatch(1, 1, 1);
				uavBarrier(cmdList);
				_particleNeedInit = false;
			}

			// 5. Begin : 出す数を決める (1 スレッド)
			cmdList->SetPipelineState(_currentPipeline->GetParticlePipeline(Pipeline::PARTICLE_KERNEL_BEGIN));
			cmdList->Dispatch(1, 1, 1);
			uavBarrier(cmdList);

			// 6. Emit : CPU が頼んだ数だけスレッドを立てる (空きが足りなければ、後ろのスレッドは何もしない)
			if (spawn > 0 && emitterCount > 0)
			{
				cmdList->SetPipelineState(_currentPipeline->GetParticlePipeline(Pipeline::PARTICLE_KERNEL_EMIT));
				cmdList->Dispatch((spawn + Pipeline::PARTICLE_EMIT_GROUP - 1) / Pipeline::PARTICLE_EMIT_GROUP, 1, 1);
				uavBarrier(cmdList);
			}

			// 7. Simulate : 生きている数は GPU しか知らないので、Begin が書いた引数で Dispatch する (CPU は待たない)
			transitionParticle(_particleArgs, D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT, barriers, barrierCount);
			cmdList->ResourceBarrier(barrierCount, barriers);
			barrierCount = 0;
			cmdList->SetPipelineState(_currentPipeline->GetParticlePipeline(Pipeline::PARTICLE_KERNEL_SIMULATE));
			cmdList->ExecuteIndirect(_currentPipeline->GetParticleDispatchSignature(), 1, _particleArgs.Resource->Get(), 0, nullptr, 0);
			transitionParticle(_particleArgs, uavState, barriers, barrierCount);
			cmdList->ResourceBarrier(barrierCount, barriers);
			barrierCount = 0;
			uavBarrier(cmdList);

			// 8. Finish : 描画の引数 (1 スレッド)
			cmdList->SetPipelineState(_currentPipeline->GetParticlePipeline(Pipeline::PARTICLE_KERNEL_FINISH));
			cmdList->Dispatch(1, 1, 1);
			uavBarrier(cmdList);

			// 9. ソート (半透明を奥から順に重ねる)
			//    並べる数の上限 = FRAME_COUNT フレーム前の生存数 + その後のフレームで頼んだ数 (CPU が知っている。実際はこれ以下)
			//    読み戻しがまだ無い時は容量全部
			if (_gpuTimer) _gpuTimer->Timestamp(cmdList, _frameIndex, TIMESTAMP_PARTICLE_BEGIN + 1);
			const bool sortAvailable = _currentPipeline->GetParticlePipeline(Pipeline::PARTICLE_KERNEL_SORT_LOCAL) &&
			                           _currentPipeline->GetParticlePipeline(Pipeline::PARTICLE_KERNEL_SORT_STEP) &&
			                           _currentPipeline->GetParticlePipeline(Pipeline::PARTICLE_KERNEL_SORT_MERGE);
			const bool sort = (_particleSettings.Flags & RHI::PARTICLE_FRAME_SORT) != 0 && sortAvailable && _particleHasAlpha;
			if (sort)
			{
				uint64_t bound = _particleCapacity;
				if (_particleAliveRead != 0xFFFFFFFFu)
				{
					bound = _particleAliveRead;
					for (uint32_t i = 0; i < FRAME_COUNT; ++i) bound += _particleSpawnHistory[i];
				}
				sortParticles(static_cast<uint32_t>((std::min)(bound, static_cast<uint64_t>(_particleCapacity))));
			}
			if (_gpuTimer) _gpuTimer->Timestamp(cmdList, _frameIndex, TIMESTAMP_PARTICLE_BEGIN + 2);

			// 10. 数を読み戻す (プロファイラー / ソートの上限。FRAME_COUNT フレーム後に RenderStart で読む)
			transitionParticle(_particleCounters, D3D12_RESOURCE_STATE_COPY_SOURCE, barriers, barrierCount);
			cmdList->ResourceBarrier(barrierCount, barriers);
			barrierCount = 0;
			cmdList->CopyBufferRegion(_particleReadback->Get(), READBACK_STRIDE * _frameIndex, _particleCounters.Resource->Get(), 0, READBACK_SIZE);
			_particleReadbackRecorded[_frameIndex] = true;

			// 11. 後片付け : 深度を描画先に戻す / 描画用の PSO に戻す / 次のフレームは今の出力が入力
			if (rebindDepth)
			{
				transitionDepth(D3D12_RESOURCE_STATE_DEPTH_WRITE);
				const D3D12_CPU_DESCRIPTOR_HANDLE dsv = boundDsv();
				cmdList->OMSetRenderTargets(_boundColorCount, _boundColorCount > 0 ? _boundRtvs : nullptr, FALSE, &dsv);
			}
			cmdList->SetPipelineState(_currentPipeline->GetPipelineState());
			// 次のフレームの入力 = 今の出力 (描くのもこれ)。描く順番の列は、ソートした時は並べた物
			_particleDrawSorted  = sort;
			_particleInput      ^= 1;
			_particleEverUpdated = true;

			// 出す数は使い切り (C# が次のフレームも送り直す。送られなければ新しく出さない)
			for (auto& emitter : _particleEmitterData) emitter.SpawnCount = 0;
			_particleSpawnRequested = 0;
		}

		void CommandContext::sortParticles(uint32_t upperBound)
		{
			// ==============================================================
			// ソート : 出力の生存リストを、使い終わった入力のリストへ写しながら奥から順に並べる
			//          (出力は次のフレームの入力なので崩さない)
			//   多い時 : 計数ソート (奥行きを 32768 区間に分けて数える → 区間の先頭の位置 → 書く。列を 2 回読むだけ)
			//   少ない時 (並べる長さ <= 65536) : バイトニックソート (計数ソートは区間の消去と累積の固定費が約 0.09 ms ある)
			//   (以前はバイトニックソートだけで、100 万個で 3.4 ms かかっていた。今は 0.34 ms)
			// ==============================================================
			auto cmdList = _frameCommandList->GetCmdList();
			const uint32_t group = Pipeline::PARTICLE_SORT_GROUP;
			const uint32_t n = (std::min)((std::max)(nextPowerOfTwo((std::max)(upperBound, 1u)), group), _particleCapacity);
			const bool countingSort = n > Pipeline::PARTICLE_COUNTING_SORT_MIN &&
			                          _currentPipeline->GetParticlePipeline(Pipeline::PARTICLE_KERNEL_BIN_CLEAR) &&
			                          _currentPipeline->GetParticlePipeline(Pipeline::PARTICLE_KERNEL_BIN_COUNT) &&
			                          _currentPipeline->GetParticlePipeline(Pipeline::PARTICLE_KERNEL_BIN_SCAN) &&
			                          _currentPipeline->GetParticlePipeline(Pipeline::PARTICLE_KERNEL_BIN_SCATTER);
			if (countingSort)
			{
				// 数える / 書くスレッドは上限の数だけ (生存数より後ろはシェーダーが何もしない)
				const UINT threads = Pipeline::PARTICLE_THREAD_GROUP;
				cmdList->SetPipelineState(_currentPipeline->GetParticlePipeline(Pipeline::PARTICLE_KERNEL_BIN_CLEAR));
				cmdList->Dispatch(Pipeline::PARTICLE_SORT_BUCKETS / threads, 1, 1);
				uavBarrier(cmdList);
				cmdList->SetPipelineState(_currentPipeline->GetParticlePipeline(Pipeline::PARTICLE_KERNEL_BIN_COUNT));
				cmdList->Dispatch((upperBound + threads - 1) / threads, 1, 1);
				uavBarrier(cmdList);
				cmdList->SetPipelineState(_currentPipeline->GetParticlePipeline(Pipeline::PARTICLE_KERNEL_BIN_SCAN));
				cmdList->Dispatch(1, 1, 1);
				uavBarrier(cmdList);
				cmdList->SetPipelineState(_currentPipeline->GetParticlePipeline(Pipeline::PARTICLE_KERNEL_BIN_SCATTER));
				cmdList->Dispatch((upperBound + threads - 1) / threads, 1, 1);
				uavBarrier(cmdList);
				return;
			}

			// バイトニックソート (並べる長さ n = 上限以上の 2 の累乗。生存数より後ろはシェーダーが「一番手前」として末尾に集める)
			//   1. SortLocal : 2048 個ずつ、共有メモリで k = 2〜2048 (最初の 1 回だけ。ここで写す)
			//   2. k = 4096〜n の段ごとに : 距離 j >= 2048 は SortStep (1 スレッド最大 16 個で 4 段ずつまとめる)、
			//      残り (1024〜1) は SortMerge (共有メモリ)
			//   Dispatch の数 : 65536 個 (2^16) で 1 + 6 + 5 = 12 回 (以前は 1 段 1 回で 21 回)
			cmdList->SetPipelineState(_currentPipeline->GetParticlePipeline(Pipeline::PARTICLE_KERNEL_SORT_LOCAL));
			cmdList->Dispatch(n / group, 1, 1);
			uavBarrier(cmdList);

			for (uint32_t k = group * 2; k <= n; k <<= 1)
			{
				uint32_t sortConstants[3] = { k, 0, 0 };   // K / J / 段の数
				for (uint32_t j = k >> 1; j >= group; )
				{
					// まとめる段 : 最大 4 段。ただし距離 2048 未満は SortMerge が行うので、そこまで
					uint32_t levels = 0;
					while (levels < Pipeline::PARTICLE_SORT_STEP_LEVELS && (j >> levels) >= group) ++levels;
					sortConstants[1] = j;
					sortConstants[2] = levels;
					cmdList->SetPipelineState(_currentPipeline->GetParticlePipeline(Pipeline::PARTICLE_KERNEL_SORT_STEP));
					cmdList->SetComputeRoot32BitConstants(Pipeline::PARTICLE_PARAM_CONSTANTS, 3, sortConstants, SORT_CONSTANT_OFFSET);
					cmdList->Dispatch((n >> levels) / Pipeline::PARTICLE_THREAD_GROUP, 1, 1);   // 1 スレッド 2^段 個
					uavBarrier(cmdList);
					j >>= levels;
				}
				cmdList->SetComputeRoot32BitConstants(Pipeline::PARTICLE_PARAM_CONSTANTS, 3, sortConstants, SORT_CONSTANT_OFFSET);
				cmdList->SetPipelineState(_currentPipeline->GetParticlePipeline(Pipeline::PARTICLE_KERNEL_SORT_MERGE));
				cmdList->Dispatch(n / group, 1, 1);
				uavBarrier(cmdList);
			}
		}

		// ==============================================================
		// 命令 DrawParticles
		// ==============================================================

		void CommandContext::drawParticles(Buffer* buffer)
		{
			if (!_particleEverUpdated || _particleCapacity == 0 || !_currentPipeline->IsParticleAvailable() || _boundColorCount == 0) return;
			ID3D12PipelineState* pso = _currentPipeline->GetParticleDrawPso(_boundFormats, _boundColorCount, _boundSamples);
			if (!pso) return;

			auto cmdList = _frameCommandList->GetCmdList();
			const bool timed = _gpuTimer && _particleTimed[_frameIndex] && !_particleDrawn;
			if (timed) _gpuTimer->Timestamp(cmdList, _frameIndex, TIMESTAMP_PARTICLE_BEGIN + 3);

			// シーンの深度はシェーダーが読んで比べる (描画先からは外す)。MSAA なら 1 サンプルへ解決した物を読む
			uint32_t constants[Pipeline::PASS_CONSTANT_COUNT] = {};
			constants[0] = 0xFFFFFFFFu;   // g_Inputs[0].x : 深度なし
			if (_depthBuffer && ensureDepthSrv(buffer))
			{
				if (_boundDepth == RHI::RenderTargetHandle::SCENE_DEPTH)
				{
					cmdList->OMSetRenderTargets(_boundColorCount, _boundRtvs, FALSE, nullptr);
					_boundDepth     = RHI::RenderTargetHandle::INVALID;
					_boundDepthMsaa = false;
				}
				if (_msaaDepthDirty) resolveSceneDepth(buffer);
				transitionDepth(D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
				_sceneDepthReadThisFrame = true;
				constants[0] = _depthSrvIndex - Buffer::SRV_INDEX_TEXTURE_BEGIN;
			}
			// g_Params[0].x : ソフトパーティクルの距離 / .y : 描画先のピクセル → 深度のピクセルの倍率 (縮小バッファなら 2)
			const float params[2] = { _particleSettings.SoftDistance,
			                          _boundWidth > 0 && _depthWidth > 0 ? static_cast<float>(_depthWidth) / static_cast<float>(_boundWidth) : 1.0f };
			std::memcpy(&constants[RHI::MAX_PASS_INPUTS], params, sizeof(params));

			// 引数 (GPU が書いた生存数) / パーティクル / 生存リストを読む状態へ
			ParticleBuffer& drawList  = _particleDrawSorted ? _particleSorted : _particleKeys;   // 描く順番 (キー, 位置)
			ParticleBuffer& particles = _particleDrawData;                                      // 描画用 32byte (最後の更新が書いた。本体は読まない)
			D3D12_RESOURCE_BARRIER barriers[3] = {};
			UINT barrierCount = 0;
			transitionParticle(_particleArgs,   D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT, barriers, barrierCount);
			transitionParticle(particles,       D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, barriers, barrierCount);
			transitionParticle(drawList,        D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE, barriers, barrierCount);
			if (barrierCount > 0) cmdList->ResourceBarrier(barrierCount, barriers);

			// ルートシグネチャはシーンと同じ。頂点シェーダーのルート SRV (t1 / t2) を描く間だけ差し替える
			cmdList->SetPipelineState(pso);
			cmdList->SetGraphicsRoot32BitConstants(Pipeline::ROOT_PARAM_PASS_CONSTANTS, Pipeline::PASS_CONSTANT_COUNT, constants, 0);
			cmdList->SetGraphicsRootShaderResourceView(Pipeline::ROOT_PARAM_DRAW_DATA_SRV,   particles.Resource->GetGPUVirtualAddress());
			cmdList->SetGraphicsRootShaderResourceView(Pipeline::ROOT_PARAM_OBJECT_DATA_SRV, drawList.Resource->GetGPUVirtualAddress());
			// 四角のインデックス (6 個) に差し替えて描き、シーンのメガインデックスバッファに戻す
			D3D12_INDEX_BUFFER_VIEW quadView = {};
			quadView.BufferLocation = _particleQuadIndices->GetGPUVirtualAddress();
			quadView.SizeInBytes    = sizeof(uint16_t) * 6;
			quadView.Format         = DXGI_FORMAT_R16_UINT;
			cmdList->IASetIndexBuffer(&quadView);
			cmdList->ExecuteIndirect(_currentPipeline->GetParticleDrawSignature(), 1, _particleArgs.Resource->Get(), ARGS_DRAW_OFFSET, nullptr, 0);
			D3D12_INDEX_BUFFER_VIEW sceneView = {};
			sceneView.BufferLocation = buffer->GetSceneIB()->GetGPUVirtualAddress();
			sceneView.SizeInBytes    = static_cast<UINT>(buffer->GetSceneIndexBufferSize());
			sceneView.Format         = DXGI_FORMAT_R32_UINT;
			cmdList->IASetIndexBuffer(&sceneView);

			// シーンの描画が続く時のために戻す (DrawData は描くたびに設定されるが、ObjectData は PrepareScene で 1 回だけ)
			if (_scene.ObjectData != 0) cmdList->SetGraphicsRootShaderResourceView(Pipeline::ROOT_PARAM_OBJECT_DATA_SRV, _scene.ObjectData);
			if (_draws[_frameIndex].Gpu) cmdList->SetGraphicsRootShaderResourceView(Pipeline::ROOT_PARAM_DRAW_DATA_SRV, _draws[_frameIndex].Gpu->GetGPUVirtualAddress());

			if (timed) _gpuTimer->Timestamp(cmdList, _frameIndex, TIMESTAMP_PARTICLE_BEGIN + 4);
			_particleDrawn = true;
		}
	}
}

/************************************
* End of file
************************************/
