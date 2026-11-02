// ==============================================================
// debug.shader
//
// 確認用の線 (C# の DebugDraw。コライダーの形など)
//   VSDebugLine : ワールドの点 → 画面 (カメラの View / Projection)
//   PSDebugLine : 頂点の色そのまま
//   深度は使わない (物に隠れずに一番上に描く)。トーンマップの後のバックバッファへ描く
//
// ルートシグネチャはシーンと同じ (Pipeline.cpp)。頂点は入力アセンブラで読む (POSITION / COLOR)
// ※この文字コードは UTF-8 (BOMなし)。FXC は BOM を読めないため BOM を付けないこと
// ==============================================================

// フレームの定数 (hlslTemplate.shader の FrameData の先頭と同じ並び)
cbuffer FrameData : register(b0)
{
    matrix g_ViewMatrix;
    matrix g_ProjectionMatrix;
};

struct DebugLineInput
{
    float3 Position : POSITION;
    float4 Color    : COLOR;      // R8G8B8A8_UNORM (0〜1)
};

struct DebugLineOutput
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR;
};

DebugLineOutput VSDebugLine(DebugLineInput input)
{
    DebugLineOutput output;
    output.Position = mul(mul(float4(input.Position, 1.0f), g_ViewMatrix), g_ProjectionMatrix);
    output.Color    = input.Color;
    return output;
}

float4 PSDebugLine(DebugLineOutput input) : SV_TARGET
{
    return input.Color;
}
