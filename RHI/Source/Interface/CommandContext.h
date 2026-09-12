
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
#include <memory>
#include "IObject.h"

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
        class GraphicsCommandList;
        class CommandQueue;
        class CommandAllocator;
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

            bool Initialize()override;
            bool Initialize(Device* device)override;


        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/
            std::unique_ptr<RHI::DirectX12::GraphicsCommandList> _graphicsCommandList;
            std::unique_ptr<RHI::DirectX12::CommandQueue>        _commandQueue;
            std::unique_ptr<RHI::DirectX12::CommandAllocator>    _commandAllocator;

        };

    }
}

#endif // RHI_COMMAND_CONTEXT_H_

/************************************
* End of file
************************************/
