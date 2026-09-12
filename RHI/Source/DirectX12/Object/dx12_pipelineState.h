
/***********************************************************************************
* @file    dx12_pipelineState.h
* @brief   DX12のPSO
* @details
*
* @author  Keigo Onari
* @date    2026/02/05
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note
************************************************************************************/

#pragma once
#ifndef DX12_PIPELINE_STATE_H_
#define DX12_PIPELINE_STATE_H_

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
		class PipelineState
		{
		public:
			PipelineState();
			~PipelineState();


			/***********************************************************************************
			 * @brief 生成関数
			 *
			 * @param[in] ID3D12Device* device デバイスのポインタ
			 * @return HRESULT 成功の有無
			 ************************************************************************************/
			HRESULT Create(ID3D12Device* device);

			/***********************************************************************************
			 * @briefe 解放関数
			 *
			 ************************************************************************************/
			void Release(void);

		private:
			Microsoft::WRL::ComPtr<ID3D12PipelineState> _pPSO = nullptr;
			D3D12_GRAPHICS_PIPELINE_STATE_DESC _psoDesc = {};
		};
	}
}

#endif // DX12_PIPELINE_STATE_H_