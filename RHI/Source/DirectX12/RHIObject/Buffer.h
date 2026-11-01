
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

#include "../../RHIObject/IBuffer.h"
#include <vector>

// 仮置き
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

namespace Renderer
{
    class ObjLoader;
    struct Mesh;    // Renderer側の定義は struct なので合わせる (C4099 警告の回避)
}

namespace RHI
{
    class ResourceBufferAllocator;

    namespace DirectX12
    {
        class DescriptorHeapManager;
        class ResourceBuffer;
    }

    namespace ObjectInterface
    {
        class IDevice;
        class ICommandContext;
        class Device;
        class CommandContext;
        class Texture;

        class Buffer : public IBuffer
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---定数---*/

            // ----------------------------------------------------------
            // SRVヒープ(CBV/SRV/UAV)の番号の割り当て
            //   [0] Global 頂点バッファ
            //   [1] Scene  頂点バッファ   ← ルートパラメータ ROOT_PARAM_VERTEX_BUFFER_TABLE (t0)
            //   [2]〜     テクスチャ      ← ルートパラメータ ROOT_PARAM_TEXTURE_TABLE (t1)。[2] は既定テクスチャ(白)
            // 以前は CommandContext 側に 1 / 2 が直書きされていた (2-8)
            // ----------------------------------------------------------
            static constexpr uint32_t SRV_INDEX_GLOBAL_VB       = 0;
            static constexpr uint32_t SRV_INDEX_SCENE_VB        = 1;
            static constexpr uint32_t SRV_INDEX_TEXTURE_BEGIN   = 2;

            // Root CBV のアドレスは 256byte の倍数でなければならない
            static constexpr uint64_t CONSTANT_BUFFER_ALIGNMENT = 256;

            // Scene定数バッファの 1オブジェクト分の間隔 (ConstantPerRenderObject を 256 に切り上げたもの)
            // 以前は sizeof(ConstantPerRenderObject) = 80byte 間隔で詰めていたため、2つ目以降のアドレスが不正だった (1-9)
            static constexpr uint64_t AlignConstantBufferSize(uint64_t size)
            {
                return (size + CONSTANT_BUFFER_ALIGNMENT - 1) & ~(CONSTANT_BUFFER_ALIGNMENT - 1);
            }
            static const uint64_t SCENE_CB_STRIDE;

        public:          /*---コンストラクタ・デストラクタ---*/
            Buffer();
            ~Buffer()override;


        public:          /*---クラスメソッド---*/

            RHI_RESULT Initialize(
                RHI::ObjectInterface::IDevice* iDevice,
                RHI::ObjectInterface::ICommandContext* iCommandContext
            )override;

            bool UploadVertexData(
                RHI::ObjectInterface::IDevice* iDevice,
                RHI::ObjectInterface::ICommandContext* iCommandContext,
                const std::vector<Renderer::Vertex>& vertices
            )override;

            bool UploadIndexData(
                RHI::ObjectInterface::IDevice* iDevice,
                RHI::ObjectInterface::ICommandContext* iCommandContext,
                const std::vector<uint32_t>& indecies
            )override;

            void UpdateViewProjection(const Renderer::Camera& camera, Renderer::Projection::ProjectionType type)override;

            void Dispose()override;

        public:          /*---アクセッサー関数群---*/

            RHI::DirectX12::DescriptorHeapManager* GetDescHeapManager()const;
            RHI::DirectX12::ResourceBuffer* GetGlobalVB()const;
            RHI::DirectX12::ResourceBuffer* GetSceneVB()const;
            RHI::DirectX12::ResourceBuffer* GetGlobalCB()const;
            RHI::DirectX12::ResourceBuffer* GetSceneCB()const;
            RHI::DirectX12::ResourceBuffer* GetCommonCB()const;
            RHI::DirectX12::ResourceBuffer* GetGlobalIB()const;
            RHI::DirectX12::ResourceBuffer* GetSceneIB()const;

            const uint64_t GetSceneIndexBufferSize()const;

        private:         /*---メンバ変数---*/

            std::unique_ptr<RHI::DirectX12::DescriptorHeapManager> _descriptorHeapManager;      // ディスクリプタヒープ管理クラス
            std::unique_ptr<RHI::DirectX12::ResourceBuffer>        _globalVB;                   // グローバルなモデルデータを格納する頂点バッファ(タイトル等)
            std::unique_ptr<RHI::DirectX12::ResourceBuffer>        _sceneVB;                    // 各シーンのみで使う頂点バッファ
            std::unique_ptr<RHI::DirectX12::ResourceBuffer>        _globalCB;                   // グローバルな定数バッファ(タイトル等)
            std::unique_ptr<RHI::DirectX12::ResourceBuffer>        _sceneCB;                    // 各シーンのみで使う定数バッファ
            std::unique_ptr<RHI::DirectX12::ResourceBuffer>        _commonCB;                   // 汎用的な定数バッファ
            std::unique_ptr<RHI::ResourceBufferAllocator>          _globalVBAllocator;          // Global 頂点バッファのメモリ管理を行うクラス
            std::unique_ptr<RHI::ResourceBufferAllocator>          _sceneVBAllocator;           // Scene  頂点バッファのメモリ管理を行うクラス
            std::unique_ptr<RHI::ResourceBufferAllocator>          _globalCBAllocator;          // Global 定数バッファのメモリ管理を行うクラス
            std::unique_ptr<RHI::ResourceBufferAllocator>          _sceneCBAllocator;           // Scene  定数バッファのメモリ管理を行うクラス
            std::unique_ptr<RHI::ResourceBufferAllocator>          _commonCBAllocator;          // Scene  定数バッファのメモリ管理を行うクラス

