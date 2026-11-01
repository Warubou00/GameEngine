
/***********************************************************************************
* @file    dx12_descriptorHeap.h
* @brief   DX12のディスクリプタヒープ
* @details 番号からハンドルを計算するだけの薄いラッパー。RTV / DSV のような小さいヒープに使う
*          (SRV の大きなヒープと空き管理は dx12_descriptorHeapManager)
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

#pragma once
#ifndef DX12_DESCRIPTOR_HEAP_H_WRAPPER_
#define DX12_DESCRIPTOR_HEAP_H_WRAPPER_

/******************************************************************
* Include Headers
******************************************************************/
#include "../dx12_header.h"

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
		class DescriptorHeap
		{
		public:
			DescriptorHeap();
			~DescriptorHeap();

			/***********************************************************************************
			 * @brief 生成関数
			 *
			 * @param[in] ID3D12Device* device デバイス
			 * @param[in] D3D12_DESCRIPTOR_HEAP_TYPE type 種類 (RTV / DSV / CBV_SRV_UAV / SAMPLER)
			 * @param[in] UINT count ディスクリプタの数
			 * @param[in] bool shaderVisible シェーダーから見えるか (RTV / DSV は false 固定)
			 * @return HRESULT 成功の有無
			 ************************************************************************************/
			HRESULT Create(ID3D12Device* device, D3D12_DESCRIPTOR_HEAP_TYPE type, UINT count, bool shaderVisible = false);

			ID3D12DescriptorHeap* Get(void)const { return _heap.Get(); }
			UINT GetCount(void)const { return _count; }

			// 指定番号のハンドル (範囲外は 0 を返す)
			D3D12_CPU_DESCRIPTOR_HANDLE GetCpuHandle(UINT index)const;
			D3D12_GPU_DESCRIPTOR_HANDLE GetGpuHandle(UINT index)const;

			void Release(void);

		private:
			Microsoft::WRL::ComPtr<ID3D12DescriptorHeap> _heap;
			// 先頭ハンドルとサイズは毎回取得すると遅いので生成時に覚えておく
			D3D12_CPU_DESCRIPTOR_HANDLE _cpuStart       = {};
			D3D12_GPU_DESCRIPTOR_HANDLE _gpuStart       = {};
			UINT                        _descriptorSize = 0;
			UINT                        _count          = 0;
		};
	}
}

#endif // DX12_DESCRIPTOR_HEAP_H_WRAPPER_
