
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
#include <wincodec.h>       // GetWICFactory の宣言に必要 (DirectXTex.h より先に読む)
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
#include "../../../../Core/Source/Log/scopedTimer.h"

#include <algorithm>
#include <future>
#include <thread>

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
            if (!filePath)
            {
                return TextureHandle();
            }

            // 1 枚でも複数枚と同じ処理 (デコード → アップロード登録)
            auto handles = LoadTexturesFromFiles(iDevice, iCommandContext, iBuffer, { std::wstring(filePath) });
            return handles.empty() ? TextureHandle() : handles[0];
        }

        std::vector<TextureHandle> Texture::LoadTexturesFromFiles(
            RHI::ObjectInterface::IDevice* iDevice,
            RHI::ObjectInterface::ICommandContext* iCommandContext,
            RHI::ObjectInterface::IBuffer* iBuffer,
            const std::vector<std::wstring>& filePaths)
        {
            // 失敗したものは既定テクスチャ(白)のハンドルのまま
            std::vector<TextureHandle> handles(filePaths.size());

            if (!iDevice || !iCommandContext || !iBuffer || filePaths.empty())
            {
                return handles;
            }

            // TODO : Phase 3 で抽象化を見直すまでは具象型へのキャストで扱う
            auto device         = static_cast<RHI::ObjectInterface::Device*>(iDevice);
            auto commandContext = static_cast<RHI::ObjectInterface::CommandContext*>(iCommandContext);
            auto buffer         = static_cast<RHI::ObjectInterface::Buffer*>(iBuffer);
            auto textureManager = getTextureManager();

            Core::ScopedTimer timer(" Texture load total");

            // ==========================================================
            // 1. デコードを並列に行う (CPU だけの処理なので複数スレッドで同時にできる)
            // ==========================================================
            std::vector<::DirectX::ScratchImage> images(filePaths.size());
            std::vector<HRESULT>                 results(filePaths.size(), E_FAIL);
            {
                Core::ScopedTimer decodeTimer(" Texture decode (parallel)");

                // WIC のファクトリーはメインスレッドで先に作っておく
                // (ワーカースレッドで初めて作ると、そのスレッドの終了と一緒に使えなくなる恐れがある)
                bool isWIC2 = false;
                ::DirectX::GetWICFactory(isWIC2);

                // 同時に動かすスレッド数は CPU のコア数まで
                const size_t hardwareThreads = (std::max)(1u, std::thread::hardware_concurrency());
                const size_t workerCount     = (std::min)(filePaths.size(), hardwareThreads);

                std::vector<std::future<void>> workers;
                workers.reserve(workerCount);
                for (size_t worker = 0; worker < workerCount; ++worker)
                {
                    // ワーカー w は w, w+workerCount, w+2*workerCount ... 番目を担当する
                    workers.push_back(std::async(std::launch::async, [&, worker]()
                    {
                        // WIC (COM) を使うので、スレッドごとに COM を初期化する
                        const HRESULT coInit = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
                        for (size_t i = worker; i < filePaths.size(); i += workerCount)
                        {
                            results[i] = RHI::DirectX12::Texture::DecodeImageFile(filePaths[i], images[i]);
                        }
                        if (SUCCEEDED(coInit))
                        {
                            CoUninitialize();
                        }
                    }));
                }
                for (auto& w : workers)
                {
                    w.get();
                }
            }

            // ==========================================================
            // 2. アップロードの登録と SRV の作成 (メインスレッドで順番に)
            //    コピー命令は UploadManager に積むだけで、送信は次の RenderEnd でまとめて行われる
            // ==========================================================
            for (size_t i = 0; i < filePaths.size(); ++i)
            {
                if (FAILED(results[i]))
                {
                    // 以前は読み込み失敗(nullptr)のまま SRV 作成に進んでクラッシュしていた (1-11)
                    Core::Logger::Log(L"Failed Load Texture. Use default texture.");
                    Core::Logger::Log(filePaths[i].c_str());
                    continue;
                }

                const ::DirectX::Image* img = images[i].GetImage(0, 0, 0);
                if (!img)
                {
                    continue;
                }

                auto newBuffer = textureManager->CreateTextureFromImage(device->GetDevice(), commandContext->GetUploadManager(), *img);
                if (!newBuffer)
                {
                    continue;
                }

                // アップロード用の領域にコピー済みなので、デコード結果のメモリはここで手放す (ピークメモリを抑える)
                images[i].Release();

                uint32_t textureID = TextureHandle::DEFAULT_TEXTURE_ID;
                if (registerTexture(device, buffer, std::move(newBuffer), &textureID))
                {
                    handles[i].TextureID = textureID;
                }
            }

            return handles;
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

            auto whiteTexture = getTextureManager()->CreateSolidColorTexture(
                device->GetDevice(),
                commandContext->GetUploadManager(),
                DEFAULT_TEXTURE_COLOR
            );
            if (!whiteTexture)
            {
                return false;
            }

            uint32_t textureID = 0;
            if (!registerTexture(device, buffer, std::move(whiteTexture), &textureID))
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

        RHI::DirectX12::Texture* Texture::getTextureManager()
        {
            if (!_texture) {
                _texture = std::make_unique<RHI::DirectX12::Texture>();
            }
            return _texture.get();
        }

        bool Texture::registerTexture(
            RHI::ObjectInterface::Device* device,
            RHI::ObjectInterface::Buffer* buffer,
            std::unique_ptr<RHI::DirectX12::ResourceBuffer> texture,
            uint32_t* outTextureID)
        {
            // SRV は「どのリソースをどう読むか」の情報なので、データの転送が終わる前に作ってよい
            // (描画キューはアップロードの完了を GPU 上で待ってから読む)
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
