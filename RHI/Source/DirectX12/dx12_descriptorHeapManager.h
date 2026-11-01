
/***********************************************************************************
* @file    dx12_descriptorHeapManager.h
* @brief   DX12のデスクリプタヒープ一括管理
* @details シェーダーから見える CBV/SRV/UAV ヒープと、その番号の空き管理
*          (RTV / DSV は描画先と一緒に CommandContext が DescriptorHeap で持つ)
*
* @author  Keigo Onari
* @date    2026/04/22
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note
************************************************************************************/

#pragma once
#ifndef DX12_DESCTIPTOR_HEAP_H_
#define DX12_DESCTIPTOR_HEAP_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "dx12_header.h"
#include <memory>
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
    namespace DirectX12
    {
        class DescriptorHeap;

        class DescriptorHeapManager
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---定数---*/

            // 確保に失敗した時の値 (以前は 9999 を返していたが、9999番も正しい番号になり得るため最大値に変更)
            static constexpr uint32_t INVALID_INDEX = 0xFFFFFFFFu;

            // シェーダーから見えるヒープのディスクリプタ数
            // 以前は 1,000,000 個 (約 32MB) 確保していた。シェーダーのテクスチャ配列は 4096 個なので、
            // 頂点バッファ等を含めても十分な数に減らした (ヒープが小さいほど作成も速くメモリも少ない)
            static constexpr uint32_t MAX_SRV_COUNT = 65536;

        public:          /*---コンストラクタ・デストラクタ---*/
            DescriptorHeapManager();
            ~DescriptorHeapManager();

        public:          /*---クラスメソッド---*/

            /***********************************************************************************
             * @brief 生成関数
             *
             * @param[in] ID3D12Device* device デバイス
             * @return HRESULT 成功の有無
             ************************************************************************************/
            HRESULT Create(ID3D12Device* device);

            // テクスチャロード時などに呼ばれる：空いているインデックスを返す (満杯なら INVALID_INDEX)
            uint32_t AllocateSrv();

            // テクスチャ破棄時に呼ばれる：インデックスを空きリストに戻す
            // ※GPU がまだ使っている可能性がある時は、完了を待ってから戻すこと
            void FreeSrv(uint32_t index);

            // 指定インデックスのCPUハンドル（実際の書き込み先アドレス）を取得する
            D3D12_CPU_DESCRIPTOR_HANDLE GetSrvCpuHandle(UINT index)const;

            // 指定インデックスのGPUハンドル（シェーダーから参照する時のアドレス）を取得する
            D3D12_GPU_DESCRIPTOR_HANDLE GetSrvGpuHandle(UINT index)const;

            ID3D12DescriptorHeap* GetSrvHeap()const;

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/

            // シェーダーから見える CBV/SRV/UAV ヒープ
            // 先頭ハンドルとサイズはラッパーが覚えているので、毎回 API で取得しない (以前は呼ぶたびに取得していた)
            // ※使われていなかった RTV / DSV / Sampler ヒープは削除した (RTV / DSV は CommandContext が持つ)
            std::unique_ptr<DescriptorHeap> _srvUavCbvHeap;

            // --- SRVヒープのアロケータ（インデックス管理） ---
            uint32_t _currentSrvIndex = 0;   // 現在未使用の先頭インデックス

            // 破棄されて再利用可能になったインデックスのリスト（FreeList）
            std::vector<uint32_t> _freeSrvIndices;

        };
    }
}


#endif // DX12_DESCRIPTOR_HEAP_H_

/************************************
* End of file
************************************/
