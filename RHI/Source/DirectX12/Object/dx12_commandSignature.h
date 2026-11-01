
/***********************************************************************************
* @file    dx12_commandSignature.h
* @brief   DX12のCS用コマンドシグネチャ
* @details
*
* @author  Keigo Onari
* @date    2026/09/23
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note
************************************************************************************/

#pragma once
#ifndef DX12_COMMAND_SIGNATURE_H_
#define DX12_COMMAND_SIGNATURE_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "../dx12_header.h"

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
		class CommandSignature
		{
		public:
			// ExecuteIndirect で GPU に渡す、描画 1 回分の引数 (DrawIndexedInstanced の引数だけ : 20byte)
			// コマンドシグネチャの引数の並び (Pipeline::makeIndirectDrawSignature) と同じにすること
			// 以前は [頂点の開始位置 (ルート定数 ×2)][オブジェクトの Root CBV][描画] の 40byte だった。
			// 描画ごとの情報は StartInstanceLocation = 描画番号 でシェーダーに渡す (ルート引数を切り替えないので GPU が速い)
			struct IndirectDrawCommand
			{
				D3D12_DRAW_INDEXED_ARGUMENTS Draw;
			};
			static_assert(sizeof(IndirectDrawCommand) == 20, "IndirectDrawCommand must be 20 bytes");

		public:
			CommandSignature();
			~CommandSignature();

			/***********************************************************************************
			 * @brief コマンドシグネチャの生成
			 * @details ルート引数を書き換える引数を含む場合は rootSignature が必須。含まない場合は nullptr にすること
			 *
			 * @param[in] ID3D12Device* デバイス
			 * @param[in] ID3D12RootSignature* rootSignature ルートシグネチャ
			 * @param[in] const D3D12_COMMAND_SIGNATURE_DESC& commandSignatureDesc 設定情報
			 * @return HRESULT 成功の有無
			 ************************************************************************************/
			HRESULT Create(ID3D12Device* device, ID3D12RootSignature* rootSignature, const D3D12_COMMAND_SIGNATURE_DESC& commandSignatureDesc);


			/***********************************************************************************
			 * @brief 解放関数
			 * @details 保持している参照を手放す
			 *
			 ************************************************************************************/
			ID3D12CommandSignature* Get(void)const;

			void Release(void);

		private:
			Microsoft::WRL::ComPtr<ID3D12CommandSignature> _cmdSignature;
		};
	}
}

#endif // DX12_COMMAND_SIGNATURE_H_

/************************************
* End of file
************************************/
