// ==============================================================
// Frame Debugger (v1.13 P4) : 止めたイベントの描画先を、確認用の絵 (RGBA8) に写す
//   PSFrameDebugView : g_Inputs[0].x = 描画先のテクスチャ (色 / シーンの深度)
//                      g_Params[0]   : x = Levels の下 / y = Levels の上 / z = 出すチャンネル (bit0 R, bit1 G, bit2 B, bit3 A) / w = 深度なら 1
//   - 下〜上を 0〜1 に広げる (HDR の明るい所 / 深度の近い所を見やすくする)
//   - チャンネルが 1 つだけなら白黒で出す。A を出さない時は不透明
//   - 深度は 1 に近いほど遠い (逆の Z ではない) ので、そのまま白黒で出す
//
// ルートシグネチャはシーンと同じ (Pipeline.cpp)。読むテクスチャは g_Inputs の番号で受け取る
// ※この文字コードは UTF-8 (BOMなし)。FXC は BOM を読めないため BOM を付けないこと
// ==============================================================

Texture2D    g_Textures[4096] : register(t0, space1);
SamplerState g_PointClamp     : register(s1);
SamplerState g_LinearClamp    : register(s2);

// パスの値 (RenderCommand.Inputs / Params)
cbuffer PassConstants : register(b1)
{
    uint4  g_Inputs[2];
    float4 g_Params[2];
};

struct FullscreenOutput
{
    float4 Position : SV_POSITION;
    float2 UV       : TEXCOORD;
};

FullscreenOutput VSFullscreen(uint vertexID : SV_VertexID)
{
    FullscreenOutput output;
    output.UV       = float2((vertexID << 1) & 2, vertexID & 2);
    output.Position = float4(output.UV * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
    return output;
}

float4 PSFrameDebugView(FullscreenOutput input) : SV_TARGET
{
    float4 color = g_Textures[g_Inputs[0].x].SampleLevel(g_PointClamp, input.UV, 0);
    const float low   = g_Params[0].x;
    const float high  = max(g_Params[0].y, low + 1e-6f);
    const uint  mask  = (uint)g_Params[0].z;
    const bool  depth = g_Params[0].w > 0.5f;
    if (depth) color = float4(color.rrr, 1.0f);

    color = saturate((color - low) / (high - low));
    const bool r = (mask & 1u) != 0, g = (mask & 2u) != 0, b = (mask & 4u) != 0, a = (mask & 8u) != 0;
    const uint count = (uint)r + (uint)g + (uint)b + (uint)a;
    if (count == 1)
    {
        const float v = r ? color.r : g ? color.g : b ? color.b : color.a;
        return float4(v, v, v, 1.0f);
    }
    return float4(r ? color.r : 0.0f, g ? color.g : 0.0f, b ? color.b : 0.0f, 1.0f);
}
