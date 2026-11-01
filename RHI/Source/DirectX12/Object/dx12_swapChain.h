
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

            // 待機用ハンドルを所有するのでコピー禁止
            SwapChain(const SwapChain&) = delete;
            SwapChain& operator=(const SwapChain&) = delete;


        public:          /*---クラスメソッド---*/

            // バックバッファの数
            // 3枚にしておくと、GPUが1枚を表示中・1枚を描画中でも、CPUが次のフレームの準備に入れる
            static constexpr UINT BUFFER_COUNT = 3;
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
             * @param[in] bool allowTearing テアリングを許可するか (DXGIFactory6::IsTearingSupported の結果を渡す)
             * @param[in] UINT maxFrameLatency CPUが先行できる最大フレーム数 (FrameContext の数と合わせる)
             * @return HRESULT 成功の有無
             ************************************************************************************/
            HRESULT Create(IDXGIFactory6* pDxgiFactory, ID3D12CommandQueue* pCmdQueue, HWND* hWnd, UINT width, UINT height,
                           bool allowTearing, UINT maxFrameLatency);

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
             * @details syncInterval == 0 かつテアリング許可時は DXGI_PRESENT_ALLOW_TEARING を付ける
             *          (付けないと VSync OFF でもリフレッシュレートで頭打ちになる環境がある)
             *
             * @param[in] UINT syncInterval 0:VSyncなし 1:VSyncあり
             * @return HRESULT 成功の有無 (DXGI_ERROR_DEVICE_REMOVED 等もそのまま返す)
             ************************************************************************************/
            HRESULT Present(UINT syncInterval);

            /***********************************************************************************
             * @brief 次のフレームを描き始めてよくなるまで待つ
             * @details Frame Latency Waitable Object を使う。
             *          CPUが GPU / 表示より先行しすぎて入力遅延が増えるのを防ぐ (先行は maxFrameLatency フレームまで)
             *
             * @param[in] DWORD timeoutMs タイムアウト(ミリ秒)
             ************************************************************************************/
            void WaitForNextFrame(DWORD timeoutMs = 1000)const;

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
            bool IsTearingAllowed(void)const { return _allowTearing; }

            /***********************************************************************************
             * @brief 解放関数
             *
             ************************************************************************************/
            void Release(void);

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/

            Microsoft::WRL::ComPtr<IDXGISwapChain4> _pSwapChain;
            Microsoft::WRL::ComPtr<ID3D12Resource>  _backBuffers[BUFFER_COUNT];
            UINT                                    _width              = 0;
            UINT                                    _height             = 0;
            UINT                                    _flags              = 0;        // 生成時のフラグ (ResizeBuffers にも同じ値を渡す必要がある)
            bool                                    _allowTearing       = false;
            HANDLE                                  _frameLatencyWaitable = nullptr; // 自分で CloseHandle する必要がある

            HRESULT acquireBackBuffers();
            void    releaseBackBuffers();
        };
    }
}


#endif // DX12_SWAP_CHAIN_H_

/************************************
* End of file
************************************/
