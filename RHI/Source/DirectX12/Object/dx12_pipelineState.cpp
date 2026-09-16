
/***********************************************************************************
* @file    dx12_pipelineState.cpp
* @brief   DX12‚ÌPSO
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
		PipelineState::PipelineState(D3D12_GRAPHICS_PIPELINE_STATE_DESC* pPsoDesc) : _pPsoDesc(pPsoDesc) {}
		PipelineState::~PipelineState() {}

		// ==============================================
		// PSO‚Ìì¬
		// ==============================================
		HRESULT PipelineState::Create(ID3D12Device* device)
		{
			if (!device || !_pPsoDesc)
			{
				return E_NOTIMPL;
			}

			return device->CreateGraphicsPipelineState(_pPsoDesc, IID_PPV_ARGS(&_pPSO));
		}

		// ==============================================
		// ‰ð•ú
		// ==============================================
		void PipelineState::Release(void)
		{
			// ComPtr‚Ì‚½‚ß–¾Ž¦“I‚È‰ð•ú‚Í”ð‚¯‚é
			//if (_pPSO.Get())_pPSO.Get()->Release();
		}
	}
}

/************************************
* End of file
************************************/