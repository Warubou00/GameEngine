
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
#include "DirectX12/RHIObject/Device.h"
#include "DirectX12/RHIObject/CommandContext.h"
#include "DirectX12/RHIObject/Buffer.h"
#include "DirectX12/RHIObject/Texture.h"
#include "DirectX12/RHIObject/Pipeline.h"

#include "DirectX12/dx12_descriptorHeapManager.h"

#include <dxgi1_5.h>

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
    RenderHardwareInterface::~RenderHardwareInterface() = default;
    
    void RenderHardwareInterface::Initialize(RHI::RenderHardwareInterface::RHIInitializeInfo* info, ObjectInterface::ObjectInfo* objInfo)
    {
        _device         = std::make_unique<ObjectInterface::Device>();
        _commandContext = std::make_unique<ObjectInterface::CommandContext>();
        _buffer         = std::make_unique<ObjectInterface::Buffer>();
        _texture        = std::make_unique<ObjectInterface::Texture>();
        _pipeline       = std::make_unique<ObjectInterface::Pipeline>();

        info->InitializedDevice         = _device->Initialize();
        info->InitializedCommandContext = _commandContext->Initialize(_device.get(), objInfo);
        info->InitializedBuffer         = _buffer->Initialize(_device.get(), _commandContext.get());
        //info->InitializedTexture        = _texture->Initialize();
        info->InitializedTexture        = _pipeline->Initialize(_device.get());

        std::vector<Vertex> quadVertices = {
            // 1つ目の三角形 (左上, 右上, 左下)
            { {-0.25f + VERTEX_OFFSET_X_1,  0.5f, 0.0f}, {1.0f, 0.0f, 0.0f, 1.0f} }, // 左上 (赤)
            { { 0.25f + VERTEX_OFFSET_X_1,  0.5f, 0.0f}, {0.0f, 1.0f, 0.0f, 1.0f} }, // 右上 (緑)
            { {-0.25f + VERTEX_OFFSET_X_1, -0.5f, 0.0f}, {0.0f, 0.0f, 1.0f, 1.0f} }, // 左下 (青)

            // 2つ目の三角形 (左下, 右上, 右下)
            { {-0.25f + VERTEX_OFFSET_X_1, -0.5f, 0.0f}, {0.0f, 0.0f, 1.0f, 1.0f} }, // 左下 (青)
            { { 0.25f + VERTEX_OFFSET_X_1,  0.5f, 0.0f}, {0.0f, 1.0f, 0.0f, 1.0f} }, // 右上 (緑)
            { { 0.25f + VERTEX_OFFSET_X_1, -0.5f, 0.0f}, {1.0f, 1.0f, 1.0f, 1.0f} }, // 右下 (白)

            // 3つ目の三角形 (左上, 右上, 左下)
            { {-0.25f + VERTEX_OFFSET_X_2,  0.5f, 0.0f}, {1.0f, 0.0f, 0.0f, 1.0f} }, // 左上 (赤)
            { { 0.25f + VERTEX_OFFSET_X_2,  0.5f, 0.0f}, {0.0f, 1.0f, 0.0f, 1.0f} }, // 右上 (緑)
            { {-0.25f + VERTEX_OFFSET_X_2, -0.5f, 0.0f}, {0.0f, 0.0f, 1.0f, 1.0f} }, // 左下 (青)

            // 4つ目の三角形 (左下, 右上, 右下)
            { {-0.25f + VERTEX_OFFSET_X_2, -0.5f, 0.0f}, {0.0f, 0.0f, 1.0f, 1.0f} }, // 左下 (青)
            { { 0.25f + VERTEX_OFFSET_X_2,  0.5f, 0.0f}, {0.0f, 1.0f, 0.0f, 1.0f} }, // 右上 (緑)
            { { 0.25f + VERTEX_OFFSET_X_2, -0.5f, 0.0f}, {1.0f, 1.0f, 1.0f, 1.0f} }  // 右下 (白)
        };

        // バッファへ転送
        _buffer->UploadVertexData(_device.get(), _commandContext.get(), quadVertices);

        return;
    }

    /// <summary>
    /// デバイスの取得
    /// </summary>
    /// <returns>ObjectInterface::Device デバイス</returns>
    inline ObjectInterface::Device* RenderHardwareInterface::GetDevice()
    {
        return _device.get();
    }

    /// <summary>
    /// コマンドコンテキストの取得
    /// </summary>
    /// <returns>ObjectInterface::CommandContext コマンドコンテキスト</returns>
    inline ObjectInterface::CommandContext* RenderHardwareInterface::GetCommandContext()
    {
        return _commandContext.get();
    }

    /// <summary>
    /// バッファーの取得
    /// </summary>
    /// <returns>ObjectInterface::Buffer バッファー</returns>
    inline ObjectInterface::Buffer* RenderHardwareInterface::GetBuffer()
    {
        return _buffer.get();
    }

    /// <summary>
    /// テクスチャの取得
    /// </summary>
    /// <returns>ObjectInterface::Texture テクスチャ</returns>
    inline ObjectInterface::Texture* RenderHardwareInterface::GetTexture()
    {
        return _texture.get();
    }

    void RenderHardwareInterface::Begin()
    {

    }

    void RenderHardwareInterface::End()
    {

    }

    void RenderHardwareInterface::CleanUp()
    {

    }

    void RenderHardwareInterface::TestRender()
    {
        std::vector<RHI::ObjectInterface::CommandContext::RenderObj> renderQueue;
        RHI::ObjectInterface::CommandContext::RenderObj renderObj1 = {}, renderObj2 = {};
        renderObj1.VertexCount  = 6;
        renderObj1.CbOffset     = 0;
        renderObj1.VertexOffset = 0;
        renderObj1.VertexCount  = 6;
        renderObj2.CbOffset     = 0;
        renderObj2.VertexOffset = 6;

        renderQueue.push_back(renderObj1);
        renderQueue.push_back(renderObj2);

        _commandContext->RenderStart(_pipeline.get(), _buffer.get());
        _commandContext->Render(_buffer.get(), renderQueue);
        _commandContext->RenderEnd();
    }

}

/************************************
* End of file
************************************/
