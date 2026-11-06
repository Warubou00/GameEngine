// ==========================================================================
// GPU スキニング (AN4)。CSSkin : 1 スレッド 1 頂点
//   休みの姿勢の頂点 (メガ頂点バッファの SourceBase から) を、骨の行列 (最大 4 本、重み付き) で曲げて、
//   その物専用の場所 (OutputBase から) に書く。描画 (影 / 深度 / 本描画 / 自分のシェーダー) はそこを普通の頂点として読む
//   (頂点シェーダーで曲げると、影 3 段 + 深度 + 本描画で 1 フレームに何回も曲げることになる)
//   読む所と書く所は同じバッファ (メガ頂点バッファ) なので、両方 RWStructuredBuffer で触る (SRV と UAV を同時に使えない)
//   毎フレーム書くのは位置と法線 (32 byte) だけ。色と UV は作った直後の 1 回 (Full) だけ写す
//   (以前は毎フレーム頂点を丸ごと (56 byte) 読み書きしていた。190 万頂点で GPU 1.6〜1.9 ms)
//   骨の行列 : C# の Matrix4x4 (行ベクトル v × M) の 3 列を float4 × 3 で (列 j = (M1j, M2j, M3j, M4j))。p' = dot(float4(p, 1), 列)
// ==========================================================================
struct Vertex
{
    float3 Position;
    float  Padding1;
    float3 Normal;
    float  Padding2;
    float4 Color;
    float2 UV;
};

// 頂点ごとの骨 : 骨の番号 16bit × 4 / 重み 16bit (0〜1) × 4
struct SkinVertex
{
    uint Bones01;
    uint Bones23;
    uint Weights01;
    uint Weights23;
};

cbuffer SkinConstants : register(b0)
{
    uint SourceBase;    // 休みの姿勢の頂点の先頭 (メガ頂点バッファの番号)
    uint OutputBase;    // 書く先の先頭
    uint SkinBase;      // 骨の番号と重みの先頭
    uint VertexCount;
    uint MatrixBase;    // 行列の先頭 (float4 の番号。1 本 3 個)
    uint Full;          // 1 = 色と UV も写す (作った直後)
};

RWStructuredBuffer<Vertex>   g_Vertices : register(u0);
StructuredBuffer<SkinVertex> g_Skin     : register(t0);
StructuredBuffer<float4>     g_Matrices : register(t1);

[numthreads(64, 1, 1)]
void CSSkin(uint3 id : SV_DispatchThreadID)
{
    if (id.x >= VertexCount) return;
    const uint source = SourceBase + id.x;
    const uint output = OutputBase + id.x;
    const float3 bindPosition = g_Vertices[source].Position;
    const float3 bindNormal   = g_Vertices[source].Normal;
    const SkinVertex s = g_Skin[SkinBase + id.x];
    const uint bones[4] = { s.Bones01 & 0xFFFFu, s.Bones01 >> 16, s.Bones23 & 0xFFFFu, s.Bones23 >> 16 };
    const float weights[4] = { (s.Weights01 & 0xFFFFu) / 65535.0f, (s.Weights01 >> 16) / 65535.0f,
                               (s.Weights23 & 0xFFFFu) / 65535.0f, (s.Weights23 >> 16) / 65535.0f };
    const float4 p = float4(bindPosition, 1.0f);
    float3 position = 0.0f;
    float3 normal = 0.0f;
    [unroll]
    for (uint k = 0; k < 4; ++k)
    {
        if (weights[k] <= 0.0f) continue;
        const uint m = MatrixBase + bones[k] * 3;
        const float4 c0 = g_Matrices[m];
        const float4 c1 = g_Matrices[m + 1];
        const float4 c2 = g_Matrices[m + 2];
        position += weights[k] * float3(dot(p, c0), dot(p, c1), dot(p, c2));
        normal   += weights[k] * float3(dot(bindNormal, c0.xyz), dot(bindNormal, c1.xyz), dot(bindNormal, c2.xyz));
    }
    g_Vertices[output].Position = position;
    g_Vertices[output].Normal   = dot(normal, normal) > 1e-12f ? normalize(normal) : bindNormal;
    if (Full != 0)
    {
        g_Vertices[output].Color = g_Vertices[source].Color;
        g_Vertices[output].UV    = g_Vertices[source].UV;
    }
}
