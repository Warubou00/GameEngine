
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


#include "../../RHIObject/IDevice.h"

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
        class Device : public IDevice
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            Device();
            ~Device()override;


        public:          /*---クラスメソッド---*/

            RHI_RESULT Initialize()override;

            /***********************************************************************************
             * @brief 終了処理
             * @details デバイスは最後に破棄すること (他のRHIオブジェクトを先に Dispose する)
             ************************************************************************************/
            void Dispose()override;

        public:          /*---アクセッサー関数群---*/

            DirectX::DXGIFactory6* GetDxgiFactory()const;
            DirectX::Adapter*      GetAdapter()const;
            DirectX12::Device*     GetDevice()const;



        private:         /*---メンバ変数---*/

            // DXGIファクトリー
            std::unique_ptr<DirectX::DXGIFactory6> _dxgiFactory;
            // アダプター
            std::unique_ptr<DirectX::Adapter> _adapter;
            // DirectX12のデバイス
            std::unique_ptr<DirectX12::Device> _dx12Device;
            // デバッグメッセージのコールバック登録ID (0なら未登録)
            unsigned long _debugCallbackCookie = 0;

            std::string getDeviceVersion();
            std::string getAdapterVersion();

            // デバッグレイヤーのメッセージを Logger に流す (Debugビルドのみ)
            void registerDebugMessageCallback();
            void unregisterDebugMessageCallback();
        };

    }
}

#endif // RHI_DEVICE_H_

/************************************
* End of file
************************************/
