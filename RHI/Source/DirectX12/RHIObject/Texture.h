
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

        public:          /*---アクセッサー関数群---*/


        private:         /*---メンバ変数---*/
            std::unique_ptr<RHI::DirectX12::Texture> _texture;
            std::vector<std::unique_ptr<RHI::DirectX12::ResourceBuffer>> _loadedBuffers;

            // SRVを作り、シェーダーから見たテクスチャ番号(TextureID)を返す。失敗時は false
            bool createShaderResourceView(
                RHI::ObjectInterface::Device* device,
                RHI::ObjectInterface::Buffer* buffer,
                RHI::DirectX12::ResourceBuffer* targetBuffer,
                uint32_t* outTextureID);

            // 作成済みのテクスチャ(+アップロード)を送信してSRVを作る共通処理
            bool registerTexture(
                RHI::ObjectInterface::Device* device,
                RHI::ObjectInterface::CommandContext* commandContext,
                RHI::ObjectInterface::Buffer* buffer,
                std::unique_ptr<RHI::DirectX12::ResourceBuffer> texture,
                std::unique_ptr<RHI::DirectX12::ResourceBuffer> uploadBuffer,
                uint32_t* outTextureID);
        };
    }
}


#endif // RHI_TEXTURE_H_

/************************************
* End of file
************************************/
