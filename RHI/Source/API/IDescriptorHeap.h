
/***********************************************************************************
* @file    IDescriptorHeap.h
* @brief   API interface descriptor heap
* @details
*
* @author  Keigo Onari
* @date    2026/09/20
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note
************************************************************************************/
#pragma once
#ifndef API_INTERFACE_DESCRIPTOR_HEAP_H_
#define API_INTERFACE_DESCRIPTOR_HEAP_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "IAPI.h"

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
		
		class IDescriptorHeap
		{
		public:
			IDescriptorHeap() {}
			virtual ~IDescriptorHeap() = default;

			virtual RHI_API_RESULT Initialize(IDevice* iDevice) = 0;

		private:
		};
	}
}

#endif // API_INTERFACE_DESCRIPTOR_HEAP_H_

/************************************
* End of file
************************************/