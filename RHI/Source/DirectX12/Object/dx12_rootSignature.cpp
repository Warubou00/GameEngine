
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

// API DirectX12オブジェクトをインクルード
// ========================================
#include "dx12_device.h"

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

        RHI_API_RESULT RootSignature::Initialize(RHI::APIInterface::IDevice* iDevice)
        {
            if (!iDevice)
            {
                return RHI_API_FAILED_INVALID_ARGUMENT;
            }

            auto device = static_cast<RHI::DirectX12::Device*>(iDevice);

            if (!device)
            {
                return RHI_API_FAILED_INVALID_ARGUMENT;
            }

            // ==============================================================
            // 1. ルートシグネチャの構築 (dx12_rootSignature.cpp を使用)
            // ==============================================================

            // SRV(メガVB)用のディスクリプタレンジ
            D3D12_DESCRIPTOR_RANGE srvRange = {};
            srvRange.RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
            srvRange.NumDescriptors = 1;
            srvRange.BaseShaderRegister = 0; // t0
            srvRange.RegisterSpace = 0;
            srvRange.OffsetInDescriptorsFromTableStart = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND;

            // パラメータの設定 (計4つ)
            D3D12_ROOT_PARAMETER rootParams[4] = {};

            // [0]: メッシュの頂点オフセット (b0) - 32Bit Constants
            rootParams[0].ParameterType = D3D12_ROOT_PARAMETER_TYPE_32BIT_CONSTANTS;
            rootParams[0].Constants.ShaderRegister = 0;
            rootParams[0].Constants.Num32BitValues = 1;
            rootParams[0].ShaderVisibility = D3D12_SHADER_VISIBILITY_VERTEX;

            // [1]: メガVBのSRV (t0) - Descriptor Table
            rootParams[1].ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
            rootParams[1].DescriptorTable.NumDescriptorRanges = 1;
            rootParams[1].DescriptorTable.pDescriptorRanges = &srvRange;
            rootParams[1].ShaderVisibility = D3D12_SHADER_VISIBILITY_VERTEX;

            // [2]: Global定数バッファ (b1) - Root CBV
            rootParams[2].ParameterType = D3D12_ROOT_PARAMETER_TYPE_CBV;
            rootParams[2].Descriptor.ShaderRegister = 1;
            rootParams[2].Descriptor.RegisterSpace = 0;
            rootParams[2].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

            // [3]: Scene(Object)定数バッファ (b2) - Root CBV
            rootParams[3].ParameterType = D3D12_ROOT_PARAMETER_TYPE_CBV;
            rootParams[3].Descriptor.ShaderRegister = 2;
            rootParams[3].Descriptor.RegisterSpace = 0;
            rootParams[3].ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;

            D3D12_ROOT_SIGNATURE_DESC rootSigDesc = {};
            rootSigDesc.NumParameters = 4;
            rootSigDesc.pParameters = rootParams;
            rootSigDesc.NumStaticSamplers = 0;
            rootSigDesc.pStaticSamplers = nullptr;
            rootSigDesc.Flags = D3D12_ROOT_SIGNATURE_FLAG_NONE; // IAを使わないためフラグ無し

            ID3DBlob* rootSigBlob;
            ID3DBlob* errorBlob;

            // シリアライズ化
            HRESULT hr = D3D12SerializeRootSignature(&rootSigDesc, D3D_ROOT_SIGNATURE_VERSION_1_0, &rootSigBlob, &errorBlob);
            if (FAILED(hr))
            {
                return RHI_API_FAILED_INITIALIZED;
            }

            // ルートシグネチャの生成
            hr = device->GetDevice()->CreateRootSignature(0, rootSigBlob->GetBufferPointer(), rootSigBlob->GetBufferSize(), IID_PPV_ARGS(&_rootSignature));

            if (FAILED(hr))
            {
                return RHI_API_FAILED_INITIALIZED;
            }
            return RHI_API_SUCCEEDED;
        }

        ID3D12RootSignature* RootSignature::Get()const
        {
            return _rootSignature.Get();
        }
	}
}

/************************************
* End of file
************************************/
