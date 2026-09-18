
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
#include "IRHIObject.h"

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

        class CommandContext : IObject
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            CommandContext();
            ~CommandContext();


        public:          /*---クラスメソッド---*/

            RHI_RESULT Initialize()override;
            RHI_RESULT Initialize(Device* device, ObjectInfo* info)override;

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
        };

    }
}

#endif // RHI_COMMAND_CONTEXT_H_

/************************************
* End of file
************************************/
