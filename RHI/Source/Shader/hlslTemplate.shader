// ==============================================================
// hlslTemplate.shader
//
// シーンを描くシェーダー (バインドレス頂点プル方式) とディファードのライティング
//   - 入力レイアウト(IA)は使わず、メガ頂点バッファ(StructuredBuffer)から SV_VertexID で頂点を読む
//   - テクスチャは配列(バインドレス)で受け取り、マテリアルにある TextureID で選ぶ
//   - 描画ごとのルート引数は使わない。入力 DRAWID (何番目の描画か) で描画ごとのデータを引く
//     (以前は描画ごとにルート定数 b0 とルート CBV b2 を切り替えていたが、
//      ExecuteIndirect で GPU がその切り替えをするのが遅かった。RTX 2070 / 20002 描画で 0.85 ms → 0.43 ms 前後)
//   - ライティング : Unlit / Lambert / PBR (GGX) を g_LightingModel で切り替える (C# の Graphics.LightingModel)
//     色は線形 (HDR) で計算し、トーンマップ (postProcess.shader) で画面の色にする
//
// エントリー
//   VSMain             : シーンの頂点
//   PSForward          : ライティングまで 1 回で描く (不透明)
//   PSForwardAlpha     : 同じ (半透明。完全に透明な所は捨てる)
//   PSForwardSplit     : ライトの光と環境光を分けて書く (Forward + SSAO)
//   PSForwardAlphaSplit: 半透明 (Forward + SSAO)。環境光の RT を、自分が覆った分だけ減らす
//   PSGBuffer          : ディファード用の G-Buffer (アルベド / 法線 / 金属・粗さ)
//   VSFullscreen       : 全画面の三角形
//   PSDeferredLighting : G-Buffer と深度からライティングする
//
// ※この文字コードは UTF-8 (BOMなし)。FXC は BOM を読めないため BOM を付けないこと
// ※実行時は C# の出力フォルダの ../Shader/ から読まれる (ビルドでコピーされる)
// ==============================================================


// ==============================================================
// 構造体定義 (C++側のデータ構造と一致させる)
// ==============================================================

// RHI/Source/Public/rhiTypes.h の RHI::Vertex と同じ並び・同じサイズ(56byte)にすること
// StructuredBuffer は 16byte 境界に詰め直さないので、C++ 側と同じパディングを明示的に入れる
// (以前は Position / Color / UV の 36byte しか無く、2頂点目以降の読み取り位置がずれていた : 1-7)
struct Vertex
{
    float3 Position;    // offset  0
    float  Padding1;    // offset 12
    float3 Normal;      // offset 16
    float  Padding2;    // offset 28
    float4 Color;       // offset 32
    float2 UV;          // offset 48
};                      // 合計 56byte

// 描画 1 回分のデータ (CommandContext::DrawData と同じ並び : 16byte)
struct DrawData
{
    uint ObjectIndex;     // g_Objects[] の添え字
    uint BaseVertex;      // メガ頂点バッファ内の開始位置
    uint MaterialIndex;   // g_Materials[] の添え字 (以前はオブジェクトがテクスチャを持っていた)
    uint Padding;
};

// オブジェクトごとのデータ (RHI::ObjectConstants : Buffer::OBJECT_DATA_STRIDE = 64byte)
// 以前は cbuffer ObjectData (b2) だった。StructuredBuffer の行列も cbuffer と同じ列優先で読まれる
struct ObjectData
{
    matrix World;
};

// マテリアル (Buffer::MaterialData と同じ並び : 32byte)
struct MaterialData
{
    float4 Color;       // テクスチャ × 頂点の色 × この色
    uint   TextureID;   // g_Textures[] の添え字 (ディスクリプタヒープの番号ではない)
    uint   Blend;       // RHI::BlendMode (PSO で分けて描くので、シェーダーでは使わない)
    float  Metallic;    // PBR : 金属らしさ (以前は詰め物だった)
    float  Roughness;   // PBR : 粗さ
};

