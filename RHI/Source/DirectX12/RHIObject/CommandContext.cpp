
/***********************************************************************************
* @file    CommandContext.cpp
* @brief   RHI CommandContext
* @details
*
* @author  Keigo Onari
* @date    2026/4/27
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
#include "CommandContext.h"

// 必要なクラスをインクルード
// ========================================
#include "Device.h"
#include "Pipeline.h"
#include "Buffer.h"
// ========================================

// DirectX12 APIのラッパーをインクルード
// ========================================
#include "../../DXGI/dxgiFactory6.h"
#include "../Object/dx12_device.h"
#include "../Object/dx12_graphicsCommandList.h"
#include "../Object/dx12_commandQueue.h"
#include "../Object/dx12_commandAllocator.h"
#include "../Object/dx12_swapChain.h"
#include "../Object/dx12_resourceBuffer.h"
#include "../Object/dx12_descriptorHeap.h"
#include "../dx12_descriptorHeapManager.h"
#include "../dx12_uploadManager.h"
#include "../Object/dx12_fence.h"
#include "../Object/dx12_gpuTimer.h"
#include "../Object/dx12_commandSignature.h"

#include <cstring>
#include <algorithm>
// ========================================

#include <d3d12.h>
#include <chrono>
#include <string>

// 外部クラスをインクルード
// ========================================
#include "../../Core/Source/Log/logger.h"
// ========================================

/******************************************************************
* Macro Definitions
******************************************************************/


/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/



namespace RHI
{
	namespace ObjectInterface
	{
		namespace
		{
			// 遷移バリアを1つ作る (同じ設定を何度も書かないためのヘルパー)
			D3D12_RESOURCE_BARRIER makeTransitionBarrier(ID3D12Resource* resource, D3D12_RESOURCE_STATES before, D3D12_RESOURCE_STATES after)
			{
				D3D12_RESOURCE_BARRIER barrier = {};
				barrier.Type                   = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
				barrier.Flags                  = D3D12_RESOURCE_BARRIER_FLAG_NONE;
				barrier.Transition.pResource   = resource;
				barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
				barrier.Transition.StateBefore = before;
				barrier.Transition.StateAfter  = after;
				return barrier;
			}

			// HRESULTをログに出す (失敗箇所を追えるように)
			void logFailed(const char* what, HRESULT hr)
			{
				char message[256] = {};
				sprintf_s(message, "[CommandContext] %s failed. HRESULT = 0x%08X", what, static_cast<unsigned int>(hr));
				Core::Logger::Log(message);
			}

			// C# の RenderTextureFormat → DXGI の形式
			DXGI_FORMAT toDxgiFormat(RHI::RenderTargetFormat format)
			{
				switch (format)
				{
				case RHI::RenderTargetFormat::RGBA8:      return DXGI_FORMAT_R8G8B8A8_UNORM;
				case RHI::RenderTargetFormat::RGBA8_SRGB: return DXGI_FORMAT_R8G8B8A8_UNORM_SRGB;   // 書く時に sRGB へ、読む時に線形へ (ハードウェアが変換)
				case RHI::RenderTargetFormat::RGBA16F:    return DXGI_FORMAT_R16G16B16A16_FLOAT;
				case RHI::RenderTargetFormat::RG16F:      return DXGI_FORMAT_R16G16_FLOAT;
				case RHI::RenderTargetFormat::R16F:       return DXGI_FORMAT_R16_FLOAT;
				case RHI::RenderTargetFormat::R32F:       return DXGI_FORMAT_R32_FLOAT;
				case RHI::RenderTargetFormat::R8:         return DXGI_FORMAT_R8_UNORM;
				case RHI::RenderTargetFormat::RGB10A2:    return DXGI_FORMAT_R10G10B10A2_UNORM;
				case RHI::RenderTargetFormat::R11G11B10F: return DXGI_FORMAT_R11G11B10_FLOAT;
				case RHI::RenderTargetFormat::Depth32:    return DXGI_FORMAT_R32_TYPELESS;   // DSV (D32_FLOAT) と SRV (R32_FLOAT) で使う
				}
				return DXGI_FORMAT_UNKNOWN;
			}

			using IndirectDrawCommand = RHI::DirectX12::CommandSignature::IndirectDrawCommand;

			// GPU のタイムスタンプの番号
			constexpr UINT TIMESTAMP_FRAME_BEGIN = 0;
			constexpr UINT TIMESTAMP_SCENE_END   = 1;
			constexpr UINT TIMESTAMP_FRAME_END   = 2;
			constexpr UINT TIMESTAMP_COUNT       = 3;

			double elapsedMs(std::chrono::steady_clock::time_point from)
			{
				return std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - from).count();
			}
		}

		CommandContext::CommandContext() = default;
		CommandContext::~CommandContext()
		{
			// GPUが使用中のオブジェクトを破棄しないよう、念のため待つ
			Dispose();
		}

		RHI_RESULT CommandContext::Initialize(RHI::ObjectInterface::IDevice* iDevice, ObjectInfo* info)
		{
			if (!iDevice || !info)
			{
				return RHI_FAILED_INVALID_ARGUMENT;
			}

			// TODO : Phase 3 で抽象化を見直すまでは具象型へのキャストで扱う
			auto device = static_cast<RHI::ObjectInterface::Device*>(iDevice);
			if (!device->GetDevice() || !device->GetDevice()->GetDevice())
			{
				// Device の初期化に失敗している
				return RHI_FAILED_INVALID_ARGUMENT;
			}
			_device = device;
			ID3D12Device* dx12Device = device->GetDevice()->GetDevice();

			// ---------- キューとフェンス ----------
			_commandQueue = std::make_unique<DirectX12::CommandQueue>();
			HRESULT hr = _commandQueue->Create(dx12Device);
			if (FAILED(hr)) { logFailed("CreateCommandQueue", hr); return RHI_FAILED_CREATE_ANY_OBJECT; }

			_fence = std::make_unique<DirectX12::Fence>();
			hr = _fence->Create(dx12Device);
			if (FAILED(hr)) { logFailed("CreateFence", hr); return RHI_FAILED_CREATE_ANY_OBJECT; }

			// ---------- フレーム用 : アロケーターはフレームの数だけ、コマンドリストは 1 本 ----------
			// (コマンドリストは ExecuteCommandLists の直後に別のアロケーターでリセットして使い回せる)
			for (auto& frame : _frames)
			{
				frame.Allocator = std::make_unique<DirectX12::CommandAllocator>();
				hr = frame.Allocator->Create(dx12Device);
				if (FAILED(hr)) { logFailed("CreateCommandAllocator(Frame)", hr); return RHI_FAILED_CREATE_ANY_OBJECT; }
				frame.FenceValue = 0;
			}

			_frameCommandList = std::make_unique<DirectX12::GraphicsCommandList>();
			hr = _frameCommandList->Create(dx12Device, _frames[0].Allocator->Get());
			if (FAILED(hr)) { logFailed("CreateCommandList(Frame)", hr); return RHI_FAILED_CREATE_ANY_OBJECT; }

			// 生成直後は記録中なので閉じておく (RenderStart で Reset するため)
			_frameCommandList->Close();

			// ---------- アップロード (COPY キュー + リングバッファ) ----------
			_uploadManager = std::make_unique<DirectX12::UploadManager>();
			hr = _uploadManager->Initialize(dx12Device);
			if (FAILED(hr)) { logFailed("UploadManager::Initialize", hr); return RHI_FAILED_CREATE_ANY_OBJECT; }
			_uploadFenceWaited = 0;

			// ---------- スワップチェーン ----------
			HWND hWnd   = static_cast<HWND>(info->HWnd);
			UINT width  = static_cast<UINT>(info->Width);
			UINT height = static_cast<UINT>(info->Height);

			// VSync設定 (以前は無視して常に Present(1, 0) だった : 2-5)
			_syncInterval = info->EnabledVSync ? 1 : 0;

			// テアリングは対応環境でのみ有効にする (VSync OFF 時にフレームレートの上限が外れる)
			const bool allowTearing = device->GetDxgiFactory()->IsTearingSupported();

			_swapChain = std::make_unique<DirectX12::SwapChain>();
			hr = _swapChain->Create(device->GetDxgiFactory()->Get(), _commandQueue->Get(), &hWnd, width, height, allowTearing, FRAME_COUNT);
			if (FAILED(hr)) { logFailed("CreateSwapChain", hr); return RHI_FAILED_CREATE_ANY_OBJECT; }

			// ---------- 描画先 (RTV / DSV) ----------
			_rtvHeap = std::make_unique<DirectX12::DescriptorHeap>();
			hr = _rtvHeap->Create(dx12Device, D3D12_DESCRIPTOR_HEAP_TYPE_RTV, DirectX12::SwapChain::BUFFER_COUNT);
			if (FAILED(hr)) { logFailed("CreateDescriptorHeap(RTV)", hr); return RHI_FAILED_CREATE_ANY_OBJECT; }

			_dsvHeap = std::make_unique<DirectX12::DescriptorHeap>();
			hr = _dsvHeap->Create(dx12Device, D3D12_DESCRIPTOR_HEAP_TYPE_DSV, 2);   // 0 = シーンの深度 / 1 = MSAA の深度 (以前は 1 個)
			if (FAILED(hr)) { logFailed("CreateDescriptorHeap(DSV)", hr); return RHI_FAILED_CREATE_ANY_OBJECT; }

			if (!createRenderTargetViews())     return RHI_FAILED_CREATE_ANY_OBJECT;
			if (!createDepthBuffer(width, height)) return RHI_FAILED_CREATE_ANY_OBJECT;

			// MSAA で使えるサンプル数 (Forward が使う HDR / 環境光 / 深度 がどれも対応している数)
			_maxSampleCount = 1;
			for (uint32_t samples = 8; samples >= 2; samples /= 2)
			{
				if (supportedSampleCount(Pipeline::HDR_FORMAT, samples) == samples &&
					supportedSampleCount(DXGI_FORMAT_R11G11B10_FLOAT, samples) == samples &&
					supportedSampleCount(DEPTH_FORMAT, samples) == samples)
				{
					_maxSampleCount = samples;
					break;
				}
			}

			// ---------- 描画ごとのデータと ExecuteIndirect の引数 (フレームごと) ----------
			// シェーダーが描画番号で DrawData を読むので、通常の描画でも必須
			{
				static_assert(sizeof(IndirectDrawCommand) == sizeof(D3D12_DRAW_INDEXED_ARGUMENTS), "IndirectDrawCommand must be DRAW_INDEXED_ARGUMENTS only");
				const UINT64 size       = CULL_DATA_OFFSET + static_cast<UINT64>(MAX_DRAWS) * sizeof(CullInstance);
				const UINT64 culledSize = COMPACT_ARGS_OFFSET + DRAW_ARGS_SIZE;     // カウンターと詰めた引数を含む
				for (auto& frame : _draws)
				{
					frame = DrawFrame();
					frame.Staging = std::make_unique<DirectX12::ResourceBuffer>();
					frame.Gpu     = std::make_unique<DirectX12::ResourceBuffer>();
					if (FAILED(frame.Staging->Create(dx12Device, size, D3D12_HEAP_TYPE_UPLOAD)) || FAILED(frame.Staging->Map()) ||
						FAILED(frame.Gpu->Create(dx12Device, size, D3D12_HEAP_TYPE_DEFAULT)))
					{
						Core::Logger::Log("[CommandContext] Failed create draw data buffer.");
						return RHI_FAILED_CREATE_ANY_OBJECT;
					}
					frame.Cpu = static_cast<uint8_t*>(frame.Staging->GetMappedData());
					frame.Args.reserve(1024);

					// GPU カリングの結果 (作れなくてもカリングしないだけ)
					frame.Culled   = std::make_unique<DirectX12::ResourceBuffer>();
					frame.Readback = std::make_unique<DirectX12::ResourceBuffer>();
					if (FAILED(frame.Culled->Create(dx12Device, culledSize, D3D12_HEAP_TYPE_DEFAULT, D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS)) ||
						FAILED(frame.Readback->Create(dx12Device, 32, D3D12_HEAP_TYPE_READBACK)))
					{
						Core::Logger::Log("[CommandContext] Failed create culling buffer. GPU culling disabled.");
						frame.Culled.reset();
						frame.Readback.reset();
					}
				}
			}

			// ---------- オクルージョンカリングの可視の記録 (作れなくてもオクルージョンカリングしないだけ) ----------
			_visibility = std::make_unique<DirectX12::ResourceBuffer>();
			if (FAILED(_visibility->Create(dx12Device, static_cast<UINT64>(MAX_DRAWS) * sizeof(uint32_t), D3D12_HEAP_TYPE_DEFAULT,
				D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS)))
			{
				Core::Logger::Log("[CommandContext] Failed create visibility buffer. Occlusion culling disabled.");
				_visibility.reset();
			_depthSrvIndex = 0xFFFFFFFFu;   // ヒープは Buffer が持っているので、番号だけ忘れる (Buffer と一緒に作り直される)
			_depthSrvDirty = true;
			}
			_visibilityValid = false;

			// ---------- 描画番号バッファ (0, 1, 2, ...) : 中身は変わらないので VRAM に 1 回だけ転送する ----------
			{
				const UINT64 size = static_cast<UINT64>(MAX_DRAWS) * sizeof(uint32_t);
				_drawIdBuffer = std::make_unique<DirectX12::ResourceBuffer>();
				DirectX12::UploadManager::Allocation allocation;
				ID3D12GraphicsCommandList* copyList = nullptr;
				if (FAILED(_drawIdBuffer->Create(dx12Device, size, D3D12_HEAP_TYPE_DEFAULT)) ||
					!_uploadManager->Allocate(size, 16, allocation) || !(copyList = _uploadManager->GetCommandList()))
				{
					Core::Logger::Log("[CommandContext] Failed create draw id buffer.");
					return RHI_FAILED_CREATE_ANY_OBJECT;
				}
				uint32_t* ids = reinterpret_cast<uint32_t*>(allocation.Cpu);
				for (uint32_t i = 0; i < MAX_DRAWS; ++i) ids[i] = i;
				// COPY キューで転送する。最初のフレームの送信前に SubmitUploads で送られ、描画キューはその完了を待つ
				// (バリアは不要 : COMMON → COPY_DEST → 完了後 COMMON → 描画キューで頂点バッファの状態に暗黙で昇格)
				copyList->CopyBufferRegion(_drawIdBuffer->Get(), 0, allocation.Resource, allocation.Offset, size);
			}

			// ---------- GPU の時間の計測 (失敗してもプロファイラーの GPU 欄が出ないだけ) ----------
			_gpuTimer = std::make_unique<DirectX12::GpuTimer>();
			hr = _gpuTimer->Initialize(dx12Device, _commandQueue->Get(), FRAME_COUNT);
			if (FAILED(hr)) { logFailed("GpuTimer::Initialize", hr); _gpuTimer.reset(); }

			_allowTearing        = allowTearing;
			_stats.VSync          = _syncInterval != 0;
			_stats.Tearing        = allowTearing && _syncInterval == 0;
			_stats.FramesInFlight = FRAME_COUNT;

#ifdef CONSOLE_DEBUG
			char message[128] = {};
			sprintf_s(message, "[CommandContext] %ux%u, VSync %s, Tearing %s, Frames in flight %u",
				width, height, _syncInterval ? "ON" : "OFF", allowTearing ? "Supported" : "Not Supported", FRAME_COUNT);
			Core::Logger::Log(message);
#endif

			return RHI_SUCCEEDED;
		}

