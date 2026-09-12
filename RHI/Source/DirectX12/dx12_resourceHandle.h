
/***********************************************************************************
* @file    dx12_resourceHandle.h
* @brief   DX12リソースハンドル
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
#ifndef DX12_RESOURCE_HANDLE_H_
#define DX12_RESOURCE_HANDLE_H_

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
        class ResourceHandle
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            ResourceHandle() : _resource(nullptr), _index(-1) {};
            ResourceHandle(ID3D12Resource* resource, int index) { Make(resource, index); }
            ~ResourceHandle() {};

        public:          /*---クラスメソッド---*/

            void Make(ID3D12Resource* resource, int index)
            {
                this->_resource = resource;
                this->_index = index;
            }

            ID3D12Resource* GetResource(void)const
            {
                return _resource;
            }

            int GetIndex(void)const
            {
                return _index;
            }

            void Clear(void)
            {
                _resource = nullptr;
                _index = -1;
            }

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/

            ID3D12Resource* _resource;
            int _index;

        };
    }
}


#endif // DX12_RESOURCE_HANDLE_H_

/************************************
* End of file
************************************/
