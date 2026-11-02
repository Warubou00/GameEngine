// ==============================================================
// ssao.shader
//
// SSAO (スクリーンスペース・アンビエントオクルージョン)。C# の SsaoPass が LoadFullscreenShader で読み込む
//   PSSsao         : 深度 (+ 法線) から、周りの物にどれだけ囲まれているか (遮蔽) を求める
//   PSSsaoBlur     : 遮蔽のノイズを、深度の違う所をまたがずにぼかす (横 → 縦の 2 回)
//   PSApplyAmbient : 「環境光 × 遮蔽」を HDR に足す (加算のブレンド。自作のパイプライン用。
//                    組み込みの Forward はトーンマップで足すので使わない)
//   PSShowAo       : 遮蔽を白黒で画面に出す (確認用)
//
// 方法 : 法線の向きの半球の中に点を置き、その点が深度バッファの物より奥にあれば「遮られている」と数える
//        点の並べ方は 4x4 ピクセルごとに回し (ノイズ)、ぼかしで 4x4 の模様を消す
//        法線は G-Buffer があれば使い (ディファード)、無ければ深度の隣のピクセルから作る (Forward)
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

// --------------------------------------------------------------
// 深度 → カメラからの距離 (ビュー空間の z)
//   行列は C++ で転置して入れてあるので、g_ProjectionMatrix[行][列] が DirectXMath の _行列 と同じ
//   透視投影 : depth = P33 + P43 / z → z = P43 / (depth - P33)
//   平行投影 : depth = z * P33 + P43
// --------------------------------------------------------------
float linearDepth(float depth)
{
    const float p33 = g_ProjectionMatrix[2][2];
    const float p43 = g_ProjectionMatrix[3][2];
    return g_ProjectionMatrix[2][3] != 0.0f ? p43 / (depth - p33) : (depth - p43) / p33;
}

// --------------------------------------------------------------
// ビュー空間 (カメラが原点、+Z が奥) で計算する
//   以前はワールド空間で計算し、点ごとに View と Projection の行列を掛けていた (1 点 4x4 行列 2 回)。
//   ビュー空間なら、投影は x, y に倍率を掛けて z で割るだけになる
//   (行列は C++ で転置して入れてあるので、g_ProjectionMatrix[行][列] が DirectXMath の _行列 と同じ。中心からずらした投影は扱わない)
// --------------------------------------------------------------
bool isPerspective() { return g_ProjectionMatrix[2][3] != 0.0f; }
float2 projectionScale() { return float2(g_ProjectionMatrix[0][0], g_ProjectionMatrix[1][1]); }

// 画面の位置 (UV) とカメラからの距離 → ビュー空間の位置
float3 viewFromUv(float2 uv, float z)
{
    const float2 ndc = float2(uv.x * 2.0f - 1.0f, 1.0f - uv.y * 2.0f);
    return float3(ndc / projectionScale() * (isPerspective() ? z : 1.0f), z);
}

// ビュー空間の位置 → 画面の位置 (UV)
float2 uvFromView(float3 v)
{
    const float2 ndc = v.xy * projectionScale() / (isPerspective() ? v.z : 1.0f);
    return ndc * float2(0.5f, -0.5f) + 0.5f;
}

float3 viewAt(uint depthTexture, float2 uv)
{
    return viewFromUv(uv, linearDepth(g_Textures[depthTexture].SampleLevel(g_PointClamp, uv, 0).r));
}

// 深度だけから法線を作る (Forward 用。ビュー空間)
//   左右・上下のうち、深度の差が小さい方の隣を使う (物の縁で向こう側の物と混ざらないように)
float3 normalFromDepth(uint depthTexture, float2 uv, float3 P)
{
    const float2 texel = g_InvScreenSize;
    const float3 left  = viewAt(depthTexture, uv - float2(texel.x, 0.0f));
    const float3 right = viewAt(depthTexture, uv + float2(texel.x, 0.0f));
    const float3 up    = viewAt(depthTexture, uv - float2(0.0f, texel.y));
    const float3 down  = viewAt(depthTexture, uv + float2(0.0f, texel.y));
    const float3 dx = dot(P - left, P - left) < dot(right - P, right - P) ? P - left : right - P;
    const float3 dy = dot(P - up, P - up)     < dot(down - P, down - P)   ? P - up   : down - P;
    const float3 N  = normalize(cross(dy, dx));
    return dot(N, P) > 0.0f ? -N : N;   // カメラ (原点) 側を向ける
}

