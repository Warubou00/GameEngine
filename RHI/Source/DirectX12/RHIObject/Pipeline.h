
/***********************************************************************************
* @file    Pipeline.h
* @brief   RHI Piepeline
* @details 
*
* @author  Keigo Onari
* @date    2026/09/16
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    
************************************************************************************/

#pragma once
#ifndef RHI_PIPELINE_H_
#define RHI_PIPELINE_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <vector>
#include <string>
#include <memory>
#include <unordered_map>
#include <d3d12.h>
#include <wrl/client.h>

#include "../../RHIObject/IPipeline.h"

/******************************************************************
* Macro Definitions
******************************************************************/


/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/

// d3d12.h では struct として宣言されているので合わせる (class だと C4099 警告になる)
struct ID3D12PipelineState;
struct ID3D12RootSignature;
struct ID3D12CommandSignature;

namespace RHI
{
    namespace DirectX12
    {
        class PipelineState;
        class RootSignature;
        class CommandSignature;
        class PipelineCache;
    }

    namespace Shader
    {
        class ShaderCompiler;
    }

    namespace ObjectInterface
    {
        class IDevice;
        class Device;

        class Pipeline : public IPipeline
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/
            struct CustomPipeline
            {
                const char* PipelineName;
                std::unique_ptr<RHI::DirectX12::PipelineState> Pipeline;
            };

        public:          /*---定数---*/

            // ----------------------------------------------------------
            // ルートシグネチャのパラメータ番号 (シェーダーのレジスタと対応)
            // 以前は 0〜4 の数字が CommandContext に直書きされていた (2-8)
            // どれも 1 フレームに 1 回だけ設定する。描画ごとに変わるもの (頂点の開始位置 / オブジェクト) は
            // 入力 DRAWID で DrawData を引いて読む
            // (以前は描画ごとにルート定数 b0 とルート CBV b2 を切り替えていて、ExecuteIndirect の GPU 時間が増えていた)
            // ----------------------------------------------------------
            enum RootParameterIndex : unsigned int
            {
                ROOT_PARAM_VERTEX_BUFFER_TABLE = 0,  // t0        : メガ頂点バッファ (ディスクリプタテーブル)
                ROOT_PARAM_GLOBAL_CBV          = 1,  // b0        : View / Projection 行列 (Root CBV)
                ROOT_PARAM_DRAW_DATA_SRV       = 2,  // t1        : 描画ごとのデータ (Root SRV)
                ROOT_PARAM_OBJECT_DATA_SRV     = 3,  // t2        : オブジェクトごとのデータ (Root SRV)
                ROOT_PARAM_TEXTURE_TABLE       = 4,  // t0,space1 : テクスチャ配列 (ディスクリプタテーブル)
                ROOT_PARAM_MATERIAL_SRV        = 5,  // t3        : マテリアル (Root SRV)
                ROOT_PARAM_LIGHT_SRV           = 6,  // t4        : ライト (Root SRV)
                ROOT_PARAM_PASS_CONSTANTS      = 7,  // b1        : パスごとの値 (ルート定数 16 個 : 読むテクスチャの番号 8 + 値 8)
                ROOT_PARAM_COUNT
            };
            static constexpr uint32_t PASS_CONSTANT_COUNT = 16;
            // テクスチャ配列を MSAA のテクスチャとして読む範囲 (t0, space2)。シェーダーの g_TexturesMS[] と同じ数
            static constexpr uint32_t MSAA_TEXTURE_COUNT  = 4096;

            // HDR の色 (Forward / ディファードのライティングの描き先)。C# の RenderTextureFormat.RGBA16F と同じ
            static constexpr DXGI_FORMAT HDR_FORMAT = DXGI_FORMAT_R16G16B16A16_FLOAT;

            // ----------------------------------------------------------
            // 描き方のまとまり (PSO)。不透明 (片面 → 両面) → 半透明 の順に描く
            //   0 / 1 は RHI::BlendMode と同じ番号。両面の不透明は裏面カリングをしない PSO で描く
            //   (以前は不透明 / 半透明の 2 つで、全部両面で描いていた)
            //   引数の並びも 不透明 → 両面の不透明 → 半透明 (GPU カリングは「半透明の先頭より後ろ = 半透明」で見分ける)
            // ----------------------------------------------------------
            enum PipelineGroup : unsigned int
            {
                PIPELINE_OPAQUE              = 0,
                PIPELINE_ALPHA_BLEND         = 1,
                PIPELINE_OPAQUE_DOUBLE_SIDED = 2,
                PIPELINE_GROUP_COUNT
            };

