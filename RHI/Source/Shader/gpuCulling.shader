// ==============================================================
// gpuCulling.shader
//
// GPU カリング (視錐台カリング) のコンピュートシェーダー
//   CPU が用意した「描画ごとの引数」と「オブジェクトごとの DrawData」から、
//   カメラに映るオブジェクトだけを残した引数と DrawData を GPU が作る。
//   ExecuteIndirect はその結果をそのまま描く (CPU は結果を待たない)
//
//   CSClear   : 引数を写して InstanceCount を 0 にする (1 スレッド = 描画 1 回分)
//   CSCull    : オブジェクトの境界球を視錐台と比べ、映るものだけ自分の描画の範囲に追加する (1 スレッド = 1 オブジェクト)
//   CSCompact : 1 個でも映った描画の引数だけを前から詰めて並べ、その数を数える (1 スレッド = 描画 1 回分)
//               ExecuteIndirect はその数 (Count バッファ) だけ描く。以前は映らなかった描画も InstanceCount = 0 で残っていた
//
//   オクルージョンカリングは 2 段階で行う (g_Phase)
//     0 : 視錐台だけ (オクルージョンカリングしない時)
//     1 : 前段 : 前のフレームで見えていた物 (g_Visibility = 1) を、視錐台だけで判定して描く
//     2 : 後段 : 前段の深度で作った Hi-Z (このフレームのカメラ) で全部を判定し直し、
//                見えている物を g_Visibility に記録して、そのうち前段で描かなかった物だけを描く
//   以前は前のフレームの深度とカメラで判定していたため、カメラが動くと新しく見えた物が 1 フレーム遅れて出ていた。
//   2 段階にすると、Hi-Z がこのフレームのカメラなので遅れが無い
//
// ※この文字コードは UTF-8 (BOMなし)。FXC は BOM を読めないため BOM を付けないこと
// ※バインディングは Pipeline.cpp の makeCullingPipeline と一致させること
// ==============================================================

// hlslTemplate.shader / CommandContext::DrawData と同じ (16byte)
struct DrawData
{
    uint ObjectIndex;
    uint BaseVertex;
    uint MaterialIndex;
    uint Padding;
};

// hlslTemplate.shader / Buffer::OBJECT_DATA_STRIDE と同じ (64byte)
struct ObjectData
{
    matrix World;
};

// CommandContext::CullInstance と同じ (20byte)
struct CullInstance
{
    float4 Sphere;   // メッシュのローカル座標での境界球 (xyz = 中心, w = 半径。負なら常に描く)
    uint   Draw;     // 自分が属する描画の番号 (引数の番号)
};

// [0] ルート定数 (CommandContext::CullConstants と同じ並び)
cbuffer CullConstants : register(b0)
{
    float4 g_Planes[6];        // 視錐台の 6 平面 (ワールド座標。内側が正、xyz は正規化済み)
    uint   g_InstanceCount;    // オブジェクトの数
    uint   g_DrawCount;        // 描画 (引数) の数
    uint   g_CounterOffset;    // g_OutArgs 内のカウンターの位置 (byte)。+0 : 映ったオブジェクトの数 / +4 : 詰めた描画の数 (Count バッファ)

    // ---------- オクルージョンカリング (Hi-Z) ----------
    uint   g_HiZWidth;         // Hi-Z の段 0 の大きさ (画面の半分、切り上げ)
    uint   g_HiZHeight;
    uint   g_HiZLevels;        // 段の数 (後段で使う)
    uint   g_Phase;            // 0 : 視錐台だけ / 1 : 前段 / 2 : 後段
    uint   g_ResetVisibility;  // 1 : g_Visibility が使えない (描画リストが変わった / 前のフレームでオクルージョンしていない)
    uint   g_AlphaDrawStart;   // この番号以降の描画は半透明 (64 の倍数。無ければ 0xFFFFFFFF)
                               // 半透明は不透明の後に描くので、前段では描かず、後段で (前段で描いたかに関係なく) 描く
                               // 詰め直しも不透明 / 半透明で分ける (カウンターと書く場所が別)
};

// [1] フレームの定数 (描画用の GlobalData と同じもの。後段で Hi-Z と比べる時に、このフレームのカメラで投影する)
// 以前は Hi-Z を作った時 (前のフレーム) の View × Projection をルート定数で渡していた
cbuffer FrameData : register(b1)
{
    matrix g_ViewMatrix;
    matrix g_ProjectionMatrix;
};