// 法線から接線と従法線を作る (分岐無し。Duff et al. 2017)
void basisFromNormal(float3 N, out float3 T, out float3 B)
{
    const float sign = N.z >= 0.0f ? 1.0f : -1.0f;
    const float a = -1.0f / (sign + N.z);
    const float b = N.x * N.y * a;
    T = float3(1.0f + sign * N.x * N.x * a, sign * b, -sign * N.x);
    B = float3(b, sign + N.y * N.y * a, -N.y);
}

// 4x4 の並び (Bayer)。ピクセルごとに点の並べ方を回す量 (0〜15)
static const float BAYER4x4[16] = { 0, 8, 2, 10, 12, 4, 14, 6, 3, 11, 1, 9, 15, 7, 13, 5 };

// ==============================================================
// SSAO
//   g_Inputs[0] : x = 深度 / y = 法線 (G-Buffer。無ければ g_Params[1].x = 0)
//   g_Params[0] : x = 半径 (ワールドの長さ) / y = 強さ (指数) / z = バイアス / w = 点の数
//   g_Params[1] : x = 法線のテクスチャを使うか (1 / 0)
//   出力 : 1 = 遮られていない / 0 = 真っ暗
// ==============================================================
float4 PSSsao(FullscreenOutput input) : SV_TARGET
{
    const uint  depthTexture = g_Inputs[0].x;
    // 深度のピクセルの中心に合わせる (縮小したバッファでは、自分の中心が深度のピクセルの境目に来るため。
    // 合わせないと、読んだ深度と位置がずれて、斜めに見える地面に縞が出ていた)
    const float2 uv0  = (floor(input.UV * g_ScreenSize) + 0.5f) * g_InvScreenSize;
    const float depth = g_Textures[depthTexture].SampleLevel(g_PointClamp, uv0, 0).r;
    if (depth >= 1.0f) return 1.0f.xxxx;   // 背景

    const float  radius = g_Params[0].x;
    const float  power  = g_Params[0].y;
    const float  bias   = g_Params[0].z;
    const uint   count  = clamp((uint)g_Params[0].w, 1u, 32u);

    const float3 P = viewFromUv(uv0, linearDepth(depth));
    float3 N;
    if (g_Params[1].x > 0.5f)
    {
        const float3 worldN = g_Textures[g_Inputs[0].y].SampleLevel(g_PointClamp, uv0, 0).xyz;
        N = normalize(mul(float4(worldN, 0.0f), g_ViewMatrix).xyz);   // ワールド → ビュー (回転だけ)
    }
    else
    {
        N = normalFromDepth(depthTexture, uv0, P);
    }
    float3 T, B;
    basisFromNormal(N, T, B);

    // ピクセルごとに並べ方を回す (4x4 で一巡。ぼかしで消える)
    const uint2 cell  = uint2(input.Position.xy) & 3u;
    const float noise = (BAYER4x4[cell.y * 4 + cell.x] + 0.5f) / 16.0f;
    float2 direction;
    sincos(noise * 6.2831853f, direction.y, direction.x);
    // 黄金角 (137.5 度) ずつ回す。sin / cos を点ごとに求めず、回転を掛けていく
    const float2 goldenStep = float2(-0.7373688f, 0.6754903f);   // (cos, sin)(2.3999632)

    float occlusion = 0.0f;
    [loop]
    for (uint i = 0; i < count; ++i)
    {
        // 半球の中の点 : 黄金角のらせんで向きを散らし (cos で重み付け)、距離は中心寄りを多めに
        const float t = (i + 0.5f) / count;
        const float r = sqrt(t);
        const float3 h = float3(direction * r, sqrt(1.0f - t));
        direction = float2(direction.x * goldenStep.x - direction.y * goldenStep.y,
                           direction.x * goldenStep.y + direction.y * goldenStep.x);
        const float s     = frac(i * 0.618034f + noise);
        const float scale = lerp(0.1f, 1.0f, s * s);
        const float3 S    = P + (T * h.x + B * h.y + N * h.z) * (radius * scale);

        // 点を画面に投影して、そこの深度 (物の表面) と比べる
        const float2 uv = uvFromView(S);
        if (any(uv < 0.0f) || any(uv > 1.0f)) continue;

        const float sceneZ = linearDepth(g_Textures[depthTexture].SampleLevel(g_PointClamp, uv, 0).r);
        // 半径より遠く離れた物 (手前を横切る物) は数えない
        const float range = saturate(radius / max(abs(P.z - sceneZ), 1e-4f));
        occlusion += (sceneZ <= S.z - bias ? 1.0f : 0.0f) * range * range;
    }
    const float ao = pow(saturate(1.0f - occlusion / count), power);
    return float4(ao, ao, ao, 1.0f);
}

