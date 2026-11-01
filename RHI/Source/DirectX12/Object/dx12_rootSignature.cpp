
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
