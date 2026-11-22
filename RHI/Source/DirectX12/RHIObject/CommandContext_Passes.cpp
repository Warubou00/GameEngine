
/***********************************************************************************
* @file    CommandContext_Passes.cpp
* @brief   RHI CommandContext : 描画パスの区切り (BeginPass / EndPass。v1.10 P2)
* @details C# の RenderPipeline が積む BeginPass / EndPass で、描画の命令をパスに分ける。描画の結果には影響しない
*            - PIX / RenderDoc のイベント (ID3D12GraphicsCommandList::BeginEvent / EndEvent)。毎フレーム
*            - CPU の時間 (C++ が命令を積むのにかかった時間)。毎フレーム
*            - GPU の時間 (タイムスタンプ)。SetPassTimingEnabled(true) の間だけ (エディターのプロファイラーの窓が開いている間)
*          GPU の値は FRAME_COUNT フレーム後 (そのフレームの GPU の完了待ちの後) に読む
*          タイムスタンプは GpuTimer の 10 番から (0〜9 はフレーム / シーン / パーティクル / スキニング)
*
*          以前はシーン全体と UI の 2 区間しか GPU の時間が分からず、どのパス (影 / SSAO / トーンマップ …) が重いか見えなかった
*
* @author  Keigo Onari
* @date    2026/11/15
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/


/******************************************************************
* Include Headers
******************************************************************/
#include "CommandContext.h"

#include "../Object/dx12_graphicsCommandList.h"
#include "../Object/dx12_gpuTimer.h"

#include <algorithm>
#include <cstring>

#include "Device.h"
#include "../Object/dx12_device.h"
#include "../Object/dx12_resourceBuffer.h"
#include "../Object/dx12_swapChain.h"

namespace RHI
{
	namespace ObjectInterface
	{
		namespace
		{
			std::wstring toWide(const std::string& text)
			{
				if (text.empty()) return std::wstring();
				const int length = MultiByteToWideChar(CP_UTF8, 0, text.c_str(), static_cast<int>(text.size()), nullptr, 0);
				std::wstring wide(static_cast<size_t>(length), L'\0');
				MultiByteToWideChar(CP_UTF8, 0, text.c_str(), static_cast<int>(text.size()), &wide[0], length);
				return wide;
			}

			constexpr UINT     PASS_TIMESTAMP_FIRST = RHI::DirectX12::GpuTimer::FIXED_TIMESTAMPS;
			constexpr UINT     PASS_TIMESTAMP_MAX   = RHI::DirectX12::GpuTimer::PASS_TIMESTAMPS;
			constexpr uint32_t NO_TIMESTAMP         = 0xFFFFFFFFu;
		}

		// ==============================================================
		// 名前
		// ==============================================================

		uint32_t CommandContext::RegisterPassName(const char* name)
		{
			const std::string key = (name && *name) ? name : "(unnamed pass)";
			const auto found = _passNameIds.find(key);
			if (found != _passNameIds.end()) return found->second;
			const uint32_t id = static_cast<uint32_t>(_passNames.size());
			_passNames.push_back(key);
			_passNamesWide.push_back(toWide(key));
			_passNameIds.emplace(key, id);
			return id;
		}

		const char* CommandContext::GetPassName(uint32_t id)const
		{
			return id < _passNames.size() ? _passNames[id].c_str() : nullptr;
		}

		uint32_t CommandContext::GetPassTimings(RHI::PassTiming* out, uint32_t capacity)const
		{
			const uint32_t count = static_cast<uint32_t>(_passResults.size());
			if (out)
			{
				const uint32_t n = (std::min)(count, capacity);
				for (uint32_t i = 0; i < n; ++i) out[i] = _passResults[i];
			}
			return count;
		}

		// ==============================================================
		// レンダーターゲットの名前 (PIX / RenderDoc / デバッグレイヤーに出る)
		// ==============================================================

		void CommandContext::SetRenderTargetName(RHI::RenderTargetHandle handle, const char* name)
		{
			if (handle.Index >= _renderTargets.size() || !_renderTargets[handle.Index].Alive) return;
			_renderTargets[handle.Index].Name = toWide(name ? name : "");
			applyRenderTargetName(handle.Index);
		}

