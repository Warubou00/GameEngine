using GameEngine.Native;

namespace GameEngine.Rendering;

// ==========================================================================
// 影の設定 (Graphics.Shadows)。変えると次のフレームから反映される
//   影を落とすのは、CastShadows の付いた最初の DirectionalLight (太陽) 1 つ
// ==========================================================================
public sealed class ShadowSettings
{
    public bool  Enabled    { get; set; } = true;
    public int   Resolution { get; set; } = 2048;    // シャドーマップの大きさ (256〜8192)
    public float Distance   { get; set; } = 30.0f;   // 影を描く距離 (カメラからの奥行き。遠くするほど 1 texel が粗くなる)
    public float NormalBias { get; set; } = 1.0f;    // 法線方向へずらす量 (texel の何倍か)。縞 (シャドーアクネ) が出たら上げる
    public float Softness   { get; set; } = 1.0f;    // 影の縁のぼかし (PCF で読む間隔。texel)
}

// ==========================================================================
// シャドーマップのパス (Forward / Deferred 共通の部品)
//   1. シャドーマップ (Depth32) にライトの視点で深度だけを描く (不透明 + 半透明はアルファテスト)
//   2. カメラの描画 (Forward のピクセルシェーダー / ディファードのライティング) がシャドーマップを読む
//   ライトの視点の行列は C++ がカメラに合わせて毎フレーム作る (カメラの視錐台を囲み、テクセル単位に丸めてちらつかせない)
//   C# は設定とシャドーマップを渡すだけ (変わった時だけ P/Invoke)
// ==========================================================================
public sealed class ShadowPass : IDisposable
{
    private RenderTexture? _map;
    private int _resolution;
    private NativeMethods.ShadowSettings _sent;
    private bool _hasSent;

    public RenderTexture? ShadowMap => _map;

    /// <summary>影を使うなら、シャドーマップを描く命令を積んで true を返す</summary>
    public bool Render(RenderCommandBuffer cmd, GraphicsSettings graphics)
    {
        var settings = graphics.Shadows;
        var use = settings.Enabled && graphics.ShadowLightIndex >= 0 && graphics.LightingModel != LightingModel.Unlit;
        if (!use)
        {
            Send(default);   // 影なし (変わった時だけ送る)
            return false;
        }

        var resolution = Math.Clamp(settings.Resolution, 256, 8192);
        if (_map == null || resolution != _resolution)
        {
            _map?.Dispose();   // C++ は消したシャドーマップの影を止める。下で新しい物を送り直す
            _map = RenderTexture.CreateFixed(RenderTextureFormat.Depth32, (uint)resolution, (uint)resolution);
            _resolution = resolution;
            _hasSent = false;
        }

        Send(new NativeMethods.ShadowSettings
        {
            Enabled      = 1,
            LightIndex   = (uint)graphics.ShadowLightIndex,
            ShadowMap    = _map.Handle,
            CascadeCount = 1,
            Distance     = MathF.Max(settings.Distance, 0.1f),
            NormalBias   = MathF.Max(settings.NormalBias, 0.0f),
            Softness     = MathF.Max(settings.Softness, 0.0f),
            SplitLambda  = 0.75f,
        });

        // 視点 1 = ライト (カスケード 1 つ目)。カリングは C++ がライトの直方体で行う
        cmd.SetRenderTargets(ReadOnlySpan<RenderTexture>.Empty, _map);
        cmd.ClearDepth();
        cmd.DrawScene(ScenePass.Shadow, RenderQueue.Opaque, view: 1);
        cmd.DrawScene(ScenePass.Shadow, RenderQueue.Transparent, view: 1);
        return true;
    }

    private void Send(NativeMethods.ShadowSettings settings)
    {
        if (_hasSent && settings.SameAs(_sent)) return;
        NativeMethods.Renderer_SetShadowSettings(ref settings);
        _sent    = settings;
        _hasSent = true;
    }

    public void Dispose()
    {
        _map?.Dispose();
        _map = null;
        _hasSent = false;
    }
}