StructuredBuffer<ObjectData>   g_Objects  : register(t0);   // このフレームのオブジェクトの定数
StructuredBuffer<DrawData>     g_InDraws  : register(t1);   // CPU が並べた DrawData (全オブジェクト)
StructuredBuffer<CullInstance> g_Cull     : register(t2);   // 各オブジェクトの境界球と描画の番号
ByteAddressBuffer              g_InArgs   : register(t3);   // CPU が並べた引数 (D3D12_DRAW_INDEXED_ARGUMENTS : 20byte)
StructuredBuffer<float>        g_HiZ      : register(t4);   // Hi-Z (全段を詰めて並べたもの。hiZ.shader が作る)

RWStructuredBuffer<DrawData>   g_OutDraws : register(u0);   // 映ったものだけの DrawData (描画ごとの範囲の前から詰める)
RWByteAddressBuffer            g_OutArgs  : register(u1);   // 描画ごとの引数 (映らなかった描画は InstanceCount = 0) + 末尾にカウンター
RWByteAddressBuffer            g_Compact  : register(u2);   // 映った描画だけを詰めた引数 (ExecuteIndirect が読む)
RWStructuredBuffer<uint>       g_Visibility : register(u3); // オブジェクトごとに「前のフレームで見えていたか」(後段が書き、次のフレームの前段が読む)

static const uint ARGS_STRIDE = 20;   // D3D12_DRAW_INDEXED_ARGUMENTS
// 引数の中の位置 : 0 IndexCountPerInstance / 4 InstanceCount / 8 StartIndexLocation / 12 BaseVertexLocation / 16 StartInstanceLocation

// ==============================================================
// 引数を写して InstanceCount を 0 にする
// ==============================================================
[numthreads(64, 1, 1)]
void CSClear(uint3 id : SV_DispatchThreadID)
{
    const uint draw = id.x;
    if (draw == 0)
    {
        g_OutArgs.Store3(g_CounterOffset, uint3(0, 0, 0));   // 映ったオブジェクトの数 / 詰めた描画の数 (不透明 / 半透明)
    }
    if (draw >= g_DrawCount) return;

    const uint offset = draw * ARGS_STRIDE;
    uint4 args = g_InArgs.Load4(offset);   // IndexCount / InstanceCount / StartIndex / BaseVertexLocation
    args.y = 0;                            // 映ったものを CSCull が数え直す
    g_OutArgs.Store4(offset, args);
    g_OutArgs.Store(offset + 16, g_InArgs.Load(offset + 16));   // StartInstanceLocation (範囲の先頭) はそのまま
}

