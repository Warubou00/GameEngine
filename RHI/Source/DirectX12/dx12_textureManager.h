
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
             * @brief 画像ファイルを GPU に送れる形で読み込む (デコード / キャッシュ)
             * @details GPU には触らないので、ワーカースレッドから並列に呼んでよい
             *          (呼ぶスレッドで CoInitializeEx 済みであること)
             *
             *          .dds      : そのまま読む (デコード不要)
             *          それ以外  : 隣に DDS キャッシュ (例: grass.jpg → grass.jpg.v1.dds) があり、
             *                      元の画像より新しければそれを読む (ほぼファイルを読むだけ)
             *                      無ければ「デコード → ミップマップ生成 → BC 圧縮」して読み込み、キャッシュに保存する
             *                      (初回だけ時間がかかる。保存に失敗しても読み込み自体は成功する)
             *
             * @param[in] const std::wstring& filePath 画像ファイル
             * @param[out] ::DirectX::ScratchImage& outImage 読み込み結果 (ミップマップ込み)
             * @param[in] bool useCache false なら毎回デコードし、キャッシュも作らない
             * @return HRESULT 成功の有無
             ************************************************************************************/
            static HRESULT LoadImageFile(const std::wstring& filePath, ::DirectX::ScratchImage& outImage, bool useCache = true);

            // キャッシュのファイル名 (圧縮の方法を変えたら DDS_CACHE_VERSION を上げて古いキャッシュを使わないようにする)
            static std::wstring GetCachePath(const std::wstring& filePath);

            /***********************************************************************************
             * @brief 画像ファイルからテクスチャを作る (読み込み + アップロード)
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
             * @brief 読み込んだ画像 (ミップマップ全段) からテクスチャを作る (アップロード部分)
             * @details データはこの関数の中でアップロード用の領域にコピーし終わるので、
             *          戻った後に image を破棄してよい
             ************************************************************************************/
            std::unique_ptr<ResourceBuffer> CreateTextureFromScratchImage(
                RHI::DirectX12::Device* device,
                RHI::DirectX12::UploadManager* uploadManager,
                const ::DirectX::ScratchImage& image);

            // ミップマップ 1 段だけの画像からテクスチャを作る
            std::unique_ptr<ResourceBuffer> CreateTextureFromImage(
                RHI::DirectX12::Device* device,
                RHI::DirectX12::UploadManager* uploadManager,
                const ::DirectX::Image& image);

            /***********************************************************************************
             * @brief 3D テクスチャをメモリの値から作る (SH2 : 雲のノイズなど)
             * @details ミップマップ無し。data は x → y → z の順に詰めた 1 ボクセル bytesPerPixel バイト
             *          (z 枚目の y 行目 x 列 = data[((z * height + y) * width + x) * bytesPerPixel])
             ************************************************************************************/
            std::unique_ptr<ResourceBuffer> CreateTexture3D(
                RHI::DirectX12::Device* device,
                RHI::DirectX12::UploadManager* uploadManager,
                uint32_t width, uint32_t height, uint32_t depth, DXGI_FORMAT format, uint32_t bytesPerPixel, const uint8_t* data);

        private:
            // mips[0] が最上段 (原寸)。mipCount 段分をまとめてアップロードする
            std::unique_ptr<ResourceBuffer> createTexture(
                RHI::DirectX12::Device* device,
                RHI::DirectX12::UploadManager* uploadManager,
                const ::DirectX::Image* mips,
                size_t mipCount);

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/


        };
    }
}


#endif // DX12_TEXTURE_MANAGER_H_

/************************************
* End of file
************************************/
