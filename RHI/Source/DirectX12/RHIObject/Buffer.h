
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
#include "CommandContext.h"     // FRAME_COUNT
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

            /***********************************************************************************
             * @brief カメラの設定 (View / Projection 行列を作り直す)
             * @details 行列は CPU 側に保持しておき、毎フレーム WriteFrameConstants でそのフレームの領域へ書く
             ************************************************************************************/
            void UpdateViewProjection(const Renderer::Camera& camera, Renderer::Projection::ProjectionType type)override;

            /***********************************************************************************
             * @brief 描画先のサイズを設定する (初期化・リサイズ時に呼ぶ)
             * @details Projection 行列のアスペクト比に使う (以前は 1280x720 固定だった : 2-4)
             ************************************************************************************/
            void SetViewportSize(uint32_t width, uint32_t height);

            /***********************************************************************************
             * @brief フレームごとの定数 (View / Projection) を書き込み、その GPU アドレスを返す
             * @details 定数バッファはフレームの数だけ領域を分けてある。
             *          GPU が前のフレームで読んでいる領域を、CPU が次のフレームのために上書きしないようにするため
             *          (FrameContext の完了待ちの後に呼ぶこと)
             *
             * @param[in] uint32_t frameIndex CommandContext::GetFrameIndex()
             * @return D3D12_GPU_VIRTUAL_ADDRESS Root CBV に渡すアドレス
             ************************************************************************************/
            D3D12_GPU_VIRTUAL_ADDRESS WriteFrameConstants(uint32_t frameIndex);

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
            // フレームごとの定数 (View / Projection) : 1 フレーム 256byte × フレーム数
            const uint64_t _COMMON_CB_SIZE = CONSTANT_BUFFER_ALIGNMENT * CommandContext::FRAME_COUNT;
            const uint64_t _GLOBAL_IB_SIZE = 16ull * 1024ull * 1024ull;
            const uint64_t _SCENE_IB_SIZE  = 64ull * 1024ull * 1024ull;

            // ---------- フレームごとの定数の元データ (CPU 側) ----------
            // 行列はカメラ・画面サイズが変わった時だけ作り直し、毎フレームは書き写すだけにする
            Renderer::Camera                    _camera         = {};
            Renderer::Projection::ProjectionType _projectionType = Renderer::Projection::ProjectionType::PROJECTION_TYPE_3D;
            ::DirectX::XMFLOAT4X4               _viewMatrix     = {};
            ::DirectX::XMFLOAT4X4               _projMatrix     = {};
            uint32_t                            _viewportWidth  = 1280;
            uint32_t                            _viewportHeight = 720;
            bool                                _hasCamera      = false;
            // 行列を更新した回数。各フレームの領域に書いた時の値と比べて、変わっていなければ書き込みを省く
            uint64_t                            _frameConstantsVersion = 1;
            uint64_t                            _writtenVersion[CommandContext::FRAME_COUNT] = {};

            void rebuildViewProjection();
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
