
/***********************************************************************************
* @file    RHIdll
* @brief   RHI‚Ìdll
* @details
*
* @author  Keigo Onari
* @date    2026/09/13
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    ‚»‚Ì‘¼
************************************************************************************/


/******************************************************************
* Include Headers
******************************************************************/
#include "RenderHardwareInterface.h"
#include "Interface/IObject.h"
#include "common.h"

/******************************************************************
* Macro Definitions
******************************************************************/


/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/

static RHI::RenderHardwareInterface* g_pRhi = nullptr;

extern "C"
{
	RHI_API void RHI_Initialize(RHI::RenderHardwareInterface::RHIInitializeInfo* initializedInfo, RHI::ObjectInterface::ObjectInfo* objInfo)
	{
		if (g_pRhi == nullptr)
			g_pRhi = new RHI::RenderHardwareInterface();

		g_pRhi->Initialize(initializedInfo, objInfo);
		return;
	}

	RHI_API bool RHI_CreateBuffer()
	{
		return true;
	}

	RHI_API void RHI_BeginRender()
	{

	}

	RHI_API void RHI_EndRender()
	{

	}

	RHI_API void RHI_CleanUp()
	{

	}
}



/************************************
* End of file
************************************/
