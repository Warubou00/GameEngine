
/***********************************************************************************
* @file    IObject.cpp
* @brief   RHI IObject
* @details 
*
* @author  Keigo Onari
* @date    2026/4/27
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    
************************************************************************************/


/******************************************************************
* Include Headers
******************************************************************/
#include "IObject.h"

#include "Device.h"
#include "CommandContext.h"
#include "Buffer.h"
#include "Texture.h"

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
	namespace ObjectInterface
	{
		IObject::IObject() = default;
		IObject::~IObject() = default;

		RHI_RESULT IObject::Initialize(Device* device) 
		{
			return RHI_SUCCEEDED;
		}

		RHI_RESULT IObject::Initialize(CommandContext* commandContext)
		{
			return RHI_SUCCEEDED;
		}

		RHI_RESULT IObject::Initialize(Device* device, CommandContext* commandContext)
		{
			return RHI_SUCCEEDED;
		}

		RHI_RESULT IObject::Initialize(Buffer* device)
		{
			return RHI_SUCCEEDED;
		}

		RHI_RESULT IObject::Initialize(Texture* device) 
		{
			return RHI_SUCCEEDED;
		}

		RHI_RESULT IObject::Initialize(Device* device, CommandContext* commandContext, Buffer* buffer, Texture* texture)
		{
			return RHI_SUCCEEDED;
		}
	}
}

/************************************
* End of file
************************************/
