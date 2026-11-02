
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
#include <algorithm>

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
			constexpr const wchar_t* CULLING_SHADER_PATH = L"../Shader/gpuCulling.shader";
			constexpr const wchar_t* HIZ_SHADER_PATH     = L"../Shader/hiZ.shader";
			constexpr const wchar_t* ANTI_ALIASING_SHADER_PATH = L"../Shader/antiAliasing.shader";   // MSAA の深度の解決

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

        // シーンのシェーダー (hlslTemplate.shader)
        struct Pipeline::SceneShaders
        {
            Shader::CompiledShader Vs;               // VSMain
            Shader::CompiledShader PsForward;        // PSForward      : ライティングまで (不透明)
            Shader::CompiledShader PsForwardAlpha;   // PSForwardAlpha : ライティングまで (半透明。透明な所を捨てる)
            Shader::CompiledShader PsGBuffer;        // PSGBuffer      : ディファード用の G-Buffer
            Shader::CompiledShader PsForwardSplit;   // PSForwardSplit : ライトの光と環境光を分けて書く (Forward + SSAO)
            Shader::CompiledShader PsForwardAlphaSplit;   // PSForwardAlphaSplit : 半透明 (Forward + SSAO)。環境光を覆った分だけ減らす
            Shader::CompiledShader PsShadowAlpha;    // PSShadowAlpha : シャドーマップ (半透明をアルファテストで描く。不透明はピクセルシェーダー無し)
        };

        // 全画面のシェーダー (ファイルの VSFullscreen + entry)
        struct Pipeline::FullscreenShader
        {
            std::wstring           Path;
            std::string            Entry;
            Shader::CompiledShader Vs;
            Shader::CompiledShader Ps;
        };

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
			_hiZPipeline.reset();
			_hiZPipelineMs.reset();
			_hiZRootSignature.reset();
			_cullCompactPipeline.reset();
			_cullPipeline.reset();
			_cullClearPipeline.reset();
			_cullRootSignature.reset();
			_customPipeline.clear();
			_psoMap.clear();
			_fullscreenShaders.clear();
			_sceneShaders.reset();
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
            _device = device;

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

            // GPU カリング用 (ExecuteIndirect が前提。作れなくてもカリングせずに続けられる)
            if (_indirectDrawSignature) makeCullingPipeline(device);
            // オクルージョンカリング用の Hi-Z (GPU カリングが前提)
            if (IsGpuCullingAvailable()) makeHiZPipeline(device);
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

        bool Pipeline::IsGpuCullingAvailable()const
        {
            return _cullRootSignature && _cullClearPipeline && _cullPipeline && _cullCompactPipeline;
        }

        ID3D12RootSignature* Pipeline::GetCullRootSignature()const
        {
            return _cullRootSignature ? _cullRootSignature->Get() : nullptr;
        }

        ID3D12PipelineState* Pipeline::GetCullClearPipeline()const
        {
            return _cullClearPipeline ? _cullClearPipeline->Get() : nullptr;
        }

        ID3D12PipelineState* Pipeline::GetCullPipeline()const
        {
            return _cullPipeline ? _cullPipeline->Get() : nullptr;
        }

        ID3D12PipelineState* Pipeline::GetCullCompactPipeline()const
        {
            return _cullCompactPipeline ? _cullCompactPipeline->Get() : nullptr;
        }

        bool Pipeline::IsHiZAvailable()const
        {
            return _hiZRootSignature && _hiZPipeline;
        }

        ID3D12RootSignature* Pipeline::GetHiZRootSignature()const
        {
            return _hiZRootSignature ? _hiZRootSignature->Get() : nullptr;
        }

        ID3D12PipelineState* Pipeline::GetHiZPipeline()const
        {
            return _hiZPipeline ? _hiZPipeline->Get() : nullptr;
        }

        ID3D12PipelineState* Pipeline::GetHiZPipelineMs()const
        {
            return _hiZPipelineMs ? _hiZPipelineMs->Get() : nullptr;
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
            srvRangeTex.BaseShaderRegister                = 0;  // t0, space1 (上限の無い範囲なので他の SRV と空間を分ける。以前は t1)
            srvRangeTex.RegisterSpace                     = 1;
            srvRangeTex.Flags                             = D3D12_DESCRIPTOR_RANGE_FLAG_DESCRIPTORS_VOLATILE |
                                                            D3D12_DESCRIPTOR_RANGE_FLAG_DATA_STATIC_WHILE_SET_AT_EXECUTE;
            srvRangeTex.OffsetInDescriptorsFromTableStart = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND;

            // 同じテクスチャ配列を MSAA のテクスチャ (Texture2DMS) としても読めるようにする (t0, space2)
            //   範囲は重ねてよい (OffsetInDescriptorsFromTableStart = 0 で同じ場所を指す)。
            //   MSAA の深度を 1 サンプルの深度へ解決するシェーダー (PSResolveDepth) が使う
            //   (Texture2D の配列から MSAA のテクスチャは読めない。種類の違う SRV を同じ添え字で読むため)
            //   t0, space3 : 同じ配列を Texture2DArray<float> として読む (カスケードのシャドーマップを比較サンプラー (SampleCmp) で読むため)
            //                (S1 は Texture2D<float> だった。シャドーマップの SRV を配列にした)
            D3D12_DESCRIPTOR_RANGE1 texRanges[3]           = { srvRangeTex, srvRangeTex, srvRangeTex };
            texRanges[0].OffsetInDescriptorsFromTableStart = 0;
            texRanges[1].NumDescriptors                    = MSAA_TEXTURE_COUNT;
            texRanges[1].RegisterSpace                     = 2;
            texRanges[1].OffsetInDescriptorsFromTableStart = 0;
            texRanges[2].NumDescriptors                    = MSAA_TEXTURE_COUNT;
            texRanges[2].RegisterSpace                     = 3;
            texRanges[2].OffsetInDescriptorsFromTableStart = 0;

            // パラメータの設定 (番号は Pipeline.h の RootParameterIndex を参照)
            D3D12_ROOT_PARAMETER1 rootParams[ROOT_PARAM_COUNT] = {};

            // [0]: メガVBのSRV (t0) - Descriptor Table
            auto& vbTable = rootParams[ROOT_PARAM_VERTEX_BUFFER_TABLE];
            vbTable.ParameterType                          = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
            vbTable.DescriptorTable.NumDescriptorRanges    = 1;
            vbTable.DescriptorTable.pDescriptorRanges      = &srvRangeVB;
            vbTable.ShaderVisibility                       = D3D12_SHADER_VISIBILITY_VERTEX;

            // [1]: Global定数バッファ (b0) - Root CBV
            //      フレームごとの領域に描画前に書き込み、描画中は変えないので DATA_STATIC_WHILE_SET_AT_EXECUTE
            auto& globalCbv = rootParams[ROOT_PARAM_GLOBAL_CBV];
            globalCbv.ParameterType                        = D3D12_ROOT_PARAMETER_TYPE_CBV;
            globalCbv.Descriptor.ShaderRegister            = 0;
            globalCbv.Descriptor.RegisterSpace             = 0;
            globalCbv.Descriptor.Flags                     = D3D12_ROOT_DESCRIPTOR_FLAG_DATA_STATIC_WHILE_SET_AT_EXECUTE;
            globalCbv.ShaderVisibility                     = D3D12_SHADER_VISIBILITY_ALL;   // ライティング (ピクセル) もカメラの位置などを読む

            // [2]: 描画ごとのデータ (t1) - Root SRV (StructuredBuffer<DrawData>)
            //      描画リストが変わった時だけ描画前にコピーで書き換える
            //      (以前の [0] 頂点オフセットのルート定数の代わり)
            auto& drawData = rootParams[ROOT_PARAM_DRAW_DATA_SRV];
            drawData.ParameterType                         = D3D12_ROOT_PARAMETER_TYPE_SRV;
            drawData.Descriptor.ShaderRegister             = 1;
            drawData.Descriptor.RegisterSpace              = 0;
            drawData.Descriptor.Flags                      = D3D12_ROOT_DESCRIPTOR_FLAG_DATA_STATIC_WHILE_SET_AT_EXECUTE;
            drawData.ShaderVisibility                      = D3D12_SHADER_VISIBILITY_VERTEX;

            // [3]: オブジェクトごとのデータ (t2) - Root SRV (StructuredBuffer<ObjectData>)
            //      このフレームの領域の先頭を 1 回だけ設定し、DrawData.ObjectIndex で引く
            //      (以前は描画ごとに Root CBV でオブジェクトの位置を指していた)
            auto& objectData = rootParams[ROOT_PARAM_OBJECT_DATA_SRV];
            objectData.ParameterType                       = D3D12_ROOT_PARAMETER_TYPE_SRV;
            objectData.Descriptor.ShaderRegister           = 2;
            objectData.Descriptor.RegisterSpace            = 0;
            objectData.Descriptor.Flags                    = D3D12_ROOT_DESCRIPTOR_FLAG_DATA_STATIC_WHILE_SET_AT_EXECUTE;
            objectData.ShaderVisibility                    = D3D12_SHADER_VISIBILITY_VERTEX;

            // [4]: テクスチャ配列のSRV (t1) - Descriptor Table
            auto& texTable = rootParams[ROOT_PARAM_TEXTURE_TABLE];
            texTable.ParameterType                         = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
            texTable.DescriptorTable.NumDescriptorRanges   = _countof(texRanges);   // 以前は 1 (Texture2D の配列だけ)
            texTable.DescriptorTable.pDescriptorRanges     = texRanges;
            texTable.ShaderVisibility                      = D3D12_SHADER_VISIBILITY_PIXEL; // テクスチャはピクセルシェーダーで読む

            // [5]: マテリアル (t3) - Root SRV (StructuredBuffer<MaterialData>)
            //      テクスチャの番号・色・ブレンドの方法。DrawData.MaterialIndex で引く (以前はオブジェクトがテクスチャを持っていた)
            auto& materialData = rootParams[ROOT_PARAM_MATERIAL_SRV];
            materialData.ParameterType                     = D3D12_ROOT_PARAMETER_TYPE_SRV;
            materialData.Descriptor.ShaderRegister         = 3;
            materialData.Descriptor.RegisterSpace          = 0;
            materialData.Descriptor.Flags                  = D3D12_ROOT_DESCRIPTOR_FLAG_DATA_STATIC_WHILE_SET_AT_EXECUTE;
            materialData.ShaderVisibility                  = D3D12_SHADER_VISIBILITY_PIXEL;   // ピクセルシェーダーが引く (以前は頂点シェーダーが読んで値を渡していた)

            // [6]: ライト (t4) - Root SRV (StructuredBuffer<LightData>)。ピクセルシェーダーが全部のライトを回す
            auto& lightData = rootParams[ROOT_PARAM_LIGHT_SRV];
            lightData.ParameterType                        = D3D12_ROOT_PARAMETER_TYPE_SRV;
            lightData.Descriptor.ShaderRegister            = 4;
            lightData.Descriptor.RegisterSpace             = 0;
            lightData.Descriptor.Flags                     = D3D12_ROOT_DESCRIPTOR_FLAG_DATA_STATIC_WHILE_SET_AT_EXECUTE;
            lightData.ShaderVisibility                     = D3D12_SHADER_VISIBILITY_PIXEL;

            // [7]: パスごとの値 (b1) - ルート定数 16 個
            //      全画面のシェーダー : [0〜7] 読むテクスチャの番号 (g_Textures の添え字) / [8〜15] 値 (float)
            //      (レンダーターゲットもテクスチャと同じ配列に入れてあるので、番号だけで読める。ディスクリプタの並べ直しが要らない)
            auto& passConstants = rootParams[ROOT_PARAM_PASS_CONSTANTS];
            passConstants.ParameterType                    = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
            passConstants.Constants.ShaderRegister         = 1;
            passConstants.Constants.RegisterSpace          = 0;
            passConstants.Constants.Num32BitValues         = PASS_CONSTANT_COUNT;
            passConstants.ShaderVisibility                 = D3D12_SHADER_VISIBILITY_ALL;

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

            // s1 : 点 (補間しない)・端で止める … G-Buffer / 深度 を 1 ピクセルずつ読む
            // s2 : 線形・端で止める           … 全画面の縮小 / 拡大 (ポストエフェクト)
            // s3 : 比較・線形・外は白 (影の PCF。深度を比べた結果を 2x2 で補間する。シャドーマップの外は「影なし」)
            D3D12_STATIC_SAMPLER_DESC samplers[4] = { samplerDesc, samplerDesc, samplerDesc, samplerDesc };
            samplers[1].Filter         = D3D12_FILTER_MIN_MAG_MIP_POINT;
            samplers[1].AddressU       = samplers[1].AddressV = samplers[1].AddressW = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
            samplers[1].ShaderRegister = 1;
            samplers[2].Filter         = D3D12_FILTER_MIN_MAG_MIP_LINEAR;
            samplers[2].AddressU       = samplers[2].AddressV = samplers[2].AddressW = D3D12_TEXTURE_ADDRESS_MODE_CLAMP;
            samplers[2].ShaderRegister = 2;
            samplers[3].Filter         = D3D12_FILTER_COMPARISON_MIN_MAG_LINEAR_MIP_POINT;
            samplers[3].AddressU       = samplers[3].AddressV = samplers[3].AddressW = D3D12_TEXTURE_ADDRESS_MODE_BORDER;
            samplers[3].BorderColor    = D3D12_STATIC_BORDER_COLOR_OPAQUE_WHITE;
            samplers[3].ComparisonFunc = D3D12_COMPARISON_FUNC_LESS_EQUAL;   // 自分の深度 <= シャドーマップ なら光が当たる
            samplers[3].ShaderRegister = 3;

            D3D12_VERSIONED_ROOT_SIGNATURE_DESC rootSigDesc = {};
            rootSigDesc.Version                       = D3D_ROOT_SIGNATURE_VERSION_1_1;
            rootSigDesc.Desc_1_1.NumParameters        = ROOT_PARAM_COUNT;
            rootSigDesc.Desc_1_1.pParameters          = rootParams;
            rootSigDesc.Desc_1_1.NumStaticSamplers    = _countof(samplers);
            rootSigDesc.Desc_1_1.pStaticSamplers      = samplers;
            // 使わないシェーダーステージからのアクセスを禁止すると、ドライバーが少し最適化できる
            // 入力レイアウトは DRAWID (描画番号) の 1 つだけ使うので ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT が必要
            rootSigDesc.Desc_1_1.Flags                = D3D12_ROOT_SIGNATURE_FLAG_ALLOW_INPUT_ASSEMBLER_INPUT_LAYOUT |
                                                        D3D12_ROOT_SIGNATURE_FLAG_DENY_HULL_SHADER_ROOT_ACCESS |
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
            // シーンのシェーダーをコンパイルし (DXC / FXC。ShaderCache/ にあれば読むだけ)、既定の PSO を作る
            // PSO は描画先の形式ごとに要るので、C# の描画パスが使う組み合わせを初めて使う時に作る (GetScenePso)
            // 以前は「バックバッファに描く」PSO を不透明 / 半透明の 2 つだけ持っていた
            // ==============================================================
            _sceneShaders = std::make_unique<SceneShaders>();
            auto& shaders = *_sceneShaders;
            if (!_shaderCompiler->Compile(DEFAULT_SHADER_PATH, "VSMain",         Shader::Stage::Vertex, shaders.Vs))             return RHI_FAILED_COMPILED_SHADER;
            if (!_shaderCompiler->Compile(DEFAULT_SHADER_PATH, "PSForward",      Shader::Stage::Pixel,  shaders.PsForward))      return RHI_FAILED_COMPILED_SHADER;
            if (!_shaderCompiler->Compile(DEFAULT_SHADER_PATH, "PSForwardAlpha", Shader::Stage::Pixel,  shaders.PsForwardAlpha)) return RHI_FAILED_COMPILED_SHADER;
            if (!_shaderCompiler->Compile(DEFAULT_SHADER_PATH, "PSGBuffer",      Shader::Stage::Pixel,  shaders.PsGBuffer))      return RHI_FAILED_COMPILED_SHADER;
            if (!_shaderCompiler->Compile(DEFAULT_SHADER_PATH, "PSForwardSplit", Shader::Stage::Pixel,  shaders.PsForwardSplit)) return RHI_FAILED_COMPILED_SHADER;
            if (!_shaderCompiler->Compile(DEFAULT_SHADER_PATH, "PSForwardAlphaSplit", Shader::Stage::Pixel, shaders.PsForwardAlphaSplit)) return RHI_FAILED_COMPILED_SHADER;
            if (!_shaderCompiler->Compile(DEFAULT_SHADER_PATH, "PSShadowAlpha", Shader::Stage::Pixel, shaders.PsShadowAlpha)) return RHI_FAILED_COMPILED_SHADER;

            // 既定の PSO (Forward / 不透明 / HDR + 深度)。コマンドリストのリセットに使い、PSO キャッシュも温める
            const DXGI_FORMAT hdr = HDR_FORMAT;
            ID3D12PipelineState* pso = GetScenePso(static_cast<uint32_t>(RHI::ScenePass::Forward), PIPELINE_OPAQUE, &hdr, 1, CommandContext::DEPTH_FORMAT, 1);
            if (!pso)
            {
                Core::Logger::Log("[Pipeline] Failed create pipeline state.");
                return RHI_FAILED_CREATE_ANY_OBJECT;
            }
            Microsoft::WRL::ComPtr<ID3D12PipelineState> defaultPso(pso);
            _pipelineState = std::make_unique<RHI::DirectX12::PipelineState>();
            _pipelineState->Attach(defaultPso);

            // プロファイラー / ログ用の説明
            const bool shaderCached = shaders.Vs.FromCache && shaders.PsForward.FromCache && shaders.PsGBuffer.FromCache;
            _description = std::string(_shaderCompiler->GetBackendName()) + " / SM " + _shaderCompiler->GetShaderModel() +
                           " / shader " + (shaderCached ? "from cache" : "compiled");
            Core::Logger::Log(("[Pipeline] " + _description).c_str());
            return RHI_SUCCEEDED;
        }

        namespace
        {
            // シーン / 全画面に共通の設定 (塗りつぶし・カリングなし・深度あり・ブレンドなし)
            D3D12_GRAPHICS_PIPELINE_STATE_DESC makeBaseDesc(ID3D12RootSignature* rootSignature)
            {
                D3D12_GRAPHICS_PIPELINE_STATE_DESC desc = {};
                desc.pRootSignature = rootSignature;

                // 既定はカリングしない (全画面の三角形など)。シーンの不透明な物は GetScenePso で裏面カリングにする
                // (以前はここに「BACK にすると速くなる」の TODO があり、シーンも全部両面で描いていた)
                desc.RasterizerState.FillMode              = D3D12_FILL_MODE_SOLID;
                desc.RasterizerState.CullMode              = D3D12_CULL_MODE_NONE;
                desc.RasterizerState.FrontCounterClockwise = FALSE;   // 画面上で時計回りが表 (D3D の既定)。以前は TRUE (カリングしないので意味が無かった)
                desc.RasterizerState.DepthBias             = D3D12_DEFAULT_DEPTH_BIAS;
                desc.RasterizerState.DepthBiasClamp        = D3D12_DEFAULT_DEPTH_BIAS_CLAMP;
                desc.RasterizerState.SlopeScaledDepthBias  = D3D12_DEFAULT_SLOPE_SCALED_DEPTH_BIAS;
                desc.RasterizerState.DepthClipEnable       = TRUE;

                for (UINT i = 0; i < D3D12_SIMULTANEOUS_RENDER_TARGET_COUNT; ++i)
                {
                    desc.BlendState.RenderTarget[i] = {
                        FALSE, FALSE,
                        D3D12_BLEND_ONE, D3D12_BLEND_ZERO, D3D12_BLEND_OP_ADD,
                        D3D12_BLEND_ONE, D3D12_BLEND_ZERO, D3D12_BLEND_OP_ADD,
                        D3D12_LOGIC_OP_NOOP, D3D12_COLOR_WRITE_ENABLE_ALL };
                }

                // 深度 (以前は深度バッファが無く、奥の面が手前に描かれていた : 2-6)
                desc.DepthStencilState.DepthEnable    = TRUE;
                desc.DepthStencilState.DepthWriteMask = D3D12_DEPTH_WRITE_MASK_ALL;
                desc.DepthStencilState.DepthFunc      = D3D12_COMPARISON_FUNC_LESS;   // 手前 (値が小さい) ものが残る。クリア値は 1.0

                desc.SampleMask            = UINT_MAX;
                desc.PrimitiveTopologyType = D3D12_PRIMITIVE_TOPOLOGY_TYPE_TRIANGLE;
                desc.SampleDesc.Count      = 1;
                return desc;
            }

            void setAlphaBlend(D3D12_RENDER_TARGET_BLEND_DESC& target)
            {
                // 色 = 自分 × α + 描いてある色 × (1 - α)
                target.BlendEnable    = TRUE;
                target.SrcBlend       = D3D12_BLEND_SRC_ALPHA;
                target.DestBlend      = D3D12_BLEND_INV_SRC_ALPHA;
                target.BlendOp        = D3D12_BLEND_OP_ADD;
                target.SrcBlendAlpha  = D3D12_BLEND_ONE;
                target.DestBlendAlpha = D3D12_BLEND_INV_SRC_ALPHA;
                target.BlendOpAlpha   = D3D12_BLEND_OP_ADD;
            }

            uint64_t hashFormats(uint64_t hash, const DXGI_FORMAT* formats, uint32_t count, DXGI_FORMAT dsv)
            {
                hash = fnv1a(&count, sizeof(count), hash);
                if (count > 0) hash = fnv1a(formats, sizeof(DXGI_FORMAT) * count, hash);
                return fnv1a(&dsv, sizeof(dsv), hash);
            }
        }

        ID3D12PipelineState* Pipeline::createPso(uint64_t key, const wchar_t* prefix, D3D12_GRAPHICS_PIPELINE_STATE_DESC& desc)
        {
            // PSO キャッシュの名前 : ルートシグネチャ・シェーダー・設定 (鍵) から作る (どれかが変われば別の PSO として作り直される)
            const uint64_t rootSignatureHash = _rootSignature->GetHash();
            uint64_t hash = fnv1a(&rootSignatureHash, sizeof(rootSignatureHash), key);
            hash = fnv1a(desc.VS.pShaderBytecode, desc.VS.BytecodeLength, hash);
            hash = fnv1a(desc.PS.pShaderBytecode, desc.PS.BytecodeLength, hash);
            hash = fnv1a(&desc.BlendState, sizeof(desc.BlendState), hash);
            hash = fnv1a(&desc.DepthStencilState, sizeof(desc.DepthStencilState), hash);
            // ラスタライザー (カリング) も名前に入れる。以前は入っておらず、裏面カリングを入れた時に
            // 前の両面の PSO と同じ名前になり、キャッシュの読み込みも保存も失敗していた (D3D12 の警告)
            hash = fnv1a(&desc.RasterizerState, sizeof(desc.RasterizerState), hash);
            wchar_t name[96] = {};
            swprintf_s(name, L"%s_%016llx", prefix, hash);

            Microsoft::WRL::ComPtr<ID3D12PipelineState> pso;
            if (FAILED(_pipelineCache->GetOrCreateGraphicsPipeline(name, desc, pso)))
            {
                Core::Logger::Log(L"[Pipeline] Failed create pipeline state.");
                Core::Logger::Log(name);
                return nullptr;
            }
            _psoMap[key] = pso;
            return pso.Get();
        }

        ID3D12PipelineState* Pipeline::GetScenePso(uint32_t scenePass, uint32_t group, const DXGI_FORMAT* rtvFormats, uint32_t rtvCount, DXGI_FORMAT dsvFormat, uint32_t sampleCount)
        {
            if (!_sceneShaders || rtvCount > RHI::MAX_PASS_COLOR_TARGETS) return nullptr;

            uint64_t key = fnv1a(&scenePass, sizeof(scenePass), 0x5CE7E0ull);
            key = fnv1a(&group, sizeof(group), key);
            key = fnv1a(&sampleCount, sizeof(sampleCount), key);
            key = hashFormats(key, rtvFormats, rtvCount, dsvFormat);
            auto found = _psoMap.find(key);
            if (found != _psoMap.end()) return found->second.Get();

            const auto& shaders = *_sceneShaders;
            D3D12_GRAPHICS_PIPELINE_STATE_DESC desc = makeBaseDesc(_rootSignature->Get());
            desc.VS = shaders.Vs.View();
            const bool gbuffer = scenePass == static_cast<uint32_t>(RHI::ScenePass::GBuffer);
            const bool split   = scenePass == static_cast<uint32_t>(RHI::ScenePass::ForwardSplitAmbient);
            const bool shadow  = scenePass == static_cast<uint32_t>(RHI::ScenePass::Shadow);
            // 裏面カリング : 片面の不透明な物だけ (ピクセルシェーダーの実行が減る。見本のシーンで GPU 時間 -16%)
            //   面の向き : ローダーの頂点の並びは、カメラから見て時計回りが表 (OBJ / PMX とも。確認済み)
            //   両面の物 / 半透明 (描く順番を崩せないので両面のまま) はカリングしない
            if (group == PIPELINE_OPAQUE) desc.RasterizerState.CullMode = D3D12_CULL_MODE_BACK;

            // 半透明は遮蔽を掛けない。環境光を分けて描く時 (split) は、環境光の RT を覆った分だけ減らす
            if (group == PIPELINE_ALPHA_BLEND) desc.PS = split ? shaders.PsForwardAlphaSplit.View() : shaders.PsForwardAlpha.View();
            else if (gbuffer)                  desc.PS = shaders.PsGBuffer.View();
            else if (split)                    desc.PS = shaders.PsForwardSplit.View();
            else                               desc.PS = shaders.PsForward.View();

            if (shadow)
            {
                // シャドーマップ : 深度だけ。不透明はピクセルシェーダー無し、半透明は透明な所を捨てる (アルファテスト)
                //   両面とも描く (薄い物 / 両面の材質も影を落とす。裏面カリングの向きに関係しない)
                //   傾いた面ほど深度をずらして、自分の影が縞になる (シャドーアクネ) のを抑える (残りは読む側の法線オフセット)
                desc.PS = group == PIPELINE_ALPHA_BLEND ? shaders.PsShadowAlpha.View() : D3D12_SHADER_BYTECODE{};
                desc.RasterizerState.CullMode             = D3D12_CULL_MODE_NONE;
                desc.RasterizerState.SlopeScaledDepthBias = SHADOW_SLOPE_BIAS;
                desc.RasterizerState.DepthBiasClamp       = 0.0f;
            }

            // 頂点はシェーダーがメガ頂点バッファから読む (Programmable Vertex Fetch) ので、頂点の入力は無い。
            // 入力アセンブラに読ませるのは、描画番号 (DRAWID) のインスタンス単位の入力 1 つだけ。
            //   中身は 0, 1, 2, ... を並べただけのバッファで、各描画の StartInstanceLocation = 描画番号 にしておくと
            //   その番号から読み始めるので、シェーダーが「自分が何番目の描画か」を知れる
            //   (SV_InstanceID には StartInstanceLocation が加算されないため、この方法を使う)
            static const D3D12_INPUT_ELEMENT_DESC inputElements[] = {
                { "DRAWID", 0, DXGI_FORMAT_R32_UINT, 0, 0, D3D12_INPUT_CLASSIFICATION_PER_INSTANCE_DATA, 1 },
            };
            desc.InputLayout = { inputElements, _countof(inputElements) };

            // 半透明 : アルファブレンド。深度は書く (MMD と同じ。完全に透明な所はピクセルシェーダーで捨てる)
            if (group == PIPELINE_ALPHA_BLEND && !shadow) setAlphaBlend(desc.BlendState.RenderTarget[0]);
            if (group == PIPELINE_ALPHA_BLEND && split)
            {
                // RT1 (環境光) : 0 × 自分 + 描いてある環境光 × (1 - α) = 半透明に覆われた分だけ減らす
                desc.BlendState.IndependentBlendEnable = TRUE;
                auto& ambient = desc.BlendState.RenderTarget[1];
                ambient.BlendEnable = TRUE;
                ambient.SrcBlend    = D3D12_BLEND_ZERO;
                ambient.DestBlend   = D3D12_BLEND_INV_SRC_ALPHA;
                ambient.BlendOp     = D3D12_BLEND_OP_ADD;
            }

            desc.NumRenderTargets = rtvCount;
            for (uint32_t i = 0; i < rtvCount; ++i) desc.RTVFormats[i] = rtvFormats[i];
            desc.DSVFormat = dsvFormat;
            if (dsvFormat == DXGI_FORMAT_UNKNOWN) desc.DepthStencilState.DepthEnable = FALSE;
            // MSAA : 描画先 (色と深度) と同じサンプル数。ピクセルシェーダーは 1 ピクセル 1 回のまま (縁のサンプルだけ増える)
            desc.SampleDesc.Count = (std::max)(1u, sampleCount);

            return createPso(key, shadow ? L"SceneShadow" : gbuffer ? L"SceneGBuffer" : L"SceneForward", desc);
        }

        ID3D12PipelineState* Pipeline::GetFullscreenPso(uint32_t shader, uint32_t blend, const DXGI_FORMAT* rtvFormats, uint32_t rtvCount, uint32_t sampleCount)
        {
            if (shader >= _fullscreenShaders.size() || rtvCount > RHI::MAX_PASS_COLOR_TARGETS) return nullptr;

            uint64_t key = fnv1a(&shader, sizeof(shader), 0xF0115C2Eull);
            key = fnv1a(&blend, sizeof(blend), key);
            key = fnv1a(&sampleCount, sizeof(sampleCount), key);
            key = hashFormats(key, rtvFormats, rtvCount, DXGI_FORMAT_UNKNOWN);
            auto found = _psoMap.find(key);
            if (found != _psoMap.end()) return found->second.Get();

            // 全画面の三角形 1 枚 (頂点はシェーダーが SV_VertexID から作る)。深度は使わない
            const auto& fullscreen = *_fullscreenShaders[shader];
            D3D12_GRAPHICS_PIPELINE_STATE_DESC desc = makeBaseDesc(_rootSignature->Get());
            desc.VS = fullscreen.Vs.View();
            desc.PS = fullscreen.Ps.View();
            desc.InputLayout = { nullptr, 0 };
            desc.DepthStencilState.DepthEnable    = FALSE;
            desc.DepthStencilState.DepthWriteMask = D3D12_DEPTH_WRITE_MASK_ZERO;
            if (blend == 1)
            {
                // 加算 (光を足す)
                auto& target = desc.BlendState.RenderTarget[0];
                target.BlendEnable = TRUE;
                target.SrcBlend    = D3D12_BLEND_ONE;
                target.DestBlend   = D3D12_BLEND_ONE;
            }
            else if (blend == 2)
            {
                setAlphaBlend(desc.BlendState.RenderTarget[0]);
            }
            desc.NumRenderTargets = rtvCount;
            for (uint32_t i = 0; i < rtvCount; ++i) desc.RTVFormats[i] = rtvFormats[i];
            desc.DSVFormat = DXGI_FORMAT_UNKNOWN;
            desc.SampleDesc.Count = (std::max)(1u, sampleCount);   // MSAA の描画先に足す時 (Forward + SSAO の環境光)
            return createPso(key, L"Fullscreen", desc);
        }

        ID3D12PipelineState* Pipeline::GetDepthResolvePso()
        {
            // ==============================================================
            // MSAA の深度 → 1 サンプルの深度 (全画面で SV_Depth に書く。色の描画先は無し)
            //   深度はハードウェアの Resolve (ResolveSubresource) では解決できない (平均すると物の縁で
            //   どちらの物でもない深度になる) ので、シェーダーでサンプルの「一番奥」を選ぶ
            //   一番奥 : Hi-Z (オクルージョン) が、縁で見えている物を隠れたと判定しないように (控えめな側に倒す)
            // ==============================================================
            if (_depthResolveShader == 0xFFFFFFFFu)
            {
                _depthResolveShader = LoadFullscreenShader(ANTI_ALIASING_SHADER_PATH, "PSResolveDepth");
                if (_depthResolveShader == 0xFFFFFFFFu) return nullptr;
            }
            const uint64_t key = fnv1a(&_depthResolveShader, sizeof(_depthResolveShader), 0xDE97E5ull);
            auto found = _psoMap.find(key);
            if (found != _psoMap.end()) return found->second.Get();

            const auto& fullscreen = *_fullscreenShaders[_depthResolveShader];
            D3D12_GRAPHICS_PIPELINE_STATE_DESC desc = makeBaseDesc(_rootSignature->Get());
            desc.VS = fullscreen.Vs.View();
            desc.PS = fullscreen.Ps.View();
            desc.InputLayout = { nullptr, 0 };
            desc.DepthStencilState.DepthFunc = D3D12_COMPARISON_FUNC_ALWAYS;   // 前の値に関係なく書く
            desc.NumRenderTargets = 0;
            desc.DSVFormat        = CommandContext::DEPTH_FORMAT;
            return createPso(key, L"DepthResolve", desc);
        }

        uint32_t Pipeline::LoadFullscreenShader(const std::wstring& filePath, const std::string& entry)
        {
            for (uint32_t i = 0; i < _fullscreenShaders.size(); ++i)
            {
                if (_fullscreenShaders[i]->Path == filePath && _fullscreenShaders[i]->Entry == entry) return i;
            }

            auto shader = std::make_unique<FullscreenShader>();
            shader->Path  = filePath;
            shader->Entry = entry;
            if (!_shaderCompiler->Compile(filePath, "VSFullscreen", Shader::Stage::Vertex, shader->Vs) ||
                !_shaderCompiler->Compile(filePath, entry, Shader::Stage::Pixel, shader->Ps))
            {
                Core::Logger::Log(("[Pipeline] Failed compile fullscreen shader : " + entry).c_str());
                return 0xFFFFFFFFu;
            }
            _fullscreenShaders.push_back(std::move(shader));
            return static_cast<uint32_t>(_fullscreenShaders.size() - 1);
        }

        void Pipeline::makeIndirectDrawSignature(Device* device)
        {
            // ==============================================================
            // ExecuteIndirect 用のコマンドシグネチャ
            //   描画 1 回ごとに GPU が引数バッファから読むのは DrawIndexedInstanced の引数だけ。
            //   描画ごとの情報は StartInstanceLocation = 描画番号 → シェーダーが DrawData を引く、で渡す
            //   以前は [ルート定数 b0 ×2][ルート CBV b2][描画] の 40byte で、GPU が描画ごとにルート引数を
            //   書き換えるのが遅かった (RTX 2070 / 20002 描画で GPU 0.85 ms。描画だけなら 0.43 ms)
            // ==============================================================
            D3D12_INDIRECT_ARGUMENT_DESC args[1] = {};
            args[0].Type = D3D12_INDIRECT_ARGUMENT_TYPE_DRAW_INDEXED;

            D3D12_COMMAND_SIGNATURE_DESC desc = {};
            desc.ByteStride       = sizeof(RHI::DirectX12::CommandSignature::IndirectDrawCommand);
            desc.NumArgumentDescs = _countof(args);
            desc.pArgumentDescs   = args;

            // ルート引数を書き換えないので、ルートシグネチャは渡さない (渡すと作成に失敗する決まり)
            _indirectDrawSignature = std::make_unique<RHI::DirectX12::CommandSignature>();
            const HRESULT hr = _indirectDrawSignature->Create(device->GetDevice()->GetDevice(), nullptr, desc);
            if (FAILED(hr))
            {
                Core::Logger::Log("[Pipeline] ExecuteIndirect is not available. Use direct draw only.");
                _indirectDrawSignature.reset();
            }
        }

        void Pipeline::makeCullingPipeline(Device* device)
        {
            // ==============================================================
            // GPU カリング (コンピュートシェーダー) のルートシグネチャと PSO
            //   描画用とは別のルートシグネチャ。どれも 1 フレームに 1 回設定するだけなので
            //   ディスクリプタヒープを使わず、ルート定数と Root SRV / UAV だけで組む
            // ==============================================================
            D3D12_ROOT_PARAMETER1 params[CULL_PARAM_COUNT] = {};

            params[CULL_PARAM_CONSTANTS].ParameterType            = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
            params[CULL_PARAM_CONSTANTS].Constants.ShaderRegister = 0;
            params[CULL_PARAM_CONSTANTS].Constants.Num32BitValues = CULL_CONSTANT_COUNT;

            // 読むだけのもの (t0〜t3)。カリングの間に中身は変わらない
            const CullRootParameterIndex srvs[] = { CULL_PARAM_OBJECTS, CULL_PARAM_IN_DRAWS, CULL_PARAM_CULL_DATA, CULL_PARAM_IN_ARGS, CULL_PARAM_HIZ };
            for (UINT i = 0; i < _countof(srvs); ++i)
            {
                auto& param = params[srvs[i]];
                param.ParameterType             = D3D12_ROOT_PARAMETER_TYPE_SRV;
                param.Descriptor.ShaderRegister = i;
                param.Descriptor.Flags          = D3D12_ROOT_DESCRIPTOR_FLAG_DATA_STATIC_WHILE_SET_AT_EXECUTE;
            }
            // 書き込むもの (u0〜u3)。atomic で書き換えるので DATA_VOLATILE
            const CullRootParameterIndex uavs[] = { CULL_PARAM_OUT_DRAWS, CULL_PARAM_OUT_ARGS, CULL_PARAM_OUT_COMPACT, CULL_PARAM_VISIBILITY };
            for (UINT i = 0; i < _countof(uavs); ++i)
            {
                auto& param = params[uavs[i]];
                param.ParameterType             = D3D12_ROOT_PARAMETER_TYPE_UAV;
                param.Descriptor.ShaderRegister = i;
                param.Descriptor.Flags          = D3D12_ROOT_DESCRIPTOR_FLAG_DATA_VOLATILE;
            }
            // フレームの定数 (b1) : 描画用と同じ View / Projection。後段のオクルージョンカリングで投影に使う
            params[CULL_PARAM_FRAME].ParameterType             = D3D12_ROOT_PARAMETER_TYPE_CBV;
            params[CULL_PARAM_FRAME].Descriptor.ShaderRegister = 1;
            params[CULL_PARAM_FRAME].Descriptor.Flags          = D3D12_ROOT_DESCRIPTOR_FLAG_DATA_STATIC_WHILE_SET_AT_EXECUTE;
            for (auto& param : params) param.ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;   // コンピュートは ALL のみ

            D3D12_VERSIONED_ROOT_SIGNATURE_DESC desc = {};
            desc.Version                = D3D_ROOT_SIGNATURE_VERSION_1_1;
            desc.Desc_1_1.NumParameters = CULL_PARAM_COUNT;
            desc.Desc_1_1.pParameters   = params;

            auto rootSignature = std::make_unique<DirectX12::RootSignature>();
            if (FAILED(rootSignature->Create(device->GetDevice()->GetDevice(), desc)))
            {
                Core::Logger::Log("[Pipeline] Failed create culling root signature. GPU culling disabled.");
                return;
            }

            // シェーダー (ShaderCache/ にあれば読むだけ) と PSO (pipeline.cache にあれば読むだけ)
            auto makePso = [&](const char* entry, std::unique_ptr<DirectX12::PipelineState>& out) -> bool
            {
                Shader::CompiledShader cs;
                if (!_shaderCompiler->Compile(CULLING_SHADER_PATH, entry, Shader::Stage::Compute, cs)) return false;

                D3D12_COMPUTE_PIPELINE_STATE_DESC psoDesc = {};
                psoDesc.pRootSignature = rootSignature->Get();
                psoDesc.CS             = cs.View();

                const uint64_t rootSignatureHash = rootSignature->GetHash();
                uint64_t hash = fnv1a(&rootSignatureHash, sizeof(rootSignatureHash));
                hash = fnv1a(cs.Bytecode.data(), cs.Bytecode.size(), hash);
                wchar_t psoName[64] = {};
                swprintf_s(psoName, L"Cull_%016llx", hash);

                Microsoft::WRL::ComPtr<ID3D12PipelineState> pso;
                if (FAILED(_pipelineCache->GetOrCreateComputePipeline(psoName, psoDesc, pso))) return false;
                out = std::make_unique<DirectX12::PipelineState>();
                out->Attach(pso);
                return true;
            };

            std::unique_ptr<DirectX12::PipelineState> clearPso, cullPso, compactPso;
            if (!makePso("CSClear", clearPso) || !makePso("CSCull", cullPso) || !makePso("CSCompact", compactPso))
            {
                Core::Logger::Log("[Pipeline] Failed create culling pipeline. GPU culling disabled.");
                return;
            }
            _cullRootSignature = std::move(rootSignature);
            _cullClearPipeline = std::move(clearPso);
            _cullPipeline      = std::move(cullPso);
            _cullCompactPipeline = std::move(compactPso);
        }

        void Pipeline::makeHiZPipeline(Device* device)
        {
            // ==============================================================
            // Hi-Z (深度の縮小ピラミッド) を作るコンピュートのルートシグネチャと PSO
            //   深度バッファはテクスチャなので、ディスクリプタテーブル (1 個) で読む。Hi-Z はバッファなので Root UAV
            //   (以前は深度バッファをバッファに写してから Root SRV で読んでいた。写すのに 約 0.012 ms かかっていた)
            // ==============================================================
            D3D12_DESCRIPTOR_RANGE1 depthRange = {};
            depthRange.RangeType                         = D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
            depthRange.NumDescriptors                    = 1;
            depthRange.BaseShaderRegister                = 0;   // t0
            depthRange.RegisterSpace                     = 0;
            depthRange.Flags                             = D3D12_DESCRIPTOR_RANGE_FLAG_DATA_STATIC_WHILE_SET_AT_EXECUTE;
            depthRange.OffsetInDescriptorsFromTableStart = 0;
            // 同じディスクリプタを MSAA の深度 (Texture2DMS) としても読む (t1。範囲を重ねる)
            D3D12_DESCRIPTOR_RANGE1 depthRanges[2] = { depthRange, depthRange };
            depthRanges[1].BaseShaderRegister = 1;

            D3D12_ROOT_PARAMETER1 params[HIZ_PARAM_COUNT] = {};
            params[HIZ_PARAM_CONSTANTS].ParameterType            = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
            params[HIZ_PARAM_CONSTANTS].Constants.ShaderRegister = 0;
            params[HIZ_PARAM_CONSTANTS].Constants.Num32BitValues = HIZ_CONSTANT_COUNT;
            params[HIZ_PARAM_DEPTH].ParameterType                       = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
            params[HIZ_PARAM_DEPTH].DescriptorTable.NumDescriptorRanges = _countof(depthRanges);   // 以前は 1 (t0 だけ)
            params[HIZ_PARAM_DEPTH].DescriptorTable.pDescriptorRanges   = depthRanges;
            params[HIZ_PARAM_HIZ].ParameterType               = D3D12_ROOT_PARAMETER_TYPE_UAV;
            params[HIZ_PARAM_HIZ].Descriptor.ShaderRegister   = 0;
            params[HIZ_PARAM_HIZ].Descriptor.Flags            = D3D12_ROOT_DESCRIPTOR_FLAG_DATA_VOLATILE;
            for (auto& param : params) param.ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

            D3D12_VERSIONED_ROOT_SIGNATURE_DESC desc = {};
            desc.Version                = D3D_ROOT_SIGNATURE_VERSION_1_1;
            desc.Desc_1_1.NumParameters = HIZ_PARAM_COUNT;
            desc.Desc_1_1.pParameters   = params;

            auto rootSignature = std::make_unique<DirectX12::RootSignature>();
            if (FAILED(rootSignature->Create(device->GetDevice()->GetDevice(), desc)))
            {
                Core::Logger::Log("[Pipeline] Failed create Hi-Z root signature. Occlusion culling disabled.");
                return;
            }

            Shader::CompiledShader cs;
            if (!_shaderCompiler->Compile(HIZ_SHADER_PATH, "CSDownsample", Shader::Stage::Compute, cs))
            {
                Core::Logger::Log("[Pipeline] Failed compile Hi-Z shader. Occlusion culling disabled.");
                return;
            }

            D3D12_COMPUTE_PIPELINE_STATE_DESC psoDesc = {};
            psoDesc.pRootSignature = rootSignature->Get();
            psoDesc.CS             = cs.View();

            const uint64_t rootSignatureHash = rootSignature->GetHash();
            uint64_t hash = fnv1a(&rootSignatureHash, sizeof(rootSignatureHash));
            hash = fnv1a(cs.Bytecode.data(), cs.Bytecode.size(), hash);
            wchar_t psoName[64] = {};
            swprintf_s(psoName, L"HiZ_%016llx", hash);

            Microsoft::WRL::ComPtr<ID3D12PipelineState> pso;
            if (FAILED(_pipelineCache->GetOrCreateComputePipeline(psoName, psoDesc, pso)))
            {
                Core::Logger::Log("[Pipeline] Failed create Hi-Z pipeline. Occlusion culling disabled.");
                return;
            }
            _hiZRootSignature = std::move(rootSignature);
            _hiZPipeline      = std::make_unique<DirectX12::PipelineState>();
            _hiZPipeline->Attach(pso);

            // MSAA の深度から作る版 (作れなくても、MSAA の時に先に深度を解決するだけ)
            Shader::CompiledShader csMs;
            if (_shaderCompiler->Compile(HIZ_SHADER_PATH, "CSDownsampleMS", Shader::Stage::Compute, csMs))
            {
                psoDesc.pRootSignature = _hiZRootSignature->Get();
                psoDesc.CS             = csMs.View();
                hash = fnv1a(&rootSignatureHash, sizeof(rootSignatureHash));
                hash = fnv1a(csMs.Bytecode.data(), csMs.Bytecode.size(), hash);
                swprintf_s(psoName, L"HiZMS_%016llx", hash);
                Microsoft::WRL::ComPtr<ID3D12PipelineState> psoMs;
                if (SUCCEEDED(_pipelineCache->GetOrCreateComputePipeline(psoName, psoDesc, psoMs)))
                {
                    _hiZPipelineMs = std::make_unique<DirectX12::PipelineState>();
                    _hiZPipelineMs->Attach(psoMs);
                }
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
