
/***********************************************************************************
* @file    dx12_pipelineState.cpp
* @brief   DX12のPSO
* @details
*
* @author  Keigo Onari
* @date    2026/02/03
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
#include "dx12_pipelineState.h"

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
		PipelineState::PipelineState() = default;
		PipelineState::~PipelineState() {}

		// ==============================================
		// PSOの作成
		// ==============================================
		HRESULT PipelineState::Create(ID3D12Device* device, const D3D12_GRAPHICS_PIPELINE_STATE_DESC& psoDesc)
		{
			if (!device || _pPSO)
			{
				return E_INVALIDARG;
			}

			return device->CreateGraphicsPipelineState(&psoDesc, IID_PPV_ARGS(&_pPSO));
		}

		ID3D12PipelineState* PipelineState::Get()const
		{
			return _pPSO.Get();
		}

		// ==============================================
		// 解放
		// ==============================================
		void PipelineState::Release(void)
		{
			_pPSO.Reset();
		}
	}
}

/************************************
* End of file
************************************/