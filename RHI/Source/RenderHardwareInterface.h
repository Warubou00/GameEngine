
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
#include "dataDefinition.h"

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
        class Pipeline;
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
            uint32_t InitializedPipeline       = -1;
        };

    public:          /*---コンストラクタ・デストラクタ---*/
        RenderHardwareInterface();
        ~RenderHardwareInterface();


    public:          /*---クラスメソッド---*/

        void Initialize(RHIInitializeInfo* info, ObjectInterface::ObjectInfo* objectInfo);

        /// <summary>
        /// 描画開始 (失敗したら false。その場合は End を呼ばないこと)
        /// </summary>
        bool Begin();

        /// <summary>
        /// 描画終了
        /// </summary>
        bool End();

        /// <summary>
        /// 終了処理 <br/>
        /// GPUの完了を待ってから、Device を最後にして破棄する。2回呼んでも問題ない
        /// </summary>
        void CleanUp();

        /// <summary>
        /// 全ての初期化に成功しているか
        /// </summary>
        bool IsInitialized()const { return _isInitialized; }

        /// <summary>
        /// テクスチャのロード <br/>
        /// 定数バッファに転送するまで...
        /// </summary>
        void Test_LoadTexture();

        /// <summary>
        /// モデルのロード <br/>
        /// objファイルのロード <br/>
        /// テクスチャ作成まで
        /// </summary>
        void Test_LoadModel(float cameraPosX, float cameraPosY, float cameraPosZ);

        void TestRender();

        // デバッグ用処理確認
        void DebugExecute();

    public:          /*---アクセッサー関数群---*/


    private:         /*---メンバ変数---*/
        std::unique_ptr<ObjectInterface::Device>         _device;
        std::unique_ptr<ObjectInterface::CommandContext> _commandContext;
        std::unique_ptr<ObjectInterface::Buffer>         _buffer;
        std::unique_ptr<ObjectInterface::Texture>        _texture;
        std::unique_ptr<ObjectInterface::Pipeline>       _pipeline;

        RenderQueue  _renderQueue;
        RenderObject _renderObj;

        bool _isInitialized = false;     // 初期化が最後まで成功したか

        // レンダーターゲットの作成
        bool makeRenderTarget() { return false; }

    };

}


#endif // RENDER_HARDWARE_INTERFACE_H_

/************************************
* End of file
************************************/
