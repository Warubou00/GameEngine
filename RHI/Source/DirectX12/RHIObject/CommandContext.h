
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
             * @brief 描画命令を積む (RenderStart と RenderEnd の間で、1 フレームに 1 回呼ぶ)
             * @details 描画ごとのルート引数は設定しない。DrawData を並べ、各描画は StartInstanceLocation = その先頭の番号で描き、
             *          シェーダーが DrawData[DRAWID] (頂点の開始位置 / オブジェクトの番号) を読む。
             *          インスタンシングが有効なら、同じメッシュを最大 INSTANCE_BATCH_SIZE 個ずつ 1 回の描画にまとめる。
             *          描画リストが前回このフレームで描いたものと同じなら、DrawData と引数の書き込みも省く
             ************************************************************************************/
            void Render(Buffer* buffer, const RHI::DrawItem* items, uint32_t count);

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

            // 描き方 (PSO) のまとまりの数 (Pipeline::PIPELINE_GROUP_COUNT と同じ)。不透明 → 半透明 の順に描く
            static constexpr uint32_t GROUP_COUNT = 2;
            // 半透明の描画の先頭を、この数の倍数にそろえる (カリングの詰め直しは 64 個ずつまとめて処理するため、
            // 64 個のまとまりの中に不透明と半透明が混ざらないようにする。間は空の描画で埋める)
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

            // DrawData と引数をこのフレームの領域に書いて VRAM へコピーする。戻り値 = 前回の内容を使い回したか
            bool updateDraws(DrawFrame& frame, Buffer* buffer, const RHI::DrawItem* items, uint32_t count);

            // DrawItem をまとまり (同じメッシュ・同じ描き方を最大 batchSize 個) に分けて、DrawData と引数を書く。戻り値 = 書いた DrawData の数
            // インスタンシングしない時は batchSize = 1 (以前は別の関数 writeDrawsDirect だった)
            uint32_t writeDraws(DrawFrame& frame, Buffer* buffer, const RHI::DrawItem* items, uint32_t count, uint32_t batchSize);

            // GPU カリングを記録する (Culled に映ったものだけの DrawData と引数ができる)
            //   phase : 0 = 視錐台だけ / 1 = オクルージョンの前段 / 2 = 後段 (Hi-Z を作った後)
            void recordCulling(DrawFrame& frame, Buffer* buffer, D3D12_GPU_VIRTUAL_ADDRESS objectData, uint32_t phase, bool resetVisibility);

            // オクルージョンカリング : 深度の写しと Hi-Z を作る (リサイズ時) / 前段の深度から Hi-Z を作る (毎フレーム)
            bool createHiZ(uint32_t width, uint32_t height);
            void buildHiZ(Buffer* buffer);
            void transitionHiZ(D3D12_RESOURCE_STATES after);
            void     warnTooManyDraws();

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
