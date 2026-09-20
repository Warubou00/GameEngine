
/***********************************************************************************
* @file    dx12_fence.h
* @brief   DX12のフェンス
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

#pragma once
#ifndef DX12_FENCE_H_
#define DX12_FENCE_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "../dx12_header.h"
#include "../../API/IFence.h"

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
	namespace APIInterface
	{
		class ICommandQueue;
		class IDevice;
		class ISwapChain;
	}

	namespace DirectX12
	{

		class Fence : public APIInterface::IFence
		{
		public:
			Fence();
			~Fence()override;

			void WaitForSignal(RHI::APIInterface::ICommandQueue* iCommandList)override;

			/***********************************************************************************
			 * @brief 生成関数
			 *
			 * @param[in] RHI::APIInterface::IDevice* iDevice デバイスのポインタ
			 * @param[in] RHI::APIInterface::ISwapChain* iSwapChain スワップチェーンのポインタ
			 * @return HRESULT 成功の有無
			 ************************************************************************************/
			 RHI_API_RESULT Initialize(RHI::APIInterface::IDevice* iDevice, RHI::APIInterface::ISwapChain* iSwapChain)override;


			/***********************************************************************************
			 * @brief 取得関数
			 *
			 * @param[in] value param explanation
			 * @return ID3D12Fence* フェンスへのポインタ
			 ************************************************************************************/
			ID3D12Fence* Get(void)const;


			/***********************************************************************************
			 * @briefe 解放関数
			 *
			 ************************************************************************************/
			void Release(void);

		private:
			Microsoft::WRL::ComPtr<ID3D12Fence> _pFence = nullptr;
			UINT64 _fenceVal         = 0;
			UINT64 _frameBufferIndex = 0;
			void*  _fenceEvent;
		};
	}
}

#endif // DX12_FENCE_H_