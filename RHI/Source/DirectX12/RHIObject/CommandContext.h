
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
#include <DirectXMath.h>
#include <unordered_map>
#include <vector>
#include <memory>
#include <d3d12.h>
#include <wrl/client.h>


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
        class DescriptorHeapManager;
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
             * @brief シーンの準備 (RenderStart と ExecuteRenderCommands の間で、1 フレームに 1 回呼ぶ)
             * @details オブジェクト / マテリアル / ライトを GPU へ送り、DrawData と描画の引数を用意する。
             *          描画ごとのルート引数は設定しない。各描画は StartInstanceLocation = DrawData の先頭の番号で描き、
             *          シェーダーが DrawData[DRAWID] (頂点の開始位置 / オブジェクトの番号) を読む。
             *          インスタンシングが有効なら、同じメッシュを最大 INSTANCE_BATCH_SIZE 個ずつ 1 回の描画にまとめる。
             *          描画リストが前回このフレームで描いたものと同じなら、DrawData と引数の書き込みも省く
             *          (以前は Render で、準備とバックバッファへの描画を一緒に行っていた)
             ************************************************************************************/
            void PrepareScene(Buffer* buffer, const RHI::DrawItem* items, uint32_t count);

            /***********************************************************************************
             * @brief 描画パスの命令 (C# が組み立てた RenderCommand の列) を順に実行する
             * @details DrawScene の不透明で GPU カリング (2 段階のオクルージョン) を行い、半透明はその結果を使う。
             *          オクルージョンはシーンの深度 (SCENE_DEPTH) に描く時だけ (Hi-Z をその深度から作るため)
             *          終わると深度は DEPTH_WRITE に、描画先はバックバッファに戻る (この後に ImGui を描く)
             ************************************************************************************/
            void ExecuteRenderCommands(Buffer* buffer, const RHI::RenderCommand* commands, uint32_t count);

            // レンダーターゲット (画面に合わせる物は Resize で作り直す)。SRV はテクスチャと同じ配列に入る
            RHI::RenderTargetHandle CreateRenderTarget(Buffer* buffer, const RHI::RenderTargetDesc& desc);
            void                    DestroyRenderTarget(RHI::RenderTargetHandle handle);
            // MSAA で使えるサンプル数の上限 (HDR / R11G11B10F / 深度 がどれも対応している数。Initialize で調べる)
            uint32_t                GetMaxSampleCount() const { return _maxSampleCount; }
            // 影 : シャドーマップのレンダーターゲットを番号に直して Buffer に渡す (シャドーマップが消されたら影を止める)
            void                    SetShadowSettings(Buffer* buffer, const RHI::ShadowSettings& settings);
            // 確認用の線 (このフレームの描画の最後に、バックバッファへ描く)
            void                    SetDebugLines(const RHI::DebugLineVertex* vertices, uint32_t count);
            // シーンを描く大きさ (ED3。0 x 0 = 画面と同じ)。決めるとシーンは「見る用のテクスチャ」に描き、画面には UI を描く
            bool                    SetRenderSize(Buffer* buffer, uint32_t width, uint32_t height);
            void                    SetUIDrawData(const RHI::UIVertex* vertices, uint32_t vertexCount, const uint16_t* indices, uint32_t indexCount,
                                                  const RHI::UIDrawCommand* commands, uint32_t commandCount);
            uint32_t                CreateUITexture(Buffer* buffer, uint32_t width, uint32_t height, const uint8_t* rgba);
            static constexpr uint32_t MAX_RENDER_TARGETS = 64;

            // ==========================================================
            // GPU パーティクル (CommandContext_Particles.cpp)
            //   発生 / 更新 / 生死の管理 / 描画の引数を全部 GPU で行う。CPU はエミッターと出す数を送るだけ
            //   描画パスの命令 UpdateParticles / DrawParticles で実行する
            // ==========================================================
            // 容量 (2 の累乗に切り上げ。0 で使わない)。GPU の完了を待ってから作り直す (生きている物は消える)
            void SetParticleCapacity(uint32_t capacity);
            // エミッター (毎フレーム。UpdateParticles の時に GPU へ送る)
            void SetParticleEmitters(const RHI::ParticleEmitterData* emitters, uint32_t count, const RHI::ParticleFrameSettings& settings);
            const RHI::ParticleStats& GetParticleStats()const { return _particleStats; }

            /***********************************************************************************
             * @brief ExecuteIndirect で描くか (false なら 1 個ずつ DrawIndexedInstanced)
             * @details ExecuteIndirect : 描画の引数をバッファに並べ、1 回の命令で全部描く。
             *          描画ごとの API 呼び出し (Draw) が無くなるので、描画数が多いほど CPU が軽くなる。
             *          コマンドシグネチャが作れなかった環境では自動で通常の描画になる
             ************************************************************************************/
            void SetIndirectDrawEnabled(bool enabled) { _useIndirectDraw = enabled; }
            bool IsIndirectDrawEnabled()const         { return _useIndirectDraw; }

            /***********************************************************************************
             * @brief インスタンシングするか (既定 : true)
             * @details 同じメッシュ (頂点の開始位置 / インデックスの範囲が同じ) の DrawItem を
             *          1 回の描画 (InstanceCount = 個数) にまとめる。オブジェクトごとの行列やテクスチャは
             *          DrawData でインスタンスごとに引くので、違っていてよい。
             *          描画リストが変わった時だけまとめ直す (同じなら前回の結果を使う)
             ************************************************************************************/
            void SetInstancingEnabled(bool enabled) { _useInstancing = enabled; }
            bool IsInstancingEnabled()const         { return _useInstancing; }

            /***********************************************************************************
             * @brief GPU カリング (視錐台カリング) をするか (既定 : true)
             * @details 毎フレーム、描く前にコンピュートシェーダーでカメラに映らないオブジェクトを除き、
             *          残ったものだけの引数を GPU が作って ExecuteIndirect で描く (CPU は結果を待たない)。
             *          ExecuteIndirect が前提なので、ExecuteIndirect が OFF の時は行わない
             ************************************************************************************/
            void SetGpuCullingEnabled(bool enabled) { _useGpuCulling = enabled; }
            bool IsGpuCullingEnabled()const         { return _useGpuCulling; }

            /***********************************************************************************
             * @brief オクルージョンカリングをするか (既定 : true)
             * @details 2 段階で行う。
             *            前段 : 前のフレームで見えていた物を描く (視錐台だけで判定)
             *            その深度から Hi-Z (深度の縮小ピラミッド) を作る (このフレームのカメラ)
             *            後段 : 全部を視錐台と Hi-Z で判定し直し、見えている物を記録して、前段で描かなかった物を描く
             *          GPU カリングが前提 (GPU カリングが OFF の時は行わない)。
             *          (以前は前のフレームの深度とカメラで判定していて、カメラが動くと新しく見えた物が 1 フレーム遅れて出ていた)
             ************************************************************************************/
            void SetOcclusionCullingEnabled(bool enabled) { _useOcclusion = enabled; }
            bool IsOcclusionCullingEnabled()const         { return _useOcclusion; }

            // インスタンシングで 1 回の描画にまとめる最大数
            // 全部を 1 回にまとめると、メッシュ A を画面全体に描いてからメッシュ B を描く順番になり、
            // 同じモデルの部品を続けて描く元の順番より GPU が遅くなった (画面上の近い場所を続けて描かなくなるため。
            // RTX 2070 / 20002 描画 : 元の順番 0.28 ms → メッシュ順 0.35 ms)。
            // そこで「同じメッシュが続く K 個」ずつまとめ、まとまりは元の順番に近い順で描く
            static constexpr uint32_t INSTANCE_BATCH_SIZE = 16;

            // 1 フレームで描けるオブジェクト (インスタンス) の最大数 (DrawData / 引数バッファ / 描画番号バッファの大きさ)
            // 以前は ExecuteIndirect だけの上限 (32768) で、超えた分は 1 個ずつ描いていた
            static constexpr uint32_t MAX_DRAWS = 65536;

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

            // ---------- 描画ごとのデータ ----------
            // シェーダーの struct DrawData と同じ並び (16byte)。描画番号 (DRAWID) で引かれる
            struct DrawData
            {
                uint32_t ObjectIndex;     // オブジェクトのデータ (StructuredBuffer<ObjectData>) の番号
                uint32_t BaseVertex;      // メガ頂点バッファ内の開始位置 (以前は描画ごとのルート定数 b0)
                uint32_t MaterialIndex;   // マテリアル (StructuredBuffer<MaterialData>) の番号
                uint32_t Padding;
            };
            static_assert(sizeof(DrawData) == 16, "DrawData must be 16 bytes (same as HLSL)");

            // 描き方 (PSO) のまとまりの数 (Pipeline::PIPELINE_GROUP_COUNT と同じ)。不透明 → 両面の不透明 → 半透明 の順に描く
            static constexpr uint32_t GROUP_COUNT = 3;
            // 2 つ目以降のまとまりの先頭を、この数の倍数にそろえる (カリングの詰め直しは 64 個ずつまとめて処理するため、
            // 64 個のまとまりの中に別の描き方が混ざらないようにする。間は空の描画で埋める)
            static constexpr uint32_t GROUP_ALIGNMENT = 64;
            // DrawFrame のバッファ内で、ExecuteIndirect の引数が始まる位置 (DrawData × MAX_DRAWS の後ろ)
            static constexpr UINT64 DRAW_ARGS_OFFSET = static_cast<UINT64>(MAX_DRAWS) * sizeof(DrawData);
            static constexpr UINT64 DRAW_ARGS_SIZE   = static_cast<UINT64>(MAX_DRAWS) * sizeof(D3D12_DRAW_INDEXED_ARGUMENTS);

            // GPU カリングの入力 : オブジェクト 1 個分 (gpuCulling.shader の CullInstance と同じ並び : 20byte)
            struct CullInstance
            {
                float    Sphere[4];   // メッシュのローカル座標での境界球 (w < 0 なら常に描く)
                uint32_t Draw;        // 属する描画 (引数) の番号
            };
            static_assert(sizeof(CullInstance) == 20, "CullInstance must be 20 bytes (same as HLSL)");
            // Staging / Gpu の中で CullInstance が始まる位置 (引数の後ろ)
            static constexpr UINT64 CULL_DATA_OFFSET = DRAW_ARGS_OFFSET + DRAW_ARGS_SIZE;
            // Culled (カリングの結果) の中でカウンターを置く位置 (引数の後ろ。引数の先頭からの距離)
            //   +0 : 映ったオブジェクトの数 (プロファイラー用)
            //   +4 : 詰めた不透明の描画の数 / +8 : 詰めた半透明の描画の数 (ExecuteIndirect の Count バッファ)
            static constexpr UINT64 CULL_COUNTER_OFFSET = DRAW_ARGS_SIZE;
            // Culled の中で、映った描画だけを詰めた引数が始まる位置 (カウンターの後ろ。256byte 境界にそろえる)
            static constexpr UINT64 COMPACT_ARGS_OFFSET = DRAW_ARGS_OFFSET + DRAW_ARGS_SIZE + 256;

            // GPU カリングのルート定数 (gpuCulling.shader の cbuffer CullConstants と同じ並び)
            struct CullConstants
            {
                float    Planes[6][4];
                uint32_t InstanceCount;
                uint32_t DrawCount;
                uint32_t CounterOffset;
                uint32_t HiZWidth;
                uint32_t HiZHeight;
                uint32_t HiZLevels;
                uint32_t Phase;              // 0 : 視錐台だけ / 1 : 前段 / 2 : 後段
                uint32_t ResetVisibility;    // 1 : 可視の記録が使えない (描画リストが変わった / 前のフレームでオクルージョンしていない)
                uint32_t AlphaDrawStart;     // 半透明の描画の先頭 (無ければ 0xFFFFFFFF)
                uint32_t DoubleSidedDrawStart;   // 両面の不透明の描画の先頭 (無ければ 0xFFFFFFFF)
            };

            // フレームごとの DrawData と ExecuteIndirect の引数 (GPU が前のフレームで読んでいる間に書き換えないよう分ける)
            //   Staging : UPLOAD ヒープ。CPU が書き込む
            //   Gpu     : DEFAULT ヒープ (VRAM)。GPU はこちらを読む
            //             描画リストが変わった時だけ Staging からコピーする。
            //             (UPLOAD ヒープを直接読ませると、GPU が毎フレーム PCIe 越しにメインメモリを読むので遅い)
            //   並び : [DrawData × MAX_DRAWS][IndirectDrawCommand × MAX_DRAWS][CullInstance × MAX_DRAWS]
            //   Culled   : DEFAULT ヒープ (UAV)。GPU カリングの結果
            //              [DrawData × MAX_DRAWS][描画ごとの引数 × MAX_DRAWS][カウンター][詰めた引数 × MAX_DRAWS]
            //   Readback : 映った数を CPU で読む (プロファイラー用。FRAME_COUNT フレーム後に読む)
            struct DrawFrame
            {
                std::unique_ptr<RHI::DirectX12::ResourceBuffer> Staging;
                std::unique_ptr<RHI::DirectX12::ResourceBuffer> Gpu;
                std::unique_ptr<RHI::DirectX12::ResourceBuffer> Culled;
                std::unique_ptr<RHI::DirectX12::ResourceBuffer> Readback;
                bool                       CullRecorded = false;    // このフレームでカリングを記録したか (読み戻しの有無)
                uint32_t                   CullPhases   = 0;        // カリングを何回したか (2 = オクルージョンの前段と後段。読み戻しは 2 回分を足す)
                uint8_t*                   Cpu       = nullptr;     // Staging を Map したままのアドレス
                // 前回このフレームに書いた内容。描画リストが同じなら書き込みを省く
                std::vector<RHI::DrawItem> LastItems;
                // 描画の引数の CPU 側の控え (1 個ずつ描く時に使う。UPLOAD ヒープは CPU から読むと非常に遅いため)
                std::vector<D3D12_DRAW_INDEXED_ARGUMENTS> Args;
                uint32_t                   Written   = 0;           // 引数の数 (まとまりの間を埋める空の描画を含む)
                uint32_t                   GroupStart[GROUP_COUNT] = {};   // 描き方 (PSO) ごとの引数の先頭
                uint32_t                   GroupCount[GROUP_COUNT] = {};   // 描き方ごとの描画の数 (空の描画は含まない)
                uint32_t                   Instances = 0;           // 描くオブジェクトの数 (DrawData の数)
                uint64_t                   Triangles = 0;
                bool                       Instanced = false;       // インスタンシングでまとめて書いたか
                uint64_t                   Layout    = 0;           // 書いた時の Buffer::GetDrawLayoutVersion (ED5)
                bool                       Valid     = false;
            };
            DrawFrame                                            _draws[FRAME_COUNT];
            // 描画番号バッファ (0, 1, 2, ... MAX_DRAWS-1)。インスタンス単位の入力 DRAWID として 1 回だけ設定する
            std::unique_ptr<RHI::DirectX12::ResourceBuffer>      _drawIdBuffer;
            bool                                                 _useIndirectDraw = true;
            bool                                                 _useInstancing   = true;
            bool                                                 _useGpuCulling   = true;
            bool                                                 _useOcclusion    = true;

            // ---------- オクルージョンカリング (Hi-Z) ----------
            //   _hiZ : 深度の縮小ピラミッド。段 0 = 画面の半分 (切り上げ)、最後の段 = 1x1 を 1 本に詰めて並べる
            //          1 個だけ持つ (同じキューで順番に実行されるので、前のフレームのカリングが読み終わってから作り直される)
            //   深度バッファは SRV で直接読む (以前はバッファに写していた : _depthCopy)
            //   SRV のディスクリプタは Buffer のヒープに 1 個だけ確保し、深度バッファを作り直した時に書き直す
            static constexpr uint32_t MAX_HIZ_LEVELS = 16;
            std::unique_ptr<RHI::DirectX12::ResourceBuffer>      _hiZ;
            uint32_t                                             _depthWidth   = 0;
            uint32_t                                             _depthHeight  = 0;
            uint32_t                                             _depthSrvIndex = 0xFFFFFFFFu;   // ヒープの番号 (未確保なら 0xFFFFFFFF)
            bool                                                 _depthSrvDirty = true;          // 深度バッファを作り直した (SRV を書き直す)
            uint32_t                                             _hiZLevels = 0;
            uint32_t                                             _hiZMipWidth[MAX_HIZ_LEVELS]  = {};
            uint32_t                                             _hiZMipHeight[MAX_HIZ_LEVELS] = {};
            uint32_t                                             _hiZMipOffset[MAX_HIZ_LEVELS] = {};   // 要素 (float) の位置
            D3D12_RESOURCE_STATES                                _hiZState    = D3D12_RESOURCE_STATE_COMMON;   // このフレームでの _hiZ の状態
            // オブジェクト (DrawData の番号) ごとに「前のフレームで見えていたか」。後段が書き、次のフレームの前段が読む
            std::unique_ptr<RHI::DirectX12::ResourceBuffer>      _visibility;
            D3D12_RESOURCE_STATES                                _visibilityState = D3D12_RESOURCE_STATE_COMMON;
            bool                                                 _visibilityValid = false;  // 前のフレームで後段まで行ったか
            bool                                                 _warnedTooManyDraws = false;

            // インスタンシングでまとめる時の作業用 (毎回確保し直さないようメンバーに持つ)
            struct MeshKey
            {
                uint32_t BaseVertex;
                uint32_t FirstIndex;
                uint32_t IndexCount;
                uint32_t Group;          // 描き方 (PSO)。同じメッシュでも描き方が違えば別の描画にする
                bool operator==(const MeshKey& o)const
                {
                    return BaseVertex == o.BaseVertex && FirstIndex == o.FirstIndex && IndexCount == o.IndexCount && Group == o.Group;
                }
            };
            struct MeshKeyHash
            {
                size_t operator()(const MeshKey& k)const
                {
                    uint64_t h = 14695981039346656037ull;
                    h = (h ^ k.BaseVertex) * 1099511628211ull;
                    h = (h ^ k.FirstIndex) * 1099511628211ull;
                    h = (h ^ k.IndexCount) * 1099511628211ull;
                    h = (h ^ k.Group)      * 1099511628211ull;
                    return static_cast<size_t>(h);
                }
            };
            // 1 回の描画 (インスタンスのまとまり)。DrawData の Start から Count 個を使う
            struct InstanceBatch
            {
                MeshKey  Key;
                uint32_t Count;      // = InstanceCount
                uint32_t Start;      // = StartInstanceLocation
                uint32_t Cursor;     // 書き込み中の位置
                uint32_t Draw;       // 引数の番号 (GPU カリングで使う)
                ::DirectX::XMFLOAT4 Sphere;   // メッシュの境界球
            };
            std::unordered_map<MeshKey, uint32_t, MeshKeyHash>   _openBatch;        // メッシュ → 書き込み中の _batches の番号
            std::vector<InstanceBatch>                           _batches;
            std::vector<uint32_t>                                _itemBatch;        // DrawItem ごとの _batches の番号
            Pipeline*                                            _currentPipeline = nullptr;   // RenderStart で受け取ったもの (所有しない)

            // ---------- レンダーターゲット (C# の RenderTexture) ----------
            struct RenderTarget
            {
                Microsoft::WRL::ComPtr<ID3D12Resource> Resource;
                DXGI_FORMAT           Format      = DXGI_FORMAT_UNKNOWN;
                float                 Scale       = 1.0f;     // 画面に対する倍率 (0 なら FixedWidth / FixedHeight)
                uint32_t              FixedWidth  = 0;
                uint32_t              FixedHeight = 0;
                uint32_t              Width       = 0;
                uint32_t              Height      = 0;
                float                 Clear[4]    = {};
                uint32_t              SrvIndex    = 0xFFFFFFFFu;   // ヒープの番号 (テクスチャの番号 = SrvIndex - SRV_INDEX_TEXTURE_BEGIN)
                D3D12_RESOURCE_STATES State       = D3D12_RESOURCE_STATE_RENDER_TARGET;   // フレームをまたいで持ち越す
                bool                  Alive       = false;

                // MSAA (SampleCount > 1) : Resource は MSAA (描画先)、Resolved は 1 サンプルの写し (SRV はこちらを指す)
                //   描画先にした後で全画面のシェーダーが読む時に、ResolveSubresource で写す (ResolveDirty)
                uint32_t              SampleCount = 1;
                bool                  IsDepth     = false;   // 深度だけ (Depth32。DSV + SRV。RTV は無い)
                uint32_t              ArraySize   = 1;       // 深度の配列の枚数 (DSV は 1 枚ずつ、SRV は Texture2DArray)
                Microsoft::WRL::ComPtr<ID3D12Resource> Resolved;
                D3D12_RESOURCE_STATES ResolvedState = D3D12_RESOURCE_STATE_PIXEL_SHADER_RESOURCE;
                bool                  ResolveDirty  = false;
            };
            std::vector<RenderTarget>                            _renderTargets;
            std::vector<uint32_t>                                _freeRenderTargets;
            std::unique_ptr<RHI::DirectX12::DescriptorHeap>      _targetRtvHeap;   // レンダーターゲットの RTV (MAX_RENDER_TARGETS 個)
            std::unique_ptr<RHI::DirectX12::DescriptorHeap>      _targetDsvHeap;   // 深度のレンダーターゲットの DSV (MAX_RENDER_TARGETS × MAX_DEPTH_SLICES 個。初めて作る時に用意)
            uint32_t                                             _boundDepthSlice = 0;   // 深度のレンダーターゲットの何枚目に描いているか
            static constexpr uint32_t                            SHADOW_UNION_VIEW = 0xFFFFFFF0u;   // _culledView : 全カスケードを囲む箱で数えた
            std::vector<RHI::DebugLineVertex>                    _debugLines;      // 確認用の線 (CPU 側の写し)
            std::unique_ptr<RHI::DirectX12::ResourceBuffer>      _debugLineBuffer[FRAME_COUNT];   // UPLOAD (フレームごと。初めて使う時に作る)
            void drawDebugLines();

            // ---------- エディター (ED3) ----------
            uint32_t _renderWidth  = 0;   // シーンを描く大きさ (0 = 画面と同じ)
            uint32_t _renderHeight = 0;
            uint32_t _viewTarget   = 0xFFFFFFFFu;   // シーンを描くテクスチャ (_renderTargets の番号。BACK_BUFFER の代わり)
            std::vector<RHI::UIVertex>      _uiVertices;
            std::vector<uint16_t>           _uiIndices;
            std::vector<RHI::UIDrawCommand> _uiCommands;
            std::unique_ptr<RHI::DirectX12::ResourceBuffer> _uiBuffer[FRAME_COUNT];   // UPLOAD (頂点 + インデックス。足りなければ作り直す)
            UINT64 _uiBufferSize[FRAME_COUNT] = {};
            std::vector<std::unique_ptr<RHI::DirectX12::ResourceBuffer>> _uiTextures;
            void drawUI();
            bool recreateSceneTargets();   // 深度 / Hi-Z / 画面に合わせるレンダーターゲットを今の大きさで作り直す
            uint32_t                                             _shadowMapTarget = RHI::RenderTargetHandle::INVALID;   // 影を読むシャドーマップ
            Buffer*                                              _shadowBuffer    = nullptr;   // SetShadowSettings の Buffer (所有しない)
            // GPU カリングの結果 (Culled) が今どの視点のものか (カメラ 0 / 影 1〜。フレームの初めは無し)
            uint32_t                                             _culledView      = 0xFFFFFFFFu;
            D3D12_RESOURCE_STATES                                _culledState     = D3D12_RESOURCE_STATE_COMMON;
            RHI::DirectX12::DescriptorHeapManager*               _heapManager = nullptr;   // SRV の置き場 (Buffer が持つ。所有しない)

            // 今の描画先 (SetRenderTargets で決まる。PSO の形式もここから選ぶ)
            uint32_t                                             _boundColors[RHI::MAX_PASS_COLOR_TARGETS] = {};
            DXGI_FORMAT                                          _boundFormats[RHI::MAX_PASS_COLOR_TARGETS] = {};
            D3D12_CPU_DESCRIPTOR_HANDLE                          _boundRtvs[RHI::MAX_PASS_COLOR_TARGETS] = {};
            uint32_t                                             _boundColorCount = 0;
            uint32_t                                             _boundDepth      = RHI::RenderTargetHandle::INVALID;
            D3D12_RESOURCE_STATES                                _depthState      = D3D12_RESOURCE_STATE_DEPTH_WRITE;
            uint32_t                                             _boundSamples    = 1;       // 描画先の MSAA のサンプル数 (PSO の選択に使う)
            uint32_t                                             _boundWidth      = 0;       // 描画先の大きさ (ビューポートを戻す時に使う)
            uint32_t                                             _boundHeight     = 0;
            bool                                                 _boundDepthMsaa  = false;   // SCENE_DEPTH として MSAA の深度を付けている

            // ---------- MSAA の深度 (シーンの深度と同じ大きさ。MSAA の描画先に SCENE_DEPTH を付けた時に使う) ----------
            //   Hi-Z / SSAO / ディファードは 1 サンプルの深度 (_depthBuffer) を読むので、読む前に解決する
            //   (PSResolveDepth。サンプルの一番奥を _depthBuffer へ書く)
            Microsoft::WRL::ComPtr<ID3D12Resource>               _msaaDepth;
            uint32_t                                             _msaaDepthSamples  = 0;     // 0 = 未作成 (リサイズで作り直す)
            D3D12_RESOURCE_STATES                                _msaaDepthState    = D3D12_RESOURCE_STATE_DEPTH_WRITE;
            uint32_t                                             _msaaDepthSrvIndex = 0xFFFFFFFFu;
            bool                                                 _msaaDepthSrvDirty = true;
            bool                                                 _msaaDepthDirty    = false; // _depthBuffer より新しい (解決が要る)
            bool                                                 _sceneDepthReadThisFrame = false;   // 全画面のシェーダーが SCENE_DEPTH を読んだ
            bool                                                 _sceneDepthReadLastFrame = false;   // (Hi-Z を MSAA の深度から直接作るかの判断に使う)
            uint32_t                                             _maxSampleCount    = 1;

            // このフレームのシーン (PrepareScene で決まり、DrawScene が使う)
            struct SceneState
            {
                D3D12_GPU_VIRTUAL_ADDRESS ObjectData    = 0;
                bool                      Valid         = false;
                bool                      Reused        = false;   // DrawData を前回から使い回したか
                bool                      Indirect      = false;
                bool                      Culling       = false;
                bool                      Occlusion     = false;   // オクルージョンカリングできるか
                bool                      Culled        = false;   // このフレームでカリングを済ませたか (半透明はその結果を使う)
                bool                      OcclusionDone = false;   // 2 段階のオクルージョンを行ったか
            };
            SceneState                                           _scene;

            void setRenderTargets(const RHI::RenderCommand& command);
            void drawScene(Buffer* buffer, uint32_t scenePass, uint32_t queue, uint32_t view);
            void ensureShadowReadable();   // シャドーマップをピクセルシェーダーが読める状態へ (カメラの描画 / 全画面の前)
            void drawFullscreen(Buffer* buffer, const RHI::RenderCommand& command);
            bool createRenderTargetResource(RenderTarget& target, uint32_t index);
            bool ensureDepthSrv(Buffer* buffer);
            void transitionDepth(D3D12_RESOURCE_STATES after);

            // MSAA
            uint32_t supportedSampleCount(DXGI_FORMAT format, uint32_t requested) const;   // requested 以下で使える一番大きい数
            bool ensureMsaaDepth(uint32_t samples);
            void transitionMsaaDepth(D3D12_RESOURCE_STATES after);
            void resolveSceneDepth(Buffer* buffer);    // MSAA の深度 → _depthBuffer (描画先とビューポートは元に戻す)
            bool ensureMsaaDepthSrv(Buffer* buffer);   // MSAA の深度の SRV (解決 / Hi-Z が読む)
            bool hiZFromMsaa() const;                  // Hi-Z を MSAA の深度から直接作るか
            // Hi-Z を MSAA の深度から直接作るサンプル数の上限。8x では先に解決する方が速かった
            // (RTX 2070 / 1280x720 / SSAO なし : 2x 0.285 → 0.265 / 4x 0.309 → 0.294 / 8x 0.369 → 0.413 ms)
            static constexpr uint32_t HIZ_FROM_MSAA_MAX_SAMPLES = 4;
            void resolveTarget(RenderTarget& target);  // MSAA の色 → Resolved
            D3D12_CPU_DESCRIPTOR_HANDLE boundDsv() const;

            // DrawData と引数をこのフレームの領域に書いて VRAM へコピーする。戻り値 = 前回の内容を使い回したか
            bool updateDraws(DrawFrame& frame, Buffer* buffer, const RHI::DrawItem* items, uint32_t count);

            // DrawItem をまとまり (同じメッシュ・同じ描き方を最大 batchSize 個) に分けて、DrawData と引数を書く。戻り値 = 書いた DrawData の数
            // インスタンシングしない時は batchSize = 1 (以前は別の関数 writeDrawsDirect だった)
            uint32_t writeDraws(DrawFrame& frame, Buffer* buffer, const RHI::DrawItem* items, uint32_t count, uint32_t batchSize);

            // GPU カリングを記録する (Culled に映ったものだけの DrawData と引数ができる)
            //   phase : 0 = 視錐台だけ / 1 = オクルージョンの前段 / 2 = 後段 (Hi-Z を作った後)
            // planes : 視錐台 (視点ごと) / stats : 映った数をプロファイラー用に読み戻すか (カメラだけ)
            void recordCulling(DrawFrame& frame, Buffer* buffer, D3D12_GPU_VIRTUAL_ADDRESS objectData, uint32_t phase, bool resetVisibility,
                               const ::DirectX::XMFLOAT4* planes, bool stats);

            // オクルージョンカリング : 深度の写しと Hi-Z を作る (リサイズ時) / 前段の深度から Hi-Z を作る (毎フレーム)
            bool createHiZ(uint32_t width, uint32_t height);
            void buildHiZ(Buffer* buffer);
            void transitionHiZ(D3D12_RESOURCE_STATES after);
            void     warnTooManyDraws();

            // ---------- GPU パーティクル (CommandContext_Particles.cpp) ----------
            //   _particleData    : パーティクル本体 [2][容量] (64byte。シェーダーの struct Particle)。毎フレーム 入力 / 出力 を入れ替え、
            //                      生き残りを出力の先頭から詰めて書く (以前は本体 1 本 + 空きの番号の列 + (キー, 番号) の列 2 本)
            //   _particleKeys    : 生き残った物の (奥行きのキー, 出力の位置) [容量]。ソートしない時は描画がこれを読む
            //   _particleSorted  : _particleKeys を奥から順に並べた物 [容量]。ソートした時は描画がこれを読む
            //   _particleCounters: 数 (生存数 / 空きの数 / 出した数 ...)。Atomic で数えるので UAV のまま
            //   _particleArgs    : 更新の DispatchIndirect と 描画の ExecuteIndirect の引数 (GPU が書く)
            //   _particleEmitters: エミッター (UPLOAD からフレームごとに写す)
            struct ParticleBuffer
            {
                std::unique_ptr<RHI::DirectX12::ResourceBuffer> Resource;
                D3D12_RESOURCE_STATES                           State = D3D12_RESOURCE_STATE_COMMON;
            };
            ParticleBuffer                                       _particleData[2];
            ParticleBuffer                                       _particleKeys;
            ParticleBuffer                                       _particleSorted;
            ParticleBuffer                                       _particleDrawData;        // 描画用 32byte [容量] (更新が書き、頂点シェーダーが読む)
            ParticleBuffer                                       _particleCounters;
            ParticleBuffer                                       _particleArgs;
            ParticleBuffer                                       _particleEmitters;
            ParticleBuffer                                       _particleSortBins;        // 計数ソートの区間 [PARTICLE_SORT_BUCKETS]
            std::unique_ptr<RHI::DirectX12::ResourceBuffer>      _particleEmitterUpload;   // [FRAME_COUNT][MAX_PARTICLE_EMITTERS] (UPLOAD。Map したまま)
            std::unique_ptr<RHI::DirectX12::ResourceBuffer>      _particleReadback;        // [FRAME_COUNT][数 16 個] (READBACK)
            std::unique_ptr<RHI::DirectX12::ResourceBuffer>      _particleQuadIndices;     // 四角のインデックス 0,1,2,0,2,3 (uint16。UPLOAD。12byte だけなので写さない)
            std::vector<RHI::ParticleEmitterData>                _particleEmitterData;     // SetParticleEmitters の写し (UpdateParticles で送る)
            RHI::ParticleFrameSettings                           _particleSettings;
            RHI::ParticleStats                                   _particleStats;
            uint32_t                                             _particleCapacity      = 0;
            uint32_t                                             _particleSpawnRequested = 0;     // このフレームに出す数の合計 (CPU が頼んだ数)
            uint32_t                                             _particleSpawnHistory[FRAME_COUNT] = {};   // フレームごとの出す数 (ソートの数の上限を出すため)
            uint32_t                                             _particleAliveRead     = 0xFFFFFFFFu;    // 読み戻した生存数 (FRAME_COUNT フレーム前。不明なら 0xFFFFFFFF)
            uint32_t                                             _particleInput         = 0;      // 入力のパーティクル本体 (0 / 1)。最後の更新の出力 = 次の入力 = 描く物
            bool                                                 _particleDrawSorted    = false;  // 描く列 : true = _particleSorted / false = _particleKeys
            bool                                                 _particleHasAlpha      = false;  // 半透明のエミッターがある (ソートが要る)
            uint32_t                                             _particleFrameSeed     = 0;
            bool                                                 _particleNeedInit      = false;  // バッファを作った後 (空きリストを作る)
            bool                                                 _particleUpdated       = false;  // このフレームで UpdateParticles をしたか
            bool                                                 _particleDrawn         = false;  // このフレームで DrawParticles をしたか
            bool                                                 _particleEverUpdated   = false;  // 描く物がある (一度でも更新した)
            bool                                                 _particleReadbackRecorded[FRAME_COUNT] = {};
            bool                                                 _particleTimed[FRAME_COUNT] = {};   // このフレームでパーティクルのタイムスタンプを書いたか

            bool createParticleBuffers(uint32_t capacity);
            void releaseParticleBuffers();
            void transitionParticle(ParticleBuffer& buffer, D3D12_RESOURCE_STATES after, D3D12_RESOURCE_BARRIER* barriers, UINT& count);
            void readParticleResults();                 // RenderStart : FRAME_COUNT フレーム前の生存数 / GPU 時間を読む
            void updateParticles(Buffer* buffer);       // 命令 UpdateParticles
            void sortParticles(uint32_t upperBound);    // 出力の生存リストを、入力のリストへ写しながら奥行きで並べる (UpdateParticles の中)
            void drawParticles(Buffer* buffer);         // 命令 DrawParticles
            void finishParticleTimestamps();            // RenderEnd : タイムスタンプの欠けを埋める

            // GPU の時間の計測 (タイムスタンプ : 0 = 開始 / 1 = シーン終わり / 2 = フレーム終わり
            //                   3〜7 = パーティクル (更新の始め / ソートの始め / 更新の終わり / 描画の始め / 描画の終わり))
            static constexpr UINT TIMESTAMP_PARTICLE_BEGIN = 3;
            static constexpr UINT TIMESTAMP_PARTICLE_COUNT = 5;
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
