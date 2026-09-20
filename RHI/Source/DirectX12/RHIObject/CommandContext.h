
/***********************************************************************************
* @file    CommandContext.h
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

#pragma once
#ifndef RHI_COMMAND_CONTEXT_H_
#define RHI_COMMAND_CONTEXT_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <vector>
#include <d3d12.h>


#include "../../RHIObject/ICommandContext.h"

/******************************************************************
* Macro Definitions
******************************************************************/


/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/

class IDXGISwapChain4;
class ID3D12CommandQueue;
class ID3D12GraphicsCommandList;
class ID3D12CommandAllocator;
class ID3D12Resource;
class ID3D12Fence;

namespace RHI
{
    namespace DirectX12
    {
        class GraphicsCommandList;
        class CommandQueue;
        class CommandAllocator;
        class SwapChain;
    }

    namespace ObjectInterface
    {
        class Device;
        class Pipeline;
        class Buffer;

        class CommandContext : public ICommandContext
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/
        public:
            struct RenderObj
            {
                uint32_t VertexOffset;
                uint32_t CbOffset;
                uint32_t VertexCount;
            };


        public:          /*---コンストラクタ・デストラクタ---*/
            CommandContext();
            ~CommandContext()override;


        public:          /*---クラスメソッド---*/

            RHI_RESULT Initialize();
            RHI_RESULT Initialize(Device* device, ObjectInfo* info);

            void RenderStart(Pipeline* pipeline, Buffer* buffer);
            void RenderEnd();

            void Render(Buffer* buffer, const std::vector<RenderObj>& renderQueue);

            void ExecuteAndSync();

            RHI::DirectX12::GraphicsCommandList* GetGraphicsCommandList()const;
            RHI::DirectX12::CommandQueue*        GetCommandQueue()const;
            RHI::DirectX12::CommandAllocator*    GetCommandAllocator()const;
            RHI::DirectX12::SwapChain*           GetSwapChain()const;

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/
            std::unique_ptr<RHI::DirectX12::GraphicsCommandList> _graphicsCommandList;
            std::unique_ptr<RHI::DirectX12::CommandQueue>        _commandQueue;
            std::unique_ptr<RHI::DirectX12::CommandAllocator>    _commandAllocator;
            std::unique_ptr<RHI::DirectX12::SwapChain>           _swapChain;
            
            // 追加: RenderStartとRenderEndで共有する変数
            UINT            _frameBufferIndex = 0;
            ID3D12Resource* _backBuffer = nullptr;

            // 追加: 同期(Fence)用オブジェクト
            ID3D12Fence* _fence = nullptr;
            uint64_t        _fenceValue = 0;
            void* _fenceEvent = nullptr; // HANDLEの代替
        };

    }
}

#endif // RHI_COMMAND_CONTEXT_H_

/************************************
* End of file
************************************/
