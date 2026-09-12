
/***********************************************************************************
* @file    Buffer.cpp
* @brief   RHI Buffer
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
#include "Buffer.h"
#include "Device.h"
#include "../DirectX12/dx12_resourceManager.h"

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
		Buffer::Buffer() {}
		Buffer::~Buffer() {}

		bool Buffer::Initialize()
		{
			return true;
		}

		bool Buffer::Initialize(Device* device)
		{
			// TODO : Bufferの作成
			// 頂点、インデックス、Initializeでやるべき？
			// 都度呼ばれるようCreate()にすべきか

			return true;
		}

		bool Buffer::Create(Device* device)
		{
			//DirectX12::ResourceManager::getInstance().CreateVertResource(device->GetDx12Device())

			return true;
		}
	}
}

/************************************
* End of file
************************************/
