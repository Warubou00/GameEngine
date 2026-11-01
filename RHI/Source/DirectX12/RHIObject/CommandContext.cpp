
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
// ========================================

#include <d3d12.h>
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

			// 1. CPU が先行しすぎていれば待つ (表示待ちのフレームが FRAME_COUNT 個を超えないように)
			_swapChain->WaitForNextFrame();

			// 2. このフレームの FrameContext を GPU が使い終わるまで待つ
			//    FRAME_COUNT フレーム前の処理なので、通常はすでに終わっていて待たない
			FrameContext& frame = _frames[_frameIndex];
			HRESULT hr = _fence->WaitForValue(frame.FenceValue);
			if (FAILED(hr)) { logFailed("Fence::WaitForValue", hr); return false; }

			// 3. アロケーター → コマンドリストの順にリセット (GPU が使い終わったので安全)
			hr = frame.Allocator->Reset();
			if (FAILED(hr)) { logFailed("CommandAllocator::Reset(Frame)", hr); return false; }

			hr = _frameCommandList->Reset(frame.Allocator->Get(), pipeline->GetPipelineState());
			if (FAILED(hr)) { logFailed("CommandList::Reset(Frame)", hr); return false; }

			auto cmdList = _frameCommandList->GetCmdList();

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

			return true;
		}

		bool CommandContext::RenderEnd()
		{
			if (!_currentBackBuffer || !_frameCommandList->IsRecording())
			{
				// RenderStart が失敗している
				return false;
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
			hr = _swapChain->Present(_syncInterval);
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

		void CommandContext::Render(Buffer* buffer, const RenderQueue& renderQueue)
		{
			if (!buffer || !_frameCommandList->IsRecording())
			{
				return;
			}

			auto cmdList = _frameCommandList->GetCmdList();
			const D3D12_GPU_VIRTUAL_ADDRESS sceneCbBase = buffer->GetSceneCB()->GetGPUVirtualAddress();

			// 同じ値を何度もセットしないように前回の値を覚えておく (ルート引数の変更は描画ごとのコストになる)
			UINT lastBaseVertex = UINT_MAX;

			for (const auto& renderObj : renderQueue.RenderObjects)
			{
				// 頂点オフセット (b0)
				// シェーダーは g_MegaVB[g_BaseVertexIndex + SV_VertexID] で頂点を読む
				if (renderObj.VertexOffset != lastBaseVertex)
				{
					UINT baseVertexIndex = renderObj.VertexOffset;
					cmdList->SetGraphicsRoot32BitConstants(Pipeline::ROOT_PARAM_BASE_VERTEX, 1, &baseVertexIndex, 0);
					lastBaseVertex = baseVertexIndex;
				}

				// Scene(Object)定数バッファ (b2)
				// CbOffset は Root CBV の制約で 256 の倍数であること (Buffer::SCENE_CB_STRIDE 単位で指定する)
				cmdList->SetGraphicsRootConstantBufferView(Pipeline::ROOT_PARAM_OBJECT_CBV, sceneCbBase + renderObj.CbOffset);

				// インデックスバッファを使用
				// BaseVertexLocation(第4引数)は SV_VertexID に加算されない。
				// 頂点の開始位置はルート定数で渡しているので、ここは 0 にしておく (2-9)
				cmdList->DrawIndexedInstanced(
					renderObj.IndexCount,
					1,
					renderObj.IndexOffset,
					0,
					0
				);
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
