
/***********************************************************************************
* @file    CommandContext.cpp
* @brief   RHI CommandContext
* @details 
*
* @author  Keigo Onari
* @date    2026/4/27
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    
************************************************************************************/


/******************************************************************
* Include Headers
******************************************************************/
#include "Pipeline.h"
#include "Device.h"

#include "../DirectX12/Object/dx12_device.h"
#include "../DirectX12/Object/dx12_pipelineState.h"
#include "../DirectX12/Object/dx12_rootSignature.h"
#include "../Shader/hlslCompiler.h"
#include "../DirectX12/Object/dx12_device.h" 

#include "../../Core/Source/Log/logger.h"

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
	namespace ObjectInterface
	{
		Pipeline::Pipeline() = default;
		Pipeline::~Pipeline() = default;

        RHI_RESULT Pipeline::Initialize()
        {
            return RHI_SUCCEEDED;
        }

		RHI_RESULT Pipeline::Initialize(Device* device)
		{
			if (!device)
			{
				return RHI_FAILED_INVALID_ARGUMENT;
			}

            return makePipeline(device);

			return RHI_SUCCEEDED;
		}

        ID3D12PipelineState* Pipeline::GetPipelineState()const
        {
            return _pipelineState->Get();
        }

        ID3D12RootSignature* Pipeline::GetRootSignature()const
        {
            return _rootSignature->Get();
        }

        RHI_RESULT Pipeline::makePipeline(Device* device)
        {
            HRESULT hr;

            // ==============================================================
            // 1. ルートシグネチャの構築 (dx12_rootSignature.cpp を使用)
            // ==============================================================

            // SRV(メガVB)用のディスクリプタレンジ
            D3D12_DESCRIPTOR_RANGE srvRange            = {};
            srvRange.RangeType                         = D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
            srvRange.NumDescriptors                    = 1;
            srvRange.BaseShaderRegister                = 0; // t0
            srvRange.RegisterSpace                     = 0;
            srvRange.OffsetInDescriptorsFromTableStart = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND;

            // パラメータの設定 (計4つ)
            D3D12_ROOT_PARAMETER rootParams[4]         = {};

            // [0]: メッシュの頂点オフセット (b0) - 32Bit Constants
            rootParams[0].ParameterType                = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
            rootParams[0].Constants.ShaderRegister     = 0;
            rootParams[0].Constants.Num32BitValues     = 1;
            rootParams[0].ShaderVisibility             = D3D12_SHADER_VISIBILITY_VERTEX;

            // [1]: メガVBのSRV (t0) - Descriptor Table
            rootParams[1].ParameterType                       = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
            rootParams[1].DescriptorTable.NumDescriptorRanges = 1;
            rootParams[1].DescriptorTable.pDescriptorRanges   = &srvRange;
            rootParams[1].ShaderVisibility                    = D3D12_SHADER_VISIBILITY_VERTEX;

            // [2]: Global定数バッファ (b1) - Root CBV
            rootParams[2].ParameterType                       = D3D12_ROOT_PARAMETER_TYPE_CBV;
            rootParams[2].Descriptor.ShaderRegister           = 1;
            rootParams[2].Descriptor.RegisterSpace            = 0;
            rootParams[2].ShaderVisibility                    = D3D12_SHADER_VISIBILITY_ALL;

            // [3]: Scene(Object)定数バッファ (b2) - Root CBV
            rootParams[3].ParameterType                       = D3D12_ROOT_PARAMETER_TYPE_CBV;
            rootParams[3].Descriptor.ShaderRegister           = 2;
            rootParams[3].Descriptor.RegisterSpace            = 0;
            rootParams[3].ShaderVisibility                    = D3D12_SHADER_VISIBILITY_ALL;

            D3D12_ROOT_SIGNATURE_DESC rootSigDesc = {};
            rootSigDesc.NumParameters             = 4;
            rootSigDesc.pParameters               = rootParams;
            rootSigDesc.NumStaticSamplers         = 0;
            rootSigDesc.pStaticSamplers           = nullptr;
            rootSigDesc.Flags                     = D3D12_ROOT_SIGNATURE_FLAG_NONE; // IAを使わないためフラグ無し

            // RootSignatureクラスのCreate関数を呼び出し[cite: 2]
            _rootSignature = std::make_unique<DirectX12::RootSignature>();
            hr = _rootSignature->Create(device->GetDevice()->GetDevice(), rootSigDesc); //[cite: 2]
            if (FAILED(hr)) return RHI_FAILED_INITIALIZED;


            // ==============================================================
            // 2. パイプラインステート (PSO) の構築 (dx12_pipelineState.cpp を使用)
            // ==============================================================
            // シェーダーコードをコンパイル
            ID3DBlob* pVSBlob;
            ID3DBlob* pPSBlob;
            ID3DBlob* pErrorBlob = nullptr;

            Shader::HLSLCompiler* hlslCompiler = new Shader::HLSLCompiler();
            // VSのコンパイル
            if (FAILED(hlslCompiler->CompileShader(
                L"../Shader/hlslTemplate.shader",
                "VSMain",
                "vs_5_0",
                &pVSBlob,
                &pErrorBlob
            )))
            {
                std::string errorLog(static_cast<const char*>(pErrorBlob->GetBufferPointer()), pErrorBlob->GetBufferSize());
                Core::Logger::Log(errorLog.c_str());

                return RHI_FAILED_INITIALIZED;
            }

            // PSのコンパイル
            if (FAILED(hlslCompiler->CompileShader(
                L"../Shader/hlslTemplate.shader",
                "PSMain",
                "ps_5_0",
                &pPSBlob,
                &pErrorBlob
            )))
            {
                std::string errorLog(static_cast<const char*>(pErrorBlob->GetBufferPointer()), pErrorBlob->GetBufferSize());
                Core::Logger::Log(errorLog.c_str());

                return RHI_FAILED_INITIALIZED;
            }

            delete hlslCompiler;

            D3D12_GRAPHICS_PIPELINE_STATE_DESC psoDesc = {};
            psoDesc.pRootSignature = _rootSignature->Get(); //[cite: 2]
            psoDesc.VS = { reinterpret_cast<UINT8*>(pVSBlob->GetBufferPointer()), pVSBlob->GetBufferSize() };
            psoDesc.PS = { reinterpret_cast<UINT8*>(pPSBlob->GetBufferPointer()), pPSBlob->GetBufferSize() };

            // 【最重要】Programmable Vertex Fetchなので入力レイアウトは空にする！
            psoDesc.InputLayout = { nullptr, 0 };

            // その他の標準的な設定 (適宜変更してください)
            // ==============================================================
            // ラスタライザステート (RasterizerState) の設定
            // ==============================================================
            D3D12_RASTERIZER_DESC rasterizerDesc = {};
            rasterizerDesc.FillMode = D3D12_FILL_MODE_SOLID; // ポリゴンを塗りつぶす (ワイヤーフレームなら D3D12_FILL_MODE_WIREFRAME)
            rasterizerDesc.CullMode = D3D12_CULL_MODE_BACK;  // 裏面をカリング(描画しない)
            rasterizerDesc.FrontCounterClockwise = FALSE;                 // 時計回りを表面とする
            rasterizerDesc.DepthBias = D3D12_DEFAULT_DEPTH_BIAS;
            rasterizerDesc.DepthBiasClamp = D3D12_DEFAULT_DEPTH_BIAS_CLAMP;
            rasterizerDesc.SlopeScaledDepthBias = D3D12_DEFAULT_SLOPE_SCALED_DEPTH_BIAS;
            rasterizerDesc.DepthClipEnable = TRUE;                  // 深度クリッピングを有効化
            rasterizerDesc.MultisampleEnable = FALSE;                 // MSAAを無効化
            rasterizerDesc.AntialiasedLineEnable = FALSE;
            rasterizerDesc.ForcedSampleCount = 0;
            rasterizerDesc.ConservativeRaster = D3D12_CONSERVATIVE_RASTERIZATION_MODE_OFF;


            // ==============================================================
            // ブレンドステート (BlendState) の設定
            // ==============================================================
            D3D12_BLEND_DESC blendDesc = {};
            blendDesc.AlphaToCoverageEnable = FALSE;
            blendDesc.IndependentBlendEnable = FALSE; // 複数のレンダーターゲットで同じブレンド設定を使う

            // RenderTarget[0] (メインの画面) に対するブレンド設定（ここでは不透明：上書き）
            const D3D12_RENDER_TARGET_BLEND_DESC defaultRenderTargetBlendDesc = {
                FALSE, FALSE,                     // BlendEnable, LogicOpEnable
                D3D12_BLEND_ONE, D3D12_BLEND_ZERO, D3D12_BLEND_OP_ADD,       // RGBのブレンド設定
                D3D12_BLEND_ONE, D3D12_BLEND_ZERO, D3D12_BLEND_OP_ADD,       // Alphaのブレンド設定
                D3D12_LOGIC_OP_NOOP,                                         // 論理演算
                D3D12_COLOR_WRITE_ENABLE_ALL                                 // RGBA全てに書き込む
            };

            // 配列の0番目（レンダーターゲット0）に設定を適用
            for (UINT i = 0; i < D3D12_SIMULTANEOUS_RENDER_TARGET_COUNT; ++i)
            {
                blendDesc.RenderTarget[i] = defaultRenderTargetBlendDesc;
            }

            psoDesc.RasterizerState = rasterizerDesc;
            psoDesc.BlendState = blendDesc;
            psoDesc.DepthStencilState.DepthEnable = FALSE;
            psoDesc.DepthStencilState.StencilEnable = FALSE;
            psoDesc.SampleMask = UINT_MAX;
            psoDesc.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
            psoDesc.NumRenderTargets = 1;
            psoDesc.RTVFormats[0] = DXGI_FORMAT_R8G8B8A8_UNORM;
            psoDesc.SampleDesc.Count = 1;

            // PipelineStateクラスのインスタンス化と生成
            _pipelineState = std::make_unique<RHI::DirectX12::PipelineState>(&psoDesc); //
            hr = _pipelineState->Create(device->GetDevice()->GetDevice()); //
            if (FAILED(hr)) return false;

            return RHI_SUCCEEDED;
        }

        RHI_RESULT Pipeline::MakeCustomPipeline(const std::wstring& vsPath, const std::string& vsEntry, const std::wstring& psPath, const std::string& psEntry)
        {
            Shader::HLSLCompiler* hlslCompiler = new Shader::HLSLCompiler();
            ID3DBlob* vsBlob, *psBlob, *errorBlob;
            hlslCompiler->CompileShader(
                vsPath,
                vsEntry,
                "vs_6_0",
                &vsBlob,
                &errorBlob
            );
            std::string errorLog(static_cast<const char*>(errorBlob->GetBufferPointer()), errorBlob->GetBufferSize());
            Core::Logger::Log(errorLog.c_str());

            hlslCompiler->CompileShader(
                psPath,
                psEntry,
                "ps_6_0",
                &psBlob,
                &errorBlob
            );
            std::string errorLog2(static_cast<const char*>(errorBlob->GetBufferPointer()), errorBlob->GetBufferSize());
            Core::Logger::Log(errorLog2.c_str());

            return RHI_SUCCEEDED;
        }
	}
}

/************************************
* End of file
************************************/
