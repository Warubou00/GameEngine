
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

// 必要なクラスをインクルード
// ========================================
#include "Device.h"
// ========================================

// DirectX12 APIのラッパーをインクルード
// ========================================
#include "../Object/dx12_device.h"
#include "../Object/dx12_pipelineState.h"
#include "../Object/dx12_rootSignature.h"
#include "../Object/dx12_swapChain.h"
#include "../../Shader/hlslCompiler.h"

// 2026/09/23 仮
#include "../Object/dx12_commandSignature.h"

// ========================================

// 外部クラスをインクルード
// ========================================
#include "../../Core/Source/Log/logger.h"
// ========================================

#include <wrl.h>

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
		namespace
		{
			// シェーダーのパス (実行ファイルからの相対パス)
			constexpr const wchar_t* DEFAULT_SHADER_PATH = L"../Shader/hlslTemplate.shader";

			// シェーダーをコンパイルし、失敗時はエラー内容をログに出す
			// 以前はファイルが見つからない時など errorBlob が nullptr のまま参照してクラッシュしていた (1-14)
			bool compileShader(
				Shader::HLSLCompiler& compiler,
				const std::wstring& path,
				const std::string& entry,
				const std::string& target,
				Microsoft::WRL::ComPtr<ID3DBlob>& outBlob)
			{
				Microsoft::WRL::ComPtr<ID3DBlob> errorBlob;
				const HRESULT hr = compiler.CompileShader(path, entry, target, outBlob.ReleaseAndGetAddressOf(), errorBlob.GetAddressOf());

				// 成功時でも警告が入っていることがあるので、あれば出す
				if (errorBlob && errorBlob->GetBufferSize() > 0)
				{
					std::string errorLog(static_cast<const char*>(errorBlob->GetBufferPointer()), errorBlob->GetBufferSize());
					Core::Logger::Log(errorLog.c_str());
				}

				if (FAILED(hr))
				{
					char message[128] = {};
					sprintf_s(message, "[Pipeline] Failed compile shader (%s / %s). HRESULT = 0x%08X", entry.c_str(), target.c_str(), static_cast<unsigned int>(hr));
					Core::Logger::Log(message);
					Core::Logger::Log(path.c_str());
					return false;
				}
				return true;
			}
		}

		Pipeline::Pipeline() = default;
		Pipeline::~Pipeline() = default;

		void Pipeline::Dispose()
		{
			// GPUの完了待ちは CommandContext::Dispose で済ませてある前提
			_commandSignature.reset();
			_customPipeline.clear();
			_pipelineState.reset();
			_rootSignature.reset();
		}

		RHI_RESULT Pipeline::Initialize(RHI::ObjectInterface::IDevice* iDevice)
		{
            if (!iDevice)
            {
				return RHI_FAILED_INVALID_ARGUMENT;
            }

            auto device = static_cast<RHI::ObjectInterface::Device*>(iDevice);
			if (!device)
			{
				return RHI_FAILED_INVALID_ARGUMENT;
			}

            return makePipeline(device);
		}

        ID3D12PipelineState* Pipeline::GetPipelineState()const
        {
            return _pipelineState ? _pipelineState->Get() : nullptr;
        }

        ID3D12RootSignature* Pipeline::GetRootSignature()const
        {
            return _rootSignature ? _rootSignature->Get() : nullptr;
        }

        RHI_RESULT Pipeline::makePipeline(Device* device)
        {
            HRESULT hr;

            // ==============================================================
            // 1. ルートシグネチャの構築 (dx12_rootSignature.cpp を使用)
            // ==============================================================

            // SRV(メガVB)用のディスクリプタレンジ (t0)
            D3D12_DESCRIPTOR_RANGE srvRangeVB            = {};
            srvRangeVB.RangeType                         = D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
            srvRangeVB.NumDescriptors                    = 1;
            srvRangeVB.BaseShaderRegister                = 0; // t0
            srvRangeVB.RegisterSpace                     = 0;
            srvRangeVB.OffsetInDescriptorsFromTableStart = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND;

            // ★追加: テクスチャ配列用のディスクリプタレンジ (t1)
            D3D12_DESCRIPTOR_RANGE srvRangeTex            = {};
            srvRangeTex.RangeType                         = D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
            srvRangeTex.NumDescriptors                    = UINT_MAX; // UINT_MAX(-1) で無制限(Unbounded)配列を指定
            srvRangeTex.BaseShaderRegister                = 1;  // t1
            srvRangeTex.RegisterSpace                     = 0;
            srvRangeTex.OffsetInDescriptorsFromTableStart = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND;
            // D3D12_DESCRIPTOR_RANGE_FLAG_DESCRIPTORS_VOLATILE 相当のフラグは D3D12_ROOT_SIGNATURE_FLAG で後述

            // パラメータの設定 (番号は Pipeline.h の RootParameterIndex を参照)
            D3D12_ROOT_PARAMETER rootParams[ROOT_PARAM_COUNT] = {};

            // [0]: メッシュの頂点オフセット (b0) - 32Bit Constants
            auto& baseVertex = rootParams[ROOT_PARAM_BASE_VERTEX];
            baseVertex.ParameterType                       = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
            baseVertex.Constants.ShaderRegister            = 0;
            baseVertex.Constants.Num32BitValues            = 1;
            baseVertex.ShaderVisibility                    = D3D12_SHADER_VISIBILITY_VERTEX;

            // [1]: メガVBのSRV (t0) - Descriptor Table
            auto& vbTable = rootParams[ROOT_PARAM_VERTEX_BUFFER_TABLE];
            vbTable.ParameterType                          = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
            vbTable.DescriptorTable.NumDescriptorRanges    = 1;
            vbTable.DescriptorTable.pDescriptorRanges      = &srvRangeVB;
            vbTable.ShaderVisibility                       = D3D12_SHADER_VISIBILITY_VERTEX;

            // [2]: Global定数バッファ (b1) - Root CBV
            auto& globalCbv = rootParams[ROOT_PARAM_GLOBAL_CBV];
            globalCbv.ParameterType                        = D3D12_ROOT_PARAMETER_TYPE_CBV;
            globalCbv.Descriptor.ShaderRegister            = 1;
            globalCbv.Descriptor.RegisterSpace             = 0;
            globalCbv.ShaderVisibility                     = D3D12_SHADER_VISIBILITY_ALL;

            // [3]: Scene(Object)定数バッファ (b2) - Root CBV
            auto& objectCbv = rootParams[ROOT_PARAM_OBJECT_CBV];
            objectCbv.ParameterType                        = D3D12_ROOT_PARAMETER_TYPE_CBV;
            objectCbv.Descriptor.ShaderRegister            = 2;
            objectCbv.Descriptor.RegisterSpace             = 0;
            objectCbv.ShaderVisibility                     = D3D12_SHADER_VISIBILITY_ALL;

            // [4]: テクスチャ配列のSRV (t1) - Descriptor Table
            auto& texTable = rootParams[ROOT_PARAM_TEXTURE_TABLE];
            texTable.ParameterType                         = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
            texTable.DescriptorTable.NumDescriptorRanges   = 1;
            texTable.DescriptorTable.pDescriptorRanges     = &srvRangeTex;
            texTable.ShaderVisibility                      = D3D12_SHADER_VISIBILITY_PIXEL; // テクスチャはピクセルシェーダーで読む

            // ★追加: 静的サンプラーの設定 (s0)
            D3D12_STATIC_SAMPLER_DESC samplerDesc = {};
            samplerDesc.Filter                    = D3D12_FILTER_MIN_MAG_MIP_LINEAR; // 一般的な線形補間
            samplerDesc.AddressU                  = D3D12_TEXTURE_ADDRESS_MODE_WRAP;
            samplerDesc.AddressV                  = D3D12_TEXTURE_ADDRESS_MODE_WRAP;
            samplerDesc.AddressW                  = D3D12_TEXTURE_ADDRESS_MODE_WRAP;
            samplerDesc.MipLODBias                = 0;
            samplerDesc.MaxAnisotropy             = 0;
            samplerDesc.ComparisonFunc            = D3D12_COMPARISON_FUNC_NEVER;
            samplerDesc.BorderColor               = D3D12_STATIC_BORDER_COLOR_TRANSPARENT_BLACK;
            samplerDesc.MinLOD                    = 0.0f;
            samplerDesc.MaxLOD                    = D3D12_FLOAT32_MAX;
            samplerDesc.ShaderRegister            = 0; // s0
            samplerDesc.RegisterSpace             = 0;
            samplerDesc.ShaderVisibility          = D3D12_SHADER_VISIBILITY_PIXEL;

            D3D12_ROOT_SIGNATURE_DESC rootSigDesc = {};
            rootSigDesc.NumParameters             = ROOT_PARAM_COUNT;
            rootSigDesc.pParameters               = rootParams;
            rootSigDesc.NumStaticSamplers         = 1; // サンプラーを1つ追加
            rootSigDesc.pStaticSamplers           = &samplerDesc;
            // Shader Model 5.1 の Unbounded Array を機能させるため、フラグを追加する場合がありますが、
            // 基本的には D3D12_ROOT_SIGNATURE_FLAG_NONE で動作します。
            rootSigDesc.Flags                     = D3D12_ROOT_SIGNATURE_FLAG_NONE;

            // RootSignatureクラスのCreate関数を呼び出し
            _rootSignature = std::make_unique<DirectX12::RootSignature>();
            hr = _rootSignature->Create(device->GetDevice()->GetDevice(), rootSigDesc);
            if (FAILED(hr))
            {
                Core::Logger::Log("[Pipeline] Failed create root signature.");
                return RHI_FAILED_CREATE_ANY_OBJECT;
            }


            // ==============================================================
            // 2. パイプラインステート (PSO) の構築 (dx12_pipelineState.cpp を使用)
            // ==============================================================
            // シェーダーコードをコンパイル
            // 以前は生ポインタで受けて解放しておらずリークしていた (1-14)
            Microsoft::WRL::ComPtr<ID3DBlob> vsBlob;
            Microsoft::WRL::ComPtr<ID3DBlob> psBlob;
            Shader::HLSLCompiler hlslCompiler;

            // FXC(D3DCompile) が扱えるのは Shader Model 5.1 まで (6.x は DXC が必要)
            if (!compileShader(hlslCompiler, DEFAULT_SHADER_PATH, "VSMain", "vs_5_1", vsBlob)) return RHI_FAILED_COMPILED_SHADER;
            if (!compileShader(hlslCompiler, DEFAULT_SHADER_PATH, "PSMain", "ps_5_1", psBlob)) return RHI_FAILED_COMPILED_SHADER;

            D3D12_GRAPHICS_PIPELINE_STATE_DESC psoDesc = {};
            psoDesc.pRootSignature = _rootSignature->Get();
            psoDesc.VS = { vsBlob->GetBufferPointer(), vsBlob->GetBufferSize() };
            psoDesc.PS = { psBlob->GetBufferPointer(), psBlob->GetBufferSize() };

            // 【最重要】Programmable Vertex Fetchなので入力レイアウトは空にする！
            psoDesc.InputLayout = { nullptr, 0 };

            // その他の標準的な設定 (適宜変更してください)
            // ==============================================================
            // ラスタライザステート (RasterizerState) の設定
            // ==============================================================
            D3D12_RASTERIZER_DESC rasterizerDesc = {};
            rasterizerDesc.FillMode = D3D12_FILL_MODE_SOLID; // ポリゴンを塗りつぶす (ワイヤーフレームなら D3D12_FILL_MODE_WIREFRAME)
            rasterizerDesc.CullMode = D3D12_CULL_MODE_NONE;  // カリングしない (両面とも描画する)
            rasterizerDesc.FrontCounterClockwise = TRUE;      // 反時計回りを表面とする
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
            psoDesc.RTVFormats[0] = RHI::DirectX12::SwapChain::FORMAT;  // スワップチェーンと必ず一致させる
            psoDesc.SampleDesc.Count = 1;

            // PipelineStateクラスのインスタンス化と生成
            _pipelineState = std::make_unique<RHI::DirectX12::PipelineState>();
            hr = _pipelineState->Create(device->GetDevice()->GetDevice(), psoDesc);
            if (FAILED(hr))
            {
                Core::Logger::Log("[Pipeline] Failed create pipeline state.");
                return RHI_FAILED_CREATE_ANY_OBJECT;
            }

            return RHI_SUCCEEDED;
        }

        void Pipeline::InitCommandSignature(RHI::ObjectInterface::Device* device)
        {
            if (!device || !_rootSignature)
            {
                return;
            }

            _commandSignature = std::make_unique<RHI::DirectX12::CommandSignature>();
         
            D3D12_INDIRECT_ARGUMENT_DESC argumentDescs[2] = {};

            // 1つ目: 構造体の先頭にある ObjectID を RootParameter[0] にセットする指示
            argumentDescs[0].Type = D3D12_INDIRECT_ARGUMENT_TYPE_CONSTANT;
            argumentDescs[0].Constant.RootParameterIndex = ROOT_PARAM_BASE_VERTEX; // ルートシグネチャのパラメータ番号
            argumentDescs[0].Constant.Num32BitValuesToSet = 1;
            argumentDescs[0].Constant.DestOffsetIn32BitValues = 0;

            // 2つ目: 構造体の後半にある DrawArguments を使って描画を行う指示
            argumentDescs[1].Type = D3D12_INDIRECT_ARGUMENT_TYPE_DRAW;

            D3D12_COMMAND_SIGNATURE_DESC commandSignatureDesc = {};
            commandSignatureDesc.pArgumentDescs = argumentDescs;
            commandSignatureDesc.NumArgumentDescs = 2;
            commandSignatureDesc.ByteStride = sizeof(RHI::DirectX12::CommandSignature::IndirectCommand); // 20バイト (4の倍数である必要があります)

            HRESULT hr = _commandSignature->Create(device->GetDevice()->GetDevice(), _rootSignature->Get(), commandSignatureDesc);
            if (FAILED(hr))
            {
                Core::Logger::Log("Failed Create CommandSignature");
            }
            else
            {
                Core::Logger::Log("Succeeded Create CommandSignature");
            }
        }

        RHI_RESULT Pipeline::MakeCustomPipeline(const std::wstring& vsPath, const std::string& vsEntry, const std::wstring& psPath, const std::string& psEntry)
        {
            // 以前は FXC に vs_6_0 / ps_6_0 を渡していたため必ず失敗し、さらに nullptr の errorBlob を参照していた (1-14)
            Shader::HLSLCompiler hlslCompiler;
            Microsoft::WRL::ComPtr<ID3DBlob> vsBlob;
            Microsoft::WRL::ComPtr<ID3DBlob> psBlob;

            if (!compileShader(hlslCompiler, vsPath, vsEntry, "vs_5_1", vsBlob)) return RHI_FAILED_COMPILED_SHADER;
            if (!compileShader(hlslCompiler, psPath, psEntry, "ps_5_1", psBlob)) return RHI_FAILED_COMPILED_SHADER;

            // TODO : PSO を作って _customPipeline に登録する (Phase 4 の PSO キャッシュで対応)
            return RHI_SUCCEEDED;
        }
	}
}

/************************************
* End of file
************************************/
