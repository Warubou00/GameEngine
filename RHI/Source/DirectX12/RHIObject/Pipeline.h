
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
            // ----------------------------------------------------------
            enum RootParameterIndex : unsigned int
            {
                ROOT_PARAM_BASE_VERTEX         = 0,  // b0 : 頂点オフセット (ルート定数)
                ROOT_PARAM_VERTEX_BUFFER_TABLE = 1,  // t0 : メガ頂点バッファ (ディスクリプタテーブル)
                ROOT_PARAM_GLOBAL_CBV          = 2,  // b1 : View / Projection 行列 (Root CBV)
                ROOT_PARAM_OBJECT_CBV          = 3,  // b2 : オブジェクトごとの定数 (Root CBV)
                ROOT_PARAM_TEXTURE_TABLE       = 4,  // t1 : テクスチャ配列 (ディスクリプタテーブル)
                ROOT_PARAM_COUNT
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

            ID3D12PipelineState* GetPipelineState()const;
            ID3D12RootSignature* GetRootSignature()const;

            // ExecuteIndirect 用のコマンドシグネチャ (作れなかった環境では nullptr = 通常の描画だけ)
            ID3D12CommandSignature* GetIndirectDrawSignature()const;

            // プロファイラー表示用 : 例 "DXC / SM 6_6 / PSO from cache"
            const std::string& GetDescription()const { return _description; }

        private:         /*---メンバ変数---*/
            std::unique_ptr<DirectX12::PipelineState>    _pipelineState;
            std::unique_ptr<DirectX12::RootSignature>    _rootSignature;
            std::unique_ptr<DirectX12::CommandSignature> _indirectDrawSignature;   // ExecuteIndirect 用
            std::unique_ptr<Shader::ShaderCompiler>      _shaderCompiler;          // DXC (無ければ FXC) + ディスクキャッシュ
            std::unique_ptr<DirectX12::PipelineCache>    _pipelineCache;           // PSO のディスクキャッシュ
            std::vector<CustomPipeline>                  _customPipeline;
            std::string                                  _description;

            RHI_RESULT makeRootSignature(Device* device);
            RHI_RESULT makePipeline(Device* device);
            void       makeIndirectDrawSignature(Device* device);
        };

    }
}

#endif // RHI_PIPELINE_H_

/************************************
* End of file
************************************/
