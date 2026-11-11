using GameEngine.Native;

namespace GameEngine.Rendering;

// ==========================================================================
// 影の設定 (Graphics.Shadows)。変えると次のフレームから反映される
//   影を落とすのは、CastShadows の付いた最初の DirectionalLight (太陽) 1 つ
// ==========================================================================
public sealed class ShadowSettings
{
    public bool  Enabled      { get; set; } = true;
    public int   Resolution   { get; set; } = 2048;    // カスケード 1 段の大きさ (256〜8192)
    public float Distance     { get; set; } = 60.0f;   // 影を描く距離 (カメラからの奥行き)。S1 は 30 (1 段で遠くまで描くと粗くなるため)
    public float NormalBias   { get; set; } = 1.0f;    // 法線方向へずらす量 (texel の何倍か)。縞 (シャドーアクネ) が出たら上げる
    public float Softness     { get; set; } = 1.0f;    // 影の縁のぼかし (PCF で読む間隔。texel)

    // ---------- カスケード (S2) ----------
    // 段の数 (1〜4)。近い段ほど細かい。既定 3 : 見本のシーンでは 4 段と見た目が変わらず、0.12 ms 軽い
    public int   CascadeCount { get; set; } = 3;
    public float SplitLambda  { get; set; } = 0.75f;   // 段の分け方 (0 = 均等 / 1 = 対数 = 近くに細かく)
    public float CascadeBlend { get; set; } = 0.1f;    // 段の境目をなめらかにつなぐ幅 (段の奥行きに対する割合。0〜0.5)
    public bool  ShowCascades { get; set; }            // 確認用 : 段ごとに色を付ける (赤 → 緑 → 青 → 黄)

    // ---------- 効率化 (S2 で比べた結果は ClaudLog/Engine_Shadow_S2) ----------
    // 全段を囲む箱で 1 回だけカリングする。段ごとにカリングすると、前の段の描画が終わるまで次の段のカリングが待つので、
    // 物が 2 万個の負荷テストでも 1 回の方が速かった (4 段 : 0.05〜0.10 ms)
    public bool  UnionCulling { get; set; } = true;
    // 遠い段の描き直しを間引く (3 段目 : 2 フレームに 1 回 / 4 段目 : 4 フレームに 1 回)。動く物の遠くの影が少し遅れる
    public bool  ThrottleFarCascades { get; set; } = true;
}

// ==========================================================================
// シャドーマップのパス (Forward / Deferred 共通の部品)
//   1. シャドーマップ (Depth32 の配列。カスケード 1 段に 1 枚) に、ライトの視点で深度だけを描く
//      (不透明 + 半透明はアルファテスト)。段ごとにカリングして描く
//   2. カメラの描画 (Forward のピクセルシェーダー / ディファードのライティング) がシャドーマップを読む
//   ライトの視点の行列は C++ がカメラに合わせて毎フレーム作る (段ごとに視錐台を囲み、テクセル単位に丸めてちらつかせない)
//   C# は設定とシャドーマップを渡すだけ (変わった時だけ P/Invoke。間引きの時は毎フレーム描く段が変わるので毎フレーム)
//   以前 (S1) は 1 段 (Texture2D) だった
// ==========================================================================
public sealed class ShadowPass : IDisposable
{
    private RenderTexture? _map;
    private int _resolution;
    private int _cascades;
    private NativeMethods.ShadowSettings _sent;
    private bool _hasSent;
    private long _frame;
    private bool _drawAll = true;
    internal bool DrawEveryCascade { get; set; }   // v1.5 : 複数のカメラで使い回す時は全段を毎回描く   // 作り直した直後は全段を描く (間引くと前の内容が無い)

    public RenderTexture? ShadowMap => _map;

    /// <summary>影を使うなら、シャドーマップを描く命令を積んで true を返す</summary>
    public bool Render(RenderCommandBuffer cmd, GraphicsSettings graphics)
    {
        var settings = graphics.Shadows;
        var use = settings.Enabled && graphics.ShadowLightIndex >= 0 && graphics.LightingModel != LightingModel.Unlit;
        if (!use)
        {
            Send(default);   // 影なし (変わった時だけ送る)
            _drawAll = true;
            return false;
        }

        var resolution = Math.Clamp(settings.Resolution, 256, 8192);
        var cascades   = Math.Clamp(settings.CascadeCount, 1, 4);
        if (_map == null || resolution != _resolution || cascades != _cascades)
        {
            _map?.Dispose();   // C++ は消したシャドーマップの影を止める。下で新しい物を送り直す
            _map = RenderTexture.CreateFixed(RenderTextureFormat.Depth32, (uint)resolution, (uint)resolution, arraySize: cascades);
            _resolution = resolution;
            _cascades   = cascades;
            _hasSent    = false;
            _drawAll    = true;
        }

        // このフレームで描き直さない段 (間引き) : 3 段目は偶数フレーム、4 段目は 4 フレームに 1 回 (3 段目と重ならないように)
        uint frozen = 0;
        // v1.5 : カメラが複数の時は間引かない (前のカメラの段が残っているので)
        if (settings.ThrottleFarCascades && !_drawAll && !DrawEveryCascade)
        {
            if (cascades >= 3 && (_frame & 1) != 0) frozen |= 1u << 2;
            if (cascades >= 4 && (_frame & 3) != 1) frozen |= 1u << 3;
        }
        _frame++;
        _drawAll = false;

        uint flags = 0;
        if (settings.UnionCulling) flags |= 1;
        if (settings.ShowCascades) flags |= 2;
        Send(new NativeMethods.ShadowSettings
        {
            Enabled      = 1,
            LightIndex   = (uint)graphics.ShadowLightIndex,
            ShadowMap    = _map.Handle,
            CascadeCount = (uint)cascades,
            Distance     = MathF.Max(settings.Distance, 0.1f),
            NormalBias   = MathF.Max(settings.NormalBias, 0.0f),
            Softness     = MathF.Max(settings.Softness, 0.0f),
            SplitLambda  = Math.Clamp(settings.SplitLambda, 0.0f, 1.0f),
            CascadeBlend = Math.Clamp(settings.CascadeBlend, 0.0f, 0.5f),
            Flags        = flags,
            FrozenMask   = frozen,
        });

        // 段ごとに : 配列の c 枚目を消して、視点 c + 1 (ライトのカスケード c) で描く。カリングは C++ が段ごと (または 1 回) に行う
        for (int c = 0; c < cascades; c++)
        {
            if ((frozen & (1u << c)) != 0) continue;
            cmd.SetRenderTargets(ReadOnlySpan<RenderTexture>.Empty, _map, depthSlice: c);
            cmd.ClearDepth();
            cmd.DrawScene(ScenePass.Shadow, RenderQueue.Opaque, view: c + 1);
            cmd.DrawScene(ScenePass.Shadow, RenderQueue.Transparent, view: c + 1);
        }
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
