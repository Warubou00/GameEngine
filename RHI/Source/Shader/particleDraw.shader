// ==============================================================
// particleDraw.shader
//
// GPU パーティクルを描く (ExecuteIndirect の DrawIndexedInstanced 1 回 : インデックス 6 個 (頂点 4 個) × 画面に映る数)
//   頂点シェーダーが SV_InstanceID 番目の列 → 描画用 32byte を読み、カメラを向いた四角を作る
//   インデックス (0,1,2, 0,2,3) で 2 頂点を使い回すので、頂点シェーダーは 1 個あたり 4 回 (以前はインデックス無しで 6 回)
//   (三角形 1 枚 (単位円に外接する正三角形。頂点 3 回) も試した : 小さい物 100 万個では少し速いが、ピクセルが約 3 割増えるので
//    画面を覆う大きな煙では遅くなる。大きな煙や炎の方がよく使うので四角にした)
//   (資料 (MH:W) の Build Primitive は頂点をバッファに作っている。ここでは頂点シェーダーがその場で作る)
//
//   ルートシグネチャはシーンと同じ物を使い、描く間だけルート SRV を差し替える (CommandContext::drawParticles)
//     t1 (シーンでは DrawData)   → 描画用 32byte (更新が書く。particleSim.shader の storeDrawData)
//                                  (以前はパーティクル本体 64byte を読んでいた)
//     t2 (シーンでは ObjectData) → 生存リスト (キー, 番号)。ソートした時は奥から順
//   g_Inputs[0].x : シーンの深度 (g_Textures の番号。無ければ 0xFFFFFFFF = 深度で比べない)
//   g_Params[0].x : ソフトパーティクルの距離 (面にこの距離まで近づくと薄くなる。0 で切る)
//   g_Params[0].y : 描画先のピクセル → シーンの深度のピクセルの倍率 (縮小バッファに描く時 2。C++ が描画先と深度の大きさから決める)
//
//   ブレンドは乗算済みアルファ (Pipeline::GetParticleDrawPso) :
//     半透明 → (色 × α, α)、加算 → (色 × α, 0)。1 つの PSO で両方を描ける
//   深度は描画先に付けず、ピクセルシェーダーがシーンの深度を読んで比べる (隠れていれば捨てる。面の近くは薄く)
//
// ※この文字コードは UTF-8 (BOMなし)。FXC は BOM を読めないため BOM を付けないこと
// ==============================================================

static const uint EMITTER_ADDITIVE = 1;
static const uint EMITTER_STRETCH  = 4;
static const uint SPRITE_SOFT  = 0;
static const uint SPRITE_SPARK = 1;
static const uint SPRITE_RING  = 2;

// hlslTemplate.shader の FrameData の先頭と同じ並び (使う所まで)
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

StructuredBuffer<uint4>    g_DrawData  : register(t1);   // 描画用 32byte = uint4 × 2 (以前は StructuredBuffer<Particle> g_Particles)
StructuredBuffer<uint2>    g_DrawList  : register(t2);
Texture2D g_Textures[4096] : register(t0, space1);

struct VSOutput
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR;
    float2 Corner   : TEXCOORD0;     // 四角の中の位置 (-1〜1。単位円の中が形)
    float  ViewZ    : VIEWZ;         // カメラからの奥行き (ソフトパーティクル)
    nointerpolation uint Flags : FLAGS;
};

float4 unpackColor(uint2 c)
{
    return float4(f16tof32(c.x), f16tof32(c.x >> 16), f16tof32(c.y), f16tof32(c.y >> 16));
}

