
/***********************************************************************************
* @file    Texture.h
* @brief   RHI Texture
* @details 
*
* @author  Keigo Onari
* @date    2025/1/13
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    
************************************************************************************/

#pragma once
#ifndef RHI_TEXTURE_H_
#define RHI_TEXTURE_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <string>
#include <vector>
#include <unordered_map>

#include "../../RHIObject/ITexture.h"

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
        class Texture;
        class ResourceBuffer;
    }

    namespace ObjectInterface
    {
        class IDevice;
        class ICommandContext;
        class IBuffer;
        class Device;
        class Buffer;
        class CommandContext;

        class Texture : public ITexture
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/


        public:          /*---コンストラクタ・デストラクタ---*/
            Texture();
            ~Texture()override;


        public:          /*---クラスメソッド---*/

            /***********************************************************************************
             * @brief 画像ファイルからテクスチャを読み込む
             * @details 読み込みに失敗した場合は既定テクスチャ(白)のハンドルを返す (描画は続けられる)
             *
             * @return TextureHandle テクスチャのハンドル
             ************************************************************************************/
            TextureHandle LoadTextureFromFile(
                RHI::ObjectInterface::IDevice* iDevice,
                RHI::ObjectInterface::ICommandContext* iCommandContext,
                RHI::ObjectInterface::IBuffer* iBuffer,
                const wchar_t* filePath)override;

            /***********************************************************************************
             * @brief 複数の画像ファイルをまとめて読み込む (デコードを並列に行う)
             * @details 1. 全ファイルのデコード (WIC) をワーカースレッドで同時に行う  ← 一番時間がかかる部分
             *          2. GPU へのアップロード登録はメインスレッドで順番に行う (UploadManager はスレッドセーフではない)
             *          読み込みに失敗したものは既定テクスチャ(白)のハンドルになる
             *
             * @return std::vector<TextureHandle> filePaths と同じ順番のハンドル
             ************************************************************************************/
            std::vector<TextureHandle> LoadTexturesFromFiles(
                RHI::ObjectInterface::IDevice* iDevice,
                RHI::ObjectInterface::ICommandContext* iCommandContext,
                RHI::ObjectInterface::IBuffer* iBuffer,
                const std::vector<std::wstring>& filePaths);

            /***********************************************************************************
             * @brief 既定テクスチャ(白 1x1)を作る
             * @details 必ず他のテクスチャより先に呼ぶこと (TextureID = 0 になるようにするため)
             *          テクスチャを使わないマテリアルは、白を掛けることで頂点カラーそのままになる
             *
             * @return bool 成功の有無
             ************************************************************************************/
            bool CreateDefaultTexture(
                RHI::ObjectInterface::IDevice* iDevice,
                RHI::ObjectInterface::ICommandContext* iCommandContext,
                RHI::ObjectInterface::IBuffer* iBuffer);

            void Dispose()override;

            /***********************************************************************************
             * @brief 読み込んだテクスチャを返す (ED5。モデルの読み直し / 解放)
             * @details SRV の番号を空きに戻し、GPU のリソースを消す。既定テクスチャ (0) は返せない
             *          ※GPU が使い終わってから呼ぶこと (RHI が GPU を待ってから呼ぶ)
             ************************************************************************************/
            void ReleaseTexture(RHI::ObjectInterface::IBuffer* iBuffer, uint32_t textureID);

            /***********************************************************************************
             * @brief 3D テクスチャをメモリの値から作る (SH2)。channels = 1 (R8) / 4 (RGBA8)
             * @details シェーダーは g_Textures3D[番号] で読む (2D と同じ番号の数え方。同じ配列に入る)。返すのは ReleaseTexture
             * @return uint32_t テクスチャの番号 (失敗時 0)
             ************************************************************************************/
            uint32_t CreateTexture3D(
                RHI::ObjectInterface::IDevice* iDevice,
                RHI::ObjectInterface::ICommandContext* iCommandContext,
                RHI::ObjectInterface::IBuffer* iBuffer,
                uint32_t width, uint32_t height, uint32_t depth, uint32_t channels, const uint8_t* data);
            size_t GetLoadedCount()const { return _loadedBuffers.size(); }

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/
            std::unique_ptr<RHI::DirectX12::Texture> _texture;
            // TextureID → リソース (以前は配列で、返す方法が無かった。ED5)
            std::unordered_map<uint32_t, std::unique_ptr<RHI::DirectX12::ResourceBuffer>> _loadedBuffers;

            // SRVを作り、シェーダーから見たテクスチャ番号(TextureID)を返す。失敗時は false
            bool createShaderResourceView(
                RHI::ObjectInterface::Device* device,
                RHI::ObjectInterface::Buffer* buffer,
                RHI::DirectX12::ResourceBuffer* targetBuffer,
                uint32_t* outTextureID);

            // 作成済みのテクスチャのSRVを作って保持する共通処理
            // (アップロードは UploadManager に積んである。送信と完了待ちは CommandContext が行う)
            bool registerTexture(
                RHI::ObjectInterface::Device* device,
                RHI::ObjectInterface::Buffer* buffer,
                std::unique_ptr<RHI::DirectX12::ResourceBuffer> texture,
                uint32_t* outTextureID);

            // _texture (DirectX12 側のテクスチャ管理) を必要なら作る
            RHI::DirectX12::Texture* getTextureManager();
        };
    }
}


#endif // RHI_TEXTURE_H_

/************************************
* End of file
************************************/
