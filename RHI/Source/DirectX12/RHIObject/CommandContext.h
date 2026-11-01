
/***********************************************************************************
* @file    CommandContext.h
* @brief   RHI CommandContext
* @details コマンドの記録・送信・画面表示・GPU同期をまとめて扱うクラス
*
* @author  Keigo Onari
* @date    2026/4/27
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    コマンドリストの状態遷移
*            [記録中] --ExecuteAndSync / RenderEnd--> [Close済み] --BeginRecording / RenderStart--> [記録中]
*          生成直後は記録中。アップロード処理などで何かを積む前には必ず BeginRecording() を呼ぶこと
************************************************************************************/

#pragma once
#ifndef RHI_COMMAND_CONTEXT_H_
#define RHI_COMMAND_CONTEXT_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <vector>
#include <d3d12.h>


#include "../../RHIObject/ICommandContext.h"
#include "../../dataDefinition.h"

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
    namespace DirectX12
    {
        class GraphicsCommandList;
        class CommandQueue;
        class CommandAllocator;
        class SwapChain;
        class Fence;
    }

    namespace ObjectInterface
    {
        class Device;
        class Pipeline;
        class Buffer;

        class CommandContext : public ICommandContext
        {

            /******************************************************************
            * Internal Enumeration Declarations
            ******************************************************************/


            /******************************************************************
            * Internal Structure Declarations
            ******************************************************************/
        public:



        public:          /*---コンストラクタ・デストラクタ---*/
            CommandContext();
            ~CommandContext()override;


        public:          /*---クラスメソッド---*/

            RHI_RESULT Initialize(RHI::ObjectInterface::IDevice* iDevice, ObjectInfo* info)override;

            /***********************************************************************************
             * @brief コマンドの記録を開始する
             * @details 既に記録中なら何もしない。Close済みならアロケーターとコマンドリストをリセットする。
             *          ※現状は送信のたびにGPUの完了を待っているので、ここでアロケーターをリセットしても安全。
             *            Phase 1 でフレームを重ねる(FrameContext)ようになったら、フレームごとのアロケーターに変える
             *
             * @return bool 記録を開始できたか
             ************************************************************************************/
            bool BeginRecording();

            /***********************************************************************************
             * @brief 描画の開始 (記録開始・バックバッファの準備・共通リソースのセット)
             *
             * @return bool 成功の有無
             ************************************************************************************/
            bool RenderStart(Pipeline* pipeline, Buffer* buffer);

            /***********************************************************************************
             * @brief 描画の終了 (送信・画面表示・GPU完了待ち)
             *
             * @return bool 成功の有無 (デバイスロスト時もfalse)
             ************************************************************************************/
            bool RenderEnd();

            void Render(Buffer* buffer, const RenderQueue& renderQueue);

            /***********************************************************************************
             * @brief 記録したコマンドを送信して、GPUの完了まで待つ
             * @details 終了後のコマンドリストはClose済みのまま。次に積む前に BeginRecording() を呼ぶこと
             *
             * @return bool 成功の有無
             ************************************************************************************/
            bool ExecuteAndSync();

            /***********************************************************************************
             * @brief GPUが今積まれている処理を全て終えるまで待つ (終了処理・リサイズ前に使う)
             ************************************************************************************/
            void WaitForGpu();

            /***********************************************************************************
             * @brief 終了処理。GPUの完了を待ってから内部オブジェクトを破棄する
             ************************************************************************************/
            void Dispose()override;

        public:          /*---アクセッサー関数群---*/

            RHI::DirectX12::GraphicsCommandList* GetGraphicsCommandList()const;
            RHI::DirectX12::CommandQueue*        GetCommandQueue()const;
            RHI::DirectX12::CommandAllocator*    GetCommandAllocator()const;
            RHI::DirectX12::SwapChain*           GetSwapChain()const;

            // スワップチェーン(=描画先)のサイズ
            uint32_t GetWidth()const;
            uint32_t GetHeight()const;

        private:         /*---メンバ変数---*/
            std::unique_ptr<RHI::DirectX12::GraphicsCommandList> _graphicsCommandList;
            std::unique_ptr<RHI::DirectX12::CommandQueue>        _commandQueue;
            std::unique_ptr<RHI::DirectX12::CommandAllocator>    _commandAllocator;
            std::unique_ptr<RHI::DirectX12::SwapChain>           _swapChain;
            std::unique_ptr<RHI::DirectX12::Fence>               _fence;

            Device*         _device            = nullptr;   // 所有しない (デバイスロストの原因取得に使う)
            ID3D12Resource* _currentBackBuffer = nullptr;   // 所有しない (スワップチェーンが保持している)
            UINT            _syncInterval      = 1;         // Presentに渡す値 0:VSyncなし 1:VSyncあり

            // デバイスロスト時に原因をログへ出す
            void logDeviceRemovedReason(HRESULT presentResult);
        };

    }
}

#endif // RHI_COMMAND_CONTEXT_H_

/************************************
* End of file
************************************/
