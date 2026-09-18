// ==============================================================
// 構造体定義 (C++側のデータ構造と一致させる)
// ==============================================================
struct Vertex 
{
    float3 Position;
    float4 Color;
};

struct VSOutput 
{
    float4 Position : SV_POSITION;
    float4 Color    : COLOR;
};

// ==============================================================
// リソースバインディング (Pipeline.cpp の Root Signature と一致)
// ==============================================================

// [パラメータ1]: メガVBのSRV (Descriptor Table)
StructuredBuffer<Vertex> g_MegaVB : register(t0);

// [パラメータ0]: 頂点オフセット (32Bit Constants)
cbuffer MeshInfo : register(b0) 
{
    uint g_BaseVertexIndex;
};

// [パラメータ2]: Global定数バッファ (Root CBV) - カメラ行列等
cbuffer GlobalData : register(b1) 
{
    matrix g_ViewProj;
};

// [パラメータ3]: Scene定数バッファ (Root CBV) - オブジェクト行列等
cbuffer ObjectData : register(b2) 
{
    matrix g_World;
};

// ==============================================================
// Vertex Shader
// ==============================================================
VSOutput VSMain(uint vertexID : SV_VertexID)
{
    VSOutput output;

    // Programmable Vertex Fetch: 頂点ID + オフセットでメガバッファから自力で取得
    Vertex v = g_MegaVB[g_BaseVertexIndex + vertexID];
    
    // 行列計算 (World -> View -> Projection)
    matrix worldViewProj = mul(g_World, g_ViewProj);
    output.Position = mul(float4(v.Position, 1.0f), worldViewProj);
    
    // カラーをそのままパス
    output.Color = v.Color;
    
    return output;
}

// ==============================================================
// Pixel Shader
// ==============================================================
float4 PSMain(VSOutput input) : SV_TARGET
{
    // 受け取った頂点カラーをそのまま画面に出力
    return input.Color;
}