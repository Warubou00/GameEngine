
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

#include <cstring>

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
		ResourceBuffer::ResourceBuffer() = default;
		ResourceBuffer::~ResourceBuffer()
		{
			Release();
		}

		// ==============================================
		// バッファーの作成
		// ==============================================
		HRESULT ResourceBuffer::Create(ID3D12Device* device, UINT64 sizeInBytes, D3D12_HEAP_TYPE heapType)
		{
			if (!device || _resourceBuffer || sizeInBytes == 0)
			{
				return E_INVALIDARG;
			}

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

            // UPLOADヒープはGENERIC_READ固定、READBACKはCOPY_DEST固定、DEFAULTはCOMMONから開始する
            D3D12_RESOURCE_STATES initialState = D3D12_RESOURCE_STATE_COMMON;
            if (heapType == D3D12_HEAP_TYPE_UPLOAD)   initialState = D3D12_RESOURCE_STATE_GENERIC_READ;
            if (heapType == D3D12_HEAP_TYPE_READBACK) initialState = D3D12_RESOURCE_STATE_COPY_DEST;

            RHI_RETURN_IF_FAILED(device->CreateCommittedResource(
                &heapProps,
                D3D12_HEAP_FLAG_NONE,
                &desc,
                initialState,
                nullptr,
                IID_PPV_ARGS(&_resourceBuffer)
            ));

            _capacity = sizeInBytes;
            return S_OK;
		}

        HRESULT ResourceBuffer::CreateTexture(ID3D12Device* device, UINT64 width, UINT height, DXGI_FORMAT format, UINT16 mipLevels, UINT16 arraySize)
        {
            if (!device || _resourceBuffer)
            {
                return E_INVALIDARG;
            }

            // テクスチャは通常VRAM(DEFAULT)に配置します
            D3D12_HEAP_PROPERTIES heapProps = {};
            heapProps.Type                  = D3D12_HEAP_TYPE_DEFAULT;
            heapProps.CPUPageProperty       = D3D12_CPU_PAGE_PROPERTY_UNKNOWN;
            heapProps.MemoryPoolPreference  = D3D12_MEMORY_POOL_UNKNOWN;
            heapProps.CreationNodeMask      = 1;
            heapProps.VisibleNodeMask       = 1;

            D3D12_RESOURCE_DESC desc = {};
            desc.Dimension                  = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
            desc.Alignment                  = 0;
            desc.Width                      = width;
            desc.Height                     = height;
            desc.DepthOrArraySize           = arraySize;
            desc.MipLevels                  = mipLevels;
            desc.Format                     = format;
            desc.SampleDesc.Count           = 1;
            desc.SampleDesc.Quality         = 0;
            desc.Layout                     = D3D12_TEXTURE_LAYOUT_UNKNOWN;
            desc.Flags                      = D3D12_RESOURCE_FLAG_NONE;

            // COMMON で作る
            //   COPY キューでコピーすると COMMON → COPY_DEST に暗黙で昇格し、コピー後は COMMON に戻る。
            //   描画キューで読む時は COMMON → PIXEL_SHADER_RESOURCE に暗黙で昇格するので、バリアは不要
            //   (COPY キューでは PIXEL_SHADER_RESOURCE へのバリアを張れないため、この仕組みを使う)
            RHI_RETURN_IF_FAILED(device->CreateCommittedResource(
                &heapProps,
                D3D12_HEAP_FLAG_NONE,
                &desc,
                D3D12_RESOURCE_STATE_COMMON,
                nullptr,
                IID_PPV_ARGS(&_resourceBuffer)
            ));

            _capacity = 0;
            return S_OK;
        }

        HRESULT ResourceBuffer::CreateDepthStencil(ID3D12Device* device, UINT64 width, UINT height, DXGI_FORMAT format, float clearDepth)
        {
            if (!device || _resourceBuffer || width == 0 || height == 0)
            {
                return E_INVALIDARG;
            }

            D3D12_HEAP_PROPERTIES heapProps = {};
            heapProps.Type                  = D3D12_HEAP_TYPE_DEFAULT;
            heapProps.CPUPageProperty       = D3D12_CPU_PAGE_PROPERTY_UNKNOWN;
            heapProps.MemoryPoolPreference  = D3D12_MEMORY_POOL_UNKNOWN;
            heapProps.CreationNodeMask      = 1;
            heapProps.VisibleNodeMask       = 1;

            D3D12_RESOURCE_DESC desc = {};
            desc.Dimension                  = D3D12_RESOURCE_DIMENSION_TEXTURE2D;
            desc.Alignment                  = 0;
            desc.Width                      = width;
            desc.Height                     = height;
            desc.DepthOrArraySize           = 1;
            desc.MipLevels                  = 1;
            desc.Format                     = format;
            desc.SampleDesc.Count           = 1;
            desc.SampleDesc.Quality         = 0;
            desc.Layout                     = D3D12_TEXTURE_LAYOUT_UNKNOWN;
            // DENY_SHADER_RESOURCE : シェーダーから読まないことを伝えると、ドライバーが深度圧縮を最適化できる
            desc.Flags                      = D3D12_RESOURCE_FLAG_ALLOW_DEPTH_STENCIL | D3D12_RESOURCE_FLAG_DENY_SHADER_RESOURCE;

            // 最適化クリア値 : ClearDepthStencilView で同じ値を使うと高速クリアになる
            D3D12_CLEAR_VALUE clearValue = {};
            clearValue.Format               = format;
            clearValue.DepthStencil.Depth   = clearDepth;
            clearValue.DepthStencil.Stencil = 0;

            RHI_RETURN_IF_FAILED(device->CreateCommittedResource(
                &heapProps,
                D3D12_HEAP_FLAG_NONE,
                &desc,
                D3D12_RESOURCE_STATE_DEPTH_WRITE,
                &clearValue,
                IID_PPV_ARGS(&_resourceBuffer)
            ));

            _capacity = 0;
            return S_OK;
        }

        void ResourceBuffer::Regist(ID3D12Resource* resource)
        {
            // 以前のリソースをMapしたまま差し替えないようにする
            Unmap();
            _resourceBuffer = resource;

            _capacity = 0;
            if (resource)
            {
                const D3D12_RESOURCE_DESC desc = resource->GetDesc();
                if (desc.Dimension == D3D12_RESOURCE_DIMENSION_BUFFER)
                {
                    _capacity = desc.Width;
                }
            }
        }

        D3D12_GPU_VIRTUAL_ADDRESS ResourceBuffer::GetGPUVirtualAddress()const
        {
            return _resourceBuffer ? _resourceBuffer->GetGPUVirtualAddress() : 0;
        }

        HRESULT ResourceBuffer::Map()
        {
            if (!_resourceBuffer)      return E_FAIL;
            if (_mappedData != nullptr) return S_OK;

            // 第2引数の D3D12_RANGE は、CPU側で読み取る範囲を指定するもの。
            // 今回は「CPUは書き込むだけで、読み取りはしない」ので nullptr (全範囲) ではなく、
            // 空のRangeを渡すのがパフォーマンス上最も良いとされています。
            D3D12_RANGE readRange = { 0, 0 };
            HRESULT hr = _resourceBuffer->Map(0, &readRange, &_mappedData);
            if (FAILED(hr))
            {
                _mappedData = nullptr;
            }
            return hr;
        }

        void ResourceBuffer::Unmap()
        {
            if (_resourceBuffer && _mappedData != nullptr)
            {
                _resourceBuffer->Unmap(0, nullptr);
            }
            _mappedData = nullptr;
        }

        // 指定したオフセットにデータを書き込むメソッド
        bool ResourceBuffer::WriteData(UINT64 offset, const void* data, size_t dataSize)
        {
            if (!_mappedData || !data) return false;

            // 範囲外書き込みはヒープ破壊になるので弾く
            if (offset > _capacity || dataSize > _capacity - offset) return false;

            // 取得したポインタの、指定オフセット位置へデータをコピー
            UINT8* dest = static_cast<UINT8*>(_mappedData) + offset;
            std::memcpy(dest, data, dataSize);
            return true;
        }

        void ResourceBuffer::Release(void)
        {
            Unmap();
            _resourceBuffer.Reset();
            _capacity = 0;
        }
	}
}

/************************************
* End of file
************************************/
