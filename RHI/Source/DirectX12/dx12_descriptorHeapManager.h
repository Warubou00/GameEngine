
/***********************************************************************************
* @file    dx12_descriptorHeap.h
* @brief   DX12のデスクリプタヒープ一括管理
* @details 
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
        class DescriptorHeapManager
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        private:          /*---コンストラクタ・デストラクタ---*/
            DescriptorHeapManager() {};
            ~DescriptorHeapManager() {};
            DescriptorHeapManager(const DescriptorHeapManager&) = delete;
            DescriptorHeapManager& operator=(const DescriptorHeapManager) = delete;

        public:          /*---クラスメソッド---*/

            static DescriptorHeapManager& getInstance()
            {
                static DescriptorHeapManager instance;
                return instance;
            }

            /***********************************************************************************
             * @brief 生成関数
             * @details 
             *
             * @param[in] IDXGIFactory6* pDxgiFactory DXGIFactory6のポインタ
             * @param[in] ID3D12CommandQueue* pCmdQueue コマンドキュー
             * @param[in] HWND* hWnd ウインドウハンドルのポインタ
             * @param[in] UINT width 幅
             * @param[in] UINT height 高さ
             * @return HRESULT 成功の有無
             ************************************************************************************/
            HRESULT Create(ID3D12Device* device);

            // テクスチャロード時などに呼ばれる：空いているインデックスを返す
            uint32_t AllocateSrv();


            // テクスチャ破棄時に呼ばれる：インデックスを空きリストに戻す
            void FreeSrv(uint32_t index);

            // 指定インデックスのCPUハンドル（実際の書き込み先アドレス）を取得する
            D3D12_CPU_DESCRIPTOR_HANDLE GetSrvCpuHandle(uint32_t index);

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/

            // DX12のヒープ実体
            Microsoft::WRL::ComPtr<ID3D12DescriptorHeap> _srvUavCbvHeap;
            Microsoft::WRL::ComPtr<ID3D12DescriptorHeap> _samplerHeap;
            Microsoft::WRL::ComPtr<ID3D12DescriptorHeap> _rtvHeap;
            Microsoft::WRL::ComPtr<ID3D12DescriptorHeap> _dsvHeap;

            // --- SRVヒープのアロケータ（インデックス管理） ---
            const uint32_t _maxSrvCount = 1000000; // 確保した最大数
            uint32_t _currentSrvIndex = 0;   // 現在未使用の先頭インデックス

            // 破棄されて再利用可能になったインデックスのリスト（FreeList）
            std::vector<uint32_t> _freeSrvIndices;

            // 各ヒープのディスクリプタのサイズ（インデックスからアドレスを計算するのに必要）
            UINT _srvDescriptorSize;

            // rtvのヒープサイズ
            UINT _rtvDescriptorSize;

        };
    }
}


#endif // DX12_DESCRIPTOR_HEAP_H_

/************************************
* End of file
************************************/
