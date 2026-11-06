// ==========================================================================
// 雲 (立方体の中をレイマーチする。SH3)
//   元 : GameEngine.Editor/assets/volumetricCloud2.hlsl (ユーザーのシェーダー。そのまま残してある) と
//        CEDEC 2019「ゼノブレイド2 のレイマーチングを使った雲表現」の雲海の作り方
//   使い方 : 立方体 (GameObject > 3D Object > Cube) にこのシェーダーのマテリアルを付けて、大きさを変える
//            (箱の中だけに雲ができる。箱の下 0 → 上 1 の高さで「雲海の最高点」と「深さ」を決める)
//
//   元のシェーダーから変えた所 :
//     - 透明度 (資料の式 clamp((ノイズ × 濃さ - 補正値) × 乗算値, 0, 1)) をレイの各点で求め、前から重ねる
//       (元は最後の 1 歩の値だけが残っていた。資料では「レイの中で一番大きい値」)
//     - 箱とレイの交わる所を求め、箱の中だけを同じ数の歩で進む (元はワールドの歩幅が箱の大きさに合っていなかった)
//       カメラが箱の中でも描けるよう、裏の面 (@cull front) で描く
//     - シーンの深度を読み、物 (山など) の手前で止める (@depth read)
//     - 3D テクスチャはエンジンが作る Perlin-Worley (R = Perlin / G = Worley)。渡された 2D のノイズは
//       資料の「2D のテクスチャで作る値のノイズ」(z を少しずらした 2 回の読みを混ぜる) で追加ノイズに使う
//       (渡されたテクスチャは端が繋がらないので、鏡の繰り返しで読む)
//     - ライティング : 資料と同じく 雲の色 と 影の色 を透明度で混ぜる。加えて太陽の方へ数点読んで自分の影を付ける
//     - 時間で流れる (Wind)。ゲルストナー波の頂点シェーダー (雲海の面) は移していない (頂点シェーダーは変えられない)
// ==========================================================================
// @queue transparent
// @blend premultiplied
// @cull front
// @depth read
// @param texture3d Volume           builtin:PerlinWorley3D:128
// @param texture   DetailNoise      Assets/Textures/Noise/valueNoise_x128.png
// @param float     PerlinIntensity  0.7  range(0, 2)
// @param float     WorleyIntensity  0.9  range(0, 2)
// @param float     BlendCoefficient 0.9  range(0, 3)
// @param float     MaxCloudHeight   0.85 range(0, 1)
// @param float     CloudDepth       0.7  range(0.01, 1)
// @param float     Correction       0.42 range(0, 1)
// @param float     Multiple         3.0  range(0, 10)
// @param float     DetailFactor     0.45 range(0, 1)
// @param float     DetailScale      3.0  range(0.5, 32)
// @param float     Density          0.9  range(0, 10)
// @param float     NoiseScale       0.012 range(0.002, 0.5)
// @param vector    Wind             (2.0, 0, 0.6, 0)
// @param float     EdgeFade         0.12 range(0.001, 0.5)
// @param float     BottomFade       0.05 range(0.001, 1)
// @param color     CloudColor       (1, 1, 1, 1)
// @param color     ShadowColor      (0.42, 0.48, 0.6, 1)
// @param float     LightAbsorption  1.4  range(0, 8)
// @param float     LightStep        4.0  range(0.5, 30)
// @param float     LightSamples     2    range(0, 4)
// @param float     Steps            48   range(8, 128)

// 箱 [-0.5, 0.5] とレイの交わる所 (x = 入る / y = 出る)
float2 boxHit(float3 origin, float3 dir)
{
    const float3 inv = 1.0f / dir;
    const float3 t0 = (-0.5f - origin) * inv;
    const float3 t1 = ( 0.5f - origin) * inv;
    const float3 tmin = min(t0, t1), tmax = max(t0, t1);
    return float2(max(max(tmin.x, tmin.y), tmin.z), min(min(tmax.x, tmax.y), tmax.z));
}

// 端の繋がらないテクスチャを鏡の繰り返しで読む (継ぎ目が出ない)
float2 mirrorUV(float2 uv) { return 1.0f - abs(1.0f - frac(uv * 0.5f) * 2.0f); }

// 資料の値のノイズ : 2D のテクスチャを、z の整数ごとにずらした 2 か所で読んで z の小数で混ぜる (Hermite で滑らかに)
float detailNoise(float3 p)
{
    const float3 i = floor(p);
    float3 f = frac(p);
    f = f * f * (3.0f - 2.0f * f);
    const float2 offset = float2(37.0f, 17.0f);   // z が 1 増えた時にずらすピクセル
    const float2 uv0 = (i.xy + offset * i.z + f.xy + 0.5f) / 128.0f;
    const float2 uv1 = uv0 + offset / 128.0f;
    const float a = DetailNoise.SampleLevel(g_Sampler, mirrorUV(uv0), 0).r;
    const float b = DetailNoise.SampleLevel(g_Sampler, mirrorUV(uv1), 0).r;
    return lerp(a, b, f.z);
}

