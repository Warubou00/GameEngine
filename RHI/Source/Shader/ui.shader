// ==============================================================
// ui.shader
//
// UI (エディターの ImGui。ED3)。全部描いた後に、画面 (スワップチェーン) へ一番上に描く
//   VSUI : ピクセルの座標 → -1〜1 (g_Params[0] = 倍率 xy / 足す値 zw)
//   PSUI : 頂点の色 × テクスチャ (g_Inputs[0].x = テクスチャの番号。フォント / シーンを描いたテクスチャ)
//
// ルートシグネチャはシーンと同じ (Pipeline.cpp)。頂点は入力アセンブラで読む (POSITION / TEXCOORD / COLOR)
// ※この文字コードは UTF-8 (BOMなし)。FXC は BOM を読めないため BOM を付けないこと
// ==============================================================

Texture2D    g_Textures[4096] : register(t0, space1);
SamplerState g_LinearClamp    : register(s2);

cbuffer PassConstants : register(b1)
{
    uint4  g_Inputs[2];
    float4 g_Params[2];
};

struct UIInput
{
    float2 Position : POSITION;
    float2 UV       : TEXCOORD;
    float4 Color    : COLOR;      // R8G8B8A8_UNORM (0〜1)
};

struct UIOutput
{
    float4 Position : SV_POSITION;
    float2 UV       : TEXCOORD;
    float4 Color    : COLOR;
};

UIOutput VSUI(UIInput input)
{
    UIOutput output;
    output.Position = float4(input.Position * g_Params[0].xy + g_Params[0].zw, 0.0f, 1.0f);
    output.UV       = input.UV;
    output.Color    = input.Color;
    return output;
}

float4 PSUI(UIOutput input) : SV_TARGET
{
    return input.Color * g_Textures[g_Inputs[0].x].SampleLevel(g_LinearClamp, input.UV, 0);
}
