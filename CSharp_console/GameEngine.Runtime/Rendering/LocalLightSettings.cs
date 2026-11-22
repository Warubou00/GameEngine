namespace GameEngine.Rendering;

// ==========================================================================
// v2.2 : 点光源 / スポットライトの設定 (Graphics.LocalLights。Project Settings > Rendering > Local Lights)
//   Clustered : 画面を 16 x 9 の区画と奥行き 24 段に分け、そこに届くライトだけを計算する (UE と同じ考え方)。
//               切ると全部のピクセルで全部のライトを回す (v2.2 より前と同じ)
//   影        : CastShadows の付いたライトのうち、カメラに近い物から MaxShadowSlices 枚まで (スポット 1 枚 / 点光源 6 枚)
//               シャドーマップは ShadowResolution の正方形の配列 (使う枚数に合わせて増やす。減らさない)
// ==========================================================================
public sealed class LocalLightSettings
{
    public bool  Clustered        { get; set; } = true;
    public bool  Shadows          { get; set; } = true;
    public int   ShadowResolution { get; set; } = 1024;   // 1 枚の大きさ (256〜4096)
    public int   MaxShadowSlices  { get; set; } = 12;     // 影に使う枚数の上限 (0〜24。点光源 2 つ = 12 枚)
    public float ShadowSoftness   { get; set; } = 1.0f;   // 影の縁のぼかし (PCF で読む間隔。texel)
}
