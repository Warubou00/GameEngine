
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
			// GPUに処理させる1回分の描画コマンドの構造体
			struct IndirectCommand
			{
				UINT32 ObjectID;                            // シェーダーに渡すオブジェクトID (4バイト)
				D3D12_DRAW_ARGUMENTS DrawArguments; // DrawInstancedの引数 (16バイト)
			};

		public:
			CommandSignature();
			~CommandSignature();

			/***********************************************************************************
			 * @brief コマンドシグネチャの生成
			 * @details ルート定数を書き換える引数を含む場合はrootSignatureが必須
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