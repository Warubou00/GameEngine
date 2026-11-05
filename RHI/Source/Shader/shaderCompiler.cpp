
/***********************************************************************************
* @file    shaderCompiler.cpp
* @brief   シェーダーのコンパイル (DXC / FXC) とディスクキャッシュ
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
#include "shaderCompiler.h"
#include "hlslCompiler.h"

#include <dxcapi.h>
#include <fstream>
#include <iterator>

#include "../../../Core/Source/Log/logger.h"

namespace RHI
{
	namespace Shader
	{
		namespace
		{
			constexpr wchar_t CACHE_DIR[] = L"ShaderCache";

#if defined(_DEBUG)
			constexpr char BUILD_TAG[] = "debug";
#else
			constexpr char BUILD_TAG[] = "release";
#endif

			// FNV-1a (64bit) : キャッシュのキー用。暗号用ではなく「変わったかどうか」が分かれば十分
			uint64_t fnv1a(const void* data, size_t size, uint64_t hash = 14695981039346656037ull)
			{
				const uint8_t* p = static_cast<const uint8_t*>(data);
				for (size_t i = 0; i < size; ++i)
				{
					hash ^= p[i];
					hash *= 1099511628211ull;
				}
				return hash;
			}

			bool readFile(const std::wstring& path, std::string& out)
			{
				std::ifstream file(path, std::ios::binary);
				if (!file) return false;
				out.assign(std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>());
				return true;
			}

			bool readBinary(const std::wstring& path, std::vector<uint8_t>& out)
			{
				std::ifstream file(path, std::ios::binary);
				if (!file) return false;
				out.assign(std::istreambuf_iterator<char>(file), std::istreambuf_iterator<char>());
				return !out.empty();
			}

			void writeBinary(const std::wstring& path, const std::vector<uint8_t>& data)
			{
				CreateDirectoryW(CACHE_DIR, nullptr);   // 既にあれば何もしない
				std::ofstream file(path, std::ios::binary | std::ios::trunc);
				if (file) file.write(reinterpret_cast<const char*>(data.data()), static_cast<std::streamsize>(data.size()));
			}

			// "../Shader/hlslTemplate.shader" → "hlslTemplate"
			std::wstring fileStem(const std::wstring& path)
			{
				size_t begin = path.find_last_of(L"/\\");
				begin = (begin == std::wstring::npos) ? 0 : begin + 1;
				size_t end = path.find_last_of(L'.');
				if (end == std::wstring::npos || end < begin) end = path.size();
				return path.substr(begin, end - begin);
			}

			std::wstring toWide(const std::string& s)
			{
				return std::wstring(s.begin(), s.end());   // ASCII のみ (エントリ名 / ターゲット名)
			}

			void logError(const char* backend, const std::string& entry, const std::string& target, const char* detail)
			{
				char message[256] = {};
				sprintf_s(message, "[Shader] %s failed to compile (%s / %s)", backend, entry.c_str(), target.c_str());
				Core::Logger::Log(message);
				if (detail && detail[0]) Core::Logger::Log(detail);
			}
		}

		ShaderCompiler::ShaderCompiler() = default;
		ShaderCompiler::~ShaderCompiler()
		{
			if (_include)  _include->Release();
			if (_compiler) _compiler->Release();
			if (_utils)    _utils->Release();
			if (_dxcModule) FreeLibrary(_dxcModule);
		}

		void ShaderCompiler::Initialize(ID3D12Device* device)
		{
			// 1. GPU が対応する一番新しい Shader Model を調べる (6.6 まで)
			//    古いランタイムは知らない値を渡すと E_INVALIDARG を返すので、下げながら試す
			D3D_SHADER_MODEL highest = D3D_SHADER_MODEL_5_1;
			const D3D_SHADER_MODEL candidates[] = { D3D_SHADER_MODEL_6_6, D3D_SHADER_MODEL_6_5, D3D_SHADER_MODEL_6_0 };
			for (auto candidate : candidates)
			{
				D3D12_FEATURE_DATA_SHADER_MODEL sm = { candidate };
				if (device && SUCCEEDED(device->CheckFeatureSupport(D3D12_FEATURE_SHADER_MODEL, &sm, sizeof(sm))))
				{
					highest = sm.HighestShaderModel;
					break;
				}
			}

			// 2. DXC を動的に読み込む (無ければ FXC で続ける)
			//    dxcompiler.dll と dxil.dll (署名用) は実行ファイルと同じフォルダに置く (csproj がコピーする)
			if (highest >= D3D_SHADER_MODEL_6_0)
			{
				// どこで失敗したかをログに出す (DLL が無い / 関数が無い / インスタンスが作れない)
				const char* failedStep = nullptr;
				HRESULT     failedHr   = S_OK;

				_dxcModule = LoadLibraryW(L"dxcompiler.dll");
				auto createInstance = _dxcModule ? reinterpret_cast<DxcCreateInstanceProc>(GetProcAddress(_dxcModule, "DxcCreateInstance")) : nullptr;
				if (!_dxcModule)                                                                        { failedStep = "LoadLibrary(dxcompiler.dll)"; failedHr = HRESULT_FROM_WIN32(GetLastError()); }
				else if (!createInstance)                                                               { failedStep = "GetProcAddress(DxcCreateInstance)"; failedHr = HRESULT_FROM_WIN32(GetLastError()); }
				else if (FAILED(failedHr = createInstance(CLSID_DxcUtils, IID_PPV_ARGS(&_utils))))       { failedStep = "DxcCreateInstance(DxcUtils)"; }
				else if (FAILED(failedHr = createInstance(CLSID_DxcCompiler, IID_PPV_ARGS(&_compiler)))) { failedStep = "DxcCreateInstance(DxcCompiler)"; }
				else if (FAILED(failedHr = _utils->CreateDefaultIncludeHandler(&_include)))               { failedStep = "CreateDefaultIncludeHandler"; }

				if (!failedStep)
				{
					const int major = (highest >> 4) & 0xF;
					const int minor = highest & 0xF;
					_shaderModel = std::to_string(major) + "_" + std::to_string(minor);
				}
				else
				{
					if (_include)  { _include->Release();  _include  = nullptr; }
					if (_compiler) { _compiler->Release(); _compiler = nullptr; }
					if (_utils)    { _utils->Release();    _utils    = nullptr; }
					char message[192] = {};
					sprintf_s(message, "[Shader] DXC is not available (%s failed, HRESULT = 0x%08X). Fall back to FXC (Shader Model 5.1).",
						failedStep, static_cast<unsigned int>(failedHr));
					Core::Logger::Log(message);
				}
			}

			char message[96] = {};
			sprintf_s(message, "[Shader] Compiler : %s / Shader Model %s", GetBackendName(), _shaderModel.c_str());
			Core::Logger::Log(message);
		}

		std::string ShaderCompiler::targetFor(Stage stage)const
		{
			const char* prefix = stage == Stage::Vertex ? "vs_" : stage == Stage::Pixel ? "ps_" : "cs_";
			return std::string(prefix) + _shaderModel;
		}

		bool ShaderCompiler::Compile(const std::wstring& path, const std::string& entry, Stage stage, CompiledShader& out)
		{
			out = CompiledShader();
			out.Target = targetFor(stage);

			std::string source;
			if (!readFile(path, source))
			{
				Core::Logger::Log(L"[Shader] Shader file not found.");
				Core::Logger::Log(path.c_str());
				return false;
			}

			// キャッシュのキー
			uint64_t hash = fnv1a(source.data(), source.size());
			hash = fnv1a(entry.data(), entry.size(), hash);
			hash = fnv1a(out.Target.data(), out.Target.size(), hash);
			hash = fnv1a(GetBackendName(), strlen(GetBackendName()), hash);
			hash = fnv1a(BUILD_TAG, sizeof(BUILD_TAG), hash);

			wchar_t cachePath[MAX_PATH] = {};
			swprintf_s(cachePath, L"%s\\%s_%s_%s_%016llx.bin",
				CACHE_DIR, fileStem(path).c_str(), toWide(entry).c_str(), toWide(out.Target).c_str(), hash);

			// 1. キャッシュがあれば読むだけ
			if (readBinary(cachePath, out.Bytecode))
			{
				out.FromCache = true;
				return true;
			}

			// 2. コンパイルしてキャッシュに保存
			const bool compiled = IsDxcAvailable()
				? compileWithDxc(source, path, entry, out.Target, out.Bytecode)
				: compileWithFxc(path, entry, out.Target, out.Bytecode);
			if (!compiled)
			{
				return false;
			}
			writeBinary(cachePath, out.Bytecode);
			return true;
		}

		bool ShaderCompiler::CompileSource(const std::string& source, const std::wstring& name, const std::string& entry, Stage stage, CompiledShader& out, std::string* error)
		{
			out = CompiledShader();
			out.Target = targetFor(stage);
			if (!IsDxcAvailable())
			{
				if (error) *error = "Custom shaders need DXC (dxcompiler.dll)";
				return false;
			}
			// キャッシュのキー (Compile と同じ作り方。ファイルの代わりにソースの文字列)
			uint64_t hash = fnv1a(source.data(), source.size());
			hash = fnv1a(entry.data(), entry.size(), hash);
			hash = fnv1a(out.Target.data(), out.Target.size(), hash);
			hash = fnv1a(GetBackendName(), strlen(GetBackendName()), hash);
			hash = fnv1a(BUILD_TAG, sizeof(BUILD_TAG), hash);
			wchar_t cachePath[MAX_PATH] = {};
			swprintf_s(cachePath, L"%s\\custom_%s_%s_%016llx.bin", CACHE_DIR, toWide(entry).c_str(), toWide(out.Target).c_str(), hash);
			if (readBinary(cachePath, out.Bytecode))
			{
				out.FromCache = true;
				return true;
			}
			if (!compileWithDxc(source, name, entry, out.Target, out.Bytecode, error)) return false;
			writeBinary(cachePath, out.Bytecode);
			return true;
		}

		bool ShaderCompiler::compileWithDxc(const std::string& source, const std::wstring& path, const std::string& entry, const std::string& target, std::vector<uint8_t>& out,
		                                    std::string* error)
		{
			const std::wstring wEntry  = toWide(entry);
			const std::wstring wTarget = toWide(target);

			std::vector<LPCWSTR> args = {
				path.c_str(),                 // エラー表示用のファイル名
				L"-E", wEntry.c_str(),
				L"-T", wTarget.c_str(),
#if defined(_DEBUG)
				L"-Od",                       // 最適化なし (PIX などでデバッグしやすく)
				L"-Zi", L"-Qembed_debug",     // デバッグ情報をシェーダーに埋め込む
#else
				L"-O3",
#endif
			};

			DxcBuffer buffer = {};
			buffer.Ptr      = source.data();
			buffer.Size     = source.size();
			buffer.Encoding = DXC_CP_UTF8;   // シェーダーファイルは UTF-8

			Microsoft::WRL::ComPtr<IDxcResult> result;
			HRESULT hr = _compiler->Compile(&buffer, args.data(), static_cast<UINT32>(args.size()), _include, IID_PPV_ARGS(&result));

			// 警告 / エラーの文字列
			Microsoft::WRL::ComPtr<IDxcBlobUtf8> errors;
			if (result) result->GetOutput(DXC_OUT_ERRORS, IID_PPV_ARGS(&errors), nullptr);
			const char* errorText = (errors && errors->GetStringLength() > 0) ? errors->GetStringPointer() : "";

			if (SUCCEEDED(hr) && result) result->GetStatus(&hr);
			if (FAILED(hr))
			{
				logError("DXC", entry, target, errorText);
				if (error) *error = errorText[0] ? errorText : "DXC failed";
				return false;
			}
			if (errorText[0]) Core::Logger::Log(errorText);   // 成功時の警告

			Microsoft::WRL::ComPtr<IDxcBlob> object;
			if (FAILED(result->GetOutput(DXC_OUT_OBJECT, IID_PPV_ARGS(&object), nullptr)) || !object)
			{
				logError("DXC", entry, target, "no object");
				return false;
			}
			const uint8_t* data = static_cast<const uint8_t*>(object->GetBufferPointer());
			out.assign(data, data + object->GetBufferSize());
			return true;
		}

		bool ShaderCompiler::compileWithFxc(const std::wstring& path, const std::string& entry, const std::string& target, std::vector<uint8_t>& out)
		{
			HLSLCompiler fxc;
			Microsoft::WRL::ComPtr<ID3DBlob> blob, errors;
			const HRESULT hr = fxc.CompileShader(path, entry, target, blob.GetAddressOf(), errors.GetAddressOf());

			const std::string errorText = (errors && errors->GetBufferSize() > 0)
				? std::string(static_cast<const char*>(errors->GetBufferPointer()), errors->GetBufferSize()) : std::string();
			if (FAILED(hr) || !blob)
			{
				logError("FXC", entry, target, errorText.c_str());
				return false;
			}
			if (!errorText.empty()) Core::Logger::Log(errorText.c_str());

			const uint8_t* data = static_cast<const uint8_t*>(blob->GetBufferPointer());
			out.assign(data, data + blob->GetBufferSize());
			return true;
		}
	}
}

/************************************
* End of file
************************************/
