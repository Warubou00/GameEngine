
/***********************************************************************************
* @file    dx12_commandQueue.h
* @brief   DX12のコマンドリストキュー
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
#ifndef DX12_COMMAND_QUEUE_H_
#define DX12_COMMAND_QUEUE_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "../dx12_header.h"
#include "../../API/ICommandQueue.h"

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
		class IDevice;
		class ICommandList;
	}

	namespace DirectX12
	{
		class Device;
		class GraphicsCommandList;
	
		class CommandQueue : public APIInterface::ICommandQueue
		{
		public:
			CommandQueue();
			~CommandQueue()override;


			/***********************************************************************************
			 * @brief コマンドキューの作成
			 * @details core explanation
			 *
			 * @param[in] ID3D12Device* device デバイス
			 * @return HRESULT 成功の有無
			 ************************************************************************************/
			RHI_API_RESULT Initialize(RHI::APIInterface::IDevice* iDevice)override;

			/***********************************************************************************
			 * @brief コマンドキューの取得
			 *
			 * @return ID3D12CommandQueue* コマンドキューのポインタ
			 ************************************************************************************/
			ID3D12CommandQueue* Get(void);

			void End(RHI::APIInterface::ICommandList* iCmdList)override;

			/***********************************************************************************
			 * @brief 解放関数
			 * @details ComPtrは解放しない
			 *
			 ************************************************************************************/
			void Release(void);

		private:
			Microsoft::WRL::ComPtr<ID3D12CommandQueue> _pCmdQueue = nullptr;
		};
	}
}

#endif // DX12_COMMAND_QUEUE_H_

/************************************
* End of file
************************************/