
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

#include <vector>


// API DirectX12オブジェクトをインクルード
// ========================================
#include "DirectX12/Object/dx12_device.h"
#include "DirectX12/Object/dx12_graphicsCommandList.h"
#include "DirectX12/Object/dx12_commandQueue.h"
#include "DirectX12/dx12_descriptorHeapManager.h"
#include "DirectX12/Object/dx12_pipelineState.h"
#include "DirectX12/Object/dx12_resourceBuffer.h"
#include "DirectX12/Object/dx12_rootSignature.h"
#include "DirectX12/Object/dx12_swapChain.h"
#include "DirectX12/Object/dx12_fence.h"
// ========================================

#include "../../Core/Source/Math/Float/float.h"

/******************************************************************
* Macro Definitions
******************************************************************/
#define VERTEX_OFFSET_X_1 (-0.2f)
#define VERTEX_OFFSET_X_2 (0.2f)

/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/

// 仮
struct Vertex
{
    Core::Float3 Position;
    Core::Float4 Color;
};

namespace RHI
{
    RenderHardwareInterface::RenderHardwareInterface() = default;
    RenderHardwareInterface::~RenderHardwareInterface() = default;

    void RenderHardwareInterface::Initialize(RHI::RenderHardwareInterface::RHIInitializeInfo* info, ObjectInterface::ObjectInfo* objInfo)
    {
        _device = std::make_unique<APIInterface::IDevice>();
        _cmdList = std::make_unique<APIInterface::ICommandList>();
        _cmdQueue = std::make_unique<APIInterface::ICommandQueue>();
        _descHeap = std::make_unique<APIInterface::IDescriptorHeap>();
        _pipeline = std::make_unique<APIInterface::IPipeline>();
        _resource = std::make_unique<APIInterface::IResource>();
        _resourceBinding = std::make_unique<APIInterface::IResourceBinding>();
        _swapChain = std::make_unique<APIInterface::ISwapChain>();
        _fence = std::make_unique<APIInterface::IFence>();

        std::vector<Vertex> quadVertices = {
            // 1つ目の三角形 (左上, 右上, 左下)
            { {-0.25f + VERTEX_OFFSET_X_1,  0.5f, 0.0f}, {1.0f, 0.0f, 0.0f, 1.0f} }, // 左上 (赤)
            { { 0.25f + VERTEX_OFFSET_X_1,  0.5f, 0.0f}, {0.0f, 1.0f, 0.0f, 1.0f} }, // 右上 (緑)
            { {-0.25f + VERTEX_OFFSET_X_1, -0.5f, 0.0f}, {0.0f, 0.0f, 1.0f, 1.0f} }, // 左下 (青)

            // 2つ目の三角形 (左下, 右上, 右下)
            { {-0.25f + VERTEX_OFFSET_X_1, -0.5f, 0.0f}, {0.0f, 0.0f, 1.0f, 1.0f} }, // 左下 (青)
            { { 0.25f + VERTEX_OFFSET_X_1,  0.5f, 0.0f}, {0.0f, 1.0f, 0.0f, 1.0f} }, // 右上 (緑)
            { { 0.25f + VERTEX_OFFSET_X_1, -0.5f, 0.0f}, {1.0f, 1.0f, 1.0f, 1.0f} }, // 右下 (白)

            // 3つ目の三角形 (左上, 右上, 左下)
            { {-0.25f + VERTEX_OFFSET_X_2,  0.5f, 0.0f}, {1.0f, 0.0f, 0.0f, 1.0f} }, // 左上 (赤)
            { { 0.25f + VERTEX_OFFSET_X_2,  0.5f, 0.0f}, {0.0f, 1.0f, 0.0f, 1.0f} }, // 右上 (緑)
            { {-0.25f + VERTEX_OFFSET_X_2, -0.5f, 0.0f}, {0.0f, 0.0f, 1.0f, 1.0f} }, // 左下 (青)

            // 4つ目の三角形 (左下, 右上, 右下)
            { {-0.25f + VERTEX_OFFSET_X_2, -0.5f, 0.0f}, {0.0f, 0.0f, 1.0f, 1.0f} }, // 左下 (青)
            { { 0.25f + VERTEX_OFFSET_X_2,  0.5f, 0.0f}, {0.0f, 1.0f, 0.0f, 1.0f} }, // 右上 (緑)
            { { 0.25f + VERTEX_OFFSET_X_2, -0.5f, 0.0f}, {1.0f, 1.0f, 1.0f, 1.0f} }  // 右下 (白)
        };

        _device->Initialize();
        _cmdList->Initialize(_device.get());
        _cmdQueue->Initialize(_device.get());
        _descHeap->Initialize(_device.get());
        _pipeline->Initialize(_device.get(), _resourceBinding.get());
        _resource->Initialize(_device.get());
        _resourceBinding->Initialize(_device.get());
        _swapChain->Initialize(_device.get(), _cmdQueue.get(), nullptr, 0, 0);
        _fence->Initialize(_device.get(), _swapChain.get());

        return;
    }