// ==============================================================
// オクルージョンカリング : 前段で描いた深度 (Hi-Z) に完全に隠れているか
//   1. ワールド座標の箱 (球を囲む立方体) の 8 頂点を、このフレームのカメラで画面に投影し、
//      画面上の四角形と一番手前の深度を求める (カメラの後ろにかかる時は判定しない = 描く)
//   2. 四角形が 2x2 texel 以内に収まる段を選び、その 4 texel の「一番奥」を読む
//   3. 物の一番手前がそれより奥なら、その範囲の全部の物の後ろにある = 隠れている
//   ※前段で描いた物は Hi-Z に自分の深度が入っているが、自分の一番手前はそれより奥にならないので、自分で自分を隠すことは無い
// ==============================================================
bool isOccluded(float3 center, float radius)
{
    float2 minNdc = float2( 1e9,  1e9);
    float2 maxNdc = float2(-1e9, -1e9);
    float  nearestZ = 1.0f;

    const matrix viewProj = mul(g_ViewMatrix, g_ProjectionMatrix);   // どちらも転置済みなので、この順で掛ける

    [unroll]
    for (uint i = 0; i < 8; ++i)
    {
        const float3 corner = center + radius * float3((i & 1) ? 1.0f : -1.0f, (i & 2) ? 1.0f : -1.0f, (i & 4) ? 1.0f : -1.0f);
        const float4 clip = mul(float4(corner, 1.0f), viewProj);
        if (clip.w <= 1e-4f) return false;   // カメラの後ろ (または真横) にかかる : 判定できないので描く
        const float3 ndc = clip.xyz / clip.w;
        minNdc   = min(minNdc, ndc.xy);
        maxNdc   = max(maxNdc, ndc.xy);
        nearestZ = min(nearestZ, ndc.z);
    }
    if (nearestZ <= 0.0f) return false;   // 手前のクリップ面にかかる

    // NDC → Hi-Z の段 0 の texel 座標 (画面の上が y = 0)。段 0 の 1 texel = 画面の 2x2 ピクセル
    const float2 size0 = float2(g_HiZWidth, g_HiZHeight);
    float2 minUv = float2(minNdc.x * 0.5f + 0.5f, 0.5f - maxNdc.y * 0.5f);
    float2 maxUv = float2(maxNdc.x * 0.5f + 0.5f, 0.5f - minNdc.y * 0.5f);
    minUv = saturate(minUv);
    maxUv = saturate(maxUv);
    const float2 minTexel = minUv * size0;
    const float2 maxTexel = maxUv * size0;

    // 四角形が 2 texel 以内に収まる段 : 段 l の 1 texel = 段 0 の 2^l texel
    const float extent = max(max(maxTexel.x - minTexel.x, maxTexel.y - minTexel.y), 1.0f);
    uint level = (uint)ceil(log2(extent));
    level = min(level, g_HiZLevels - 1);

    // その段の大きさと、バッファの中の先頭 (段 0 から順に詰めてある)
    uint width = g_HiZWidth, height = g_HiZHeight, offset = 0;
    for (uint l = 0; l < level; ++l)
    {
        offset += width * height;
        width  = max(1, (width  + 1) / 2);
        height = max(1, (height + 1) / 2);
    }

    const float scale = 1.0f / (float)(1u << level);
    const uint2 t0 = min((uint2)(minTexel * scale), uint2(width - 1, height - 1));
    const uint2 t1 = min((uint2)(maxTexel * scale), uint2(width - 1, height - 1));

    // 四角形にかかる texel (最大 2x2) の一番奥
    float farthest = 0.0f;
    for (uint y = t0.y; y <= t1.y; ++y)
    {
        for (uint x = t0.x; x <= t1.x; ++x)
        {
            farthest = max(farthest, g_HiZ[offset + y * width + x]);
        }
    }
    return nearestZ > farthest;
}

// ==============================================================
// 視錐台カリング (+ オクルージョンカリング)
// ==============================================================
// プロファイラー用の「映った数」は、グループの中で数えてから 1 回だけ全体のカウンターに足す
// (1 個ずつ全体のカウンターに足すと、同じ場所への atomic が集中して遅くなるため)
groupshared uint gs_Visible;

[numthreads(64, 1, 1)]
void CSCull(uint3 id : SV_DispatchThreadID, uint groupIndex : SV_GroupIndex)
{
    if (groupIndex == 0) gs_Visible = 0;
    GroupMemoryBarrierWithGroupSync();

    // ※途中で return しない (下の GroupMemoryBarrierWithGroupSync に全スレッドが来る必要がある)
    const uint index = id.x;
    bool visible = index < g_InstanceCount;

    CullInstance cull = (CullInstance)0;
    DrawData     draw = (DrawData)0;
    if (visible)
    {
        cull = g_Cull[index];
        draw = g_InDraws[index];

        // 前のフレームで見えていたか (前段で描いたか)
        const bool wasVisible = g_ResetVisibility == 0 && g_Visibility[index] != 0;
        bool       inFrustum  = true;
        bool       occluded   = false;

        // 境界球が無い (半径が負) ものは常に描く
        if (cull.Sphere.w >= 0.0f)
        {
            const matrix world = g_Objects[draw.ObjectIndex].World;

            // 中心をワールド座標へ (頂点シェーダーと同じ mul(ベクトル, 行列) の順)
            const float3 center = mul(float4(cull.Sphere.xyz, 1.0f), world).xyz;
            // 半径は一番大きく伸びている軸の拡大率を掛ける (不均一スケールでも見落とさないように)
            const float scale2 = max(max(dot(world[0].xyz, world[0].xyz), dot(world[1].xyz, world[1].xyz)), dot(world[2].xyz, world[2].xyz));
            const float radius = cull.Sphere.w * sqrt(scale2);

            // 6 平面のどれか 1 つの完全に外側なら映らない
            [unroll]
            for (uint i = 0; i < 6; ++i)
            {
                if (dot(g_Planes[i].xyz, center) + g_Planes[i].w < -radius) inFrustum = false;
            }

            // 後段だけ : 視錐台の中にあるものを、前段の深度 (Hi-Z) に隠れていないか調べる
            if (g_Phase == 2 && inFrustum && isOccluded(center, radius)) occluded = true;
        }

        const bool transparent = cull.Draw >= g_AlphaDrawStart;
        if (g_Phase == 0)
        {
            visible = inFrustum;                                    // 視錐台だけ
        }
        else if (g_Phase == 1)
        {
            visible = inFrustum && wasVisible && !transparent;      // 前段 : 前のフレームで見えていた不透明な物
        }
        else
        {
            // 後段 : 見えているかを記録し (次のフレームの前段で使う)、前段で描かなかった物だけ描く
            // 半透明は前段で描かないので、見えていれば必ずここで描く
            const bool nowVisible = inFrustum && !occluded;
            g_Visibility[index] = nowVisible ? 1 : 0;
            visible = nowVisible && (transparent || !wasVisible);
        }
    }

    if (visible)
    {
        // 映る : 自分の描画の InstanceCount を 1 増やし、その番号の場所に DrawData を書く
        const uint argsOffset = cull.Draw * ARGS_STRIDE;
        uint slot;
        g_OutArgs.InterlockedAdd(argsOffset + 4, 1, slot);
        const uint start = g_InArgs.Load(argsOffset + 16);
        g_OutDraws[start + slot] = draw;
        InterlockedAdd(gs_Visible, 1);
    }

    GroupMemoryBarrierWithGroupSync();
    if (groupIndex == 0 && gs_Visible > 0)
    {
        uint dummy;
        g_OutArgs.InterlockedAdd(g_CounterOffset, gs_Visible, dummy);
    }
}

