//
///***********************************************************************************
//* @file    dx12_device.h
//* @brief   dx12デバイス
//* @details
//*
//* @author  Keigo Onari
//* @date    2026/09/18
//* @version v_1.0.0
//*
//* @copyright
//* Copyright (c) 2024 Keigo Onari. All rights reserved.
//*
//* @note
//************************************************************************************/
//
//#pragma once
//#ifndef DIRECTX12_DEVICE_H_
//#define DIRECTX12_DEVICE_H_
//
///******************************************************************
//* Include Headers
//******************************************************************/
//#include "../dx12_header.h"
//#include "../../Interface/IDevice.h"
//
///******************************************************************
//* Macro Definitions
//******************************************************************/
//
//
///******************************************************************
//* Enumeration Definitions
//******************************************************************/
//
//
///******************************************************************
//* Structure Declarations
//******************************************************************/
//
//
//
//namespace RHI
//{
//    namespace API
//    {
//        class IBuffer;
//        class ITexture;
//        class IPipelineState;
//        
//        namespace directX12
//        {
//            class DX12Device : IDevice
//            {
//
//                /******************************************************************
//                * Internal Enumeration Declarations
//                ******************************************************************/
//
//
//                /******************************************************************
//                * Internal Structure Declarations
//                ******************************************************************/
//
//
//            public:          /*---コンストラクタ・デストラクタ---*/
//                DX12Device() {};
//                ~DX12Device()override = default;
//
//
//            public:          /*---クラスメソッド---*/
//
//                IBuffer* CreateBuffer(uint32_t sizeInBytes)override;
//                ITexture* CreateTexture(uint32_t width, uint32_t height)override;
//                IPipelineState* CreatePipelineState()override;
//
//            public:          /*---アクセッサー関数群---*/
//
//
//            private:         /*---メンバ変数---*/
//
//                Microsoft::WRL::ComPtr<ID3D12Device>  _device;
//                Microsoft::WRL::ComPtr<ID3D12Device5> _device5;
//                Microsoft::WRL::ComPtr<ID3D12Device9> _device9;
//
//            };
//        }
//    }
//}
//
//
//#endif // DIRECTX12_DEVICE_H_
//
///************************************
//* End of file
//************************************/
