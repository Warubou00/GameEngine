
/***********************************************************************************
* @file    ResourceBufferAllocator.cpp
* @brief   リソースバッファのメモリ管理
* @details 
*
* @author  Keigo Onari
* @date    2026/09/12
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
#include "ResourceBufferAllocator.h"

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
	ResourceBufferAllocator::ResourceBufferAllocator() {}
	ResourceBufferAllocator::~ResourceBufferAllocator() {}
	
	void ResourceBufferAllocator::Initialize(uint64_t maxByteSize)
	{
		_maxByteSize         = maxByteSize;
		_currentByteOffset   = 0;
		_currentVertexOffset = 0;
	}

	ResourceBufferAllocation ResourceBufferAllocator::Allocate(uint32_t count, uint32_t stride)
	{
		ResourceBufferAllocation result = {};
		uint64_t allocateSize = static_cast<uint64_t>(count) * stride;

		// 容量オーバーチェック (メガバッファのサイズを超えたら確保失敗)
		if (count == 0 || stride == 0 || _currentByteOffset + allocateSize > _maxByteSize)
		{
			return result; // 失敗時は IsValid == false
		}

		// 現在のオフセットを結果として確保
		result.ByteOffset   = _currentByteOffset;
		result.StrideOffset = _currentVertexOffset;
		result.Count        = count;
		result.IsValid      = true;

		// 次のモデルが書き込めるように、オフセットを後ろへ進める
		_currentByteOffset   += allocateSize;
		_currentVertexOffset += count;

		return result;
	}

	void ResourceBufferAllocator::Reset()
	{
		_currentByteOffset   = 0;
		_currentVertexOffset = 0;
	}

}

/************************************
* End of file
************************************/
