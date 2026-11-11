// ==============================================================
// preview.shader (AN5)
//
// プレビューの窓 (エディター) : シーンとは別の小さな世界を、その窓のレンダーターゲットへ描く
//   VSPreview / PSPreview      : 物 (メガ頂点バッファから SV_VertexID で読む。曲げた物はその物専用の頂点の場所)
//   PSPreviewWire              : ワイヤーフレーム (同じ VS。決まった色)
//   VSPreviewLine / PSPreviewLine : 床の格子 / 骨 / 法線の線 (入力アセンブラ。POSITION / COLOR)
//   ライティングは簡単な物 (半球の環境光 + 平行光 1 つ)。トーンマップはせず、そのまま sRGB 風に少し持ち上げて出す
//
// ルートシグネチャはシーンと同じ (Pipeline.cpp) :
//   b0 = PreviewFrame (Root CBV。窓ごと) / b1 = 描画ごとの値 (ルート定数) / t0 = メガ頂点バッファ /
//   t2 = オブジェクト (g_Objects) / t3 = マテリアル (g_Materials) / t0,space1 = テクスチャ配列
// ※この文字コードは UTF-8 (BOMなし)。FXC は BOM を読めないため BOM を付けないこと
// ==============================================================

struct Vertex
{
    float3 Position;
    float  Padding1;
    float3 Normal;
    float  Padding2;
    float4 Color;
    float2 UV;
};

struct ObjectData
{
    matrix World;
    uint4  Info;   // v1.5 : x = 印 (1 影を落とさない / 2 影を受けない) / y = レイヤーのビット (RHI::ObjectConstants と同じ並び : 80byte)
};

struct MaterialData
{
    float4 Color;
    uint   TextureID;
    uint   Blend;
    float  Metallic;
    float  Roughness;
};

// RHI::PreviewFrame と同じ並び
cbuffer PreviewFrame : register(b0)
{
    matrix g_ViewProjection;
    float3 g_LightDirection;   // 光が進む向き (ワールド)
    uint   g_Flags;
    float3 g_CameraPosition;
    float  g_Padding0;
    float4 g_WireColor;
};

// 描画ごと (ルート定数。b1 の 16 個のうち先頭 4 個)
cbuffer PreviewDraw : register(b1)
{
    uint g_ObjectIndex;
    uint g_BaseVertex;
    uint g_MaterialIndex;
    uint g_DrawPadding;
};

StructuredBuffer<Vertex>       g_MegaVB    : register(t0);
StructuredBuffer<ObjectData>   g_Objects   : register(t2);
StructuredBuffer<MaterialData> g_Materials : register(t3);
Texture2D g_Textures[4096] : register(t0, space1);
SamplerState g_Sampler : register(s0);

struct PreviewOutput
{
    float4 Position : SV_POSITION;
    float3 Normal   : NORMAL;
    float4 Color    : COLOR;
    float2 UV       : TEXCOORD;
};

PreviewOutput VSPreview(uint vertexId : SV_VertexID)
{
    // SV_VertexID はインデックスの値 (DrawIndexedInstanced の BaseVertexLocation は 0)。頂点の場所は g_BaseVertex から
    const Vertex v = g_MegaVB[g_BaseVertex + vertexId];
    const matrix world = g_Objects[g_ObjectIndex].World;
    PreviewOutput output;
    const float4 worldPosition = mul(float4(v.Position, 1.0f), world);
    output.Position = mul(worldPosition, g_ViewProjection);
    output.Normal   = mul(v.Normal, (float3x3)world);
    output.Color    = v.Color;
    output.UV       = v.UV;
    return output;
}

float4 PSPreview(PreviewOutput input) : SV_TARGET
{
    const MaterialData material = g_Materials[g_MaterialIndex];
    float4 albedo = g_Textures[NonUniformResourceIndex(material.TextureID)].Sample(g_Sampler, input.UV) * input.Color * material.Color;
    if (albedo.a < 0.05f) discard;   // 抜きの所 (半透明は並べ替えずに不透明として描く)
    float3 n = normalize(input.Normal);
    const float3 l = -normalize(g_LightDirection);
    const float diffuse = saturate(dot(n, l));
    const float hemisphere = 0.5f + 0.5f * n.y;   // 上から明るい環境光
    const float3 color = albedo.rgb * (0.25f + 0.25f * hemisphere + 0.75f * diffuse);
    return float4(pow(saturate(color), 1.0f / 1.4f), 1.0f);
}

float4 PSPreviewWire(PreviewOutput input) : SV_TARGET
{
    return g_WireColor;
}

struct LineInput
{
    float3 Position : POSITION;
    float4 Color    : COLOR;
};

struct LineOutput
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR;
};

LineOutput VSPreviewLine(LineInput input)
{
    LineOutput output;
    output.Position = mul(float4(input.Position, 1.0f), g_ViewProjection);
    output.Color    = input.Color;
    return output;
}

float4 PSPreviewLine(LineOutput input) : SV_TARGET
{
    return input.Color;
}
