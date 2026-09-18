
/***********************************************************************************
* @file    RenderHardwareInterface.cpp
* @brief   RHI構造
* @details 
*
* @author  Keigo Onari
* @date    2026/1/13
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
#include "RenderHardwareInterface.h"
#include "Interface/Device.h"
#include "Interface/CommandContext.h"
#include "Interface/Buffer.h"
#include "Interface/Texture.h"
#include "Interface/Pipeline.h"

#include "DirectX12/dx12_descriptorHeapManager.h"

#include <dxgi1_5.h>

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
    RenderHardwareInterface::RenderHardwareInterface() = default;
    RenderHardwareInterface::~RenderHardwareInterface() = default;
    
    void RenderHardwareInterface::Initialize(RHI::RenderHardwareInterface::RHIInitializeInfo* info, ObjectInterface::ObjectInfo* objInfo)
    {
        _device         = std::make_unique<ObjectInterface::Device>();
        _commandContext = std::make_unique<ObjectInterface::CommandContext>();
        _buffer         = std::make_unique<ObjectInterface::Buffer>();
        _texture        = std::make_unique<ObjectInterface::Texture>();
        _pipeline       = std::make_unique<ObjectInterface::Pipeline>();

        info->InitializedDevice         = _device->Initialize();
        info->InitializedCommandContext = _commandContext->Initialize(_device.get(), objInfo);
        info->InitializedBuffer         = _buffer->Initialize(_device.get(), _commandContext.get());
        //info->InitializedTexture        = _texture->Initialize();
        info->InitializedTexture        = _pipeline->Initialize(_device.get());

        return;
    }

    /// <summary>
    /// デバイスの取得
    /// </summary>
    /// <returns>ObjectInterface::Device デバイス</returns>
    inline ObjectInterface::Device* RenderHardwareInterface::GetDevice()
    {
        return _device.get();
    }

    /// <summary>
    /// コマンドコンテキストの取得
    /// </summary>
    /// <returns>ObjectInterface::CommandContext コマンドコンテキスト</returns>
    inline ObjectInterface::CommandContext* RenderHardwareInterface::GetCommandContext()
    {
        return _commandContext.get();
    }

    /// <summary>
    /// バッファーの取得
    /// </summary>
    /// <returns>ObjectInterface::Buffer バッファー</returns>
    inline ObjectInterface::Buffer* RenderHardwareInterface::GetBuffer()
    {
        return _buffer.get();
    }

    /// <summary>
    /// テクスチャの取得
    /// </summary>
    /// <returns>ObjectInterface::Texture テクスチャ</returns>
    inline ObjectInterface::Texture* RenderHardwareInterface::GetTexture()
    {
        return _texture.get();
    }

    void RenderHardwareInterface::Begin()
    {

    }

    void RenderHardwareInterface::End()
    {

    }

    void RenderHardwareInterface::CleanUp()
    {

    }

    void RenderHardwareInterface::TestRender()
    {
        // 各種DX12オブジェクトの取得
        auto cmdList = _commandContext->GetCommandList();
        auto allocator = _commandContext->GetCommandAllocator();
        auto swapChain = _commandContext->GetSwapChain(); //[cite: 5]

        // 1. コマンドリストとアロケータのリセット
        allocator->Reset();
        cmdList->Reset(allocator, _pipeline->GetPipelineState());

        // 2. ビューポートとシザー矩形の設定 (画面サイズに応じた設定)
        UINT width = 1280;          // 仮
        UINT height = 720;          // 仮
        D3D12_VIEWPORT viewport = { 0.0f, 0.0f, static_cast<float>(width), static_cast<float>(height), 0.0f, 1.0f };
        D3D12_RECT scissorRect = { 0, 0, static_cast<LONG>(width), static_cast<LONG>(height) };
        cmdList->RSSetViewports(1, &viewport);
        cmdList->RSSetScissorRects(1, &scissorRect);

        // 3. リソースバリア (Present -> RenderTarget)
        UINT _frameIndex;
        ID3D12Resource* backBuffer;
        swapChain->GetBuffer(_frameIndex, IID_PPV_ARGS(&backBuffer));
        D3D12_RESOURCE_BARRIER barrier = CD3DX12_RESOURCE_BARRIER::Transition(
            backBuffer, D3D12_RESOURCE_STATE_PRESENT, D3D12_RESOURCE_STATE_RENDER_TARGET);
        cmdList->ResourceBarrier(1, &barrier);

        // 4. RTVの設定と画面クリア
        auto rtvHandle = _buffer->GetDescHeapManager()->GetRtvCpuHandle(_frameIndex); //
        cmdList->OMSetRenderTargets(1, &rtvHandle, FALSE, nullptr);
        const float clearColor[] = { 0.1f, 0.2f, 0.4f, 1.0f };
        cmdList->ClearRenderTargetView(rtvHandle, clearColor, 0, nullptr);

        // ==============================================================
        // 5. ルートシグネチャとヒープのセット
        // ==============================================================
        cmdList->SetGraphicsRootSignature(_pipeline->GetRootSignature()); //[cite: 7]

        // メガVB用のSRVが登録されているヒープをセット[cite: 4, 7]
        ID3D12DescriptorHeap* heaps[] = { _buffer->GetDescriptorHeapManager()->GetSrvHeap() };
        cmdList->SetDescriptorHeaps(1, heaps);

        // [パラメータ1]: メガVBのSRVテーブル (t0) をセット[cite: 7]
        cmdList->SetGraphicsRootDescriptorTable(1, _buffer->GetDescHeapManager()->GetSrvGpuHandle(0));

        // [パラメータ2]: Global定数バッファ (b1) を Root CBV としてセット[cite: 7]
        D3D12_GPU_VIRTUAL_ADDRESS globalCbAddress = _buffer->GetGlobalCB()->GetGPUVirtualAddress();
        cmdList->SetGraphicsRootConstantBufferView(2, globalCbAddress);

        // ==============================================================
        // 6. オブジェクト単位の描画ループ
        // ==============================================================
        cmdList->IASetPrimitiveTopology(D3D_PRIMITIVE_TOPOLOGY_TRIANGLELIST);

        for (const auto& renderObj : _renderQueue)
        {
            // [パラメータ0]: 頂点オフセット (b0)[cite: 7]
            UINT baseVertexIndex = renderObj.VertexOffset;
            cmdList->SetGraphicsRoot32BitConstants(0, 1, &baseVertexIndex, 0);

            // [パラメータ3]: Scene(Object)定数バッファ (b2)[cite: 7]
            D3D12_GPU_VIRTUAL_ADDRESS objectCbAddress = _buffer->GetSceneCB()->GetGPUVirtualAddress() + renderObj.CbOffset;
            cmdList->SetGraphicsRootConstantBufferView(3, objectCbAddress);

            // 頂点バッファ(IA)はバインドせず直接描画
            cmdList->DrawInstanced(renderObj.VertexCount, 1, 0, 0);
        }

        // 7. リソースバリア (RenderTarget -> Present)
        barrier = CD3DX12_RESOURCE_BARRIER::Transition(
            backBuffer, D3D12_RESOURCE_STATE_RENDER_TARGET, D3D12_RESOURCE_STATE_PRESENT);
        cmdList->ResourceBarrier(1, &barrier);

        // 8. コマンドリストのクローズと実行
        cmdList->Close();
        ID3D12CommandList* ppCommandLists[] = { cmdList };
        _commandContext->GetCommandQueue()->Get()->ExecuteCommandLists(1, ppCommandLists); //[cite: 5]

        // 9. 画面への表示とインデックス更新
        swapChain->Present(1, 0); //[cite: 5]

        // (※ここにフェンスを用いたGPU待機の同期処理が入ります)
        _frameIndex = swapChain->GetCurrentBackBufferIndex();
    }

}

/************************************
* End of file
************************************/
