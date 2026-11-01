
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

#include <chrono>
#include <cwchar>

#include "../../Core/Source/Log/logger.h"
#include "../../../Core/Source/Log/scopedTimer.h"

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
        namespace
        {
            // キャッシュの形式を変えたら上げる (古いキャッシュは使われず、作り直される)
            constexpr wchar_t DDS_CACHE_VERSION[] = L"v1";

            // ファイルの最終更新時刻 (無ければ false)
            bool getLastWriteTime(const std::wstring& path, ULARGE_INTEGER& outTime)
            {
                WIN32_FILE_ATTRIBUTE_DATA data = {};
                if (!GetFileAttributesExW(path.c_str(), GetFileExInfoStandard, &data))
                {
                    return false;
                }
                outTime.LowPart  = data.ftLastWriteTime.dwLowDateTime;
                outTime.HighPart = data.ftLastWriteTime.dwHighDateTime;
                return true;
            }

            // 拡張子が .dds か (大文字小文字を区別しない)
            bool isDdsFile(const std::wstring& path)
            {
                if (path.size() < 4) return false;
                return _wcsicmp(path.c_str() + path.size() - 4, L".dds") == 0;
            }

            // 4 の倍数に切り上げ (BC 圧縮は 4x4 ピクセルのブロック単位なので、原寸は 4 の倍数が必須)
            size_t alignTo4(size_t value)
            {
                return (value + 3) & ~static_cast<size_t>(3);
            }

            void logTime(const wchar_t* what, const std::wstring& path, double ms)
            {
                wchar_t message[512] = {};
                swprintf_s(message, L"  [Texture] %s : %.3f ms (%s)", what, ms, path.c_str());
                Core::Logger::Log(message);
            }

            /*******************************************************************************
             * デコードした画像を「GPU 向け」に変換する
             *   1. 縦横を 4 の倍数にそろえる (必要な時だけ拡大縮小)
             *   2. ミップマップを全段作る      → 遠くを描く時に小さい画像を使うので、速くて綺麗
             *   3. BC 圧縮                     → 不透明なら BC1 (RGBA8 の 1/8)、半透明があれば BC7 (1/4)
             *                                   VRAM もアップロード量も減り、描画時の読み込みも速くなる
             *******************************************************************************/
            HRESULT convertForGpu(::DirectX::ScratchImage& image)
            {
                const auto& meta = image.GetMetadata();

                // 1. 4 の倍数にそろえる
                if ((meta.width % 4) != 0 || (meta.height % 4) != 0)
                {
                    ::DirectX::ScratchImage resized;
                    RHI_RETURN_IF_FAILED(::DirectX::Resize(*image.GetImage(0, 0, 0),
                        alignTo4(meta.width), alignTo4(meta.height), ::DirectX::TEX_FILTER_DEFAULT, resized));
                    image = std::move(resized);
                }

                // 2. ミップマップ (0 = 1x1 まで全段)
                ::DirectX::ScratchImage mipChain;
                RHI_RETURN_IF_FAILED(::DirectX::GenerateMipMaps(*image.GetImage(0, 0, 0), ::DirectX::TEX_FILTER_DEFAULT, 0, mipChain));

                // 3. BC 圧縮 (JPEG などアルファが全て不透明なら BC1、そうでなければ BC7)
                const bool opaque = mipChain.IsAlphaAllOpaque();
                const DXGI_FORMAT format = opaque ? DXGI_FORMAT_BC1_UNORM : DXGI_FORMAT_BC7_UNORM;
                // PARALLEL : OpenMP で複数スレッド圧縮 / BC7_QUICK : BC7 の探索を減らして高速化 (画質は少し落ちる)
                ::DirectX::TEX_COMPRESS_FLAGS flags = ::DirectX::TEX_COMPRESS_PARALLEL;
                if (!opaque) flags |= ::DirectX::TEX_COMPRESS_BC7_QUICK;

                ::DirectX::ScratchImage compressed;
                RHI_RETURN_IF_FAILED(::DirectX::Compress(mipChain.GetImages(), mipChain.GetImageCount(), mipChain.GetMetadata(),
                    format, flags, ::DirectX::TEX_THRESHOLD_DEFAULT, compressed));

                image = std::move(compressed);
                return S_OK;
            }
        }

        Texture::Texture() {}
        Texture::~Texture() {}

        std::wstring Texture::GetCachePath(const std::wstring& filePath)
        {
            return filePath + L"." + DDS_CACHE_VERSION + L".dds";
        }

        HRESULT Texture::LoadImageFile(const std::wstring& filePath, ::DirectX::ScratchImage& outImage, bool useCache)
        {
            if (filePath.empty())
            {
                return E_INVALIDARG;
            }

            Core::ScopedTimer timer("  Texture load (file)");

            // 1. 最初から DDS ならそのまま読む
            if (isDdsFile(filePath))
            {
                return ::DirectX::LoadFromDDSFile(filePath.c_str(), ::DirectX::DDS_FLAGS_NONE, nullptr, outImage);
            }

            // 2. キャッシュが元の画像より新しければそれを読む
            const std::wstring cachePath = GetCachePath(filePath);
            ULARGE_INTEGER sourceTime = {}, cacheTime = {};
            const bool hasSource = getLastWriteTime(filePath, sourceTime);
            if (useCache && getLastWriteTime(cachePath, cacheTime) && (!hasSource || cacheTime.QuadPart >= sourceTime.QuadPart))
            {
                if (SUCCEEDED(::DirectX::LoadFromDDSFile(cachePath.c_str(), ::DirectX::DDS_FLAGS_NONE, nullptr, outImage)))
                {
                    return S_OK;
                }
                // 壊れている時は作り直す
                Core::Logger::Log(L"  [Texture] Broken DDS cache. Rebuild.");
            }

            // 3. デコード (WIC)
            Core::ScopedTimer decodeTimer("  Texture decode (WIC)");
            RHI_RETURN_IF_FAILED(::DirectX::LoadFromWICFile(filePath.c_str(), ::DirectX::WIC_FLAGS_NONE, nullptr, outImage));
            if (!useCache)
            {
                return S_OK;
            }

            // 4. GPU 向けに変換してキャッシュに保存 (初回だけ)
            const auto convertStart = std::chrono::steady_clock::now();
            if (FAILED(convertForGpu(outImage)))
            {
                // 変換できなくても、デコードした画像はそのまま使える (圧縮なし・ミップなし)
                Core::Logger::Log(L"  [Texture] Failed BC compress. Use uncompressed.");
                outImage.Release();
                return ::DirectX::LoadFromWICFile(filePath.c_str(), ::DirectX::WIC_FLAGS_NONE, nullptr, outImage);
            }
            logTime(L"Build DDS cache (mip + BC)", filePath,
                std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - convertStart).count());

            if (FAILED(::DirectX::SaveToDDSFile(outImage.GetImages(), outImage.GetImageCount(), outImage.GetMetadata(),
                ::DirectX::DDS_FLAGS_NONE, cachePath.c_str())))
            {
                // 書き込めない場所でも読み込みは続ける (次回もまた変換される)
                Core::Logger::Log(L"  [Texture] Failed save DDS cache.");
                Core::Logger::Log(cachePath.c_str());
            }
            return S_OK;
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

            ::DirectX::ScratchImage image;
            if (FAILED(LoadImageFile(filePath, image)))
            {
                Core::Logger::Log(L"Failed Open Texture File.");
                Core::Logger::Log(filePath.c_str());
                return nullptr;
            }
            return CreateTextureFromScratchImage(device, uploadManager, image);
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

        std::unique_ptr<ResourceBuffer> Texture::CreateTextureFromScratchImage(
            RHI::DirectX12::Device* device,
            RHI::DirectX12::UploadManager* uploadManager,
            const ::DirectX::ScratchImage& image)
        {
            const auto& meta = image.GetMetadata();
            if (meta.dimension != ::DirectX::TEX_DIMENSION_TEXTURE2D || meta.arraySize != 1 || meta.mipLevels == 0)
            {
                // キューブマップ・配列・3D テクスチャはまだ扱わない
                Core::Logger::Log("[Texture] Only 2D textures are supported.");
                return nullptr;
            }

            // ミップマップは配列 0 番の 0 段目から順に並んでいる
            return createTexture(device, uploadManager, image.GetImage(0, 0, 0), meta.mipLevels);
        }

        std::unique_ptr<ResourceBuffer> Texture::CreateTextureFromImage(
            RHI::DirectX12::Device* device,
            RHI::DirectX12::UploadManager* uploadManager,
            const ::DirectX::Image& img)
        {
            return createTexture(device, uploadManager, &img, 1);
        }

        std::unique_ptr<ResourceBuffer> Texture::createTexture(
            RHI::DirectX12::Device* device,
            RHI::DirectX12::UploadManager* uploadManager,
            const ::DirectX::Image* mips,
            size_t mipCount)
        {
            if (!device || !uploadManager || !mips || mipCount == 0 || !mips[0].pixels)
            {
                return nullptr;
            }

            constexpr size_t MAX_MIPS = 16;   // 65536 x 65536 まで
            if (mipCount > MAX_MIPS) mipCount = MAX_MIPS;
            const UINT subresourceCount = static_cast<UINT>(mipCount);

            // 2. テクスチャ用リソース (DEFAULTヒープ) の作成 (COMMON 状態)
            auto textureBuffer = std::make_unique<ResourceBuffer>();
            HRESULT hr = textureBuffer->CreateTexture(
                device->GetDevice(),
                mips[0].width,
                static_cast<UINT>(mips[0].height),
                mips[0].format,
                static_cast<UINT16>(mipCount), // MipLevels
                1                              // ArraySize
            );
            if (FAILED(hr))
            {
                Core::Logger::Log("Failed Create Texture Resource.");
                return nullptr;
            }

            // 3. GPUが要求するメモリレイアウト (フットプリント) をミップマップの全段について計算
            D3D12_RESOURCE_DESC texDesc = textureBuffer->Get()->GetDesc();
            D3D12_PLACED_SUBRESOURCE_FOOTPRINT footprints[MAX_MIPS] = {};
            UINT   numRows[MAX_MIPS]        = {};
            UINT64 rowSizeInBytes[MAX_MIPS] = {};
            UINT64 totalUploadBufferSize    = 0;

            device->GetDevice()->GetCopyableFootprints(
                &texDesc, 0, subresourceCount, 0,
                footprints, numRows, rowSizeInBytes, &totalUploadBufferSize);

            // 4. アップロード用の領域をリングバッファから確保 (全段まとめて 1 回)
            //    テクスチャのコピー元は 512byte 境界に置く必要がある
            UploadManager::Allocation allocation;
            if (!uploadManager->Allocate(totalUploadBufferSize, D3D12_TEXTURE_DATA_PLACEMENT_ALIGNMENT, allocation))
            {
                Core::Logger::Log("Failed Allocate Upload Memory.");
                return nullptr;
            }

            auto cmdList = uploadManager->GetCommandList();
            if (!cmdList)
            {
                return nullptr;
            }

            for (UINT mip = 0; mip < subresourceCount; ++mip)
            {
                const ::DirectX::Image& img = mips[mip];
                const auto& fp = footprints[mip];

                // 5. 1 段分のコピー
                //    BC 圧縮の時、行 = 4 ピクセル分のブロックの行 (numRows も rowPitch もブロック単位で数える)
                uint8_t* destSubresource = allocation.Cpu + fp.Offset;
                if (fp.Footprint.RowPitch == img.rowPitch && numRows[mip] > 0)
                {
                    // 行の間隔が同じなので、最初の行の先頭から最後の行の終わりまでを一度にコピーする
                    const size_t copySize = static_cast<size_t>(img.rowPitch) * (numRows[mip] - 1) + static_cast<size_t>(rowSizeInBytes[mip]);
                    memcpy(destSubresource, img.pixels, copySize);
                }
                else
                {
                    for (UINT y = 0; y < numRows[mip]; ++y)
                    {
                        // コピー先: 256の倍数でアライメントされた行の先頭 / コピー元: 実際の画像の行の先頭
                        memcpy(destSubresource + y * fp.Footprint.RowPitch,
                               img.pixels + y * img.rowPitch,
                               static_cast<size_t>(rowSizeInBytes[mip]));
                    }
                }

                // 6. COPY 用コマンドリストにコピー命令を積む (送信は CommandContext がまとめて行う)
                D3D12_TEXTURE_COPY_LOCATION dstLoc = {};
                dstLoc.pResource        = textureBuffer->Get();
                dstLoc.Type             = D3D12_TEXTURE_COPY_TYPE_SUBRESOURCE_INDEX;
                dstLoc.SubresourceIndex = mip;

                // フットプリントはリング内の位置からの相対なので、確保した位置を足す
                D3D12_PLACED_SUBRESOURCE_FOOTPRINT placed = fp;
                placed.Offset += allocation.Offset;

                D3D12_TEXTURE_COPY_LOCATION srcLoc = {};
                srcLoc.pResource       = allocation.Resource;
                srcLoc.Type            = D3D12_TEXTURE_COPY_TYPE_PLACED_FOOTPRINT;
                srcLoc.PlacedFootprint = placed;

                cmdList->CopyTextureRegion(&dstLoc, 0, 0, 0, &srcLoc, nullptr);
            }

            // 7. バリアは張らない
            //    COPY キューでは PIXEL_SHADER_RESOURCE へのバリアを張れない。
            //    コピー後は COMMON に戻り、描画キューで読む時に PIXEL_SHADER_RESOURCE へ暗黙で昇格する

            // テクスチャのインスタンスを返す
            return textureBuffer;
        }
    };
}

/************************************
* End of file
************************************/
