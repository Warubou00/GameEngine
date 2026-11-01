
/***********************************************************************************
* @file    dx12_rootSignature.cpp
* @brief   DX12ルートシグネチャ
* @details 
*
* @author  Keigo Onari
* @date    2026/04/22
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
#include "dx12_rootSignature.h"

#include <vector>

namespace
{
    // FNV-1a (64bit) : シリアライズしたルートシグネチャのハッシュ
    uint64_t hashBlob(ID3DBlob* blob)
    {
        uint64_t hash = 14695981039346656037ull;
        if (!blob) return hash;
        const uint8_t* p = static_cast<const uint8_t*>(blob->GetBufferPointer());
        for (SIZE_T i = 0; i < blob->GetBufferSize(); ++i) { hash ^= p[i]; hash *= 1099511628211ull; }
        return hash;
    }
}

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
        RootSignature::RootSignature() {}
        RootSignature::~RootSignature() {}

        HRESULT RootSignature::Create(ID3D12Device* device, const D3D12_ROOT_SIGNATURE_DESC& rootSignatureDesc)
        {
            if (!device || _rootSignature)
            {
                return E_INVALIDARG;
            }

            // 以前は生ポインタで受けて解放しておらずリークしていた (errorBlobは未初期化)
            Microsoft::WRL::ComPtr<ID3DBlob> rootSigBlob;
            Microsoft::WRL::ComPtr<ID3DBlob> errorBlob;

            // シリアライズ化
            HRESULT hr = D3D12SerializeRootSignature(&rootSignatureDesc, D3D_ROOT_SIGNATURE_VERSION_1_0, &rootSigBlob, &errorBlob);
            if (FAILED(hr))
            {
                // 失敗理由はerrorBlobに文字列で入っている
                if (errorBlob)
                {
                    OutputDebugStringA(static_cast<const char*>(errorBlob->GetBufferPointer()));
                }
                return hr;
            }

            // ルートシグネチャの生成
            _hash = hashBlob(rootSigBlob.Get());
            return device->CreateRootSignature(0, rootSigBlob->GetBufferPointer(), rootSigBlob->GetBufferSize(), IID_PPV_ARGS(&_rootSignature));
        }

        bool RootSignature::IsVersion11Supported(ID3D12Device* device)
        {
            if (!device) return false;
            D3D12_FEATURE_DATA_ROOT_SIGNATURE feature = { D3D_ROOT_SIGNATURE_VERSION_1_1 };
            return SUCCEEDED(device->CheckFeatureSupport(D3D12_FEATURE_ROOT_SIGNATURE, &feature, sizeof(feature))) &&
                   feature.HighestVersion >= D3D_ROOT_SIGNATURE_VERSION_1_1;
        }

        HRESULT RootSignature::Create(ID3D12Device* device, const D3D12_VERSIONED_ROOT_SIGNATURE_DESC& rootSignatureDesc)
        {
            if (!device || _rootSignature)
            {
                return E_INVALIDARG;
            }

            // 1.1 に対応していない環境では 1.0 に変換する (フラグを捨てるだけ。構造はほぼ同じ)
            if (rootSignatureDesc.Version == D3D_ROOT_SIGNATURE_VERSION_1_1 && !IsVersion11Supported(device))
            {
                const auto& src = rootSignatureDesc.Desc_1_1;
                std::vector<D3D12_ROOT_PARAMETER>               params(src.NumParameters);
                std::vector<std::vector<D3D12_DESCRIPTOR_RANGE>> ranges(src.NumParameters);
                for (UINT i = 0; i < src.NumParameters; ++i)
                {
                    const auto& p = src.pParameters[i];
                    params[i].ParameterType    = p.ParameterType;
                    params[i].ShaderVisibility = p.ShaderVisibility;
                    switch (p.ParameterType)
                    {
                    case D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE:
                        for (UINT r = 0; r < p.DescriptorTable.NumDescriptorRanges; ++r)
                        {
                            const auto& sr = p.DescriptorTable.pDescriptorRanges[r];
                            ranges[i].push_back({ sr.RangeType, sr.NumDescriptors, sr.BaseShaderRegister, sr.RegisterSpace, sr.OffsetInDescriptorsFromTableStart });
                        }
                        params[i].DescriptorTable.NumDescriptorRanges = static_cast<UINT>(ranges[i].size());
                        params[i].DescriptorTable.pDescriptorRanges   = ranges[i].data();
                        break;
                    case D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS:
                        params[i].Constants = p.Constants;
                        break;
                    default:   // CBV / SRV / UAV
                        params[i].Descriptor.ShaderRegister = p.Descriptor.ShaderRegister;
                        params[i].Descriptor.RegisterSpace  = p.Descriptor.RegisterSpace;
                        break;
                    }
                }

                D3D12_ROOT_SIGNATURE_DESC desc10 = {};
                desc10.NumParameters     = src.NumParameters;
                desc10.pParameters       = params.data();
                desc10.NumStaticSamplers = src.NumStaticSamplers;
                desc10.pStaticSamplers   = src.pStaticSamplers;
                desc10.Flags             = src.Flags;
                return Create(device, desc10);
            }

            Microsoft::WRL::ComPtr<ID3DBlob> rootSigBlob;
            Microsoft::WRL::ComPtr<ID3DBlob> errorBlob;
            HRESULT hr = D3D12SerializeVersionedRootSignature(&rootSignatureDesc, &rootSigBlob, &errorBlob);
            if (FAILED(hr))
            {
                if (errorBlob)
                {
                    OutputDebugStringA(static_cast<const char*>(errorBlob->GetBufferPointer()));
                }
                return hr;
            }
            _hash = hashBlob(rootSigBlob.Get());
            return device->CreateRootSignature(0, rootSigBlob->GetBufferPointer(), rootSigBlob->GetBufferSize(), IID_PPV_ARGS(&_rootSignature));
        }

        ID3D12RootSignature* RootSignature::Get()const
        {
            return _rootSignature.Get();
        }

        void RootSignature::Release(void)
        {
            _rootSignature.Reset();
        }
	}
}

/************************************
* End of file
************************************/
