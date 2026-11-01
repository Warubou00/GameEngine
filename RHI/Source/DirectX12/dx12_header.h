
/***********************************************************************************
* @file    dx12_header.h
* @brief   DX12ヘッダー
* @details
*
* @author  Keigo Onari
* @date    2026/1/13
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    その他
************************************************************************************/

#pragma once
#ifndef DX12_HEADER_H_
#define DX12_HEADER_H_

/******************************************************************
* Include Headers
******************************************************************/
// NOMINMAX は Windows.h が最初に読まれる前に定義しないと効かない
// (d3d12.h が内部で Windows.h を読むため、必ず先頭に置く)
#ifndef NOMINMAX
#define NOMINMAX
#endif
#include <Windows.h>

#include<d3d12.h>
#include<dxgi1_6.h>

#include <wrl.h>

#pragma comment(lib,"d3d12.lib")
#pragma comment(lib,"dxgi.lib")

/******************************************************************
* Macro Definitions
******************************************************************/

// HRESULT が失敗ならその値をそのまま返す
#ifndef RHI_RETURN_IF_FAILED
#define RHI_RETURN_IF_FAILED(expr)          \
	do {                                    \
		const HRESULT hr_ = (expr);         \
		if (FAILED(hr_)) { return hr_; }    \
	} while (0)
#endif

/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/

namespace RHI
{
	namespace DirectX12
	{

	}
}

#endif // DX12_HEADER_H_

/************************************
* End of file
************************************/
