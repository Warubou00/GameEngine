
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
#include <utility>     // std::move

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
			 * @details 以前はコンストラクタで受け取ったDescのポインタを保持していたが、
			 *          呼び出し元のスタック変数を指していたため Create 時に直接受け取るようにした
			 *
			 * @param[in] ID3D12Device* device デバイスのポインタ
			 * @param[in] const D3D12_GRAPHICS_PIPELINE_STATE_DESC& psoDesc PSOの設定
			 * @return HRESULT 成功の有無
			 ************************************************************************************/
			HRESULT Create(ID3D12Device* device, const D3D12_GRAPHICS_PIPELINE_STATE_DESC& psoDesc);

			ID3D12PipelineState* Get()const;

			// 作成済みの PSO を持たせる (PSO キャッシュから読み込んだ時など)
			void Attach(Microsoft::WRL::ComPtr<ID3D12PipelineState> pso) { _pPSO = std::move(pso); }

			/***********************************************************************************
			 * @brief 解放関数
			 *
			 ************************************************************************************/
			void Release(void);

		private:
			Microsoft::WRL::ComPtr<ID3D12PipelineState> _pPSO = nullptr;
		};
	}
}

#endif // DX12_PIPELINE_STATE_H_
