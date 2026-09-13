
/***********************************************************************************
* @file    RenderHardwareInterface.h
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
* @note    その他
************************************************************************************/

#pragma once
#ifndef RENDER_HARDWARE_INTERFACE_H_
#define RENDER_HARDWARE_INTERFACE_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <memory>

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
        class Device;
        class CommandContext;
        class Buffer;
        class Texture;
        struct ObjectInfo;
    }

    class RenderHardwareInterface
    {

        /******************************************************************
        * Internal Enumeration Declarations
        ******************************************************************/


        /******************************************************************
        * Internal Structure Declarations
        ******************************************************************/
    public:        
        struct RHIInitializeInfo
        {
            uint32_t InitializedDevice         = -1;
            uint32_t InitializedCommandContext = -1;
            uint32_t InitializedBuffer         = -1;
            uint32_t InitializedTexture        = -1;
        };

    public:          /*---コンストラクタ・デストラクタ---*/
        RenderHardwareInterface();
        ~RenderHardwareInterface();


    public:          /*---クラスメソッド---*/

        void Initialize(RHIInitializeInfo* info, ObjectInterface::ObjectInfo* objectInfo);

        /// <summary>
        /// デバイスの取得
        /// </summary>
        /// <returns>ObjectInterface::Device デバイス</returns>
        ObjectInterface::Device* GetDevice();

        /// <summary>
        /// コマンドコンテキストの取得
        /// </summary>
        /// <returns>ObjectInterface::CommandContext コマンドコンテキスト</returns>
        ObjectInterface::CommandContext* GetCommandContext();

        /// <summary>
        /// バッファーの取得
        /// </summary>
        /// <returns>ObjectInterface::Buffer バッファー</returns>
        ObjectInterface::Buffer* GetBuffer();

        /// <summary>
        /// テクスチャの取得
        /// </summary>
        /// <returns>ObjectInterface::Texture テクスチャ</returns>
        ObjectInterface::Texture* GetTexture();

        /// <summary>
        /// 描画開始
        /// </summary>
        void Begin();

        /// <summary>
        /// 描画終了
        /// </summary>
        void End();

        /// <summary>
        /// 終了処理
        /// </summary>
        void CleanUp();

    public:          /*---アクセッサー関数群---*/


    private:         /*---メンバ変数---*/
        std::unique_ptr<ObjectInterface::Device> _device;
        std::unique_ptr<ObjectInterface::CommandContext> _commandContext;
        std::unique_ptr<ObjectInterface::Buffer> _buffer;
        std::unique_ptr<ObjectInterface::Texture> _texture;

        // レンダーターゲットの作成
        bool makeRenderTarget();

    };

}


#endif // RENDER_HARDWARE_INTERFACE_H_

/************************************
* End of file
************************************/