// ライト (RHI::LightData と同じ並び : 48byte)
struct LightData
{
    float3 Position;    // 点光源の位置
    float  Range;       // 点光源の届く距離
    float3 Direction;   // 平行光源の光が進む向き
    uint   Type;        // 0 : 平行光源 (太陽) / 1 : 点光源
    float3 Color;
    float  Intensity;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float3 Normal   : NORMAL;      // ワールド空間の法線
    float4 Color    : COLOR;
    float2 UV       : TEXCOORD;
    // マテリアルは番号だけ渡し、ピクセルシェーダーが g_Materials を引く (3 頂点とも同じ値なので補間しない)
    // 以前はテクスチャの番号・色を頂点シェーダーから渡していた。
    // ワールド座標も渡さず、ピクセルシェーダーが SV_Position から戻す (worldPosition)。
    //   小さい三角形が多い場面では、補間する値の準備 (三角形ごと) が重い。
    //   RTX 2070 / 負荷テスト 20002 個 / Forward : ワールド座標を渡すと 0.336 ms → 戻すと 0.265 ms
    //   (ライティング前は 0.245 ms。法線は戻せないので、その分は増える)
    nointerpolation uint MaterialIndex : MATERIAL;
};

// ==============================================================
// リソースバインディング (Pipeline.cpp の Root Signature と一致)
// 番号は Pipeline.h の RootParameterIndex を参照
// ==============================================================

// [ROOT_PARAM_VERTEX_BUFFER_TABLE]: メガVBのSRV (Descriptor Table)
StructuredBuffer<Vertex> g_MegaVB : register(t0);

// [ROOT_PARAM_DRAW_DATA_SRV]: 描画ごとのデータ (Root SRV)。DRAWID 番目を読む
StructuredBuffer<DrawData>   g_Draws   : register(t1);

// [ROOT_PARAM_OBJECT_DATA_SRV]: オブジェクトごとのデータ (Root SRV)。このフレームの領域の先頭を指す
StructuredBuffer<ObjectData> g_Objects : register(t2);

// [ROOT_PARAM_MATERIAL_SRV]: マテリアル (Root SRV)。DrawData.MaterialIndex 番目を読む
StructuredBuffer<MaterialData> g_Materials : register(t3);

// [ROOT_PARAM_LIGHT_SRV]: ライト (Root SRV)。g_LightCount 個
StructuredBuffer<LightData> g_Lights : register(t4);

// [ROOT_PARAM_TEXTURE_TABLE]: バインドレス・テクスチャ配列 (Descriptor Table)
// [0] は既定テクスチャ(白 1x1)。テクスチャを持たないマテリアルはこれを使う
// レンダーターゲット (G-Buffer など) と深度バッファもここに入っている (番号は g_Inputs で受け取る)
// 上限の無い範囲なので、他の SRV と番号が重ならないよう space1 に置く (以前は t1)
Texture2D g_Textures[4096] : register(t0, space1);

// (Root Signature の StaticSampler)
SamplerState g_Sampler      : register(s0);   // 線形・繰り返し (モデルのテクスチャ)
SamplerState g_PointClamp   : register(s1);   // 点・端で止める
SamplerState g_LinearClamp  : register(s2);   // 線形・端で止める

// [ROOT_PARAM_GLOBAL_CBV]: フレームの定数 (Root CBV) - Buffer::FrameConstants と同じ並び
// C++ 側で XMMatrixTranspose 済みの行列が入っているので、mul(ベクトル, 行列) の順で掛ける
cbuffer FrameData : register(b0)
{
    matrix g_ViewMatrix;
    matrix g_ProjectionMatrix;
    matrix g_InvViewProj;          // クリップ → ワールド (深度から位置を戻す)
    float3 g_CameraPosition;
    uint   g_LightCount;
    float3 g_Ambient;              // 環境光 (線形)
    uint   g_LightingModel;        // 0 : Unlit / 1 : Lambert / 2 : PBR
    float2 g_ScreenSize;
    float2 g_InvScreenSize;
};

// [ROOT_PARAM_PASS_CONSTANTS]: 全画面のパスの値 (ルート定数 16 個)
//   g_Inputs : 読むテクスチャの番号 (RenderCommand.Inputs) / g_Params : 値 (RenderCommand.Params)
cbuffer PassConstants : register(b1)
{
    uint4  g_Inputs[2];
    float4 g_Params[2];
};

static const float PI = 3.14159265f;
static const uint LIGHTING_UNLIT   = 0;
static const uint LIGHTING_LAMBERT = 1;
static const uint LIGHTING_PBR     = 2;