// ==============================================================
// ぼかし (深度を見て、物の縁をまたがない)
//   g_Inputs[0] : x = 遮蔽 / y = 深度
//   g_Params[0] : xy = 向き (横 (1, 0) / 縦 (0, 1)) / z = 深度の違いにどれだけ敏感か
// ==============================================================
float4 PSSsaoBlur(FullscreenOutput input) : SV_TARGET
{
    const uint   aoTexture    = g_Inputs[0].x;
    const uint   depthTexture = g_Inputs[0].y;
    float width, height;
    g_Textures[aoTexture].GetDimensions(width, height);
    const float2 step         = g_Params[0].xy / float2(width, height);   // 遮蔽のバッファの 1 ピクセル
    const float  sharpness    = g_Params[0].z;

    const float centerDepth = g_Textures[depthTexture].SampleLevel(g_PointClamp, input.UV, 0).r;
    if (centerDepth >= 1.0f) return 1.0f.xxxx;
    const float centerZ = linearDepth(centerDepth);

    // 9 タップ (±4)。重みはガウス × 深度の近さ
    static const float WEIGHTS[5] = { 0.2270270f, 0.1945946f, 0.1216216f, 0.0540541f, 0.0162162f };
    float sum = 0.0f, total = 0.0f;
    [unroll]
    for (int i = -4; i <= 4; ++i)
    {
        const float2 uv = input.UV + step * i;
        const float  ao = g_Textures[aoTexture].SampleLevel(g_PointClamp, uv, 0).r;
        const float  z  = linearDepth(g_Textures[depthTexture].SampleLevel(g_PointClamp, uv, 0).r);
        const float  w  = WEIGHTS[abs(i)] * exp2(-abs(z - centerZ) / centerZ * sharpness);
        sum   += ao * w;
        total += w;
    }
    const float result = sum / max(total, 1e-5f);
    return float4(result, result, result, 1.0f);
}

// ==============================================================
// Forward 用 : 環境光 × 遮蔽 (加算のブレンドで HDR に足す)
//   g_Inputs[0] : x = 環境光 (PSForwardSplit の RT1) / y = 遮蔽
// ==============================================================
float4 PSApplyAmbient(FullscreenOutput input) : SV_TARGET
{
    const float3 ambient = g_Textures[g_Inputs[0].x].Load(int3(input.Position.xy, 0)).rgb;
    const float  ao      = g_Textures[g_Inputs[0].y].SampleLevel(g_LinearClamp, input.UV, 0).r;
    return float4(ambient * ao, 0.0f);
}

// ==============================================================
// 確認用 : 遮蔽を白黒で出す (バックバッファへ)
//   g_Inputs[0] : x = 遮蔽
// ==============================================================
float4 PSShowAo(FullscreenOutput input) : SV_TARGET
{
    const float ao = g_Textures[g_Inputs[0].x].SampleLevel(g_LinearClamp, input.UV, 0).r;
    return float4(ao, ao, ao, 1.0f);
}
