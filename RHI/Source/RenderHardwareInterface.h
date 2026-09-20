
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
    namespace APIInterface
    {
        class IDevice;
        class ICommandList;
        class ICommandQueue;
        class IDescriptorHeap;
        class IPipeline;
        class IResource;
        class IResourceBinding;
        class ISwapChain;
        class IFence;
    }

    namespace ObjectInterface
    {
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
        /// 描画開始
        /// </summary>
        void Begin();

        /// <summary>
        /// 描画処理
        /// </summary>
        void Render();

        /// <summary>
        /// 描画終了
        /// </summary>
        void End();

        /// <summary>
        /// 終了処理
        /// </summary>
        void CleanUp();

        void TestRender();

    public:          /*---アクセッサー関数群---*/


    private:         /*---メンバ変数---*/
        std::unique_ptr<RHI::APIInterface::IDevice>            _device;
        std::unique_ptr<RHI::APIInterface::ICommandList>       _cmdList;
        std::unique_ptr<RHI::APIInterface::ICommandQueue>      _cmdQueue;
        std::unique_ptr<RHI::APIInterface::IDescriptorHeap>    _descHeap;
        std::unique_ptr<RHI::APIInterface::IPipeline>          _pipeline;
        std::unique_ptr<RHI::APIInterface::IResource>          _resource;
        std::unique_ptr<RHI::APIInterface::IResourceBinding>   _resourceBinding;
        std::unique_ptr<RHI::APIInterface::ISwapChain>         _swapChain;
        std::unique_ptr<RHI::APIInterface::IFence>             _fence;

        // レンダーターゲットの作成
        bool makeRenderTarget() { return false; }
    };

}


#endif // RENDER_HARDWARE_INTERFACE_H_

/************************************
* End of file
************************************/
