
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
		ResourceBuffer::ResourceBuffer() : _capacity(0ull), _currentOffset(0ull) {}
		ResourceBuffer::~ResourceBuffer() {}

		// ==============================================
		// コマンドアロケータの作成
		// ==============================================
		HRESULT ResourceBuffer::Create(ID3D12Device* device, UINT64 sizeInBytes, D3D12_HEAP_TYPE heapType)
		{
			if (!device)
			{
				// TODO : 例外処理 -> nullptr
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

            return device->CreateCommittedResource(
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

        UINT64 ResourceBuffer::alignUp(UINT64 size, UINT64 alignment)
        {
            return (size + alignment - 1) & ~(alignment - 1);
        }
	}
}

/************************************
* End of file
************************************/