// ==============================================================
// ライティング
//   光の強さは「真上から当たった白い面がその明るさになる」単位 (Unity と同じ)。
//   そのため拡散反射は albedo × N・L (Lambert の 1/π を光の強さに含めている)、
//   PBR の鏡面反射も同じだけ π 倍してそろえる (Lambert と PBR を切り替えても明るさが大きく変わらない)
// ==============================================================

// テクスチャ / マテリアルの色 (sRGB) → 線形 (ライティングは線形で計算する)
float3 toLinear(float3 color)
{
    return pow(max(color, 0.0f), 2.2f);
}

// 点光源の減衰 : 距離の 2 乗で弱くなり、Range でちょうど 0 になる (UE4 と同じ形)
float pointAttenuation(float distance, float range)
{
    const float x = distance / max(range, 1e-4f);
    float falloff = saturate(1.0f - x * x * x * x);
    falloff *= falloff;
    return falloff / (distance * distance + 1.0f);
}

// 環境光の鏡面反射の近似 (Karis "Physically Based Shading on Mobile")。環境マップ無しで、粗さに応じた反射の量だけ求める
float3 envBrdfApprox(float3 f0, float roughness, float NoV)
{
    const float4 c0 = float4(-1.0f, -0.0275f, -0.572f, 0.022f);
    const float4 c1 = float4( 1.0f,  0.0425f,  1.04f, -0.04f);
    const float4 r = roughness * c0 + c1;
    const float a004 = min(r.x * r.x, exp2(-9.28f * NoV)) * r.x + r.y;
    const float2 ab = float2(-1.04f, 1.04f) * a004 + r.zw;
    return f0 * ab.x + ab.y;
}

// 環境光 (空の明るさ)。PBR は鏡面反射の分も入れる
//   SSAO (遮蔽) はこの分にだけ掛ける (ライトの直接の光には掛けない)。Unlit は 0
// N : 法線 (正規化済み) / V : 視線 (物 → カメラ、正規化済み)
float3 shadeAmbient(float3 albedo, float3 N, float3 V, float metallic, float roughness)
{
    if (g_LightingModel == LIGHTING_UNLIT) return 0.0f.xxx;
    if (g_LightingModel != LIGHTING_PBR)   return albedo * g_Ambient;

    const float  NoV = saturate(dot(N, V)) + 1e-4f;
    const float3 f0  = lerp(0.04f.xxx, albedo, metallic);
    return (albedo * (1.0f - metallic) + envBrdfApprox(f0, clamp(roughness, 0.045f, 1.0f), NoV)) * g_Ambient;
}

// ライトの直接の光 (Unlit はテクスチャの色そのまま)
// N : 法線 (正規化済み) / V : 視線 (物 → カメラ、正規化済み) / P : ワールド座標
float3 shadeLights(float3 albedo, float3 N, float3 V, float3 P, float metallic, float roughness)
{
    if (g_LightingModel == LIGHTING_UNLIT) return albedo;

    const bool  pbr = g_LightingModel == LIGHTING_PBR;
    const float NoV = saturate(dot(N, V)) + 1e-4f;
    roughness = clamp(roughness, 0.045f, 1.0f);   // 0 だとハイライトが 1 ピクセルに潰れる
    const float  a   = roughness * roughness;
    const float  a2  = a * a;
    const float  k   = (roughness + 1.0f) * (roughness + 1.0f) / 8.0f;   // Smith-Schlick (UE4)
    const float3 f0  = lerp(0.04f.xxx, albedo, metallic);                // 非金属は 4% 反射、金属は自分の色で反射
    const float3 diffuseColor = pbr ? albedo * (1.0f - metallic) : albedo;

    float3 color = 0.0f.xxx;
    for (uint i = 0; i < g_LightCount; ++i)
    {
        const LightData light = g_Lights[i];
        float3 L;
        float  attenuation = 1.0f;
        if (light.Type == 0)
        {
            L = -light.Direction;
        }
        else
        {
            const float3 toLight  = light.Position - P;
            const float  distance = length(toLight);
            if (distance >= light.Range) continue;
            L = toLight / max(distance, 1e-4f);
            attenuation = pointAttenuation(distance, light.Range);
        }

        const float NoL = saturate(dot(N, L));
        if (NoL <= 0.0f) continue;
        const float3 radiance = light.Color * (light.Intensity * attenuation * NoL);

        if (!pbr)
        {
            color += albedo * radiance;   // Lambert : 拡散反射だけ
            continue;
        }

        // PBR (Cook-Torrance) : GGX の分布 × Smith の遮蔽 × Schlick のフレネル
        const float3 H   = normalize(L + V);
        const float  NoH = saturate(dot(N, H));
        const float  VoH = saturate(dot(V, H));
        const float  d   = NoH * NoH * (a2 - 1.0f) + 1.0f;
        const float  D   = a2 / (PI * d * d);
        const float  vis = 1.0f / ((NoL * (1.0f - k) + k) * (NoV * (1.0f - k) + k) * 4.0f);   // G / (4 N・L N・V)
        const float3 F   = f0 + (1.0f - f0) * pow(1.0f - VoH, 5.0f);
        const float3 specular = D * vis * F * PI;     // π 倍 : 拡散反射の 1/π を光に含めた分 (上の説明)
        const float3 kd = 1.0f - F;                   // 鏡面で反射した分は拡散しない
        color += (kd * diffuseColor + specular) * radiance;
    }
    return color;
}

