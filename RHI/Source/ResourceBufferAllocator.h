
/***********************************************************************************
* @file    ResourceBufferAllocator.h
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

#pragma once
#ifndef RESOURCE_BUFFER_ALLOCATOR_H_
#define RESOURCE_BUFFER_ALLOCATOR_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <stdint.h>


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
    struct ResourceBufferAllocation
    {
        uint64_t ByteOffset   = 0;     // VRAMへのコピー時に CopyBufferRegion に渡すオフセット
        uint32_t StrideOffset = 0;     // 描画時に SetGraphicsRoot32BitConstants へ渡す頂点オフセット
        uint32_t Count        = 0;     // 割り当てられた数
        bool     IsValid      = false; // 確保できたか (以前は失敗しても全て0で返り、先頭を上書きしていた)
    };

    class ResourceBufferAllocator
    {

        /******************************************************************
        * Internal Enumeration Declarations
        ******************************************************************/


        /******************************************************************
        * Internal Structure Declarations
        ******************************************************************/


    public:          /*---コンストラクタ・デストラクタ---*/
        ResourceBufferAllocator();
        ~ResourceBufferAllocator();

    public:          /*---クラスメソッド---*/

        void Initialize(uint64_t maxByteSize);


        /***********************************************************************************
         * @brief メモリ情報の作成
         * @details
         *
         * @param[in] uint32_t count 数
         * @param[in] uint32_t stride 1つ当たりのサイズ
         * @return ResourceBufferAllocation バッファーのメモリ情報 (容量不足なら IsValid == false)
         ************************************************************************************/
        ResourceBufferAllocation Allocate(uint32_t count, uint32_t stride);

        void Reset();

    public:          /*---アクセッサー関数群---*/


    private:         /*---メンバ変数---*/

        uint64_t _maxByteSize         = 0;
        uint64_t _currentByteOffset   = 0;     // 4GB を超えるバッファでも溢れないよう 64bit
        uint32_t _currentVertexOffset = 0;


    };
}

#endif // RESOURCE_BUFFER_ALLOCATOR_H_

/************************************
* End of file
************************************/
