
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

            // Scene定数バッファの 1オブジェクト分の間隔 (シェーダーの struct ObjectData と同じ 64byte = ワールド行列だけ)
            // (テクスチャの番号をマテリアルに移したので 80 → 64byte)
            // シェーダーは StructuredBuffer<ObjectData> (Root SRV) として読むので、16byte の倍数に切り上げるだけでよい
            // 以前は描画ごとに Root CBV で指していたため 256byte 間隔が必須だった (1-9)。
            // 詰めたことで、1 フレームの領域に入るオブジェクトの数が約 3 倍になり、GPU が読むメモリも減る
            static constexpr uint64_t OBJECT_DATA_STRIDE = (sizeof(RHI::ObjectConstants) + 15) & ~15ull;
            static_assert(OBJECT_DATA_STRIDE == 80, "OBJECT_DATA_STRIDE must match HLSL struct ObjectData (80 bytes : World + Info)");   // v1.5 : 以前は 64

            // ---------- マテリアル ----------
            // シェーダーの struct MaterialData と同じ並び (32byte)
            struct MaterialData
            {
                float    Color[4];
                uint32_t TextureIndex;
                uint32_t Blend;          // RHI::BlendMode
                float    Metallic;       // PBR (以前は詰め物だった所を使う)
                float    Roughness;
            };
            static_assert(sizeof(MaterialData) == 32, "MaterialData must be 32 bytes (same as HLSL)");
            static constexpr uint32_t MAX_MATERIALS = 4096;
            static constexpr uint32_t MAX_LIGHTS    = 256;

            // ---------- フレームの定数 (シェーダーの cbuffer FrameData と同じ並び : 560byte) ----------
            //   視点 (カメラ + 影のカスケード) ごとに 1 つずつ書く (FRAME_SLOT_SIZE ごと)。影の項目はカメラの視点だけが使う
            //   以前は 240byte (カメラ 1 つ、256byte の枠)
            struct FrameConstants
            {
                ::DirectX::XMFLOAT4X4 View;            // シェーダー用に転置済み
                ::DirectX::XMFLOAT4X4 Projection;
                ::DirectX::XMFLOAT4X4 InvViewProj;     // 深度から位置を戻す (ディファードのライティング)
                float                 CameraPosition[3];
                uint32_t              LightCount;
                float                 Ambient[3];
                uint32_t              LightingModel;   // RHI::LightingModel
                float                 ScreenSize[2];
                float                 InvScreenSize[2];
                // ---------- 影 (カメラの視点で使う) ----------
                ::DirectX::XMFLOAT4X4 ShadowViewProj[RHI::MAX_SHADOW_CASCADES];   // ワールド → シャドーマップ (転置済み)
                float                 ShadowSplits[RHI::MAX_SHADOW_CASCADES];     // カスケードの奥の端 (カメラからの奥行き)
                float                 ShadowNormalOffset[RHI::MAX_SHADOW_CASCADES];   // 法線方向へずらす長さ (ワールド)
                uint32_t              ShadowLightIndex;     // 影を付けるライト (無ければ 0xFFFFFFFF)
                uint32_t              ShadowMapTexture;     // g_Textures の番号
                uint32_t              ShadowCascadeCount;   // 0 なら影なし
                float                 ShadowTexelSize;      // 1 / 解像度
                float                 ShadowFadeStart;      // ここから Distance まで影を薄くする
                float                 ShadowDistance;
                float                 ShadowSoftness;       // PCF の間隔 (texel)
                float                 ShadowBlend;          // 段の境目をつなぐ幅 (割合)
                uint32_t              ShadowShowCascades;   // 1 : 段ごとに色を付ける (確認用)
                float                 ShadowPadding[3];
            };
            static constexpr uint64_t FRAME_SLOT_SIZE = 768;   // 1 視点の枠 (Root CBV は 256byte の倍数)
            static_assert(sizeof(FrameConstants) <= FRAME_SLOT_SIZE, "FrameConstants must fit in FRAME_SLOT_SIZE");
            static constexpr uint32_t VIEW_COUNT = 1 + RHI::MAX_SHADOW_CASCADES;   // カメラ + 影のカスケード

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
             * @brief マテリアルを作る (テクスチャ / 色 / ブレンドの方法)
             * @details CPU 側に足すだけ。GPU へは WriteMaterials でフレームごとの領域に書いて VRAM へ写す
             *          満杯なら既定のマテリアル (0 : 白・不透明)
             ************************************************************************************/
            RHI::MaterialHandle CreateMaterial(const RHI::MaterialDesc& desc);

            // ---------- ED5 : 変える / 返す (GPU が使い終わってから呼ぶこと。RHI が GPU を待ってから呼ぶ) ----------
            // マテリアルの値を変える (エディターのマテリアル)。ブレンド / 両面が変わったら描画のまとまりを作り直させる
            void UpdateMaterial(RHI::MaterialHandle material, const RHI::MaterialDesc& desc);
            // マテリアルを返す (番号は次の CreateMaterial で使い回す。0 = 既定は返せない)
            void DestroyMaterial(RHI::MaterialHandle material);
            // 頂点 / インデックスの範囲を返す (メガバッファの空きに戻す)
            void FreeVertexData(const RHI::VertexRange& range);
            void FreeIndexData(const RHI::IndexRange& range);
            // 描画のまとまりの分け方が変わった回数 (CommandContext は描画リストが同じでも、これが変われば引数を書き直す)
            uint64_t GetDrawLayoutVersion()const { return _drawLayoutVersion; }
            uint64_t GetVertexBytesUsed()const;
            uint64_t GetIndexBytesUsed()const;

            // ---------- AN4 : GPU スキニング ----------
            struct SkinInstance
            {
                RHI::VertexRange   Source;           // 休みの姿勢の頂点 (モデルの物)
                RHI::VertexRange   Output;           // この物専用の場所 (曲げた頂点を書く)
                RHI::SkinRange     Skin;             // 骨の番号と重み
                std::vector<float> Matrices;         // 骨の行列 (1 本 12 個)
                uint32_t           BoneCount = 0;
                bool               Dirty     = true; // 次の描画の前に曲げる
                bool               Full      = true; // 次に曲げる時に 色 / UV も書く (作った直後の 1 回だけ。以降は位置と法線だけ)
                bool               Alive     = false;
            };
            static constexpr uint32_t MAX_SKIN_MATRICES = 65536;   // 1 フレームに送れる骨の行列の数 (全部の物の合計)
            RHI::SkinRange  UploadSkinData(RHI::ObjectInterface::IDevice* iDevice, RHI::ObjectInterface::ICommandContext* iCommandContext,
                                           const RHI::SkinVertex* vertices, uint32_t count);
            void            FreeSkinData(const RHI::SkinRange& range);
            RHI::SkinHandle CreateSkinInstance(RHI::VertexRange source, RHI::SkinRange skin, uint32_t boneCount);
            RHI::VertexRange GetSkinOutput(RHI::SkinHandle instance)const;
            void            SetSkinPose(RHI::SkinHandle instance, const float* matrices, uint32_t boneCount);
            void            SetSkinBounds(RHI::SkinHandle instance, const float* sphere);
            void            DestroySkinInstance(RHI::SkinHandle instance);
            std::vector<SkinInstance>& GetSkinInstances() { return _skinInstances; }
            RHI::DirectX12::ResourceBuffer* GetSkinBuffer()const { return _skinBuffer.get(); }
            // このフレームの行列の領域に書く (戻り値 = 書いた先頭の行列の番号。入らなければ UINT32_MAX) と、その領域のアドレス
            uint32_t WriteSkinMatrices(uint32_t frameIndex, uint32_t written, const SkinInstance& instance);
            D3D12_GPU_VIRTUAL_ADDRESS GetSkinMatrixAddress(uint32_t frameIndex)const;

            // マテリアルのブレンドの方法 (描く順番と PSO を決める。範囲外なら不透明)
            RHI::BlendMode GetMaterialBlend(uint32_t material)const
            {
                return material < _materials.size() ? static_cast<RHI::BlendMode>(_materials[material].Blend) : RHI::BlendMode::Opaque;
            }

            // マテリアルが両面か (PSO を分ける。GPU のデータには入れない : CPU 側の描画のまとまり分けにだけ使う)
            bool GetMaterialDoubleSided(uint32_t material)const
            {
                return material < _materialDoubleSided.size() && _materialDoubleSided[material] != 0;
            }

            // ---------- 自分のシェーダー (SH2) ----------
            // マテリアルに自分のシェーダーとパラメーター (float4 × count) を付ける (shader = 0 で外す)
            // シェーダーが付いた / 外れた時は描画のまとまりが変わる (まとめて描く道から外れる)
            void SetMaterialShader(RHI::MaterialHandle material, uint32_t shader, const float* params, uint32_t paramCount);
            uint32_t GetMaterialShader(uint32_t material)const
            {
                return material < _materialShader.size() ? _materialShader[material] : 0;
            }
            // マテリアルのパラメーター (float4 の並び。無ければ空)
            const std::vector<float>& GetMaterialParams(uint32_t material)const
            {
                static const std::vector<float> empty;
                return material < _materialParams.size() ? _materialParams[material] : empty;
            }
            // オブジェクトのワールドの位置 (半透明の自分のシェーダーの物を遠い順に並べる)
            ::DirectX::XMFLOAT3 GetObjectPosition(uint32_t object)const;
            const float* GetCameraPosition()const { return _cameraPosition; }
            // オブジェクトの定数 (シェーダー用に転置済みの行列。SH2 : 自分のシェーダーに渡す)。無ければ nullptr
            const RHI::ObjectConstants* GetObjectConstants(uint32_t object)const;

            /***********************************************************************************
             * @brief マテリアルをこのフレームの領域へ書いて VRAM へ写し、その先頭アドレスを返す
             * @details 増えた / 変わった時だけ書く (止まっているシーンでは何もしない)
             *          戻り値を Root SRV (StructuredBuffer<MaterialData>) に渡す
             ************************************************************************************/
            D3D12_GPU_VIRTUAL_ADDRESS WriteMaterials(uint32_t frameIndex, ID3D12GraphicsCommandList* cmdList);

            /***********************************************************************************
             * @brief 描画オブジェクトを削除する (番号は次の CreateObject で使い回す)
             * @details 削除したオブジェクトを指す DrawItem を、次の描画で渡さないこと。
             *          GPU が前のフレームで読んでいる領域は、フレームごとに分けてあるので書き換わらない
             ************************************************************************************/
            void DestroyObject(RHI::ObjectHandle object);

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
            // 視点ごとの視錐台 (0 = カメラ / 1〜 = 影のカスケード。影はライトの直方体)
            const ::DirectX::XMFLOAT4* GetFrustumPlanes(uint32_t view);

            // 登録されている描画オブジェクトの数 (プロファイラー用)
            uint32_t GetObjectCount()const { return static_cast<uint32_t>(_objects.size() - _freeObjects.size()); }

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
            // 視点の定数のアドレス (WriteFrameConstants の後。0 はカメラ = WriteFrameConstants の戻り値と同じ)
            D3D12_GPU_VIRTUAL_ADDRESS GetViewConstants(uint32_t frameIndex, uint32_t view)const;

            // v1.5 : 複数のカメラ。SetCameraViews で覚え、SelectCamera で今のカメラ (行列 / 定数の枠) を切り替える
            //        定数はカメラごとに別の枠に書く (前のカメラの定数を GPU が読んでいる間に上書きしないため)
            void     SetCameraViews(const RHI::CameraView* views, uint32_t count);
            bool     SelectCamera(uint32_t camera);   // 戻り値 = そのカメラがあるか
            uint32_t GetCameraCount()const { return _cameraCount; }
            uint32_t GetCameraSlot()const { return _cameraSlot; }
            uint32_t GetCullingMask()const { return _cameraCount > 0 ? _cameraViews[_cameraSlot].CullingMask : 0xFFFFFFFFu; }

            // 影 (shadowMapTexture : シャドーマップの g_Textures の番号 / resolution : その大きさ)
            void SetShadowSettings(const RHI::ShadowSettings& settings, uint32_t shadowMapTexture, uint32_t resolution);
            uint32_t GetShadowViewCount()const { return _shadowViewCount; }   // 今のフレームで描く影の視点の数 (0 なら影なし)
            bool     IsShadowUnionCulling()const { return (_shadowSettings.Flags & RHI::SHADOW_FLAG_UNION_CULLING) != 0; }
            const ::DirectX::XMFLOAT4* GetShadowUnionPlanes()const { return _shadowUnionPlanes; }   // 全カスケードを囲む箱

            // 画面の大きさ (フレームの定数に入れる。全画面のシェーダーが使う)
            void SetScreenSize(uint32_t width, uint32_t height);

            // ライト / ライティングの方法と環境光 (変わった時だけ呼ばれる。GPU へは次のフレームから)
            void SetLights(const RHI::LightData* lights, uint32_t count);
            void SetLightingSettings(const RHI::LightingSettings& settings);

            // ライトをこのフレームの領域に書いて VRAM へ写し、その先頭アドレスを返す (変わった時だけ書く)
            D3D12_GPU_VIRTUAL_ADDRESS WriteLights(uint32_t frameIndex, ID3D12GraphicsCommandList* cmdList);

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

            // ---------- AN4 : GPU スキニング ----------
            std::unique_ptr<RHI::DirectX12::ResourceBuffer>        _skinBuffer;                 // 骨の番号と重み (VRAM)
            std::unique_ptr<RHI::ResourceBufferAllocator>          _skinAllocator;
            std::unique_ptr<RHI::DirectX12::ResourceBuffer>        _skinMatrices;               // 骨の行列 (UPLOAD。フレームごとの領域)
            std::vector<SkinInstance>                              _skinInstances;
            std::vector<uint32_t>                                  _freeSkinInstances;
            const uint64_t _SKIN_BUFFER_SIZE = 64ull * 1024ull * 1024ull;   // 400 万頂点分

            const uint64_t _GLOBAL_VB_SIZE = 16ull * 1024ull * 1024ull;
            // AN4 : 曲げる物はそれぞれ頂点の場所を持つので広げた (以前は 64 MB = 約 120 万頂点)
            const uint64_t _SCENE_VB_SIZE  = 256ull * 1024ull * 1024ull;
            const uint64_t _GLOBAL_CB_SIZE = 4ull  * 1024ull * 1024ull;
            const uint64_t _SCENE_CB_SIZE  = SCENE_CB_SIZE;
            // フレームごとの定数 (View / Projection) : 1 フレーム 256byte × フレーム数
            // 以前は 1 フレーム 256byte (カメラだけ)。今は 視点の数 × FRAME_SLOT_SIZE
            const uint64_t _COMMON_CB_SIZE = FRAME_SLOT_SIZE * VIEW_COUNT * RHI::MAX_CAMERAS * CommandContext::FRAME_COUNT;   // v1.5 : カメラごと
            const uint64_t _GLOBAL_IB_SIZE = 16ull * 1024ull * 1024ull;
            const uint64_t _SCENE_IB_SIZE  = 64ull * 1024ull * 1024ull;

            // ---------- フレームごとの定数の元データ (CPU 側) ----------
            // 行列はカメラが変わった時だけ更新し、毎フレームは書き写すだけにする (シェーダー用に転置済み)
            ::DirectX::XMFLOAT4X4               _viewMatrix     = {};
            ::DirectX::XMFLOAT4X4               _projMatrix     = {};
            // 行列を更新した回数。各フレームの領域に書いた時の値と比べて、変わっていなければ書き込みを省く
            uint64_t                            _frameConstantsVersion = 1;
            uint64_t                            _writtenVersion[CommandContext::FRAME_COUNT][RHI::MAX_CAMERAS] = {};
            RHI::CameraView                     _cameraViews[RHI::MAX_CAMERAS] = {};   // v1.5
            uint32_t                            _cameraCount = 0;
            uint32_t                            _cameraSlot  = 0;

            // ---------- GPU カリング用 ----------
            std::unordered_map<uint32_t, ::DirectX::XMFLOAT4> _vertexBounds;
            std::vector<uint32_t>                             _freeMaterials;          // 返されたマテリアルの番号 (ED5)
            uint64_t                                          _drawLayoutVersion = 1;  // ED5            // BaseVertex → 境界球
            ::DirectX::XMFLOAT4                 _frustumPlanes[6] = {};
            uint64_t                            _frustumVersion   = 0;                      // 計算した時の _frameConstantsVersion

            // ---------- ライト (CPU 側と、フレームごとの UPLOAD / VRAM) ----------
            std::vector<RHI::LightData>                     _lights;
            uint64_t                                        _lightsVersion = 1;
            uint64_t                                        _lightsWritten[CommandContext::FRAME_COUNT] = {};
            std::unique_ptr<RHI::DirectX12::ResourceBuffer> _lightUpload;
            std::unique_ptr<RHI::DirectX12::ResourceBuffer> _lightGpu;
            RHI::LightingSettings                           _lightingSettings;
            ::DirectX::XMFLOAT4X4                           _invViewProj = {};
            float                                           _cameraPosition[3] = {};
            uint32_t                                        _screenWidth  = 1;
            uint32_t                                        _screenHeight = 1;

            // ---------- 影 ----------
            RHI::ShadowSettings                             _shadowSettings;
            uint32_t                                        _shadowMapTexture = 0;
            uint32_t                                        _shadowResolution = 1;
            uint32_t                                        _shadowViewCount  = 0;   // 作ったカスケードの数 (計算した結果)
            ::DirectX::XMFLOAT4X4                           _shadowView[RHI::MAX_SHADOW_CASCADES] = {};       // そのまま (転置していない)
            ::DirectX::XMFLOAT4X4                           _shadowProj[RHI::MAX_SHADOW_CASCADES] = {};
            float                                           _shadowSplits[RHI::MAX_SHADOW_CASCADES] = {};
            float                                           _shadowNormalOffset[RHI::MAX_SHADOW_CASCADES] = {};
            ::DirectX::XMFLOAT4                             _shadowPlanes[RHI::MAX_SHADOW_CASCADES][6] = {};
            ::DirectX::XMFLOAT4                             _shadowUnionPlanes[6] = {};
            bool                                            _shadowValid[RHI::MAX_SHADOW_CASCADES] = {};   // 一度でも作ったか (止めてよいか)
            void computeShadowViews();   // カメラとライトから、カスケードごとのライトの行列を作る (WriteFrameConstants の中)

            // ---------- マテリアル (CPU 側と、フレームごとの UPLOAD / VRAM) ----------
            std::vector<MaterialData>                       _materials;
            std::vector<uint8_t>                          _materialDoubleSided;   // マテリアルごとの両面 (_materials と同じ並び)
            std::vector<uint32_t>                         _materialShader;        // マテリアルごとの自分のシェーダー (SH2。0 = 無し。_materials と同じ並び)
            std::vector<std::vector<float>>               _materialParams;        // そのパラメーター (float4 の並び)
            uint64_t                                        _materialsVersion = 1;
            uint64_t                                        _materialsWritten[CommandContext::FRAME_COUNT] = {};
            std::unique_ptr<RHI::DirectX12::ResourceBuffer> _materialUpload;    // [フレーム][MAX_MATERIALS]
            std::unique_ptr<RHI::DirectX12::ResourceBuffer> _materialGpu;       // 同じ並びの VRAM 側

            // ---------- オブジェクトごとの定数 (CPU 側) ----------
            std::vector<RHI::ObjectConstants>   _objects;                                   // シェーダー用に転置済み
            std::vector<uint64_t>               _objectVersion;                             // オブジェクトごとの更新回数
            std::vector<uint32_t>               _freeObjects;                               // 削除されて使い回せる番号
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
            bool makeBuffer(RHI::ObjectInterface::Device* device, RHI::DirectX12::ResourceBuffer* resourceBuffer, uint64_t sizeInBytes, D3D12_HEAP_TYPE heapType,
                            D3D12_RESOURCE_FLAGS flags = D3D12_RESOURCE_FLAG_NONE);     // TODO : D3D12の機能に直接触れたくないため要検討

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
