// ==============================================================
// antiAliasing.shader
//
// アンチエイリアス (全画面のシェーダー)
//   VSFullscreen   : 全画面の三角形
//   PSFxaa         : FXAA (ポストエフェクト)。トーンマップの後の画面の色 (LDR) から、縁を見つけて縁に沿ってぼかす
//   PSResolveDepth : MSAA の深度 → 1 サンプルの深度 (RHI が使う。C# からは呼ばない)
//
// ルートシグネチャはシーンと同じ (Pipeline.cpp)。読むテクスチャは g_Inputs の番号で受け取る
// ※この文字コードは UTF-8 (BOMなし)。FXC は BOM を読めないため BOM を付けないこと
// ==============================================================

Texture2D          g_Textures[4096]   : register(t0, space1);
Texture2DMS<float> g_TexturesMS[4096] : register(t0, space2);   // 同じテクスチャ配列を MSAA のテクスチャとして読む
SamplerState       g_LinearClamp      : register(s2);

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

// ==============================================================
// MSAA の深度の解決
//   g_Inputs[0] : x = MSAA の深度 (Texture2DMS) / y = サンプル数
//   サンプルの一番奥 (値が大きい) を選ぶ。平均すると物の縁でどちらの物でもない深度になり、
//   一番手前だと Hi-Z (オクルージョン) が縁で見えている物を隠れたと判定することがあるため
// ==============================================================
float PSResolveDepth(FullscreenOutput input) : SV_Depth
{
    const int2 pixel   = int2(input.Position.xy);
    const uint samples = g_Inputs[0].y;
    float depth = 0.0f;
    for (uint i = 0; i < samples; ++i)
    {
        depth = max(depth, g_TexturesMS[g_Inputs[0].x].Load(pixel, i));
    }
    return depth;
}

// ==============================================================
// FXAA (Fast Approximate Anti-Aliasing。Timothy Lottes の FXAA 3.11 Quality の考え方)
//   1. 自分と上下左右の明るさの差 (コントラスト) が小さい所は何もしない (ほとんどのピクセルはここで終わる)
//   2. 縁の向き (横 / 縦) と、縁の向こう側 (上下 / 左右のどちらか) を決める
//   3. 縁に沿って両側へ、明るさが変わる所 (縁の端) まで探す
//   4. 縁の端までの距離から「縁を横切る方向にどれだけずらして読むか」を決め、線形補間で読む
//      (階段の段の真ん中ほどずらさず、端ほど大きくずらす → 階段がなだらかな坂になる)
//   5. 1 ピクセルだけの細い物 (縁の探索では見つからない) は、周りとの差でぼかす (サブピクセル)
//
//   g_Inputs[0].x : 画面の色 (トーンマップの後。ガンマを掛けた LDR。アルファ = 明るさ (luma))
//   g_Params[0]   : x = サブピクセルの強さ (0〜1。0.75) / y = 縁と見なすコントラスト (割合。0.166)
//                   z = 縁と見なすコントラストの下限 (暗い所。0.0833)
//   明るさはトーンマップがアルファに書いておく (ガンマを掛けた色から求める。人の目の感じ方に近い)
//   → GatherAlpha 1 回で 2x2 ピクセルの明るさが読めるので、縁でない所 (ほとんどのピクセル) は 2 回の読み込みで終わる
//   以前 (最初の実装) は色を 5 回読んで明るさを計算していた : +0.069 ms → 下の計測を参照
// ==============================================================

// 縁の端を探す時の 1 歩の長さ (ピクセル)。近くは細かく、遠くは大きく進む (FXAA の Quality 39 相当)
static const uint  FXAA_SEARCH_STEPS = 10;
static const float FXAA_STEP[FXAA_SEARCH_STEPS] = { 1.0f, 1.0f, 1.0f, 1.0f, 1.0f, 1.5f, 2.0f, 2.0f, 4.0f, 8.0f };

