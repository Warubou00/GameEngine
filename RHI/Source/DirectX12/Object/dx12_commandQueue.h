
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
		class CommandQueue
		{
		public:
			CommandQueue();
			~CommandQueue();



			/***********************************************************************************
			 * @brief コマンドキューの作成
			 *
			 * @param[in] ID3D12Device* device デバイス
			 * @param[in] D3D12_COMMAND_LIST_TYPE type キューの種類 (DIRECT / COMPUTE / COPY)
			 * @return HRESULT 成功の有無
			 ************************************************************************************/
			HRESULT Create(ID3D12Device* device, D3D12_COMMAND_LIST_TYPE type = D3D12_COMMAND_LIST_TYPE_DIRECT);

			/***********************************************************************************
			 * @brief コマンドキューの取得
			 *
			 * @return ID3D12CommandQueue* コマンドキューのポインタ
			 ************************************************************************************/
			ID3D12CommandQueue* Get(void)const;

			D3D12_COMMAND_LIST_TYPE GetType(void)const { return _type; }

			/***********************************************************************************
			 * @brief 解放関数
			 * @details 保持している参照を手放す
			 *
			 ************************************************************************************/
			void Release(void);

		private:
			Microsoft::WRL::ComPtr<ID3D12CommandQueue> _pCmdQueue = nullptr;
			D3D12_COMMAND_LIST_TYPE                    _type      = D3D12_COMMAND_LIST_TYPE_DIRECT;
		};
	}
}

#endif // DX12_COMMAND_QUEUE_H_

/************************************
* End of file
************************************/