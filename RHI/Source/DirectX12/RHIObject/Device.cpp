
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

// DirectX12 APIのラッパーをインクルード
// ========================================
#include "../Object/dx12_device.h"
#include "../../DXGI/dxgiFactory6.h"
#include <string>
#include "../../DXGI/adapter.h"
// ========================================

// 外部クラスをインクルード
// ========================================
#include "../../Core/Source/Log/logger.h"
// ========================================


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
		namespace
		{
			// HRESULTをログに出す (失敗箇所を追えるように)
			void logFailed(const char* what, HRESULT hr)
			{
				char message[256] = {};
				sprintf_s(message, "[Device] %s failed. HRESULT = 0x%08X", what, static_cast<unsigned int>(hr));
				Core::Logger::Log(message);
			}

#if defined(_DEBUG)
			// デバッグレイヤーのメッセージを Logger (C#のコンソール) に流すコールバック
			// 以前は Visual Studio の出力ウィンドウでしか見えなかった
			void CALLBACK onDebugMessage(
				D3D12_MESSAGE_CATEGORY /*category*/,
				D3D12_MESSAGE_SEVERITY severity,
				D3D12_MESSAGE_ID       id,
				LPCSTR                 description,
				void*                  /*context*/)
			{
				// PSO キャッシュに無い名前を読もうとした時の警告は、初回起動では必ず出る想定どおりのものなので出さない
				if (id == D3D12_MESSAGE_ID_LOADPIPELINE_NAMENOTFOUND) return;

				// INFO / MESSAGE は量が多いので警告以上だけ出す
				const char* label = nullptr;
				switch (severity)
				{
				case D3D12_MESSAGE_SEVERITY_CORRUPTION: label = "[D3D12 CORRUPTION] "; break;
				case D3D12_MESSAGE_SEVERITY_ERROR:      label = "[D3D12 ERROR] ";      break;
				case D3D12_MESSAGE_SEVERITY_WARNING:    label = "[D3D12 WARNING] ";    break;
				default: return;
				}
				std::string message = std::string(label) + (description ? description : "");
				Core::Logger::Log(message.c_str());
			}
#endif
		}

		Device::Device() = default;
		Device::~Device()
		{
			Dispose();
		}

		RHI_RESULT Device::Initialize()
		{
			// オブジェクトの作成
			_dxgiFactory = std::make_unique<DXGI::DXGIFactory6>();
			_adapter     = std::make_unique<DXGI::Adapter>();
			_dx12Device  = std::make_unique<DirectX12::Device>();

			// 1. DXGIファクトリー
			// 以前は HRESULT を全て無視して常に成功を返していた (1-15)
			HRESULT hr = _dxgiFactory->Create();
			if (FAILED(hr)) { logFailed("CreateDXGIFactory", hr); return RHI_FAILED_INITIALIZED; }

			// 2. アダプター(GPU)の選択
#if 0
			// 名前走査
			hr = _adapter->EnumerateAdaptersFromGpuName(_dxgiFactory->Get(), L"NVIDIA");
#else
			// 高GPU走査
			hr = _adapter->EnumerateAdapterFromHighPerformance(_dxgiFactory->Get());
#endif
			if (FAILED(hr)) { logFailed("EnumerateAdapter (使えるGPUが見つからない)", hr); return RHI_FAILED_INITIALIZED; }

			// Adapter3 が取れなくてもデバイスは作れるので失敗扱いにはしない
			_adapter->TryVersionUp3();

			// 3. D3D12デバイス
			hr = _dx12Device->Create(_adapter->GetAdapter());
			if (FAILED(hr)) { logFailed("D3D12CreateDevice", hr); return RHI_FAILED_INITIALIZED; }

			// Device5 / Device9 は使える機能が増えるだけなので、取れなくても続行する
			_dx12Device->TryVersionUp();

#if defined(_DEBUG)
			// 4. デバッグメッセージの受け取り設定
			registerDebugMessageCallback();
#endif

#ifdef CONSOLE_DEBUG

			std::wstring gpuName       = _adapter->GetAdapterName();
			std::string deviceVersion  = getDeviceVersion();
			std::string adapterVersion = getAdapterVersion();

			Core::Logger::Log("**************************************************");
			Core::Logger::Log("Succeeded Initialize Device From C++");
			Core::Logger::Log((L"GPU : " + gpuName).c_str());
			std::cout << "Device  Version : " << deviceVersion << std::endl;
			std::cout << "Adapter Version : " << adapterVersion << std::endl;
			std::cout << "Raytracing      : " << (_dx12Device->IsRaytracingSupported() ? "Supported" : "Not Supported") << std::endl;
			std::cout << "Mesh Shader     : " << (_dx12Device->IsMeshShaderSupported() ? "Supported" : "Not Supported") << std::endl;
			Core::Logger::Log("**************************************************");
#endif

			return RHI_SUCCEEDED;
		}

		void Device::Dispose()
		{
			// コールバックの登録解除 → デバイス → アダプター → ファクトリーの順に破棄
			unregisterDebugMessageCallback();
			_dx12Device.reset();
			_adapter.reset();
			_dxgiFactory.reset();
		}

		void Device::registerDebugMessageCallback()
		{
#if defined(_DEBUG)
			if (!_dx12Device || !_dx12Device->GetDevice()) return;

			Microsoft::WRL::ComPtr<ID3D12InfoQueue> infoQueue;
			if (SUCCEEDED(_dx12Device->GetDevice()->QueryInterface(IID_PPV_ARGS(&infoQueue))))
			{
				// デバッガーが付いている時だけ、致命的なエラーで止める (付いていない時に止めるとクラッシュ扱いになる)
				if (IsDebuggerPresent())
				{
					infoQueue->SetBreakOnSeverity(D3D12_MESSAGE_SEVERITY_CORRUPTION, TRUE);
					infoQueue->SetBreakOnSeverity(D3D12_MESSAGE_SEVERITY_ERROR, TRUE);
				}
			}

			// ID3D12InfoQueue1 は比較的新しいランタイムでのみ使える。取れなければ何もしない
			Microsoft::WRL::ComPtr<ID3D12InfoQueue1> infoQueue1;
			if (SUCCEEDED(_dx12Device->GetDevice()->QueryInterface(IID_PPV_ARGS(&infoQueue1))) &&
				SUCCEEDED(infoQueue1->RegisterMessageCallback(onDebugMessage, D3D12_MESSAGE_CALLBACK_FLAG_NONE, nullptr, &_debugCallbackCookie)))
			{
				Core::Logger::Log("[Device] D3D12 debug messages -> Logger : enabled");
			}
			else
			{
				// ここに来た場合、D3D12 のエラーはコンソールに出ない (Visual Studio の出力ウィンドウには出る)
				Core::Logger::Log("[Device] D3D12 debug messages -> Logger : NOT available (ID3D12InfoQueue1 / debug layer missing)");
			}
#endif
		}

		void Device::unregisterDebugMessageCallback()
		{
#if defined(_DEBUG)
			if (_debugCallbackCookie == 0 || !_dx12Device || !_dx12Device->GetDevice()) return;

			Microsoft::WRL::ComPtr<ID3D12InfoQueue1> infoQueue1;
			if (SUCCEEDED(_dx12Device->GetDevice()->QueryInterface(IID_PPV_ARGS(&infoQueue1))))
			{
				infoQueue1->UnregisterMessageCallback(_debugCallbackCookie);
			}
#endif
			_debugCallbackCookie = 0;
		}

		DXGI::DXGIFactory6* Device::GetDxgiFactory()const
		{
			return _dxgiFactory.get();
		}

		DXGI::Adapter* Device::GetAdapter()const
		{
			return _adapter.get();
		}

		DirectX12::Device* Device::GetDevice()const
		{
			return _dx12Device.get();
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
