
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

// TODO : 削除予定
#include <d3d12.h>

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

            bool makeCommonBuffer(Device* device);                                                                                               // Resource Bufferの作成
            bool makeGlobalVB(Device* device);                                                                                                   // Global Vertex   Bufferの作成
            bool makeSceneVB(Device* device);                                                                                                    // Scene  Vertex   Bufferの作成
            bool makeGlobalCB(Device* device);                                                                                                   // Global Constant Bufferの作成
            bool makeSceneCB(Device* device);                                                                                                    // Scene  Constant Bufferの作成
            bool makeBuffer(Device* device, RHI::DirectX12::ResourceBuffer* resourceBuffer, uint64_t sizeInBytes, D3D12_HEAP_TYPE heapType);     // TODO : D3D12の機能に直接触れたくないため要検討

            bool makeCommonView(Device* device);                                                                                                 // Viewの作成
            bool makeGlobalVBV(Device* device);                                                                                                  // Global Vertex   Buffer View(srv)の作成
            bool makeSceneVBV(Device* device);                                                                                                   // Scene  Vertex   Buffer View(srv)の作成
            bool makeGlobalCBV(Device* device);                                                                                                  // Global Constant Buffer View(cbv)の作成 ※未使用 RootCBVを採用
            bool makeSceneCBV(Device* device);                                                                                                   // Scene  Constant Buffer View(cbv)の作成 ※未使用 RootCBVを採用
            bool makeVBView(Device* device, RHI::DirectX12::ResourceBuffer* resourceBuffer, uint64_t sizeInBytes, uint32_t structureByteStride); // 頂点バッファをSRVとして実装
            bool makeCBView(Device* device, RHI::DirectX12::ResourceBuffer* resourceBuffer, uint64_t sizeInBytes, uint32_t cbOffset);            // 未使用 RootCBVを採用
        };
    }
}


#endif // RHI_BUFFER_H_

/************************************
* End of file
************************************/
