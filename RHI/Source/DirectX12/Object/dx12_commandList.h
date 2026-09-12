
/***********************************************************************************
* @file    dx12_commandList.h
* @brief   DX12のコマンドリスト
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
#ifndef DX12_COMMAND_LIST_H_
#define DX12_COMMAND_LIST_H_

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
		class CommandList
		{
		public:
			CommandList();
			~CommandList();


			/***********************************************************************************
			 * @brief コマンドリストの作成
			 * @details core explanation
			 *
			 * @param[in] ID3D12Device* device デバイス
			 * @param[in] ID3D12CommandAllocator* commandAllocator コマンドアロケーター
			 * @return HRESULT 成功の有無
			 ************************************************************************************/
			virtual HRESULT Create(ID3D12Device* deviece, ID3D12CommandAllocator* commandAllocator) = 0;

			/***********************************************************************************
			 * @brief 解放関数
			 * @details ComPtrは解放しない
			 *
			 ************************************************************************************/
			virtual void Release(void) = 0;

		private:

		};
	}
}

#endif // DX12_COMMAND_LIST_H_

/************************************
* End of file
************************************/