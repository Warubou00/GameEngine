
/***********************************************************************************
* @file    dx12_pipelineState.cpp
* @brief   DX12のPSO
* @details
*
* @author  Keigo Onari
* @date    2026/02/03
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
#include "dx12_pipelineState.h"

// API DirectX12オブジェクトをインクルード
// ========================================
#include "dx12_device.h"
#include "dx12_rootSignature.h"
#include "../../Shader/hlslCompiler.h"

// 外部dllをインクルード
// ========================================
#include "../../Core/Source/Log/logger.h"
// ========================================

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
        PipelineState::PipelineState() {}
		PipelineState::~PipelineState() {}

		// ==============================================
		// PSOの作成
		// ==============================================
        RHI_API_RESULT PipelineState::Initialize(RHI::APIInterface::IDevice* iDevice, RHI::APIInterface::IResourceBinding* iResourceBinding)
		{
            if (!iDevice || !iResourceBinding)
            {
                return RHI_API_FAILED_INVALID_ARGUMENT;
            }

            auto device = static_cast<RHI::DirectX12::Device*>(iDevice);
            auto rootSignature = static_cast<RHI::DirectX12::RootSignature*>(iResourceBinding);

			if (!device || !rootSignature)
			{
				return RHI_API_FAILED_INVALID_ARGUMENT;
			}

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

                return E_INVALIDARG;
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

                return E_INVALIDARG;
            }

            delete hlslCompiler;

            D3D12_GRAPHICS_PIPELINE_STATE_DESC psoDesc = {};
            psoDesc.pRootSignature = rootSignature->Get(); //[cite: 2]
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

			return device->GetDevice()->CreateGraphicsPipelineState(&psoDesc, IID_PPV_ARGS(&_pPSO));
		}

		ID3D12PipelineState* PipelineState::Get()const
		{
			return _pPSO.Get();
		}

		// ==============================================
		// 解放
		// ==============================================
		void PipelineState::Release(void)
		{
			// ComPtrのため明示的な解放は避ける
			//if (_pPSO.Get())_pPSO.Get()->Release();
		}
	}
}

/************************************
* End of file
************************************/