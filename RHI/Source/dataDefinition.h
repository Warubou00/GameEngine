
/***********************************************************************************
* @file    dataDefinition.h
* @brief   汎用的なヘッダー
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

#pragma once
#ifndef RHI_DATA_DEFINITION_H_
#define RHI_DATA_DEFINITION_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <stdint.h>
#include <vector>
#include "../../Core/Source/Math/Float/float.h"


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
	struct RenderObject
	{
		uint32_t VertexCount;			// 描画頂点数
		uint32_t VertexOffset;			// 描画開始頂点位置
		uint32_t IndexCount;			// インデックス数
		uint32_t IndexOffset;			// インデックス読み取り位置
		uint32_t CbOffset;				// 定数バッファオフセット
		uint32_t TextureOffset;			// テクスチャオフセット
	};

	struct RenderQueue
	{
		std::vector<RenderObject> RenderObjects;
	};
}

#endif // RHI_DATA_DEFINITION_H_

/************************************
* End of file
************************************/
