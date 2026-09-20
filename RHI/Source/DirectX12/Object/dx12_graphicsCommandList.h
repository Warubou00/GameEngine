
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
	namespace APIInterface
	{
		class IDevice;
		class IPipeline;
		class IDescriptorHeap;
		class IResourceBinding;
		class IResource;
		class ISwapChain;
	}
	
	namespace DirectX12
	{
		class Device;
		class CommandAllocator;

		class GraphicsCommandList : public CommandList
		{
		public:
			GraphicsCommandList();
			~GraphicsCommandList()override;

			/***********************************************************************************
			 * @brief コマンドリストの作成
			 *
			 * @param[in] RHI::DirectX12::Device* device デバイス
			 * @param[in] RHI::DirectX12::CommandAllocator* commandAllocator コマンドアロケータ
			 * @return HRESULT 成功の有無
			 ************************************************************************************/
			RHI_API_RESULT Initialize(RHI::APIInterface::IDevice* iDevice)override;

			/***********************************************************************************
			 * @brief レンダリング初期処理
			 * @details core explanation
			 *
			 * @param[in] IPipeline* パイプライン
			 * @return 無し
			 ************************************************************************************/
			void Begin(RHI::APIInterface::IPipeline* iPipeline)override;

			void ResourceBarriar(RHI::APIInterface::IResource* iResource, ResourceState before, ResourceState after)override;

			/***********************************************************************************
			 * @brief レンダリング
			 * @details core explanation
			 *
			 * @param[in] IPipeline*        パイプライン
			 * @param[in] IDescriptorHeap*  ディスクリプタヒープ
			 * @param[in] IResourceBinding* リソースバインディング
			 * @param[in] IResource*        リソース
			 * @param[in] ISwapChain*       スワップチェイン
			 * @return 無し
			 ************************************************************************************/
			void Render(
				RHI::APIInterface::IPipeline*        iPipeline,
				RHI::APIInterface::IDescriptorHeap*  iDescHeap,
				RHI::APIInterface::IResourceBinding* iResourceBinding,
				RHI::APIInterface::IResource*        iResource,
				RHI::APIInterface::ISwapChain*       iSwapChian
			)override;

			/***********************************************************************************
			 * @brief レンダリング修了処理
			 * @details core explanation
			 *
			 * @param[in]
			 * @return 無し
			 ************************************************************************************/
			void End()override;

			ID3D12CommandList* GetQueueList(void);

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
			 * @details ComPtrは解放しない
			 * 
			 ************************************************************************************/
			void Release(void)override;

		private:
			Microsoft::WRL::ComPtr<ID3D12GraphicsCommandList>  _pCmdList;
			Microsoft::WRL::ComPtr<ID3D12GraphicsCommandList4> _pCmdList4;
			Microsoft::WRL::ComPtr<ID3D12GraphicsCommandList6> _pCmdList6;
			Microsoft::WRL::ComPtr<ID3D12GraphicsCommandList7> _pCmdList7;
			Microsoft::WRL::ComPtr<ID3D12CommandAllocator>     _pCmdAllocator;

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