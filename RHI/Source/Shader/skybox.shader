// ==============================================================
// skybox.shader (v1.4)
//
// 空 (Skybox) : 正距円筒の画像 (横 = 方位 360 度 / 縦 = 上から下へ 180 度) を、カメラの向きで全画面に描く
//   C# の SkyboxPass が、不透明の物を描く前に HDR へ描く (深度は使わない。物はこの上に描かれる)
//   g_Inputs[0].x : 空の画像 (sRGB の 8 bit。ここで線形に戻す)
//   g_Params[0]   : x = 明るさ (倍率) / y = 回転 (ラジアン。方位を回す)
// ルートシグネチャはシーンと同じ (Pipeline.cpp)
// ※この文字コードは UTF-8 (BOMなし)。FXC は BOM を読めないため BOM を付けないこと
// ==============================================================

Texture2D    g_Textures[4096] : register(t0, space1);
SamplerState g_Sampler        : register(s0);   // 線形・繰り返し (方位の継ぎ目で途切れない)

// フレームの定数 (hlslTemplate.shader の FrameData と同じ並び)
cbuffer FrameData : register(b0)
{
    matrix g_ViewMatrix;
    matrix g_ProjectionMatrix;
    matrix g_InvViewProj;
    float3 g_CameraPosition;
    uint   g_LightCount;
    float3 g_Ambient;
    uint   g_LightingModel;
    float2 g_ScreenSize;
    float2 g_InvScreenSize;
};

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

static const float PI = 3.14159265f;

float4 PSSkybox(FullscreenOutput input) : SV_TARGET
{
    // 画面の点 → 遠い面のワールドの位置 → カメラからの向き
    const float2 ndc   = float2(input.UV.x * 2.0f - 1.0f, 1.0f - input.UV.y * 2.0f);
    const float4 world = mul(float4(ndc, 1.0f, 1.0f), g_InvViewProj);
    const float3 dir   = normalize(world.xyz / world.w - g_CameraPosition);

    // 向き → 正距円筒の UV (方位は +Z が真ん中。回転を足す)
    const float azimuth = atan2(dir.x, dir.z) + g_Params[0].y;
    const float2 uv = float2(azimuth / (2.0f * PI) + 0.5f, acos(clamp(dir.y, -1.0f, 1.0f)) / PI);
    // 方位の継ぎ目 (atan2 が飛ぶ所) で mip の選び方が乱れないよう、一番細かい画像を読む
    const float3 srgb = g_Textures[g_Inputs[0].x].SampleLevel(g_Sampler, uv, 0).rgb;
    const float3 color = pow(srgb, 2.2f) * g_Params[0].x;
    return float4(color, 1.0f);
}
