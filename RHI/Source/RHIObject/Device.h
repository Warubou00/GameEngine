
/***********************************************************************************
* @file    Device.h
* @brief   RHI Device
* @details 
*
* @author  Keigo Onari
* @date    2026/1/13
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    
************************************************************************************/

#pragma once
#ifndef RHI_DEVICE_H_
#define RHI_DEVICE_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <string>
#include "IRHIObject.h"

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
        class Adapter;
        class DXGIFactory6;
    }

    namespace DirectX12
    {
        class Device;
    }

    namespace ObjectInterface
    {
        class Device : IObject
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            Device();
            ~Device();


        public:          /*---クラスメソッド---*/

            RHI_RESULT Initialize()override;

            DirectX::DXGIFactory6* GetDxgiFactory()const;
            DirectX::Adapter*      GetAdapter()const;
            DirectX12::Device*     GetDevice()const;

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/

            // DXGIファクトリー
            std::unique_ptr<DirectX::DXGIFactory6> _dxgiFactory;
            // アダプター
            std::unique_ptr<DirectX::Adapter> _adapter;
            // DirectX12のデバイス
            std::unique_ptr<DirectX12::Device> _dx12Device;

            std::string getDeviceVersion();
            std::string getAdapterVersion();
        };

    }
}

#endif // RHI_DEVICE_H_

/************************************
* End of file
************************************/
