
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
#include "CommandContext.h"     // DEPTH_FORMAT
// ========================================

// DirectX12 APIのラッパーをインクルード
// ========================================
#include "../Object/dx12_device.h"
#include "../Object/dx12_pipelineState.h"
#include "../Object/dx12_rootSignature.h"
#include "../Object/dx12_swapChain.h"
#include "../../Shader/shaderCompiler.h"
#include "../dx12_pipelineCache.h"

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

			// PSO キャッシュのファイル (実行フォルダ)
			constexpr const wchar_t* PIPELINE_CACHE_PATH = L"pipeline.cache";

			// FNV-1a (64bit) : PSO キャッシュの名前用 (シェーダーが変わったら別の名前になる)
			uint64_t fnv1a(const void* data, size_t size, uint64_t hash = 14695981039346656037ull)
			{
				const uint8_t* p = static_cast<const uint8_t*>(data);
				for (size_t i = 0; i < size; ++i) { hash ^= p[i]; hash *= 1099511628211ull; }
				return hash;
			}
		}

		Pipeline::Pipeline() = default;
		Pipeline::~Pipeline() = default;

		void Pipeline::Dispose()
		{
			// GPUの完了待ちは CommandContext::Dispose で済ませてある前提
			// 新しく作った PSO があればキャッシュをファイルに保存する
			if (_pipelineCache) _pipelineCache->Release();
			_pipelineCache.reset();
			_shaderCompiler.reset();
			_indirectDrawSignature.reset();
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
            ID3D12Device* dx12Device = device->GetDevice()->GetDevice();

            // シェーダーのコンパイラ (DXC / 無ければ FXC) と PSO キャッシュ
            _shaderCompiler = std::make_unique<Shader::ShaderCompiler>();
            _shaderCompiler->Initialize(dx12Device);

            _pipelineCache = std::make_unique<DirectX12::PipelineCache>();
            _pipelineCache->Initialize(dx12Device, PIPELINE_CACHE_PATH);

            RHI_RESULT result = makeRootSignature(device);
            if (result != RHI_SUCCEEDED) return result;

            result = makePipeline(device);
            if (result != RHI_SUCCEEDED) return result;

            // ExecuteIndirect 用 (作れなくても通常の描画で続けられる)
            makeIndirectDrawSignature(device);
            return RHI_SUCCEEDED;
		}

        ID3D12PipelineState* Pipeline::GetPipelineState()const
        {
            return _pipelineState ? _pipelineState->Get() : nullptr;
        }

        ID3D12RootSignature* Pipeline::GetRootSignature()const
        {
            return _rootSignature ? _rootSignature->Get() : nullptr;
        }

        ID3D12CommandSignature* Pipeline::GetIndirectDrawSignature()const
        {
            return _indirectDrawSignature ? _indirectDrawSignature->Get() : nullptr;
        }

        RHI_RESULT Pipeline::makeRootSignature(Device* device)
        {
            // ==============================================================
            // ルートシグネチャ (バージョン 1.1)
            //   1.1 では範囲ごとに「データ / ディスクリプタが変わるかどうか」を伝えられ、
            //   ドライバーが変わらないものを前提に最適化できる (レビュー 3-6)
            //   1.1 に対応していない環境では RootSignature::Create が 1.0 に変換する
            // ==============================================================

            // SRV(メガVB)用のディスクリプタレンジ (t0)
            // DATA_VOLATILE : 描画中に COPY キューが同じバッファの別の場所へ新しいモデルを書き込むことがあるため
            D3D12_DESCRIPTOR_RANGE1 srvRangeVB            = {};
            srvRangeVB.RangeType                          = D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
            srvRangeVB.NumDescriptors                     = 1;
            srvRangeVB.BaseShaderRegister                 = 0; // t0
            srvRangeVB.RegisterSpace                      = 0;
            srvRangeVB.Flags                              = D3D12_DESCRIPTOR_RANGE_FLAG_DATA_VOLATILE;
            srvRangeVB.OffsetInDescriptorsFromTableStart  = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND;

            // テクスチャ配列用のディスクリプタレンジ (t1)
            // DESCRIPTORS_VOLATILE : 無制限の範囲の中にまだテクスチャを入れていない枠があるため (1.0 では既定でこの扱い)
            // DATA_STATIC_WHILE_SET_AT_EXECUTE : テクスチャの中身は描画中に変わらない
            D3D12_DESCRIPTOR_RANGE1 srvRangeTex           = {};
            srvRangeTex.RangeType                         = D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
            srvRangeTex.NumDescriptors                    = UINT_MAX; // UINT_MAX(-1) で無制限(Unbounded)配列を指定
            srvRangeTex.BaseShaderRegister                = 1;  // t1
            srvRangeTex.RegisterSpace                     = 0;
            srvRangeTex.Flags                             = D3D12_DESCRIPTOR_RANGE_FLAG_DESCRIPTORS_VOLATILE |
                                                            D3D12_DESCRIPTOR_RANGE_FLAG_DATA_STATIC_WHILE_SET_AT_EXECUTE;
            srvRangeTex.OffsetInDescriptorsFromTableStart = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND;

            // パラメータの設定 (番号は Pipeline.h の RootParameterIndex を参照)
            D3D12_ROOT_PARAMETER1 rootParams[ROOT_PARAM_COUNT] = {};

            // [0]: メッシュの頂点オフセット (b0) - 32Bit Constants
            auto& baseVertex = rootParams[ROOT_PARAM_BASE_VERTEX];
            baseVertex.ParameterType                       = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
            baseVertex.Constants.ShaderRegister            = 0;
            // 2 個目は ExecuteIndirect の引数で 64bit のアドレスを 8byte 境界にそろえるための詰め物 (シェーダーでは使わない)
            baseVertex.Constants.Num32BitValues            = 2;
            baseVertex.ShaderVisibility                    = D3D12_SHADER_VISIBILITY_VERTEX;

            // [1]: メガVBのSRV (t0) - Descriptor Table
            auto& vbTable = rootParams[ROOT_PARAM_VERTEX_BUFFER_TABLE];
            vbTable.ParameterType                          = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
            vbTable.DescriptorTable.NumDescriptorRanges    = 1;
            vbTable.DescriptorTable.pDescriptorRanges      = &srvRangeVB;
            vbTable.ShaderVisibility                       = D3D12_SHADER_VISIBILITY_VERTEX;

            // [2]: Global定数バッファ (b1) - Root CBV
            //      フレームごとの領域に描画前に書き込み、描画中は変えないので DATA_STATIC_WHILE_SET_AT_EXECUTE
            auto& globalCbv = rootParams[ROOT_PARAM_GLOBAL_CBV];
            globalCbv.ParameterType                        = D3D12_ROOT_PARAMETER_TYPE_CBV;
            globalCbv.Descriptor.ShaderRegister            = 1;
            globalCbv.Descriptor.RegisterSpace             = 0;
            globalCbv.Descriptor.Flags                     = D3D12_ROOT_DESCRIPTOR_FLAG_DATA_STATIC_WHILE_SET_AT_EXECUTE;
            globalCbv.ShaderVisibility                     = D3D12_SHADER_VISIBILITY_ALL;

            // [3]: Scene(Object)定数バッファ (b2) - Root CBV
            auto& objectCbv = rootParams[ROOT_PARAM_OBJECT_CBV];
            objectCbv.ParameterType                        = D3D12_ROOT_PARAMETER_TYPE_CBV;
            objectCbv.Descriptor.ShaderRegister            = 2;
            objectCbv.Descriptor.RegisterSpace             = 0;
            objectCbv.Descriptor.Flags                     = D3D12_ROOT_DESCRIPTOR_FLAG_DATA_STATIC_WHILE_SET_AT_EXECUTE;
            objectCbv.ShaderVisibility                     = D3D12_SHADER_VISIBILITY_ALL;

            // [4]: テクスチャ配列のSRV (t1) - Descriptor Table
            auto& texTable = rootParams[ROOT_PARAM_TEXTURE_TABLE];
            texTable.ParameterType                         = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
            texTable.DescriptorTable.NumDescriptorRanges   = 1;
            texTable.DescriptorTable.pDescriptorRanges     = &srvRangeTex;
            texTable.ShaderVisibility                      = D3D12_SHADER_VISIBILITY_PIXEL; // テクスチャはピクセルシェーダーで読む

            // 静的サンプラーの設定 (s0)
            D3D12_STATIC_SAMPLER_DESC samplerDesc = {};
            samplerDesc.Filter                    = D3D12_FILTER_MIN_MAG_MIP_LINEAR; // ミップマップ間も線形補間 (トライリニア)
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

            D3D12_VERSIONED_ROOT_SIGNATURE_DESC rootSigDesc = {};
            rootSigDesc.Version                       = D3D_ROOT_SIGNATURE_VERSION_1_1;
            rootSigDesc.Desc_1_1.NumParameters        = ROOT_PARAM_COUNT;
            rootSigDesc.Desc_1_1.pParameters          = rootParams;
            rootSigDesc.Desc_1_1.NumStaticSamplers    = 1;
            rootSigDesc.Desc_1_1.pStaticSamplers      = &samplerDesc;
            // 使わないシェーダーステージからのアクセスを禁止すると、ドライバーが少し最適化できる
            rootSigDesc.Desc_1_1.Flags                = D3D12_ROOT_SIGNATURE_FLAG_DENY_HULL_SHADER_ROOT_ACCESS |
                                                        D3D12_ROOT_SIGNATURE_FLAG_DENY_DOMAIN_SHADER_ROOT_ACCESS |
                                                        D3D12_ROOT_SIGNATURE_FLAG_DENY_GEOMETRY_SHADER_ROOT_ACCESS;

            _rootSignature = std::make_unique<DirectX12::RootSignature>();
            const HRESULT hr = _rootSignature->Create(device->GetDevice()->GetDevice(), rootSigDesc);
            if (FAILED(hr))
            {
                Core::Logger::Log("[Pipeline] Failed create root signature.");
                return RHI_FAILED_CREATE_ANY_OBJECT;
            }
            return RHI_SUCCEEDED;
        }

        RHI_RESULT Pipeline::makePipeline(Device* device)
        {
            // ==============================================================
            // パイプラインステート (PSO)
            // ==============================================================
            // シェーダーのコンパイル (DXC / FXC。ShaderCache/ にあれば読むだけ)
            Shader::CompiledShader vs, ps;
            if (!_shaderCompiler->Compile(DEFAULT_SHADER_PATH, "VSMain", Shader::Stage::Vertex, vs)) return RHI_FAILED_COMPILED_SHADER;
            if (!_shaderCompiler->Compile(DEFAULT_SHADER_PATH, "PSMain", Shader::Stage::Pixel,  ps)) return RHI_FAILED_COMPILED_SHADER;

            D3D12_GRAPHICS_PIPELINE_STATE_DESC psoDesc = {};
            psoDesc.pRootSignature = _rootSignature->Get();
            psoDesc.VS = vs.View();
            psoDesc.PS = ps.View();

            // 【最重要】Programmable Vertex Fetchなので入力レイアウトは空にする！
            psoDesc.InputLayout = { nullptr, 0 };

            // ==============================================================
            // ラスタライザステート (RasterizerState) の設定
            // ==============================================================
            D3D12_RASTERIZER_DESC rasterizerDesc = {};
            rasterizerDesc.FillMode = D3D12_FILL_MODE_SOLID; // ポリゴンを塗りつぶす (ワイヤーフレームなら D3D12_FILL_MODE_WIREFRAME)
            // カリングしない (両面とも描画する)
            // TODO(高速化) : 閉じたメッシュなら BACK にするとピクセルシェーダーの実行が約半分になる。
            //               OBJ(右手系) と DirectXMath(左手系) で面の向きが逆になるため、ローダー側で揃えてから有効にする
            rasterizerDesc.CullMode = D3D12_CULL_MODE_NONE;
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
            // ブレンドステート (BlendState) の設定 (不透明 : 上書き)
            // ==============================================================
            D3D12_BLEND_DESC blendDesc = {};
            blendDesc.AlphaToCoverageEnable = FALSE;
            blendDesc.IndependentBlendEnable = FALSE; // 複数のレンダーターゲットで同じブレンド設定を使う

            const D3D12_RENDER_TARGET_BLEND_DESC defaultRenderTargetBlendDesc = {
                FALSE, FALSE,                     // BlendEnable, LogicOpEnable
                D3D12_BLEND_ONE, D3D12_BLEND_ZERO, D3D12_BLEND_OP_ADD,       // RGBのブレンド設定
                D3D12_BLEND_ONE, D3D12_BLEND_ZERO, D3D12_BLEND_OP_ADD,       // Alphaのブレンド設定
                D3D12_LOGIC_OP_NOOP,                                         // 論理演算
                D3D12_COLOR_WRITE_ENABLE_ALL                                 // RGBA全てに書き込む
            };
            for (UINT i = 0; i < D3D12_SIMULTANEOUS_RENDER_TARGET_COUNT; ++i)
            {
                blendDesc.RenderTarget[i] = defaultRenderTargetBlendDesc;
            }

            psoDesc.RasterizerState = rasterizerDesc;
            psoDesc.BlendState = blendDesc;
            // ==============================================================
            // 深度ステンシルステート (以前は深度バッファが無く、奥の面が手前に描かれていた : 2-6)
            // ==============================================================
            psoDesc.DepthStencilState.DepthEnable    = TRUE;
            psoDesc.DepthStencilState.DepthWriteMask = D3D12_DEPTH_WRITE_MASK_ALL;
            psoDesc.DepthStencilState.DepthFunc      = D3D12_COMPARISON_FUNC_LESS;  // 手前(値が小さい)ものが残る。クリア値は 1.0
            psoDesc.DepthStencilState.StencilEnable  = FALSE;
            psoDesc.DSVFormat = CommandContext::DEPTH_FORMAT;                     // 深度バッファと必ず一致させる

            psoDesc.SampleMask = UINT_MAX;
            psoDesc.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
            psoDesc.NumRenderTargets = 1;
            psoDesc.RTVFormats[0] = RHI::DirectX12::SwapChain::FORMAT;  // スワップチェーンと必ず一致させる
            psoDesc.SampleDesc.Count = 1;

            // PSO キャッシュの名前 : ルートシグネチャ・シェーダー・主な設定から作る (どれかが変われば別の PSO として作り直される)
            const uint64_t rootSignatureHash = _rootSignature->GetHash();
            uint64_t hash = fnv1a(&rootSignatureHash, sizeof(rootSignatureHash));
            hash = fnv1a(vs.Bytecode.data(), vs.Bytecode.size(), hash);
            hash = fnv1a(ps.Bytecode.data(), ps.Bytecode.size(), hash);
            hash = fnv1a(&rasterizerDesc, sizeof(rasterizerDesc), hash);
            hash = fnv1a(&psoDesc.DepthStencilState, sizeof(psoDesc.DepthStencilState), hash);
            hash = fnv1a(&psoDesc.RTVFormats[0], sizeof(psoDesc.RTVFormats[0]), hash);
            hash = fnv1a(&psoDesc.DSVFormat, sizeof(psoDesc.DSVFormat), hash);
            wchar_t psoName[64] = {};
            swprintf_s(psoName, L"Default_%016llx", hash);

            // キャッシュにあれば読み込み、無ければ作成して登録する
            Microsoft::WRL::ComPtr<ID3D12PipelineState> pso;
            bool fromCache = false;
            const HRESULT hr = _pipelineCache->GetOrCreateGraphicsPipeline(psoName, psoDesc, pso, &fromCache);
            if (FAILED(hr))
            {
                Core::Logger::Log("[Pipeline] Failed create pipeline state.");
                return RHI_FAILED_CREATE_ANY_OBJECT;
            }
            _pipelineState = std::make_unique<RHI::DirectX12::PipelineState>();
            _pipelineState->Attach(pso);

            // プロファイラー / ログ用の説明
            _description = std::string(_shaderCompiler->GetBackendName()) + " / SM " + _shaderCompiler->GetShaderModel() +
                           " / shader " + (vs.FromCache && ps.FromCache ? "from cache" : "compiled") +
                           " / PSO " + (fromCache ? "from cache" : "created");
            Core::Logger::Log(("[Pipeline] " + _description).c_str());

            return RHI_SUCCEEDED;
        }

        void Pipeline::makeIndirectDrawSignature(Device* device)
        {
            // ==============================================================
            // ExecuteIndirect 用のコマンドシグネチャ
            //   描画 1 回ごとに GPU が引数バッファから読んで設定するもの :
            //     1. 頂点の開始位置 (ルート定数 : b0。2 個目は詰め物)
            //     2. オブジェクトの定数バッファ (Root CBV : b2)
            //     3. DrawIndexedInstanced
            //   ルート引数はルートパラメータの番号が小さい順に並べる決まりがある
            //   (逆にすると "Root parameter {slots, offset} must be increasing" のエラーになる)
            //   並び順は CommandSignature::IndirectDrawCommand と一致させること
            // ==============================================================
            D3D12_INDIRECT_ARGUMENT_DESC args[3] = {};

            args[0].Type                            = D3D12_INDIRECT_ARGUMENT_TYPE_CONSTANT;
            args[0].Constant.RootParameterIndex     = ROOT_PARAM_BASE_VERTEX;
            args[0].Constant.DestOffsetIn32BitValues = 0;
            args[0].Constant.Num32BitValuesToSet    = 2;

            args[1].Type                            = D3D12_INDIRECT_ARGUMENT_TYPE_CONSTANT_BUFFER_VIEW;
            args[1].ConstantBufferView.RootParameterIndex = ROOT_PARAM_OBJECT_CBV;

            args[2].Type                            = D3D12_INDIRECT_ARGUMENT_TYPE_DRAW_INDEXED;

            D3D12_COMMAND_SIGNATURE_DESC desc = {};
            desc.ByteStride       = sizeof(RHI::DirectX12::CommandSignature::IndirectDrawCommand);
            desc.NumArgumentDescs = _countof(args);
            desc.pArgumentDescs   = args;

            // ルート引数を書き換える引数を含むので、ルートシグネチャの指定が必要
            _indirectDrawSignature = std::make_unique<RHI::DirectX12::CommandSignature>();
            const HRESULT hr = _indirectDrawSignature->Create(device->GetDevice()->GetDevice(), _rootSignature->Get(), desc);
            if (FAILED(hr))
            {
                Core::Logger::Log("[Pipeline] ExecuteIndirect is not available. Use direct draw only.");
                _indirectDrawSignature.reset();
            }
        }

        RHI_RESULT Pipeline::MakeCustomPipeline(const std::wstring& vsPath, const std::string& vsEntry, const std::wstring& psPath, const std::string& psEntry)
        {
            if (!_shaderCompiler) return RHI_FAILED_INITIALIZED;

            // シェーダーのコンパイルの確認まで (DXC / FXC、ディスクキャッシュあり)
            Shader::CompiledShader vs, ps;
            // 以前は FXC に vs_6_0 / ps_6_0 を渡していたため必ず失敗していた (1-14)。今は使えるコンパイラに合わせてターゲットを選ぶ
            if (!_shaderCompiler->Compile(vsPath, vsEntry, Shader::Stage::Vertex, vs)) return RHI_FAILED_COMPILED_SHADER;
            if (!_shaderCompiler->Compile(psPath, psEntry, Shader::Stage::Pixel,  ps)) return RHI_FAILED_COMPILED_SHADER;

            // TODO : PSO を作って _customPipeline に登録する (_pipelineCache->GetOrCreateGraphicsPipeline を使う)
            return RHI_SUCCEEDED;
        }
	}
}

/************************************
* End of file
************************************/