            // ----------------------------------------------------------
            // GPU カリング (コンピュート) のルートシグネチャのパラメータ番号 (gpuCulling.shader と対応)
            // ----------------------------------------------------------
            enum CullRootParameterIndex : unsigned int
            {
                CULL_PARAM_CONSTANTS   = 0,  // b0 : 視錐台 / 数 (ルート定数)
                CULL_PARAM_OBJECTS     = 1,  // t0 : オブジェクトのデータ (Root SRV)
                CULL_PARAM_IN_DRAWS    = 2,  // t1 : CPU が並べた DrawData (Root SRV)
                CULL_PARAM_CULL_DATA   = 3,  // t2 : 境界球と描画の番号 (Root SRV)
                CULL_PARAM_IN_ARGS     = 4,  // t3 : CPU が並べた引数 (Root SRV)
                CULL_PARAM_OUT_DRAWS   = 5,  // u0 : 映ったものの DrawData (Root UAV)
                CULL_PARAM_OUT_ARGS    = 6,  // u1 : 映ったものの引数 + カウンター (Root UAV)
                CULL_PARAM_OUT_COMPACT = 7,  // u2 : 映った描画だけを詰めた引数 (Root UAV)
                CULL_PARAM_HIZ         = 8,  // t4 : Hi-Z (オクルージョンカリング。Root SRV)
                CULL_PARAM_VISIBILITY  = 9,  // u3 : オブジェクトごとの「前のフレームで見えていたか」(Root UAV)
                CULL_PARAM_FRAME       = 10, // b1 : フレームの定数 (View / Projection。Root CBV)
                CULL_PARAM_COUNT
            };
            // float4 ×6 + uint ×9
            // ルートシグネチャの上限は 64 DWORD : 33 + ルート CBV / SRV / UAV 10 個 × 2 = 53
            // (以前は Hi-Z のカメラの行列もルート定数で渡していて 63 だった。今はフレームの定数を Root CBV で読む)
            static constexpr uint32_t CULL_CONSTANT_COUNT = 6 * 4 + 10;   // 以前は + 9 (両面の不透明の先頭を足した)
            static constexpr uint32_t CULL_THREAD_GROUP   = 64;          // numthreads(64, 1, 1)

            // ----------------------------------------------------------
            // Hi-Z (オクルージョンカリング用の深度の縮小ピラミッド) を作るコンピュートのルートパラメータ (hiZ.shader と対応)
            // ----------------------------------------------------------
            enum HiZRootParameterIndex : unsigned int
            {
                HIZ_PARAM_CONSTANTS = 0,  // b0 : 読む段 / 書く段の大きさと位置 (ルート定数)
                HIZ_PARAM_DEPTH     = 1,  // t0 : 深度バッファ (Texture2D。ディスクリプタテーブル)
                                          //      以前は深度バッファの写し (バッファ。Root SRV) だった
                HIZ_PARAM_HIZ       = 2,  // u0 : Hi-Z (Root UAV)
                HIZ_PARAM_COUNT
            };
            static constexpr uint32_t HIZ_LEVELS_PER_PASS = 5;           // 1 回の Dispatch で作る段の数 (16→8→4→2→1)
            static constexpr uint32_t HIZ_CONSTANT_COUNT  = 8 + 4 * HIZ_LEVELS_PER_PASS;   // uint ×8 + uint4 ×5
            static constexpr uint32_t HIZ_THREAD_GROUP    = 16;          // numthreads(16, 16, 1)

        public:          /*---コンストラクタ・デストラクタ---*/
            Pipeline();
            ~Pipeline()override;


        public:          /*---クラスメソッド---*/

            RHI_RESULT Initialize(RHI::ObjectInterface::IDevice* iDevice)override;

            /***********************************************************************************
             * @brief 任意のシェーダーをコンパイルする (作成途中)
             * @details 現状はコンパイルの確認まで。PSOの作成と登録はまだ行わない
             ************************************************************************************/
            RHI_RESULT MakeCustomPipeline(
                const std::wstring& vsPath,
                const std::string& vsEntry,
                const std::wstring& psPath,
                const std::string& psEntry
            );

            void Dispose()override;

        public:          /*---アクセッサー関数群---*/

            ID3D12PipelineState* GetPipelineState()const;   // 既定 (Forward / 不透明 / HDR)。コマンドリストのリセット用

            /***********************************************************************************
             * @brief シーンを描く PSO (シェーダーの種類 × 描き方 × 描画先の形式 ごとに、初めて使う時に作る)
             * @param scenePass RHI::ScenePass (Forward / GBuffer)
             * @param group     PipelineGroup (不透明 / 半透明)
             * @details 作った PSO は覚えておき、PSO のディスクキャッシュにも入れる (2 回目の起動からは読むだけ)
             ************************************************************************************/
            //   sampleCount : MSAA のサンプル数 (描画先と同じ。1 なら MSAA なし)
            ID3D12PipelineState* GetScenePso(uint32_t scenePass, uint32_t group, const DXGI_FORMAT* rtvFormats, uint32_t rtvCount, DXGI_FORMAT dsvFormat, uint32_t sampleCount);