// 環境光 × 遮蔽 (ao : 1 = 遮られていない) + ライト
float3 shade(float3 albedo, float3 N, float3 V, float3 P, float metallic, float roughness, float ao)
{
    return shadeAmbient(albedo, N, V, metallic, roughness) * ao + shadeLights(albedo, N, V, P, metallic, roughness);
}

// ==============================================================
// Vertex Shader
// ==============================================================
// drawID : インスタンス単位の入力 (中身は 0, 1, 2, ... の番号を並べただけのバッファ)。
//          各描画の StartInstanceLocation = 描画番号 にしているので、その番号が届く
//          (SV_InstanceID には StartInstanceLocation が加算されないため、入力アセンブラに読ませる)
VSOutput VSMain(uint vertexID : SV_VertexID, uint drawID : DRAWID)
{
    VSOutput output;
    const DrawData   draw   = g_Draws[drawID];
    const ObjectData object = g_Objects[draw.ObjectIndex];
    Vertex v = g_MegaVB[draw.BaseVertex + vertexID];

    // ローカル → ワールド → ビュー → クリップ の順に変換する
    // (以前は行列を使わず、頂点座標をそのままクリップ空間として扱っていた : 2-7)
    float4 worldPos = mul(float4(v.Position, 1.0f), object.World);
    float4 viewPos  = mul(worldPos, g_ViewMatrix);
    output.Position = mul(viewPos, g_ProjectionMatrix);

    // 法線はワールド行列の余因子行列で変換する (= 逆転置行列 × 行列式。向きは同じで、逆行列を求めなくてよい)
    //   以前はワールド行列でそのまま変換していて、不均一なスケール (地面のように平たく伸ばした箱) で
    //   法線が傾き、ライティングと SSAO (ディファード) が崩れていた
    // 正規化はピクセルシェーダーで補間後に行う。鏡像 (行列式が負) で裏返っても、ピクセルシェーダーがカメラ側に向け直す
    const float3 r0 = object.World[0].xyz;
    const float3 r1 = object.World[1].xyz;
    const float3 r2 = object.World[2].xyz;
    output.Normal = v.Normal.x * cross(r1, r2) + v.Normal.y * cross(r2, r0) + v.Normal.z * cross(r0, r1);

    output.Color = v.Color;
    output.UV    = v.UV;
    output.MaterialIndex = draw.MaterialIndex;

    return output;
}

// ==============================================================
// Pixel Shader
// ==============================================================

// 表面の情報 (Forward / G-Buffer 共通)
struct Surface
{
    float3 Albedo;     // 線形
    float  Alpha;
    float  Metallic;
    float  Roughness;
    float3 Normal;     // カメラ側を向いた法線 (両面を描くので、裏から見た時は裏返す)
    float3 View;       // 物 → カメラ
    float3 Position;   // ワールド座標
};