		void CommandContext::applyRenderTargetName(uint32_t index)
		{
			if (index >= _renderTargets.size()) return;
			RenderTarget& target = _renderTargets[index];
			if (target.Name.empty()) return;   // 既定の名前 (RenderTarget / DepthTarget …) のまま
			if (target.Resource) target.Resource->SetName(target.Name.c_str());
			if (target.Resolved) target.Resolved->SetName((target.Name + L" (resolved)").c_str());
		}

		// ==============================================================
		// フレームの記録
		// ==============================================================

		void CommandContext::startPassFrame()
		{
			_passStack.clear();
			_passRecords[_frameIndex].clear();
			_passTimestampsUsed[_frameIndex] = 0;
			_passTimed[_frameIndex] = _passTimingEnabled && _gpuTimer != nullptr;
			// C++ の中のパスの名前 (初めて使う時に登録する)
			if (_uiPassName == 0xFFFFFFFFu)   _uiPassName   = RegisterPassName("UI (ImGui / game UI)");
			if (_skinPassName == 0xFFFFFFFFu) _skinPassName = RegisterPassName("Skinning (compute)");
			if (_debugLinesPassName == 0xFFFFFFFFu) _debugLinesPassName = RegisterPassName("Finish (debug lines)");   // v1.12 P3

			// v1.12 P3 : Render Graph Viewer の記録 (窓が開いている間だけ)
			_graphRecording = _graphCapture;
			_graphAccesses.clear();
			_graphTextureInputs.clear();
			_graphResourceMap.clear();
			s_graphSink = nullptr;
			if (_graphRecording)
			{
				// バリアの相手 (ID3D12Resource) → リソースの Id。最上位 bit = MSAA の写しの方
				for (uint32_t i = 0; i < static_cast<uint32_t>(_renderTargets.size()); ++i)
				{
					const RenderTarget& target = _renderTargets[i];
					if (!target.Alive) continue;
					if (target.Resource) _graphResourceMap.emplace_back(target.Resource.Get(), i);
					if (target.Resolved) _graphResourceMap.emplace_back(target.Resolved.Get(), i | 0x80000000u);
				}
				if (_depthBuffer) _graphResourceMap.emplace_back(_depthBuffer->Get(), RHI::RenderTargetHandle::SCENE_DEPTH & 0x7FFFFFFFu);
				if (_msaaDepth)   _graphResourceMap.emplace_back(_msaaDepth.Get(), RHI::RENDER_GRAPH_MSAA_DEPTH & 0x7FFFFFFFu);
				if (_currentBackBuffer) _graphResourceMap.emplace_back(_currentBackBuffer, RHI::RenderTargetHandle::BACK_BUFFER & 0x7FFFFFFFu);
				s_graphSink = this;
			}
		}

		// ==============================================================
		// v1.12 P3 : Render Graph Viewer の記録
		// ==============================================================

		CommandContext* CommandContext::s_graphSink = nullptr;

		void CommandContext::graphAccess(uint32_t resourceId, uint32_t kind)
		{
			if (!_graphRecording || resourceId == RHI::RenderTargetHandle::INVALID) return;
			// エディター : 画面の代わりにシーンの絵へ描いている
			if (resourceId == RHI::RenderTargetHandle::BACK_BUFFER && _viewTarget != 0xFFFFFFFFu) resourceId = _viewTarget;
			// 普通のテクスチャ (全画面シェーダーの入力)
			if (resourceId < 0x80000000u && (resourceId & RHI::RenderTargetHandle::TEXTURE_INPUT) != 0 &&
			    std::find(_graphTextureInputs.begin(), _graphTextureInputs.end(), resourceId) == _graphTextureInputs.end())
			{
				_graphTextureInputs.push_back(resourceId);
			}
			const uint32_t pass = _passStack.empty() ? RHI::RENDER_GRAPH_NO_PASS : _passStack.back();
			// 同じパス・同じリソースの読み書きは 1 つにまとめる (後ろの数十個だけを見る。バリアは別)
			const size_t count = _graphAccesses.size();
			for (size_t i = count, seen = 0; i > 0 && seen < 64; --i, ++seen)
			{
				RHI::RenderGraphAccess& access = _graphAccesses[i - 1];
				if (access.Pass != pass) break;
				if (access.ResourceId == resourceId && (access.Kind & RHI::RENDER_GRAPH_BARRIER) == 0)
				{
					access.Kind |= kind;
					return;
				}
			}
			RHI::RenderGraphAccess access;
			access.Pass       = pass;
			access.ResourceId = resourceId;
			access.Kind       = kind;
			_graphAccesses.push_back(access);
		}

