
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
#include "dataDefinition.h"

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
#include "../../Renderer/Source/camera.h"
#include "../../Renderer/Source/WVP/projectionMatrix.h"
#include "../../Renderer/Source/mesh.h"
#include "../../Renderer/Source/constant.h"

/******************************************************************
* Macro Definitions
******************************************************************/
#define VERTEX_OFFSET_X_1 (-0.2f)
#define VERTEX_OFFSET_X_2 (0.2f)

/******************************************************************
* Enumeration Definitions
******************************************************************/


/******************************************************************
* Structure Declarations
******************************************************************/



namespace RHI
{
    RenderHardwareInterface::RenderHardwareInterface() = default;
    RenderHardwareInterface::~RenderHardwareInterface()
    {
        // CleanUp を呼び忘れても、GPUの完了を待ってから正しい順番で破棄されるようにする
        CleanUp();
    }
    
    void RenderHardwareInterface::Initialize(RHI::RenderHardwareInterface::RHIInitializeInfo* info, ObjectInterface::ObjectInfo* objInfo)
    {
#ifdef _DEBUG
        RHI::DirectX12::EnableDebugLayer();
#endif

        _device         = std::make_unique<ObjectInterface::Device>();
        _commandContext = std::make_unique<ObjectInterface::CommandContext>();
        _buffer         = std::make_unique<ObjectInterface::Buffer>();
        _texture        = std::make_unique<ObjectInterface::Texture>();
        _pipeline       = std::make_unique<ObjectInterface::Pipeline>();

        _isInitialized = false;

        // 以前は前の段階が失敗していても次へ進み、壊れたデバイスで初期化を続けていた
        // → 失敗した段階で止める (それ以降の項目は -1 のまま = 未初期化)
        info->InitializedDevice = _device->Initialize();
        if (info->InitializedDevice != RHI_SUCCEEDED) { Core::Logger::Log("[RHI] Failed initialize Device."); return; }

        info->InitializedCommandContext = _commandContext->Initialize(_device.get(), objInfo);
        if (info->InitializedCommandContext != RHI_SUCCEEDED) { Core::Logger::Log("[RHI] Failed initialize CommandContext."); return; }

        info->InitializedBuffer = _buffer->Initialize(_device.get(), _commandContext.get());
        if (info->InitializedBuffer != RHI_SUCCEEDED) { Core::Logger::Log("[RHI] Failed initialize Buffer."); return; }

        info->InitializedTexture = RHI_SUCCEEDED;

        info->InitializedPipeline = _pipeline->Initialize(_device.get());
        if (info->InitializedPipeline != RHI_SUCCEEDED) { Core::Logger::Log("[RHI] Failed initialize Pipeline."); return; }

        _isInitialized = true;


        //// TODO : 何かしらのモデルの読み込みがあった場合としての仮
        //// モデル1
        //std::vector<Renderer::Vertex> quadVertices1 = {
        //    // 1つ目の三角形 (左上, 右上, 左下)
        //    { {-0.25f + VERTEX_OFFSET_X_1,  0.5f, 0.0f}, {0.0f,0.0f,0.0f},{1.0f, 0.0f, 0.0f, 1.0f}, {0.0f, 0.0f} }, // 左上 (赤)
        //    { { 0.25f + VERTEX_OFFSET_X_1,  0.5f, 0.0f}, {0.0f,0.0f,0.0f},{0.0f, 1.0f, 0.0f, 1.0f}, {1.0f, 0.0f} }, // 右上 (緑)
        //    { {-0.25f + VERTEX_OFFSET_X_1, -0.5f, 0.0f}, {0.0f,0.0f,0.0f},{0.0f, 0.0f, 1.0f, 1.0f}, {0.0f, 1.0f} }, // 左下 (青)

        //    // 2つ目の三角形 (左下, 右上, 右下)
        //    { {-0.25f + VERTEX_OFFSET_X_1, -0.5f, 0.0f}, {0.0f,0.0f,0.0f},{0.0f, 0.0f, 1.0f, 1.0f}, {0.0f, 1.0f} }, // 左下 (青)
        //    { { 0.25f + VERTEX_OFFSET_X_1,  0.5f, 0.0f}, {0.0f,0.0f,0.0f},{0.0f, 1.0f, 0.0f, 1.0f}, {1.0f, 0.0f} }, // 右上 (緑)
        //    { { 0.25f + VERTEX_OFFSET_X_1, -0.5f, 0.0f}, {0.0f,0.0f,0.0f},{1.0f, 1.0f, 1.0f, 1.0f}, {1.0f, 1.0f} }  // 右下 (白)
        //};

        //// モデル2
        //std::vector<Renderer::Vertex> quadVertices2 = {
        //    // 1つ目の三角形 (左上, 右上, 左下)
        //    { {-0.25f + VERTEX_OFFSET_X_2,  0.5f, 0.0f}, {0.0f,0.0f,0.0f},{1.0f, 0.0f, 0.0f, 1.0f}, {0.0f, 0.0f} }, // 左上 (赤)
        //    { { 0.25f + VERTEX_OFFSET_X_2,  0.5f, 0.0f}, {0.0f,0.0f,0.0f},{0.0f, 1.0f, 0.0f, 1.0f}, {1.0f, 0.0f} }, // 右上 (緑)
        //    { {-0.25f + VERTEX_OFFSET_X_2, -0.5f, 0.0f}, {0.0f,0.0f,0.0f},{0.0f, 0.0f, 1.0f, 1.0f}, {0.0f, 1.0f} }, // 左下 (青)

        //    // 2つ目の三角形 (左下, 右上, 右下)
        //    { {-0.25f + VERTEX_OFFSET_X_2, -0.5f, 0.0f}, {0.0f,0.0f,0.0f}, { 0.0f, 0.0f, 1.0f, 1.0f }, {0.0f, 1.0f} }, // 左下 (青)
        //    { { 0.25f + VERTEX_OFFSET_X_2,  0.5f, 0.0f}, {0.0f,0.0f,0.0f}, {0.0f, 1.0f, 0.0f, 1.0f}, {1.0f, 0.0f} }, // 右上 (緑)
        //    { { 0.25f + VERTEX_OFFSET_X_2, -0.5f, 0.0f}, {0.0f,0.0f,0.0f}, {1.0f, 1.0f, 1.0f, 1.0f}, {1.0f, 1.0f} }  // 右下 (白)
        //};

        ////// バッファへ転送
        //_buffer->UploadVertexData(_device.get(), _commandContext.get(), quadVertices1);
        //_buffer->UploadVertexData(_device.get(), _commandContext.get(), quadVertices2);

        //Test_LoadTexture();

        return;
    }


