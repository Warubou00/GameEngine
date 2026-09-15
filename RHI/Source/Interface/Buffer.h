
/***********************************************************************************
* @file    Buffer.h
* @brief   RHI Buffer
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

#pragma once
#ifndef RHI_BUFFER_H_
#define RHI_BUFFER_H_

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
        class DescriptorHeapManager;
        class ResourceBuffer;
    }

    namespace ObjectInterface
    {
        class Device;
        class CommandContext;

        class Buffer : IObject
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            Buffer();
            ~Buffer();


        public:          /*---クラスメソッド---*/

            RHI_RESULT Initialize()override;
            RHI_RESULT Initialize(Device* device, CommandContext* commandContext)override;

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/

            std::unique_ptr<RHI::DirectX12::DescriptorHeapManager> _descriptorHeapManager;
            std::unique_ptr<RHI::DirectX12::ResourceBuffer>        _globalVB;
            std::unique_ptr<RHI::DirectX12::ResourceBuffer>        _sceneVB;
            std::unique_ptr<RHI::DirectX12::ResourceBuffer>        _globalCB;
            std::unique_ptr<RHI::DirectX12::ResourceBuffer>        _sceneCB;

            const uint64_t _GLOBAL_VB_SIZE = 16ull * 1024ull * 1024ull;
            const uint64_t _SCENE_VB_SIZE  = 64ull * 1024ull * 1024ull;
            const uint64_t _GLOBAL_CB_SIZE = 4ull  * 1024ull * 1024ull;
            const uint64_t _SCENE_CB_SIZE  = 16ull * 1024ull * 1024ull;

            bool makeRtv(Device* device, CommandContext* commandContext);
            bool nullCheckManager();

            bool makeBuffer(Device* device);
            bool makeGlobalVB(Device* device);
            bool makeSceneVB(Device* device);
            bool makeGlobalCB(Device* device);
            bool makeSceneCB(Device* device);

        };
    }
}


#endif // RHI_BUFFER_H_

/************************************
* End of file
************************************/
