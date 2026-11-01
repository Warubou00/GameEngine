
/***********************************************************************************
* @file    dx12_graphicsCommandList.h
* @brief   DX12のグラフィックコマンドリスト
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

#pragma once
#ifndef DX12_GRAPHICS_COMMAND_LIST_H_
#define DX12_GRAPHICS_COMMAND_LIST_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "dx12_commandList.h"

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
		// 以前は private 継承になっていたため public に
		class GraphicsCommandList : public CommandList
		{
		public:
			GraphicsCommandList();
			~GraphicsCommandList() override;



			/***********************************************************************************
			 * @brief コマンドリストの作成
			 *
			 * @param[in] ID3D12Device* device デバイス
			 * @return HRESULT 成功の有無
			 ************************************************************************************/
			HRESULT Create(ID3D12Device* device, ID3D12CommandAllocator* commandAllocator) override;

			/***********************************************************************************
			 * @brief 種類を指定してコマンドリストを作成 (COPY / COMPUTE 用)
			 * @details COPY のコマンドリストでもインターフェイスは ID3D12GraphicsCommandList だが、
			 *          使えるのはコピー系の命令だけ (描画・バリアの多くは使えない)
			 ************************************************************************************/
			HRESULT Create(ID3D12Device* device, ID3D12CommandAllocator* commandAllocator, D3D12_COMMAND_LIST_TYPE type);

			/***********************************************************************************
			 * @brief 記録の開始 (コマンドリストのリセット)
			 * @details Close済みであること
			 *
			 * @param[in] ID3D12CommandAllocator* commandAllocator 記録先のアロケーター
			 * @param[in] ID3D12PipelineState* pso 初期PSO (nullptr可)
			 * @return HRESULT 成功の有無
			 ************************************************************************************/
			HRESULT Reset(ID3D12CommandAllocator* commandAllocator, ID3D12PipelineState* pso = nullptr);

			/***********************************************************************************
			 * @brief 記録の終了
			 * @details 記録中でなければ何もしない
			 *
			 * @return HRESULT 成功の有無 (記録したコマンドに不正があるとここで失敗する)
			 ************************************************************************************/
			HRESULT Close(void);

			bool IsRecording(void)const { return _isRecording; }

			/***********************************************************************************
			 * @brief コマンドリストのバージョンアップの試み(4,6,7)
			 *
			 * @return HRESULT 成功の有無
			 ************************************************************************************/
			HRESULT TryVersionUp();

			/***********************************************************************************
			 * @brief コマンドリストのバージョンアップの試み(4)
			 *
			 * @return HRESULT 成功の有無
			 ************************************************************************************/
			HRESULT TryVersionUp4();

			/***********************************************************************************
			 * @brief コマンドリストのバージョンアップの試み(6)
			 *
			 * @return HRESULT 成功の有無
			 ************************************************************************************/
			HRESULT TryVersionUp6();

			/***********************************************************************************
			 * @brief コマンドリストのバージョンアップの試み(7)
			 *
			 * @return HRESULT 成功の有無
			 ************************************************************************************/
			HRESULT TryVersionUp7();

			/***********************************************************************************
			 * @brief コマンドリストの取得
			 *
			 * @return ID3D12GraphicsCommandList* グラフィックコマンドリスト
			 ************************************************************************************/
			ID3D12GraphicsCommandList* GetCmdList(void)const;

			/***********************************************************************************
			 * @brief コマンドリストの取得
			 *
			 * @return ID3D12GraphicsCommandList4* グラフィックコマンドリスト4
			 ************************************************************************************/
			ID3D12GraphicsCommandList4* GetCmdList4(void)const;

			/***********************************************************************************
			 * @brief コマンドリストの取得
			 *
			 * @return ID3D12GraphicsCommandList6* グラフィックコマンドリスト6
			 ************************************************************************************/
			ID3D12GraphicsCommandList6* GetCmdList6(void)const;

			/***********************************************************************************
			 * @brief コマンドリストの取得
			 *
			 * @return ID3D12GraphicsCommandList7* グラフィックコマンドリスト7
			 ************************************************************************************/
			ID3D12GraphicsCommandList7* GetCmdList7(void)const;

			/***********************************************************************************
			 * @brief 解放関数
			 * @details 保持している参照を手放す
			 * 
			 ************************************************************************************/
			void Release(void) override;

		private:
			Microsoft::WRL::ComPtr<ID3D12GraphicsCommandList>  _pCmdList;
			Microsoft::WRL::ComPtr<ID3D12GraphicsCommandList4> _pCmdList4;
			Microsoft::WRL::ComPtr<ID3D12GraphicsCommandList6> _pCmdList6;
			Microsoft::WRL::ComPtr<ID3D12GraphicsCommandList7> _pCmdList7;
			bool                                               _isRecording = false;

			HRESULT asCmdList();
			HRESULT asCmdList4();
			HRESULT asCmdList6();
			HRESULT asCmdList7();
		};
	}
}

#endif // DX12_GRAPHICS_COMMAND_LIST_H_

/************************************
* End of file
************************************/