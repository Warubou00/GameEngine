
/***********************************************************************************
* @file    Device.cpp
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


/******************************************************************
* Include Headers
******************************************************************/
#include "Device.h"
#include <d3d12.h>

#include "../DirectX12/Object/dx12_device.h"
#include "../DXGI/dxgiFactory6.h"
#include "../adapter.h"

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
	namespace ObjectInterface
	{
		Device::Device() = default;
		Device::~Device() = default;

		RHI_RESULT Device::Initialize()
		{
			// オブジェクトの作成
			_dxgiFactory = std::make_unique<DirectX::DXGIFactory6>();
			_adapter     = std::make_unique<DirectX::Adapter>();
			_dx12Device  = std::make_unique<DirectX12::Device>();

			HRESULT hr   = _dxgiFactory->Create();
#if 0
			// 名前走査
			_adapter->EnumerateAdaptersFromGpuName(_dxgiFactory->Get(), L"NVIDIA");
#else
			// 高GPU走査
			_adapter->EnumerateAdapterFromHighPerformance(_dxgiFactory->Get());
#endif
			// TODO : hrの例外処理
			if (SUCCEEDED(_adapter->TryVersionUp3()))
				hr = _dx12Device->Create(_adapter->GetAdapter3());
			else
				hr = _dx12Device->Create(_adapter->GetAdapter());

			hr = _dx12Device->TryVersionUp();

#ifdef CONSOLE_DEBUG

			WCHAR* gpuName             = _adapter->GetAdapterName();
			std::string deviceVersion  = getDeviceVersion();
			std::string adapterVersion = getAdapterVersion();

			std::cout << "**************************************************" << std::endl;
			std::cout << "Succeeded Initialize Device From C++" << std::endl;
			std::cout << "Device  Name    : " << *gpuName << std::endl;
			std::cout << "Device  Version : " << deviceVersion << std::endl;
			std::cout << "Adapter Version : " << adapterVersion << std::endl;
			std::cout << "**************************************************" << std::endl;
#endif

			return RHI_SUCCEEDED;
		}

		IDXGIFactory6* Device::GetDxgiFactory6()const
		{
			return _dxgiFactory->Get();
		}

		ID3D12Device* Device::GetDx12Device()const
		{
			return _dx12Device->GetDevice();
		}

		ID3D12Device5* Device::GetDx12Device5()const
		{
			return _dx12Device->GetDevice5();
		}

		ID3D12Device9* Device::GetDx12Device9()const
		{
			return _dx12Device->GetDevice9();
		}

		std::string Device::getDeviceVersion()
		{
			std::string deviceVersion;

#ifdef CONSOLE_DEBUG
			if (_dx12Device->GetDevice5())
			{
				deviceVersion = "Device5";
				if (_dx12Device->GetDevice9())
				{
					deviceVersion = "Device9";
				}
			}
			else
			{
				deviceVersion = "Device No Mark";
			}
#endif

			return deviceVersion;
		}

		std::string Device::getAdapterVersion()
		{
			std::string adapterVersion;
#ifdef CONSOLE_DEBUG

			if (_adapter->GetAdapter1())
			{
				adapterVersion = "Adapter1";
				if (_adapter->GetAdapter3())
				{
					adapterVersion = "Adapter3";
				}
			}
			else
			{
				adapterVersion = "Adapter No Mark";
			}
#endif

			return adapterVersion;
		}
	}
}

/************************************
* End of file
************************************/
