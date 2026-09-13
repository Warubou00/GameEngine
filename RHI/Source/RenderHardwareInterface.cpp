
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
#include "Interface/Device.h"
#include "Interface/CommandContext.h"
#include "Interface/Buffer.h"
#include "Interface/Texture.h"

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
    RenderHardwareInterface::RenderHardwareInterface() = default;
    RenderHardwareInterface::~RenderHardwareInterface() = default;
    
    void RenderHardwareInterface::Initialize(RHI::RenderHardwareInterface::RHIInitializeInfo* info, RHI::RenderHardwareInterface::RHIInfo* rhiInfo)
    {
        _device = std::make_unique<ObjectInterface::Device>();
        _commandContext = std::make_unique<ObjectInterface::CommandContext>();
        _buffer = std::make_unique<ObjectInterface::Buffer>();
        _texture = std::make_unique<ObjectInterface::Texture>();

        HWND hWnd = static_cast<HWND>(rhiInfo->HWnd);
        info->InitializedDevice         = _device->Initialize();
        info->InitializedCommandContext = _commandContext->Initialize(_device.get(), &hWnd);
        info->InitializedBuffer         = _buffer->Initialize(_device.get(), _commandContext.get());
        info->InitializedTexture        = _texture->Initialize();

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

}

/************************************
* End of file
************************************/
