
/***********************************************************************************
* @file    dx12_textureManager.cpp
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


/******************************************************************
* Include Headers
******************************************************************/
#include "dx12_textureManager.h"

#include "Object/dx12_device.h"
#include "Object/dx12_graphicsCommandList.h"
#include "Object/dx12_resourceBuffer.h"

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
    namespace DirectX12
    {
        Texture::Texture() {}
        Texture::~Texture() {}

        // テクスチャを読み込み、テクスチャ用バッファとアップロード用バッファを生成して返す
        std::unique_ptr<ResourceBuffer> Texture::LoadTextureFromFile(
            RHI::DirectX12::Device* device,
            RHI::DirectX12::GraphicsCommandList* cmdList,
            const std::wstring& filePath,
            std::unique_ptr<ResourceBuffer>& outUploadBuffer) // 実行完了まで破棄しないこと！
        {
            if (!device || !cmdList || filePath.empty())
            {// 例外処理nullptr / パスなし
                return nullptr;
            }

            ::DirectX::TexMetadata metadata;
            ::DirectX::ScratchImage image;

            // 1. 画像のデコード (WIC)
            HRESULT hr = ::DirectX::LoadFromWICFile(filePath.c_str(), ::DirectX::WIC_FLAGS_NONE, &metadata, image);
            if (FAILED(hr))
            {
                Core::Logger::Log(L"Failed Open Texture File.");
                Core::Logger::Log(filePath.c_str());
                return nullptr;
            }

            const ::DirectX::Image* img = image.GetImage(0, 0, 0); // 原寸大の画像データ
            if (!img)
            {
                return nullptr;
            }
            return CreateTextureFromImage(device, cmdList, *img, outUploadBuffer);
        }

        std::unique_ptr<ResourceBuffer> Texture::CreateSolidColorTexture(
            RHI::DirectX12::Device* device,
            RHI::DirectX12::GraphicsCommandList* cmdList,
            uint32_t rgba,
            std::unique_ptr<ResourceBuffer>& outUploadBuffer)
        {
            // 1x1 ピクセルの画像を手で組み立てる (R8G8B8A8 なので 1ピクセル = 4byte)
            uint8_t pixel[4] = {
                static_cast<uint8_t>((rgba >> 24) & 0xFF),   // R
                static_cast<uint8_t>((rgba >> 16) & 0xFF),   // G
                static_cast<uint8_t>((rgba >>  8) & 0xFF),   // B
                static_cast<uint8_t>((rgba      ) & 0xFF),   // A
            };

            ::DirectX::Image img = {};
            img.width      = 1;
            img.height     = 1;
            img.format     = DXGI_FORMAT_R8G8B8A8_UNORM;
            img.rowPitch   = sizeof(pixel);
            img.slicePitch = sizeof(pixel);
            img.pixels     = pixel;

            // CreateTextureFromImage の中でアップロードバッファへコピーし終えるので、pixel はスタック上で問題ない
            return CreateTextureFromImage(device, cmdList, img, outUploadBuffer);
        }

        std::unique_ptr<ResourceBuffer> Texture::CreateTextureFromImage(
            RHI::DirectX12::Device* device,
            RHI::DirectX12::GraphicsCommandList* cmdList,
            const ::DirectX::Image& img,
            std::unique_ptr<ResourceBuffer>& outUploadBuffer)
        {
            if (!device || !cmdList || !img.pixels)
            {
                return nullptr;
            }

            // 2. テクスチャ用リソース (DEFAULTヒープ) の作成
            auto textureBuffer = std::make_unique<ResourceBuffer>();
            HRESULT hr = textureBuffer->CreateTexture(
                device->GetDevice(),
                img.width,
                static_cast<UINT>(img.height),
                img.format,
                1, // MipLevels (ミップマップなし)
                1  // ArraySize
            );
            if (FAILED(hr))
            {
                Core::Logger::Log("Failed Create Texture Resource.");
                return nullptr;
            }

            // 3. GPUが要求するメモリレイアウト (フットプリント) の計算
            D3D12_RESOURCE_DESC texDesc = textureBuffer->Get()->GetDesc();
            D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprint = {};
            UINT numRows = 0;
            UINT64 rowSizeInBytes = 0;
            UINT64 totalUploadBufferSize = 0;

            device->GetDevice()->GetCopyableFootprints(
                &texDesc, 0, 1, 0,
                &footprint, &numRows, &rowSizeInBytes, &totalUploadBufferSize);

            // 4. 計算されたサイズでアップロード用バッファ (UPLOADヒープ) を作成
            outUploadBuffer = std::make_unique<ResourceBuffer>();
            hr = outUploadBuffer->Create(device->GetDevice(), totalUploadBufferSize, D3D12_HEAP_TYPE_UPLOAD);
            if (FAILED(hr))
            {
                Core::Logger::Log("Failed Create Upload Buffer.");
                return nullptr;
            }

            // 5. 256バイトアライメントを考慮した 1行ずつの手動コピー
            void* mappedData = nullptr;
            hr = outUploadBuffer->Get()->Map(0, nullptr, &mappedData);
            if (FAILED(hr) || !mappedData)
            {
                Core::Logger::Log("Failed Map Upload Buffer.");
                return nullptr;
            }

            uint8_t* destSubresource = static_cast<uint8_t*>(mappedData) + footprint.Offset;

            for (UINT y = 0; y < numRows; ++y)
            {
                // コピー先: 256の倍数でアライメントされた行の先頭
                uint8_t* destRow = destSubresource + (y * footprint.Footprint.RowPitch);
                // コピー元: 実際の画像の行の先頭
                const uint8_t* srcRow = img.pixels + (y * img.rowPitch);

                // 実データ分だけコピー (隙間は無視)
                memcpy(destRow, srcRow, static_cast<size_t>(rowSizeInBytes));
            }
            outUploadBuffer->Get()->Unmap(0, nullptr);

            // 6. コマンドリストにコピー命令を積む
            D3D12_TEXTURE_COPY_LOCATION dstLoc = {};
            dstLoc.pResource = textureBuffer->Get();
            dstLoc.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
            dstLoc.SubresourceIndex = 0;

            D3D12_TEXTURE_COPY_LOCATION srcLoc = {};
            srcLoc.pResource = outUploadBuffer->Get(); // ID3D12Resource* を取得
            srcLoc.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
            srcLoc.PlacedFootprint = footprint;

            cmdList->GetCmdList()->CopyTextureRegion(&dstLoc, 0, 0, 0, &srcLoc, nullptr);

            // 7. リソースバリア (COPY_DEST -> PIXEL_SHADER_RESOURCE)
            D3D12_RESOURCE_BARRIER barrier = {};
            barrier.Type = D3D12_RESOURCE_BARRIER_TYPE_TRANSITION;
            barrier.Flags = D3D12_RESOURCE_BARRIER_FLAG_NONE;
            barrier.Transition.pResource = textureBuffer->Get();
            barrier.Transition.Subresource = D3D12_RESOURCE_BARRIER_ALL_SUBRESOURCES;
            barrier.Transition.StateBefore = D3D12_RESOURCE_STATE_COPY_DEST;
            barrier.Transition.StateAfter = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
            cmdList->GetCmdList()->ResourceBarrier(1, &barrier);

            // テクスチャのインスタンスを返す
            return textureBuffer;
        }
    };
}

/************************************
* End of file
************************************/
