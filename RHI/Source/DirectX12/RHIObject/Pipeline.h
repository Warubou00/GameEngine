
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
            // シャドーマップを描く時の傾斜バイアス (面が傾くほど深度を奥へずらす)
            static constexpr float    SHADOW_SLOPE_BIAS   = 2.0f;

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

            // ----------------------------------------------------------
            // GPU パーティクル (コンピュート) のルートシグネチャのパラメータ番号 (particleSim.shader と対応)
            //   b0 / b1 は描画用と同じ並び (b0 = フレームの定数、b1 = ルート定数 16 個) にして、シェーダーを読みやすくする
            // ----------------------------------------------------------
            enum ParticleRootParameterIndex : unsigned int
            {
                PARTICLE_PARAM_FRAME     = 0,  // b0 : フレームの定数 (View / Projection。Root CBV)
                PARTICLE_PARAM_CONSTANTS = 1,  // b1 : 経過時間 / 数 / ソートの段 (ルート定数)
                PARTICLE_PARAM_EMITTERS  = 2,  // t0 : エミッター (Root SRV)
                PARTICLE_PARAM_PARTICLES_IN  = 3,  // u0 : 入力のパーティクル本体 (Root UAV)
                PARTICLE_PARAM_PARTICLES_OUT = 4,  // u1 : 出力のパーティクル本体 (生き残りを詰めて書く)
                PARTICLE_PARAM_SORTED    = 5,  // u2 : 並べた (キー, 位置) の列
                PARTICLE_PARAM_KEYS      = 6,  // u3 : 生き残った物の (キー, 位置) の列
                // (以前は u0 パーティクル / u1 空きリスト / u2・u3 入力・出力の (キー, 番号) の列)
                PARTICLE_PARAM_COUNTERS  = 7,  // u4 : 数
                PARTICLE_PARAM_ARGS      = 8,  // u5 : DispatchIndirect / 描画の引数
                PARTICLE_PARAM_DEPTH     = 9,  // t1 : シーンの深度 (衝突。ディスクリプタテーブル)
                PARTICLE_PARAM_SORT_BINS = 10, // u6 : ソートの区間ごとの数 / 書く位置 (計数ソート)
                PARTICLE_PARAM_DRAW_DATA = 11, // u7 : 描画用 32byte (更新が書き、頂点シェーダーが読む)
                PARTICLE_PARAM_COUNT
            };
            static constexpr uint32_t PARTICLE_CONSTANT_COUNT = 16;
            static constexpr uint32_t PARTICLE_THREAD_GROUP   = 256;   // CSParticleSimulate / CSParticleInit
            static constexpr uint32_t PARTICLE_EMIT_GROUP     = 64;    // CSParticleEmit
            static constexpr uint32_t PARTICLE_SORT_GROUP     = 2048;  // バイトニックソートで 1 グループの中 (共有メモリ) で並べる数 (1024 スレッド × 2。以前は 1024)
            static constexpr uint32_t PARTICLE_SORT_STEP_LEVELS = 4;   // CSParticleSortStep で 1 回にまとめる段の数 (1 スレッド 16 個)
            static constexpr uint32_t PARTICLE_SORT_BUCKETS   = 32768; // 計数ソートの奥行きの区間の数 (particleSim.shader の SORT_BUCKETS)
            // 並べる長さ (2 の累乗) がこれを超えたら計数ソート、以下ならバイトニックソート
            // (RTX 2070 : 2 万個 0.049 vs 0.093 ms / 6 万個 0.088 vs 0.103 ms / 10 万個 0.180 vs 0.111 ms (バイトニック vs 計数))
            static constexpr uint32_t PARTICLE_COUNTING_SORT_MIN = 65536;

            // GPU パーティクルのコンピュートシェーダー (作れなかった環境では nullptr = パーティクルを使わない)
            enum ParticleKernel : unsigned int
            {
                PARTICLE_KERNEL_INIT = 0,    // CSParticleInit     : 数を 0 に (作り直した時だけ)
                PARTICLE_KERNEL_BEGIN,       // CSParticleBegin    : 出す数を決める
                PARTICLE_KERNEL_EMIT,        // CSParticleEmit     : 出す
                PARTICLE_KERNEL_SIMULATE,    // CSParticleSimulate : 動かす / 生死を分ける
                PARTICLE_KERNEL_FINISH,      // CSParticleFinish   : 描画の引数
                PARTICLE_KERNEL_SORT_LOCAL,  // CSParticleSortLocal  : 1024 個ずつ共有メモリで並べる (最初の段)
                PARTICLE_KERNEL_SORT_STEP,   // CSParticleSortStep   : 離れた 2 個を比べて入れ替える (段の大きい比較)
                PARTICLE_KERNEL_SORT_MERGE,  // CSParticleSortMerge  : 段の残り (距離 512 以下) を共有メモリで
                PARTICLE_KERNEL_BIN_CLEAR,   // CSParticleBinClear   : 計数ソート : 区間の数を 0 に
                PARTICLE_KERNEL_BIN_COUNT,   // CSParticleBinCount   : 計数ソート : 区間ごとに数える
                PARTICLE_KERNEL_BIN_SCAN,    // CSParticleBinScan    : 計数ソート : 区間の先頭の位置 (1 グループ)
                PARTICLE_KERNEL_BIN_SCATTER, // CSParticleBinScatter : 計数ソート : 並べ先へ書く
                PARTICLE_KERNEL_COUNT
            };

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

            // 全画面を描く PSO (blend : 0 なし / 1 加算 / 2 アルファ / 3 乗算済みアルファ)。シーンと同じく初めて使う時に作る
            ID3D12PipelineState* GetFullscreenPso(uint32_t shader, uint32_t blend, const DXGI_FORMAT* rtvFormats, uint32_t rtvCount, uint32_t sampleCount);

            // MSAA の深度を 1 サンプルの深度に解決する PSO (antiAliasing.shader の PSResolveDepth。初めて使う時に作る)
            ID3D12PipelineState* GetDepthResolvePso();

            // 確認用の線の PSO (debug.shader。バックバッファへ、深度なし、アルファブレンド。初めて使う時に作る)
            ID3D12PipelineState* GetDebugLinePso();

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
            ID3D12PipelineState* GetHiZPipelineMs()const;        // CSDownsampleMS (MSAA の深度から段 0 を作る。無ければ nullptr)

            // GPU パーティクル
            bool                    IsParticleAvailable()const;
            ID3D12RootSignature*    GetParticleRootSignature()const;
            ID3D12PipelineState*    GetParticlePipeline(ParticleKernel kernel)const;
            ID3D12CommandSignature* GetParticleDrawSignature()const;     // DrawIndexedInstanced 1 回 (ExecuteIndirect)
            ID3D12CommandSignature* GetParticleDispatchSignature()const; // Dispatch 1 回 (更新の DispatchIndirect)
            // 描く PSO (乗算済みアルファのブレンド・深度なし。描画先の形式ごとに初めて使う時に作る)
            ID3D12PipelineState*    GetParticleDrawPso(const DXGI_FORMAT* rtvFormats, uint32_t rtvCount, uint32_t sampleCount);

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
            Microsoft::WRL::ComPtr<ID3D12PipelineState>    _debugLinePso;
            bool                                           _debugLineFailed = false;
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
            std::unique_ptr<DirectX12::PipelineState>    _hiZPipelineMs;
            std::unique_ptr<DirectX12::RootSignature>    _particleRootSignature;   // GPU パーティクル用
            std::unique_ptr<DirectX12::PipelineState>    _particlePipelines[PARTICLE_KERNEL_COUNT];
            std::unique_ptr<DirectX12::CommandSignature> _particleDrawSignature;
            std::unique_ptr<DirectX12::CommandSignature> _particleDispatchSignature;
            struct ParticleDrawShaders;
            std::unique_ptr<ParticleDrawShaders>         _particleDrawShaders;     // particleDraw.shader (初めて描く時にコンパイル)
            bool                                         _particleDrawFailed = false;
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
            void       makeParticlePipeline(Device* device);
        };

    }
}

#endif // RHI_PIPELINE_H_

/************************************
* End of file
************************************/
