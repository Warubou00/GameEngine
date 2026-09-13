
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
#include <memory>
#include "IObject.h"

/******************************************************************
* Macro Definitions
******************************************************************/


/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/

class IDXGIFactory6;
class ID3D12Device;
class ID3D12Device5;
class ID3D12Device9;

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

            bool Initialize()override;

            IDXGIFactory6* GetDxgiFactory6()const;

            ID3D12Device* GetDx12Device()const;

            ID3D12Device5* GetDx12Device5()const;
            
            ID3D12Device9* GetDx12Device9()const;

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/

            // DXGIファクトリー
            std::unique_ptr<DirectX::DXGIFactory6> _dxgiFactory;
            // アダプター
            std::unique_ptr<DirectX::Adapter> _adapter;
            // DirectX12のデバイス
            std::unique_ptr<RHI::DirectX12::Device> _dx12Device;

        };

    }
}

#endif // RHI_DEVICE_H_

/************************************
* End of file
************************************/
