
/***********************************************************************************
* @file    dx12_resourceManager.h
* @brief   DX12のリソース括管理
* @details 
*
* @author  Keigo Onari
* @date    2026/04/24
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    ComPtrなのでRelease後、他のComPtrからの参照が無い場合削除される。
           生ポインタで持っているところがあるとダングリングポインタとなるので注意
************************************************************************************/

#pragma once
#ifndef DX12_RESOURCE_MANAGER_H_
#define DX12_RESOURCE_MANAGER_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "dx12_header.h"
#include <vector>
#include <string>
#include "dx12_resourceHandle.h"

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
        class ResourceManager
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/
        public:
            struct ResourcePorperty
            {
                ResourcePorperty() :heapFrag(D3D12_HEAP_FLAG_NONE), resourceStates(D3D12_RESOURCE_STATE_GENERIC_READ) {}
                D3D12_HEAP_FLAGS heapFrag;
                D3D12_RESOURCE_STATES resourceStates;
            };

        private:          /*---コンストラクタ・デストラクタ---*/
            ResourceManager() {};
            ~ResourceManager() {};
            ResourceManager(const ResourceManager&) = delete;
            ResourceManager& operator=(const ResourceManager) = delete;

        public:          /*---クラスメソッド---*/

            static ResourceManager& getInstance()
            {
                static ResourceManager instance;
                return instance;
            }

            /***********************************************************************************
             * @brief 生成関数
             * @details 
             *
             * @param[in] IDXGIFactory6* pDxgiFactory DXGIFactory6のポインタ
             * @param[in] ID3D12CommandQueue* pCmdQueue コマンドキュー
             * @param[in] HWND* hWnd ウインドウハンドルのポインタ
             * @param[in] UINT width 幅
             * @param[in] UINT height 高さ
             * @return HRESULT 成功の有無
             ************************************************************************************/
            ResourceHandle CreateTexResource(ID3D12Device* device, const D3D12_HEAP_PROPERTIES& heapProp, const D3D12_RESOURCE_DESC& resDesc);
            ID3D12Resource* GetTexResource(const ResourceHandle& handle) const;
            void ReleaseTexResource(ResourceHandle& handle);
            
            ResourceHandle CreateVertResource(ID3D12Device* device, const D3D12_HEAP_PROPERTIES& heapProp, const D3D12_RESOURCE_DESC& resDesc);
            ID3D12Resource* GetVertResource(const ResourceHandle& handle) const;
            void ReleaseVertResource(ResourceHandle& handle);

            ResourceHandle CreateIndexResource(ID3D12Device* device, const D3D12_HEAP_PROPERTIES& heapProp, const D3D12_RESOURCE_DESC& resDesc);
            ID3D12Resource* GetIndexResource(const ResourceHandle& handle) const;
            void ReleaseIndexResource(ResourceHandle& handle);

            ResourceHandle CreateOtherResource(
                ID3D12Device* device,
                const D3D12_HEAP_PROPERTIES& heapProp,
                const D3D12_RESOURCE_DESC& resDesc,
                const ResourcePorperty& resourceProp);
            ID3D12Resource* GetOtherResource(const ResourceHandle& handle) const;
            void ReleaseOtherResource(ResourceHandle& handle);

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/

            std::vector<Microsoft::WRL::ComPtr<ID3D12Resource>> _texResources;
            std::vector<Microsoft::WRL::ComPtr<ID3D12Resource>> _vertexResources;
            std::vector<Microsoft::WRL::ComPtr<ID3D12Resource>> _indexResources;
            std::vector<Microsoft::WRL::ComPtr<ID3D12Resource>> _otherResources;

        private:         /*---メンバ関数(非公開)---*/
            ResourceHandle  _Create( const std::vector<Microsoft::WRL::ComPtr<ID3D12Resource>>& resources, ID3D12Device* device, const D3D12_HEAP_PROPERTIES& heapProp, const D3D12_RESOURCE_DESC& resDesc, const ResourcePorperty& resourceProp = ResourcePorperty());
            ID3D12Resource* _Get(    const std::vector<Microsoft::WRL::ComPtr<ID3D12Resource>>& resources, const ResourceHandle& handle)const;
            void            _Release(      std::vector<Microsoft::WRL::ComPtr<ID3D12Resource>>& resources, ResourceHandle& handle);
        };
    }
}


#endif // DX12_RESOURCE_MANAGER_H_

/************************************
* End of file
************************************/
