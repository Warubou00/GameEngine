
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
                ROOT_PARAM_COUNT
            };

            // ----------------------------------------------------------
            // 描き方のまとまり (PSO)。この順番に描く (不透明 → 半透明)。RHI::BlendMode と同じ番号
            // ----------------------------------------------------------
            enum PipelineGroup : unsigned int
            {
                PIPELINE_OPAQUE      = 0,
                PIPELINE_ALPHA_BLEND = 1,
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
            static constexpr uint32_t CULL_CONSTANT_COUNT = 6 * 4 + 9;
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

            ID3D12PipelineState* GetPipelineState()const;                 // 不透明
            ID3D12PipelineState* GetPipelineState(uint32_t group)const;   // PipelineGroup ごと
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
            std::unique_ptr<DirectX12::PipelineState>    _pipelineState;           // 不透明
            std::unique_ptr<DirectX12::PipelineState>    _blendPipelineState;      // 半透明 (アルファブレンド)
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
