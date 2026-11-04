
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
		_freeBlocks.clear();
		_freeBytes = 0;
	}

	ResourceBufferAllocation ResourceBufferAllocator::Allocate(uint32_t count, uint32_t stride)
	{
		ResourceBufferAllocation result = {};
		uint64_t allocateSize = static_cast<uint64_t>(count) * stride;

		// 返された空きから先に探す (最初に入る所。余りは空きに残す。ED5)
		if (count > 0 && stride > 0)
		{
			for (size_t i = 0; i < _freeBlocks.size(); ++i)
			{
				FreeBlock& block = _freeBlocks[i];
				if (block.Count < count) continue;
				result.ByteOffset   = block.ByteOffset;
				result.StrideOffset = block.StrideOffset;
				result.Count        = count;
				result.IsValid      = true;
				block.ByteOffset   += allocateSize;
				block.StrideOffset += count;
				block.Count        -= count;
				_freeBytes         -= allocateSize;
				if (block.Count == 0) _freeBlocks.erase(_freeBlocks.begin() + static_cast<ptrdiff_t>(i));
				return result;
			}
		}

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

	void ResourceBufferAllocator::Free(const ResourceBufferAllocation& allocation, uint32_t stride)
	{
		if (!allocation.IsValid || allocation.Count == 0 || stride == 0) return;
		const uint64_t size = static_cast<uint64_t>(allocation.Count) * stride;

		// 番号の順の位置に入れる
		size_t at = 0;
		while (at < _freeBlocks.size() && _freeBlocks[at].ByteOffset < allocation.ByteOffset) ++at;
		_freeBlocks.insert(_freeBlocks.begin() + static_cast<ptrdiff_t>(at), FreeBlock{ allocation.ByteOffset, allocation.StrideOffset, allocation.Count });
		_freeBytes += size;

		// 後ろの空きとつなげる → 前の空きとつなげる
		auto end = [stride](const FreeBlock& b) { return b.ByteOffset + static_cast<uint64_t>(b.Count) * stride; };
		if (at + 1 < _freeBlocks.size() && end(_freeBlocks[at]) == _freeBlocks[at + 1].ByteOffset)
		{
			_freeBlocks[at].Count += _freeBlocks[at + 1].Count;
			_freeBlocks.erase(_freeBlocks.begin() + static_cast<ptrdiff_t>(at + 1));
		}
		if (at > 0 && end(_freeBlocks[at - 1]) == _freeBlocks[at].ByteOffset)
		{
			_freeBlocks[at - 1].Count += _freeBlocks[at].Count;
			_freeBlocks.erase(_freeBlocks.begin() + static_cast<ptrdiff_t>(at));
		}

		// 一番後ろの空きは、前へ進めた位置を戻す (空きの一覧から外す)
		if (!_freeBlocks.empty() && end(_freeBlocks.back()) == _currentByteOffset)
		{
			const FreeBlock last = _freeBlocks.back();
			_freeBlocks.pop_back();
			_currentByteOffset   = last.ByteOffset;
			_currentVertexOffset = last.StrideOffset;
			_freeBytes          -= static_cast<uint64_t>(last.Count) * stride;
		}
	}

	void ResourceBufferAllocator::Reset()
	{
		_currentByteOffset   = 0;
		_currentVertexOffset = 0;
		_freeBlocks.clear();
		_freeBytes = 0;
	}

}

/************************************
* End of file
************************************/
