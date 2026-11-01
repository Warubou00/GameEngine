
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
#include "../dx12_descriptorHeapManager.h"
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
				std::string message = std::string("[CommandContext] ") + what + " failed. HRESULT = 0x";
				char code[16] = {};
				sprintf_s(code, "%08X", static_cast<unsigned int>(hr));
				message += code;
				Core::Logger::Log(message.c_str());
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

			_commandQueue        = std::make_unique<DirectX12::CommandQueue>();
			_commandAllocator    = std::make_unique<DirectX12::CommandAllocator>();
			_graphicsCommandList = std::make_unique<DirectX12::GraphicsCommandList>();
			_swapChain           = std::make_unique<DirectX12::SwapChain>();
			_fence               = std::make_unique<DirectX12::Fence>();

			ID3D12Device* dx12Device = device->GetDevice()->GetDevice();

			// 以前は HRESULT を全て無視して常に成功を返していた (1-15)
			HRESULT hr = _commandQueue->Create(dx12Device);
			if (FAILED(hr)) { logFailed("CreateCommandQueue", hr); return RHI_FAILED_CREATE_ANY_OBJECT; }

			hr = _commandAllocator->Create(dx12Device);
			if (FAILED(hr)) { logFailed("CreateCommandAllocator", hr); return RHI_FAILED_CREATE_ANY_OBJECT; }

			// 生成直後のコマンドリストは記録中の状態 (初期化中のアップロードをそのまま積めるようにしている)
			hr = _graphicsCommandList->Create(dx12Device, _commandAllocator->Get());
			if (FAILED(hr)) { logFailed("CreateCommandList", hr); return RHI_FAILED_CREATE_ANY_OBJECT; }

			HWND hWnd   = static_cast<HWND>(info->HWnd);
			UINT width  = static_cast<UINT>(info->Width);
			UINT height = static_cast<UINT>(info->Height);
			hr = _swapChain->Create(device->GetDxgiFactory()->Get(), _commandQueue->Get(), &hWnd, width, height);
			if (FAILED(hr)) { logFailed("CreateSwapChain", hr); return RHI_FAILED_CREATE_ANY_OBJECT; }

			hr = _fence->Create(dx12Device);
			if (FAILED(hr)) { logFailed("CreateFence", hr); return RHI_FAILED_CREATE_ANY_OBJECT; }

			// VSync設定 (以前は無視して常に Present(1, 0) だった : 2-5)
			_syncInterval = info->EnabledVSync ? 1 : 0;

			return RHI_SUCCEEDED;
		}

		bool CommandContext::BeginRecording()
		{
			if (!_graphicsCommandList || !_commandAllocator)
			{
				return false;
			}

			// 記録中ならそのまま積み足す
			if (_graphicsCommandList->IsRecording())
			{
				return true;
			}

			// 以前は記録中のコマンドリストがある状態でアロケーターをリセットしていた (1-10)
			// → Close済みであることを確認してから、アロケーター → コマンドリストの順でリセットする
			HRESULT hr = _commandAllocator->Reset();
			if (FAILED(hr)) { logFailed("CommandAllocator::Reset", hr); return false; }

			hr = _graphicsCommandList->Reset(_commandAllocator->Get(), nullptr);
			if (FAILED(hr)) { logFailed("CommandList::Reset", hr); return false; }

			return true;
		}

		bool CommandContext::RenderStart(Pipeline* pipeline, Buffer* buffer)
		{
			if (!pipeline || !buffer || !_swapChain || !_swapChain->Get())
			{
				return false;
			}

			// 1. 記録の開始 (アロケータとコマンドリストのリセット)
			if (!BeginRecording())
			{
				return false;
			}
			auto cmdList = _graphicsCommandList->GetCmdList();
			cmdList->SetPipelineState(pipeline->GetPipelineState());

			// 2. ビューポートとシザー矩形の設定 (スワップチェーンのサイズに合わせる。以前は1280x720固定だった)
			const UINT width  = _swapChain->GetWidth();
			const UINT height = _swapChain->GetHeight();
			D3D12_VIEWPORT viewport = { 0.0f, 0.0f, static_cast<float>(width), static_cast<float>(height), 0.0f, 1.0f };
			D3D12_RECT scissorRect  = { 0, 0, static_cast<LONG>(width), static_cast<LONG>(height) };
			cmdList->RSSetViewports(1, &viewport);
			cmdList->RSSetScissorRects(1, &scissorRect);

			// 3. リソースバリア (Present -> RenderTarget)
			const uint32_t frameBufferIndex = _swapChain->GetCurrentBackBufferIndex();
			_currentBackBuffer = _swapChain->GetBackBuffer();
			if (!_currentBackBuffer)
			{
				return false;
			}
			const auto toRenderTarget = makeTransitionBarrier(_currentBackBuffer, D3D12_RESOURCE_STATE_PRESENT, D3D12_RESOURCE_STATE_RENDER_TARGET);
			cmdList->ResourceBarrier(1, &toRenderTarget);

			// 4. RTVの設定と画面クリア
			auto rtvHandle = buffer->GetDescHeapManager()->GetRtvCpuHandle(frameBufferIndex);
			cmdList->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);
			const float clearColor[] = { 0.1f, 0.2f, 0.4f, 1.0f };
			cmdList->ClearRenderTargetView(rtvHandle, clearColor, 0, nullptr);

			// ==============================================================
			// 5. ルートシグネチャとヒープのセット
			// ==============================================================
			cmdList->SetGraphicsRootSignature(pipeline->GetRootSignature());

			// メガVBやテクスチャのSRVが登録されているヒープをセット
			auto heapManager = buffer->GetDescHeapManager();
			ID3D12DescriptorHeap* heaps[] = { heapManager->GetSrvHeap() };
			cmdList->SetDescriptorHeaps(1, heaps);

			// メガVBのSRVテーブル (t0)
			cmdList->SetGraphicsRootDescriptorTable(Pipeline::ROOT_PARAM_VERTEX_BUFFER_TABLE, heapManager->GetSrvGpuHandle(Buffer::SRV_INDEX_SCENE_VB));

			// Global定数バッファ (b1) を Root CBV としてセット (View / Projection 行列)
			cmdList->SetGraphicsRootConstantBufferView(Pipeline::ROOT_PARAM_GLOBAL_CBV, buffer->GetCommonCB()->GetGPUVirtualAddress());

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
			if (!_currentBackBuffer || !_graphicsCommandList->IsRecording())
			{
				// RenderStart が失敗している
				return false;
			}

			// 6. リソースバリア (RenderTarget -> Present)
			const auto toPresent = makeTransitionBarrier(_currentBackBuffer, D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_PRESENT);
			_graphicsCommandList->GetCmdList()->ResourceBarrier(1, &toPresent);
			_currentBackBuffer = nullptr;

			// 7. コマンドリストのクローズと実行
			HRESULT hr = _graphicsCommandList->Close();
			if (FAILED(hr)) { logFailed("CommandList::Close", hr); return false; }

			ID3D12CommandList* ppCommandLists[] = { _graphicsCommandList->GetCmdList() };
			_commandQueue->Get()->ExecuteCommandLists(1, ppCommandLists);

			// 8. 画面への表示
			hr = _swapChain->Present(_syncInterval);
			if (FAILED(hr))
			{
				logDeviceRemovedReason(hr);
				return false;
			}

			// ==============================================================
			// 9. フェンスを用いたGPU待機の同期処理
			// ※毎フレームGPUの完了を待っているのでCPUとGPUが並行して動けない。Phase 1 で改善する
			// ==============================================================
			hr = _fence->WaitForSignal(_commandQueue.get());
			if (FAILED(hr)) { logFailed("Fence::WaitForSignal", hr); return false; }

			return true;
		}

		void CommandContext::Render(Buffer* buffer, const RenderQueue& renderQueue)
		{
			if (!buffer || !_graphicsCommandList->IsRecording())
			{
				return;
			}

			auto cmdList = _graphicsCommandList->GetCmdList();
			const D3D12_GPU_VIRTUAL_ADDRESS sceneCbBase = buffer->GetSceneCB()->GetGPUVirtualAddress();

			for (const auto& renderObj : renderQueue.RenderObjects)
			{
				// 頂点オフセット (b0)
				// シェーダーは g_MegaVB[g_BaseVertexIndex + SV_VertexID] で頂点を読む
				UINT baseVertexIndex = renderObj.VertexOffset;
				cmdList->SetGraphicsRoot32BitConstants(Pipeline::ROOT_PARAM_BASE_VERTEX, 1, &baseVertexIndex, 0);

				// Scene(Object)定数バッファ (b2)
				// CbOffset は Root CBV の制約で 256 の倍数であること (Buffer::SCENE_CB_STRIDE 単位で指定する)
				cmdList->SetGraphicsRootConstantBufferView(Pipeline::ROOT_PARAM_OBJECT_CBV, sceneCbBase + renderObj.CbOffset);

				// インデックスバッファを使用
				// BaseVertexLocation(第4引数)は SV_VertexID に加算されない。
				// 頂点の開始位置はルート定数で渡しているので、ここは 0 にしておく (2-9 : 以前は二重に指定していた)
				cmdList->DrawIndexedInstanced(
					renderObj.IndexCount,
					1,
					renderObj.IndexOffset,
					0,
					0
				);
			}
		}

		bool CommandContext::ExecuteAndSync()
		{
			if (!_graphicsCommandList || !_graphicsCommandList->IsRecording())
			{
				// 何も記録していない
				return true;
			}

			// 1. コマンドリストの記録を終了する
			HRESULT hr = _graphicsCommandList->Close();
			if (FAILED(hr)) { logFailed("CommandList::Close", hr); return false; }

			// 2. コマンドリストをキューに送信して実行
			ID3D12CommandList* ppCommandLists[] = { _graphicsCommandList->GetCmdList() };
			_commandQueue->Get()->ExecuteCommandLists(1, ppCommandLists);

			// 3. 実行完了まで待機する
			hr = _fence->WaitForSignal(_commandQueue.get());
			if (FAILED(hr)) { logFailed("Fence::WaitForSignal", hr); return false; }

			// 以前はここでリセットして「記録中」のまま返していたため、
			// 次の RenderStart で記録中のリストがあるのにアロケーターをリセットしてしまっていた (1-10)
			// → Close済みのまま返し、次に積む側が BeginRecording() を呼ぶ
			return true;
		}

		void CommandContext::WaitForGpu()
		{
			if (_fence && _fence->Get() && _commandQueue && _commandQueue->Get())
			{
				_fence->WaitForSignal(_commandQueue.get());
			}
		}

		void CommandContext::Dispose()
		{
			// 以前は何もしておらず、GPUが使用中のまま破棄されていた (2-11)
			WaitForGpu();

			// 記録中のまま破棄しないように閉じておく
			if (_graphicsCommandList)
			{
				_graphicsCommandList->Close();
			}

			// スワップチェーン → コマンドリスト → アロケーター → キュー の順に破棄
			_currentBackBuffer = nullptr;
			_swapChain.reset();
			_graphicsCommandList.reset();
			_commandAllocator.reset();
			_fence.reset();
			_commandQueue.reset();
			_device = nullptr;
		}

		RHI::DirectX12::GraphicsCommandList* CommandContext::GetGraphicsCommandList()const
		{
			return _graphicsCommandList.get();
		}
		RHI::DirectX12::CommandQueue* CommandContext::GetCommandQueue()const
		{
			return _commandQueue.get();
		}
		RHI::DirectX12::CommandAllocator* CommandContext::GetCommandAllocator()const
		{
			return _commandAllocator.get();
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
