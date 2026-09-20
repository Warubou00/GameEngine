
/***********************************************************************************
* @file    IPipeline.h
* @brief   API interface pipeline
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
#ifndef API_INTERFACE_PIPELINE_H_
#define API_INTERFACE_PIPELINE_H_

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
		class IResourceBinding;

		class IPipeline
		{
		public:
			IPipeline() {}
			virtual ~IPipeline() = default;

			virtual RHI_API_RESULT Initialize(RHI::APIInterface::IDevice* iDevice, RHI::APIInterface::IResourceBinding* iResourceBinding) = 0;

		private:
		};
	}
}

#endif // API_INTERFACE_PIPELINE_H_

/************************************
* End of file
************************************/