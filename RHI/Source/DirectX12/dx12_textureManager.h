
/***********************************************************************************
* @file    dx12_textureManager.h
* @brief   DX12のテクスチャ括管理
* @details 
*
* @author  Keigo Onari
* @date    2026/04/24
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    
************************************************************************************/

#pragma once
#ifndef DX12_TEXTURE_MANAGER_H_
#define DX12_TEXTURE_MANAGER_H_

/******************************************************************
* Include Headers
******************************************************************/
#include "dx12_header.h"
#include <vector>
#include <string>
#include <memory>
#include <DirectXTex.h>

#include "../TextureHandle.h"

#pragma comment(lib, "DirectXTex.lib")

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
        class Device;
        class GraphicsCommandList;
        class DescriptorHeapManager;
        class Fence;
        class ResourceBuffer;
        class CommandQueue;
        class UploadManager;

        class Texture
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            Texture();
            ~Texture();

        public:          /*---クラスメソッド---*/

            /***********************************************************************************
             * @brief 画像ファイルのデコード (WIC) だけを行う
             * @details GPU には触らないので、ワーカースレッドから並列に呼んでよい
             *          (呼ぶスレッドで CoInitializeEx 済みであること)
             *
             * @param[in] const std::wstring& filePath 画像ファイル
             * @param[out] ::DirectX::ScratchImage& outImage デコード結果
             * @return HRESULT 成功の有無
             ************************************************************************************/
            static HRESULT DecodeImageFile(const std::wstring& filePath, ::DirectX::ScratchImage& outImage);

            /***********************************************************************************
             * @brief 画像ファイルからテクスチャを作る (デコード + アップロード)
             * @details コピー命令を UploadManager に積むだけ。送信は CommandContext がまとめて行う
             *
             * @return std::unique_ptr<ResourceBuffer> テクスチャ (失敗時は nullptr)
             ************************************************************************************/
            std::unique_ptr<ResourceBuffer> LoadTextureFromFile(
                RHI::DirectX12::Device* device,
                RHI::DirectX12::UploadManager* uploadManager,
                const std::wstring& filePath);

            /***********************************************************************************
             * @brief 単色 1x1 のテクスチャを作る (テクスチャ無しマテリアルや読み込み失敗時の代わり)
             *
             * @param[in] uint32_t rgba 色 (0xRRGGBBAA)
             * @return std::unique_ptr<ResourceBuffer> テクスチャ (失敗時は nullptr)
             ************************************************************************************/
            std::unique_ptr<ResourceBuffer> CreateSolidColorTexture(
                RHI::DirectX12::Device* device,
                RHI::DirectX12::UploadManager* uploadManager,
                uint32_t rgba);

            /***********************************************************************************
             * @brief メモリ上の画像からテクスチャを作る (アップロード部分)
             * @details image のデータはこの関数の中でアップロード用の領域にコピーし終わるので、
             *          戻った後に image を破棄してよい
             ************************************************************************************/
            std::unique_ptr<ResourceBuffer> CreateTextureFromImage(
                RHI::DirectX12::Device* device,
                RHI::DirectX12::UploadManager* uploadManager,
                const ::DirectX::Image& image);

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/


        };
    }
}


#endif // DX12_TEXTURE_MANAGER_H_

/************************************
* End of file
************************************/
