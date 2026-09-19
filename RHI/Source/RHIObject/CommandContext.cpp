
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
#include "Device.h"
#include "Pipeline.h"
#include "Buffer.h"

// TODO : 削除方針
// 目標 : Factory化し、これらをインクルードしない方向で行く
#include "../DXGI/dxgiFactory6.h"
#include "../DirectX12/Object/dx12_device.h"
#include "../DirectX12/Object/dx12_graphicsCommandList.h"
#include "../DirectX12/Object/dx12_commandQueue.h"
#include "../DirectX12/Object/dx12_commandAllocator.h"
#include "../DirectX12/Object/dx12_swapChain.h"
#include "../DirectX12/Object/dx12_resourceBuffer.h"
#include "../DirectX12/Object/dx12_rootSignature.h"
#include "../DirectX12/dx12_descriptorHeapManager.h"

#include <d3d12.h>

#include "../../Core/Source/Log/logger.h"

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
		CommandContext::CommandContext() = default;
		CommandContext::~CommandContext() = default;

		RHI_RESULT CommandContext::Initialize()
		{
			return RHI_SUCCEEDED;
		}

		RHI_RESULT CommandContext::Initialize(Device* device, ObjectInfo* info)
		{
			if (!device)
			{
				return RHI_FAILED_INVALID_ARGUMENT;
			}

			_commandQueue        = std::make_unique<DirectX12::CommandQueue>();
			_commandAllocator    = std::make_unique<DirectX12::CommandAllocator>();
			_graphicsCommandList = std::make_unique<DirectX12::GraphicsCommandList>();
			_swapChain           = std::make_unique<DirectX12::SwapChain>();

			ID3D12Device* dx12Device = device->GetDevice()->GetDevice();
			HRESULT hr = _commandQueue->Create(dx12Device);
			hr         = _commandAllocator->Create(dx12Device);
			hr         = _graphicsCommandList->Create(dx12Device, _commandAllocator->Get());

			HWND hWnd   = static_cast<HWND>(info->HWnd);
			UINT width  = static_cast<UINT>(info->Width);
			UINT height = static_cast<UINT>(info->Height);
			hr         = _swapChain->Create(device->GetDxgiFactory()->Get(), _commandQueue->Get(), &hWnd, width, height);

			// ==============================================================
			// フェンスとイベントの初期化
			// ==============================================================
			hr = dx12Device->CreateFence(0, D3D12_FENCE_FLAG_NONE, IID_PPV_ARGS(&_fence));
			_fenceValue = 1;
			_fenceEvent = CreateEvent(nullptr, FALSE, FALSE, nullptr);

			// 最初のフレームインデックスを取得
			_frameBufferIndex = _swapChain->Get()->GetCurrentBackBufferIndex();

#ifdef CONSOLE_DEBUG
			Core::Logger::Log("**************************************************");
			Core::Logger::Log("Succeeded Initialize CommandContext From C++");
			std::cout << "Succeeded Initialize CommandContext From C++" << std::endl;
			std::cout << "Width  : " << width  << std::endl;
			std::cout << "Height : " << height << std::endl;
			Core::Logger::Log("**************************************************");
#endif

			return RHI_SUCCEEDED;
		}

		void CommandContext::RenderStart(Pipeline* pipeline, Buffer* buffer)
		{
			// 1. コマンドリストとアロケータのリセット
			_commandAllocator->Get()->Reset();
			_graphicsCommandList->GetCmdList()->Reset(_commandAllocator->Get(), pipeline->GetPipelineState());

			// 2. ビューポートとシザー矩形の設定 (画面サイズに応じた設定)
			UINT width = 1280;          // 仮
			UINT height = 720;          // 仮
			D3D12_VIEWPORT viewport = { 0.0f, 0.0f, static_cast<float>(width), static_cast<float>(height), 0.0f, 1.0f };
			D3D12_RECT scissorRect = { 0, 0, static_cast<LONG>(width), static_cast<LONG>(height) };
			_graphicsCommandList->GetCmdList()->RSSetViewports(1, &viewport);
			_graphicsCommandList->GetCmdList()->RSSetScissorRects(1, &scissorRect);

			// 3. リソースバリア (Present -> RenderTarget)
			_swapChain->Get()->GetBuffer(_frameBufferIndex, IID_PPV_ARGS(&_backBuffer));
			D3D12_RESOURCE_BARRIER barrier = {};
			barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
			barrier.Flags = D3D12_RESOURCE_BARRIER_FLAG_NONE;
			barrier.Transition.pResource = _backBuffer;
			barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
			barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_PRESENT;
			barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_RENDER_TARGET;
			_graphicsCommandList->GetCmdList()->ResourceBarrier(1, &barrier);

			// 4. RTVの設定と画面クリア
			auto rtvHandle = buffer->GetDescHeapManager()->GetRtvCpuHandle(_frameBufferIndex); //
			_graphicsCommandList->GetCmdList()->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);
			const float clearColor[] = { 0.1f, 0.2f, 0.4f, 1.0f };
			_graphicsCommandList->GetCmdList()->ClearRenderTargetView(rtvHandle, clearColor, 0, nullptr);

			// ==============================================================
			// 5. ルートシグネチャとヒープのセット
			// ==============================================================
			_graphicsCommandList->GetCmdList()->SetGraphicsRootSignature(pipeline->GetRootSignature()); //[cite: 7]

			// メガVB用のSRVが登録されているヒープをセット[cite: 4, 7]
			ID3D12DescriptorHeap* heaps[] = { buffer->GetDescHeapManager()->GetSrvHeap() };
			_graphicsCommandList->GetCmdList()->SetDescriptorHeaps(1, heaps);

			// [パラメータ1]: メガVBのSRVテーブル (t0) をセット[cite: 7]
			_graphicsCommandList->GetCmdList()->SetGraphicsRootDescriptorTable(1, buffer->GetDescHeapManager()->GetSrvGpuHandle(1));

			// [パラメータ2]: Global定数バッファ (b1) を Root CBV としてセット[cite: 7]
			D3D12_GPU_VIRTUAL_ADDRESS globalCbAddress = buffer->GetGlobalCB()->Get()->GetGPUVirtualAddress();
			_graphicsCommandList->GetCmdList()->SetGraphicsRootConstantBufferView(2, globalCbAddress);

			// ==============================================================
			// 6. オブジェクト単位の描画ループ
			// ==============================================================
			_graphicsCommandList->GetCmdList()->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
		}

		void CommandContext::RenderEnd()
		{
			D3D12_RESOURCE_BARRIER barrier = {};
			barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
			barrier.Flags = D3D12_RESOURCE_BARRIER_FLAG_NONE;
			barrier.Transition.pResource = _backBuffer;
			barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
			barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_RENDER_TARGET;
			barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_PRESENT;

			_graphicsCommandList->GetCmdList()->ResourceBarrier(1, &barrier);

			// 8. コマンドリストのクローズと実行
			_graphicsCommandList->GetCmdList()->Close();
			ID3D12CommandList* ppCommandLists[] = { _graphicsCommandList->GetCmdList() };
			_commandQueue->Get()->ExecuteCommandLists(1, ppCommandLists); //[cite: 5]

			// 9. 画面への表示とインデックス更新
			_swapChain->Get()->Present(1, 0); //[cite: 5]

			// ==============================================================
			// フェンスを用いたGPU待機の同期処理
			// ==============================================================
			const uint64_t currentFenceValue = _fenceValue;
			_commandQueue->Get()->Signal(_fence, currentFenceValue);
			_fenceValue++;

			if (_fence->GetCompletedValue() < currentFenceValue)
			{
				_fence->SetEventOnCompletion(currentFenceValue, _fenceEvent);
				WaitForSingleObject(_fenceEvent, INFINITE);
			}

			// (※ここにフェンスを用いたGPU待機の同期処理が入ります)
			_frameBufferIndex = _swapChain->Get()->GetCurrentBackBufferIndex();

			// COMオブジェクトの参照カウントを下げる
			if (_backBuffer)
			{
				_backBuffer->Release();
				_backBuffer = nullptr;
			}
		}

		void CommandContext::Render(Buffer* buffer, const std::vector<RenderObj>& renderQueue)
		{
			for (const auto& renderObj : renderQueue)
			{
				// [パラメータ0]: 頂点オフセット (b0)[cite: 7]
				UINT baseVertexIndex = renderObj.VertexOffset;
				_graphicsCommandList->GetCmdList()->SetGraphicsRoot32BitConstants(0, 1, &baseVertexIndex, 0);

				// [パラメータ3]: Scene(Object)定数バッファ (b2)[cite: 7]
				D3D12_GPU_VIRTUAL_ADDRESS objectCbAddress = buffer->GetSceneCB()->Get()->GetGPUVirtualAddress() + renderObj.CbOffset;
				_graphicsCommandList->GetCmdList()->SetGraphicsRootConstantBufferView(3, objectCbAddress);

				// 頂点バッファ(IA)はバインドせず直接描画
				_graphicsCommandList->GetCmdList()->DrawInstanced(renderObj.VertexCount, 1, renderObj.VertexOffset, 0);
			}
		}

		void CommandContext::ExecuteAndSync()
		{
			// 1. コマンドリストの記録を終了する
			_graphicsCommandList->GetCmdList()->Close();

			// 2. コマンドリストをキューに送信して実行
			ID3D12CommandList* ppCommandLists[] = { _graphicsCommandList->GetCmdList() };
			_commandQueue->Get()->ExecuteCommandLists(1, ppCommandLists);

			// 3. 実行完了の目印（フェンス値）をキューにセット
			const uint64_t currentFenceValue = _fenceValue;
			_commandQueue->Get()->Signal(_fence, currentFenceValue);
			_fenceValue++;

			// 4. GPUが先ほどセットした目印に到達するまで、CPUをスリープさせて待機
			if (_fence->GetCompletedValue() < currentFenceValue)
			{
				_fence->SetEventOnCompletion(currentFenceValue, _fenceEvent);
				WaitForSingleObject(_fenceEvent, INFINITE);
			}

			// ==============================================================
			// 5. 次のコマンド記録に向けたリセット処理
			// ==============================================================
			// GPUが確実に処理を終えた（待機を抜けた）ため、アロケータを安全にリセット可能
			_commandAllocator->Get()->Reset();

			// コマンドリストも初期状態に戻す（この段階では特定のPSOに依存しないため nullptr を指定）
			_graphicsCommandList->GetCmdList()->Reset(_commandAllocator->Get(), nullptr);
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
	}
}

/************************************
* End of file
************************************/
