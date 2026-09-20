
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
#include "../../API/ICommandList.h"

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
		IDevice;
	}
	namespace DirectX12
	{		
		class CommandList : public APIInterface::ICommandList
		{
			/******************************************************************
			* Internal Structure Declarations
			******************************************************************/
		public:

		public:
			CommandList();
			virtual ~CommandList() = default;

			//virtual RHI_API_RESULT Initialize(APIInterface::IDevice* iDevice) = 0;

		private:

		};
	}
}

#endif // DX12_COMMAND_LIST_H_

/************************************
* End of file
************************************/