// ==============================================================
// postProcess.shader
//
// ポストエフェクト (全画面のシェーダー)。C# の描画パスが LoadFullscreenShader で読み込む
//   VSFullscreen : 全画面の三角形
//   PSTonemap    : HDR (線形) → 画面の色 (露出 / トーンマップ / ガンマ)
//   PSParticleComposite : 縮小バッファに描いた GPU パーティクルを HDR へ重ねる (乗算済みアルファの合成で)
//
// ルートシグネチャはシーンと同じ (Pipeline.cpp)。読むテクスチャは g_Inputs の番号で受け取る
// ※この文字コードは UTF-8 (BOMなし)。FXC は BOM を読めないため BOM を付けないこと
// ==============================================================

Texture2D    g_Textures[4096] : register(t0, space1);
SamplerState g_PointClamp     : register(s1);
SamplerState g_LinearClamp    : register(s2);

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

// ACES (Narkowicz の近似)。明るい所を白く飛ばさず、なだらかに丸める
float3 acesFilm(float3 x)
{
    return saturate((x * (2.51f * x + 0.03f)) / (x * (2.43f * x + 0.59f) + 0.14f));
}

// ==============================================================
// トーンマップ
//   g_Inputs[0]   : x = HDR の色 / y = 環境光 / z = 遮蔽 (y, z は g_Params[1].x = 1 の時だけ)
//   g_Params[0]   : x = 露出 (倍率) / y = 方法 (0 なし / 1 Reinhard / 2 ACES) / z = ガンマ (2.2)
//                   w = 1 ならアルファに明るさ (luma) を書く (FXAA が GatherAlpha で 4 ピクセル分をまとめて読む)
//   g_Params[1]   : x = 1 なら「環境光 × 遮蔽」を足す (Forward + SSAO。環境光とライトの光を分けて描いた時)
//                   以前は全画面のパス (ssao.shader の PSApplyAmbient) で HDR に足してからトーンマップしていた。
//                   ここで足すと HDR の読み書き (加算のブレンド) が 1 回減る (MSAA では 4 サンプル分)
// ==============================================================
float4 PSTonemap(FullscreenOutput input) : SV_TARGET
{
    const int3 pixel = int3(input.Position.xy, 0);
    float3 color = g_Textures[g_Inputs[0].x].Load(pixel).rgb;
    if (g_Params[1].x > 0.5f)
    {
        const float3 ambient = g_Textures[g_Inputs[0].y].Load(pixel).rgb;
        const float  ao      = g_Textures[g_Inputs[0].z].SampleLevel(g_LinearClamp, input.UV, 0).r;   // 縮小したバッファでもよい
        color += ambient * ao;
    }
    color *= g_Params[0].x;

    const uint method = (uint)g_Params[0].y;
    if (method == 1)      color = color / (1.0f + color);
    else if (method == 2) color = acesFilm(color);

    // 線形 → 画面 (バックバッファは UNORM なので自分でガンマをかける)
    color = pow(saturate(color), 1.0f / max(g_Params[0].z, 0.01f));
    const float luma = dot(color, float3(0.299f, 0.587f, 0.114f));   // ガンマを掛けた後 (人の目の感じ方に近い)
    return float4(color, g_Params[0].w > 0.5f ? luma : 1.0f);
}

// ==============================================================
// 縮小バッファに描いた GPU パーティクルを重ねる
//   g_Inputs[0].x : パーティクルを描いた縮小バッファ (rgb = 乗算済みの色の合計 / a = 覆った割合 (1 - 透けて見える割合))
//   線形補間で拡大して出す。合成は 色 + HDR × (1 - a) (C# の FullscreenBlend.Premultiplied)
//   縮小バッファに描くと、画面を覆う大きな煙の塗りつぶしが 1/4 になる (細かい粒は少しぼやける)
// ==============================================================
float4 PSParticleComposite(FullscreenOutput input) : SV_TARGET
{
    return g_Textures[g_Inputs[0].x].SampleLevel(g_LinearClamp, input.UV, 0);
}
