
/***********************************************************************************
* @file    CommandContext_FrameDebug.cpp
* @brief   RHI CommandContext : Frame Debugger (v1.13 P4)
* @details 描画をイベント (消す / シーンの描画 1 回 / 全画面 / パーティクル / 自分のシェーダーの 1 個 …) に分けて数え、
*          StopEvent のイベントまで描いたら、その時の描画先を確認用の絵 (OutputTarget) に写して、残りの描画を飛ばす
*          (UI と後片付けは続ける)。Unity の Frame Debugger と同じく、毎フレーム同じフレームを描き直して止める方式
*
*          - 有効な間は ExecuteIndirect / GPU カリング / オクルージョンを使わず 1 個ずつ描く (描画ごとに止めるため。PrepareScene)
*          - 写す : PSFrameDebugView (frameDebug.shader) を全画面で描く。色の描画先 / シーンの深度を読める。
*                   画面 (Player のバックバッファ) と深度の配列 (シャドーマップ) は写せない (Status = 3)
*          - イベントの一覧は止めた所まで (C# は止めずに描いたフレームで全部の一覧を取っておく)
*
*          以前は描画の途中の絵を見る方法が無く、PIX / RenderDoc を使うしかなかった
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

#include <algorithm>
#include <cstring>

namespace RHI
{
	namespace ObjectInterface
	{
		void CommandContext::fdStartFrame(Buffer* /*buffer*/)
		{
			_fdActive  = _fdSettings.Enabled != 0;
			_fdStopHit = false;
			_fdStopped = false;
			_fdEvents.clear();
			_fdResult = RHI::FrameDebugResult();
			_fdResult.Status = 1;   // 止めていない
		}

		void CommandContext::fdFillTargets(RHI::FrameDebugEvent& e)const
		{
			e.ColorCount = (std::min)(_boundColorCount, 4u);
			for (uint32_t c = 0; c < 4; ++c) e.Colors[c] = c < e.ColorCount ? _boundColors[c] : 0xFFFFFFFFu;
			e.Depth = _boundDepth;   // MSAA の深度に描いている時も SCENE_DEPTH (写す時は 1 サンプルへ解決して読む)
		}

		bool CommandContext::fdEvent(RHI::FrameDebugEvent& e)
		{
			if (!_fdActive) return true;
			if (_fdStopHit || _fdStopped) return false;   // 止めた後 : 描かない
			e.Pass = _passStack.empty() ? RHI::RENDER_GRAPH_NO_PASS : _passStack.back();
			fdFillTargets(e);
			_fdEvents.push_back(e);
			if (_fdSettings.StopEvent != RHI::FRAME_DEBUG_NO_STOP && _fdEvents.size() - 1 == _fdSettings.StopEvent) _fdStopHit = true;
			return true;
		}

		void CommandContext::fdAfterCommand(Buffer* buffer)
		{
			if (!_fdActive || !_fdStopHit || _fdStopped) return;
			fdCapture(buffer);
			_fdStopped = true;
		}

		void CommandContext::fdCapture(Buffer* buffer)
		{
			_fdResult.Stopped = 1;
			if (_fdEvents.empty()) { _fdResult.Status = 2; return; }
			const RHI::FrameDebugEvent& e = _fdEvents.back();

			// 写す描画先 (イベントの時の描画先。Slot 0〜3 = 色 / 4 = 深度)
			uint32_t source = 0xFFFFFFFFu;
			bool     depth  = false;
			if (_fdSettings.Slot < 4) source = _fdSettings.Slot < e.ColorCount ? e.Colors[_fdSettings.Slot] : 0xFFFFFFFFu;
			else { source = e.Depth; depth = true; }
			_fdResult.Source = source;
			if (source == 0xFFFFFFFFu) { _fdResult.Status = 2; return; }
			if (source == RHI::RenderTargetHandle::BACK_BUFFER) { _fdResult.Status = 3; return; }   // 画面は SRV で読めない

			if (source == RHI::RenderTargetHandle::SCENE_DEPTH)
			{
				_fdResult.SourceWidth  = _depthWidth;
				_fdResult.SourceHeight = _depthHeight;
				_fdResult.SourceFormat = static_cast<uint32_t>(DXGI_FORMAT_D32_FLOAT);
			}
			else if (source < _renderTargets.size() && _renderTargets[source].Alive)
			{
				const RenderTarget& target = _renderTargets[source];
				if (target.ArraySize > 1) { _fdResult.Status = 3; return; }   // 深度の配列 (シャドーマップ) は全画面のシェーダーで読めない
				_fdResult.SourceWidth  = target.Width;
				_fdResult.SourceHeight = target.Height;
				_fdResult.SourceFormat = static_cast<uint32_t>(target.IsDepth ? DXGI_FORMAT_D32_FLOAT : target.Format);
				depth = depth || target.IsDepth;
			}
			else { _fdResult.Status = 2; return; }

			const uint32_t output = _fdSettings.OutputTarget;
			if (_fdSettings.ViewShader == 0xFFFFFFFFu || output >= _renderTargets.size() || !_renderTargets[output].Alive || output == source)
			{
				_fdResult.Status = 2;
				return;
			}

			// 確認用の絵へ全画面で写す (描画先を替えるので、写した後は描かない = 残りは飛ばす)
			RHI::RenderCommand set;
			set.Type       = RHI::RenderCommandType::SetRenderTargets;
			set.ColorCount = 1;
			set.Colors[0]  = output;
			set.Depth      = RHI::RenderTargetHandle::INVALID;
			setRenderTargets(set);

			RHI::RenderCommand view;
			view.Type = RHI::RenderCommandType::DrawFullscreen;
			view.Arg0 = _fdSettings.ViewShader;
			view.Arg1 = 0;   // ブレンドなし
			for (uint32_t i = 0; i < RHI::MAX_PASS_INPUTS; ++i) view.Inputs[i] = RHI::RenderTargetHandle::INVALID;
			view.Inputs[0] = source;
			view.Params[0] = _fdSettings.LevelMin;
			view.Params[1] = _fdSettings.LevelMax;
			view.Params[2] = static_cast<float>(_fdSettings.ChannelMask & 0xFu);
			view.Params[3] = depth ? 1.0f : 0.0f;
			drawFullscreen(buffer, view);

			_fdResult.Captured = 1;
			_fdResult.Status   = 0;
		}

		void CommandContext::fdFinishFrame()
		{
			if (!_fdActive) return;
			const auto& records = _passRecords[_frameIndex];
			_fdLastPasses.resize(records.size());
			for (size_t i = 0; i < records.size(); ++i)
			{
				_fdLastPasses[i].NameId = records[i].NameId;
				_fdLastPasses[i].Depth  = records[i].Depth;
				_fdLastPasses[i].Parent = records[i].Parent;
				_fdLastPasses[i].Flags  = 0;
			}
			_fdResult.EventCount = static_cast<uint32_t>(_fdEvents.size());
			_fdLastEvents = _fdEvents;
			_fdLastResult = _fdResult;
			_fdHasLast    = true;
		}

		void CommandContext::fdBuildArgObjects(const DrawFrame& frame)
		{
			// 描画の引数の番号 → その描画に入っている最初の DrawItem の物とマテリアル (インスタンシングでまとめた時は最初の 1 つ)
			_fdArgObjects.assign(frame.Written, { 0xFFFFFFFFu, 0xFFFFFFFFu });
			const auto& items = frame.LastItems;
			const size_t count = (std::min)(items.size(), _itemBatch.size());
			for (size_t i = 0; i < count; ++i)
			{
				const uint32_t batch = _itemBatch[i];
				if (batch >= _batches.size()) continue;   // 描かない物 / 自分のシェーダーの物
				const uint32_t draw = _batches[batch].Draw;
				if (draw < _fdArgObjects.size() && _fdArgObjects[draw].first == 0xFFFFFFFFu)
				{
					_fdArgObjects[draw] = { items[i].Object.Index, items[i].Material.Index };
				}
			}
		}

		bool CommandContext::GetFrameDebugEvents(RHI::FrameDebugEvent* events, uint32_t eventCapacity, uint32_t* eventCount,
		                                         RHI::RenderGraphPass* passes, uint32_t passCapacity, uint32_t* passCount,
		                                         RHI::FrameDebugResult* result)const
		{
			const uint32_t ne = static_cast<uint32_t>(_fdLastEvents.size());
			const uint32_t np = static_cast<uint32_t>(_fdLastPasses.size());
			if (eventCount) *eventCount = ne;
			if (passCount)  *passCount  = np;
			if (events) for (uint32_t i = 0; i < ne && i < eventCapacity; ++i) events[i] = _fdLastEvents[i];
			if (passes) for (uint32_t i = 0; i < np && i < passCapacity; ++i) passes[i] = _fdLastPasses[i];
			if (result) *result = _fdLastResult;
			return _fdHasLast;
		}
	}
}

/************************************
* End of file
************************************/
