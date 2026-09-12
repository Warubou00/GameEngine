
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
#include <vector>

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
    namespace DirectX
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

            HRESULT EnumerateAdaptersFromGpuName(IDXGIFactory6* pFactory, const std::wstring& enumeratingGPU);
            HRESULT EnumerateAdapterFromHighPerformance(IDXGIFactory6* pFactory);

            HRESULT TryVersionUp();
            HRESULT TryVersionUp1();
            HRESULT TryVersionUp3();

            IDXGIAdapter* GetAdapter(void)const;
            IDXGIAdapter1* GetAdapter1(void)const;
            IDXGIAdapter3* GetAdapter3(void)const;

            void Release(void);

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/

            std::vector<IDXGIAdapter*> _pAdapters;
            Microsoft::WRL::ComPtr<IDXGIAdapter>  _pAdapter;
            Microsoft::WRL::ComPtr<IDXGIAdapter1> _pAdapter1;
            Microsoft::WRL::ComPtr<IDXGIAdapter3> _pAdapter3;

            HRESULT asAdapter();
            HRESULT asAdapter1();
            HRESULT asAdapter3();
        };
    }
}


#endif // DX12_DEVICE_H_

/************************************
* End of file
************************************/
