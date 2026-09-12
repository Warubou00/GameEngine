
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

		bool IObject::Initialize(Device* device) 
		{
			return true;
		}

		bool IObject::Initialize(CommandContext* device)
		{
			return true;
		}

		bool IObject::Initialize(Buffer* device)
		{
			return true;
		}

		bool IObject::Initialize(Texture* device) 
		{
			return true;
		}

		bool IObject::Initialize(Device* device, CommandContext* commandContext, Buffer* buffer, Texture* texture)
		{
			return true;
		}
	}
}

/************************************
* End of file
************************************/