float4 PSFxaa(FullscreenOutput input) : SV_TARGET
{
    Texture2D source = g_Textures[g_Inputs[0].x];
    uint width, height;
    source.GetDimensions(width, height);
    const float2 texel = 1.0f / float2(width, height);
    const float2 pixel = floor(input.Position.xy);
    const float2 uv    = (pixel + 0.5f) * texel;   // ピクセルの中心

    // ---------- 1. コントラスト (上下左右) ----------
    // ピクセルの右下の角で Gather すると、自分 (左上) / 右 / 下 / 右下 の 4 つが返る (w, z, x, y の順)
    // (中心で Gather するとどの 2x2 になるかが丸め次第になるので、角を使う)
    const float2 corner = (pixel + 1.0f) * texel;
    const float4 quadSE = source.GatherAlpha(g_LinearClamp, corner);                  // w = M  z = E  x = S  y = SE
    const float4 quadNW = source.GatherAlpha(g_LinearClamp, corner, int2(-1, -1));     // w = NW z = N  x = W  y = M
    const float lumaM = quadSE.w;
    const float lumaE = quadSE.z;
    const float lumaS = quadSE.x;
    const float lumaN = quadNW.z;
    const float lumaW = quadNW.x;

    const float lumaMax = max(lumaM, max(max(lumaN, lumaS), max(lumaW, lumaE)));
    const float lumaMin = min(lumaM, min(min(lumaN, lumaS), min(lumaW, lumaE)));
    const float range   = lumaMax - lumaMin;
    if (range < max(g_Params[0].z, lumaMax * g_Params[0].y))
    {
        return float4(source.Load(int3(pixel, 0)).rgb, 1.0f);   // 縁ではない
    }

    // ---------- 2. 縁の向き ----------
    const float lumaNW = quadNW.w;
    const float lumaSE = quadSE.y;
    const float lumaNE = source.SampleLevel(g_LinearClamp, uv, 0, int2( 1, -1)).a;
    const float lumaSW = source.SampleLevel(g_LinearClamp, uv, 0, int2(-1,  1)).a;

    // 横の縁 = 縦方向の変化が大きい (3 列の 2 階差分。真ん中の列を重く)
    const float edgeH = abs(lumaNW - 2.0f * lumaN + lumaNE) + 2.0f * abs(lumaW - 2.0f * lumaM + lumaE) + abs(lumaSW - 2.0f * lumaS + lumaSE);
    const float edgeV = abs(lumaNW - 2.0f * lumaW + lumaSW) + 2.0f * abs(lumaN - 2.0f * lumaM + lumaS) + abs(lumaNE - 2.0f * lumaE + lumaSE);
    const bool  horizontal = edgeH >= edgeV;

    // 縁の向こう側 : 差の大きい方 (横の縁なら上か下、縦の縁なら左か右)
    const float luma1 = horizontal ? lumaN : lumaW;
    const float luma2 = horizontal ? lumaS : lumaE;
    const float gradient1 = abs(luma1 - lumaM);
    const float gradient2 = abs(luma2 - lumaM);
    const bool  side1 = gradient1 >= gradient2;
    float stepLength = horizontal ? texel.y : texel.x;
    if (side1) stepLength = -stepLength;   // 上 / 左 (uv が減る方)
    const float lumaLocal      = 0.5f * ((side1 ? luma1 : luma2) + lumaM);   // 縁の上の明るさ
    const float gradientScaled = 0.25f * max(gradient1, gradient2);          // 「縁の端」と見なす変化

    // ---------- 3. 縁に沿って両側へ探す (縁の上 = ピクセルの半分だけ向こう側へずらした線) ----------
    float2 edgeUv = uv;
    if (horizontal) edgeUv.y += stepLength * 0.5f;
    else            edgeUv.x += stepLength * 0.5f;
    const float2 along = horizontal ? float2(texel.x, 0.0f) : float2(0.0f, texel.y);

    float2 uvNeg = edgeUv - along;
    float2 uvPos = edgeUv + along;
    float endNeg = source.SampleLevel(g_LinearClamp, uvNeg, 0).a - lumaLocal;
    float endPos = source.SampleLevel(g_LinearClamp, uvPos, 0).a - lumaLocal;
    bool doneNeg = abs(endNeg) >= gradientScaled;
    bool donePos = abs(endPos) >= gradientScaled;
    [loop]
    for (uint i = 1; i < FXAA_SEARCH_STEPS && !(doneNeg && donePos); ++i)
    {
        if (!doneNeg)
        {
            uvNeg -= along * FXAA_STEP[i];
            endNeg = source.SampleLevel(g_LinearClamp, uvNeg, 0).a - lumaLocal;
            doneNeg = abs(endNeg) >= gradientScaled;
        }
        if (!donePos)
        {
            uvPos += along * FXAA_STEP[i];
            endPos = source.SampleLevel(g_LinearClamp, uvPos, 0).a - lumaLocal;
            donePos = abs(endPos) >= gradientScaled;
        }
    }

    // ---------- 4. ずらす量 ----------
    const float distNeg = horizontal ? (uv.x - uvNeg.x) : (uv.y - uvNeg.y);
    const float distPos = horizontal ? (uvPos.x - uv.x) : (uvPos.y - uv.y);
    const bool  nearNeg = distNeg < distPos;
    const float dist    = min(distNeg, distPos);
    const float edgeLength = distNeg + distPos;
    float offset = 0.5f - dist / edgeLength;   // 縁の端に近いほど大きくずらす (真ん中で 0)
    // 近い方の端で、明るさの変わり方が自分と同じ向きならずらさない (縁の外側の階段)
    const bool mSmaller = lumaM < lumaLocal;
    if (((nearNeg ? endNeg : endPos) < 0.0f) == mSmaller) offset = 0.0f;

    // ---------- 5. サブピクセル (周り 3x3 の平均との差。細い線 / 点のちらつきを抑える) ----------
    const float lumaAverage = (2.0f * (lumaN + lumaS + lumaW + lumaE) + (lumaNW + lumaNE + lumaSW + lumaSE)) / 12.0f;
    const float subpixel  = saturate(abs(lumaAverage - lumaM) / range);
    const float subpixelS = (-2.0f * subpixel + 3.0f) * subpixel * subpixel;   // smoothstep
    offset = max(offset, subpixelS * subpixelS * g_Params[0].x);

    float2 finalUv = uv;
    if (horizontal) finalUv.y += offset * stepLength;
    else            finalUv.x += offset * stepLength;
    return float4(source.SampleLevel(g_LinearClamp, finalUv, 0).rgb, 1.0f);
}
