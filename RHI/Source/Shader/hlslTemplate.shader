// ==============================================================
// hlslTemplate.shader
//
// バインドレス頂点プル方式の基本シェーダー
//   - 入力レイアウト(IA)は使わず、メガ頂点バッファ(StructuredBuffer)から SV_VertexID で頂点を読む
//   - テクスチャは配列(バインドレス)で受け取り、オブジェクトのデータにある TextureID で選ぶ
//   - 描画ごとのルート引数は使わない。入力 DRAWID (何番目の描画か) で描画ごとのデータを引く
//     (以前は描画ごとにルート定数 b0 とルート CBV b2 を切り替えていたが、
//      ExecuteIndirect で GPU がその切り替えをするのが遅かった。RTX 2070 / 20002 描画で 0.85 ms → 0.43 ms 前後)
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

// 描画 1 回分のデータ (CommandContext::DrawData と同じ並び : 8byte)
struct DrawData
{
    uint ObjectIndex;   // g_Objects[] の添え字
    uint BaseVertex;    // メガ頂点バッファ内の開始位置
};

// オブジェクトごとのデータ (RHI::ObjectConstants + 詰め物 : Buffer::OBJECT_DATA_STRIDE = 80byte)
// 以前は cbuffer ObjectData (b2) だった。StructuredBuffer の行列も cbuffer と同じ列優先で読まれる
struct ObjectData
{
    matrix World;
    uint   TextureID;   // g_Textures[] の添え字 (ディスクリプタヒープの番号ではない)
    uint3  Padding;
};

struct VSOutput
{
    float4 Position : SV_POSITION;
    float3 Normal   : NORMAL;      // ワールド空間の法線 (ライティング用。今は未使用)
    float4 Color    : COLOR;
    float2 UV       : TEXCOORD;
    nointerpolation uint TextureID : TEXTUREID;   // 3 頂点とも同じ値なので補間しない
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

// [ROOT_PARAM_TEXTURE_TABLE]: バインドレス・テクスチャ配列 (Descriptor Table)
// [0] は既定テクスチャ(白 1x1)。テクスチャを持たないマテリアルはこれを使う
// 上限の無い範囲なので、他の SRV と番号が重ならないよう space1 に置く (以前は t1)
Texture2D g_Textures[4096] : register(t0, space1);

// (Root Signature の StaticSampler): サンプラー
SamplerState g_Sampler : register(s0);

// [ROOT_PARAM_GLOBAL_CBV]: Global定数バッファ (Root CBV) - カメラ行列等
// C++ 側で XMMatrixTranspose 済みの行列が入っているので、mul(ベクトル, 行列) の順で掛ける
// (以前は b1。b0 の頂点オフセットのルート定数は DrawData.BaseVertex に移した)
cbuffer GlobalData : register(b0)
{
    matrix g_ViewMatrix;
    matrix g_ProjectionMatrix;
};

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

    // 法線は平行移動を含めないので w = 0 で変換する
    // (不均一スケールを使うようになったら逆転置行列に変えること)
    output.Normal = normalize(mul(float4(v.Normal, 0.0f), object.World).xyz);

    output.Color = v.Color;
    output.UV    = v.UV;
    output.TextureID = object.TextureID;

    return output;
}

// ==============================================================
// Pixel Shader
// ==============================================================
float4 PSMain(VSOutput input) : SV_TARGET
{
    // テクスチャIDを使って配列から対象画像を選び、色をサンプリングする
    float4 texColor = g_Textures[input.TextureID].Sample(g_Sampler, input.UV);

    // 頂点カラーとテクスチャカラーを掛け合わせる
    // (頂点カラーが白ならテクスチャそのままの色、既定テクスチャ(白)なら頂点カラーそのままになる)
    return input.Color * texColor;
}
