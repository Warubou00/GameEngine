
/***********************************************************************************
* @file    dx12_commandSignature.cpp
* @brief   DX12のCS用コマンドシグネチャ
* @details
*
* @author  Keigo Onari
* @date    2026/02/02
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
#include "dx12_commandSignature.h"

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
		CommandSignature::CommandSignature() {}
		CommandSignature::~CommandSignature() {}

		// ==============================================
		// コマンドリストの作成
		// ==============================================
		HRESULT CommandSignature::Create(ID3D12Device* device, ID3D12RootSignature* rootSignature, const D3D12_COMMAND_SIGNATURE_DESC& commandSignatureDesc)
		{
			if (!device || _cmdSignature)
			{
				return E_INVALIDARG;
			}

			//D3D12_INDIRECT_ARGUMENT_DESC argumentDescs[2] = {};

			//// 1つ目: 構造体の先頭にある ObjectID を RootParameter[0] にセットする指示
			//argumentDescs[0].Type = D3D12_INDIRECT_ARGUMENT_TYPE_CONSTANT;
			//argumentDescs[0].Constant.RootParameterIndex = 0; // ルートシグネチャのパラメータ番号
			//argumentDescs[0].Constant.Num32BitValuesToSet = 1;
			//argumentDescs[0].Constant.DestOffsetIn32BitValues = 0;

			//// 2つ目: 構造体の後半にある DrawArguments を使って描画を行う指示
			//argumentDescs[1].Type = D3D12_INDIRECT_ARGUMENT_TYPE_DRAW;

			//D3D12_COMMAND_SIGNATURE_DESC commandSignatureDesc = {};
			//commandSignatureDesc.pArgumentDescs = argumentDescs;
			//commandSignatureDesc.NumArgumentDescs = 2;
			//commandSignatureDesc.ByteStride = sizeof(IndirectCommand); // 20バイト (4の倍数である必要があります)

			// 定数バッファ(Root Constant)を更新する指示が含まれるため、使用するRootSignatureも渡す必要があります
			HRESULT hr = device->CreateCommandSignature(
				&commandSignatureDesc,
				rootSignature,
				IID_PPV_ARGS(&_cmdSignature)
			);

			return hr;
		}



		ID3D12CommandSignature* CommandSignature::Get(void)const
		{
			return _cmdSignature.Get();
		}

		// ==============================================
		// 解放
		// ==============================================
		void CommandSignature::Release(void)
		{
			_cmdSignature.Reset();
		}
	}
}

/************************************
* End of file
************************************/