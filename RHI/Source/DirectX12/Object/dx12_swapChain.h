
/***********************************************************************************
* @file    dx12_swapChain.h
* @brief   DX12のスワップチェーン
* @details
*
* @author  Keigo Onari
* @date    2026/02/02
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note
************************************************************************************/

#pragma once
#ifndef DX12_SWAP_CHAIN_H_
#define DX12_SWAP_CHAIN_H_

/******************************************************************
* Include Headers
******************************************************************/
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
        class SwapChain
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            SwapChain();
            ~SwapChain();


        public:          /*---クラスメソッド---*/

            static constexpr UINT BUFFER_COUNT = 2;
            static constexpr DXGI_FORMAT FORMAT = DXGI_FORMAT_R8G8B8A8_UNORM;

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
            HRESULT Create(IDXGIFactory6* pDxgiFactory, ID3D12CommandQueue* pCmdQueue, HWND* hWnd, UINT width, UINT height);

            /***********************************************************************************
             * @brief バックバッファのサイズ変更
             * @details 呼ぶ前にGPUの完了待ちをし、外部で保持しているバックバッファ参照(RTV等)を破棄しておくこと
             *          width / height が0(最小化中)なら何もしない
             *
             * @param[in] UINT width 幅
             * @param[in] UINT height 高さ
             * @return HRESULT 成功の有無
             ************************************************************************************/
            HRESULT Resize(UINT width, UINT height);

            /***********************************************************************************
             * @brief 画面への表示
             *
             * @param[in] UINT syncInterval 0:VSyncなし 1:VSyncあり
             * @return HRESULT 成功の有無 (DXGI_ERROR_DEVICE_REMOVED 等もそのまま返す)
             ************************************************************************************/
            HRESULT Present(UINT syncInterval);

            /***********************************************************************************
             * @brief 取得関数
             *
             * @return IDXGISwapChain4* スワップチェーンのポインタ
             ************************************************************************************/
            IDXGISwapChain4* Get(void)const;

            UINT GetCurrentBackBufferIndex(void)const;

            /***********************************************************************************
             * @brief バックバッファの取得
             * @details スワップチェーンが所有する参照を返す。参照カウントは増えないのでReleaseしないこと
             ************************************************************************************/
            ID3D12Resource* GetBufferFromIndex(UINT index)const;
            ID3D12Resource* GetBackBuffer(void)const;

            UINT GetBufferCount(void)const;
            UINT GetWidth(void)const  { return _width; }
            UINT GetHeight(void)const { return _height; }

            /***********************************************************************************
             * @brief 解放関数
             *
             ************************************************************************************/
            void Release(void);

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/

            Microsoft::WRL::ComPtr<IDXGISwapChain4> _pSwapChain;
            Microsoft::WRL::ComPtr<ID3D12Resource>  _backBuffers[BUFFER_COUNT];
            UINT                                    _width  = 0;
            UINT                                    _height = 0;

            HRESULT acquireBackBuffers();
            void    releaseBackBuffers();
        };
    }
}


#endif // DX12_SWAP_CHAIN_H_

/************************************
* End of file
************************************/
