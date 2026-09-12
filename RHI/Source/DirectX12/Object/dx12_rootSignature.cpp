
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
            if (!device)
            {
                return E_INVALIDARG;
            }

            ID3DBlob* rootSigBlob;
            ID3DBlob* errorBlob;

            // シリアライズ化
            HRESULT hr = D3D12SerializeRootSignature(&rootSignatureDesc, D3D_ROOT_SIGNATURE_VERSION_1_0, &rootSigBlob, &errorBlob);
            if (FAILED(hr))
            {
                return hr;
            }

            // ルートシグネチャの生成
            hr = device->CreateRootSignature(0, rootSigBlob->GetBufferPointer(), rootSigBlob->GetBufferSize(), IID_PPV_ARGS(&_rootSignature));
            return hr;
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
