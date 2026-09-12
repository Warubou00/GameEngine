
/***********************************************************************************
* @file    dx12_descriptorHandle.h
* @brief   DX12ディスクリプタハンドル
* @details 
*
* @author  Keigo Onari
* @date    2026/04/24
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    
************************************************************************************/

#pragma once
#ifndef DX12_DESCRIPTOR_HANDLE_H_
#define DX12_DESCRIPTOR_HANDLE_H_

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
        class DescriptorHandle
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            DescriptorHandle() : _cpu(D3D12_CPU_DESCRIPTOR_HANDLE()), _gpu(D3D12_GPU_DESCRIPTOR_HANDLE()) {};
            DescriptorHandle(D3D12_CPU_DESCRIPTOR_HANDLE cpu, D3D12_GPU_DESCRIPTOR_HANDLE gpu) { Make(cpu, gpu); }
            ~DescriptorHandle() {};

        public:          /*---クラスメソッド---*/

            void Make(D3D12_CPU_DESCRIPTOR_HANDLE cpu, D3D12_GPU_DESCRIPTOR_HANDLE gpu)
            {
                this->_cpu = cpu;
                this->_gpu = gpu;
            }

            const D3D12_CPU_DESCRIPTOR_HANDLE& GetCPUHandle(void)const
            {
                return _cpu;
            }

            const D3D12_GPU_DESCRIPTOR_HANDLE& GetGPUHandle(void)const
            {
                return _gpu;
            }

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/
            D3D12_CPU_DESCRIPTOR_HANDLE _cpu;
            D3D12_GPU_DESCRIPTOR_HANDLE _gpu;

        };
    }
}


#endif // DX12_DESCRIPTOR_HANDLE_H_

/************************************
* End of file
************************************/
