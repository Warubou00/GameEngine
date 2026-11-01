
/***********************************************************************************
* @file    RenderHardwareInterface.h
* @brief   RHI構造
* @details 公開インターフェイス IRenderHardwareInterface (Public/rhi.h) の DirectX12 実装。
*          このクラス自体は RHI.dll の外には出さない (RHI_CreateRenderHardwareInterface で生成する)
*
* @author  Keigo Onari
* @date    2026/1/13
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    以前はモデルの読み込みやカメラ (Renderer の型) もここで扱っていたため、RHI が Renderer に依存していた。
*          それらは Renderer::SceneRenderer に移し、ここは GPU の仕事だけにした (Phase 3)
************************************************************************************/

#pragma once
#ifndef RENDER_HARDWARE_INTERFACE_H_
#define RENDER_HARDWARE_INTERFACE_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <chrono>
#include <memory>
#include "Public/rhi.h"
#include "Profiler/debugUI.h"
#include "Profiler/profiler.h"

/******************************************************************
* Structure Declarations
******************************************************************/

namespace RHI
{

    namespace ObjectInterface
    {
        class Device;
        class CommandContext;
        class Buffer;
        class Pipeline;
    }

    class RenderHardwareInterface final : public IRenderHardwareInterface
    {
    public:          /*---コンストラクタ・デストラクタ---*/
        RenderHardwareInterface();
        ~RenderHardwareInterface()override;

        RenderHardwareInterface(const RenderHardwareInterface&) = delete;
        RenderHardwareInterface& operator=(const RenderHardwareInterface&) = delete;

    public:          /*---IRenderHardwareInterface---*/

        bool Initialize(const InitializeDesc& desc, InitializeResult* outResult)override;
        void Shutdown()override;
        bool IsInitialized()const override { return _isInitialized; }

        bool     Resize(uint32_t width, uint32_t height)override;
        uint32_t GetWidth()const override;
        uint32_t GetHeight()const override;

        VertexRange  UploadVertices(const Vertex* vertices, uint32_t count)override;
        IndexRange   UploadIndices(const uint32_t* indices, uint32_t count)override;
        void         LoadTextures(const wchar_t* const* filePaths, uint32_t count, TextureHandle* outHandles)override;
        ObjectHandle CreateObject()override;
        void         UpdateObject(ObjectHandle object, const ObjectConstants& constants)override;
        void         SetViewProjection(const ::DirectX::XMFLOAT4X4& view, const ::DirectX::XMFLOAT4X4& projection)override;

        bool Render(const DrawItem* items, uint32_t count)override;
        void WaitForGpu()override;

        void SetProfilerVisible(bool visible)override { _debugUI.SetVisible(visible); }
        bool IsProfilerVisible()const override         { return _debugUI.IsVisible(); }
        void ReportProfilerValue(const char* name, double value)override { _profiler.SetValue(name, value); }

    private:         /*---メンバ変数---*/
        // 破棄は Shutdown で「GPU 完了待ち → Pipeline / Buffer / CommandContext → Device」の順に行う
        std::unique_ptr<ObjectInterface::Device>         _device;
        std::unique_ptr<ObjectInterface::CommandContext> _commandContext;
        std::unique_ptr<ObjectInterface::Buffer>         _buffer;
        std::unique_ptr<ObjectInterface::Pipeline>       _pipeline;

        // プロファイラー (ImGui)
        DebugUI  _debugUI;
        Profiler _profiler;
        std::chrono::steady_clock::time_point _lastFrameStart;
        bool     _hasLastFrame = false;

        bool _isInitialized = false;     // 初期化が最後まで成功したか
    };

}


#endif // RENDER_HARDWARE_INTERFACE_H_

/************************************
* End of file
************************************/
