
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
			// ExecuteIndirect で GPU に渡す、描画 1 回分の引数
			// コマンドシグネチャの引数の並び (Pipeline::makeIndirectDrawSignature) と同じ順番にすること
			// ※D3D12 の決まりで、ルート引数はルートパラメータの番号が小さい順に並べる必要がある
			//   1. 頂点の開始位置 (ルート定数 b0 の 2 個 : 2 個目は 64bit のアドレスを 8byte 境界にそろえるための詰め物) : 8byte
			//   2. オブジェクトの定数バッファのアドレス (Root CBV b2)                                                   : 8byte
			//   3. DrawIndexedInstanced の引数                                                                           : 20byte
			struct IndirectDrawCommand
			{
				UINT                         BaseVertex;
				UINT                         Padding;
				D3D12_GPU_VIRTUAL_ADDRESS    ObjectCbv;
				D3D12_DRAW_INDEXED_ARGUMENTS Draw;
			};
			static_assert(sizeof(IndirectDrawCommand) == 40, "IndirectDrawCommand must be 40 bytes");

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
