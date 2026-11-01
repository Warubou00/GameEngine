
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
#include "dx12_uploadManager.h"

#include "../../Core/Source/Log/logger.h"
#include "../scopedTimer.h"

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

        HRESULT Texture::DecodeImageFile(const std::wstring& filePath, ::DirectX::ScratchImage& outImage)
        {
            if (filePath.empty())
            {
                return E_INVALIDARG;
            }

            RHI::ScopedTimer timer("  Texture decode (WIC)");
            ::DirectX::TexMetadata metadata;
            return ::DirectX::LoadFromWICFile(filePath.c_str(), ::DirectX::WIC_FLAGS_NONE, &metadata, outImage);
        }

        std::unique_ptr<ResourceBuffer> Texture::LoadTextureFromFile(
            RHI::DirectX12::Device* device,
            RHI::DirectX12::UploadManager* uploadManager,
            const std::wstring& filePath)
        {
            if (!device || !uploadManager || filePath.empty())
            {// 例外処理nullptr / パスなし
                return nullptr;
            }

            // 1. 画像のデコード (WIC)
            ::DirectX::ScratchImage image;
            if (FAILED(DecodeImageFile(filePath, image)))
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
            return CreateTextureFromImage(device, uploadManager, *img);
        }

        std::unique_ptr<ResourceBuffer> Texture::CreateSolidColorTexture(
            RHI::DirectX12::Device* device,
            RHI::DirectX12::UploadManager* uploadManager,
            uint32_t rgba)
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

            // CreateTextureFromImage の中でアップロード用の領域へコピーし終えるので、pixel はスタック上で問題ない
            return CreateTextureFromImage(device, uploadManager, img);
        }

        std::unique_ptr<ResourceBuffer> Texture::CreateTextureFromImage(
            RHI::DirectX12::Device* device,
            RHI::DirectX12::UploadManager* uploadManager,
            const ::DirectX::Image& img)
        {
            if (!device || !uploadManager || !img.pixels)
            {
                return nullptr;
            }

            // 2. テクスチャ用リソース (DEFAULTヒープ) の作成 (COMMON 状態)
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

            // 4. アップロード用の領域をリングバッファから確保 (以前はテクスチャごとに UPLOAD バッファを新規作成していた)
            //    テクスチャのコピー元は 512byte 境界に置く必要がある
            UploadManager::Allocation allocation;
            if (!uploadManager->Allocate(totalUploadBufferSize, D3D12_TEXTURE_DATA_PLACEMENT_ALIGNMENT, allocation))
            {
                Core::Logger::Log("Failed Allocate Upload Memory.");
                return nullptr;
            }

            // 5. 256バイトアライメントを考慮した 1行ずつの手動コピー
            //    行の長さが同じなら (隙間が無ければ) 1 回の memcpy で済ませる
            uint8_t* destSubresource = allocation.Cpu + footprint.Offset;
            if (footprint.Footprint.RowPitch == img.rowPitch && numRows > 0)
            {
                // 行の間隔が同じなので、最初の行の先頭から最後の行の終わりまでを一度にコピーする
                const size_t copySize = static_cast<size_t>(img.rowPitch) * (numRows - 1) + static_cast<size_t>(rowSizeInBytes);
                memcpy(destSubresource, img.pixels, copySize);
            }
            else
            {
                for (UINT y = 0; y < numRows; ++y)
                {
                    // コピー先: 256の倍数でアライメントされた行の先頭
                    uint8_t* destRow = destSubresource + (y * footprint.Footprint.RowPitch);
                    // コピー元: 実際の画像の行の先頭
                    const uint8_t* srcRow = img.pixels + (y * img.rowPitch);

                    // 実データ分だけコピー (隙間は無視)
                    memcpy(destRow, srcRow, static_cast<size_t>(rowSizeInBytes));
                }
            }

            // 6. COPY 用コマンドリストにコピー命令を積む (送信は CommandContext がまとめて行う)
            auto cmdList = uploadManager->GetCommandList();
            if (!cmdList)
            {
                return nullptr;
            }

            D3D12_TEXTURE_COPY_LOCATION dstLoc = {};
            dstLoc.pResource = textureBuffer->Get();
            dstLoc.Type = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
            dstLoc.SubresourceIndex = 0;

            // フットプリントはリング内の位置からの相対なので、確保した位置を足す
            D3D12_PLACED_SUBRESOURCE_FOOTPRINT placed = footprint;
            placed.Offset += allocation.Offset;

            D3D12_TEXTURE_COPY_LOCATION srcLoc = {};
            srcLoc.pResource = allocation.Resource;
            srcLoc.Type = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
            srcLoc.PlacedFootprint = placed;

            cmdList->CopyTextureRegion(&dstLoc, 0, 0, 0, &srcLoc, nullptr);

            // 7. バリアは張らない
            //    COPY キューでは PIXEL_SHADER_RESOURCE へのバリアを張れない。
            //    コピー後は COMMON に戻り、描画キューで読む時に PIXEL_SHADER_RESOURCE へ暗黙で昇格する
            //    (Phase 1 までは描画キューで COPY_DEST → PIXEL_SHADER_RESOURCE のバリアを張っていた)

            // テクスチャのインスタンスを返す
            return textureBuffer;
        }
    };
}

/************************************
* End of file
************************************/
