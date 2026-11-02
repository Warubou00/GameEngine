namespace GameEngine.Rendering;

// ==========================================================================
// SSAO の設定 (Graphics.Ssao)。変えると次のフレームから反映される
// ==========================================================================
public sealed class SsaoSettings
{
    public bool  Enabled         { get; set; } = true;
    public float Radius          { get; set; } = 0.6f;    // 周りを調べる半径 (ワールドの長さ)
    public float Intensity       { get; set; } = 1.5f;    // 濃さ (遮蔽の指数。大きいほど暗い)
    public float Bias            { get; set; } = 0.02f;   // 自分自身で遮らないための余裕 (ワールドの長さ)
    public int   SampleCount     { get; set; } = 12;      // 1 ピクセルで調べる点の数 (1〜32)
    // 遮蔽を求める解像度 (画面に対する倍率)。既定は縦横半分
    //   RTX 2070 / 1280x720 / 12 点 : 1.0 で +0.26 ms、0.5 で +0.10 ms。見た目の差はほとんど無い (縁が少しぼやける)
    public float ResolutionScale { get; set; } = 0.5f;
    public bool  ShowOnly        { get; set; }            // 確認用 : 遮蔽だけを白黒で出す
}

// ==========================================================================
// SSAO のパス (Forward / Deferred の両方から使う部品)
//   1. PSSsao     : 深度 (+ 法線) → 遮蔽 (R8)
//   2. PSSsaoBlur : 横 → 縦にぼかす (深度の違う所はまたがない)
//   結果のテクスチャを返すので、パイプラインが環境光に掛ける
//     Deferred : ライティングのシェーダーが読む
//     Forward  : 環境光を別に書いておき、「環境光 × 遮蔽」を足す (SsaoPass.ApplyAmbient)
//   レンダーターゲットは初めて使う時に作り、解像度を変えたら作り直す
// ==========================================================================
public sealed class SsaoPass : IDisposable
{
    private RenderTexture? _ao;
    private RenderTexture? _blur;
    private float _scale;
    private FullscreenShader? _ssao;
    private FullscreenShader? _blurShader;
    private FullscreenShader? _applyAmbient;
    private FullscreenShader? _showAo;

    public const string ShaderPath = "../Shader/ssao.shader";

    /// <summary>遮蔽を求めて、ぼかした結果を返す</summary>
    /// <param name="normal">ワールド空間の法線 (G-Buffer)。null なら深度から作る</param>
    public RenderTexture Render(RenderCommandBuffer cmd, SsaoSettings settings, RenderTexture? normal)
    {
        var scale = Math.Clamp(settings.ResolutionScale, 0.25f, 1.0f);
        if (_ao == null || scale != _scale)
        {
            DisposeTargets();
            _scale = scale;
            _ao   = RenderTexture.Create(RenderTextureFormat.R8, scale);
            _blur = RenderTexture.Create(RenderTextureFormat.R8, scale);
        }
        _ssao       ??= FullscreenShader.Load(ShaderPath, "PSSsao");
        _blurShader ??= FullscreenShader.Load(ShaderPath, "PSSsaoBlur");

        var depth = RenderTexture.SceneDepth;
        cmd.SetRenderTarget(_ao);
        cmd.DrawFullscreen(_ssao, [depth, normal ?? depth],
            [settings.Radius, settings.Intensity, settings.Bias, Math.Clamp(settings.SampleCount, 1, 32), normal != null ? 1.0f : 0.0f]);

        // ぼかし : 横 → 縦 (1 歩 = 遮蔽のバッファの 1 ピクセル。シェーダーがテクスチャの大きさから求める)
        const float sharpness = 40.0f;   // 深度が 2.5% 違うと重みが半分
        cmd.SetRenderTarget(_blur!);
        cmd.DrawFullscreen(_blurShader, [_ao, depth], [1.0f, 0.0f, sharpness]);
        cmd.SetRenderTarget(_ao);
        cmd.DrawFullscreen(_blurShader, [_blur!, depth], [0.0f, 1.0f, sharpness]);
        return _ao;
    }

    /// <summary>Forward 用 : 「環境光 × 遮蔽」を今の描画先 (HDR) に足す</summary>
    public void ApplyAmbient(RenderCommandBuffer cmd, RenderTexture hdr, RenderTexture ambient, RenderTexture ao)
    {
        _applyAmbient ??= FullscreenShader.Load(ShaderPath, "PSApplyAmbient");
        cmd.SetRenderTarget(hdr);
        cmd.DrawFullscreen(_applyAmbient, [ambient, ao], [], FullscreenBlend.Additive);
    }

    /// <summary>確認用 : 遮蔽を白黒で画面に出す</summary>
    public void Show(RenderCommandBuffer cmd, RenderTexture ao)
    {
        _showAo ??= FullscreenShader.Load(ShaderPath, "PSShowAo");
        cmd.SetRenderTarget(RenderTexture.BackBuffer);
        cmd.DrawFullscreen(_showAo, [ao], []);
    }


    private void DisposeTargets()
    {
        _ao?.Dispose();
        _blur?.Dispose();
        _ao = _blur = null;
    }

    public void Dispose() => DisposeTargets();
}