// ==============================================================
// 描画数の詰め直し : 1 個でも映った描画の引数だけを g_Compact に前から並べる
//   グループ (64 描画) の中では元の順番を保つ (各スレッドの書き込み位置をグループ内の前から数えて決める)。
//   グループ同士の順番は atomic の早い者勝ちになるが、64 個ずつのまとまりは保たれる
//   (描く順番を大きく崩すと、画面上の近い場所を続けて描かなくなって GPU が遅くなるため : インスタンシングの調査より)
//   SM 5.1 (FXC) でも動くよう、Wave 命令は使わず groupshared で数える
// ==============================================================
groupshared uint gs_Flag[64];
groupshared uint gs_Offset[64];
groupshared uint gs_Base;

[numthreads(64, 1, 1)]
void CSCompact(uint3 id : SV_DispatchThreadID, uint groupIndex : SV_GroupIndex)
{
    const uint draw = id.x;
    uint4 args = uint4(0, 0, 0, 0);
    uint  start = 0;
    bool  visible = false;
    if (draw < g_DrawCount)
    {
        const uint offset = draw * ARGS_STRIDE;
        args    = g_OutArgs.Load4(offset);        // CSCull が数え終わった InstanceCount が入っている
        start   = g_OutArgs.Load(offset + 16);
        visible = args.y > 0;
    }
    gs_Flag[groupIndex] = visible ? 1 : 0;
    GroupMemoryBarrierWithGroupSync();

    // 不透明 / 半透明のどちらの描画か (半透明の先頭は 64 の倍数なので、64 個のまとまりはどちらか一方だけ)
    // それぞれ別のカウンターに数え、別の場所 (自分の描き方の先頭から) に詰める
    const uint pipelineGroup = (id.x - groupIndex) >= g_AlphaDrawStart ? 1 : 0;
    const uint groupFirst    = pipelineGroup == 1 ? g_AlphaDrawStart : 0;

    // グループ内の書き込み位置 (前にある「映った描画」の数) と、全体の中の先頭をまとめて決める
    if (groupIndex == 0)
    {
        uint sum = 0;
        for (uint i = 0; i < 64; ++i)
        {
            gs_Offset[i] = sum;
            sum += gs_Flag[i];
        }
        uint base = 0;
        if (sum > 0) g_OutArgs.InterlockedAdd(g_CounterOffset + 4 + pipelineGroup * 4, sum, base);
        gs_Base = base;
    }
    GroupMemoryBarrierWithGroupSync();

    if (visible)
    {
        const uint dest = (groupFirst + gs_Base + gs_Offset[groupIndex]) * ARGS_STRIDE;
        g_Compact.Store4(dest, args);
        g_Compact.Store(dest + 16, start);
    }
}
