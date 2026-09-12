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
		// デバッグレイヤーの有効化
		void EnableDebugLayer() {
			ID3D12Debug* debugLayer = nullptr;
			if (SUCCEEDED(D3D12GetDebugInterface(IID_PPV_ARGS(&debugLayer)))) {
				debugLayer->EnableDebugLayer();
				debugLayer->Release();
			}
		}
	}
}

#endif // DX12_DEBuG_LAYER_H_