// 箱の横の面と底の近くで薄くする (箱の形が見えないように。浮かぶ雲は底を大きく薄くすると丸くなる)
float edgeFade(float3 local)
{
    const float2 d = 0.5f - abs(local.xz);
    return saturate(min(d.x, d.y) / EdgeFade) * saturate((local.y + 0.5f) / BottomFade);
}

// その点の「透明度」(資料の式。0 = 何も無い / 1 = 濃い雲)
float cloudAmount(float3 local, float3 world, float time)
{
    const float height = local.y + 0.5f;                                     // 箱の下 0 → 上 1
    const float density = saturate((MaxCloudHeight - height) / CloudDepth);  // 雲海の濃さ (最高点より下ほど濃い)
    if (density <= 0.0f) return 0.0f;
    const float3 uvw = (world + Wind.xyz * time) * NoiseScale;
    const float4 n = Volume.SampleLevel(g_Sampler, uvw, 0);
    float noise = (n.r * PerlinIntensity + n.g * WorleyIntensity) * BlendCoefficient;   // Perlin-Worley (元のシェーダーと同じ混ぜ方)
    noise *= lerp(1.0f, detailNoise(uvw * DetailScale * 16.0f) * 2.0f, DetailFactor); // 追加ノイズで煙らしく (資料)
    return saturate((noise * density - Correction) * Multiple) * edgeFade(local);
}

float4 Surface(SurfaceInput IN)
{
    // ローカル (箱) の空間でレイを作る
    const float3 cameraLocal = mul(float4(IN.CameraPosition, 1.0f), IN.WorldToLocal).xyz;
    const float3 dirLocal    = normalize(IN.LocalPosition - cameraLocal);
    const float2 hit = boxHit(cameraLocal, dirLocal);
    const float tNear = max(hit.x, 0.0f);
    float tFar = hit.y;

    // シーンの物の手前で止める
    if (SceneDepth(IN.ScreenUV) < 1.0f)
    {
        const float3 sceneLocal = mul(float4(SceneWorldPosition(IN.ScreenUV), 1.0f), IN.WorldToLocal).xyz;
        tFar = min(tFar, dot(sceneLocal - cameraLocal, dirLocal));
    }
    if (tFar <= tNear) return float4(0.0f, 0.0f, 0.0f, 0.0f);

    const int steps = (int)Steps;
    const float dt = (tFar - tNear) / steps;
    const float worldStep = length(mul(dirLocal * dt, (float3x3)IN.LocalToWorld));   // 1 歩のワールドの長さ (m)
    // 1 歩の中で始めの位置をずらす (歩の縞が見えないように)
    const float jitter = frac(sin(dot(IN.ScreenUV * 1000.0f, float2(12.9898f, 78.233f))) * 43758.5453f);

    float3 lightDir, lightColor;
    const bool hasLight = MainLight(lightDir, lightColor);
    const float lightStrength = hasLight ? min(dot(lightColor, float3(0.3333f, 0.3333f, 0.3333f)), 2.0f) : 1.0f;
    const int lightSamples = (int)LightSamples;

    float transmittance = 1.0f;
    float3 color = float3(0.0f, 0.0f, 0.0f);
    [loop]
    for (int i = 0; i < steps; ++i)
    {
        const float t = tNear + (i + jitter) * dt;
        const float3 local = cameraLocal + dirLocal * t;
        const float3 world = mul(float4(local, 1.0f), IN.LocalToWorld).xyz;
        const float amount = cloudAmount(local, world, IN.Time);
        if (amount <= 0.001f) continue;

        // 太陽の方の雲の量 (自分の影)。ワールドで LightStep (m) ずつ太陽の方へ進んで読む
        //   (箱の空間で進むと、平たい箱では向きによって進む長さが大きく変わる)
        float occlusion = 0.0f;
        [loop]
        for (int k = 1; k <= lightSamples; ++k)
        {
            const float3 w = world - lightDir * (LightStep * k);
            occlusion += cloudAmount(mul(float4(w, 1.0f), IN.WorldToLocal).xyz, w, IN.Time);
        }
        const float lit = exp(-occlusion * LightAbsorption);

        // 資料 / 元のシェーダーのライティング : 透明度で 雲の色 と 影の色 を混ぜる
        float3 c = ToLinear(lerp(ShadowColor.rgb, CloudColor.rgb, amount));
        c *= (0.35f + 0.65f * lit) * lightStrength;

        const float alpha = 1.0f - exp(-amount * Density * worldStep);
        color += transmittance * alpha * c;
        transmittance *= 1.0f - alpha;
        if (transmittance < 0.01f) break;
    }
    return float4(color, 1.0f - transmittance);   // 乗算済みアルファ
}