		void CommandContext::graphBoundWrite(bool depthWrite)
		{
			if (!_graphRecording) return;
			for (uint32_t c = 0; c < _boundColorCount; ++c) graphAccess(_boundColors[c], RHI::RENDER_GRAPH_WRITE);
			if (_boundDepth != RHI::RenderTargetHandle::INVALID)
			{
				const uint32_t depth = (_boundDepth == RHI::RenderTargetHandle::SCENE_DEPTH && _boundDepthMsaa) ? RHI::RENDER_GRAPH_MSAA_DEPTH : _boundDepth;
				graphAccess(depth, depthWrite ? RHI::RENDER_GRAPH_DEPTH_WRITE : RHI::RENDER_GRAPH_DEPTH_TEST);
			}
		}

		void CommandContext::graphShadowRead()
		{
			if (!_graphRecording) return;
			if (_shadowMapTarget != RHI::RenderTargetHandle::INVALID) graphAccess(_shadowMapTarget, RHI::RENDER_GRAPH_READ | RHI::RENDER_GRAPH_IMPLICIT);
			if (_localShadowMapTarget != RHI::RenderTargetHandle::INVALID) graphAccess(_localShadowMapTarget, RHI::RENDER_GRAPH_READ | RHI::RENDER_GRAPH_IMPLICIT);   // v2.2
		}

		void CommandContext::RecordGraphBarrier(ID3D12Resource* resource, D3D12_RESOURCE_STATES before, D3D12_RESOURCE_STATES after)
		{
			if (!_graphRecording || !resource) return;
			for (const auto& entry : _graphResourceMap)
			{
				if (entry.first != resource) continue;
				const uint32_t raw = entry.second;
				uint32_t id = raw & 0x7FFFFFFFu;
				if (id == (RHI::RenderTargetHandle::SCENE_DEPTH & 0x7FFFFFFFu)) id = RHI::RenderTargetHandle::SCENE_DEPTH;
				else if (id == (RHI::RENDER_GRAPH_MSAA_DEPTH & 0x7FFFFFFFu)) id = RHI::RENDER_GRAPH_MSAA_DEPTH;
				else if (id == (RHI::RenderTargetHandle::BACK_BUFFER & 0x7FFFFFFFu)) id = RHI::RenderTargetHandle::BACK_BUFFER;
				RHI::RenderGraphAccess access;
				access.Pass        = _passStack.empty() ? RHI::RENDER_GRAPH_NO_PASS : _passStack.back();
				access.ResourceId  = id;
				access.Kind        = RHI::RENDER_GRAPH_BARRIER | ((raw & 0x80000000u) != 0 ? RHI::RENDER_GRAPH_RESOLVED_COPY : 0u);
				access.StateBefore = static_cast<uint32_t>(before);
				access.StateAfter  = static_cast<uint32_t>(after);
				_graphAccesses.push_back(access);
				return;
			}
			// レンダーターゲット以外 (バッファ : カリング / スキニング / パーティクル) は記録しない
		}

