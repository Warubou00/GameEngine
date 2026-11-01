
/***********************************************************************************
* @file    CommandContext.h
* @brief   RHI CommandContext
* @details コマンドの記録・送信・画面表示・GPU同期・描画先(RTV/DSV)をまとめて扱うクラス
*
* @author  Keigo Onari
* @date    2026/4/27
* @version v_1.0.0
*
* @copyright
* Copyright (c) 2024 Keigo Onari. All rights reserved.
*
* @note    コマンドリストは用途ごとに 2 本ある
*
*          [フレーム用]   RenderStart → Render → RenderEnd で使う。
*                         FrameContext(フレームごとのアロケーター+フェンス値)を FRAME_COUNT 個持ち、
*                         GPU が前のフレームを処理している間に CPU が次のフレームを記録できる。
*                         (Phase 0 までは毎フレーム GPU の完了を待っていたため CPU と GPU が交互にしか動けなかった)
*
*            フレーム N  : CPU [記録 N  ] → GPU [実行 N  ]
*            フレーム N+1:                   CPU [記録 N+1] → GPU [実行 N+1]
*                                            ↑ GPU が N を実行中でも、N+1 の記録を始められる
*
*          [アップロード] UploadManager (COPY キュー) を使う。テクスチャ・頂点の転送用。
*                         積んだコピー命令は RenderEnd でまとめて送信し、描画キューは GPU 上で完了を待つ。
*                         (Phase 1 までは送信のたびに CPU で GPU の完了を待っていた)
************************************************************************************/

#pragma once
#ifndef RHI_COMMAND_CONTEXT_H_
#define RHI_COMMAND_CONTEXT_H_

/******************************************************************
* Include Headers
******************************************************************/
#include <vector>
#include <memory>
#include <d3d12.h>


