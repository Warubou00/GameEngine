
/***********************************************************************************
* @file    adapter.h
* @brief   Graphicsボードのアダプタークラス
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
#ifndef DIRECTX_ADAPTER_H_
#define DIRECTX_ADAPTER_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <dxgi1_6.h>
#include <string>

#include <wrl.h>

#pragma comment(lib,"dxgi.lib")

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
    namespace DXGI
    {
        class Adapter
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            Adapter();
            ~Adapter();


        public:          /*---クラスメソッド---*/

            /***********************************************************************************
             * @brief GPU名の部分一致でアダプターを選ぶ
             * @details ソフトウェアアダプター(WARP等)は対象外
             *
             * @param[in] IDXGIFactory6* pFactory ファクトリー
             * @param[in] const std::wstring& enumeratingGPU 探すGPU名 (例: L"NVIDIA")
             * @return HRESULT 見つからなければ DXGI_ERROR_NOT_FOUND
             ************************************************************************************/
            HRESULT EnumerateAdaptersFromGpuName(IDXGIFactory6* pFactory, const std::wstring& enumeratingGPU);

            /***********************************************************************************
             * @brief 高性能GPUを優先してアダプターを選ぶ
             * @details ソフトウェアアダプター(WARP等)は飛ばす
             *
             * @param[in] IDXGIFactory6* pFactory ファクトリー
             * @return HRESULT 見つからなければ DXGI_ERROR_NOT_FOUND
             ************************************************************************************/
            HRESULT EnumerateAdapterFromHighPerformance(IDXGIFactory6* pFactory);

            HRESULT TryVersionUp();
            HRESULT TryVersionUp1();
            HRESULT TryVersionUp3();

            IDXGIAdapter* GetAdapter(void)const;
            IDXGIAdapter1* GetAdapter1(void)const;
            IDXGIAdapter3* GetAdapter3(void)const;

            /***********************************************************************************
             * @brief アダプター名の取得
             * @details 以前はスタック上の DXGI_ADAPTER_DESC 内を指すポインタを返していた(関数終了で無効)ため値で返す
             *
             * @return std::wstring アダプター名 (未選択なら空)
             ************************************************************************************/
            std::wstring GetAdapterName(void)const;

            void Release(void);

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/

            Microsoft::WRL::ComPtr<IDXGIAdapter>  _pAdapter;
            Microsoft::WRL::ComPtr<IDXGIAdapter1> _pAdapter1;
            Microsoft::WRL::ComPtr<IDXGIAdapter3> _pAdapter3;

            HRESULT asAdapter();
            HRESULT asAdapter1();
            HRESULT asAdapter3();
        };
    }
}


#endif // DIRECTX_ADAPTER_H_

/************************************
* End of file
************************************/