		// ==============================================================
		// フレーム描画
		// ==============================================================

		bool CommandContext::RenderStart(Pipeline* pipeline, Buffer* buffer)
		{
			if (!pipeline || !buffer || !_swapChain || !_swapChain->Get())
			{
				return false;
			}

			const auto waitStart = std::chrono::steady_clock::now();

			// 1. CPU が先行しすぎていれば待つ (表示待ちのフレームが FRAME_COUNT 個を超えないように)
			_swapChain->WaitForNextFrame();

			// 2. このフレームの FrameContext を GPU が使い終わるまで待つ
			//    FRAME_COUNT フレーム前の処理なので、通常はすでに終わっていて待たない
			FrameContext& frame = _frames[_frameIndex];
			HRESULT hr = _fence->WaitForValue(frame.FenceValue);
			if (FAILED(hr)) { logFailed("Fence::WaitForValue", hr); return false; }
			_stats.WaitMs = elapsedMs(waitStart);

			// GPU が使い終わったので、FRAME_COUNT フレーム前に計測した GPU の時間を読める
			if (_gpuTimer)
			{
				double intervals[TIMESTAMP_COUNT - 1] = {};
				if (_gpuTimer->ReadIntervals(_frameIndex, TIMESTAMP_COUNT - 1, intervals))
				{
					_stats.GpuValid   = true;
					_stats.GpuSceneMs = intervals[0];
					_stats.GpuUiMs    = intervals[1];
					_stats.GpuTotalMs = intervals[0] + intervals[1];
				}
			}
			_stats.Width  = _swapChain->GetWidth();
			_stats.Height = _swapChain->GetHeight();

			// GPU カリングで映った数 (FRAME_COUNT フレーム前の結果。GPU は使い終わっているので読める)
			{
				DrawFrame& draws = _draws[_frameIndex];
				_stats.VisibleValid = false;
				if (draws.CullRecorded && draws.Readback)
				{
					void* mapped = nullptr;
					const D3D12_RANGE readRange = { 0, sizeof(uint32_t) * 8 };
					if (SUCCEEDED(draws.Readback->Get()->Map(0, &readRange, &mapped)))
					{
						// [0〜3] : 1 回目 (視錐台だけ / 前段) の映ったオブジェクト数・描画数 (不透明 / 半透明 / 両面の不透明)、[4〜7] : 後段
						const uint32_t* counters = static_cast<const uint32_t*>(mapped);
						const bool two = draws.CullPhases == 2;
						_stats.VisibleInstances = counters[0] + (two ? counters[4] : 0);
						_stats.VisibleDraws     = counters[1] + counters[2] + counters[3] + (two ? counters[5] + counters[6] + counters[7] : 0);
						_stats.VisibleValid     = true;
						const D3D12_RANGE noWrite = { 0, 0 };
						draws.Readback->Get()->Unmap(0, &noWrite);
					}
				}
				draws.CullRecorded = false;
			}
			// バッファは前のフレームの ExecuteCommandLists の完了で COMMON に戻っている
			_hiZState        = D3D12_RESOURCE_STATE_COMMON;
			_visibilityState = D3D12_RESOURCE_STATE_COMMON;

			// 3. アロケーター → コマンドリストの順にリセット (GPU が使い終わったので安全)
			hr = frame.Allocator->Reset();
			if (FAILED(hr)) { logFailed("CommandAllocator::Reset(Frame)", hr); return false; }

			hr = _frameCommandList->Reset(frame.Allocator->Get(), pipeline->GetPipelineState());
			if (FAILED(hr)) { logFailed("CommandList::Reset(Frame)", hr); return false; }

			auto cmdList = _frameCommandList->GetCmdList();
			_currentPipeline = pipeline;

			// GPU のタイムスタンプ : フレームの開始
			if (_gpuTimer) _gpuTimer->Timestamp(cmdList, _frameIndex, TIMESTAMP_FRAME_BEGIN);

			// 4. ビューポートとシザー矩形の設定 (スワップチェーンのサイズに合わせる)
			const UINT width  = _swapChain->GetWidth();
			const UINT height = _swapChain->GetHeight();
			const D3D12_VIEWPORT viewport = { 0.0f, 0.0f, static_cast<float>(width), static_cast<float>(height), 0.0f, 1.0f };
			const D3D12_RECT scissorRect  = { 0, 0, static_cast<LONG>(width), static_cast<LONG>(height) };
			cmdList->RSSetViewports(1, &viewport);
			cmdList->RSSetScissorRects(1, &scissorRect);

			// 5. リソースバリア (Present -> RenderTarget)
			const uint32_t backBufferIndex = _swapChain->GetCurrentBackBufferIndex();
			_currentBackBuffer = _swapChain->GetBufferFromIndex(backBufferIndex);
			if (!_currentBackBuffer)
			{
				// コマンドリストは記録中のまま。閉じておかないと次の Reset が失敗する
				_frameCommandList->Close();
				return false;
			}
			const auto toRenderTarget = makeTransitionBarrier(_currentBackBuffer, D3D12_RESOURCE_STATE_PRESENT, D3D12_RESOURCE_STATE_RENDER_TARGET);
			cmdList->ResourceBarrier(1, &toRenderTarget);

			// 6. 描画先の設定とクリアは、C# の描画パス (SetRenderTargets / ClearColor / ClearDepth) が行う
			//    (以前はここでバックバッファと深度を設定してクリアしていた。描画先が 1 つしか無かった)
			//    深度は前のフレームの終わりに DEPTH_WRITE に戻してある
			_depthState      = D3D12_RESOURCE_STATE_DEPTH_WRITE;
			_boundColorCount = 0;
			_boundDepth      = RHI::RenderTargetHandle::INVALID;
			buffer->SetScreenSize(width, height);   // 全画面のシェーダーが使う (フレームの定数に入る)

			// ==============================================================
			// 7. ルートシグネチャとヒープのセット
			// ==============================================================
			cmdList->SetGraphicsRootSignature(pipeline->GetRootSignature());

			// メガVBやテクスチャのSRVが登録されているヒープをセット
			auto heapManager = buffer->GetDescHeapManager();
			ID3D12DescriptorHeap* heaps[] = { heapManager->GetSrvHeap() };
			cmdList->SetDescriptorHeaps(1, heaps);

			// メガVBのSRVテーブル (t0)
			cmdList->SetGraphicsRootDescriptorTable(Pipeline::ROOT_PARAM_VERTEX_BUFFER_TABLE, heapManager->GetSrvGpuHandle(Buffer::SRV_INDEX_SCENE_VB));

			// Global定数バッファ (b1) : このフレーム専用の領域に View / Projection を書いてからセットする
			// (GPU が前のフレームで読んでいる領域を CPU が上書きしないよう、フレームごとに領域を分けている)
			cmdList->SetGraphicsRootConstantBufferView(Pipeline::ROOT_PARAM_GLOBAL_CBV, buffer->WriteFrameConstants(_frameIndex));

			// バインドレス・テクスチャ配列 (t1)
			// テーブルの先頭 = テクスチャの0番。シェーダー側の g_TextureID はここからの相対番号になる
			cmdList->SetGraphicsRootDescriptorTable(Pipeline::ROOT_PARAM_TEXTURE_TABLE, heapManager->GetSrvGpuHandle(Buffer::SRV_INDEX_TEXTURE_BEGIN));

			// インデックスバッファを使用
			cmdList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);

			D3D12_INDEX_BUFFER_VIEW ibView = {};
			ibView.BufferLocation = buffer->GetSceneIB()->GetGPUVirtualAddress();
			ibView.SizeInBytes    = static_cast<UINT>(buffer->GetSceneIndexBufferSize());
			ibView.Format         = DXGI_FORMAT_R32_UINT;
			cmdList->IASetIndexBuffer(&ibView);

			// 描画番号バッファ (インスタンス単位の入力 DRAWID)。StartInstanceLocation の位置から読まれる
			// (ImGui が自分の頂点バッファに差し替えるので、毎フレームここで設定し直す)
			D3D12_VERTEX_BUFFER_VIEW drawIdView = {};
			drawIdView.BufferLocation = _drawIdBuffer->GetGPUVirtualAddress();
			drawIdView.SizeInBytes    = MAX_DRAWS * sizeof(uint32_t);
			drawIdView.StrideInBytes  = sizeof(uint32_t);
			cmdList->IASetVertexBuffers(0, 1, &drawIdView);

