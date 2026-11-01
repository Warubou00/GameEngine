
/***********************************************************************************
* @file    RenderHardwareInterface.cpp
* @brief   RHI構造
* @details
*
* @author  Keigo Onari
* @date    2026/1/13
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
#include "RenderHardwareInterface.h"

// DirectX12
// ========================================
#include "DirectX12/RHIObject/Device.h"
#include "DirectX12/RHIObject/CommandContext.h"
#include "DirectX12/RHIObject/Buffer.h"
#include "DirectX12/RHIObject/Texture.h"
#include "DirectX12/RHIObject/Pipeline.h"
#include "DirectX12/debugLayer.h"
// ========================================

#include "../../Core/Source/Log/logger.h"

namespace RHI
{
    RenderHardwareInterface::RenderHardwareInterface() = default;
    RenderHardwareInterface::~RenderHardwareInterface()
    {
        // Shutdown を呼び忘れても、GPUの完了を待ってから正しい順番で破棄されるようにする
        Shutdown();
    }

    bool RenderHardwareInterface::Initialize(const InitializeDesc& desc, InitializeResult* outResult)
    {
        InitializeResult result;
        _isInitialized = false;

#ifdef _DEBUG
        RHI::DirectX12::EnableDebugLayer();
#endif

        _device         = std::make_unique<ObjectInterface::Device>();
        _commandContext = std::make_unique<ObjectInterface::CommandContext>();
        _buffer         = std::make_unique<ObjectInterface::Buffer>();
        _pipeline       = std::make_unique<ObjectInterface::Pipeline>();

        // 公開用の設定を内部用の形に詰め替える
        ObjectInterface::ObjectInfo objInfo = {};
        objInfo.HWnd         = desc.WindowHandle;
        objInfo.Width        = desc.Width;
        objInfo.Height       = desc.Height;
        objInfo.EnabledVSync = desc.EnableVSync ? 1u : 0u;

        // 失敗した段階で止める (それ以降の項目は NotInitialized のまま)
        auto finish = [&](bool succeeded)
        {
            if (outResult) *outResult = result;
            _isInitialized = succeeded;
            return succeeded;
        };

        result.Device = _device->Initialize();
        if (result.Device != Result::Succeeded) { Core::Logger::Log("[RHI] Failed initialize Device."); return finish(false); }

        result.CommandContext = _commandContext->Initialize(_device.get(), &objInfo);
        if (result.CommandContext != Result::Succeeded) { Core::Logger::Log("[RHI] Failed initialize CommandContext."); return finish(false); }

        result.Buffer = _buffer->Initialize(_device.get(), _commandContext.get());
        if (result.Buffer != Result::Succeeded) { Core::Logger::Log("[RHI] Failed initialize Buffer."); return finish(false); }

        // テクスチャは Buffer の初期化で既定テクスチャまで作ってある
        result.Texture = Result::Succeeded;

        result.Pipeline = _pipeline->Initialize(_device.get());
        if (result.Pipeline != Result::Succeeded) { Core::Logger::Log("[RHI] Failed initialize Pipeline."); return finish(false); }

        return finish(true);
    }

    void RenderHardwareInterface::Shutdown()
    {
        // 破棄の順番が重要:
        //   1. CommandContext で GPU の完了を待つ (使用中のリソースを消さないため)
        //   2. GPU リソースを持つオブジェクトを破棄
        //   3. 最後に Device
        if (_commandContext) _commandContext->WaitForGpu();

        if (_pipeline)       _pipeline->Dispose();
        if (_buffer)         _buffer->Dispose();
        if (_commandContext) _commandContext->Dispose();

        _pipeline.reset();
        _buffer.reset();
        _commandContext.reset();

        if (_device) _device->Dispose();
        _device.reset();

        _isInitialized = false;
    }

    bool RenderHardwareInterface::Resize(uint32_t width, uint32_t height)
    {
        if (!_isInitialized) return false;

        // スワップチェーン・RTV・深度バッファを作り直す
        // (Projection 行列のアスペクト比は、行列を作る側 (Renderer) が GetWidth / GetHeight を見て作り直す)
        if (!_commandContext->Resize(width, height))
        {
            Core::Logger::Log("[RHI] Failed resize.");
            return false;
        }
        return true;
    }

    uint32_t RenderHardwareInterface::GetWidth()const
    {
        return _commandContext ? _commandContext->GetWidth() : 0;
    }

    uint32_t RenderHardwareInterface::GetHeight()const
    {
        return _commandContext ? _commandContext->GetHeight() : 0;
    }

    VertexRange RenderHardwareInterface::UploadVertices(const Vertex* vertices, uint32_t count)
    {
        if (!_isInitialized) return VertexRange();
        return _buffer->UploadVertexData(_device.get(), _commandContext.get(), vertices, count);
    }

    IndexRange RenderHardwareInterface::UploadIndices(const uint32_t* indices, uint32_t count)
    {
        if (!_isInitialized) return IndexRange();
        return _buffer->UploadIndexData(_device.get(), _commandContext.get(), indices, count);
    }

    void RenderHardwareInterface::LoadTextures(const wchar_t* const* filePaths, uint32_t count, TextureHandle* outHandles)
    {
        if (!outHandles) return;

        // 失敗時はどれも既定テクスチャ
        for (uint32_t i = 0; i < count; ++i) outHandles[i] = TextureHandle();
        if (!_isInitialized || !filePaths || count == 0) return;

        std::vector<std::wstring> paths;
        paths.reserve(count);
        for (uint32_t i = 0; i < count; ++i)
        {
            paths.emplace_back(filePaths[i] ? filePaths[i] : L"");
        }

        // 既定テクスチャを作ったものと同じ Texture を使う (TextureID の通し番号を共有するため)
        auto handles = _buffer->GetTexture()->LoadTexturesFromFiles(_device.get(), _commandContext.get(), _buffer.get(), paths);
        for (uint32_t i = 0; i < count && i < handles.size(); ++i)
        {
            outHandles[i].Index = handles[i].TextureID;
        }
    }

    ObjectHandle RenderHardwareInterface::CreateObject()
    {
        if (!_isInitialized) return ObjectHandle();
        return _buffer->CreateObject();
    }

    void RenderHardwareInterface::UpdateObject(ObjectHandle object, const ObjectConstants& constants)
    {
        if (!_isInitialized) return;
        _buffer->UpdateObject(object, constants);
    }

    void RenderHardwareInterface::SetViewProjection(const ::DirectX::XMFLOAT4X4& view, const ::DirectX::XMFLOAT4X4& projection)
    {
        if (!_isInitialized) return;
        _buffer->SetViewProjection(view, projection);
    }

    bool RenderHardwareInterface::Render(const DrawItem* items, uint32_t count)
    {
        if (!_isInitialized) return false;

        if (!_commandContext->RenderStart(_pipeline.get(), _buffer.get()))
        {
            return false;
        }
        _commandContext->Render(_buffer.get(), items, count);
        return _commandContext->RenderEnd();
    }

    void RenderHardwareInterface::WaitForGpu()
    {
        if (_commandContext) _commandContext->WaitForGpu();
    }
}

/************************************
* End of file
************************************/
