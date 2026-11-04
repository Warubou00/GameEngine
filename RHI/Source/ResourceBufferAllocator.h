
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
#include <vector>


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

        /***********************************************************************************
         * @brief 使い終わった範囲を返す (ED5。モデルの読み直し / 解放)
         * @details 空きの一覧に入れ、隣の空きとつなげる。次の Allocate は空きから先に探す (最初に入る所)
         *          以前は前へ進めるだけで、返す方法が無かった (読み直すたびに使う量が増えた)
         *          ※GPU が使い終わってから呼ぶこと (呼ぶ側で GPU の完了を待つ)
         *          1 つのアロケーターは 1 種類の大きさ (stride) で使う前提 (頂点 / インデックス)
         ************************************************************************************/
        void Free(const ResourceBufferAllocation& allocation, uint32_t stride);

        void Reset();

        uint64_t GetUsedBytes() const { return _currentByteOffset - _freeBytes; }   // 使っている量 (空きを除く)

    public:          /*---アクセッサー関数群---*/


    private:         /*---メンバ変数---*/

        uint64_t _maxByteSize         = 0;
        uint64_t _currentByteOffset   = 0;     // 4GB を超えるバッファでも溢れないよう 64bit
        uint32_t _currentVertexOffset = 0;

        // 空き (番号の順に並べる。隣どうしはつなげる)
        struct FreeBlock
        {
            uint64_t ByteOffset;
            uint32_t StrideOffset;
            uint32_t Count;
        };
        std::vector<FreeBlock> _freeBlocks;
        uint64_t               _freeBytes = 0;


    };
}

#endif // RESOURCE_BUFFER_ALLOCATOR_H_

/************************************
* End of file
************************************/