			return true;
		}

		bool CommandContext::RenderEnd()
		{
			if (!_currentBackBuffer || !_frameCommandList->IsRecording())
			{
				// RenderStart が失敗している
				return false;
			}

			// GPU のタイムスタンプ : フレームの終わり → 読み戻し用バッファへ移す
			if (_gpuTimer)
			{
				_gpuTimer->Timestamp(_frameCommandList->GetCmdList(), _frameIndex, TIMESTAMP_FRAME_END);
				_gpuTimer->Resolve(_frameCommandList->GetCmdList(), _frameIndex, TIMESTAMP_COUNT);
			}

			// 8. リソースバリア (RenderTarget -> Present)
			const auto toPresent = makeTransitionBarrier(_currentBackBuffer, D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_PRESENT);
			_frameCommandList->GetCmdList()->ResourceBarrier(1, &toPresent);
			_currentBackBuffer = nullptr;

			// 9. コマンドリストのクローズと実行
			HRESULT hr = _frameCommandList->Close();
			if (FAILED(hr)) { logFailed("CommandList::Close(Frame)", hr); return false; }

			// このフレームで使うデータのアップロードを送信し、描画キューに GPU 上で完了を待たせる
			// (CPU は待たない。COPY キューと描画キューは並行して動く)
			SubmitUploads();

			ID3D12CommandList* ppCommandLists[] = { _frameCommandList->GetCmdList() };
			_commandQueue->Get()->ExecuteCommandLists(1, ppCommandLists);

			// 10. 画面への表示
			// 最小化中などは DXGI_STATUS_OCCLUDED (成功コード) が返る
			const auto presentStart = std::chrono::steady_clock::now();
			hr = _swapChain->Present(_syncInterval);
			_stats.PresentMs = elapsedMs(presentStart);
			if (FAILED(hr))
			{
				logDeviceRemovedReason(hr);
				return false;
			}

			// 11. このフレームの完了の目印をシグナルして、次のフレームへ
			//     ここでは待たない (Phase 0 までは毎フレーム GPU の完了を待っていた : 2-3)
			_frames[_frameIndex].FenceValue = _fence->Signal(_commandQueue->Get());
			_frameIndex = (_frameIndex + 1) % FRAME_COUNT;

			return true;
		}

		// ==============================================================
		// シーンの準備 / 描画パスの実行
		// ==============================================================

		void CommandContext::PrepareScene(Buffer* buffer, const RHI::DrawItem* items, uint32_t count)
		{
			_scene = SceneState();
			if (!buffer || !_frameCommandList->IsRecording() || (!items && count > 0))
			{
				return;
			}

			auto cmdList = _frameCommandList->GetCmdList();
			DrawFrame& frame = _draws[_frameIndex];

			// オブジェクトの定数を、このフレーム用の領域に書いてからその先頭を Root SRV に設定する
			// (変わったオブジェクトだけ書く。GPU が前のフレームで読んでいる領域には触らない)
			// 以前は描画ごとに Root CBV でオブジェクトの位置を設定していた
			// (この先頭アドレスは GPU カリングでも使う)
			_scene.ObjectData = buffer->WriteObjectConstants(_frameIndex, cmdList);
			cmdList->SetGraphicsRootShaderResourceView(Pipeline::ROOT_PARAM_OBJECT_DATA_SRV, _scene.ObjectData);

			// マテリアル / ライト (増えた / 変わった時だけ VRAM へ写す)
			cmdList->SetGraphicsRootShaderResourceView(Pipeline::ROOT_PARAM_MATERIAL_SRV, buffer->WriteMaterials(_frameIndex, cmdList));
			cmdList->SetGraphicsRootShaderResourceView(Pipeline::ROOT_PARAM_LIGHT_SRV,    buffer->WriteLights(_frameIndex, cmdList));

			// 描画ごとのデータ (DrawData) と引数を用意する (描画リストが同じなら前回のものを使い回す)
			_scene.Reused = updateDraws(frame, buffer, items, count);

			ID3D12CommandSignature* signature = _currentPipeline ? _currentPipeline->GetIndirectDrawSignature() : nullptr;
			_scene.Indirect = _useIndirectDraw && signature;
			// GPU カリング : ExecuteIndirect の時だけ (結果の引数を GPU が作るので、1 個ずつ描く時は使えない)
			_scene.Culling  = _scene.Indirect && _useGpuCulling && frame.Culled && frame.Written > 0 &&
			                  _currentPipeline->IsGpuCullingAvailable();
			// オクルージョンカリング (2 段階) : GPU カリングが前提
			_scene.Occlusion = _scene.Culling && _useOcclusion && _hiZ && _visibility && _currentPipeline->IsHiZAvailable();
			_scene.Valid     = true;

			_stats.IndirectDraw       = _scene.Indirect;
			_stats.IndirectArgsReused = _scene.Reused;
			_stats.DrawCalls          = frame.GroupCount[0] + frame.GroupCount[1] + frame.GroupCount[2];
			_stats.Instances          = frame.Instances;
			_stats.Instancing         = frame.Instanced;
			_stats.GpuCulling         = _scene.Culling;
			_stats.Triangles          = frame.Triangles;
		}

		void CommandContext::ExecuteRenderCommands(Buffer* buffer, const RHI::RenderCommand* commands, uint32_t count)
		{
			if (!buffer || !_frameCommandList->IsRecording() || (!commands && count > 0))
			{
				return;
			}
			auto cmdList = _frameCommandList->GetCmdList();
			_msaaDepthDirty = false;   // MSAA の深度は、このフレームで描いた時だけ解決する
			_culledView     = 0xFFFFFFFFu;                       // カリングの結果はフレームごと
			_culledState    = D3D12_RESOURCE_STATE_COMMON;       // 前のフレームの完了で COMMON に戻っている
			_sceneDepthReadLastFrame = _sceneDepthReadThisFrame;
			_sceneDepthReadThisFrame = false;

			for (uint32_t i = 0; i < count; ++i)
			{
				const RHI::RenderCommand& command = commands[i];
				switch (command.Type)
				{
				case RHI::RenderCommandType::SetRenderTargets:
					setRenderTargets(command);
					break;

				case RHI::RenderCommandType::ClearColor:
					// 最適化クリア値と同じ色なら高速クリアになる (RenderTargetDesc.ClearColor)
					for (uint32_t c = 0; c < _boundColorCount; ++c)
					{
						cmdList->ClearRenderTargetView(_boundRtvs[c], command.ClearColor, 0, nullptr);
					}
					break;

				case RHI::RenderCommandType::ClearDepth:
					if (_boundDepth != RHI::RenderTargetHandle::INVALID)   // シーンの深度 / 深度のレンダーターゲット
					{
						cmdList->ClearDepthStencilView(boundDsv(), D3D12_CLEAR_FLAG_DEPTH, command.ClearDepth, 0, 0, nullptr);
					}
					break;

				case RHI::RenderCommandType::DrawScene:
					drawScene(buffer, command.Arg0, command.Arg1, command.Inputs[0]);   // Inputs[0] = 視点 (0 カメラ / 1〜 影)
					break;

				case RHI::RenderCommandType::DrawFullscreen:
					drawFullscreen(buffer, command);
					break;
				}
			}

			// 可視の記録は 2 段階のオクルージョンをした時だけ更新される。しなかったら次は使わない
			_visibilityValid = _scene.OcclusionDone;
			_stats.OcclusionCulling = _scene.OcclusionDone;

			// 後片付け : 深度は DEPTH_WRITE に (次のフレームの前提)、描画先はバックバッファに (この後 ImGui を描く)
			transitionDepth(D3D12_RESOURCE_STATE_DEPTH_WRITE);
			transitionMsaaDepth(D3D12_RESOURCE_STATE_DEPTH_WRITE);
			_boundSamples   = 1;
			_boundDepthMsaa = false;
			const UINT width  = _swapChain->GetWidth();
			const UINT height = _swapChain->GetHeight();
			const D3D12_VIEWPORT viewport = { 0.0f, 0.0f, static_cast<float>(width), static_cast<float>(height), 0.0f, 1.0f };
			const D3D12_RECT scissorRect  = { 0, 0, static_cast<LONG>(width), static_cast<LONG>(height) };
			cmdList->RSSetViewports(1, &viewport);
			cmdList->RSSetScissorRects(1, &scissorRect);
			const auto rtvHandle = _rtvHeap->GetCpuHandle(_swapChain->GetCurrentBackBufferIndex());
			cmdList->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);
			_boundColorCount = 0;
			_boundDepth      = RHI::RenderTargetHandle::INVALID;
			cmdList->SetPipelineState(_currentPipeline->GetPipelineState());
		}

		void CommandContext::setRenderTargets(const RHI::RenderCommand& command)
		{
			auto cmdList = _frameCommandList->GetCmdList();

			// 描画先にするレンダーターゲットを RENDER_TARGET へ (まとめて 1 回のバリアで)
			D3D12_RESOURCE_BARRIER barriers[RHI::MAX_PASS_COLOR_TARGETS] = {};
			UINT barrierCount = 0;
			uint32_t width = 0, height = 0;
			uint32_t samples = 0;   // 0 = まだ決まっていない (最初の色の描画先で決まる)
			_boundColorCount = 0;
			const uint32_t colorCount = (std::min)(command.ColorCount, RHI::MAX_PASS_COLOR_TARGETS);
			for (uint32_t c = 0; c < colorCount; ++c)
			{
				const uint32_t index = command.Colors[c];
				uint32_t w = 0, h = 0, s = 1;
				if (index == RHI::RenderTargetHandle::BACK_BUFFER)
				{
					_boundRtvs[_boundColorCount]    = _rtvHeap->GetCpuHandle(_swapChain->GetCurrentBackBufferIndex());
					_boundFormats[_boundColorCount] = DirectX12::SwapChain::FORMAT;
					w = _swapChain->GetWidth();
					h = _swapChain->GetHeight();
				}
				else if (index < _renderTargets.size() && _renderTargets[index].Alive)
				{
					RenderTarget& target = _renderTargets[index];
					if (target.IsDepth) continue;   // 深度のレンダーターゲットは Depth に指定する
					s = target.SampleCount;
					if (samples != 0 && s != samples)
					{
						// 同時に描く描画先はサンプル数が揃っていないといけない (D3D12 の決まり)。揃わない物は付けない
						Core::Logger::Log("[CommandContext] Render targets with different sample counts are bound together. Skipped.");
						continue;
					}
					target.ResolveDirty = s > 1;   // 描いたかもしれないので、次に読む時に解決し直す
					if (target.State != D3D12_RESOURCE_STATE_RENDER_TARGET)
					{
						barriers[barrierCount++] = makeTransitionBarrier(target.Resource.Get(), target.State, D3D12_RESOURCE_STATE_RENDER_TARGET);
						target.State = D3D12_RESOURCE_STATE_RENDER_TARGET;
					}
					_boundRtvs[_boundColorCount]    = _targetRtvHeap->GetCpuHandle(index);
					_boundFormats[_boundColorCount] = target.Format;
					w = target.Width;
					h = target.Height;
				}
				else
				{
					continue;   // 消されたレンダーターゲット (描かない)
				}
				_boundColors[_boundColorCount++] = index;
				if (samples == 0) samples = s;
				if (width == 0) { width = w; height = h; }
			}
			if (barrierCount > 0) cmdList->ResourceBarrier(barrierCount, barriers);
			_boundSamples = (std::max)(1u, samples);

			// シーンの深度 : 描画先が MSAA なら、同じサンプル数の MSAA の深度を付ける (C# からは同じ SCENE_DEPTH に見える)
			_boundDepth     = RHI::RenderTargetHandle::INVALID;
			_boundDepthMsaa = false;
			if (command.Depth == RHI::RenderTargetHandle::SCENE_DEPTH && _depthBuffer)
			{
				if (_boundSamples > 1)
				{
					if (ensureMsaaDepth(_boundSamples))
					{
						transitionMsaaDepth(D3D12_RESOURCE_STATE_DEPTH_WRITE);
						_boundDepth     = RHI::RenderTargetHandle::SCENE_DEPTH;
						_boundDepthMsaa = true;
						_msaaDepthDirty = true;   // 描くかもしれないので、次に読む時に解決し直す
					}
				}
				else
				{
					transitionDepth(D3D12_RESOURCE_STATE_DEPTH_WRITE);
					_boundDepth     = RHI::RenderTargetHandle::SCENE_DEPTH;
					_msaaDepthDirty = false;  // 1 サンプルの深度に直接描く (MSAA の深度は古くなる)
				}
				if (width == 0) { width = _depthWidth; height = _depthHeight; }
			}
			else if (command.Depth < _renderTargets.size() && _renderTargets[command.Depth].Alive && _renderTargets[command.Depth].IsDepth)
			{
				// 深度のレンダーターゲット (シャドーマップ)
				RenderTarget& target = _renderTargets[command.Depth];
				if (target.State != D3D12_RESOURCE_STATE_DEPTH_WRITE)
				{
					const auto barrier = makeTransitionBarrier(target.Resource.Get(), target.State, D3D12_RESOURCE_STATE_DEPTH_WRITE);
					cmdList->ResourceBarrier(1, &barrier);
					target.State = D3D12_RESOURCE_STATE_DEPTH_WRITE;
				}
				_boundDepth      = command.Depth;
				_boundDepthSlice = (std::min)(command.Arg0, target.ArraySize - 1);   // 配列の何枚目に描くか
				if (width == 0) { width = target.Width; height = target.Height; }
			}
			const D3D12_CPU_DESCRIPTOR_HANDLE dsvHandle = boundDsv();
			cmdList->OMSetRenderTargets(_boundColorCount, _boundColorCount > 0 ? _boundRtvs : nullptr, FALSE,
				_boundDepth != RHI::RenderTargetHandle::INVALID ? &dsvHandle : nullptr);

			// ビューポートは描画先の大きさ (縮小バッファに描くパスもあるため)
			_boundWidth  = width;
			_boundHeight = height;
			if (width > 0 && height > 0)
			{
				const D3D12_VIEWPORT viewport = { 0.0f, 0.0f, static_cast<float>(width), static_cast<float>(height), 0.0f, 1.0f };
				const D3D12_RECT scissorRect  = { 0, 0, static_cast<LONG>(width), static_cast<LONG>(height) };
				cmdList->RSSetViewports(1, &viewport);
				cmdList->RSSetScissorRects(1, &scissorRect);
			}
		}

		void CommandContext::drawScene(Buffer* buffer, uint32_t scenePass, uint32_t queue, uint32_t view)
		{
			// ==============================================================
			// シーンを描く (今の描画先へ)
			//   不透明 : ここで GPU カリング (オクルージョンは 2 段階) を行って描く
			//   半透明 : 不透明のカリングの結果を使う (カリングがまだなら視錐台だけで行う)。G-Buffer には描かない
			// PSO は「シェーダー (Forward / GBuffer) × 不透明 / 半透明 × 描画先の形式」で選ぶ (初めての組み合わせなら作る)
			// 以前は不透明 → 半透明を 1 回でバックバッファに描いていた
			// ==============================================================
			if (!_scene.Valid) return;
			DrawFrame& frame = _draws[_frameIndex];
			if (frame.Written == 0) return;

			auto cmdList = _frameCommandList->GetCmdList();
			const bool        gbuffer = scenePass == static_cast<uint32_t>(RHI::ScenePass::GBuffer);
			const bool        opaque  = queue == static_cast<uint32_t>(RHI::RenderQueue::Opaque);
			if (!opaque && gbuffer) return;   // 半透明は G-Buffer に書けない (ディファードでも Forward で重ねる)

			if (view > buffer->GetShadowViewCount()) return;   // 影の視点が無い (影なし / カスケードの数を超えた)
			if (view == 0) ensureShadowReadable();              // カメラの描画は影を読む

			// シーンの深度も深度のレンダーターゲットも D32_FLOAT (以前はシーンの深度だけ)
			const DXGI_FORMAT dsvFormat = _boundDepth != RHI::RenderTargetHandle::INVALID ? DEPTH_FORMAT : DXGI_FORMAT_UNKNOWN;
			ID3D12CommandSignature* signature = _currentPipeline->GetIndirectDrawSignature();
			constexpr uint32_t GROUP_OPAQUE = Pipeline::PIPELINE_OPAQUE;   // (OPAQUE は wingdi.h のマクロと重なるので使わない)
			constexpr uint32_t GROUP_ALPHA  = Pipeline::PIPELINE_ALPHA_BLEND;
			constexpr uint32_t GROUP_DOUBLE = Pipeline::PIPELINE_OPAQUE_DOUBLE_SIDED;

			auto setPso = [&](uint32_t group) -> bool
			{
				ID3D12PipelineState* pso = _currentPipeline->GetScenePso(scenePass, group, _boundFormats, _boundColorCount, dsvFormat, _boundSamples);
				if (pso) cmdList->SetPipelineState(pso);
				return pso != nullptr;
			};

			// カリングの結果 (映った描画だけを詰めた引数) を、GPU が数えた数 (Count バッファ) だけ描く
			// GroupCount は上限 (詰めた数がこれを超えることは無い)
			auto drawCulled = [&](uint32_t group)
			{
				if (frame.GroupCount[group] == 0 || !setPso(group)) return;
				ID3D12Resource* culled = frame.Culled->Get();
				cmdList->SetGraphicsRootShaderResourceView(Pipeline::ROOT_PARAM_DRAW_DATA_SRV, culled->GetGPUVirtualAddress());
				cmdList->ExecuteIndirect(signature, frame.GroupCount[group], culled,
					COMPACT_ARGS_OFFSET + static_cast<UINT64>(frame.GroupStart[group]) * sizeof(IndirectDrawCommand),
					culled, DRAW_ARGS_OFFSET + CULL_COUNTER_OFFSET + sizeof(uint32_t) * (1 + group));
			};

			// カリングしない : CPU が並べた引数で描く
			auto drawAll = [&](uint32_t group)
			{
				if (frame.GroupCount[group] == 0 || !setPso(group)) return;
				ID3D12Resource* source = frame.Gpu->Get();
				cmdList->SetGraphicsRootShaderResourceView(Pipeline::ROOT_PARAM_DRAW_DATA_SRV, source->GetGPUVirtualAddress());
				if (_scene.Indirect)
				{
					// 引数バッファ (VRAM) に並べた分を 1 回の命令で描く。GPU は描画の引数を読むだけ (ルート引数は変えない)
					// 書き直さなかったフレームは COMMON → INDIRECT_ARGUMENT に暗黙で昇格するのでバリアは不要
					cmdList->ExecuteIndirect(signature, frame.GroupCount[group], source,
						DRAW_ARGS_OFFSET + static_cast<UINT64>(frame.GroupStart[group]) * sizeof(IndirectDrawCommand), nullptr, 0);
				}
				else
				{
					// 1 個ずつ描く。描画ごとのルート引数の設定は無い (以前は描画ごとにルート定数とルート CBV を設定していた)
					// BaseVertexLocation (第4引数) は SV_VertexID に加算されないので 0。頂点の開始位置は DrawData で渡す (2-9)
					for (uint32_t i = 0; i < frame.GroupCount[group]; ++i)
					{
						const auto& args = frame.Args[frame.GroupStart[group] + i];
						cmdList->DrawIndexedInstanced(args.IndexCountPerInstance, args.InstanceCount, args.StartIndexLocation, 0, args.StartInstanceLocation);
					}
				}
			};

			// 不透明 : 片面 (裏面カリング) → 両面 の 2 つの PSO で描く
			auto drawOpaque = [&](auto&& draw)
			{
				draw(GROUP_OPAQUE);
				draw(GROUP_DOUBLE);
			};

			// ==============================================================
			// 影の視点 : その視点のフレームの定数 (ライトの View / Projection) に差し替えて描く
			//   カリングはライトの直方体で 1 回 (視錐台だけ。オクルージョンはしない)。結果は Culled に入り、
			//   カメラの描画が後で使う時はカメラで数え直す (_culledView で見分ける)
			// ==============================================================
			if (view > 0)
			{
				cmdList->SetGraphicsRootConstantBufferView(Pipeline::ROOT_PARAM_GLOBAL_CBV, buffer->GetViewConstants(_frameIndex, view));
				if (!_scene.Culling)
				{
					if (opaque) drawOpaque(drawAll);
					else        drawAll(GROUP_ALPHA);
				}
				else
				{
					// 段ごとにカリングする (既定) / 全カスケードを囲む箱で 1 回だけ数えて全段で使う (SHADOW_FLAG_UNION_CULLING)
					const bool     unionCulling = buffer->IsShadowUnionCulling();
					const uint32_t cullKey      = unionCulling ? SHADOW_UNION_VIEW : view;
					if (_culledView != cullKey)
					{
						recordCulling(frame, buffer, _scene.ObjectData, 0, true,
							unionCulling ? buffer->GetShadowUnionPlanes() : buffer->GetFrustumPlanes(view), false);
						_culledView = cullKey;
					}
					if (opaque) drawOpaque(drawCulled);
					else        drawCulled(GROUP_ALPHA);
				}
				cmdList->SetGraphicsRootConstantBufferView(Pipeline::ROOT_PARAM_GLOBAL_CBV, buffer->GetViewConstants(_frameIndex, 0));
				return;
			}

			if (!_scene.Culling)
			{
				if (opaque) drawOpaque(drawAll);
				else        drawAll(GROUP_ALPHA);
				return;
			}

			if (_scene.Culled && _culledView != 0)
			{
				// カメラのカリングの後に影の視点で数え直していた : カメラの視錐台でもう一度 (オクルージョンの結果は使えない)
				recordCulling(frame, buffer, _scene.ObjectData, 0, true, buffer->GetFrustumPlanes(), false);
				_culledView = 0;
			}

			if (!_scene.Culled)
			{
				// オクルージョンはシーンの深度に描く時だけ (Hi-Z をその深度から作る)
				if (opaque && _scene.Occlusion && _boundDepth == RHI::RenderTargetHandle::SCENE_DEPTH)
				{
					// ==============================================================
					// 2 段階のオクルージョンカリング
					//   前段 : 前のフレームで見えていた不透明な物を描く (視錐台だけで判定)
					//   Hi-Z : 前段の深度から作る (このフレームのカメラなので、カメラが動いても遅れない)
					//   後段 : 全部を視錐台と Hi-Z で判定し直して可視を記録し、前段で描かなかった物を描く (下の drawCulled)
					//          半透明は後段の結果に入る (不透明の後に別のコマンドで描く)
					//   描画リストが変わった時 / 前のフレームでオクルージョンしていない時は、可視の記録が使えないので
					//   前段は何も描かず、後段で全部を判定する (そのフレームだけ Hi-Z は空 = 何も隠れない)
					// ==============================================================
					const bool resetVisibility = !_scene.Reused || !_visibilityValid;
					recordCulling(frame, buffer, _scene.ObjectData, 1, resetVisibility, buffer->GetFrustumPlanes(), true);
					drawOpaque(drawCulled);
					// MSAA : Hi-Z は MSAA の深度から直接作る (作れない環境では先に 1 サンプルの深度へ解決する)
					// 以前は毎回解決していた (SSAO もあると 1 フレームに 2 回)
					// (8x は先に解決する方が速い : HIZ_FROM_MSAA_MAX_SAMPLES)
					if (_boundDepthMsaa && !hiZFromMsaa()) resolveSceneDepth(buffer);
					buildHiZ(buffer);
					recordCulling(frame, buffer, _scene.ObjectData, 2, resetVisibility, buffer->GetFrustumPlanes(), true);
					_scene.OcclusionDone = true;
				}
				else
				{
					recordCulling(frame, buffer, _scene.ObjectData, 0, true, buffer->GetFrustumPlanes(), true);
				}
				_scene.Culled = true;
				_culledView   = 0;
			}
			// ※ 2 段階のオクルージョンの後に不透明をもう一度描くと、後段の分 (前段で描かなかった物) だけになる。
			//    同じフレームで不透明を 2 回描く描画パスを作る時は、カリングをパスごとに分けること (影は視点ごとに分けてある)
			if (opaque) drawOpaque(drawCulled);
			else        drawCulled(GROUP_ALPHA);
		}

		void CommandContext::drawFullscreen(Buffer* buffer, const RHI::RenderCommand& command)
		{
			// ==============================================================
			// 全画面の三角形を 1 枚描く (ライティング / ポストエフェクト)
			//   読むテクスチャはテクスチャの配列 (バインドレス) の番号でルート定数に渡す。
			//   レンダーターゲットの SRV もテクスチャと同じヒープに置いてあるので、ディスクリプタを並べ直さなくてよい
			// ==============================================================
			ID3D12PipelineState* pso = _currentPipeline->GetFullscreenPso(command.Arg0, command.Arg1, _boundFormats, _boundColorCount, _boundSamples);
			if (!pso) return;
			auto cmdList = _frameCommandList->GetCmdList();
			ensureShadowReadable();   // ディファードのライティングが影を読む

			uint32_t constants[Pipeline::PASS_CONSTANT_COUNT] = {};
			D3D12_RESOURCE_BARRIER barriers[RHI::MAX_PASS_INPUTS] = {};
			UINT barrierCount = 0;
			const D3D12_RESOURCE_STATES readState = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
			for (uint32_t i = 0; i < RHI::MAX_PASS_INPUTS; ++i)
			{
				const uint32_t index = command.Inputs[i];
				constants[i] = 0;   // 無い時は既定テクスチャ (白)
				if (index == RHI::RenderTargetHandle::SCENE_DEPTH)
				{
					if (!ensureDepthSrv(buffer)) continue;
					if (_boundDepth == RHI::RenderTargetHandle::SCENE_DEPTH)
					{
						// 深度を描画先にしたまま読むことはできないので、色だけの描画先にする
						cmdList->OMSetRenderTargets(_boundColorCount, _boundColorCount > 0 ? _boundRtvs : nullptr, FALSE, nullptr);
						_boundDepth     = RHI::RenderTargetHandle::INVALID;
						_boundDepthMsaa = false;
					}
					// MSAA の深度に描いていたら、1 サンプルの深度へ解決してから読む (このフレームで 1 回だけ)
					if (_msaaDepthDirty) resolveSceneDepth(buffer);
					_sceneDepthReadThisFrame = true;
					transitionDepth(readState);
					constants[i] = _depthSrvIndex - Buffer::SRV_INDEX_TEXTURE_BEGIN;
				}
				else if (index < _renderTargets.size() && _renderTargets[index].Alive)
				{
					RenderTarget& target = _renderTargets[index];
					if (_boundDepth == index)
					{
						// 深度のレンダーターゲットを描画先にしたまま読むことはできないので、色だけの描画先にする
						cmdList->OMSetRenderTargets(_boundColorCount, _boundColorCount > 0 ? _boundRtvs : nullptr, FALSE, nullptr);
						_boundDepth = RHI::RenderTargetHandle::INVALID;
					}
					if (target.SampleCount > 1)
					{
						// MSAA : 1 サンプルの写し (Resolved) を読む。描いた後なら先に解決する
						if (target.ResolveDirty) resolveTarget(target);
						if (target.ResolvedState != readState)
						{
							barriers[barrierCount++] = makeTransitionBarrier(target.Resolved.Get(), target.ResolvedState, readState);
							target.ResolvedState = readState;
						}
					}
					else if (target.State != readState)
					{
						barriers[barrierCount++] = makeTransitionBarrier(target.Resource.Get(), target.State, readState);
						target.State = readState;
					}
					constants[i] = target.SrvIndex - Buffer::SRV_INDEX_TEXTURE_BEGIN;
				}
			}
			if (barrierCount > 0) cmdList->ResourceBarrier(barrierCount, barriers);
			static_assert(RHI::MAX_PASS_INPUTS + RHI::MAX_PASS_PARAMS == Pipeline::PASS_CONSTANT_COUNT, "pass constants size mismatch");
			std::memcpy(&constants[RHI::MAX_PASS_INPUTS], command.Params, sizeof(command.Params));

			cmdList->SetPipelineState(pso);
			cmdList->SetGraphicsRoot32BitConstants(Pipeline::ROOT_PARAM_PASS_CONSTANTS, Pipeline::PASS_CONSTANT_COUNT, constants, 0);
			cmdList->DrawInstanced(3, 1, 0, 0);   // 頂点はシェーダーが SV_VertexID から作る (入力レイアウト無し)
		}

		void CommandContext::ensureShadowReadable()
		{
			if (_shadowMapTarget >= _renderTargets.size() || _shadowMapTarget == _boundDepth) return;
			RenderTarget& target = _renderTargets[_shadowMapTarget];
			if (!target.Alive || target.State == D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE) return;
			const auto barrier = makeTransitionBarrier(target.Resource.Get(), target.State, D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
			_frameCommandList->GetCmdList()->ResourceBarrier(1, &barrier);
			target.State = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
		}

		void CommandContext::SetShadowSettings(Buffer* buffer, const RHI::ShadowSettings& settings)
		{
			if (!buffer) return;
			RHI::ShadowSettings s = settings;
			uint32_t texture = 0, resolution = 1;
			_shadowMapTarget = RHI::RenderTargetHandle::INVALID;
			if (s.Enabled && s.ShadowMap < _renderTargets.size() && _renderTargets[s.ShadowMap].Alive && _renderTargets[s.ShadowMap].IsDepth)
			{
				const RenderTarget& target = _renderTargets[s.ShadowMap];
				texture          = target.SrvIndex - Buffer::SRV_INDEX_TEXTURE_BEGIN;
				resolution       = target.Width;
				_shadowMapTarget = s.ShadowMap;
			}
			else
			{
				s.Enabled = 0;   // シャドーマップが無い / 深度でない
			}
			_shadowBuffer = buffer;
			buffer->SetShadowSettings(s, texture, resolution);
		}

		void CommandContext::transitionDepth(D3D12_RESOURCE_STATES after)
		{
			if (!_depthBuffer || _depthState == after) return;
			const auto barrier = makeTransitionBarrier(_depthBuffer->Get(), _depthState, after);
			_frameCommandList->GetCmdList()->ResourceBarrier(1, &barrier);
			_depthState = after;
		}

		// ==============================================================
		// MSAA (マルチサンプル アンチエイリアス。ハードウェアの固定機能)
		//   描画先 (色・深度) を 1 ピクセル N サンプルにして、物の縁だけ N 回分の塗り分けを持つ。
		//   ピクセルシェーダーは 1 ピクセル 1 回のまま (縁のサンプルに同じ色が書かれる) なので、
		//   SSAA (N 倍の解像度で描く) より軽い。読む時に 1 サンプルへ解決 (平均) する
		//   C# からは「サンプル数を指定した RenderTexture」に見え、解決は RHI が自動で行う
		// ==============================================================

		uint32_t CommandContext::supportedSampleCount(DXGI_FORMAT format, uint32_t requested) const
		{
			ID3D12Device* dx12Device = _device->GetDevice()->GetDevice();
			// 色は解決 (ResolveSubresource) もできる形式に限る (深度はシェーダーで解決するので要らない)
			const bool depth = format == DEPTH_FORMAT;
			if (!depth)
			{
				D3D12_FEATURE_DATA_FORMAT_SUPPORT support = { format };
				if (FAILED(dx12Device->CheckFeatureSupport(D3D12_FEATURE_FORMAT_SUPPORT, &support, sizeof(support))) ||
					!(support.Support1 & D3D12_FORMAT_SUPPORT1_MULTISAMPLE_RESOLVE))
				{
					return 1;
				}
			}
			for (uint32_t samples = (std::min)(requested, 8u); samples >= 2; samples /= 2)
			{
				D3D12_FEATURE_DATA_MULTISAMPLE_QUALITY_LEVELS levels = {};
				levels.Format      = format;
				levels.SampleCount = samples;
				if (SUCCEEDED(dx12Device->CheckFeatureSupport(D3D12_FEATURE_MULTISAMPLE_QUALITY_LEVELS, &levels, sizeof(levels))) &&
					levels.NumQualityLevels > 0)
				{
					return samples;
				}
			}
			return 1;
		}

		bool CommandContext::ensureMsaaDepth(uint32_t samples)
		{
			// MSAA の深度 (シーンの深度と同じ大きさ)。サンプル数が変わった時 / リサイズの後に作り直す
			if (_msaaDepth && _msaaDepthSamples == samples) return true;
			if (_msaaDepth)
			{
				WaitForGpu();   // 前のサンプル数の深度を GPU が使い終わってから (AA の切り替えの時だけ)
				_msaaDepth.Reset();
			}
			_msaaDepthSamples = 0;
			if (_depthWidth == 0 || _depthHeight == 0) return false;

			ID3D12Device* dx12Device = _device->GetDevice()->GetDevice();
			D3D12_HEAP_PROPERTIES heapProps = {};
			heapProps.Type = D3D12_HEAP_TYPE_DEFAULT;
			D3D12_RESOURCE_DESC desc = {};
			desc.Dimension        = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
			desc.Width            = _depthWidth;
			desc.Height           = _depthHeight;
			desc.DepthOrArraySize = 1;
			desc.MipLevels        = 1;
			desc.Format           = DXGI_FORMAT_R32_TYPELESS;   // DSV (D32_FLOAT) と SRV (R32_FLOAT) の両方で使う
			desc.SampleDesc.Count = samples;
			desc.Layout           = D3D12_TEXTURE_LAYOUT_UNKNOWN;
			desc.Flags            = D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL;
			D3D12_CLEAR_VALUE clearValue = {};
			clearValue.Format             = DEPTH_FORMAT;
			clearValue.DepthStencil.Depth = DEPTH_CLEAR_VALUE;
			const HRESULT hr = dx12Device->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAG_NONE, &desc,
				D3D12_RESOURCE_STATE_DEPTH_WRITE, &clearValue, IID_PPV_ARGS(&_msaaDepth));
			if (FAILED(hr)) { logFailed("Create MSAA depth", hr); _msaaDepth.Reset(); return false; }
			_msaaDepth->SetName(L"MsaaDepth");
			_msaaDepthState    = D3D12_RESOURCE_STATE_DEPTH_WRITE;
			_msaaDepthSamples  = samples;
			_msaaDepthSrvDirty = true;

			D3D12_DEPTH_STENCIL_VIEW_DESC dsvDesc = {};
			dsvDesc.Format        = DEPTH_FORMAT;
			dsvDesc.ViewDimension = D3D12_DSV_DIMENSION_TEXTURE2DMS;
			dx12Device->CreateDepthStencilView(_msaaDepth.Get(), &dsvDesc, _dsvHeap->GetCpuHandle(1));
			return true;
		}

		void CommandContext::transitionMsaaDepth(D3D12_RESOURCE_STATES after)
		{
			if (!_msaaDepth || _msaaDepthState == after) return;
			const auto barrier = makeTransitionBarrier(_msaaDepth.Get(), _msaaDepthState, after);
			_frameCommandList->GetCmdList()->ResourceBarrier(1, &barrier);
			_msaaDepthState = after;
		}

		D3D12_CPU_DESCRIPTOR_HANDLE CommandContext::boundDsv() const
		{
			if (_boundDepth < _renderTargets.size() && _targetDsvHeap) return _targetDsvHeap->GetCpuHandle(_boundDepth * RHI::MAX_DEPTH_SLICES + _boundDepthSlice);   // 深度のレンダーターゲット
			return _dsvHeap->GetCpuHandle(_boundDepthMsaa ? 1 : 0);
		}

		void CommandContext::resolveSceneDepth(Buffer* buffer)
		{
			// ==============================================================
			// MSAA の深度 → 1 サンプルの深度 (_depthBuffer)
			//   全画面の三角形で、ピクセルごとにサンプルの一番奥を SV_Depth に書く (PSResolveDepth)
			//   Hi-Z の前 (オクルージョンの前段の後) と、全画面のシェーダーが SCENE_DEPTH を読む前に呼ばれる
			//   終わったら描画先とビューポートを元に戻す (この後もシーンの描画が続くことがある)
			// ==============================================================
			if (!_msaaDepth || !_msaaDepthDirty) return;
			ID3D12PipelineState* pso = _currentPipeline->GetDepthResolvePso();
			if (!pso || !ensureMsaaDepthSrv(buffer)) return;

			auto cmdList = _frameCommandList->GetCmdList();
			transitionMsaaDepth(D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE);
			transitionDepth(D3D12_RESOURCE_STATE_DEPTH_WRITE);
			const D3D12_CPU_DESCRIPTOR_HANDLE dsv = _dsvHeap->GetCpuHandle(0);
			cmdList->OMSetRenderTargets(0, nullptr, FALSE, &dsv);
			const D3D12_VIEWPORT viewport = { 0.0f, 0.0f, static_cast<float>(_depthWidth), static_cast<float>(_depthHeight), 0.0f, 1.0f };
			const D3D12_RECT scissorRect  = { 0, 0, static_cast<LONG>(_depthWidth), static_cast<LONG>(_depthHeight) };
			cmdList->RSSetViewports(1, &viewport);
			cmdList->RSSetScissorRects(1, &scissorRect);

			uint32_t constants[Pipeline::PASS_CONSTANT_COUNT] = {};
			constants[0] = _msaaDepthSrvIndex - Buffer::SRV_INDEX_TEXTURE_BEGIN;   // g_Inputs[0].x : MSAA の深度
			constants[1] = _msaaDepthSamples;                                       // g_Inputs[0].y : サンプル数
			cmdList->SetPipelineState(pso);
			cmdList->SetGraphicsRoot32BitConstants(Pipeline::ROOT_PARAM_PASS_CONSTANTS, Pipeline::PASS_CONSTANT_COUNT, constants, 0);
			cmdList->DrawInstanced(3, 1, 0, 0);
			_msaaDepthDirty = false;

			// 元の描画先に戻す (MSAA の深度を付けていたなら、描ける状態に戻して付け直す)
			if (_boundDepthMsaa) transitionMsaaDepth(D3D12_RESOURCE_STATE_DEPTH_WRITE);
			const D3D12_CPU_DESCRIPTOR_HANDLE bound = boundDsv();
			cmdList->OMSetRenderTargets(_boundColorCount, _boundColorCount > 0 ? _boundRtvs : nullptr, FALSE,
				_boundDepth != RHI::RenderTargetHandle::INVALID ? &bound : nullptr);
			if (_boundWidth > 0 && _boundHeight > 0)
			{
				const D3D12_VIEWPORT boundViewport = { 0.0f, 0.0f, static_cast<float>(_boundWidth), static_cast<float>(_boundHeight), 0.0f, 1.0f };
				const D3D12_RECT boundRect         = { 0, 0, static_cast<LONG>(_boundWidth), static_cast<LONG>(_boundHeight) };
				cmdList->RSSetViewports(1, &boundViewport);
				cmdList->RSSetScissorRects(1, &boundRect);
			}
		}

		bool CommandContext::hiZFromMsaa() const
		{
			// 後で全画面のシェーダーが深度を読む (SSAO など) なら、どうせ解決するので先に解決して、その深度から作る方が速い
			// (MSAA の深度をコンピュートで読むと圧縮の展開が別に要るため。4x + SSAO : 直接 0.443 / 解決 0.426 ms)
			// 今のフレームの後のことは分からないので、前のフレームで読んだかで決める (切り替えた次のフレームから合う)
			if (_sceneDepthReadLastFrame) return false;
			return _currentPipeline->GetHiZPipelineMs() && _msaaDepthSamples <= HIZ_FROM_MSAA_MAX_SAMPLES;
		}

		bool CommandContext::ensureMsaaDepthSrv(Buffer* buffer)
		{
			// MSAA の深度の SRV (ヒープに 1 個だけ確保し、MSAA の深度を作り直した時に書き直す)
			if (!_msaaDepth) return false;
			auto heapManager = buffer->GetDescHeapManager();
			if (_msaaDepthSrvIndex == DirectX12::DescriptorHeapManager::INVALID_INDEX)
			{
				_msaaDepthSrvIndex = heapManager->AllocateSrv();
				if (_msaaDepthSrvIndex == DirectX12::DescriptorHeapManager::INVALID_INDEX) return false;   // 満杯
				_msaaDepthSrvDirty = true;
			}
			if (_msaaDepthSrvDirty)
			{
				D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
				srvDesc.Format                  = DXGI_FORMAT_R32_FLOAT;
				srvDesc.ViewDimension           = D3D12_SRV_DIMENSION_TEXTURE2DMS;
				srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
				_device->GetDevice()->GetDevice()->CreateShaderResourceView(_msaaDepth.Get(), &srvDesc, heapManager->GetSrvCpuHandle(_msaaDepthSrvIndex));
				_msaaDepthSrvDirty = false;
			}
			return true;
		}

		void CommandContext::resolveTarget(RenderTarget& target)
		{
			// MSAA の色 → 1 サンプルの写し (ハードウェアの解決 = サンプルの平均)
			auto cmdList = _frameCommandList->GetCmdList();
			D3D12_RESOURCE_BARRIER barriers[2] = {};
			UINT count = 0;
			if (target.State != D3D12_RESOURCE_STATE_RESOLVE_SOURCE)
			{
				barriers[count++] = makeTransitionBarrier(target.Resource.Get(), target.State, D3D12_RESOURCE_STATE_RESOLVE_SOURCE);
				target.State = D3D12_RESOURCE_STATE_RESOLVE_SOURCE;
			}
			if (target.ResolvedState != D3D12_RESOURCE_STATE_RESOLVE_DEST)
			{
				barriers[count++] = makeTransitionBarrier(target.Resolved.Get(), target.ResolvedState, D3D12_RESOURCE_STATE_RESOLVE_DEST);
				target.ResolvedState = D3D12_RESOURCE_STATE_RESOLVE_DEST;
			}
			if (count > 0) cmdList->ResourceBarrier(count, barriers);
			cmdList->ResolveSubresource(target.Resolved.Get(), 0, target.Resource.Get(), 0, target.Format);
			target.ResolveDirty = false;
		}

		// ==============================================================
		// レンダーターゲット
		// ==============================================================

		RHI::RenderTargetHandle CommandContext::CreateRenderTarget(Buffer* buffer, const RHI::RenderTargetDesc& desc)
		{
			RHI::RenderTargetHandle handle;
			const DXGI_FORMAT format = toDxgiFormat(desc.Format);
			if (!buffer || !_device || format == DXGI_FORMAT_UNKNOWN) return handle;
			if (desc.Scale <= 0.0f && (desc.Width == 0 || desc.Height == 0)) return handle;

			// RTV ヒープ (初めて作る時に用意する)
			if (!_targetRtvHeap)
			{
				_targetRtvHeap = std::make_unique<DirectX12::DescriptorHeap>();
				if (FAILED(_targetRtvHeap->Create(_device->GetDevice()->GetDevice(), D3D12_DESCRIPTOR_HEAP_TYPE_RTV, MAX_RENDER_TARGETS)))
				{
					Core::Logger::Log("[CommandContext] Failed create render target heap.");
					_targetRtvHeap.reset();
					return handle;
				}
			}

			uint32_t index = 0;
			if (!_freeRenderTargets.empty())
			{
				index = _freeRenderTargets.back();
				_freeRenderTargets.pop_back();
			}
			else
			{
				if (_renderTargets.size() >= MAX_RENDER_TARGETS)
				{
					Core::Logger::Log("[CommandContext] Too many render targets.");
					return handle;
				}
				index = static_cast<uint32_t>(_renderTargets.size());
				_renderTargets.emplace_back();
			}

			_heapManager = buffer->GetDescHeapManager();
			RenderTarget& target = _renderTargets[index];
			target = RenderTarget();
			target.Format      = format;
			target.Scale       = desc.Scale;
			target.FixedWidth  = desc.Width;
			target.FixedHeight = desc.Height;
			std::memcpy(target.Clear, desc.ClearColor, sizeof(target.Clear));
			target.IsDepth     = desc.Format == RHI::RenderTargetFormat::Depth32;
			target.ArraySize   = target.IsDepth ? (std::min)((std::max)(desc.ArraySize, 1u), RHI::MAX_DEPTH_SLICES) : 1;
			target.SampleCount = (!target.IsDepth && desc.SampleCount > 1) ? supportedSampleCount(format, desc.SampleCount) : 1;
			if (target.IsDepth && !_targetDsvHeap)
			{
				_targetDsvHeap = std::make_unique<DirectX12::DescriptorHeap>();
				if (FAILED(_targetDsvHeap->Create(_device->GetDevice()->GetDevice(), D3D12_DESCRIPTOR_HEAP_TYPE_DSV, MAX_RENDER_TARGETS * RHI::MAX_DEPTH_SLICES)))
				{
					Core::Logger::Log("[CommandContext] Failed create depth target heap.");
					_targetDsvHeap.reset();
				}
			}
			target.SrvIndex = _heapManager->AllocateSrv();
			if (target.SrvIndex == DirectX12::DescriptorHeapManager::INVALID_INDEX || !createRenderTargetResource(target, index))
			{
				if (target.SrvIndex != DirectX12::DescriptorHeapManager::INVALID_INDEX) _heapManager->FreeSrv(target.SrvIndex);
				target = RenderTarget();
				_freeRenderTargets.push_back(index);
				return handle;
			}
			target.Alive = true;
			handle.Index = index;
			return handle;
		}

		bool CommandContext::createRenderTargetResource(RenderTarget& target, uint32_t index)
		{
			ID3D12Device* dx12Device = _device->GetDevice()->GetDevice();
			uint32_t width  = target.FixedWidth;
			uint32_t height = target.FixedHeight;
			if (target.Scale > 0.0f)
			{
				width  = (std::max)(1u, static_cast<uint32_t>(GetWidth()  * target.Scale + 0.5f));
				height = (std::max)(1u, static_cast<uint32_t>(GetHeight() * target.Scale + 0.5f));
			}

			D3D12_HEAP_PROPERTIES heapProps = {};
			heapProps.Type = D3D12_HEAP_TYPE_DEFAULT;

			D3D12_RESOURCE_DESC desc = {};
			desc.Dimension        = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
			desc.Width            = width;
			desc.Height           = height;
			desc.DepthOrArraySize = 1;
			desc.MipLevels        = 1;
			desc.Format           = target.Format;
			desc.SampleDesc.Count = target.SampleCount;
			desc.Layout           = D3D12_TEXTURE_LAYOUT_UNKNOWN;
			desc.Flags            = D3D12_RESOURCE_FLAG_ALLOW_RENDER_TARGET;

			if (target.IsDepth)
			{
				// 深度だけ (シャドーマップ) : DSV (D32_FLOAT) で描き、SRV (R32_FLOAT) で読む
				if (!_targetDsvHeap) return false;
				desc.Flags            = D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL;
				desc.DepthOrArraySize = static_cast<UINT16>(target.ArraySize);
				D3D12_CLEAR_VALUE depthClear = {};
				depthClear.Format             = DEPTH_FORMAT;
				depthClear.DepthStencil.Depth = DEPTH_CLEAR_VALUE;
				target.Resource.Reset();
				const HRESULT depthHr = dx12Device->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAG_NONE, &desc,
					D3D12_RESOURCE_STATE_DEPTH_WRITE, &depthClear, IID_PPV_ARGS(&target.Resource));
				if (FAILED(depthHr)) { logFailed("CreateRenderTarget (depth)", depthHr); return false; }
				target.Resource->SetName(L"DepthTarget");
				target.State  = D3D12_RESOURCE_STATE_DEPTH_WRITE;
				target.Width  = width;
				target.Height = height;

				// DSV は 1 枚ずつ (番号 = レンダーターゲットの番号 × MAX_DEPTH_SLICES + 枚目)。SRV は配列のまま
				// (以前は 1 枚だけ : TEXTURE2D)
				for (uint32_t slice = 0; slice < target.ArraySize; ++slice)
				{
					D3D12_DEPTH_STENCIL_VIEW_DESC dsvDesc = {};
					dsvDesc.Format                         = DEPTH_FORMAT;
					dsvDesc.ViewDimension                  = D3D12_DSV_DIMENSION_TEXTURE2DARRAY;
					dsvDesc.Texture2DArray.FirstArraySlice = slice;
					dsvDesc.Texture2DArray.ArraySize       = 1;
					dx12Device->CreateDepthStencilView(target.Resource.Get(), &dsvDesc, _targetDsvHeap->GetCpuHandle(index * RHI::MAX_DEPTH_SLICES + slice));
				}
				D3D12_SHADER_RESOURCE_VIEW_DESC depthSrv = {};
				depthSrv.Format                   = DXGI_FORMAT_R32_FLOAT;
				depthSrv.ViewDimension            = D3D12_SRV_DIMENSION_TEXTURE2DARRAY;
				depthSrv.Shader4ComponentMapping  = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
				depthSrv.Texture2DArray.MipLevels = 1;
				depthSrv.Texture2DArray.ArraySize = target.ArraySize;
				dx12Device->CreateShaderResourceView(target.Resource.Get(), &depthSrv, _heapManager->GetSrvCpuHandle(target.SrvIndex));
				return true;
			}

			// 最適化クリア値 : ClearColor で同じ色を使うと高速クリアになる
			D3D12_CLEAR_VALUE clearValue = {};
			clearValue.Format = target.Format;
			std::memcpy(clearValue.Color, target.Clear, sizeof(clearValue.Color));

			target.Resource.Reset();
			const HRESULT hr = dx12Device->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAG_NONE, &desc,
				D3D12_RESOURCE_STATE_RENDER_TARGET, &clearValue, IID_PPV_ARGS(&target.Resource));
			if (FAILED(hr)) { logFailed("CreateRenderTarget", hr); return false; }
			target.Resource->SetName(target.SampleCount > 1 ? L"RenderTargetMsaa" : L"RenderTarget");
			target.State  = D3D12_RESOURCE_STATE_RENDER_TARGET;
			target.Width  = width;
			target.Height = height;

			// MSAA : 読む時の 1 サンプルの写し (解決の書き先。描画先にはしない)
			target.Resolved.Reset();
			target.ResolveDirty = false;
			if (target.SampleCount > 1)
			{
				desc.SampleDesc.Count = 1;
				desc.Flags            = D3D12_RESOURCE_FLAG_NONE;
				const HRESULT resolvedHr = dx12Device->CreateCommittedResource(&heapProps, D3D12_HEAP_FLAG_NONE, &desc,
					D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE, nullptr, IID_PPV_ARGS(&target.Resolved));
				if (FAILED(resolvedHr)) { logFailed("CreateRenderTarget (resolve)", resolvedHr); return false; }
				target.Resolved->SetName(L"RenderTargetResolved");
				target.ResolvedState = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
			}

			// RTV (描画先) と SRV (後のパスで読む)。番号はリサイズで作り直しても変わらない
			// (MSAA の RTV は nullptr の既定で TEXTURE2DMS になる。SRV は 1 サンプルの写しを指す)
			dx12Device->CreateRenderTargetView(target.Resource.Get(), nullptr, _targetRtvHeap->GetCpuHandle(index));
			D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
			srvDesc.Format                  = target.Format;
			srvDesc.ViewDimension           = D3D12_SRV_DIMENSION_TEXTURE2D;
			srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
			srvDesc.Texture2D.MipLevels     = 1;
			ID3D12Resource* readable = target.SampleCount > 1 ? target.Resolved.Get() : target.Resource.Get();
			dx12Device->CreateShaderResourceView(readable, &srvDesc, _heapManager->GetSrvCpuHandle(target.SrvIndex));
			return true;
		}

		void CommandContext::DestroyRenderTarget(RHI::RenderTargetHandle handle)
		{
			if (handle.Index >= _renderTargets.size() || !_renderTargets[handle.Index].Alive) return;

			// GPU が使い終わってから消す (描画パスを切り替える時だけなので、待ってよい)
			WaitForGpu();
			if (handle.Index == _shadowMapTarget && _shadowBuffer)
			{
				// 影が読んでいるシャドーマップ : 消える SRV を読まないように影を止める (C# が次に設定し直すまで)
				_shadowMapTarget = RHI::RenderTargetHandle::INVALID;
				_shadowBuffer->SetShadowSettings(RHI::ShadowSettings(), 0, 1);
			}
			RenderTarget& target = _renderTargets[handle.Index];
			if (_heapManager && target.SrvIndex != DirectX12::DescriptorHeapManager::INVALID_INDEX) _heapManager->FreeSrv(target.SrvIndex);
			target = RenderTarget();
			_freeRenderTargets.push_back(handle.Index);
		}

		bool CommandContext::updateDraws(DrawFrame& frame, Buffer* buffer, const RHI::DrawItem* items, uint32_t count)
		{
			// 描画リスト (とインスタンシングの ON / OFF) が前回このフレームで書いたものと同じなら、書き込みもコピーも省く
			// (止まっているシーンでは毎フレーム同じなので、UPLOAD ヒープにも触らず、コピーも走らない)
			if (frame.Valid && frame.Instanced == _useInstancing && frame.LastItems.size() == count &&
				(count == 0 || std::memcmp(frame.LastItems.data(), items, sizeof(RHI::DrawItem) * count) == 0))
			{
				return true;
			}

			frame.Args.clear();
			frame.Triangles = 0;
			const uint32_t instances = writeDraws(frame, buffer, items, count, _useInstancing ? INSTANCE_BATCH_SIZE : 1);
			const uint32_t written   = static_cast<uint32_t>(frame.Args.size());

			// 引数はまとめて書く (UPLOAD ヒープは書き込み結合メモリなので、連続で書くのが速い)
			if (written > 0)
			{
				std::memcpy(frame.Cpu + DRAW_ARGS_OFFSET, frame.Args.data(), sizeof(IndirectDrawCommand) * written);
			}

			frame.LastItems.assign(items, items + count);
			frame.Written   = written;
			frame.Instances = instances;
			frame.Instanced = _useInstancing;
			frame.Valid     = true;

			// VRAM 側へコピーする (書き直した時だけ。使った分だけ)
			//   バッファは毎フレーム COMMON に戻る (ExecuteCommandLists の完了で減衰する) ので、
			//   コピーでは COMMON → COPY_DEST に暗黙で昇格する。コピー後はシェーダーと ExecuteIndirect が読む状態へ明示的に移す
			if (written > 0)
			{
				auto cmdList = _frameCommandList->GetCmdList();
				cmdList->CopyBufferRegion(frame.Gpu->Get(), 0, frame.Staging->Get(), 0, sizeof(DrawData) * instances);
				cmdList->CopyBufferRegion(frame.Gpu->Get(), DRAW_ARGS_OFFSET, frame.Staging->Get(), DRAW_ARGS_OFFSET,
					sizeof(IndirectDrawCommand) * written);
				cmdList->CopyBufferRegion(frame.Gpu->Get(), CULL_DATA_OFFSET, frame.Staging->Get(), CULL_DATA_OFFSET,
					sizeof(CullInstance) * instances);
				const auto toRead = makeTransitionBarrier(frame.Gpu->Get(), D3D12_RESOURCE_STATE_COPY_DEST,
					D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT);
				cmdList->ResourceBarrier(1, &toRead);
			}
			return false;
		}

		uint32_t CommandContext::writeDraws(DrawFrame& frame, Buffer* buffer, const RHI::DrawItem* items, uint32_t count, uint32_t batchSize)
		{
			// ==============================================================
			// DrawItem を描画のまとまりに分けて、DrawData と引数を書く
			//   1. DrawItem を前から順に見て、同じメッシュ・同じ描き方 (PSO) の書き込み中のまとまりに入れる
			//      (まとまりが無い / batchSize 個たまったら新しく作る)。ここでは数えるだけ
			//      インスタンシングする時は batchSize = INSTANCE_BATCH_SIZE、しない時は 1 (DrawItem 1 個 = 描画 1 回)
			//   2. まとまりを作られた順 (= 元の DrawItem の順番に近い) に並べ、DrawData の連続した範囲を割り当てる
			//   3. 描画の引数はまとまりごとに 1 個 : InstanceCount = 個数, StartInstanceLocation = 範囲の先頭
			//      描き方ごとに分けて並べる (不透明 → 半透明。半透明の先頭は GROUP_ALIGNMENT の倍数。間は空の描画)
			//   4. 各 DrawItem の DrawData (オブジェクト / 頂点の開始位置 / マテリアル) と
			//      GPU カリング用の境界球・描画の番号を、自分のまとまりの範囲に詰めて書く
			// 描画番号バッファは 0, 1, 2, ... なので、インスタンス j には DRAWID = Start + j が届き、
			// シェーダーはそのまま DrawData[Start + j] (= j 個目のオブジェクト) を読める
			// マテリアルはインスタンスごと (DrawData) に持つので、同じメッシュならマテリアルが違ってもまとめられる
			// (先に場所を確保する方法だと、埋まらなかったまとまりの分だけ DrawData が無駄になり、
			//  違うメッシュが多いと上限に届いてしまうので、数えてから割り当てる)
			// ==============================================================
			_openBatch.clear();
			_batches.clear();
			_itemBatch.resize(count);

			// 1. まとまりに分ける
			for (uint32_t i = 0; i < count; ++i)
			{
				const RHI::DrawItem& item = items[i];
				if (!item.Object.IsValid() || item.IndexCount == 0) { _itemBatch[i] = UINT32_MAX; continue; }

				uint32_t group = Pipeline::PIPELINE_OPAQUE;
				if (buffer->GetMaterialBlend(item.Material.Index) == RHI::BlendMode::AlphaBlend) group = Pipeline::PIPELINE_ALPHA_BLEND;
				else if (buffer->GetMaterialDoubleSided(item.Material.Index))                    group = Pipeline::PIPELINE_OPAQUE_DOUBLE_SIDED;
				const MeshKey key = { item.BaseVertex, item.FirstIndex, item.IndexCount, group };
				auto open = _openBatch.find(key);
				if (open == _openBatch.end() || _batches[open->second].Count >= batchSize)
				{
					const uint32_t index = static_cast<uint32_t>(_batches.size());
					_batches.push_back({ key, 0, 0, 0, 0, {} });
					if (open == _openBatch.end()) open = _openBatch.emplace(key, index).first;
					else                          open->second = index;
				}
				_batches[open->second].Count++;
				_itemBatch[i] = open->second;
			}

			// 2. 範囲を割り当てる (入りきらない分は削る)
			uint32_t slot = 0;
			for (auto& batch : _batches)
			{
				if (slot + batch.Count > MAX_DRAWS)
				{
					warnTooManyDraws();
					batch.Count = MAX_DRAWS - slot;
				}
				batch.Start  = slot;
				batch.Cursor = slot;
				slot += batch.Count;
			}

			// 3. 描画の引数 (まとまりごとに 1 個。描き方ごとに分けて並べる)
			//    並び : 不透明 → 両面の不透明 → 半透明 (半透明を最後にして「半透明の先頭より後ろ = 半透明」で見分ける)
			static constexpr uint32_t GROUP_ORDER[GROUP_COUNT] = {
				Pipeline::PIPELINE_OPAQUE, Pipeline::PIPELINE_OPAQUE_DOUBLE_SIDED, Pipeline::PIPELINE_ALPHA_BLEND };
			for (uint32_t order = 0; order < GROUP_COUNT; ++order)
			{
				const uint32_t g = GROUP_ORDER[order];
				// 前の描き方の後ろを、空の描画で GROUP_ALIGNMENT の倍数まで埋める
				// (空の描画は InstanceCount = 0。どのオブジェクトも属さないので、カリングで詰め直すと消える)
				while (frame.Args.size() % GROUP_ALIGNMENT != 0 && order > 0)
				{
					frame.Args.push_back(D3D12_DRAW_INDEXED_ARGUMENTS{});
				}
				frame.GroupStart[g] = static_cast<uint32_t>(frame.Args.size());
				for (auto& batch : _batches)
				{
					if (batch.Count == 0 || batch.Key.Group != g) continue;
					D3D12_DRAW_INDEXED_ARGUMENTS args;
					args.IndexCountPerInstance = batch.Key.IndexCount;
					args.InstanceCount         = batch.Count;
					args.StartIndexLocation    = batch.Key.FirstIndex;
					args.BaseVertexLocation    = 0;             // SV_VertexID に加算されないので 0 (2-9)
					args.StartInstanceLocation = batch.Start;   // インスタンス j の DRAWID = Start + j
					batch.Draw   = static_cast<uint32_t>(frame.Args.size());
					batch.Sphere = buffer->GetVertexBounds(batch.Key.BaseVertex);   // メッシュごとに 1 回だけ引く
					frame.Args.push_back(args);
					frame.Triangles += static_cast<uint64_t>(batch.Key.IndexCount / 3) * batch.Count;
				}
				frame.GroupCount[g] = static_cast<uint32_t>(frame.Args.size()) - frame.GroupStart[g];
				if (frame.GroupCount[g] == 0)
				{
					// 描く物が無ければ、埋めた空の描画も要らない
					frame.Args.resize(frame.GroupStart[g]);
				}
			}

			// 4. DrawData と GPU カリング用のデータを書く
			auto* drawData = reinterpret_cast<DrawData*>(frame.Cpu);
			auto* cullData = reinterpret_cast<CullInstance*>(frame.Cpu + CULL_DATA_OFFSET);
			for (uint32_t i = 0; i < count; ++i)
			{
				if (_itemBatch[i] == UINT32_MAX) continue;
				InstanceBatch& batch = _batches[_itemBatch[i]];
				if (batch.Cursor >= batch.Start + batch.Count) continue;   // 入りきらなかった分
				cullData[batch.Cursor] = { { batch.Sphere.x, batch.Sphere.y, batch.Sphere.z, batch.Sphere.w }, batch.Draw };
				drawData[batch.Cursor] = { items[i].Object.Index, items[i].BaseVertex, items[i].Material.Index, 0 };
				batch.Cursor++;
			}
			return slot;
		}

		void CommandContext::recordCulling(DrawFrame& frame, Buffer* buffer, D3D12_GPU_VIRTUAL_ADDRESS objectData, uint32_t phase, bool resetVisibility,
		                                   const ::DirectX::XMFLOAT4* planes, bool stats)
		{
			// ==============================================================
			// GPU カリング (描画の前に、同じコマンドリストでコンピュートシェーダーを 2 回実行する)
			//   1. CSClear : CPU が並べた引数を Culled に写し、InstanceCount を 0 にする
			//   2. CSCull  : オブジェクトごとに境界球と視錐台を比べ、映るものだけ
			//                自分の描画の InstanceCount を増やして、その場所に DrawData を書く
			//   3. CSCompact : 1 個でも映った描画の引数だけを前から詰めて並べ、その数を Count バッファに数える
			//   4. Culled を ExecuteIndirect と頂点シェーダーが読む状態にする (映った数・描画数は読み戻し用にコピー)
			// オクルージョンカリングの時は、前段 (phase 1) と後段 (phase 2) の 2 回呼ばれる
			// CPU は結果を待たない (同じフレームの ExecuteIndirect がそのまま使う)
			// ==============================================================
			auto cmdList = _frameCommandList->GetCmdList();
			ID3D12Resource* culled = frame.Culled->Get();
			const D3D12_GPU_VIRTUAL_ADDRESS input  = frame.Gpu->GetGPUVirtualAddress();
			const D3D12_GPU_VIRTUAL_ADDRESS output = culled->GetGPUVirtualAddress();

			CullConstants constants = {};
			// 視錐台は視点ごと (以前はカメラだけ : buffer->GetFrustumPlanes())
			for (int i = 0; i < 6; ++i)
			{
				constants.Planes[i][0] = planes[i].x; constants.Planes[i][1] = planes[i].y;
				constants.Planes[i][2] = planes[i].z; constants.Planes[i][3] = planes[i].w;
			}
			constants.InstanceCount = frame.Instances;
			constants.DrawCount     = frame.Written;
			constants.CounterOffset = static_cast<uint32_t>(CULL_COUNTER_OFFSET);

			// オクルージョンカリング : 後段は前段の深度で作った Hi-Z を読む
			constants.HiZWidth        = _hiZMipWidth[0];
			constants.HiZHeight       = _hiZMipHeight[0];
			constants.HiZLevels       = _hiZLevels;
			constants.Phase           = phase;
			constants.ResetVisibility = resetVisibility ? 1 : 0;
			constants.AlphaDrawStart  = frame.GroupCount[Pipeline::PIPELINE_ALPHA_BLEND] > 0 ? frame.GroupStart[Pipeline::PIPELINE_ALPHA_BLEND] : 0xFFFFFFFFu;
			constants.DoubleSidedDrawStart = frame.GroupCount[Pipeline::PIPELINE_OPAQUE_DOUBLE_SIDED] > 0
				? frame.GroupStart[Pipeline::PIPELINE_OPAQUE_DOUBLE_SIDED] : 0xFFFFFFFFu;
			static_assert(sizeof(CullConstants) == Pipeline::CULL_CONSTANT_COUNT * sizeof(uint32_t), "CullConstants size mismatch");

			// Culled : 1 回目はフレームの始めの COMMON から、後段は前段の描画が読み終わってから書き直す
			// (この遷移バリアで、前段の ExecuteIndirect と頂点シェーダーの読み込みが終わるのを待つ)
			const D3D12_RESOURCE_STATES culledRead =
				D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT | D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_COPY_SOURCE;
			// 状態は覚えておく (影の視点とカメラで 1 フレームに何度もカリングするため。以前は 前段 = COMMON / 後段 = 読む状態 に決め打ち)
			// 描画のルート SRV (DrawData。DATA_STATIC_WHILE_SET_AT_EXECUTE) が Culled を指したままだと、書き換える前に付け直す必要がある
			// (D3D12 のデバッグレイヤーのエラー。影の視点で描いた後にカメラでカリングし直す時に起きた)
			// → 書き換えない入力のバッファへ付け替えておく (描く時は drawCulled / drawAll が設定し直す)
			cmdList->SetGraphicsRootShaderResourceView(Pipeline::ROOT_PARAM_DRAW_DATA_SRV, input);
			const auto toUav = makeTransitionBarrier(culled, _culledState, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
			cmdList->ResourceBarrier(1, &toUav);
			_culledState = D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
			// Hi-Z と可視の記録は、使わない段階でもルートに設定するので、毎回その状態にしておく
			// (デバッグレイヤーはルートに設定しただけで「使った」とみなし、暗黙の昇格をしたことにするため。
			//  前段で状態を合わせないと、Hi-Z を作る時の遷移バリアが食い違っていた)
			transitionHiZ(D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
			if (_visibility && _visibilityState != D3D12_RESOURCE_STATE_UNORDERED_ACCESS)
			{
				const auto visibilityToUav = makeTransitionBarrier(_visibility->Get(), _visibilityState, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
				cmdList->ResourceBarrier(1, &visibilityToUav);
				_visibilityState = D3D12_RESOURCE_STATE_UNORDERED_ACCESS;
			}
			else if (phase == 2 && _visibility)
			{
				// 前段が読み終わってから後段が書き換えるように
				D3D12_RESOURCE_BARRIER visibilityUav = {};
				visibilityUav.Type          = D3D12_RESOURCE_BARRIER_TYPE_UAV;
				visibilityUav.UAV.pResource = _visibility->Get();
				cmdList->ResourceBarrier(1, &visibilityUav);
			}

			cmdList->SetComputeRootSignature(_currentPipeline->GetCullRootSignature());
			cmdList->SetComputeRoot32BitConstants(Pipeline::CULL_PARAM_CONSTANTS, Pipeline::CULL_CONSTANT_COUNT, &constants, 0);
			cmdList->SetComputeRootShaderResourceView(Pipeline::CULL_PARAM_OBJECTS,   objectData);
			cmdList->SetComputeRootShaderResourceView(Pipeline::CULL_PARAM_IN_DRAWS,  input);
			cmdList->SetComputeRootShaderResourceView(Pipeline::CULL_PARAM_CULL_DATA, input + CULL_DATA_OFFSET);
			cmdList->SetComputeRootShaderResourceView(Pipeline::CULL_PARAM_IN_ARGS,   input + DRAW_ARGS_OFFSET);
			cmdList->SetComputeRootUnorderedAccessView(Pipeline::CULL_PARAM_OUT_DRAWS, output);
			cmdList->SetComputeRootUnorderedAccessView(Pipeline::CULL_PARAM_OUT_ARGS,  output + DRAW_ARGS_OFFSET);
			cmdList->SetComputeRootUnorderedAccessView(Pipeline::CULL_PARAM_OUT_COMPACT, output + COMPACT_ARGS_OFFSET);
			// Hi-Z / 可視の記録が無い時もルート SRV / UAV には有効なアドレスを入れておく (シェーダーは後段 / 前段でしか読まない)
			cmdList->SetComputeRootShaderResourceView(Pipeline::CULL_PARAM_HIZ, _hiZ ? _hiZ->GetGPUVirtualAddress() : input);
			cmdList->SetComputeRootUnorderedAccessView(Pipeline::CULL_PARAM_VISIBILITY, _visibility ? _visibility->GetGPUVirtualAddress() : output);
			cmdList->SetComputeRootConstantBufferView(Pipeline::CULL_PARAM_FRAME, buffer->WriteFrameConstants(_frameIndex));

			const UINT group = Pipeline::CULL_THREAD_GROUP;

			// 1. 引数を写して InstanceCount = 0
			cmdList->SetPipelineState(_currentPipeline->GetCullClearPipeline());
			cmdList->Dispatch((frame.Written + group - 1) / group, 1, 1);

			// CSClear の書き込みが終わってから CSCull が数え始めるように
			D3D12_RESOURCE_BARRIER uavBarrier = {};
			uavBarrier.Type          = D3D12_RESOURCE_BARRIER_TYPE_UAV;
			uavBarrier.UAV.pResource = culled;
			cmdList->ResourceBarrier(1, &uavBarrier);

			// 2. 視錐台カリング
			cmdList->SetPipelineState(_currentPipeline->GetCullPipeline());
			cmdList->Dispatch((frame.Instances + group - 1) / group, 1, 1);

			// 3. 描画数の詰め直し (CSCull の数え終わりを待ってから、映った描画の引数だけを前から並べる)
			//    RTX 2070 / インスタンシングなし / 格子の中から : 20002 描画 → 4996 描画、GPU 0.137 → 0.115 ms
			//    (インスタンシングありでは空の描画が元々少なく、差はほぼ無い。全部映る場面でも負担は測れない程度)
			cmdList->ResourceBarrier(1, &uavBarrier);
			cmdList->SetPipelineState(_currentPipeline->GetCullCompactPipeline());
			cmdList->Dispatch((frame.Written + group - 1) / group, 1, 1);

			// 4. 描画が読む状態へ (Count バッファ / 読み戻しのコピー元にもする)
			const auto toRead = makeTransitionBarrier(culled, D3D12_RESOURCE_STATE_UNORDERED_ACCESS,
				D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT | D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_COPY_SOURCE);
			cmdList->ResourceBarrier(1, &toRead);
			_culledState = culledRead;
			if (stats)
			{
				// 読み戻し : 1 回目 (視錐台だけ / 前段) は [0〜3]、後段は [4〜7] に写す
				// (映った数 / 不透明 / 半透明 / 両面の不透明 の描画数。以前は 3 つ)。影の視点は読まない
				cmdList->CopyBufferRegion(frame.Readback->Get(), phase == 2 ? sizeof(uint32_t) * 4 : 0, culled,
					DRAW_ARGS_OFFSET + CULL_COUNTER_OFFSET, sizeof(uint32_t) * 4);
				frame.CullRecorded = true;
				frame.CullPhases   = phase == 0 ? 1 : 2;
			}

			// 描画用の PSO に戻す (ルートシグネチャはグラフィックス用とコンピュート用で別なので、描画側の設定はそのまま)
			cmdList->SetPipelineState(_currentPipeline->GetPipelineState());
		}

		bool CommandContext::createHiZ(uint32_t width, uint32_t height)
		{
			_hiZLevels = 0;
			_hiZ.reset();
			if (!_depthBuffer || width == 0 || height == 0) return false;

			ID3D12Device* dx12Device = _device->GetDevice()->GetDevice();

			// 深度バッファを作り直したので、次に Hi-Z を作る時に SRV を書き直す
			_depthWidth    = width;
			_depthHeight   = height;
			_depthSrvDirty = true;

			// Hi-Z の段 : 段 0 = 画面の半分 (切り上げ)、1x1 になるまで半分にしていく
			uint32_t w = (width + 1) / 2, h = (height + 1) / 2, offset = 0;
			while (_hiZLevels < MAX_HIZ_LEVELS)
			{
				_hiZMipWidth[_hiZLevels]  = w;
				_hiZMipHeight[_hiZLevels] = h;
				_hiZMipOffset[_hiZLevels] = offset;
				offset += w * h;
				_hiZLevels++;
				if (w == 1 && h == 1) break;
				w = (std::max)(1u, (w + 1) / 2);
				h = (std::max)(1u, (h + 1) / 2);
			}

			_hiZ = std::make_unique<DirectX12::ResourceBuffer>();
			if (FAILED(_hiZ->Create(dx12Device, static_cast<UINT64>(offset) * sizeof(float), D3D12_HEAP_TYPE_DEFAULT,
				D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS)))
			{
				_hiZ.reset();
				_hiZLevels = 0;
				return false;
			}
			return true;
		}

		void CommandContext::transitionHiZ(D3D12_RESOURCE_STATES after)
		{
			if (!_hiZ || _hiZState == after) return;
			const auto barrier = makeTransitionBarrier(_hiZ->Get(), _hiZState, after);
			_frameCommandList->GetCmdList()->ResourceBarrier(1, &barrier);
			_hiZState = after;
		}

		void CommandContext::buildHiZ(Buffer* buffer)
		{
			// ==============================================================
			// Hi-Z を作る (オクルージョンカリングの前段を描いた直後。後段はこの Hi-Z で判定する)
			//   1. 深度バッファをシェーダーが読む状態にする (DEPTH_WRITE → NON_PIXEL_SHADER_RESOURCE)
			//   2. 段 0 : 深度バッファの 2x2 の一番奥 / 段 1〜 : 前の段の 2x2 の一番奥
			//   3. 深度バッファを描画用に戻す (後段の描画が続く)
			//   (以前は深度バッファを CopyTextureRegion でバッファに写してから読んでいた)
			// (以前はシーンを描き終えた後に作って、次のフレームで使っていた。カメラが動くと 1 フレーム遅れていた)
			// ==============================================================
			auto cmdList = _frameCommandList->GetCmdList();
			if (!ensureDepthSrv(buffer)) return;   // ヒープが満杯 (Hi-Z を作らない)
			auto heapManager = buffer->GetDescHeapManager();

			// MSAA の深度に描いている (まだ解決していない) なら、MSAA の深度から直接作る
			const bool fromMsaa = _boundDepthMsaa && _msaaDepthDirty && hiZFromMsaa() && ensureMsaaDepthSrv(buffer);

			// 1. 深度バッファを読む状態へ
			if (fromMsaa) transitionMsaaDepth(D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
			else          transitionDepth(D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE);
			transitionHiZ(D3D12_RESOURCE_STATE_UNORDERED_ACCESS);

			// 2. 段ごとに縮小する
			cmdList->SetComputeRootSignature(_currentPipeline->GetHiZRootSignature());
			cmdList->SetComputeRootDescriptorTable(Pipeline::HIZ_PARAM_DEPTH, heapManager->GetSrvGpuHandle(fromMsaa ? _msaaDepthSrvIndex : _depthSrvIndex));
			cmdList->SetComputeRootUnorderedAccessView(Pipeline::HIZ_PARAM_HIZ,  _hiZ->GetGPUVirtualAddress());

			D3D12_RESOURCE_BARRIER uavBarrier = {};
			uavBarrier.Type          = D3D12_RESOURCE_BARRIER_TYPE_UAV;
			uavBarrier.UAV.pResource = _hiZ->Get();

			// 1 回の Dispatch で最大 HIZ_LEVELS_PER_PASS 段を作る (グループの中で 16→8→4→2→1 と縮める)
			// 以前は 1 段ごとに Dispatch + UAV バリアで、小さい段ほど GPU の待ちばかりだった
			// (RTX 2070 / 1280x720 : 11 回で約 0.047 ms → 3 回)
			const UINT group = Pipeline::HIZ_THREAD_GROUP;
			for (uint32_t first = 0; first < _hiZLevels; first += Pipeline::HIZ_LEVELS_PER_PASS)
			{
				const uint32_t count = (std::min)(Pipeline::HIZ_LEVELS_PER_PASS, _hiZLevels - first);

				// hiZ.shader の cbuffer HiZConstants と同じ並び
				uint32_t constants[Pipeline::HIZ_CONSTANT_COUNT] = {};
				if (first == 0)
				{
					constants[0] = _depthWidth;                                  // 読む段 = 深度バッファ
					constants[1] = _depthHeight;
					constants[2] = 0;
					constants[3] = 0;
					constants[4] = 1;                                            // 深度バッファから読む
				}
				else
				{
					constants[0] = _hiZMipWidth[first - 1];                      // 読む段 = 前の Dispatch で作った最後の段
					constants[1] = _hiZMipHeight[first - 1];
					constants[2] = _hiZMipOffset[first - 1];
					constants[3] = _hiZMipWidth[first - 1];
					constants[4] = 0;
				}
				constants[5] = count;
				constants[6] = fromMsaa ? _msaaDepthSamples : 1;                 // MSAA の深度のサンプル数
				// 段 0 を作る Dispatch だけ MSAA の深度から読む (以降は Hi-Z の前の段から読むので同じシェーダーでよい)
				cmdList->SetPipelineState(fromMsaa && first == 0 ? _currentPipeline->GetHiZPipelineMs() : _currentPipeline->GetHiZPipeline());
				for (uint32_t i = 0; i < count; ++i)                             // 作る段 (uint4 ずつ)
				{
					constants[8 + i * 4 + 0] = _hiZMipWidth[first + i];
					constants[8 + i * 4 + 1] = _hiZMipHeight[first + i];
					constants[8 + i * 4 + 2] = _hiZMipOffset[first + i];
				}

				cmdList->SetComputeRoot32BitConstants(Pipeline::HIZ_PARAM_CONSTANTS, Pipeline::HIZ_CONSTANT_COUNT, constants, 0);
				cmdList->Dispatch((_hiZMipWidth[first] + group - 1) / group, (_hiZMipHeight[first] + group - 1) / group, 1);
				cmdList->ResourceBarrier(1, &uavBarrier);   // 次の Dispatch はこの Dispatch の最後の段を読む
			}

			// 3. 深度バッファを描画用に戻す
			if (fromMsaa) transitionMsaaDepth(D3D12_RESOURCE_STATE_DEPTH_WRITE);
			else          transitionDepth(D3D12_RESOURCE_STATE_DEPTH_WRITE);

			// 描画用の PSO に戻す (この後、後段のカリングと描画が続く)
			cmdList->SetPipelineState(_currentPipeline->GetPipelineState());
		}

		bool CommandContext::ensureDepthSrv(Buffer* buffer)
		{
			// 深度バッファの SRV (ヒープに 1 個だけ確保し、深度バッファを作り直した時に書き直す)
			// Hi-Z と、全画面のシェーダー (ディファードのライティング / SSAO) が読む
			// リサイズの時は GPU の完了を待ってから深度バッファを作り直すので、使用中のディスクリプタを書き換えることは無い
			if (!_depthBuffer) return false;
			auto heapManager = buffer->GetDescHeapManager();
			if (_depthSrvIndex == DirectX12::DescriptorHeapManager::INVALID_INDEX)
			{
				_depthSrvIndex = heapManager->AllocateSrv();
				if (_depthSrvIndex == DirectX12::DescriptorHeapManager::INVALID_INDEX) return false;   // 満杯
				_depthSrvDirty = true;
			}
			if (_depthSrvDirty)
			{
				D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
				srvDesc.Format                  = DXGI_FORMAT_R32_FLOAT;   // 深度バッファ (R32_TYPELESS) を float として読む
				srvDesc.ViewDimension           = D3D12_SRV_DIMENSION_TEXTURE2D;
				srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
				srvDesc.Texture2D.MipLevels     = 1;
				_device->GetDevice()->GetDevice()->CreateShaderResourceView(_depthBuffer->Get(), &srvDesc, heapManager->GetSrvCpuHandle(_depthSrvIndex));
				_depthSrvDirty = false;
			}
			return true;
		}

		void CommandContext::warnTooManyDraws()
		{
			if (!_warnedTooManyDraws)
			{
				Core::Logger::Log("[CommandContext] Too many draws. Draws over MAX_DRAWS are skipped.");
				_warnedTooManyDraws = true;
			}
		}

		ID3D12GraphicsCommandList* CommandContext::GetFrameCommandList()const
		{
			return (_frameCommandList && _frameCommandList->IsRecording()) ? _frameCommandList->GetCmdList() : nullptr;
		}

		void CommandContext::BindBackBufferWithoutDepth()
		{
			if (!_frameCommandList || !_frameCommandList->IsRecording() || !_currentBackBuffer) return;

			const auto rtvHandle = _rtvHeap->GetCpuHandle(_swapChain->GetCurrentBackBufferIndex());
			_frameCommandList->GetCmdList()->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);
		}

		void CommandContext::MarkGpuSceneEnd()
		{
			if (_gpuTimer && _frameCommandList && _frameCommandList->IsRecording())
			{
				_gpuTimer->Timestamp(_frameCommandList->GetCmdList(), _frameIndex, TIMESTAMP_SCENE_END);
			}
		}

		// ==============================================================
		// アップロード
		// ==============================================================

		void CommandContext::SubmitUploads()
		{
			if (!_uploadManager || !_commandQueue)
			{
				return;
			}

			const UINT64 uploadFence = _uploadManager->Flush();
			_stats.UploadBytes    = _uploadManager->GetLastFlushBytes();
			_stats.UploadRingUsed = _uploadManager->GetRingUsedBytes();
			_stats.UploadRingSize = _uploadManager->GetRingSize();

			// まだ描画キューに待たせていない転送があれば、GPU 上で待たせる
			// (ID3D12CommandQueue::Wait は GPU 側の待機。CPU はすぐに戻る)
			if (uploadFence > _uploadFenceWaited)
			{
				_commandQueue->Get()->Wait(_uploadManager->GetFence(), uploadFence);
				_uploadFenceWaited = uploadFence;
			}
		}

		// ==============================================================
		// その他
		// ==============================================================

		bool CommandContext::Resize(uint32_t width, uint32_t height)
		{
			if (!_swapChain || !_swapChain->Get())
			{
				return false;
			}

			// 最小化中はサイズ 0 が来る。作り直さずにそのまま (表示は止まる)
			if (width == 0 || height == 0)
			{
				return true;
			}
			if (width == _swapChain->GetWidth() && height == _swapChain->GetHeight())
			{
				return true;
			}

			// 1. GPU が古いバックバッファ・深度バッファを使い終わるまで待つ
			WaitForGpu();

			// 2. バックバッファへの参照を手放す (残っていると ResizeBuffers が失敗する)
			_currentBackBuffer = nullptr;
			_depthBuffer.reset();
			_msaaDepth.Reset();        // MSAA の深度は次に使う時に新しい大きさで作る
			_msaaDepthSamples = 0;

			// 3. スワップチェーンを作り直して、RTV と深度バッファも作り直す
			HRESULT hr = _swapChain->Resize(width, height);
			if (FAILED(hr)) { logFailed("SwapChain::Resize", hr); return false; }

			if (!createRenderTargetViews())        return false;
			if (!createDepthBuffer(width, height)) return false;

			// 画面に合わせるレンダーターゲット (Scale > 0) を作り直す (RTV / SRV の番号はそのまま)
			for (uint32_t i = 0; i < _renderTargets.size(); ++i)
			{
				RenderTarget& target = _renderTargets[i];
				if (target.Alive && target.Scale > 0.0f && !createRenderTargetResource(target, i)) return false;
			}
			return true;
		}

		void CommandContext::WaitForGpu()
		{
			// アップロードを送信して描画キューに待たせてから、描画キューの完了を待つ
			// → 描画キューが終われば、その前のアップロードも終わっている
			SubmitUploads();

			if (_fence && _fence->Get() && _commandQueue && _commandQueue->Get())
			{
				_fence->WaitForSignal(_commandQueue.get());
			}
			if (_uploadManager)
			{
				_uploadManager->WaitIdle();
			}
		}

		void CommandContext::Dispose()
		{
			// 以前は何もしておらず、GPUが使用中のまま破棄されていた (2-11)
			WaitForGpu();

			// 記録中のまま破棄しないように閉じておく
			if (_frameCommandList)     _frameCommandList->Close();

			// 描画先 → スワップチェーン → コマンドリスト → アロケーター → フェンス → キュー の順に破棄
			_currentBackBuffer = nullptr;
			_gpuTimer.reset();
			for (auto& frame : _draws)
			{
				if (frame.Staging) frame.Staging->Unmap();
				frame = DrawFrame();   // Culled / Readback もここで解放される
			}
			_visibility.reset();
			_drawIdBuffer.reset();
			_renderTargets.clear();
			_freeRenderTargets.clear();
			_targetRtvHeap.reset();
			_heapManager = nullptr;
			_currentPipeline = nullptr;
			_depthBuffer.reset();
			_msaaDepth.Reset();
			_msaaDepthSamples = 0;
			_dsvHeap.reset();
			_rtvHeap.reset();
			_swapChain.reset();
			_frameCommandList.reset();
			_uploadManager.reset();
			_uploadFenceWaited = 0;
			for (auto& frame : _frames)
			{
				frame.Allocator.reset();
				frame.FenceValue = 0;
			}
			_fence.reset();
			_commandQueue.reset();
			_device = nullptr;
		}

		RHI::DirectX12::UploadManager* CommandContext::GetUploadManager()const
		{
			return _uploadManager.get();
		}
		RHI::DirectX12::CommandQueue* CommandContext::GetCommandQueue()const
		{
			return _commandQueue.get();
		}
		RHI::DirectX12::SwapChain* CommandContext::GetSwapChain()const
		{
			return _swapChain.get();
		}

		uint32_t CommandContext::GetWidth()const
		{
			return _swapChain ? _swapChain->GetWidth() : 0;
		}

		uint32_t CommandContext::GetHeight()const
		{
			return _swapChain ? _swapChain->GetHeight() : 0;
		}

		bool CommandContext::createRenderTargetViews()
		{
			ID3D12Device* dx12Device = _device->GetDevice()->GetDevice();

			// バックバッファの数だけ RTV を作る (以前は Buffer 側で作っていた)
			for (UINT i = 0; i < DirectX12::SwapChain::BUFFER_COUNT; ++i)
			{
				// スワップチェーンが保持しているので参照カウントは増えない (Release不要)
				ID3D12Resource* backBuffer = _swapChain->GetBufferFromIndex(i);
				if (!backBuffer)
				{
					Core::Logger::Log("[CommandContext] Back buffer is null.");
					return false;
				}
				dx12Device->CreateRenderTargetView(backBuffer, nullptr, _rtvHeap->GetCpuHandle(i));
			}
			return true;
		}

		bool CommandContext::createDepthBuffer(uint32_t width, uint32_t height)
		{
			ID3D12Device* dx12Device = _device->GetDevice()->GetDevice();

			_depthBuffer = std::make_unique<DirectX12::ResourceBuffer>();
			// オクルージョンカリングの Hi-Z を作るのにシェーダーから読むので、読める深度バッファにする
			HRESULT hr = _depthBuffer->CreateDepthStencil(dx12Device, width, height, DEPTH_FORMAT, DEPTH_CLEAR_VALUE, true);
			_depthState = D3D12_RESOURCE_STATE_DEPTH_WRITE;
			if (FAILED(hr)) { logFailed("CreateDepthStencil", hr); return false; }

			D3D12_DEPTH_STENCIL_VIEW_DESC dsvDesc = {};
			dsvDesc.Format        = DEPTH_FORMAT;
			dsvDesc.ViewDimension = D3D12_DSV_DIMENSION_TEXTURE2D;
			dsvDesc.Flags         = D3D12_DSV_FLAG_NONE;
			dx12Device->CreateDepthStencilView(_depthBuffer->Get(), &dsvDesc, _dsvHeap->GetCpuHandle(0));

			// オクルージョンカリング用 (深度の大きさが変わるので作り直す。作れなくてもオクルージョンカリングしないだけ)
			if (!createHiZ(width, height))
			{
				Core::Logger::Log("[CommandContext] Failed create Hi-Z buffer. Occlusion culling disabled.");
			}
			return true;
		}

		void CommandContext::logDeviceRemovedReason(HRESULT presentResult)
		{
			logFailed("SwapChain::Present", presentResult);

			// デバイスロストの場合は原因を取得して出す (ドライバーのクラッシュ / TDR / 不正なコマンド 等)
			if (presentResult == DXGI_ERROR_DEVICE_REMOVED || presentResult == DXGI_ERROR_DEVICE_RESET)
			{
				if (_device && _device->GetDevice() && _device->GetDevice()->GetDevice())
				{
					HRESULT reason = _device->GetDevice()->GetDevice()->GetDeviceRemovedReason();
					logFailed("Device removed. Reason", reason);
				}
			}
		}
	}
}

/************************************
* End of file
************************************/
