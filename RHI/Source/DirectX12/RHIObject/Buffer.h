
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

#include <unordered_map>
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

            // Scene定数バッファの 1オブジェクト分の間隔 (シェーダーの struct ObjectData と同じ 80byte)
            // シェーダーは StructuredBuffer<ObjectData> (Root SRV) として読むので、16byte の倍数に切り上げるだけでよい
            // 以前は描画ごとに Root CBV で指していたため 256byte 間隔が必須だった (1-9)。
            // 詰めたことで、1 フレームの領域に入るオブジェクトの数が約 3 倍になり、GPU が読むメモリも減る
            static constexpr uint64_t OBJECT_DATA_STRIDE = (sizeof(RHI::ObjectConstants) + 15) & ~15ull;
            static_assert(OBJECT_DATA_STRIDE == 80, "OBJECT_DATA_STRIDE must match HLSL struct ObjectData (80 bytes)");

            // Scene定数バッファの容量 (フレームの数で等分して使う)
            static constexpr uint64_t SCENE_CB_SIZE        = 16ull * 1024ull * 1024ull;
            static constexpr uint64_t SCENE_CB_FRAME_SIZE  = SCENE_CB_SIZE / CommandContext::FRAME_COUNT;
            // 1 フレームで扱えるオブジェクトの最大数
            static constexpr uint32_t MAX_OBJECTS          = static_cast<uint32_t>(SCENE_CB_FRAME_SIZE / OBJECT_DATA_STRIDE);

        public:          /*---コンストラクタ・デストラクタ---*/
            Buffer();
            ~Buffer()override;


        public:          /*---クラスメソッド---*/

            RHI_RESULT Initialize(
                RHI::ObjectInterface::IDevice* iDevice,
                RHI::ObjectInterface::ICommandContext* iCommandContext
            )override;

            // 頂点をメガ頂点バッファへ (転送は UploadManager に積むだけ)
            RHI::VertexRange UploadVertexData(
                RHI::ObjectInterface::IDevice* iDevice,
                RHI::ObjectInterface::ICommandContext* iCommandContext,
                const RHI::Vertex* vertices,
                uint32_t count
            )override;

            // インデックスをメガインデックスバッファへ
            RHI::IndexRange UploadIndexData(
                RHI::ObjectInterface::IDevice* iDevice,
                RHI::ObjectInterface::ICommandContext* iCommandContext,
                const uint32_t* indices,
                uint32_t count
            )override;

            /***********************************************************************************
             * @brief カメラの行列を設定する
             * @details 転置して CPU 側に保持しておき、毎フレーム WriteFrameConstants でそのフレームの領域へ書く
             *          (以前は Camera を受け取って RHI の中で行列を作っていた。行列を作るのは Renderer の仕事)
             ************************************************************************************/
            void SetViewProjection(const ::DirectX::XMFLOAT4X4& view, const ::DirectX::XMFLOAT4X4& projection)override;

            /***********************************************************************************
             * @brief 描画オブジェクト (ワールド行列 / テクスチャの置き場) を作る
             * @return ObjectHandle 満杯なら IsValid() == false
             ************************************************************************************/
            RHI::ObjectHandle CreateObject();

            /***********************************************************************************
             * @brief オブジェクトの定数を更新する
             * @details CPU 側の値だけを書き換え、GPU へは WriteObjectConstants でフレームごとの領域に書く。
             *          GPU が前のフレームで読んでいる最中の値を上書きしないので、毎フレーム呼んでもよい
             ************************************************************************************/
            void UpdateObject(RHI::ObjectHandle object, const RHI::ObjectConstants& constants);

            /***********************************************************************************
             * @brief オブジェクトの定数をこのフレームの領域へ書き込み、その先頭アドレスを返す
             * @details 前回このフレームの領域に書いてから変わったオブジェクトだけ書く
             *          戻り値を Root SRV (StructuredBuffer<ObjectData>) に渡す。オブジェクト i は戻り値 + i * OBJECT_DATA_STRIDE
             *          書いた範囲は cmdList で VRAM 側 (_objectGpu) にコピーし、GPU はそちらを読む
             *          (以前は UPLOAD ヒープを直接読ませていて、GPU が毎フレーム PCIe 越しに
             *           オブジェクトの数 × 80byte を読んでいた。GPU カリングで 2 回読むようになり、はっきり遅くなった)
             ************************************************************************************/
            D3D12_GPU_VIRTUAL_ADDRESS WriteObjectConstants(uint32_t frameIndex, ID3D12GraphicsCommandList* cmdList);

            /***********************************************************************************
             * @brief メッシュのローカル座標での境界球 (GPU カリング用)
             * @details UploadVertexData で頂点を受け取った時に計算しておく (頂点の範囲ごと)。
             *          インデックスで頂点の一部だけを使う描画でも、範囲全体の球なので見落としは無い (少し大きめになるだけ)
             * @return xyz = 中心, w = 半径。不明なら w = -1 (= カリングしない)
             ************************************************************************************/
            ::DirectX::XMFLOAT4 GetVertexBounds(uint32_t baseVertex)const;

            /***********************************************************************************
             * @brief 今のカメラの視錐台の 6 平面 (ワールド座標。内側が正、xyz は正規化済み)
             * @details 左 / 右 / 下 / 上 / 手前 / 奥 の順。カメラが変わった時だけ計算し直す
             ************************************************************************************/
            const ::DirectX::XMFLOAT4* GetFrustumPlanes();

            // 今のカメラの View × Projection (シェーダー用に転置済み)。オクルージョンカリングで Hi-Z と一緒に覚えておく
            const ::DirectX::XMFLOAT4X4& GetViewProjectionForShader() { GetFrustumPlanes(); return _viewProjForShader; }

            // 登録されている描画オブジェクトの数 (プロファイラー用)
            uint32_t GetObjectCount()const { return static_cast<uint32_t>(_objects.size()); }

            // テクスチャの読み込み口 (既定テクスチャを作ったものと同じものを使う)
            RHI::ObjectInterface::Texture* GetTexture()const { return _texture.get(); }

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
            std::unique_ptr<RHI::DirectX12::ResourceBuffer>        _sceneCB;                    // 各シーンのみで使う定数バッファ (UPLOAD。CPU が書く)
            std::unique_ptr<RHI::DirectX12::ResourceBuffer>        _objectGpu;                  // _sceneCB の VRAM 側の写し (GPU が読む。変わった所だけコピー)
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
            const uint64_t _SCENE_CB_SIZE  = SCENE_CB_SIZE;
            // フレームごとの定数 (View / Projection) : 1 フレーム 256byte × フレーム数
            const uint64_t _COMMON_CB_SIZE = CONSTANT_BUFFER_ALIGNMENT * CommandContext::FRAME_COUNT;
            const uint64_t _GLOBAL_IB_SIZE = 16ull * 1024ull * 1024ull;
            const uint64_t _SCENE_IB_SIZE  = 64ull * 1024ull * 1024ull;

            // ---------- フレームごとの定数の元データ (CPU 側) ----------
            // 行列はカメラが変わった時だけ更新し、毎フレームは書き写すだけにする (シェーダー用に転置済み)
            ::DirectX::XMFLOAT4X4               _viewMatrix     = {};
            ::DirectX::XMFLOAT4X4               _projMatrix     = {};
            // 行列を更新した回数。各フレームの領域に書いた時の値と比べて、変わっていなければ書き込みを省く
            uint64_t                            _frameConstantsVersion = 1;
            uint64_t                            _writtenVersion[CommandContext::FRAME_COUNT] = {};

            // ---------- GPU カリング用 ----------
            std::unordered_map<uint32_t, ::DirectX::XMFLOAT4> _vertexBounds;            // BaseVertex → 境界球
            ::DirectX::XMFLOAT4                 _frustumPlanes[6] = {};
            ::DirectX::XMFLOAT4X4               _viewProjForShader = {};
            uint64_t                            _frustumVersion   = 0;                      // 計算した時の _frameConstantsVersion

            // ---------- オブジェクトごとの定数 (CPU 側) ----------
            std::vector<RHI::ObjectConstants>   _objects;                                   // シェーダー用に転置済み
            std::vector<uint64_t>               _objectVersion;                             // オブジェクトごとの更新回数
            std::vector<uint64_t>               _objectWritten[CommandContext::FRAME_COUNT]; // フレームの領域に書いた時の更新回数
            uint64_t                            _objectsVersion = 1;                        // どれかが更新されたら増える
            uint64_t                            _objectsWrittenVersion[CommandContext::FRAME_COUNT] = {};
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

        };
    }
}


#endif // RHI_BUFFER_H_

/************************************
* End of file
************************************/