#include "../../RHIObject/ICommandContext.h"
#include "../../Public/rhiTypes.h"     // DrawItem
#include "../../Profiler/frameStats.h"

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
        class DescriptorHeap;
        class ResourceBuffer;
        class UploadManager;
        class GpuTimer;
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

            // 1 フレーム分の記録に使うもの
            struct FrameContext
            {
                std::unique_ptr<RHI::DirectX12::CommandAllocator> Allocator;
                UINT64 FenceValue = 0;      // このフレームの送信後にシグナルした値 (0 = まだ送信していない)
            };

        public:          /*---定数---*/

            // 同時に処理中にできるフレーム数 (CPU が GPU より何フレーム先行できるか)
            // 増やすほど並列度は上がるが、入力遅延とメモリが増える。2 が一般的
            static constexpr uint32_t FRAME_COUNT = 2;

            // 深度バッファ
            static constexpr DXGI_FORMAT DEPTH_FORMAT      = DXGI_FORMAT_D32_FLOAT;
            static constexpr float       DEPTH_CLEAR_VALUE = 1.0f;   // 奥 (LESS 比較で手前のものが残る)

        public:          /*---コンストラクタ・デストラクタ---*/
            CommandContext();
            ~CommandContext()override;


        public:          /*---クラスメソッド---*/

            RHI_RESULT Initialize(RHI::ObjectInterface::IDevice* iDevice, ObjectInfo* info)override;

            // ==========================================================
            // フレーム描画
            // ==========================================================

            /***********************************************************************************
             * @brief 描画の開始
             * @details 1. スワップチェーンの待機ハンドルで、CPU が先行しすぎていれば待つ
             *          2. このフレームの FrameContext を GPU が使い終わるまで待つ (通常はすでに終わっている)
             *          3. アロケーターとコマンドリストをリセットし、描画先と共通リソースをセットする
             *
             * @return bool 成功の有無 (false の時は Render / RenderEnd を呼ばないこと)
             ************************************************************************************/
            bool RenderStart(Pipeline* pipeline, Buffer* buffer);

            /***********************************************************************************
             * @brief 描画の終了 (送信・画面表示・フェンス値の記録)
             * @details GPU の完了は待たない。次に同じ FrameContext を使う時 (FRAME_COUNT フレーム後) に待つ
             *
             * @return bool 成功の有無 (デバイスロスト時もfalse)
             ************************************************************************************/
            bool RenderEnd();

            /***********************************************************************************
             * @brief 描画命令を積む (RenderStart と RenderEnd の間で呼ぶ)
             * @details 同じ値のルート引数は設定し直さない。DrawItem を頂点の開始位置ごとに並べておくと効率が良い
             ************************************************************************************/
            void Render(Buffer* buffer, const RHI::DrawItem* items, uint32_t count);

            // ==========================================================
            // プロファイラー / ImGui 用 (RenderStart と RenderEnd の間で使う)
            // ==========================================================

            // 記録中のフレーム用コマンドリスト (ImGui の描画命令を積むのに使う)
            ID3D12GraphicsCommandList* GetFrameCommandList()const;

            // 描画先をバックバッファだけにする (深度を外す。ImGui の PSO は深度無しで作られているため)
            void BindBackBufferWithoutDepth();

            // GPU のタイムスタンプ : シーンの描画がここまで (ここから先は UI)
            void MarkGpuSceneEnd();

            // このフレームまでの計測値 (CPU の待ち / Present / 描画数 / GPU 時間 など)
            const RHI::FrameStats& GetStats()const { return _stats; }

            /***********************************************************************************
             * @brief 積んであるアップロードを送信し、描画キューがその完了を GPU 上で待つようにする
             * @details RenderEnd で毎フレーム自動で呼ばれる。ロード直後に描画せず使う場合に明示的に呼ぶ
             ************************************************************************************/
            void SubmitUploads();

            // ==========================================================
            // その他
            // ==========================================================

            /***********************************************************************************
             * @brief 描画先のサイズ変更 (ウィンドウのリサイズ時に呼ぶ)
             * @details GPU の完了を待ってから、スワップチェーン・RTV・深度バッファを作り直す
             *          width / height が 0 (最小化中) なら何もしない
             *
             * @return bool 成功の有無
             ************************************************************************************/
            bool Resize(uint32_t width, uint32_t height);

            /***********************************************************************************
             * @brief GPUが今積まれている処理を全て終えるまで待つ (終了処理・リサイズ前に使う)
             * @details 描画キューとアップロード (COPY キュー) の両方を待つ
             ************************************************************************************/
            void WaitForGpu();

            /***********************************************************************************
             * @brief 終了処理。GPUの完了を待ってから内部オブジェクトを破棄する
             ************************************************************************************/
            void Dispose()override;

        public:          /*---アクセッサー関数群---*/

            // アップロード (Buffer / Texture が転送に使う)
            RHI::DirectX12::UploadManager*       GetUploadManager()const;
            RHI::DirectX12::CommandQueue*        GetCommandQueue()const;
            RHI::DirectX12::SwapChain*           GetSwapChain()const;

            // スワップチェーン(=描画先)のサイズ
            uint32_t GetWidth()const;
            uint32_t GetHeight()const;

            // 今記録中 (または次に記録する) フレームの番号 0 〜 FRAME_COUNT-1
            uint32_t GetFrameIndex()const { return _frameIndex; }

        private:         /*---メンバ変数---*/
            std::unique_ptr<RHI::DirectX12::CommandQueue>        _commandQueue;
            std::unique_ptr<RHI::DirectX12::SwapChain>           _swapChain;
            std::unique_ptr<RHI::DirectX12::Fence>               _fence;

            // フレーム用
            FrameContext                                         _frames[FRAME_COUNT];
            std::unique_ptr<RHI::DirectX12::GraphicsCommandList> _frameCommandList;
            uint32_t                                             _frameIndex = 0;

            // アップロード (COPY キュー)
            std::unique_ptr<RHI::DirectX12::UploadManager>       _uploadManager;
            UINT64                                               _uploadFenceWaited = 0;   // 描画キューに待たせたアップロードのフェンス値

            // 描画先
            std::unique_ptr<RHI::DirectX12::DescriptorHeap>      _rtvHeap;       // バックバッファの数だけ
            std::unique_ptr<RHI::DirectX12::DescriptorHeap>      _dsvHeap;       // 1 つ
            std::unique_ptr<RHI::DirectX12::ResourceBuffer>      _depthBuffer;

            // GPU の時間の計測 (タイムスタンプ : 0 = 開始 / 1 = シーン終わり / 2 = フレーム終わり)
            std::unique_ptr<RHI::DirectX12::GpuTimer>            _gpuTimer;
            RHI::FrameStats                                      _stats;
            bool                                                 _allowTearing = false;

            Device*         _device            = nullptr;   // 所有しない (デバイスロストの原因取得・作り直しに使う)
            ID3D12Resource* _currentBackBuffer = nullptr;   // 所有しない (スワップチェーンが保持している)
            UINT            _syncInterval      = 1;         // Presentに渡す値 0:VSyncなし 1:VSyncあり

            // 描画先の作成 (初期化・リサイズ時)
            bool createRenderTargetViews();
            bool createDepthBuffer(uint32_t width, uint32_t height);

            // デバイスロスト時に原因をログへ出す
            void logDeviceRemovedReason(HRESULT presentResult);
        };

    }
}

#endif // RHI_COMMAND_CONTEXT_H_

/************************************
* End of file
************************************/