            // 全画面を描く PSO (blend : 0 なし / 1 加算 / 2 アルファ)。シーンと同じく初めて使う時に作る
            ID3D12PipelineState* GetFullscreenPso(uint32_t shader, uint32_t blend, const DXGI_FORMAT* rtvFormats, uint32_t rtvCount, uint32_t sampleCount);

            // MSAA の深度を 1 サンプルの深度に解決する PSO (antiAliasing.shader の PSResolveDepth。初めて使う時に作る)
            ID3D12PipelineState* GetDepthResolvePso();

            /***********************************************************************************
             * @brief 全画面のシェーダーを読み込む (ファイルの VSFullscreen と、entry のピクセルシェーダー)
             * @return uint32_t 番号 (失敗時 0xFFFFFFFF)。同じファイル・同じ entry は同じ番号を返す
             ************************************************************************************/
            uint32_t LoadFullscreenShader(const std::wstring& filePath, const std::string& entry);
            ID3D12RootSignature* GetRootSignature()const;

            // ExecuteIndirect 用のコマンドシグネチャ (作れなかった環境では nullptr = 通常の描画だけ)
            ID3D12CommandSignature* GetIndirectDrawSignature()const;

            // GPU カリング (作れなかった環境では nullptr = カリングしない)
            bool                 IsGpuCullingAvailable()const;
            ID3D12RootSignature* GetCullRootSignature()const;
            ID3D12PipelineState* GetCullClearPipeline()const;   // CSClear
            ID3D12PipelineState* GetCullPipeline()const;        // CSCull
            ID3D12PipelineState* GetCullCompactPipeline()const; // CSCompact

            // Hi-Z を作るコンピュート (作れなかった環境では nullptr = オクルージョンカリングしない)
            bool                 IsHiZAvailable()const;
            ID3D12RootSignature* GetHiZRootSignature()const;
            ID3D12PipelineState* GetHiZPipeline()const;          // CSDownsample

            // プロファイラー表示用 : 例 "DXC / SM 6_6 / PSO from cache"
            const std::string& GetDescription()const { return _description; }

        private:         /*---メンバ変数---*/
            std::unique_ptr<DirectX12::PipelineState>    _pipelineState;           // 既定 (Forward / 不透明 / HDR)

            // シーンのシェーダー (起動時に 1 回コンパイル) / 全画面のシェーダー (C# から読み込む)
            struct SceneShaders;
            struct FullscreenShader;
            std::unique_ptr<SceneShaders>                  _sceneShaders;
            std::vector<std::unique_ptr<FullscreenShader>> _fullscreenShaders;
            uint32_t                                       _depthResolveShader = 0xFFFFFFFFu;   // _fullscreenShaders の番号
            // 作った PSO (鍵 = シェーダー / 描き方 / 描画先の形式 のハッシュ)
            std::unordered_map<uint64_t, Microsoft::WRL::ComPtr<ID3D12PipelineState>> _psoMap;
            Device*                                        _device = nullptr;   // PSO を後から作るのに使う (所有しない)
            std::unique_ptr<DirectX12::RootSignature>    _rootSignature;
            std::unique_ptr<DirectX12::CommandSignature> _indirectDrawSignature;   // ExecuteIndirect 用
            std::unique_ptr<DirectX12::RootSignature>    _cullRootSignature;       // GPU カリング用
            std::unique_ptr<DirectX12::PipelineState>    _cullClearPipeline;
            std::unique_ptr<DirectX12::PipelineState>    _cullPipeline;
            std::unique_ptr<DirectX12::PipelineState>    _cullCompactPipeline;
            std::unique_ptr<DirectX12::RootSignature>    _hiZRootSignature;        // Hi-Z 用
            std::unique_ptr<DirectX12::PipelineState>    _hiZPipeline;
            std::unique_ptr<Shader::ShaderCompiler>      _shaderCompiler;          // DXC (無ければ FXC) + ディスクキャッシュ
            std::unique_ptr<DirectX12::PipelineCache>    _pipelineCache;           // PSO のディスクキャッシュ
            std::vector<CustomPipeline>                  _customPipeline;
            std::string                                  _description;

            RHI_RESULT makeRootSignature(Device* device);
            RHI_RESULT makePipeline(Device* device);
            ID3D12PipelineState* createPso(uint64_t key, const wchar_t* prefix, D3D12_GRAPHICS_PIPELINE_STATE_DESC& desc);
            void       makeIndirectDrawSignature(Device* device);
            void       makeCullingPipeline(Device* device);
            void       makeHiZPipeline(Device* device);
        };

    }
}

#endif // RHI_PIPELINE_H_

/************************************
* End of file
************************************/
