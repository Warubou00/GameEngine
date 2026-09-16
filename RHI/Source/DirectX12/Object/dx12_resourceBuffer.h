
/***********************************************************************************
* @file    dx12_resourceBuffer.h
* @brief   リソースバッファー
* @details
*
* @author  Keigo Onari
* @date    2026/02/02
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note
************************************************************************************/

#pragma once
#ifndef DX12_RESOURCE_BUFFER_H_
#define DX12_RESOURCE_BUFFER_H_

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
		class ResourceBuffer
		{
		public:
			ResourceBuffer();
			~ResourceBuffer();


			/***********************************************************************************
			 * @brief バッファーの生成
			 * @details core explanation
			 *
			 * @param[in] ID3D12Device* デバイス
			 * @param[in] UINT64 バッファーサイズ
			 * @param[in] D3D12_HEAP_TYPE ヒープのタイプ
			 * @return HRESULT 成功の有無
			 ************************************************************************************/
			HRESULT Create(ID3D12Device* device, UINT64 sizeInBytes, D3D12_HEAP_TYPE heapType);

			/***********************************************************************************
			 * @brief メモリの割り当て
			 * @details core explanation
			 *
			 * @param[in] UINT64 sizeInBytes バッファーサイズ
			 * @param[in] UINT64 allignment バッファのアライメント 頂点バッファなら4byte, 定数バッファは256byte指定
			 * @return UINT64 メモリオフセット
			 ************************************************************************************/
			UINT64 Allocate(UINT64 sizeInBytes, UINT64 alignment);

			ID3D12Resource* Get() { return _resourceBuffer.Get(); }

		private:
			Microsoft::WRL::ComPtr<ID3D12Resource> _resourceBuffer;
			UINT64 _capacity;
			UINT64 _currentOffset;

			//メモリの開始位置を特定のアライメント(倍数)に切り上げる便利関数
			UINT64 alignUp(UINT64 size, UINT64 alignment);
		};
	}
}

#endif // DX12_RESOURCE_BUFFER_H_

/************************************
* End of file
************************************/