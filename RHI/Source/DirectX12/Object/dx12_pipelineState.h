
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
#include "../../API/IPipeline.h"

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
		class Device;
		class RootSignature;

		class PipelineState : public APIInterface::IPipeline
		{
		public:
			PipelineState();
			~PipelineState()override;

			/***********************************************************************************
			 * @brief 生成関数
			 *
			 * @param[in] RHI::DirectX12::Device* device デバイスのポインタ
			 * @param[in] RHI::DirectX12::RootSignature* rootSignature ルートシグネチャのポインタ
			 * @return HRESULT 成功の有無
			 ************************************************************************************/
			RHI_API_RESULT Initialize(RHI::APIInterface::IDevice* iDevice, RHI::APIInterface::IResourceBinding* iResourceBinding)override;

			ID3D12PipelineState* Get()const;

			/***********************************************************************************
			 * @briefe 解放関数
			 *
			 ************************************************************************************/
			void Release(void);

		private:
			Microsoft::WRL::ComPtr<ID3D12PipelineState> _pPSO = nullptr;
		};
	}
}

#endif // DX12_PIPELINE_STATE_H_