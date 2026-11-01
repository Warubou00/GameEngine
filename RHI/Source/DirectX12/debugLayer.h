/***********************************************************************************
* @file    debugLayer.h
* @brief   DX12デバッグレイヤー
* @details
*
* @author  Keigo Onari
* @date    2026/02/02
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    その他
************************************************************************************/

#pragma once
#ifndef DX12_DEBUG_LAYER_H_
#define DX12_DEBUG_LAYER_H_

#include "dx12_header.h"

namespace RHI
{
	namespace DirectX12
	{
		/***********************************************************************************
		 * @brief デバッグレイヤーの有効化
		 * @details デバイス生成より前に呼ぶこと。
		 *          ヘッダー定義のため inline にしておかないと、複数の cpp から include した時点でリンクエラー(ODR違反)になる
		 *
		 * @param[in] bool enableGpuValidation GPUベース検証も有効にするか(重いので既定はOFF)
		 * @return bool 有効化できたか
		 ************************************************************************************/
		inline bool EnableDebugLayer(bool enableGpuValidation = false)
		{
			Microsoft::WRL::ComPtr<ID3D12Debug> debugLayer;
			if (FAILED(D3D12GetDebugInterface(IID_PPV_ARGS(&debugLayer))))
			{
				return false;
			}
			debugLayer->EnableDebugLayer();

			if (enableGpuValidation)
			{
				Microsoft::WRL::ComPtr<ID3D12Debug1> debugLayer1;
				if (SUCCEEDED(debugLayer.As(&debugLayer1)))
				{
					debugLayer1->SetEnableGPUBasedValidation(TRUE);
				}
			}
			return true;
		}
	}
}

#endif // DX12_DEBUG_LAYER_H_
