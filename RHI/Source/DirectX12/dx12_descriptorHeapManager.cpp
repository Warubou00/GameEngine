
/***********************************************************************************
* @file    dx12_descriptorHeapManager.cpp
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
#include "Object/dx12_descriptorHeap.h"

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
        DescriptorHeapManager::DescriptorHeapManager() = default;
        DescriptorHeapManager::~DescriptorHeapManager() = default;

        HRESULT DescriptorHeapManager::Create(ID3D12Device* device)
        {
            if (!device || _srvUavCbvHeap)
            {
                return E_INVALIDARG;
            }

            // シェーダーから見えるヒープ (CBV/SRV/UAV)
            _srvUavCbvHeap = std::make_unique<DescriptorHeap>();
            RHI_RETURN_IF_FAILED(_srvUavCbvHeap->Create(device, D3D12_DESCRIPTOR_HEAP_TYPE_CBV_SRV_UAV, MAX_SRV_COUNT, true));

            _currentSrvIndex = 0;
            _freeSrvIndices.clear();
            return S_OK;
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

            if (_currentSrvIndex >= MAX_SRV_COUNT)
            {// 許容オーバー
                return INVALID_INDEX;
            }
            // なければ新しい番号を発行
            return _currentSrvIndex++;
        }

        void DescriptorHeapManager::FreeSrv(uint32_t index)
        {
            // 無効な番号や未発行の番号を空きリストに入れると、後で同じ枠を二重に使ってしまう
            if (index == INVALID_INDEX || index >= _currentSrvIndex) return;
            _freeSrvIndices.push_back(index);
        }

        D3D12_CPU_DESCRIPTOR_HANDLE DescriptorHeapManager::GetSrvCpuHandle(UINT index)const
        {
            return _srvUavCbvHeap ? _srvUavCbvHeap->GetCpuHandle(index) : D3D12_CPU_DESCRIPTOR_HANDLE{};
        }

        D3D12_GPU_DESCRIPTOR_HANDLE DescriptorHeapManager::GetSrvGpuHandle(UINT index)const
        {
            return _srvUavCbvHeap ? _srvUavCbvHeap->GetGpuHandle(index) : D3D12_GPU_DESCRIPTOR_HANDLE{};
        }

        ID3D12DescriptorHeap* DescriptorHeapManager::GetSrvHeap()const
        {
            return _srvUavCbvHeap ? _srvUavCbvHeap->Get() : nullptr;
        }
	}
}

/************************************
* End of file
************************************/
