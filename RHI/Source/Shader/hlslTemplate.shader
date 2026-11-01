// ==============================================================
// hlslTemplate.shader
//
// バインドレス頂点プル方式の基本シェーダー
//   - 入力レイアウト(IA)は使わず、メガ頂点バッファ(StructuredBuffer)から SV_VertexID で頂点を読む
//   - テクスチャは配列(バインドレス)で受け取り、g_TextureID で選ぶ
//
// ※この文字コードは UTF-8 (BOMなし)。FXC は BOM を読めないため BOM を付けないこと
// ※実行時は C# の出力フォルダの ../Shader/ から読まれる。編集したらそちらにもコピーすること
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

struct VSOutput
{
    float4 Position : SV_POSITION;
    float3 Normal   : NORMAL;      // ワールド空間の法線 (ライティング用。今は未使用)
    float4 Color    : COLOR;
    float2 UV       : TEXCOORD;
};

// ==============================================================
// リソースバインディング (Pipeline.cpp の Root Signature と一致)
// 番号は Pipeline.h の RootParameterIndex を参照
// ==============================================================

// [ROOT_PARAM_VERTEX_BUFFER_TABLE]: メガVBのSRV (Descriptor Table)
StructuredBuffer<Vertex> g_MegaVB : register(t0);

// [ROOT_PARAM_TEXTURE_TABLE]: バインドレス・テクスチャ配列 (Descriptor Table)
// [0] は既定テクスチャ(白 1x1)。テクスチャを持たないマテリアルはこれを使う
Texture2D g_Textures[4096] : register(t1);

// (Root Signature の StaticSampler): サンプラー
SamplerState g_Sampler : register(s0);

// [ROOT_PARAM_BASE_VERTEX]: 頂点オフセット (32Bit Constants)
// SV_VertexID には DrawIndexedInstanced の BaseVertexLocation が加算されないため、ここで渡す
cbuffer MeshInfo : register(b0)
{
    uint g_BaseVertexIndex;
};

// [ROOT_PARAM_GLOBAL_CBV]: Global定数バッファ (Root CBV) - カメラ行列等
// C++ 側で XMMatrixTranspose 済みの行列が入っているので、mul(ベクトル, 行列) の順で掛ける
cbuffer GlobalData : register(b1)
{
    matrix g_ViewMatrix;
    matrix g_ProjectionMatrix;
};

// [ROOT_PARAM_OBJECT_CBV]: Scene定数バッファ (Root CBV) - オブジェクト行列等
// RHI::ObjectConstants (Public/rhiTypes.h) と同じ並び (matrix 64byte + uint)
cbuffer ObjectData : register(b2)
{
    matrix g_WorldMatrix;
    uint   g_TextureID;     // g_Textures[] の添え字 (ディスクリプタヒープの番号ではない)
};

// ==============================================================
// Vertex Shader
// ==============================================================
VSOutput VSMain(uint vertexID : SV_VertexID)
{
    VSOutput output;
    Vertex v = g_MegaVB[g_BaseVertexIndex + vertexID];

    // ローカル → ワールド → ビュー → クリップ の順に変換する
    // (以前は行列を使わず、頂点座標をそのままクリップ空間として扱っていた : 2-7)
    float4 worldPos = mul(float4(v.Position, 1.0f), g_WorldMatrix);
    float4 viewPos  = mul(worldPos, g_ViewMatrix);
    output.Position = mul(viewPos, g_ProjectionMatrix);

    // 法線は平行移動を含めないので w = 0 で変換する
    // (不均一スケールを使うようになったら逆転置行列に変えること)
    output.Normal = normalize(mul(float4(v.Normal, 0.0f), g_WorldMatrix).xyz);

    output.Color = v.Color;
    output.UV    = v.UV;

    return output;
}

// ==============================================================
// Pixel Shader
// ==============================================================
float4 PSMain(VSOutput input) : SV_TARGET
{
    // テクスチャIDを使って配列から対象画像を選び、色をサンプリングする
    float4 texColor = g_Textures[g_TextureID].Sample(g_Sampler, input.UV);

    // 頂点カラーとテクスチャカラーを掛け合わせる
    // (頂点カラーが白ならテクスチャそのままの色、既定テクスチャ(白)なら頂点カラーそのままになる)
    return input.Color * texColor;
}
