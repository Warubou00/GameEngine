
/***********************************************************************************
* @file    IDevice.h
* @brief   API interface device
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
#ifndef API_INTERFACE_DEVICE_H_
#define API_INTERFACE_DEVICE_H_

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
		class IDevice
		{
		public:
			IDevice() {}
			virtual ~IDevice() = default;

			virtual RHI_API_RESULT Initialize() = 0;

		private:
		};
	}
}

#endif // API_INTERFACE_DEVICE_H_

/************************************
* End of file
************************************/