// ピクセルのワールド座標を SV_Position (画面の位置と深度) から戻す
// 補間する値を減らすため、頂点シェーダーからは渡さない (小さい三角形が多いほど補間の準備が重い)
// ※ シーンは画面と同じ大きさに描く前提 (g_InvScreenSize を使うため。縮小したバッファに描くなら直すこと)
float3 worldPosition(float4 svPosition)
{
    const float2 ndc   = float2(svPosition.x * g_InvScreenSize.x * 2.0f - 1.0f, 1.0f - svPosition.y * g_InvScreenSize.y * 2.0f);
    const float4 world = mul(float4(ndc, svPosition.z, 1.0f), g_InvViewProj);
    return world.xyz / world.w;
}

Surface getSurface(VSOutput input)
{
    Surface surface;
    // 頂点カラー × テクスチャカラー × マテリアルの色
    // (頂点カラーとマテリアルが白ならテクスチャそのままの色、既定テクスチャ(白)なら色だけになる)
    const MaterialData material = g_Materials[input.MaterialIndex];
    const float4 texColor = g_Textures[material.TextureID].Sample(g_Sampler, input.UV);
    const float4 color    = input.Color * texColor * material.Color;
    surface.Albedo    = toLinear(color.rgb);
    surface.Alpha     = color.a;
    surface.Metallic  = material.Metallic;
    surface.Roughness = material.Roughness;

    surface.Position = worldPosition(input.Position);
    surface.View = normalize(g_CameraPosition - surface.Position);
    float3 N = input.Normal;
    const float lengthSq = dot(N, N);
    N = lengthSq > 1e-12f ? N * rsqrt(lengthSq) : surface.View;   // 法線の無いモデルはカメラ向き
    // カリングせず両面を描くので、裏面を見ている時は法線を裏返す (裏側が真っ暗にならないように)
    if (dot(N, surface.View) < 0.0f) N = -N;
    surface.Normal = N;
    return surface;
}

float4 PSForward(VSOutput input) : SV_TARGET
{
    const Surface surface = getSurface(input);
    const float3 color = shade(surface.Albedo, surface.Normal, surface.View, surface.Position, surface.Metallic, surface.Roughness, 1.0f);
    return float4(color, surface.Alpha);
}

// ==============================================================
// Pixel Shader (半透明用。アルファブレンドの PSO で使う)
//   完全に透明な所は描かない (半透明のマテリアルは深度も書くので、透明な所で後ろを隠さないように)
//   不透明用 (PSForward) に clip を入れると、GPU が描く前の深度テスト (early-Z) を使えなくなるので分けている
// ==============================================================
float4 PSForwardAlpha(VSOutput input) : SV_TARGET
{
    const Surface surface = getSurface(input);
    clip(surface.Alpha - 1.0f / 255.0f);
    const float3 color = shade(surface.Albedo, surface.Normal, surface.View, surface.Position, surface.Metallic, surface.Roughness, 1.0f);
    return float4(color, surface.Alpha);
}

// ==============================================================
// Forward (SSAO あり) : ライトの光と環境光を別々に書く
//   RT0 : ライトの光 (HDR) / RT1 : 環境光 (R11G11B10F)
//   トーンマップが「ライトの光 + 環境光 × 遮蔽」にする (postProcess.shader の PSTonemap)
//   こうすると Forward でも遮蔽が環境光にだけ掛かり、深度だけを先に描く (プリパス) も要らない
//   (以前は SSAO の後に全画面のパス (PSApplyAmbient) で RT0 に足していた)
// ==============================================================
struct ForwardSplitOutput
{
    float4 Lights  : SV_TARGET0;
    float4 Ambient : SV_TARGET1;
};

ForwardSplitOutput PSForwardSplit(VSOutput input)
{
    const Surface surface = getSurface(input);
    ForwardSplitOutput output;
    output.Lights  = float4(shadeLights(surface.Albedo, surface.Normal, surface.View, surface.Position, surface.Metallic, surface.Roughness), 1.0f);
    output.Ambient = float4(shadeAmbient(surface.Albedo, surface.Normal, surface.View, surface.Metallic, surface.Roughness), 1.0f);
    return output;
}

