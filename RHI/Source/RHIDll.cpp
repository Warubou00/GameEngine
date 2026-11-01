
/***********************************************************************************
* @file    RHIdll
* @brief   RHIのdll
* @details
*
* @author  Keigo Onari
* @date    2026/09/13
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    その他
************************************************************************************/


/******************************************************************
* Include Headers
******************************************************************/
#include "RenderHardwareInterface.h"
#include "RHIObject/IRHIObject.h"
#include "common.h"
#include "../../Core/Source/Log/logger.h"

/******************************************************************
* Macro Definitions
******************************************************************/


/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/
struct OutRenderObjData
{
	uint32_t RenderObjectID;
};

static RHI::RenderHardwareInterface* g_pRhi = nullptr;

extern "C"
{
	RHI_API void RHI_SetLogCallback(LogCallback callback, WLogCallback wcallback)
	{
		Core::Logger::Initialize(callback, wcallback);
	}

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

	RHI_API void RHI_TestRender()
	{
		if (g_pRhi == nullptr)
			return;

		g_pRhi->TestRender();
	}

	RHI_API void RHI_TestLoadModel(float posX, float posY, float posZ)
	{
		if (g_pRhi == nullptr)
			return;

		g_pRhi->Test_LoadModel(posX, posY, posZ);
	}

	RHI_API void RHI_DebugExecute()
	{
		if (g_pRhi == nullptr)
			return;

		g_pRhi->DebugExecute();
	}

	RHI_API void RHI_BeginRender()
	{

	}

	RHI_API void RHI_EndRender()
	{

	}

	// ウィンドウのクライアント領域のサイズが変わった時に呼ぶ (最小化中の 0 は無視される)
	RHI_API bool RHI_Resize(uint32_t width, uint32_t height)
	{
		if (g_pRhi == nullptr)
			return false;

		return g_pRhi->Resize(width, height);
	}

	RHI_API void RHI_CleanUp()
	{
		if (g_pRhi == nullptr)
			return;

		// 以前は CleanUp だけで g_pRhi 自体が delete されずリークしていた (2-11)
		g_pRhi->CleanUp();
		delete g_pRhi;
		g_pRhi = nullptr;
	}
}



/************************************
* End of file
************************************/
