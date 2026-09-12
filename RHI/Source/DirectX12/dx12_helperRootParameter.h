
/***********************************************************************************
* @file    dx12_helperRootParameter.h
* @brief   DX12ルートパラメーターヘルパー
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
#ifndef DX12_HELPER_ROOT_PARAMETER_H_
#define DX12_HELPER_ROOT_PARAMETER_H_

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
        struct HRootParameter : D3D12_ROOT_PARAMETER
        {
            enum class ShaderVisibilityType
            {
                ALL = 0,
                VS = 1,
                PS = 2,
                HULL = 3,
                DOMAIN = 4,
                GEOMETRY = 5,
                AMPLIFICATION = 6,
                MESH = 7
            };

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            HRootParameter(ShaderVisibilityType shaderVisibilityType, D3D12_DESCRIPTOR_RANGE* pDescriptorRange, UINT numDescriptorRange)
            {
                switch (shaderVisibilityType)
                {
                case ShaderVisibilityType::ALL:
                    ShaderVisibility = D3D12_SHADER_VISIBILITY_ALL;
                    break;
                case ShaderVisibilityType::VS:
                    ShaderVisibility = D3D12_SHADER_VISIBILITY_VERTEX;
                    break;
                case ShaderVisibilityType::PS:
                    ShaderVisibility = D3D12_SHADER_VISIBILITY_PIXEL;
                    break;
                case ShaderVisibilityType::HULL:
                    ShaderVisibility = D3D12_SHADER_VISIBILITY_HULL;
                    break;
                case ShaderVisibilityType::DOMAIN:
                    ShaderVisibility = D3D12_SHADER_VISIBILITY_DOMAIN;
                    break;
                case ShaderVisibilityType::GEOMETRY:
                    ShaderVisibility = D3D12_SHADER_VISIBILITY_GEOMETRY;
                    break;
                case ShaderVisibilityType::AMPLIFICATION:
                    ShaderVisibility = D3D12_SHADER_VISIBILITY_AMPLIFICATION;
                    break;
                case ShaderVisibilityType::MESH:
                    ShaderVisibility = D3D12_SHADER_VISIBILITY_MESH;
                    break;
                default:
                    break;
                }

                ParameterType = D3D12_ROOT_PARAMETER_TYPE_DESCRIPTOR_TABLE;
                DescriptorTable.pDescriptorRanges = pDescriptorRange;
                DescriptorTable.NumDescriptorRanges = numDescriptorRange;

            }
            ~HRootParameter() {}

        public:          /*---クラスメソッド---*/


        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/

        };
    }
}


#endif // DX12_HELPER_ROOT_PARAMETER_H_

/************************************
* End of file
************************************/
