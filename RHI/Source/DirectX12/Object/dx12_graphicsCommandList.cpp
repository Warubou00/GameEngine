
/***********************************************************************************
* @file    dx12_graphicsCommandList.cpp
* @brief   DX12のグラフィックコマンドリスト
* @details
*
* @author  Keigo Onari
* @date    2026/02/02
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
#include "dx12_graphicsCommandList.h"

// API DirectX12オブジェクトをインクルード
// ========================================
#include "dx12_device.h"
#include "dx12_pipelineState.h"
#include "dx12_rootSignature.h"
#include "dx12_swapChain.h"
#include "../dx12_descriptorHeapManager.h"
#include "dx12_resourceBuffer.h"

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
	namespace DirectX12
	{
		GraphicsCommandList::GraphicsCommandList() {}
		GraphicsCommandList::~GraphicsCommandList() {}

		// ==============================================
		// コマンドリストの作成
		// ==============================================
		RHI_API_RESULT GraphicsCommandList::Initialize(RHI::APIInterface::IDevice* iDevice)
		{
			if (!iDevice)
			{
				return RHI_API_FAILED_INVALID_ARGUMENT;
			}

			auto device = static_cast<RHI::DirectX12::Device*>(iDevice);
			if (!device)
			{
				return RHI_API_FAILED_INVALID_ARGUMENT;
			}

			device->GetDevice()->CreateCommandAllocator(D3D12_COMMAND_LIST_TYPE_DIRECT, IID_PPV_ARGS(&_pCmdAllocator));
			return device->GetDevice()->CreateCommandList(0, D3D12_COMMAND_LIST_TYPE_DIRECT, _pCmdAllocator.Get(), nullptr, IID_PPV_ARGS(&_pCmdList));
		}

		// レンダリング初期処理
		void GraphicsCommandList::Begin(RHI::APIInterface::IPipeline* iPipeline)
		{
			auto pipelineState = static_cast<PipelineState*>(iPipeline);
			if (!pipelineState)
			{
				return;
			}

			// リセット
			_pCmdList->Reset(_pCmdAllocator.Get(), pipelineState->Get());

			// 2. ビューポートとシザー矩形の設定 (画面サイズに応じた設定)
			UINT width = 1280;          // 仮
			UINT height = 720;          // 仮
			D3D12_VIEWPORT viewport = { 0.0f, 0.0f, static_cast<float>(width), static_cast<float>(height), 0.0f, 1.0f };
			D3D12_RECT scissorRect = { 0, 0, static_cast<LONG>(width), static_cast<LONG>(height) };
			_pCmdList->RSSetViewports(1, &viewport);
			_pCmdList->RSSetScissorRects(1, &scissorRect);

		}

		void GraphicsCommandList::ResourceBarriar(RHI::APIInterface::IResource* iResource, ResourceState before, ResourceState after)
		{
			if (!iResource)
			{
				return;
			}

			auto resource = static_cast<RHI::DirectX12::ResourceBuffer*>(iResource);

			D3D12_RESOURCE_STATES stateBefore, stateAfter;
			stateBefore = static_cast<D3D12_RESOURCE_STATES>(before);
			stateAfter = static_cast<D3D12_RESOURCE_STATES>(after);

			// 3. リソースバリア (Present -> RenderTarget)
			D3D12_RESOURCE_BARRIER barrier = {};
			barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
			barrier.Flags = D3D12_RESOURCE_BARRIER_FLAG_NONE;
			barrier.Transition.pResource = resource->Get();
			barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
			barrier.Transition.StateBefore = stateBefore;
			barrier.Transition.StateAfter = stateAfter;
			_pCmdList->ResourceBarrier(1, &barrier);
		}

		// レンダリング処理
		void GraphicsCommandList::Render(
			RHI::APIInterface::IPipeline*        iPipeline,
			RHI::APIInterface::IDescriptorHeap*  iDescHeap,
			RHI::APIInterface::IResourceBinding* iResourceBinding,
			RHI::APIInterface::IResource*        iResource,
			RHI::APIInterface::ISwapChain*       iSwapChain
		)
		{
			// Beginとの間にResourceBarrierを記述
			
			auto pipelineState  = static_cast<PipelineState*>(iPipeline);
			auto descHeap       = static_cast<DescriptorHeapManager*>(iDescHeap);
			auto rootSignature  = static_cast<RootSignature*>(iResourceBinding);
			auto resourceBuffer = static_cast<ResourceBuffer*>(iResource);
			auto swapChain      = static_cast<SwapChain*>(iSwapChain);

			if (!pipelineState || !descHeap || !rootSignature || !resourceBuffer)
			{
				return;
			}

			// 4. RTVの設定と画面クリア
			auto rtvHandle = descHeap->GetRtvCpuHandle(swapChain->GetFrameBufferIndex()); //
			_pCmdList->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);
			const float clearColor[] = { 0.1f, 0.2f, 0.4f, 1.0f };
			_pCmdList->ClearRenderTargetView(rtvHandle, clearColor, 0, nullptr);

			// ==============================================================
			// 5. ルートシグネチャとヒープのセット
			// ==============================================================
			_pCmdList->SetGraphicsRootSignature(rootSignature->Get()); //[cite: 7]

			// メガVB用のSRVが登録されているヒープをセット[cite: 4, 7]
			ID3D12DescriptorHeap* heaps[] = { descHeap->GetSrvHeap() };
			_pCmdList->SetDescriptorHeaps(1, heaps);

			// [パラメータ1]: メガVBのSRVテーブル (t0) をセット[cite: 7]
			_pCmdList->SetGraphicsRootDescriptorTable(1, descHeap->GetSrvGpuHandle(1));

			// [パラメータ2]: Global定数バッファ (b1) を Root CBV としてセット[cite: 7]
			D3D12_GPU_VIRTUAL_ADDRESS globalCbAddress = resourceBuffer->Get()->GetGPUVirtualAddress();
			_pCmdList->SetGraphicsRootConstantBufferView(2, globalCbAddress);

			// ==============================================================
			// 6. オブジェクト単位の描画ループ
			// ==============================================================
			_pCmdList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);

			// 以下にレンダリング処理を記述


		}

		// レンダリング修了処理
		void GraphicsCommandList::End()
		{
			// 8. コマンドリストのクローズと実行
			_pCmdList->Close();
		}

		ID3D12CommandList* GraphicsCommandList::GetQueueList(void)
		{
			ID3D12CommandList* cmdList = { _pCmdList.Get() };
			return cmdList;
		}

		HRESULT GraphicsCommandList::TryVersionUp()
		{
			if (!_pCmdList)
			{
				return E_NOTIMPL;
			}

			return asCmdList();
		}

		HRESULT GraphicsCommandList::TryVersionUp4()
		{
			if (!_pCmdList)
			{
				return E_NOTIMPL;
			}

			return asCmdList4();
		}

		HRESULT GraphicsCommandList::TryVersionUp6()
		{
			if (!_pCmdList)
			{
				return E_NOTIMPL;
			}

			// 4も前提に...
			asCmdList4();
			return asCmdList6();
		}

		HRESULT GraphicsCommandList::TryVersionUp7()
		{
			if (!_pCmdList)
			{
				return E_NOTIMPL;
			}

			// 4,6も前提に...
			asCmdList4();
			asCmdList6();
			return asCmdList7();
		}

		// ==============================================
		// コマンドリストの取得
		// ==============================================
		ID3D12GraphicsCommandList* GraphicsCommandList::GetCmdList(void)const
		{
			return _pCmdList.Get();
		}

		ID3D12GraphicsCommandList4* GraphicsCommandList::GetCmdList4(void)const
		{
			return _pCmdList4.Get();
		}

		ID3D12GraphicsCommandList6* GraphicsCommandList::GetCmdList6(void)const
		{
			return _pCmdList6.Get();
		}

		ID3D12GraphicsCommandList7* GraphicsCommandList::GetCmdList7(void)const
		{
			return _pCmdList7.Get();
		}

		HRESULT GraphicsCommandList::asCmdList()
		{
			HRESULT hr = S_OK;

			hr = asCmdList4();
			hr = asCmdList6();
			hr = asCmdList7();

			return hr;
		}

		HRESULT GraphicsCommandList::asCmdList4()
		{
			return _pCmdList.As(&_pCmdList4);
		}

		HRESULT GraphicsCommandList::asCmdList6()
		{
			return _pCmdList.As(&_pCmdList6);
		}

		HRESULT GraphicsCommandList::asCmdList7()
		{
			return _pCmdList.As(&_pCmdList7);
		}

		// ==============================================
		// 解放
		// ==============================================
		void GraphicsCommandList::Release(void)
		{
			// ComPtrなので明示的な解放は避ける
			//if (_pCmdList.Get())_pCmdList->Release();
		}
	}
}

/************************************
* End of file
************************************/