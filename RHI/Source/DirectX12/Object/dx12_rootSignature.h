
/***********************************************************************************
* @file    dx12_rootSignature.h
* @brief   DX12ルートシグネチャ
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
#ifndef DX12_ROOT_SIGNATURE_H_
#define DX12_ROOT_SIGNATURE_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <cstdint>
#include "../dx12_header.h"

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
        class RootSignature
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            RootSignature();
            ~RootSignature();

        public:          /*---クラスメソッド---*/

            /***********************************************************************************
             * @brief 生成関数
             * @details 
             *
             * @param[in] ID3D12Device* device dx12デバイス
             * @param[in] const D3D12_ROOT_SIGNATURE_DESC& rootSignatureDesc ルートシグネチャのデスクリプタ設定
             * @return HRESULT 成功の有無
             ************************************************************************************/
            HRESULT Create(ID3D12Device* device, const D3D12_ROOT_SIGNATURE_DESC& rootSignatureDesc);

            /***********************************************************************************
             * @brief 生成関数 (バージョン指定。1.1 なら範囲ごとに「データが変わらない」などの情報を渡せる)
             * @details 1.1 を渡しても GPU / ランタイムが 1.1 に対応していなければ 1.0 に変換して作る
             *          (1.1 の追加情報は捨てられるだけで、動作は同じ)
             ************************************************************************************/
            HRESULT Create(ID3D12Device* device, const D3D12_VERSIONED_ROOT_SIGNATURE_DESC& rootSignatureDesc);

            // ルートシグネチャ 1.1 が使えるか
            static bool IsVersion11Supported(ID3D12Device* device);

            // シリアライズした中身のハッシュ (PSO キャッシュの名前に使う。ルートシグネチャが変わったら別の PSO になる)
            uint64_t GetHash()const { return _hash; }

            /***********************************************************************************
             * @brief 取得関数
             * @details
             *
             * @return ID3D12RootSignature* dx12ルートシグネチャ
             ************************************************************************************/
            ID3D12RootSignature* Get()const;

            void Release(void);

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/

            Microsoft::WRL::ComPtr<ID3D12RootSignature> _rootSignature;
            uint64_t                                    _hash = 0;

        };
    }
}


#endif // DX12_ROOT_SIGNATURE_H_

/************************************
* End of file
************************************/
