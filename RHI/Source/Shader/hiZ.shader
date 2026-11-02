// ==============================================================
// hiZ.shader
//
// Hi-Z (深度の縮小ピラミッド) を作るコンピュートシェーダー (オクルージョンカリング用)
//   描き終わった深度バッファを、2x2 の「一番奥 (値が大きい)」を取りながら半分ずつ縮小していく。
//   Hi-Z の 1 texel は「その範囲で一番奥にある物の深度」になるので、
//   ある物の一番手前がそれより奥なら、その範囲では完全に隠れていると言える。
//
//   すべての段は 1 本のバッファ (float) に詰めて並べる (段 0 = 画面の半分の大きさ、最後の段 = 1x1)。
//   Hi-Z をテクスチャではなくバッファにしたのは、ルート UAV だけで扱えてディスクリプタの管理が要らないため。
//   深度バッファはテクスチャのまま SRV で直接読む (以前は CopyTextureRegion でバッファに写してから読んでいた)。
//
//   1 回の Dispatch で最大 5 段を作る (16x16 スレッドのグループが、自分の範囲を groupshared で 16→8→4→2→1 と縮める)。
//   以前は 1 段ごとに Dispatch + UAV バリアで、小さい段ほど GPU の待ちばかりになっていた
//   (1280x720 で 11 回 → 3 回)
//
// ※この文字コードは UTF-8 (BOMなし)。FXC は BOM を読めないため BOM を付けないこと
// ※バインディングは Pipeline.cpp の makeHiZPipeline と一致させること
// ==============================================================

static const uint MAX_LEVELS_PER_PASS = 5;   // 16 → 8 → 4 → 2 → 1

cbuffer HiZConstants : register(b0)
{
    uint  g_SrcWidth;     // 読む段の大きさ
    uint  g_SrcHeight;
    uint  g_SrcOffset;    // 読む段の先頭 (要素数)
    uint  g_SrcPitch;     // 読む段の 1 行の要素数 (Hi-Z の段は幅と同じ。深度バッファから読む時は使わない)
    uint  g_FromDepth;    // 1 : 深度バッファから読む (段 0 を作る) / 0 : Hi-Z の前の段から読む
    uint  g_LevelCount;   // この Dispatch で作る段の数 (1〜5)
    uint2 g_Padding;
    uint4 g_Dst[MAX_LEVELS_PER_PASS];   // 作る段 : x = 幅, y = 高さ, z = 先頭 (要素数)
};

Texture2D<float>          g_Depth : register(t0);   // 深度バッファ (R32_FLOAT の SRV)
RWStructuredBuffer<float> g_HiZ   : register(u0);   // Hi-Z (全段)

groupshared float gs_Depth[16][16];

// 範囲外は 0 (一番手前) として読む。「一番奥」を取るので、0 は結果に影響しない
// (段の大きさは前の段の半分の切り上げなので、範囲内の texel が見落とされることは無い)
float loadSource(uint x, uint y)
{
    if (x >= g_SrcWidth || y >= g_SrcHeight) return 0.0f;
    if (g_FromDepth != 0) return g_Depth.Load(int3(x, y, 0));
    return g_HiZ[g_SrcOffset + y * g_SrcPitch + x];
}

[numthreads(16, 16, 1)]
void CSDownsample(uint3 groupId : SV_GroupID, uint3 threadId : SV_GroupThreadID)
{
    const uint2 t = threadId.xy;

    // 1 段目 : 読む段の 2x2 の一番奥
    const uint2 p  = groupId.xy * 16 + t;
    const uint2 sp = p * 2;
    float depth = 0.0f;
    if (p.x < g_Dst[0].x && p.y < g_Dst[0].y)
    {
        depth = max(max(loadSource(sp.x, sp.y),     loadSource(sp.x + 1, sp.y)),
                    max(loadSource(sp.x, sp.y + 1), loadSource(sp.x + 1, sp.y + 1)));
        g_HiZ[g_Dst[0].z + p.y * g_Dst[0].x + p.x] = depth;
    }
    gs_Depth[t.y][t.x] = depth;

    // 2 段目以降 : グループの中で半分ずつにしていく (g_LevelCount は全スレッドで同じなので、バリアは全員が通る)
    uint size = 16;
    for (uint level = 1; level < g_LevelCount; ++level)
    {
        GroupMemoryBarrierWithGroupSync();
        size /= 2;
        const bool active = t.x < size && t.y < size;
        float value = 0.0f;
        if (active)
        {
            const uint2 c = t * 2;
            value = max(max(gs_Depth[c.y][c.x],     gs_Depth[c.y][c.x + 1]),
                        max(gs_Depth[c.y + 1][c.x], gs_Depth[c.y + 1][c.x + 1]));
        }
        GroupMemoryBarrierWithGroupSync();   // 全員が読み終わってから上書きする
        if (active)
        {
            gs_Depth[t.y][t.x] = value;
            const uint2 q = groupId.xy * size + t;
            if (q.x < g_Dst[level].x && q.y < g_Dst[level].y)
            {
                g_HiZ[g_Dst[level].z + q.y * g_Dst[level].x + q.x] = value;
            }
        }
    }
}
