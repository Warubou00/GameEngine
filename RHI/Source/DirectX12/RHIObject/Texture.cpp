
/***********************************************************************************
* @file    Texture.cpp
* @brief   RHI Texture
* @details
*
* @author  Keigo Onari
* @date    2026/4/24
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
#include <DirectXTex.h>
#include "Texture.h"
#include "Device.h"
#include "CommandContext.h"
#include "Buffer.h"

#include "../Object/dx12_device.h"
#include "../dx12_textureManager.h"
#include "../Object/dx12_resourceBuffer.h"
#include "../dx12_descriptorHeapManager.h"

#include "../../Core/Source/Log/logger.h"

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
	namespace ObjectInterface
	{
		namespace
		{
			// 既定テクスチャの色 (0xRRGGBBAA)
			constexpr uint32_t DEFAULT_TEXTURE_COLOR = 0xFFFFFFFF;
		}

		Texture::Texture() {}
		Texture::~Texture() {}

        TextureHandle Texture::LoadTextureFromFile(
            RHI::ObjectInterface::IDevice* iDevice,
            RHI::ObjectInterface::ICommandContext* iCommandContext,
            RHI::ObjectInterface::IBuffer* iBuffer,
            const wchar_t* filePath)
        {
            // 失敗時はどれも既定テクスチャ(白)のハンドルを返す
            TextureHandle handle;

            if (!iDevice || !iCommandContext || !iBuffer || !filePath)
            {
                return handle;
            }

            // TODO : Phase 3 で抽象化を見直すまでは具象型へのキャストで扱う
            auto device         = static_cast<RHI::ObjectInterface::Device*>(iDevice);
            auto commandContext = static_cast<RHI::ObjectInterface::CommandContext*>(iCommandContext);
            auto buffer         = static_cast<RHI::ObjectInterface::Buffer*>(iBuffer);

            // _textureの初期化は初回のみ行うようにする
            if (!_texture) {
                _texture = std::make_unique<RHI::DirectX12::Texture>();
            }

            // コピー命令を積む前に記録状態にしておく
            if (!commandContext->BeginRecording())
            {
                return handle;
            }

            // 画像読み込み (メンバ変数ではなくローカル変数で受け取る)
            std::unique_ptr<RHI::DirectX12::ResourceBuffer> uploadBuffer;
            auto newBuffer = _texture->LoadTextureFromFile(
                device->GetDevice(),
                commandContext->GetGraphicsCommandList(),
                filePath,
                uploadBuffer
            );

            // 以前は読み込み失敗(nullptr)のまま SRV 作成に進んでクラッシュしていた (1-11)
            if (!newBuffer)
            {
                Core::Logger::Log("Failed Load Texture. Use default texture.");
                return handle;
            }

            uint32_t textureID = TextureHandle::DEFAULT_TEXTURE_ID;
            if (registerTexture(device, commandContext, buffer, std::move(newBuffer), std::move(uploadBuffer), &textureID))
            {
                handle.TextureID = textureID;
            }
            return handle;
        }

        bool Texture::CreateDefaultTexture(
            RHI::ObjectInterface::IDevice* iDevice,
            RHI::ObjectInterface::ICommandContext* iCommandContext,
            RHI::ObjectInterface::IBuffer* iBuffer)
        {
            if (!iDevice || !iCommandContext || !iBuffer)
            {
                return false;
            }

            auto device         = static_cast<RHI::ObjectInterface::Device*>(iDevice);
            auto commandContext = static_cast<RHI::ObjectInterface::CommandContext*>(iCommandContext);
            auto buffer         = static_cast<RHI::ObjectInterface::Buffer*>(iBuffer);

            if (!_texture) {
                _texture = std::make_unique<RHI::DirectX12::Texture>();
            }

            if (!commandContext->BeginRecording())
            {
                return false;
            }

            std::unique_ptr<RHI::DirectX12::ResourceBuffer> uploadBuffer;
            auto whiteTexture = _texture->CreateSolidColorTexture(
                device->GetDevice(),
                commandContext->GetGraphicsCommandList(),
                DEFAULT_TEXTURE_COLOR,
                uploadBuffer
            );
            if (!whiteTexture)
            {
                return false;
            }

            uint32_t textureID = 0;
            if (!registerTexture(device, commandContext, buffer, std::move(whiteTexture), std::move(uploadBuffer), &textureID))
            {
                return false;
            }

            // 既定テクスチャは必ず 0 番でなければならない (他のテクスチャより先に作る約束)
            if (textureID != TextureHandle::DEFAULT_TEXTURE_ID)
            {
                Core::Logger::Log("[Texture] Default texture must be created first.");
                return false;
            }
            return true;
        }

        void Texture::Dispose()
        {
            // GPUの完了待ちは CommandContext::Dispose で済ませてある前提
            _loadedBuffers.clear();
            _texture.reset();
        }

        bool Texture::registerTexture(
            RHI::ObjectInterface::Device* device,
            RHI::ObjectInterface::CommandContext* commandContext,
            RHI::ObjectInterface::Buffer* buffer,
            std::unique_ptr<RHI::DirectX12::ResourceBuffer> texture,
            std::unique_ptr<RHI::DirectX12::ResourceBuffer> uploadBuffer,
            uint32_t* outTextureID)
        {
            // コマンド実行 (完了するまでアップロード用バッファを破棄してはいけない)
            if (!commandContext->ExecuteAndSync())
            {
                return false;
            }

            // アップロード用バッファ解放 (GPUのコピーは完了している)
            uploadBuffer.reset();

            // SRV作成
            if (!createShaderResourceView(device, buffer, texture.get(), outTextureID))
            {
                return false;
            }

            // ★重要: GPUが使い続けるため、配列に保存して寿命を管理する
            _loadedBuffers.push_back(std::move(texture));
            return true;
        }

        bool Texture::createShaderResourceView(
            RHI::ObjectInterface::Device* device,
            RHI::ObjectInterface::Buffer* buffer,
            RHI::DirectX12::ResourceBuffer* targetBuffer,
            uint32_t* outTextureID)
        {
            if (!targetBuffer || !targetBuffer->Get() || !outTextureID)
            {
                return false;
            }

            auto heapManager = buffer->GetDescHeapManager();
            const uint32_t srvIndex = heapManager->AllocateSrv();
            if (srvIndex == RHI::DirectX12::DescriptorHeapManager::INVALID_INDEX)
            {
                Core::Logger::Log("[Texture] SRV heap is full.");
                return false;
            }

            // テクスチャテーブルより前の番号(頂点バッファ用)が返ることは無いはず
            if (srvIndex < Buffer::SRV_INDEX_TEXTURE_BEGIN)
            {
                heapManager->FreeSrv(srvIndex);
                return false;
            }

            D3D12_SHADER_RESOURCE_VIEW_DESC srvDesc = {};
            srvDesc.Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING;
            // 渡されたバッファからフォーマットを取得する
            srvDesc.Format = targetBuffer->Get()->GetDesc().Format;
            srvDesc.ViewDimension = D3D12_SRV_DIMENSION_TEXTURE2D;
            srvDesc.Texture2D.MipLevels = 1;

            device->GetDevice()->GetDevice()->CreateShaderResourceView(
                targetBuffer->Get(),
                &srvDesc,
                heapManager->GetSrvCpuHandle(srvIndex)
            );

            // 以前はヒープの番号をそのまま TextureID にしていたが、
            // シェーダーの g_Textures[] はテクスチャテーブルの先頭(ヒープの2番)から数えるため 2 つずれていた (1-8)
            *outTextureID = srvIndex - Buffer::SRV_INDEX_TEXTURE_BEGIN;
            return true;
        }
	}
}

/************************************
* End of file
************************************/
