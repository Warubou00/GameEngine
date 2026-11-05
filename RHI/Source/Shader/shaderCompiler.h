
/***********************************************************************************
* @file    shaderCompiler.h
* @brief   シェーダーのコンパイル (DXC / FXC) とディスクキャッシュ
* @details
*          DXC (dxcompiler.dll)  : Shader Model 6.x (DXIL)。GPU が対応する範囲で最大 6.6 を使う
*                                  dxcompiler.dll は動的に読み込む (無い環境では FXC に戻す)
*          FXC (D3DCompile)      : Shader Model 5.1 (DXBC)。DXC が使えない時の予備
*                                  (以前はこちらだけを使っていた。Shader Model 6 以降の機能は使えない)
*
*          ディスクキャッシュ
*            コンパイル結果を ShaderCache/ に保存し、次回は読むだけにする (起動時間の短縮)
*            キー = ソースの中身 + エントリ名 + ターゲット + コンパイラ + ビルド構成 のハッシュ
*            → シェーダーを書き換えればキーが変わり、自動で作り直される
*            ※#include した別ファイルの変更は検出しない (今のシェーダーは #include を使っていない)
*
* @author  Keigo Onari
* @date    2026/11/02
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/

#pragma once
#ifndef RHI_SHADER_COMPILER_H_
#define RHI_SHADER_COMPILER_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <string>
#include <vector>
#include <cstdint>

#include "../DirectX12/dx12_header.h"

struct IDxcUtils;
struct IDxcCompiler3;
struct IDxcIncludeHandler;

namespace RHI
{
	namespace Shader
	{
		enum class Stage
		{
			Vertex,
			Pixel,
			Compute,    // GPU カリングなど
		};

		// コンパイル結果
		struct CompiledShader
		{
			std::vector<uint8_t> Bytecode;
			std::string          Target;            // 例: "vs_6_6" / "ps_5_1"
			bool                 FromCache = false; // ディスクキャッシュから読んだか

			D3D12_SHADER_BYTECODE View()const { return { Bytecode.data(), Bytecode.size() }; }
		};

		class ShaderCompiler
		{
		public:
			ShaderCompiler();
			~ShaderCompiler();

			ShaderCompiler(const ShaderCompiler&) = delete;
			ShaderCompiler& operator=(const ShaderCompiler&) = delete;

			/***********************************************************************************
			 * @brief 初期化 (GPU が対応する Shader Model を調べ、DXC を読み込む)
			 * @details DXC が無くても失敗にはしない (FXC で続ける)
			 ************************************************************************************/
			void Initialize(ID3D12Device* device);

			/***********************************************************************************
			 * @brief コンパイル (キャッシュがあれば読むだけ)
			 * @return bool 成功の有無 (失敗時はエラー内容をログに出す)
			 ************************************************************************************/
			bool Compile(const std::wstring& path, const std::string& entry, Stage stage, CompiledShader& out);

			/***********************************************************************************
			 * @brief 文字列のソースをコンパイルする (SH2 : 自分のシェーダー。キャッシュはソースのハッシュで引く)
			 * @details name はエラーの表示とキャッシュのファイル名に使う。DXC が無い時は失敗 (FXC の文字列のコンパイルは作っていない)
			 *          失敗時は error にコンパイラーの文 (行番号付き) を入れる
			 ************************************************************************************/
			bool CompileSource(const std::string& source, const std::wstring& name, const std::string& entry, Stage stage, CompiledShader& out, std::string* error);

			bool        IsDxcAvailable()const { return _compiler != nullptr; }
			const char* GetBackendName()const { return IsDxcAvailable() ? "DXC" : "FXC"; }

			// 例: "6_6" (DXC) / "5_1" (FXC)
			const std::string& GetShaderModel()const { return _shaderModel; }

		private:
			std::string targetFor(Stage stage)const;
			bool compileWithDxc(const std::string& source, const std::wstring& path, const std::string& entry, const std::string& target, std::vector<uint8_t>& out,
			                    std::string* error = nullptr);
			bool compileWithFxc(const std::wstring& path, const std::string& entry, const std::string& target, std::vector<uint8_t>& out);

			HMODULE             _dxcModule = nullptr;
			IDxcUtils*          _utils     = nullptr;   // 生ポインタで持つ (dxcapi.h をヘッダーに出さないため)
			IDxcCompiler3*      _compiler  = nullptr;
			IDxcIncludeHandler* _include   = nullptr;
			std::string         _shaderModel = "5_1";
		};
	}
}

#endif // RHI_SHADER_COMPILER_H_
