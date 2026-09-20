
/***********************************************************************************
* @file    dx12_resourceBuffer.cpp
* @brief   DX12のリソースバッファー
* @details
*
* @author  Keigo Onari
* @date    2026/09/15
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
#include "dx12_resourceBuffer.h"

// API DirectX12オブジェクトをインクルード
// ========================================
#include "dx12_device.h"
#include "dx12_swapChain.h"
#include "../dx12_descriptorHeapManager.h"

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
        std::vector<Microsoft::WRL::ComPtr<ID3D12Resource>> _rtvResources = std::vector<Microsoft::WRL::ComPtr<ID3D12Resource>>();
		ResourceBuffer::ResourceBuffer() : _capacity(0ull), _currentOffset(0ull) {}
		ResourceBuffer::~ResourceBuffer() {}

		// ==============================================
		// コマンドアロケータの作成
		// ==============================================
		HRESULT ResourceBuffer::Create(RHI::DirectX12::Device* device, UINT64 sizeInBytes, D3D12_HEAP_TYPE heapType)
		{
			if (!device)
			{
				return E_NOTIMPL;
			}

            _currentOffset = 0;

            D3D12_HEAP_PROPERTIES heapProps = {};
            heapProps.Type = heapType;
            heapProps.CPUPageProperty       = D3D12_CPU_PAGE_PROPERTY_UNKNOWN;
            heapProps.MemoryPoolPreference  = D3D12_MEMORY_POOL_UNKNOWN;
            heapProps.CreationNodeMask      = 1;
            heapProps.VisibleNodeMask       = 1;

            D3D12_RESOURCE_DESC desc        = {};
            desc.Dimension                  = D3D12_RESOURCE_DIMENSION_BUFFER;
            desc.Alignment                  = 0;
            desc.Width                      = sizeInBytes;
            desc.Height                     = 1;
            desc.DepthOrArraySize           = 1;
            desc.MipLevels                  = 1;
            desc.Format                     = DXGI_FORMAT_UNKNOWN;
            desc.SampleDesc.Count           = 1;
            desc.SampleDesc.Quality         = 0;
            desc.Layout                     = D3D12_TEXTURE_LAYOUT_ROW_MAJOR;
            desc.Flags                      = D3D12_RESOURCE_FLAG_NONE;

            // UPLOADヒープ(定数バッファ等)ならGENERIC_READ、DEFAULT(頂点等)ならCOMMONを初期状態にする
            D3D12_RESOURCE_STATES initialState = (heapType == D3D12_HEAP_TYPE_UPLOAD)
                ? D3D12_RESOURCE_STATE_GENERIC_READ
                : D3D12_RESOURCE_STATE_COMMON;

            return device->GetDevice()->CreateCommittedResource(
                &heapProps,
                D3D12_HEAP_FLAG_NONE,
                &desc,
                initialState,
                nullptr,
                IID_PPV_ARGS(&_resourceBuffer)
            );
		}

        UINT64 ResourceBuffer::Allocate(UINT64 sizeInBytes, UINT64 alignment)
        {
            UINT64 alignedOffset = alignUp(_currentOffset, alignment);

            // 容量オーバーチェック
            if (alignedOffset + sizeInBytes > _capacity) {
                return (UINT64)-1; // エラー値として -1 を返す
            }

            // オフセットを進める
            _currentOffset = alignedOffset + sizeInBytes;
            return alignedOffset;
        }

        void ResourceBuffer::CreateView(
            RHI::DirectX12::Device* device,
            const D3D12_SHADER_RESOURCE_VIEW_DESC& viewDesc,
            const D3D12_CPU_DESCRIPTOR_HANDLE& handle
        )
        {
            device->GetDevice()->CreateShaderResourceView(_resourceBuffer.Get(), &viewDesc, handle);
        }

        void ResourceBuffer::CreateView(
            RHI::DirectX12::Device* device,
            const D3D12_CONSTANT_BUFFER_VIEW_DESC& viewDesc,
            const D3D12_CPU_DESCRIPTOR_HANDLE& handle
        )
        {
            device->GetDevice()->CreateConstantBufferView(&viewDesc, handle);
        }

        HRESULT ResourceBuffer::CreateRtv(
            RHI::DirectX12::Device* device,
            RHI::DirectX12::SwapChain* swapChain,
            RHI::DirectX12::DescriptorHeapManager* descHeapManager
        )
        {
            // RTVのハンドル取得
            auto rtvHandle = descHeapManager->GetRtvCpuHandleStart();

            // SwapChainDescを取得
            DXGI_SWAP_CHAIN_DESC1 scDesc = {};
            auto hr = swapChain->Get()->GetDesc1(&scDesc);

            // rtvの作成
            ID3D12Resource* resource = nullptr;
            for (int i = 0; i < scDesc.BufferCount; i++)
            {
                swapChain->Get()->GetBuffer(i, IID_PPV_ARGS(&resource));

                device->GetDevice()->CreateRenderTargetView(resource, nullptr, rtvHandle);

                // サイズ分進める
                rtvHandle.ptr += descHeapManager->GetRtvDescriptorSize();
            }
        }

        UINT64 ResourceBuffer::alignUp(UINT64 size, UINT64 alignment)
        {
            return (size + alignment - 1) & ~(alignment - 1);
        }
	}
}

/************************************
* End of file
************************************/