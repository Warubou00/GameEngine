
/***********************************************************************************
* @file    dx12_descriptorHeap.cpp
* @brief   DX12のディスクリプタヒープ
* @details
*
* @author  Keigo Onari
* @date    2026/11/01
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
#include "dx12_descriptorHeap.h"

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
		DescriptorHeap::DescriptorHeap() = default;
		DescriptorHeap::~DescriptorHeap() = default;

		HRESULT DescriptorHeap::Create(ID3D12Device* device, D3D12_DESCRIPTOR_HEAP_TYPE type, UINT count, bool shaderVisible)
		{
			if (!device || count == 0 || _heap)
			{
				return E_INVALIDARG;
			}

			// RTV / DSV はシェーダーから見えるヒープにできない
			if (type == D3D12_DESCRIPTOR_HEAP_TYPE_RTV || type == D3D12_DESCRIPTOR_HEAP_TYPE_DSV)
			{
				shaderVisible = false;
			}

			D3D12_DESCRIPTOR_HEAP_DESC desc = {};
			desc.Type           = type;
			desc.NumDescriptors = count;
			desc.Flags          = shaderVisible ? D3D12_DESCRIPTOR_HEAP_FLAG_SHADER_VISIBLE : D3D12_DESCRIPTOR_HEAP_FLAG_NONE;
			desc.NodeMask       = 0;
			RHI_RETURN_IF_FAILED(device->CreateDescriptorHeap(&desc, IID_PPV_ARGS(&_heap)));

			_count          = count;
			_descriptorSize = device->GetDescriptorHandleIncrementSize(type);
			_cpuStart       = _heap->GetCPUDescriptorHandleForHeapStart();
			if (shaderVisible)
			{
				// シェーダーから見えないヒープで GetGPUDescriptorHandleForHeapStart を呼ぶとデバッグレイヤーのエラーになる
				_gpuStart = _heap->GetGPUDescriptorHandleForHeapStart();
			}
			return S_OK;
		}

		D3D12_CPU_DESCRIPTOR_HANDLE DescriptorHeap::GetCpuHandle(UINT index)const
		{
			D3D12_CPU_DESCRIPTOR_HANDLE handle = {};
			if (index < _count)
			{
				handle.ptr = _cpuStart.ptr + static_cast<SIZE_T>(index) * _descriptorSize;
			}
			return handle;
		}

		D3D12_GPU_DESCRIPTOR_HANDLE DescriptorHeap::GetGpuHandle(UINT index)const
		{
			D3D12_GPU_DESCRIPTOR_HANDLE handle = {};
			if (index < _count && _gpuStart.ptr != 0)
			{
				handle.ptr = _gpuStart.ptr + static_cast<UINT64>(index) * _descriptorSize;
			}
			return handle;
		}

		void DescriptorHeap::Release(void)
		{
			_heap.Reset();
			_cpuStart       = {};
			_gpuStart       = {};
			_descriptorSize = 0;
			_count          = 0;
		}
	}
}

/************************************
* End of file
************************************/
