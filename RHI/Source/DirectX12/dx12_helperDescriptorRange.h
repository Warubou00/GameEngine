
/***********************************************************************************
* @file    dx12_helperDescriptorRange.h
* @brief   DX12ディスクリプタレンジヘルパー
* @details 
*
* @author  Keigo Onari
* @date    2026/04/26
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    
************************************************************************************/

#pragma once
#ifndef DX12_HELPER_DESCRIPTOR_RANGE_H_
#define DX12_HELPER_DESCRIPTOR_RANGE_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "dx12_header.h"

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
        struct HDescriptorRange : D3D12_DESCRIPTOR_RANGE
        {
            enum class TypeRange
            {
                SRV     = 0,
                CBV     = 1,
                UAV     = 2,
                SAMPLER = 3
            };

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            HDescriptorRange(TypeRange rangeType, UINT numDescriptor, UINT baseShaderRegister)
            {
                switch (rangeType)
                {
                case TypeRange::SRV:
                    RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_SRV;
                    break;
                case TypeRange::CBV:
                    RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_CBV;
                    break;
                case TypeRange::UAV:
                    RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_UAV;
                    break;
                case TypeRange::SAMPLER:
                    RangeType = D3D12_DESCRIPTOR_RANGE_TYPE_SAMPLER;
                    break;
                }
                NumDescriptors = numDescriptor;
                BaseShaderRegister = baseShaderRegister;
                OffsetInDescriptorsFromTableStart = D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND;
                RegisterSpace = 0;
            }
            ~HDescriptorRange() {}

        public:          /*---クラスメソッド---*/


        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/

        };
    }
}


#endif // DX12_HELPER_DESCRIPTOR_RANGE_H_

/************************************
* End of file
************************************/