		void CommandContext::finishGraphFrame()
		{
			if (!_graphRecording) return;
			_graphRecording = false;
			s_graphSink     = nullptr;

			// パス
			const auto& records = _passRecords[_frameIndex];
			_graphLastPasses.resize(records.size());
			for (size_t i = 0; i < records.size(); ++i)
			{
				_graphLastPasses[i].NameId = records[i].NameId;
				_graphLastPasses[i].Depth  = records[i].Depth;
				_graphLastPasses[i].Parent = records[i].Parent;
				_graphLastPasses[i].Flags  = 0;
			}

			// リソース : 生きているレンダーターゲット全部 (使っていない物も。Memory の窓) + 画面 / シーンの深度 / MSAA の深度 / 読んだテクスチャ
			_graphLastResources.clear();
			auto used = [&](uint32_t id)
			{
				for (const auto& access : _graphAccesses) if (access.ResourceId == id) return true;
				return false;
			};
			for (uint32_t i = 0; i < static_cast<uint32_t>(_renderTargets.size()); ++i)
			{
				const RenderTarget& target = _renderTargets[i];
				if (!target.Alive) continue;
				RHI::RenderGraphResource resource;
				resource.Id         = i;
				resource.DxgiFormat = static_cast<uint32_t>(target.IsDepth ? DXGI_FORMAT_D32_FLOAT : target.Format);
				resource.Width      = target.Width;
				resource.Height     = target.Height;
				resource.Samples    = target.SampleCount;
				resource.ArraySize  = target.ArraySize;
				resource.Bytes      = target.Bytes;
				if (target.IsDepth)        resource.Flags |= RHI::RENDER_GRAPH_RESOURCE_DEPTH;
				if (target.SampleCount > 1) resource.Flags |= RHI::RENDER_GRAPH_RESOURCE_MSAA;
				if (i == _viewTarget)      resource.Flags |= RHI::RENDER_GRAPH_RESOURCE_IMPORTED;
				if (used(i))               resource.Flags |= RHI::RENDER_GRAPH_RESOURCE_USED;
				if (!target.IsDepth && target.ArraySize == 1) resource.TextureId = GetRenderTargetTexture(i);
				_graphLastResources.push_back(resource);
			}
			{
				RHI::RenderGraphResource back;
				back.Id         = RHI::RenderTargetHandle::BACK_BUFFER;
				back.DxgiFormat = static_cast<uint32_t>(DXGI_FORMAT_R8G8B8A8_UNORM);
				back.Width      = _swapChain->GetWidth();
				back.Height     = _swapChain->GetHeight();
				back.Flags      = RHI::RENDER_GRAPH_RESOURCE_IMPORTED | (used(back.Id) ? RHI::RENDER_GRAPH_RESOURCE_USED : 0u);
				_graphLastResources.push_back(back);
			}
			ID3D12Device* device = _device->GetDevice()->GetDevice();
			if (_depthBuffer)
			{
				RHI::RenderGraphResource depth;
				depth.Id         = RHI::RenderTargetHandle::SCENE_DEPTH;
				depth.DxgiFormat = static_cast<uint32_t>(DXGI_FORMAT_D32_FLOAT);
				depth.Width      = _depthWidth;
				depth.Height     = _depthHeight;
				const D3D12_RESOURCE_DESC desc = _depthBuffer->Get()->GetDesc();
				depth.Bytes      = device->GetResourceAllocationInfo(0, 1, &desc).SizeInBytes;
				depth.Flags      = RHI::RENDER_GRAPH_RESOURCE_DEPTH | RHI::RENDER_GRAPH_RESOURCE_IMPORTED | (used(depth.Id) ? RHI::RENDER_GRAPH_RESOURCE_USED : 0u);
				_graphLastResources.push_back(depth);
			}
			if (_msaaDepth)
			{
				RHI::RenderGraphResource depth;
				depth.Id         = RHI::RENDER_GRAPH_MSAA_DEPTH;
				depth.DxgiFormat = static_cast<uint32_t>(DXGI_FORMAT_D32_FLOAT);
				depth.Width      = _depthWidth;
				depth.Height     = _depthHeight;
				depth.Samples    = _msaaDepthSamples;
				const D3D12_RESOURCE_DESC desc = _msaaDepth->GetDesc();
				depth.Bytes      = device->GetResourceAllocationInfo(0, 1, &desc).SizeInBytes;
				depth.Flags      = RHI::RENDER_GRAPH_RESOURCE_DEPTH | RHI::RENDER_GRAPH_RESOURCE_MSAA | (used(depth.Id) ? RHI::RENDER_GRAPH_RESOURCE_USED : 0u);
				_graphLastResources.push_back(depth);
			}
			for (const uint32_t texture : _graphTextureInputs)
			{
				RHI::RenderGraphResource input;
				input.Id        = texture;
				input.Flags     = RHI::RENDER_GRAPH_RESOURCE_TEXTURE | RHI::RENDER_GRAPH_RESOURCE_IMPORTED | RHI::RENDER_GRAPH_RESOURCE_USED;
				input.TextureId = texture & ~RHI::RenderTargetHandle::TEXTURE_INPUT;
				_graphLastResources.push_back(input);
			}

			_graphLastAccesses = _graphAccesses;
			_graphHasLast = true;
		}

