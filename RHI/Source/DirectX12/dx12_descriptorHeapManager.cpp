
/***********************************************************************************
* @file    dx12_descriptorHeap.cpp
* @brief   DX12のデスクリプタヒープ一括管理
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
#include "dx12_descriptorHeapManager.h"

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
        HRESULT DescriptorHeapManager::Create(ID3D12Device* device)
        {
            if (!device)
            {
                // TODO : 例外処理 -> nullptr
                return E_INVALIDARG;
            }

            D3D12_DESCRIPTOR_HEAP_DESC descHeapDesc = {};
            descHeapDesc.NumDescriptors = 2;
            descHeapDesc.Flags          = D3D12_DESCRIPTOR_HEAP_FLAG_NONE;                      // シェーダーから隠す
            descHeapDesc.Type           = D3D12_DESCRIPTOR_HEAP_TYPE_RTV;                       // RTV
            descHeapDesc.NodeMask       = 0;
            HRESULT hr = device->CreateDescriptorHeap(&descHeapDesc, IID_PPV_ARGS(&_rtvHeap));
            if (FAILED(hr))return hr;

            _rtvDescriptorSize = device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_RTV);

            descHeapDesc.NumDescriptors = _maxSrvCount;                                         // 最大ビュー数
            descHeapDesc.Flags          = D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE;            // シェーダーから見えるように
            descHeapDesc.Type           = D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV;               // CBV/SRV/UAV
            hr = device ->CreateDescriptorHeap(&descHeapDesc, IID_PPV_ARGS(&_srvUavCbvHeap));   // 生成
            if (FAILED(hr))return hr;

            _srvDescriptorSize = device->GetDescriptorHandleIncrementSize(D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV);

            descHeapDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_SAMPLER;
            descHeapDesc.Type = D3D12_DESCRIPTOR_HEAP_TYPE_DSV;

            return hr;
        }
        
		uint32_t DescriptorHeapManager::AllocateSrv()
        {
            if (!_freeSrvIndices.empty())
            {
                // 再利用可能な空きがあればそれを返す
                uint32_t index = _freeSrvIndices.back();
                _freeSrvIndices.pop_back();
                return index;
            }
            else 
            {
                if (_currentSrvIndex > _maxSrvCount)
                {// 許容オーバー
                    MessageBox(NULL, "SRV / CBV / DSV Heap is full!", "SRV / CBV / DSV Heap is full!", MB_OK);
                    return -1;
                }
                // なければ新しい番号を発行
                return _currentSrvIndex++;
            }
        }

        void DescriptorHeapManager::FreeSrv(uint32_t index) 
        {
            _freeSrvIndices.push_back(index);
        }

        D3D12_CPU_DESCRIPTOR_HANDLE DescriptorHeapManager::GetSrvCpuHandle(uint32_t index)
        {
            D3D12_CPU_DESCRIPTOR_HANDLE handle = _srvUavCbvHeap->GetCPUDescriptorHandleForHeapStart();
            handle.ptr += static_cast<SIZE_T>(index) * _srvDescriptorSize;
            return handle;
        }
	}
}

/************************************
* End of file
************************************/
