
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

    bool RenderHardwareInterface::SetRenderSize(uint32_t width, uint32_t height)
    {
        return _isInitialized && _commandContext->SetRenderSize(_buffer.get(), width, height);
    }

    void RenderHardwareInterface::SetUIDrawData(const UIVertex* vertices, uint32_t vertexCount, const uint16_t* indices, uint32_t indexCount,
                                                const UIDrawCommand* commands, uint32_t commandCount)
    {
        if (_isInitialized) _commandContext->SetUIDrawData(vertices, vertexCount, indices, indexCount, commands, commandCount);
    }

    uint32_t RenderHardwareInterface::CreateUITexture(uint32_t width, uint32_t height, const uint8_t* rgba)
    {
        return _isInitialized ? _commandContext->CreateUITexture(_buffer.get(), width, height, rgba) : 0xFFFFFFFFu;
    }

    // ==============================================================
    // SH2 : 自分のシェーダー / 3D テクスチャ
    // ==============================================================
    namespace
    {
        void copyError(const std::string& text, char* error, uint32_t capacity)
        {
            if (!error || capacity == 0) return;
            const size_t count = (std::min)(text.size(), static_cast<size_t>(capacity - 1));
            std::memcpy(error, text.data(), count);
            error[count] = 0;
        }
    }

    uint32_t RenderHardwareInterface::CreateCustomShader(const char* source, const CustomShaderSettings& settings, char* error, uint32_t errorCapacity)
    {
        if (!_isInitialized || !source) { copyError("Renderer is not initialized", error, errorCapacity); return 0; }
        std::string message;
        const uint32_t shader = _pipeline->CreateCustomShader(source, settings, message);
        copyError(message, error, errorCapacity);
        return shader;
    }

    bool RenderHardwareInterface::UpdateCustomShader(uint32_t shader, const char* source, const CustomShaderSettings& settings, char* error, uint32_t errorCapacity)
    {
        if (!_isInitialized || !source) { copyError("Renderer is not initialized", error, errorCapacity); return false; }
        // 前の PSO を GPU が使っているかもしれないので待つ (保存した時だけ呼ばれる)
        WaitForGpu();
        std::string message;
        const bool ok = _pipeline->UpdateCustomShader(shader, source, settings, message);
        copyError(message, error, errorCapacity);
        return ok;
    }

    void RenderHardwareInterface::DestroyCustomShader(uint32_t shader)
    {
        if (!_isInitialized) return;
        WaitForGpu();
        _pipeline->DestroyCustomShader(shader);
    }

    void RenderHardwareInterface::SetMaterialShader(MaterialHandle material, uint32_t shader, const float* params, uint32_t paramCount)
    {
        if (_isInitialized) _buffer->SetMaterialShader(material, shader, params, paramCount);
    }

    uint32_t RenderHardwareInterface::GetCustomDrawCount()const
    {
        return _isInitialized ? _commandContext->GetCustomDrawCount() : 0;
    }

    TextureHandle RenderHardwareInterface::CreateTexture3D(uint32_t width, uint32_t height, uint32_t depth, uint32_t channels, const uint8_t* data)
    {
        TextureHandle handle;
        if (!_isInitialized) return handle;
        handle.Index = _buffer->GetTexture()->CreateTexture3D(_device.get(), _commandContext.get(), _buffer.get(), width, height, depth, channels, data);
        return handle;
    }

    void RenderHardwareInterface::SetShadowSettings(const ShadowSettings& settings)
    {
        if (_isInitialized) _commandContext->SetShadowSettings(_buffer.get(), settings);
    }

    void RenderHardwareInterface::SetParticleCapacity(uint32_t capacity)
    {
        if (_isInitialized) _commandContext->SetParticleCapacity(capacity);
    }

    void RenderHardwareInterface::SetParticleEmitters(const ParticleEmitterData* emitters, uint32_t count, const ParticleFrameSettings& settings)
    {
        if (_isInitialized) _commandContext->SetParticleEmitters(emitters, count, settings);
    }

    ParticleStats RenderHardwareInterface::GetParticleStats() const
    {
        return _isInitialized ? _commandContext->GetParticleStats() : ParticleStats();
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

        // GPU パーティクル (使っている時だけ。GPU の値は FRAME_COUNT フレーム前)
        const ParticleStats particles = _commandContext->GetParticleStats();
        if (particles.Capacity > 0 && particles.Valid)
        {
            _profiler.SetValue("Particles alive", particles.Alive);
            _profiler.SetValue("Particles visible", particles.Visible);
            _profiler.SetValue("Particles emitters", particles.Emitters);
            _profiler.SetValue("GPU particles update (ms)", particles.UpdateMs);
            _profiler.SetValue("GPU particles sort (ms)", particles.SortMs);
            _profiler.SetValue("GPU particles draw (ms)", particles.DrawMs);
        }

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

    // ==============================================================
    // ED5 : エディターの読み直し / 解放
    // ==============================================================
    void RenderHardwareInterface::UpdateMaterial(MaterialHandle material, const MaterialDesc& desc)
    {
        if (!_isInitialized) return;
        _buffer->UpdateMaterial(material, desc);
    }

    void RenderHardwareInterface::ReleaseResources(const VertexRange* vertices, uint32_t vertexCount, const IndexRange* indices, uint32_t indexCount,
                                                   const MaterialHandle* materials, uint32_t materialCount, const TextureHandle* textures, uint32_t textureCount)
    {
        if (!_isInitialized) return;
        // 前のフレームの描画がまだ読んでいるかもしれないので、GPU が終わるのを待ってから返す
        // (エディターの操作でだけ呼ぶので、フレームの数を数えて遅らせて返す仕組みは作っていない)
        WaitForGpu();
        for (uint32_t i = 0; vertices  && i < vertexCount;   ++i) _buffer->FreeVertexData(vertices[i]);
        for (uint32_t i = 0; indices   && i < indexCount;    ++i) _buffer->FreeIndexData(indices[i]);
        for (uint32_t i = 0; materials && i < materialCount; ++i) _buffer->DestroyMaterial(materials[i]);
        for (uint32_t i = 0; textures  && i < textureCount;  ++i) _buffer->GetTexture()->ReleaseTexture(_buffer.get(), textures[i].Index);
    }

    // ==============================================================
    // AN4 : GPU スキニング (データは Buffer、曲げるのは CommandContext が描画の前に)
    // ==============================================================

    SkinRange RenderHardwareInterface::UploadSkin(const SkinVertex* vertices, uint32_t count)
    {
        if (!_isInitialized) return SkinRange();
        return _buffer->UploadSkinData(_device.get(), _commandContext.get(), vertices, count);
    }

    void RenderHardwareInterface::ReleaseSkin(SkinRange range)
    {
        if (!_isInitialized || !range.IsValid()) return;
        WaitForGpu();
        _buffer->FreeSkinData(range);
    }

    SkinHandle RenderHardwareInterface::CreateSkinInstance(VertexRange source, SkinRange skin, uint32_t boneCount)
    {
        if (!_isInitialized) return SkinHandle();
        return _buffer->CreateSkinInstance(source, skin, boneCount);
    }

    VertexRange RenderHardwareInterface::GetSkinOutput(SkinHandle instance)const
    {
        return _isInitialized ? _buffer->GetSkinOutput(instance) : VertexRange();
    }

    void RenderHardwareInterface::SetSkinPose(SkinHandle instance, const float* matrices, uint32_t boneCount)
    {
        if (_isInitialized) _buffer->SetSkinPose(instance, matrices, boneCount);
    }

    void RenderHardwareInterface::SetSkinBounds(SkinHandle instance, const float* sphere)
    {
        if (_isInitialized) _buffer->SetSkinBounds(instance, sphere);
    }

    void RenderHardwareInterface::DestroySkinInstance(SkinHandle instance)
    {
        if (!_isInitialized || !instance.IsValid()) return;
        WaitForGpu();   // 前のフレームが書いた / 読んでいる頂点の場所を返すので
        _buffer->DestroySkinInstance(instance);
    }

    void RenderHardwareInterface::GetSkinStats(uint32_t* outInstances, uint32_t* outVertices, float* outGpuMs)const
    {
        if (outInstances) *outInstances = _isInitialized ? _commandContext->GetSkinnedInstances() : 0;
        if (outVertices)  *outVertices  = _isInitialized ? _commandContext->GetSkinnedVertices() : 0;
        if (outGpuMs)     *outGpuMs     = _isInitialized ? _commandContext->GetSkinGpuMs() : 0.0f;
    }

    void RenderHardwareInterface::RequestSkinReadback(SkinHandle instance)
    {
        if (_isInitialized) _commandContext->RequestSkinReadback(_buffer->GetSkinOutput(instance));
    }

    uint32_t RenderHardwareInterface::ReadSkinReadback(Vertex* outVertices, uint32_t maxCount)
    {
        if (!_isInitialized) return 0;
        WaitForGpu();
        return _commandContext->ReadSkinReadback(outVertices, maxCount);
    }

    bool RenderHardwareInterface::ReloadShaders()
    {
        if (!_isInitialized) return false;
        WaitForGpu();   // 使っている PSO を消すので
        return _pipeline->ReloadShaders();
    }

    uint64_t RenderHardwareInterface::GetMeshBytesUsed()const
    {
        return _isInitialized ? _buffer->GetVertexBytesUsed() + _buffer->GetIndexBytesUsed() : 0;
    }
}

/************************************
* End of file
************************************/
