
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
#include "DirectX12/Object/dx12_device.h"
#include "DirectX12/Object/dx12_swapChain.h"
#include "DirectX12/dx12_descriptorHeapManager.h"
#include "DXGI/adapter.h"
// ========================================

#include "../../Core/Source/Log/logger.h"
#include "../../Core/Source/Log/scopedTimer.h"

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

        // 起動時間の計測 (シェーダーのキャッシュ / PSO キャッシュの効果確認用)
        Core::ScopedTimer initTimer("RHI Initialize (total)");

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

        {
            Core::ScopedTimer t("RHI Initialize : Device");
            result.Device = _device->Initialize();
        }
        if (result.Device != Result::Succeeded) { Core::Logger::Log("[RHI] Failed initialize Device."); return finish(false); }

        {
            Core::ScopedTimer t("RHI Initialize : CommandContext (queue / swap chain / upload)");
            result.CommandContext = _commandContext->Initialize(_device.get(), &objInfo);
        }
        if (result.CommandContext != Result::Succeeded) { Core::Logger::Log("[RHI] Failed initialize CommandContext."); return finish(false); }

        {
            Core::ScopedTimer t("RHI Initialize : Buffer (mega buffers / default texture)");
            result.Buffer = _buffer->Initialize(_device.get(), _commandContext.get());
        }
        if (result.Buffer != Result::Succeeded) { Core::Logger::Log("[RHI] Failed initialize Buffer."); return finish(false); }

        // テクスチャは Buffer の初期化で既定テクスチャまで作ってある
        result.Texture = Result::Succeeded;

        {
            Core::ScopedTimer pipelineTimer("RHI Initialize : Pipeline (shader + root signature + PSO)");
            result.Pipeline = _pipeline->Initialize(_device.get());
        }
        if (result.Pipeline != Result::Succeeded) { Core::Logger::Log("[RHI] Failed initialize Pipeline."); return finish(false); }

        // プロファイラー (ImGui)。失敗しても描画は続けられるので、初期化の失敗にはしない
        Core::ScopedTimer uiTimer("RHI Initialize : DebugUI (ImGui)");
        if (!_debugUI.Initialize(static_cast<HWND>(desc.WindowHandle), _device->GetDevice()->GetDevice(),
                                 _buffer->GetDescHeapManager(), ObjectInterface::CommandContext::FRAME_COUNT,
                                 DirectX12::SwapChain::FORMAT))
        {
            Core::Logger::Log("[RHI] Profiler (ImGui) is not available.");
        }
        _profiler.Initialize(_device->GetAdapter()->GetAdapter3(), _device->GetAdapter()->GetAdapterName());
        _profiler.SetInfo("Pipeline", _pipeline->GetDescription());   // 例 : "DXC / SM 6_6 / shader from cache / PSO from cache"
        _hasLastFrame = false;

        return finish(true);
    }

    void RenderHardwareInterface::Shutdown()
    {
        // 破棄の順番が重要:
        //   1. CommandContext で GPU の完了を待つ (使用中のリソースを消さないため)
        //   2. GPU リソースを持つオブジェクトを破棄
        //   3. 最後に Device
        if (_commandContext) _commandContext->WaitForGpu();

        // 起動からの平均をログに出す (計測用)
        if (_isInitialized) _profiler.LogSummary();

        // ImGui は SRV ヒープ (Buffer が持つ) とデバイスを使うので、それらより先に破棄する
        _debugUI.Shutdown();

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

    MaterialHandle RenderHardwareInterface::CreateMaterial(const MaterialDesc& desc)
    {
        if (!_isInitialized) return MaterialHandle();
        return _buffer->CreateMaterial(desc);
    }

    ObjectHandle RenderHardwareInterface::CreateObject()
    {
        if (!_isInitialized) return ObjectHandle();
        return _buffer->CreateObject();
    }

    void RenderHardwareInterface::DestroyObject(ObjectHandle object)
    {
        if (!_isInitialized) return;
        _buffer->DestroyObject(object);
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

    RenderTargetHandle RenderHardwareInterface::CreateRenderTarget(const RenderTargetDesc& desc)
    {
        if (!_isInitialized) return RenderTargetHandle();
        return _commandContext->CreateRenderTarget(_buffer.get(), desc);
    }

    void RenderHardwareInterface::DestroyRenderTarget(RenderTargetHandle handle)
    {
        if (_isInitialized) _commandContext->DestroyRenderTarget(handle);
    }

    void RenderHardwareInterface::SetDebugLines(const DebugLineVertex* vertices, uint32_t count)
    {
        if (_isInitialized) _commandContext->SetDebugLines(vertices, count);
    }

    void RenderHardwareInterface::SetShadowSettings(const ShadowSettings& settings)
    {
        if (_isInitialized) _commandContext->SetShadowSettings(_buffer.get(), settings);
    }

    uint32_t RenderHardwareInterface::GetMaxSampleCount() const
    {
        return _isInitialized ? _commandContext->GetMaxSampleCount() : 1;
    }

    FullscreenShaderHandle RenderHardwareInterface::LoadFullscreenShader(const wchar_t* filePath, const char* entry)
    {
        FullscreenShaderHandle handle;
        if (!_isInitialized || !filePath || !entry) return handle;
        handle.Index = _pipeline->LoadFullscreenShader(filePath, entry);
        return handle;
    }

    void RenderHardwareInterface::SetLights(const LightData* lights, uint32_t count)
    {
        if (_isInitialized) _buffer->SetLights(lights, count);
    }

    void RenderHardwareInterface::SetLightingSettings(const LightingSettings& settings)
    {
        if (_isInitialized) _buffer->SetLightingSettings(settings);
    }

    bool RenderHardwareInterface::Render(const DrawItem* items, uint32_t count, const RenderCommand* commands, uint32_t commandCount)
    {
        if (!_isInitialized) return false;

        // フレーム時間 = 前のフレームの開始からこのフレームの開始まで
        const auto frameStart = std::chrono::steady_clock::now();
        const double frameMs = _hasLastFrame ? std::chrono::duration<double, std::milli>(frameStart - _lastFrameStart).count() : 0.0;
        _lastFrameStart = frameStart;
        _hasLastFrame   = true;

        if (!_commandContext->RenderStart(_pipeline.get(), _buffer.get()))
        {
            return false;
        }

        // シーン : 描くものを用意してから、C# が組み立てた描画パスの命令を順に実行する
        // (以前はここでシーンをバックバッファに 1 回描くだけだった)
        const auto sceneStart = std::chrono::steady_clock::now();
        _commandContext->PrepareScene(_buffer.get(), items, count);
        _commandContext->ExecuteRenderCommands(_buffer.get(), commands, commandCount);
        _commandContext->MarkGpuSceneEnd();
        const double sceneMs = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - sceneStart).count();

        // プロファイラー (非表示の時は ImGui の処理を丸ごと省く)
        const auto uiStart = std::chrono::steady_clock::now();
        if (_debugUI.IsVisible())
        {
            _debugUI.BeginFrame();
            // プロファイラーから ExecuteIndirect の ON / OFF を切り替えられる (効果をその場で比べるため)
            // インスタンシングも同じように切り替えられる
            bool indirect   = _commandContext->IsIndirectDrawEnabled();
            bool instancing = _commandContext->IsInstancingEnabled();
            bool culling    = _commandContext->IsGpuCullingEnabled();
            bool occlusion  = _commandContext->IsOcclusionCullingEnabled();
            _profiler.Draw(&indirect, &instancing, &culling, &occlusion);
            _commandContext->SetOcclusionCullingEnabled(occlusion);
            _commandContext->SetIndirectDrawEnabled(indirect);
            _commandContext->SetInstancingEnabled(instancing);
            _commandContext->SetGpuCullingEnabled(culling);
            // ImGui の PSO は深度無しで作られているので、描画先をバックバッファだけにしてから積む
            _commandContext->BindBackBufferWithoutDepth();
            _debugUI.EndFrame(_commandContext->GetFrameCommandList());
        }
        const double uiMs = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - uiStart).count();

        // 計測値をまとめる (GPU / Present / 待ち時間は CommandContext が測ったもの。表示は 1 フレーム遅れ)
        FrameStats stats    = _commandContext->GetStats();
        stats.FrameMs       = frameMs;
        stats.SceneRecordMs = sceneMs;
        stats.UiMs          = uiMs;
        stats.Objects       = _buffer->GetObjectCount();
        if (frameMs > 0.0) _profiler.AddFrame(stats);

        return _commandContext->RenderEnd();
    }

    void RenderHardwareInterface::SetIndirectDrawEnabled(bool enabled)
    {
        if (_commandContext) _commandContext->SetIndirectDrawEnabled(enabled);
    }

    bool RenderHardwareInterface::IsIndirectDrawEnabled()const
    {
        return _commandContext ? _commandContext->IsIndirectDrawEnabled() : false;
    }

    void RenderHardwareInterface::SetInstancingEnabled(bool enabled)
    {
        if (_commandContext) _commandContext->SetInstancingEnabled(enabled);
    }

    bool RenderHardwareInterface::IsInstancingEnabled()const
    {
        return _commandContext ? _commandContext->IsInstancingEnabled() : false;
    }

    void RenderHardwareInterface::SetGpuCullingEnabled(bool enabled)
    {
        if (_commandContext) _commandContext->SetGpuCullingEnabled(enabled);
    }

    bool RenderHardwareInterface::IsGpuCullingEnabled()const
    {
        return _commandContext ? _commandContext->IsGpuCullingEnabled() : false;
    }

    void RenderHardwareInterface::SetOcclusionCullingEnabled(bool enabled)
    {
        if (_commandContext) _commandContext->SetOcclusionCullingEnabled(enabled);
    }

    bool RenderHardwareInterface::IsOcclusionCullingEnabled()const
    {
        return _commandContext ? _commandContext->IsOcclusionCullingEnabled() : false;
    }

    void RenderHardwareInterface::WaitForGpu()
    {
        if (_commandContext) _commandContext->WaitForGpu();
    }
}

/************************************
* End of file
************************************/
