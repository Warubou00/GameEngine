namespace GameEngine.Rendering;

// ==========================================================================
// アンチエイリアス (Graphics.AntiAliasing)。変えると次のフレームから反映される
//   Fxaa    : ポストエフェクト。トーンマップの後の画面の色から縁を見つけてぼかす
//             軽い (画面の大きさだけで決まる)。Forward / Deferred のどちらでも使える。
//             細かい模様や文字も少しぼける。動くと縁がちらつくことがある (1 フレームの絵しか見ないため)
//   MsaaNx  : 固定機能 (ハードウェア)。描画先を 1 ピクセル N サンプルにして、物の縁を正しく塗り分ける
//             ぼけない。重さは縁の多さと N で決まる (ピクセルシェーダーは 1 ピクセル 1 回のまま)。
//             物の内側 (テクスチャ / ハイライト) のギザギザには効かない
//             Forward だけ。Deferred は G-Buffer を MSAA にすると重すぎる (メモリ・帯域 N 倍、
//             ライティングをサンプルごとに) ので、代わりに FXAA を使う
//   GPU が対応していないサンプル数は、対応している一番大きい数に下げる (Graphics.MaxMsaaSamples)
// ==========================================================================
public enum AntiAliasing
{
    None,
    Fxaa,
    Msaa2x,
    Msaa4x,
    Msaa8x,
}

// FXAA の設定 (Graphics.Fxaa)
public sealed class FxaaSettings
{
    public float Subpixel          { get; set; } = 0.75f;    // 細い線 / 点をぼかす強さ (0〜1。大きいほどぼける)
    public float EdgeThreshold     { get; set; } = 0.166f;   // 縁と見なす明るさの差 (周りの一番明るい所に対する割合)
    public float EdgeThresholdMin  { get; set; } = 0.0833f;  // 縁と見なす差の下限 (暗い所のノイズを拾わない)
}

internal static class AntiAliasingExtensions
{
    // MSAA のサンプル数 (MSAA でなければ 1)
    public static int SampleCount(this AntiAliasing mode) => mode switch
    {
        AntiAliasing.Msaa2x => 2,
        AntiAliasing.Msaa4x => 4,
        AntiAliasing.Msaa8x => 8,
        _                   => 1,
    };
}
