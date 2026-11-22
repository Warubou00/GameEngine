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

// v2.2 : パスの値 (hlslTemplate.shader の PassConstants と同じ)。g_Params[0].x = 三角形の色の明るさの倍率
cbuffer PassConstants : register(b1)
{
    uint4  g_Inputs[2];
    float4 g_Params[2];
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

// ==============================================================
// v2.2 : 確認用の塗りつぶしの三角形 (エディターの CollisionMap など。RenderCommandType::DrawDebugTriangles)
//   今の描画先 (HDR + シーンの深度) へ描く。面の向き (位置の ddx / ddy) と視線から、カメラの方から照らしたような陰を付ける
//   (法線を持たなくても形が分かる。色は線形のまま HDR へ)
// ==============================================================
struct DebugTriangleOutput
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR;
    float3 World    : WORLDPOS;
};

DebugTriangleOutput VSDebugTriangle(DebugLineInput input)
{
    DebugTriangleOutput output;
    output.Position = mul(mul(float4(input.Position, 1.0f), g_ViewMatrix), g_ProjectionMatrix);
    output.Color    = input.Color;
    output.World    = input.Position;
    return output;
}

float4 PSDebugTriangle(DebugTriangleOutput input) : SV_TARGET
{
    // 面の法線 (どちらの向きでもよいように、カメラの方へ向ける)
    float3 n = normalize(cross(ddx(input.World), ddy(input.World)));
    // カメラの前の向き (ワールド) = View の 3 列目 (行ベクトルの行列)
    const float3 forward = normalize(float3(g_ViewMatrix[0][2], g_ViewMatrix[1][2], g_ViewMatrix[2][2]));
    if (dot(n, forward) > 0.0f) n = -n;
    // カメラの少し上から照らす + 上からの空の明るさ
    const float3 light = normalize(-forward + float3(0.0f, 0.6f, 0.0f));
    const float shade = 0.35f + 0.65f * saturate(dot(n, light)) + 0.15f * saturate(n.y);
    return float4(input.Color.rgb * shade * g_Params[0].x, input.Color.a);
}

// v2.2 : ワイヤーフレーム (FillMode = WIREFRAME の PSO)。陰を付けず、色 × 明るさの倍率 (塗りつぶしに重ねる時は暗くする)
float4 PSDebugWire(DebugTriangleOutput input) : SV_TARGET
{
    return float4(input.Color.rgb * g_Params[0].x, max(input.Color.a, 0.6f));
}