            std::unique_ptr<RHI::DirectX12::ResourceBuffer>        _globalIB;                   // グローバルなインデックスバッファー
            std::unique_ptr<RHI::DirectX12::ResourceBuffer>        _sceneIB;                    // 各シーンのみで使うインデックスバッファー
            std::unique_ptr<RHI::ResourceBufferAllocator>          _globalIBAllocator;          // Global インデックスバッファのメモリ管理を行うクラス
            std::unique_ptr<RHI::ResourceBufferAllocator>          _sceneIBAllocator;           // Global インデックスバッファのメモリ管理を行うクラス

            std::unique_ptr<RHI::ObjectInterface::Texture>         _texture;                    // Texture

            const uint64_t _GLOBAL_VB_SIZE = 16ull * 1024ull * 1024ull;
            const uint64_t _SCENE_VB_SIZE  = 64ull * 1024ull * 1024ull;
            const uint64_t _GLOBAL_CB_SIZE = 4ull  * 1024ull * 1024ull;
            const uint64_t _SCENE_CB_SIZE  = 16ull * 1024ull * 1024ull;
            const uint64_t _COMMON_CB_SIZE = 256ull;                    // 256bytes
            const uint64_t _GLOBAL_IB_SIZE = 16ull * 1024ull * 1024ull;
            const uint64_t _SCENE_IB_SIZE  = 64ull * 1024ull * 1024ull;

            bool makeRtv(RHI::ObjectInterface::Device* device, RHI::ObjectInterface::CommandContext* commandContext);
            bool nullCheckManager();

            bool makeCommonBuffer(RHI::ObjectInterface::Device* device);                                                                                               // Resource Bufferの作成
            bool makeGlobalVB(RHI::ObjectInterface::Device* device);                                                                                                   // Global Vertex   Bufferの作成
            bool makeSceneVB(RHI::ObjectInterface::Device* device);                                                                                                    // Scene  Vertex   Bufferの作成
            bool makeGlobalCB(RHI::ObjectInterface::Device* device);                                                                                                   // Global Constant Bufferの作成
            bool makeSceneCB(RHI::ObjectInterface::Device* device);                                                                                                    // Scene  Constant Bufferの作成
            bool makeCommonCB(RHI::ObjectInterface::Device* device);                                                                                                   // Common Constant Buffer
            bool makeGlobalIB(RHI::ObjectInterface::Device* device);                                                                                                   // Global Index    Buffer
            bool makeSceneIB(RHI::ObjectInterface::Device* device);                                                                                                    // Scene  Index    Buffer
            bool makeBuffer(RHI::ObjectInterface::Device* device, RHI::DirectX12::ResourceBuffer* resourceBuffer, uint64_t sizeInBytes, D3D12_HEAP_TYPE heapType);     // TODO : D3D12の機能に直接触れたくないため要検討

            bool makeCommonView(RHI::ObjectInterface::Device* device);                                                                                                 // Viewの作成
            bool makeGlobalVBV(RHI::ObjectInterface::Device* device);                                                                                                  // Global Vertex   Buffer View(srv)の作成
            bool makeSceneVBV(RHI::ObjectInterface::Device* device);                                                                                                   // Scene  Vertex   Buffer View(srv)の作成

            bool makeAllocator();                                                                                                                                      // メモリ管理を行うクラスのインスタンス生成
#if 0
            bool makeGlobalCBV(RHI::ObjectInterface::Device* device);                                                                                                  // Global Constant Buffer View(cbv)の作成 ※未使用 RootCBVを採用
            bool makeSceneCBV(RHI::ObjectInterface::Device* device);                                                                                                   // Scene  Constant Buffer View(cbv)の作成 ※未使用 RootCBVを採用
#endif
            bool makeVBView(RHI::ObjectInterface::Device* device, RHI::DirectX12::ResourceBuffer* resourceBuffer, uint64_t sizeInBytes, uint32_t structureByteStride, uint32_t expectedSrvIndex); // 頂点バッファをSRVとして実装

            // DEFAULTヒープのバッファの指定位置へ、UPLOADヒープ経由でデータを転送する (頂点/インデックス共通)
            bool uploadToDefaultBuffer(
                RHI::ObjectInterface::Device* device,
                RHI::ObjectInterface::CommandContext* cmdContext,
                RHI::DirectX12::ResourceBuffer* destBuffer,
                uint64_t destByteOffset,
                const void* data,
                uint64_t dataSize);

#if 0
            bool makeCBView(RHI::ObjectInterface::Device* device, RHI::DirectX12::ResourceBuffer* resourceBuffer, uint64_t sizeInBytes, uint32_t cbOffset);            // 未使用 RootCBVを採用
#endif

        public:
            Renderer::Mesh* LoadModel(
                RHI::ObjectInterface::IDevice* iDevice,
                RHI::ObjectInterface::ICommandContext* iCommandContext, 
                const std::wstring& filePath);
        };
    }
}


#endif // RHI_BUFFER_H_

/************************************
* End of file
************************************/