		bool CommandContext::GetRenderGraph(RHI::RenderGraphPass* passes, uint32_t passCapacity, uint32_t* passCount,
		                                    RHI::RenderGraphResource* resources, uint32_t resourceCapacity, uint32_t* resourceCount,
		                                    RHI::RenderGraphAccess* accesses, uint32_t accessCapacity, uint32_t* accessCount)const
		{
			auto copy = [](const auto& source, auto* out, uint32_t capacity, uint32_t* count)
			{
				const uint32_t n = static_cast<uint32_t>(source.size());
				if (count) *count = n;
				if (out) for (uint32_t i = 0; i < n && i < capacity; ++i) out[i] = source[i];
			};
			copy(_graphLastPasses, passes, passCapacity, passCount);
			copy(_graphLastResources, resources, resourceCapacity, resourceCount);
			copy(_graphLastAccesses, accesses, accessCapacity, accessCount);
			return _graphHasLast;
		}

		uint32_t CommandContext::GetRenderTargetName(RHI::RenderTargetHandle handle, char* out, uint32_t size)const
		{
			if (out && size > 0) out[0] = 0;
			if (handle.Index >= _renderTargets.size() || !_renderTargets[handle.Index].Alive) return 0;
			const std::wstring& name = _renderTargets[handle.Index].Name;
			if (name.empty()) return 0;
			const int length = WideCharToMultiByte(CP_UTF8, 0, name.c_str(), static_cast<int>(name.size()), nullptr, 0, nullptr, nullptr);
			if (out && size > 1)
			{
				std::string utf8(static_cast<size_t>(length), '?');
				WideCharToMultiByte(CP_UTF8, 0, name.c_str(), static_cast<int>(name.size()), &utf8[0], length, nullptr, nullptr);
				const size_t n = (std::min)(utf8.size(), static_cast<size_t>(size - 1));
				std::memcpy(out, utf8.data(), n);
				out[n] = 0;
			}
			return static_cast<uint32_t>(length);
		}

		void CommandContext::prepareDebugReadTarget()
		{
			// Render Graph Viewer の窓が中身を UI で出すレンダーターゲット : UI (ピクセルシェーダー) が読める状態にする
			if (_debugReadTarget >= _renderTargets.size() || !_renderTargets[_debugReadTarget].Alive) return;
			RenderTarget& target = _renderTargets[_debugReadTarget];
			if (target.IsDepth || target.ArraySize != 1) return;   // 深度 / 配列は UI では読めない (窓は絵を出さない)
			const D3D12_RESOURCE_STATES readState = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
			auto* cmdList = _frameCommandList->GetCmdList();
			if (target.SampleCount > 1)
			{
				if (target.ResolveDirty) resolveTarget(target);
				if (target.Resolved && target.ResolvedState != readState)
				{
					D3D12_RESOURCE_BARRIER barrier = {};
					barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
					barrier.Transition.pResource   = target.Resolved.Get();
					barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
					barrier.Transition.StateBefore = target.ResolvedState;
					barrier.Transition.StateAfter  = readState;
					cmdList->ResourceBarrier(1, &barrier);
					target.ResolvedState = readState;
				}
				return;
			}
			if (target.State != readState)
			{
				D3D12_RESOURCE_BARRIER barrier = {};
				barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
				barrier.Transition.pResource   = target.Resource.Get();
				barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
				barrier.Transition.StateBefore = target.State;
				barrier.Transition.StateAfter  = readState;
				cmdList->ResourceBarrier(1, &barrier);
				target.State = readState;
			}
		}

