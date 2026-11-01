
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

			// 画面クリア色
			constexpr float CLEAR_COLOR[] = { 0.1f, 0.2f, 0.4f, 1.0f };

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
			hr = _dsvHeap->Create(dx12Device, D3D12_DESCRIPTOR_HEAP_TYPE_DSV, 1);
			if (FAILED(hr)) { logFailed("CreateDescriptorHeap(DSV)", hr); return RHI_FAILED_CREATE_ANY_OBJECT; }

			if (!createRenderTargetViews())     return RHI_FAILED_CREATE_ANY_OBJECT;
			if (!createDepthBuffer(width, height)) return RHI_FAILED_CREATE_ANY_OBJECT;

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
						FAILED(frame.Readback->Create(dx12Device, 16, D3D12_HEAP_TYPE_READBACK)))
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
					const D3D12_RANGE readRange = { 0, sizeof(uint32_t) * 4 };
					if (SUCCEEDED(draws.Readback->Get()->Map(0, &readRange, &mapped)))
					{
						// [0][1] : 1 回目 (視錐台だけ / 前段) の映ったオブジェクト数・描画数、[2][3] : 後段
						const uint32_t* counters = static_cast<const uint32_t*>(mapped);
						_stats.VisibleInstances = counters[0] + (draws.CullPhases == 2 ? counters[2] : 0);
						_stats.VisibleDraws     = counters[1] + (draws.CullPhases == 2 ? counters[3] : 0);
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

			// 6. 描画先の設定とクリア (色 + 深度)
			const auto rtvHandle = _rtvHeap->GetCpuHandle(backBufferIndex);
			const auto dsvHandle = _dsvHeap->GetCpuHandle(0);
			cmdList->OMSetRenderTargets(1, &rtvHandle, FALSE, &dsvHandle);
			cmdList->ClearRenderTargetView(rtvHandle, CLEAR_COLOR, 0, nullptr);
			// 深度は生成時の最適化クリア値と同じ値でクリアする (違うと高速クリアが効かない)
			cmdList->ClearDepthStencilView(dsvHandle, D3D12_CLEAR_FLAG_DEPTH, DEPTH_CLEAR_VALUE, 0, 0, nullptr);

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

		void CommandContext::Render(Buffer* buffer, const RHI::DrawItem* items, uint32_t count)
		{
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
			const D3D12_GPU_VIRTUAL_ADDRESS objectData = buffer->WriteObjectConstants(_frameIndex, cmdList);
			cmdList->SetGraphicsRootShaderResourceView(Pipeline::ROOT_PARAM_OBJECT_DATA_SRV, objectData);

			// 描画ごとのデータ (DrawData) と引数を用意する (描画リストが同じなら前回のものを使い回す)
			const bool reused = updateDraws(frame, buffer, items, count);

			ID3D12CommandSignature* signature = _currentPipeline ? _currentPipeline->GetIndirectDrawSignature() : nullptr;
			const bool indirect = _useIndirectDraw && signature;
			// GPU カリング : ExecuteIndirect の時だけ (結果の引数を GPU が作るので、1 個ずつ描く時は使えない)
			const bool culling  = indirect && _useGpuCulling && frame.Culled && frame.Written > 0 &&
			                      _currentPipeline->IsGpuCullingAvailable();

			// オクルージョンカリング (2 段階) : GPU カリングが前提
			const bool occlusion = culling && _useOcclusion && _hiZ && _visibility && _currentPipeline->IsHiZAvailable();

			// 描画が読む DrawData と引数 : カリングした時はその結果、しない時は CPU が並べたもの
			ID3D12Resource* drawSource = culling ? frame.Culled->Get() : frame.Gpu->Get();

			// カリングの結果 (映った描画だけを詰めた引数) を、GPU が数えた数 (Count バッファ) だけ描く
			// frame.Written は上限 (詰めた数がこれを超えることは無い)
			// (以前は映らなかった描画も InstanceCount = 0 のまま全部渡していて、GPU が 1 個ずつ読み飛ばしていた)
			auto drawCulled = [&]()
			{
				cmdList->SetGraphicsRootShaderResourceView(Pipeline::ROOT_PARAM_DRAW_DATA_SRV, drawSource->GetGPUVirtualAddress());
				cmdList->ExecuteIndirect(signature, frame.Written, drawSource, COMPACT_ARGS_OFFSET,
					drawSource, DRAW_ARGS_OFFSET + CULL_COUNTER_OFFSET + sizeof(uint32_t));
			};

			if (occlusion)
			{
				// ==============================================================
				// 2 段階のオクルージョンカリング
				//   前段 : 前のフレームで見えていた物を描く (視錐台だけで判定)
				//   Hi-Z : 前段の深度から作る (このフレームのカメラなので、カメラが動いても遅れない)
				//   後段 : 全部を視錐台と Hi-Z で判定し直して可視を記録し、前段で描かなかった物を描く
				//   描画リストが変わった時 / 前のフレームでオクルージョンしていない時は、可視の記録が使えないので
				//   前段は何も描かず、後段で全部を判定する (そのフレームだけ Hi-Z は空 = 何も隠れない)
				// ==============================================================
				const bool resetVisibility = !reused || !_visibilityValid;
				recordCulling(frame, buffer, objectData, 1, resetVisibility);
				drawCulled();
				buildHiZ();
				recordCulling(frame, buffer, objectData, 2, resetVisibility);
				drawCulled();
				_visibilityValid = true;
			}
			else
			{
				if (culling) recordCulling(frame, buffer, objectData, 0, true);
				_visibilityValid = false;   // 可視の記録は更新されないので、次にオクルージョンする時は使わない

				if (frame.Written > 0)
				{
					if (culling)
					{
						drawCulled();
					}
					else if (indirect)
					{
						// 引数バッファ (VRAM) に並べた分を 1 回の命令で描く。GPU は描画の引数を読むだけ (ルート引数は変えない)
						// 書き直さなかったフレームは COMMON → INDIRECT_ARGUMENT に暗黙で昇格するのでバリアは不要
						cmdList->SetGraphicsRootShaderResourceView(Pipeline::ROOT_PARAM_DRAW_DATA_SRV, drawSource->GetGPUVirtualAddress());
						cmdList->ExecuteIndirect(signature, frame.Written, drawSource, DRAW_ARGS_OFFSET, nullptr, 0);
					}
					else
					{
						// 1 個ずつ描く。描画ごとのルート引数の設定は無い (以前は描画ごとにルート定数とルート CBV を設定していた)
						// BaseVertexLocation (第4引数) は SV_VertexID に加算されないので 0。頂点の開始位置は DrawData で渡す (2-9)
						cmdList->SetGraphicsRootShaderResourceView(Pipeline::ROOT_PARAM_DRAW_DATA_SRV, drawSource->GetGPUVirtualAddress());
						for (const auto& args : frame.Args)
						{
							cmdList->DrawIndexedInstanced(args.IndexCountPerInstance, args.InstanceCount, args.StartIndexLocation, 0, args.StartInstanceLocation);
						}
					}
				}
			}
			_stats.OcclusionCulling = occlusion;

			_stats.IndirectDraw       = indirect;
			_stats.IndirectArgsReused = reused;
			_stats.DrawCalls          = frame.Written;
			_stats.Instances          = frame.Instances;
			_stats.Instancing         = frame.Instanced;
			_stats.GpuCulling         = culling;
			_stats.Triangles          = frame.Triangles;
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
			const uint32_t instances = _useInstancing ? writeDrawsInstanced(frame, buffer, items, count) : writeDrawsDirect(frame, buffer, items, count);
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

		uint32_t CommandContext::writeDrawsDirect(DrawFrame& frame, Buffer* buffer, const RHI::DrawItem* items, uint32_t count)
		{
			// DrawItem 1 個 = 描画 1 回 (InstanceCount = 1)
			auto* drawData = reinterpret_cast<DrawData*>(frame.Cpu);
			auto* cullData = reinterpret_cast<CullInstance*>(frame.Cpu + CULL_DATA_OFFSET);
			uint32_t written = 0;
			for (uint32_t i = 0; i < count; ++i)
			{
				const RHI::DrawItem& item = items[i];
				if (!item.Object.IsValid() || item.IndexCount == 0) continue;
				if (written >= MAX_DRAWS) { warnTooManyDraws(); break; }

				// 描画番号 written の描画 : シェーダーは DrawData[written] を読む
				drawData[written] = { item.Object.Index, item.BaseVertex };
				// GPU カリング用 : 境界球と、属する描画 (= 自分)
				const ::DirectX::XMFLOAT4 sphere = buffer->GetVertexBounds(item.BaseVertex);
				cullData[written] = { { sphere.x, sphere.y, sphere.z, sphere.w }, written };

				D3D12_DRAW_INDEXED_ARGUMENTS args;
				args.IndexCountPerInstance = item.IndexCount;
				args.InstanceCount         = 1;
				args.StartIndexLocation    = item.FirstIndex;
				args.BaseVertexLocation    = 0;         // SV_VertexID に加算されないので 0 (2-9)
				args.StartInstanceLocation = written;   // = 描画番号 (DRAWID)
				frame.Args.push_back(args);

				++written;
				frame.Triangles += item.IndexCount / 3;
			}
			return written;
		}

		uint32_t CommandContext::writeDrawsInstanced(DrawFrame& frame, Buffer* buffer, const RHI::DrawItem* items, uint32_t count)
		{
			// ==============================================================
			// インスタンシング : 同じメッシュの DrawItem を、最大 INSTANCE_BATCH_SIZE 個ずつ 1 回の描画にまとめる
			//   1. DrawItem を前から順に見て、そのメッシュの書き込み中のまとまりに入れる
			//      (まとまりが無い / いっぱいなら新しく作る)。ここでは数えるだけ
			//   2. まとまりを作られた順 (= 元の DrawItem の順番に近い) に並べ、DrawData の連続した範囲を割り当てる
			//   3. 描画の引数はまとまりごとに 1 個 : InstanceCount = 個数, StartInstanceLocation = 範囲の先頭
			//   4. 各 DrawItem の DrawData (と GPU カリング用の境界球・描画の番号) を、自分のまとまりの範囲に詰めて書く
			// 描画番号バッファは 0, 1, 2, ... なので、インスタンス j には DRAWID = Start + j が届き、
			// シェーダーはそのまま DrawData[Start + j] (= j 個目のオブジェクト) を読める。シェーダーの変更は不要
			// 不透明なので、描く順番が少し変わっても見た目は同じ
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

				const MeshKey key = { item.BaseVertex, item.FirstIndex, item.IndexCount };
				auto open = _openBatch.find(key);
				if (open == _openBatch.end() || _batches[open->second].Count >= INSTANCE_BATCH_SIZE)
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

			// 3. 描画の引数 (まとまりごとに 1 個)
			for (auto& batch : _batches)
			{
				if (batch.Count == 0) continue;
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

			// 4. DrawData と GPU カリング用のデータを書く
			auto* drawData = reinterpret_cast<DrawData*>(frame.Cpu);
			auto* cullData = reinterpret_cast<CullInstance*>(frame.Cpu + CULL_DATA_OFFSET);
			for (uint32_t i = 0; i < count; ++i)
			{
				if (_itemBatch[i] == UINT32_MAX) continue;
				InstanceBatch& batch = _batches[_itemBatch[i]];
				if (batch.Cursor >= batch.Start + batch.Count) continue;   // 入りきらなかった分
				cullData[batch.Cursor] = { { batch.Sphere.x, batch.Sphere.y, batch.Sphere.z, batch.Sphere.w }, batch.Draw };
				drawData[batch.Cursor] = { items[i].Object.Index, items[i].BaseVertex };
				batch.Cursor++;
			}
			return slot;
		}

		void CommandContext::recordCulling(DrawFrame& frame, Buffer* buffer, D3D12_GPU_VIRTUAL_ADDRESS objectData, uint32_t phase, bool resetVisibility)
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
			const ::DirectX::XMFLOAT4* planes = buffer->GetFrustumPlanes();
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
			static_assert(sizeof(CullConstants) == Pipeline::CULL_CONSTANT_COUNT * sizeof(uint32_t), "CullConstants size mismatch");

			// Culled : 1 回目はフレームの始めの COMMON から、後段は前段の描画が読み終わってから書き直す
			// (この遷移バリアで、前段の ExecuteIndirect と頂点シェーダーの読み込みが終わるのを待つ)
			const D3D12_RESOURCE_STATES culledRead =
				D3D12_RESOURCE_STATE_INDIRECT_ARGUMENT | D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE | D3D12_RESOURCE_STATE_COPY_SOURCE;
			const auto toUav = makeTransitionBarrier(culled, phase == 2 ? culledRead : D3D12_RESOURCE_STATE_COMMON, D3D12_RESOURCE_STATE_UNORDERED_ACCESS);
			cmdList->ResourceBarrier(1, &toUav);
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
			// 読み戻し : 1 回目 (視錐台だけ / 前段) は [0][1]、後段は [2][3] に写す
			cmdList->CopyBufferRegion(frame.Readback->Get(), phase == 2 ? sizeof(uint32_t) * 2 : 0, culled,
				DRAW_ARGS_OFFSET + CULL_COUNTER_OFFSET, sizeof(uint32_t) * 2);
			frame.CullRecorded = true;
			frame.CullPhases   = phase == 0 ? 1 : 2;

			// 描画用の PSO に戻す (ルートシグネチャはグラフィックス用とコンピュート用で別なので、描画側の設定はそのまま)
			cmdList->SetPipelineState(_currentPipeline->GetPipelineState());
		}

		bool CommandContext::createHiZ(uint32_t width, uint32_t height)
		{
			_hiZLevels = 0;
			_depthCopy.reset();
			_hiZ.reset();
			if (!_depthBuffer || width == 0 || height == 0) return false;

			ID3D12Device* dx12Device = _device->GetDevice()->GetDevice();

			// 深度の写し : CopyTextureRegion で写せる並び (1 行は 256byte 境界) をデバイスに聞く
			const D3D12_RESOURCE_DESC depthDesc = _depthBuffer->Get()->GetDesc();
			UINT64 copySize = 0;
			dx12Device->GetCopyableFootprints(&depthDesc, 0, 1, 0, &_depthFootprint, nullptr, nullptr, &copySize);
			_depthCopy = std::make_unique<DirectX12::ResourceBuffer>();
			if (FAILED(_depthCopy->Create(dx12Device, copySize, D3D12_HEAP_TYPE_DEFAULT)))
			{
				_depthCopy.reset();
				return false;
			}

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
				_depthCopy.reset();
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

		void CommandContext::buildHiZ()
		{
			// ==============================================================
			// Hi-Z を作る (オクルージョンカリングの前段を描いた直後。後段はこの Hi-Z で判定する)
			//   1. 深度バッファをバッファに写す (DEPTH_WRITE → COPY_SOURCE → 写す → DEPTH_WRITE に戻す)
			//   2. 段 0 : 深度の写しの 2x2 の一番奥 / 段 1〜 : 前の段の 2x2 の一番奥
			// (以前はシーンを描き終えた後に作って、次のフレームで使っていた。カメラが動くと 1 フレーム遅れていた)
			// ==============================================================
			auto cmdList = _frameCommandList->GetCmdList();
			ID3D12Resource* depth = _depthBuffer->Get();

			// 1. 深度を写す
			const auto toCopy = makeTransitionBarrier(depth, D3D12_RESOURCE_STATE_DEPTH_WRITE, D3D12_RESOURCE_STATE_COPY_SOURCE);
			cmdList->ResourceBarrier(1, &toCopy);

			D3D12_TEXTURE_COPY_LOCATION dst = {};
			dst.pResource       = _depthCopy->Get();
			dst.Type            = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
			dst.PlacedFootprint = _depthFootprint;
			D3D12_TEXTURE_COPY_LOCATION src = {};
			src.pResource        = depth;
			src.Type             = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
			src.SubresourceIndex = 0;
			cmdList->CopyTextureRegion(&dst, 0, 0, 0, &src, nullptr);

			// 深度は描画用に戻す / 写しはシェーダーが読む状態へ (コピーで COMMON → COPY_DEST に暗黙で昇格している)
			D3D12_RESOURCE_BARRIER barriers[2] = {
				makeTransitionBarrier(depth, D3D12_RESOURCE_STATE_COPY_SOURCE, D3D12_RESOURCE_STATE_DEPTH_WRITE),
				makeTransitionBarrier(_depthCopy->Get(), D3D12_RESOURCE_STATE_COPY_DEST, D3D12_RESOURCE_STATE_NON_PIXEL_SHADER_RESOURCE),
			};
			cmdList->ResourceBarrier(2, barriers);
			transitionHiZ(D3D12_RESOURCE_STATE_UNORDERED_ACCESS);

			// 2. 段ごとに縮小する
			cmdList->SetComputeRootSignature(_currentPipeline->GetHiZRootSignature());
			cmdList->SetPipelineState(_currentPipeline->GetHiZPipeline());
			cmdList->SetComputeRootShaderResourceView(Pipeline::HIZ_PARAM_DEPTH, _depthCopy->GetGPUVirtualAddress());
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
					constants[0] = _depthFootprint.Footprint.Width;              // 読む段 = 深度の写し
					constants[1] = _depthFootprint.Footprint.Height;
					constants[2] = 0;
					constants[3] = _depthFootprint.Footprint.RowPitch / sizeof(float);
					constants[4] = 1;                                            // 深度の写しから読む
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

			// 描画用の PSO に戻す (この後、後段のカリングと描画が続く)
			cmdList->SetPipelineState(_currentPipeline->GetPipelineState());
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

			// 3. スワップチェーンを作り直して、RTV と深度バッファも作り直す
			HRESULT hr = _swapChain->Resize(width, height);
			if (FAILED(hr)) { logFailed("SwapChain::Resize", hr); return false; }

			if (!createRenderTargetViews())        return false;
			if (!createDepthBuffer(width, height)) return false;

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
			_currentPipeline = nullptr;
			_depthBuffer.reset();
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
			HRESULT hr = _depthBuffer->CreateDepthStencil(dx12Device, width, height, DEPTH_FORMAT, DEPTH_CLEAR_VALUE);
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
