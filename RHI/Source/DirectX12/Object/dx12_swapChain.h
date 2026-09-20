
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
#include "../../API/ISwapChain.h"

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
    namespace APIInterface
    {
        class IDevice;
        class ICommandQueue;
    }

    namespace DirectX
    {
        class DXGIFactory6;
    }

    namespace DirectX12
    {
        class CommandQueue;

        class SwapChain : public APIInterface::ISwapChain
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            SwapChain();
            ~SwapChain()override;


        public:          /*---クラスメソッド---*/


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
            RHI_API_RESULT Initialize(RHI::APIInterface::IDevice* iDevice, RHI::APIInterface::ICommandQueue* iCmdQueue, HWND* hWnd, UINT width, UINT height)override;

            /***********************************************************************************
             * @brief 取得関数
             *
             * @return IDXGISwapChain4* スワップチェーンのポインタ
             ************************************************************************************/
            IDXGISwapChain4* Get(void)const;

            UINT GetFrameBufferIndex(void)const;

            ID3D12Resource* GetCurrentBuffer(void)const;

            void Present(void)override;

            /***********************************************************************************
             * @brief 解放関数
             * @details ComPtrは明示的な解放をしない
             * 
             ************************************************************************************/
            void Release(void);

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/

            Microsoft::WRL::ComPtr<IDXGISwapChain4> _pSwapChain ;

        };
    }
}


#endif // DX12_SWAP_CHAIN_H_

/************************************
* End of file
************************************/
