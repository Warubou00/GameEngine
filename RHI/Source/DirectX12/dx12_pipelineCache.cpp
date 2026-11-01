
/***********************************************************************************
* @file    dx12_pipelineCache.cpp
* @brief   PSO のディスクキャッシュ (ID3D12PipelineLibrary)
* @details
*
* @author  Keigo Onari
* @date    2026/11/02
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/


/******************************************************************
* Include Headers
******************************************************************/
#include "dx12_pipelineCache.h"

#include <fstream>
#include <iterator>

#include "../../../Core/Source/Log/logger.h"

namespace RHI
{
	namespace DirectX12
	{
		PipelineCache::PipelineCache() = default;
		PipelineCache::~PipelineCache()
		{
			Release();
		}

		void PipelineCache::Initialize(ID3D12Device* device, const std::wstring& filePath)
		{
			_device   = device;
			_filePath = filePath;
			_dirty    = false;

			Microsoft::WRL::ComPtr<ID3D12Device1> device1;
			if (!device || FAILED(device->QueryInterface(IID_PPV_ARGS(&device1))))
			{
				Core::Logger::Log("[PipelineCache] ID3D12Device1 is not available. PSO cache disabled.");
				return;
			}

			// 1. 保存したキャッシュを読む
			{
				std::ifstream file(filePath, std::ios::binary);
				if (file)
				{
					_fileData.assign(std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>());
				}
			}

			// 2. ライブラリを作る (ドライバーが変わっていたら失敗するので、その時は空から作り直す)
			HRESULT hr = E_FAIL;
			if (!_fileData.empty())
			{
				hr = device1->CreatePipelineLibrary(_fileData.data(), _fileData.size(), IID_PPV_ARGS(&_library));
				if (FAILED(hr))
				{
					char message[128] = {};
					sprintf_s(message, "[PipelineCache] Saved cache is not usable (HRESULT = 0x%08X). Rebuild.", static_cast<unsigned int>(hr));
					Core::Logger::Log(message);
					_fileData.clear();
				}
			}
			if (FAILED(hr))
			{
				hr = device1->CreatePipelineLibrary(nullptr, 0, IID_PPV_ARGS(&_library));
				if (FAILED(hr))
				{
					Core::Logger::Log("[PipelineCache] CreatePipelineLibrary failed. PSO cache disabled.");
					_library.Reset();
				}
			}
		}

		HRESULT PipelineCache::GetOrCreateGraphicsPipeline(
			const wchar_t* name,
			const D3D12_GRAPHICS_PIPELINE_STATE_DESC& desc,
			Microsoft::WRL::ComPtr<ID3D12PipelineState>& outPipeline,
			bool* outFromCache)
		{
			if (outFromCache) *outFromCache = false;
			if (!_device || !name) return E_INVALIDARG;

			// 1. キャッシュから読む (同じ名前で中身が違う desc を渡すと E_INVALIDARG になる)
			if (_library && SUCCEEDED(_library->LoadGraphicsPipeline(name, &desc, IID_PPV_ARGS(&outPipeline))))
			{
				if (outFromCache) *outFromCache = true;
				return S_OK;
			}

			// 2. 無ければ作って登録する
			RHI_RETURN_IF_FAILED(_device->CreateGraphicsPipelineState(&desc, IID_PPV_ARGS(&outPipeline)));
			if (_library)
			{
				const HRESULT hr = _library->StorePipeline(name, outPipeline.Get());
				if (SUCCEEDED(hr))
				{
					_dirty = true;
				}
				else
				{
					// 同じ名前が既にある (desc が違う) など。作った PSO はそのまま使える
					Core::Logger::Log(L"[PipelineCache] StorePipeline failed (name already used with a different desc?).");
					Core::Logger::Log(name);
				}
			}
			return S_OK;
		}

		HRESULT PipelineCache::GetOrCreateComputePipeline(
			const wchar_t* name,
			const D3D12_COMPUTE_PIPELINE_STATE_DESC& desc,
			Microsoft::WRL::ComPtr<ID3D12PipelineState>& outPipeline,
			bool* outFromCache)
		{
			if (outFromCache) *outFromCache = false;
			if (!_device || !name) return E_INVALIDARG;

			if (_library && SUCCEEDED(_library->LoadComputePipeline(name, &desc, IID_PPV_ARGS(&outPipeline))))
			{
				if (outFromCache) *outFromCache = true;
				return S_OK;
			}

			RHI_RETURN_IF_FAILED(_device->CreateComputePipelineState(&desc, IID_PPV_ARGS(&outPipeline)));
			if (_library)
			{
				if (SUCCEEDED(_library->StorePipeline(name, outPipeline.Get()))) _dirty = true;
				else
				{
					Core::Logger::Log(L"[PipelineCache] StorePipeline failed (name already used with a different desc?).");
					Core::Logger::Log(name);
				}
			}
			return S_OK;
		}

		void PipelineCache::Save()
		{
			if (!_library || !_dirty || _filePath.empty()) return;

			const SIZE_T size = _library->GetSerializedSize();
			std::vector<uint8_t> data(size);
			if (size == 0 || FAILED(_library->Serialize(data.data(), size)))
			{
				Core::Logger::Log("[PipelineCache] Serialize failed.");
				return;
			}

			std::ofstream file(_filePath, std::ios::binary | std::ios::trunc);
			if (!file)
			{
				Core::Logger::Log(L"[PipelineCache] Cannot write the cache file.");
				return;
			}
			file.write(reinterpret_cast<const char*>(data.data()), static_cast<std::streamsize>(data.size()));
			_dirty = false;
		}

		void PipelineCache::Release()
		{
			Save();
			_library.Reset();
			_fileData.clear();
			_device = nullptr;
		}
	}
}

/************************************
* End of file
************************************/
