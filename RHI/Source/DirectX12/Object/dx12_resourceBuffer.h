
/***********************************************************************************
* @file    dx12_resourceBuffer.h
* @brief   リソースバッファー
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
#ifndef DX12_RESOURCE_BUFFER_H_
#define DX12_RESOURCE_BUFFER_H_

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
		class ResourceBuffer
		{
		public:
			ResourceBuffer();
			~ResourceBuffer();

			// Map中のポインタを所有するのでコピー禁止
			ResourceBuffer(const ResourceBuffer&) = delete;
			ResourceBuffer& operator=(const ResourceBuffer&) = delete;

			/***********************************************************************************
			 * @brief バッファーの生成
			 * @details CreateかCreateTextureしか呼べない
			 *
			 * @param[in] ID3D12Device* デバイス
			 * @param[in] UINT64 バッファーサイズ
			 * @param[in] D3D12_HEAP_TYPE ヒープのタイプ
			 * @return HRESULT 成功の有無
			 ************************************************************************************/
			// flags : コンピュートシェーダーから書き込むバッファは D3D12_RESOURCE_FLAG_ALLOW_UNORDERED_ACCESS
			HRESULT Create(ID3D12Device* device, UINT64 sizeInBytes, D3D12_HEAP_TYPE heapType, D3D12_RESOURCE_FLAGS flags = D3D12_RESOURCE_FLAG_NONE);

			/***********************************************************************************
			 * @brief テクスチャ用のバッファ作成
			 * @details CreateかCreateTextureしか呼べない
			 *
			 * @param[in] ID3D12Device* デバイス
			 * @param[in] UINT64 width 幅
			 * @param[in] UINT height 高さ
			 * @param[in] DXGI_FORMAT format フォーマット
			 * @param[in] UINT16 mipLevel ミップレベル
			 * @param[in] UINT16 arraySize サイズ
			 * @return HRESULT 成功の有無
			 ************************************************************************************/
			HRESULT CreateTexture(ID3D12Device* device, UINT64 width, UINT height, DXGI_FORMAT format, UINT16 mipLevels, UINT16 arraySize);

			/***********************************************************************************
			 * @brief 深度バッファの作成
			 * @details 初期状態は DEPTH_WRITE。最適化クリア値を指定するので、
			 *          ClearDepthStencilView には同じ値 (clearDepth) を渡すこと (違うと高速クリアが効かない)
			 *
			 * @param[in] ID3D12Device* デバイス
			 * @param[in] UINT64 width 幅
			 * @param[in] UINT height 高さ
			 * @param[in] DXGI_FORMAT format 深度フォーマット (例: DXGI_FORMAT_D32_FLOAT)
			 * @param[in] float clearDepth クリアする深度値
			 * @return HRESULT 成功の有無
			 ************************************************************************************/
			HRESULT CreateDepthStencil(ID3D12Device* device, UINT64 width, UINT height, DXGI_FORMAT format, float clearDepth);

			/***********************************************************************************
			 * @brief バッファーの登録
			 * @details Createと対照的に直接外部からのResourceを登録する (参照カウントは+1される)
			 *
			 * @param[in] ID3D12Resource* リソース
			 * @return 無し
			 ************************************************************************************/
			void Regist(ID3D12Resource* resource);

			ID3D12Resource* Get()const { return _resourceBuffer.Get(); }

			/***********************************************************************************
			 * @brief バッファーのGPU仮想アドレス (未生成なら0)
			 ************************************************************************************/
			D3D12_GPU_VIRTUAL_ADDRESS GetGPUVirtualAddress()const;

			/***********************************************************************************
			 * @brief バッファーのサイズ(byte)。テクスチャ/未生成なら0
			 ************************************************************************************/
			UINT64 GetCapacity()const { return _capacity; }

			/***********************************************************************************
			 * @brief CPUから書き込むためにMapする
			 * @details UPLOADヒープ以外ではMapできない。既にMap済みならS_OK
			 *
			 * @return HRESULT 成功の有無
			 ************************************************************************************/
			HRESULT Map();
			void Unmap();
			bool IsMapped()const { return _mappedData != nullptr; }

			// Map中の先頭アドレス (Mapしていなければ nullptr)。UploadManager のリングバッファで直接書き込むのに使う
			void* GetMappedData()const { return _mappedData; }

			/***********************************************************************************
			 * @brief 指定したオフセットにデータを書き込む
			 * @details Map済みであること。範囲外の書き込みは行わずfalseを返す
			 *
			 * @param[in] UINT64 offset 書き込み開始位置(byte)
			 * @param[in] const void* data データ
			 * @param[in] size_t dataSize データサイズ(byte)
			 * @return bool 書き込めたか
			 ************************************************************************************/
			bool WriteData(UINT64 offset, const void* data, size_t dataSize);

			void Release(void);

		private:
			Microsoft::WRL::ComPtr<ID3D12Resource> _resourceBuffer;
			UINT64 _capacity   = 0;
			void*  _mappedData = nullptr;
		};
	}
}

#endif // DX12_RESOURCE_BUFFER_H_

/************************************
* End of file
************************************/