VSOutput VSParticle(uint vertexID : SV_VertexID, uint instanceID : SV_InstanceID)
{
    // 四角の 4 隅 (インデックス 0,1,2 / 0,2,3。裏表は関係ない : カリングしない PSO)
    static const float2 CORNERS[4] = { float2(-1, -1), float2(-1, 1), float2(1, 1), float2(1, -1) };
    const float2 corner = CORNERS[vertexID];
    const uint  slot   = g_DrawList[instanceID].y;
    const uint4 data0  = g_DrawData[slot * 2];
    const uint4 data1  = g_DrawData[slot * 2 + 1];
    const float3 position = asfloat(data0.xyz);
    const float  size     = asfloat(data0.w);
    const uint   flags    = data1.w >> 16;

    // ビュー空間で四角を広げる (いつもカメラの方を向く)
    float4 viewPos = mul(float4(position, 1.0f), g_ViewMatrix);
    float2 offset;
    if ((flags & EMITTER_STRETCH) != 0)
    {
        // 速度の向きに伸ばす (火花 / 雨)。画面上の「速度 × 伸ばす秒」の向きを長い辺にする
        const float3 stretch = float3(f16tof32(data1.z), f16tof32(data1.z >> 16), f16tof32(data1.w));
        const float2 v      = mul(float4(stretch, 0.0f), g_ViewMatrix).xy;
        const float  length2 = length(v);
        const float2 axis   = length2 > 1e-6f ? v / length2 : float2(0.0f, 1.0f);
        const float2 side   = float2(-axis.y, axis.x);
        const float  along  = size + length2;
        offset = axis * (corner.y * along) + side * (corner.x * size);
    }
    else
    {
        float s, c;
        sincos(asfloat(data1.z), s, c);
        offset = float2(corner.x * c - corner.y * s, corner.x * s + corner.y * c) * size;
    }
    viewPos.xy += offset;

    VSOutput output;
    output.Position = mul(viewPos, g_ProjectionMatrix);
    output.Color    = unpackColor(data1.xy);
    output.Corner   = corner;
    output.ViewZ    = viewPos.z;
    output.Flags    = flags;
    return output;
}

float4 PSParticle(VSOutput input) : SV_TARGET
{
    // 形 : 四角の中心からの距離で濃さを決める (テクスチャは使わない)
    const float  d2     = dot(input.Corner, input.Corner);
    const uint   sprite = (input.Flags >> 8) & 0xFFu;
    float shape;
    if (sprite == SPRITE_SPARK)
    {
        shape = saturate(1.0f - sqrt(d2));
        shape = shape * shape * shape * 2.0f;          // 芯が明るい
    }
    else if (sprite == SPRITE_RING)
    {
        const float r = sqrt(d2);
        shape = saturate(1.0f - abs(r - 0.75f) * 6.0f);
    }
    else
    {
        shape = saturate(1.0f - d2);
        shape *= shape;                                // ふちがぼやけた丸
    }
    if (shape <= 0.0f) discard;

    // シーンの深度と比べる (隠れていれば捨てる。面の近くは薄くする = ソフトパーティクル)
    float fade = 1.0f;
    const uint depthTexture = g_Inputs[0].x;
    if (depthTexture != 0xFFFFFFFFu)
    {
        // 縮小バッファに描く時は、元の画面のピクセルの位置に直して読む (2x2 の中の 1 つ)
        const float2 screen = input.Position.xy * g_Params[0].y;
        const float depth = g_Textures[depthTexture].Load(int3(screen, 0)).r;
        if (depth < 1.0f)
        {
            const float2 ndc   = float2(screen.x * g_InvScreenSize.x * 2.0f - 1.0f, 1.0f - screen.y * g_InvScreenSize.y * 2.0f);
            const float4 world = mul(float4(ndc, depth, 1.0f), g_InvViewProj);
            const float  sceneZ = mul(float4(world.xyz / world.w, 1.0f), g_ViewMatrix).z;
            const float  gap    = sceneZ - input.ViewZ;
            if (gap <= 0.0f) discard;
            const float softDistance = g_Params[0].x;
            if (softDistance > 0.0f) fade = saturate(gap / softDistance);
        }
    }

    const float4 color = input.Color;
    const float  alpha = saturate(color.a * shape * fade);
    // 乗算済みアルファ : 加算は α を 0 にして後ろを消さない
    const bool additive = (input.Flags & EMITTER_ADDITIVE) != 0;
    return float4(max(color.rgb, 0.0f) * alpha, additive ? 0.0f : alpha);
}