    bool RenderHardwareInterface::Begin()
    {
        if (!_isInitialized) return false;
        return _commandContext->RenderStart(_pipeline.get(), _buffer.get());
    }

    bool RenderHardwareInterface::End()
    {
        if (!_isInitialized) return false;
        return _commandContext->RenderEnd();
    }

    void RenderHardwareInterface::CleanUp()
    {
        // 破棄の順番が重要:
        //   1. CommandContext で GPU の完了を待つ (使用中のリソースを消さないため)
        //   2. GPU リソースを持つオブジェクトを破棄
        //   3. 最後に Device
        // 以前は Device を最初に Dispose していた (2-11)
        if (_commandContext) _commandContext->WaitForGpu();

        if (_texture)  _texture->Dispose();
        if (_pipeline) _pipeline->Dispose();
        if (_buffer)   _buffer->Dispose();
        if (_commandContext) _commandContext->Dispose();

        _texture.reset();
        _pipeline.reset();
        _buffer.reset();
        _commandContext.reset();

        if (_device) _device->Dispose();
        _device.reset();

        _renderQueue.RenderObjects.clear();
        _isInitialized = false;
    }

    void RenderHardwareInterface::Test_LoadTexture()
    {
        if (!_isInitialized) return;
        _texture->LoadTextureFromFile(_device.get(), _commandContext.get(), _buffer.get(), L"../Image/image.png");
    }

    void RenderHardwareInterface::Test_LoadModel(float cameraPosX, float cameraPosY, float cameraPosZ)
    {
        if (!_isInitialized) return;

        Core::Logger::Log("call Test_LoadModel()");
        auto mesh = _buffer->LoadModel(_device.get(), _commandContext.get(), L"../Model/box.obj");
        if (!mesh)
        {
            // 以前は読み込み失敗時に nullptr を参照していた
            Core::Logger::Log("[RHI] Failed load model.");
            return;
        }

        uint32_t offset = 0;
        uint32_t idx = 0;
        for (const auto& index : mesh->Indecies)
        {
            _renderObj.VertexOffset = 0;
            _renderObj.IndexOffset  = offset;
            _renderObj.VertexCount  = static_cast<uint32_t>(mesh->Vertices.size());
            _renderObj.IndexCount   = static_cast<uint32_t>(index.size());
            // Root CBV は 256byte 境界が必須。Buffer::LoadModel が書き込む間隔と同じ値を使う (1-9)
            _renderObj.CbOffset     = static_cast<uint32_t>(idx * ObjectInterface::Buffer::SCENE_CB_STRIDE);
            _renderQueue.RenderObjects.emplace_back(_renderObj);
            offset += static_cast<uint32_t>(index.size());
            idx++;
        }

        Core::Logger::Log("called Test_LoadModel()");

        Renderer::Camera camera;
        camera.Position = Core::Vector3(cameraPosX, cameraPosY, cameraPosZ);
        camera.Target   = Core::Vector3(0.0f, 0.0f, 0.0f);
        camera.Up       = Core::Vector3(0.0f, 1.0f, 0.0f);
        Core::Logger::Log("call UpdateViewProjection()");
        _buffer->UpdateViewProjection(camera, Renderer::Projection::ProjectionType::PROJECTION_TYPE_3D);
        Core::Logger::Log("called UpdateViewProjection()");
    }

    void RenderHardwareInterface::TestRender()
    {
        //RenderQueue renderQueue;
        //RenderObject renderObj1 = {}, renderObj2 = {};
        //renderObj1.VertexCount  = 6;
        //renderObj1.CbOffset     = 0;
        //renderObj1.VertexOffset = 0;
        //renderObj2.VertexCount  = 6;
        //renderObj2.CbOffset     = 0;
        //renderObj2.VertexOffset = 6;

        //renderQueue.RenderObjects.push_back(renderObj1);
        //renderQueue.RenderObjects.push_back(renderObj2);

        if (!Begin()) return;
        _commandContext->Render(_buffer.get(), _renderQueue);
        End();
    }

    void RenderHardwareInterface::DebugExecute()
    {
        if (!_isInitialized) return;
        _pipeline->InitCommandSignature(_device.get());
    }

}

/************************************
* End of file
************************************/