// ==============================================================
// 半透明 (Forward + SSAO)
//   RT0 : 色 (遮蔽なし。PSForwardAlpha と同じ) をアルファブレンド
//   RT1 : 環境光の RT を (1 - α) 倍にする (ブレンド : 0 × 自分 + 環境光 × (1 - α)。Pipeline.cpp)
//   環境光 × 遮蔽 はトーンマップで足すので、半透明の後ろの物の環境光は、半透明に覆われた分だけ減らしておく
//   (しないと、後ろの物の環境光が半透明の上に透けて足される)
// ==============================================================
ForwardSplitOutput PSForwardAlphaSplit(VSOutput input)
{
    const Surface surface = getSurface(input);
    clip(surface.Alpha - 1.0f / 255.0f);
    ForwardSplitOutput output;
    output.Lights  = float4(shade(surface.Albedo, surface.Normal, surface.View, surface.Position, surface.Metallic, surface.Roughness, 1.0f), surface.Alpha);
    output.Ambient = float4(0.0f, 0.0f, 0.0f, surface.Alpha);
    return output;
}

// ==============================================================
// G-Buffer (ディファード)
//   RT0 : アルベド (RGBA8_SRGB。線形で書くとハードウェアが sRGB に詰める)
//   RT1 : 法線 (RGBA16F。ワールド空間)
//   RT2 : x = 金属らしさ / y = 粗さ (RGBA8)
//   位置は書かない (ライティングで深度から戻す)
// ==============================================================
struct GBufferOutput
{
    float4 Albedo   : SV_TARGET0;
    float4 Normal   : SV_TARGET1;
    float4 Material : SV_TARGET2;
};

GBufferOutput PSGBuffer(VSOutput input)
{
    const Surface surface = getSurface(input);
    GBufferOutput output;
    output.Albedo   = float4(surface.Albedo, 1.0f);
    output.Normal   = float4(surface.Normal, 1.0f);
    output.Material = float4(surface.Metallic, surface.Roughness, 0.0f, 1.0f);
    return output;
}

// ==============================================================
// 全画面の三角形 (頂点 3 個で画面全体を覆う。入力レイアウト無し)
// ==============================================================
struct FullscreenOutput
{
    float4 Position : SV_POSITION;
    float2 UV       : TEXCOORD;
};

FullscreenOutput VSFullscreen(uint vertexID : SV_VertexID)
{
    FullscreenOutput output;
    output.UV       = float2((vertexID << 1) & 2, vertexID & 2);           // (0,0) (2,0) (0,2)
    output.Position = float4(output.UV * float2(2.0f, -2.0f) + float2(-1.0f, 1.0f), 0.0f, 1.0f);
    return output;
}

// ==============================================================
// ディファードのライティング
//   g_Inputs[0] : x = アルベド / y = 法線 / z = 金属・粗さ / w = 深度
//   g_Inputs[1] : x = SSAO (無ければ既定テクスチャの白 = 遮蔽なし。縮小したバッファでもよい)
//   g_Params[0] : 背景の色 (何も描かれていない所。線形)
// ==============================================================
float4 PSDeferredLighting(FullscreenOutput input) : SV_TARGET
{
    const int3  pixel = int3(input.Position.xy, 0);
    const float depth = g_Textures[g_Inputs[0].w].Load(pixel).r;
    if (depth >= 1.0f) return float4(g_Params[0].rgb, 1.0f);   // 背景

    const float3 albedo   = g_Textures[g_Inputs[0].x].Load(pixel).rgb;   // sRGB の RT から読むと線形に戻る
    const float3 normal   = normalize(g_Textures[g_Inputs[0].y].Load(pixel).xyz);
    const float2 material = g_Textures[g_Inputs[0].z].Load(pixel).xy;

    // 深度からワールド座標を戻す (UV の y は下向き、クリップ空間の y は上向き)
    const float2 ndc   = float2(input.UV.x * 2.0f - 1.0f, 1.0f - input.UV.y * 2.0f);
    const float4 world = mul(float4(ndc, depth, 1.0f), g_InvViewProj);
    const float3 P     = world.xyz / world.w;
    const float3 V     = normalize(g_CameraPosition - P);

    const float  ao    = g_Textures[g_Inputs[1].x].SampleLevel(g_LinearClamp, input.UV, 0).r;
    return float4(shade(albedo, normal, V, P, material.x, material.y, ao), 1.0f);
}
