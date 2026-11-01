
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
             * @brief 画像ファイルからテクスチャを作る
             * @details コピー命令をコマンドリストに積むだけ。送信して完了するまで outUploadBuffer を破棄しないこと
             *
             * @return std::unique_ptr<ResourceBuffer> テクスチャ (失敗時は nullptr)
             ************************************************************************************/
            std::unique_ptr<ResourceBuffer> LoadTextureFromFile(
                RHI::DirectX12::Device* device,
                RHI::DirectX12::GraphicsCommandList* cmdList,
                const std::wstring& filePath,
                std::unique_ptr<ResourceBuffer>& outUploadBuffer);

            /***********************************************************************************
             * @brief 単色 1x1 のテクスチャを作る (テクスチャ無しマテリアルや読み込み失敗時の代わり)
             *
             * @param[in] uint32_t rgba 色 (0xRRGGBBAA)
             * @return std::unique_ptr<ResourceBuffer> テクスチャ (失敗時は nullptr)
             ************************************************************************************/
            std::unique_ptr<ResourceBuffer> CreateSolidColorTexture(
                RHI::DirectX12::Device* device,
                RHI::DirectX12::GraphicsCommandList* cmdList,
                uint32_t rgba,
                std::unique_ptr<ResourceBuffer>& outUploadBuffer);

            /***********************************************************************************
             * @brief メモリ上の画像からテクスチャを作る (上の2つの共通処理)
             ************************************************************************************/
            std::unique_ptr<ResourceBuffer> CreateTextureFromImage(
                RHI::DirectX12::Device* device,
                RHI::DirectX12::GraphicsCommandList* cmdList,
                const ::DirectX::Image& image,
                std::unique_ptr<ResourceBuffer>& outUploadBuffer);

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/


        };
    }
}


#endif // DX12_TEXTURE_MANAGER_H_

/************************************
* End of file
************************************/
