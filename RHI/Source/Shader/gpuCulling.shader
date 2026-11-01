// ==============================================================
// gpuCulling.shader
//
// GPU カリング (視錐台カリング) のコンピュートシェーダー
//   CPU が用意した「描画ごとの引数」と「オブジェクトごとの DrawData」から、
//   カメラに映るオブジェクトだけを残した引数と DrawData を GPU が作る。
//   ExecuteIndirect はその結果をそのまま描く (CPU は結果を待たない)
//
//   CSClear : 引数を写して InstanceCount を 0 にする (1 スレッド = 描画 1 回分)
//   CSCull  : オブジェクトの境界球を視錐台と比べ、映るものだけ自分の描画の範囲に追加する (1 スレッド = 1 オブジェクト)
//
// ※この文字コードは UTF-8 (BOMなし)。FXC は BOM を読めないため BOM を付けないこと
// ※バインディングは Pipeline.cpp の makeCullingPipeline と一致させること
// ==============================================================

// hlslTemplate.shader / CommandContext::DrawData と同じ (8byte)
struct DrawData
{
    uint ObjectIndex;
    uint BaseVertex;
};

// hlslTemplate.shader / Buffer::OBJECT_DATA_STRIDE と同じ (80byte)
struct ObjectData
{
    matrix World;
    uint   TextureID;
    uint3  Padding;
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
    uint   g_CounterOffset;    // g_OutArgs 内の「映った数」のカウンターの位置 (byte)
};

StructuredBuffer<ObjectData>   g_Objects  : register(t0);   // このフレームのオブジェクトの定数
StructuredBuffer<DrawData>     g_InDraws  : register(t1);   // CPU が並べた DrawData (全オブジェクト)
StructuredBuffer<CullInstance> g_Cull     : register(t2);   // 各オブジェクトの境界球と描画の番号
ByteAddressBuffer              g_InArgs   : register(t3);   // CPU が並べた引数 (D3D12_DRAW_INDEXED_ARGUMENTS : 20byte)

RWStructuredBuffer<DrawData>   g_OutDraws : register(u0);   // 映ったものだけの DrawData (描画ごとの範囲の前から詰める)
RWByteAddressBuffer            g_OutArgs  : register(u1);   // ExecuteIndirect が読む引数 (+ 末尾に映った数のカウンター)

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
        g_OutArgs.Store(g_CounterOffset, 0);
    }
    if (draw >= g_DrawCount) return;

    const uint offset = draw * ARGS_STRIDE;
    uint4 args = g_InArgs.Load4(offset);   // IndexCount / InstanceCount / StartIndex / BaseVertexLocation
    args.y = 0;                            // 映ったものを CSCull が数え直す
    g_OutArgs.Store4(offset, args);
    g_OutArgs.Store(offset + 16, g_InArgs.Load(offset + 16));   // StartInstanceLocation (範囲の先頭) はそのまま
}

// ==============================================================
// 視錐台カリング
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
                if (dot(g_Planes[i].xyz, center) + g_Planes[i].w < -radius) visible = false;
            }
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
