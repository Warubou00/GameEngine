
/***********************************************************************************
* @file    RHIdll
* @brief   RHIのdll
* @details RHI.dll が export するのは、公開インターフェイスを作る / 壊す関数だけ。
*          (以前は C# から直接呼ぶ関数 RHI_Initialize / RHI_TestRender などをここで export していた。
*           C# の入口は Renderer.dll に移した : Phase 3)
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
#include "Public/rhi.h"
#include "RenderHardwareInterface.h"

extern "C"
{
	RHI_API RHI::IRenderHardwareInterface* RHI_CreateRenderHardwareInterface()
	{
		return new RHI::RenderHardwareInterface();
	}

	RHI_API void RHI_DestroyRenderHardwareInterface(RHI::IRenderHardwareInterface* rhi)
	{
		// 生成した DLL の中で delete する (DLL をまたいで new / delete を混ぜないため)
		// デストラクタで GPU の完了を待ってから破棄される
		delete rhi;
	}
}

/************************************
* End of file
************************************/
