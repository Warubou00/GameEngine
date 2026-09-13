
/***********************************************************************************
* @file    dx12_resourceManager.cpp
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
* @note    
************************************************************************************/


/******************************************************************
* Include Headers
******************************************************************/
#include "dx12_resourceManager.h"

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
		// TextureResource作成
		ResourceHandle ResourceManager::CreateTexResource(ID3D12Device* device, const D3D12_HEAP_PROPERTIES& heapProp, const D3D12_RESOURCE_DESC& resDesc)
        {
			return _Create(_texResources, device, heapProp, resDesc);
        }

		// TextureResource取得
		ID3D12Resource* ResourceManager::GetTexResource(const ResourceHandle& handle) const
		{
			return _Get(_texResources, handle);
		}

		// TextureResource解放
		void ResourceManager::ReleaseTexResource(ResourceHandle& handle)
		{
			_Release(_texResources, handle);
		}

		// VertexResource作成
		ResourceHandle ResourceManager::CreateVertResource(ID3D12Device* device, const D3D12_HEAP_PROPERTIES& heapProp, const D3D12_RESOURCE_DESC& resDesc)
		{
			return _Create(_vertexResources, device, heapProp, resDesc);
		}

		// VertexResource取得
		ID3D12Resource* ResourceManager::GetVertResource(const ResourceHandle& handle) const
		{
			return _Get(_vertexResources, handle);
		}

		// VertexResource解放
		void ResourceManager::ReleaseVertResource(ResourceHandle& handle)
		{
			_Release(_vertexResources, handle);
		}

		// IndexResource取得
		ResourceHandle ResourceManager::CreateIndexResource(ID3D12Device* device, const D3D12_HEAP_PROPERTIES& heapProp, const D3D12_RESOURCE_DESC& resDesc)
		{
			return _Create(_indexResources, device, heapProp, resDesc);
		}
	
		// IndexResource解放
		ID3D12Resource* ResourceManager::GetIndexResource(const ResourceHandle& handle) const
		{
			return _Get(_indexResources, handle);
		}

		// IndexResource取得
		void ResourceManager::ReleaseIndexResource(ResourceHandle& handle)
		{
			_Release(_indexResources, handle);
		}

		// OtherResource作成
		ResourceHandle ResourceManager::CreateOtherResource(
			ID3D12Device* device,
			const D3D12_HEAP_PROPERTIES& heapProp,
			const D3D12_RESOURCE_DESC& resDesc,
			const ResourcePorperty& resourceProp)
		{
			return _Create(_otherResources, device, heapProp, resDesc, resourceProp);
		}

		// RTVリソースの登録
		void ResourceManager::RegistRtvResource(ID3D12Resource* resource)
		{
			_rtvResources.push_back(resource);
		}

		// RTVリソースの取得
		const std::vector < Microsoft::WRL::ComPtr<ID3D12Resource>> ResourceManager::GetRtvResources()const
		{
			return _rtvResources;
		}

		// OtherResource取得
		ID3D12Resource* ResourceManager::GetOtherResource(const ResourceHandle& handle) const
		{
			return _Get(_otherResources, handle);
		}

		// OtherResource解放
		void ResourceManager::ReleaseOtherResource(ResourceHandle& handle)
		{
			_Release(_otherResources, handle);
		}



		ResourceHandle ResourceManager::_Create(
			std::vector<Microsoft::WRL::ComPtr<ID3D12Resource>>& resources,
			ID3D12Device* device,
			const D3D12_HEAP_PROPERTIES& heapProp,
			const D3D12_RESOURCE_DESC& resDesc,
			const ResourcePorperty& resourceProp)
		{
			if (!device)
			{
				return ResourceHandle(nullptr, -1);
			}

			Microsoft::WRL::ComPtr<ID3D12Resource> pResource;
			HRESULT hr = device->CreateCommittedResource(
				&heapProp,
				D3D12_HEAP_FLAG_NONE,
				&resDesc,
				D3D12_RESOURCE_STATE_GENERIC_READ,
				nullptr,
				IID_PPV_ARGS(&pResource));

			resources.push_back(pResource);

			return ResourceHandle(pResource.Get(), resources.size() - 1);
		}


		ID3D12Resource* ResourceManager::_Get(const std::vector<Microsoft::WRL::ComPtr<ID3D12Resource>>& resources, const ResourceHandle& handle)const
		{
			int index = handle.GetIndex();

			if (resources.empty() || resources.size() <= index)
			{
				return nullptr;
			}

			return resources[index].Get();
		}

		void ResourceManager::_Release(std::vector<Microsoft::WRL::ComPtr<ID3D12Resource>>& resources, ResourceHandle& handle)
		{
			int index = handle.GetIndex();

			if (resources.empty() || resources.size() <= index)
			{
				return;
			}

			// 指定した箇所を削除
			handle.Clear();
			resources.erase(resources.begin() + index);
		}

	}
}

/************************************
* End of file
************************************/