    void RenderHardwareInterface::Begin()
    {
        _cmdList->Begin(_pipeline.get());
        _cmdList->ResourceBarriar(
            _resource.get(),
            APIInterface::ICommandList::ResourceState::RESOURCE_STATE_PRESENT,
            APIInterface::ICommandList::ResourceState::RESOURCE_STATE_RENDER_TARGET
        );
    }

    void RenderHardwareInterface::Render()
    {

    }

    void RenderHardwareInterface::End()
    {
        _cmdList->End();
        _cmdQueue->End(_cmdList.get());
        _cmdList->ResourceBarriar(
            _resource.get(),
            APIInterface::ICommandList::ResourceState::RESOURCE_STATE_RENDER_TARGET,
            APIInterface::ICommandList::ResourceState::RESOURCE_STATE_PRESENT
        );
        _swapChain->Present();
        _fence->WaitForSignal(_cmdQueue.get());
    }

    void RenderHardwareInterface::CleanUp()
    {

    }

    void RenderHardwareInterface::TestRender()
    {
        std::vector<RHI::ObjectInterface::CommandContext::RenderObj> renderQueue;
        RHI::ObjectInterface::CommandContext::RenderObj renderObj1 = {}, renderObj2 = {};
        renderObj1.VertexCount  = 6;
        renderObj1.CbOffset     = 0;
        renderObj1.VertexOffset = 0;
        renderObj1.VertexCount  = 6;
        renderObj2.CbOffset     = 0;
        renderObj2.VertexOffset = 6;

        renderQueue.push_back(renderObj1);
        renderQueue.push_back(renderObj2);

        _commandContext->RenderStart(_pipeline.get(), _buffer.get());
        _commandContext->Render(_buffer.get(), renderQueue);
        _commandContext->RenderEnd();
    }

    /*
            bool Buffer::UploadVertexData(Device* device, CommandContext* commandContext, const std::vector<Vertex>& vertices)
        {
            if (vertices.empty()) return false;

            UINT64 dataSize = vertices.size() * sizeof(Vertex);


            auto uploadBuffer = std::make_unique<RHI::DirectX12::ResourceBuffer>();
            uploadBuffer->Create(device->GetDevice()->GetDevice(), dataSize, D3D12_HEAP_TYPE_UPLOAD);

            void* mappedData = nullptr;

            HRESULT hr = uploadBuffer->Get()->Map(0, nullptr, &mappedData);
            if (FAILED(hr)) return false;

            memcpy(mappedData, vertices.data(), dataSize);
            uploadBuffer->Get()->Unmap(0, nullptr);

            auto cmdList = commandContext->GetGraphicsCommandList()->GetCmdList();


            D3D12_RESOURCE_BARRIER barrier = {};
            barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
            barrier.Flags = D3D12_RESOURCE_BARRIER_FLAG_NONE;
            barrier.Transition.pResource = _sceneVB->Get();
            barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
            barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_COMMON;
            barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_COPY_DEST;
            cmdList->ResourceBarrier(1, &barrier);


            cmdList->CopyBufferRegion(
                _sceneVB->Get(),
                0,             
                uploadBuffer->Get(), 
                0,               
                dataSize         
            );


            barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_DEST;
            barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_GENERIC_READ;
            cmdList->ResourceBarrier(1, &barrier);




            commandContext->ExecuteAndSync();

            return true;
        }
    */

    /*
            void CommandContext::ExecuteAndSync()
        {

            _graphicsCommandList->GetCmdList()->Close();


            ID3D12CommandList* ppCommandLists[] = { _graphicsCommandList->GetCmdList() };
            _commandQueue->Get()->ExecuteCommandLists(1, ppCommandLists);

            const uint64_t currentFenceValue = _fenceValue;
            _commandQueue->Get()->Signal(_fence, currentFenceValue);
            _fenceValue++;


            if (_fence->GetCompletedValue() < currentFenceValue)
            {
                _fence->SetEventOnCompletion(currentFenceValue, _fenceEvent);
                WaitForSingleObject(_fenceEvent, INFINITE);
            }


            _commandAllocator->Get()->Reset();


            _graphicsCommandList->GetCmdList()->Reset(_commandAllocator->Get(), nullptr);
        }*/

}

/************************************
* End of file
************************************/
