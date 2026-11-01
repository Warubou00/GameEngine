
/***********************************************************************************
* @file    dx12_pipelineCache.h
* @brief   PSO のディスクキャッシュ (ID3D12PipelineLibrary)
* @details PSO の作成ではドライバーがシェーダーを GPU 用の命令に変換するため時間がかかる。
*          作った PSO をドライバーの形式のままファイルに保存しておき、次回はそこから読み込む。
*
*            初回     : LoadGraphicsPipeline → 無い → 作成 → ライブラリに登録 (終了時にファイルへ保存)
*            2 回目以降: LoadGraphicsPipeline → ある → そのまま使う (変換が不要)
*
*          ドライバーを更新したり別の GPU で動かすと、保存したものは使えない
*          (CreatePipelineLibrary が失敗する) ので、その時は空のライブラリから作り直す。
*
* @author  Keigo Onari
* @date    2026/11/02
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
************************************************************************************/

#pragma once
#ifndef DX12_PIPELINE_CACHE_H_
#define DX12_PIPELINE_CACHE_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <string>
#include <vector>

#include "dx12_header.h"

namespace RHI
{
	namespace DirectX12
	{
		class PipelineCache
		{
		public:
			PipelineCache();
			~PipelineCache();

			PipelineCache(const PipelineCache&) = delete;
			PipelineCache& operator=(const PipelineCache&) = delete;

			/***********************************************************************************
			 * @brief ファイルからキャッシュを読み込む (無い / 使えない時は空で始める)
			 * @details ID3D12Device1 が無い環境では何もしない (毎回 PSO を作るだけ)
			 ************************************************************************************/
			void Initialize(ID3D12Device* device, const std::wstring& filePath);

			/***********************************************************************************
			 * @brief キャッシュにあれば読み込み、無ければ作成して登録する
			 *
			 * @param[in] const wchar_t* name PSO の名前 (シェーダーや設定が変わったら別の名前にすること)
			 * @param[out] bool* outFromCache キャッシュから読んだか (nullptr 可)
			 ************************************************************************************/
			HRESULT GetOrCreateGraphicsPipeline(
				const wchar_t* name,
				const D3D12_GRAPHICS_PIPELINE_STATE_DESC& desc,
				Microsoft::WRL::ComPtr<ID3D12PipelineState>& outPipeline,
				bool* outFromCache = nullptr);

			// 新しく登録したものがあればファイルに保存する (終了時に呼ぶ)
			void Save();

			void Release();

		private:
			ID3D12Device*                             _device = nullptr;   // 所有しない
			Microsoft::WRL::ComPtr<ID3D12PipelineLibrary> _library;
			std::vector<uint8_t>                      _fileData;   // ライブラリはこのメモリを参照し続けるので、生存中は保持する
			std::wstring                              _filePath;
			bool                                      _dirty = false;
		};
	}
}

#endif // DX12_PIPELINE_CACHE_H_