		void CommandContext::beginPass(uint32_t nameId)
		{
			auto* cmdList = _frameCommandList->GetCmdList();

			// PIX / RenderDoc のイベント (Metadata 0 = UTF-16 の文字列。WinPixEventRuntime が無い時の PIXBeginEvent と同じ形)
			static const wchar_t UNKNOWN[] = L"Pass";
			if (nameId < _passNamesWide.size() && !_passNamesWide[nameId].empty())
			{
				const std::wstring& wide = _passNamesWide[nameId];
				cmdList->BeginEvent(0, wide.c_str(), static_cast<UINT>((wide.size() + 1) * sizeof(wchar_t)));
			}
			else
			{
				cmdList->BeginEvent(0, UNKNOWN, static_cast<UINT>(sizeof(UNKNOWN)));
			}

			auto& records = _passRecords[_frameIndex];
			PassRecord record;
			record.NameId = nameId;
			record.Depth  = static_cast<uint32_t>(_passStack.size());
			record.Parent = _passStack.empty() ? 0xFFFFFFFFu : _passStack.back();   // v1.12 P3
			record.Start  = std::chrono::steady_clock::now();
			// GPU のタイムスタンプ (計る時だけ。始め / 終わりの 2 つ。足りなくなったら以降のパスは CPU の時間だけ)
			uint32_t& used = _passTimestampsUsed[_frameIndex];
			if (_passTimed[_frameIndex] && used + 2 <= PASS_TIMESTAMP_MAX)
			{
				record.Timestamp = PASS_TIMESTAMP_FIRST + used;
				used += 2;
				_gpuTimer->Timestamp(cmdList, _frameIndex, record.Timestamp);
			}
			_passStack.push_back(static_cast<uint32_t>(records.size()));
			records.push_back(record);
		}

		void CommandContext::endPass()
		{
			if (_passStack.empty()) return;   // 始めていないのに終わり : 何もしない
			auto* cmdList = _frameCommandList->GetCmdList();
			PassRecord& record = _passRecords[_frameIndex][_passStack.back()];
			_passStack.pop_back();
			if (record.Timestamp != NO_TIMESTAMP) _gpuTimer->Timestamp(cmdList, _frameIndex, record.Timestamp + 1);
			record.CpuMs = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - record.Start).count();
			cmdList->EndEvent();
		}

		void CommandContext::closeOpenPasses()
		{
			while (!_passStack.empty()) endPass();
		}

		void CommandContext::resolvePassTimestamps()
		{
			if (!_gpuTimer) return;
			const UINT count = _passTimed[_frameIndex] ? _passTimestampsUsed[_frameIndex] : 0;
			_gpuTimer->ResolveRange(_frameCommandList->GetCmdList(), _frameIndex, PASS_TIMESTAMP_FIRST, count);
		}

		void CommandContext::collectPassResults()
		{
			// このフレームの番号で FRAME_COUNT フレーム前に記録したパス (GPU は使い終わっている)
			auto& records = _passRecords[_frameIndex];
			if (records.empty()) { _passTimed[_frameIndex] = false; _passTimestampsUsed[_frameIndex] = 0; return; }   // 記録が無かった (最初のフレームなど) : 前の結果のまま

			const UINT used = _passTimestampsUsed[_frameIndex];
			bool gpu = false;
			UINT64 frequency = 0;
			if (_passTimed[_frameIndex] && _gpuTimer && used > 0)
			{
				_passTicks.resize(used);
				gpu = _gpuTimer->ReadTicks(_frameIndex, PASS_TIMESTAMP_FIRST, used, _passTicks.data());
				frequency = _gpuTimer->GetFrequency();
			}

			_passResults.resize(records.size());
			for (size_t i = 0; i < records.size(); ++i)
			{
				const PassRecord& record = records[i];
				RHI::PassTiming& result = _passResults[i];
				result.NameId = record.NameId;
				result.Depth  = record.Depth;
				result.CpuMs  = static_cast<float>(record.CpuMs);
				result.GpuMs  = -1.0f;
				if (gpu && frequency > 0 && record.Timestamp != NO_TIMESTAMP)
				{
					const UINT local = record.Timestamp - PASS_TIMESTAMP_FIRST;
					const UINT64 begin = _passTicks[local];
					const UINT64 end   = _passTicks[local + 1];
					result.GpuMs = end > begin ? static_cast<float>(static_cast<double>(end - begin) * 1000.0 / static_cast<double>(frequency)) : 0.0f;
				}
			}

			// 読んだので空にする (このフレームで ExecuteRenderCommands が呼ばれなかった時に、古い数のタイムスタンプを読み戻さないように)
			records.clear();
			_passTimed[_frameIndex] = false;
			_passTimestampsUsed[_frameIndex] = 0;
		}
	}
}

/************************************
* End of file
************************************/
