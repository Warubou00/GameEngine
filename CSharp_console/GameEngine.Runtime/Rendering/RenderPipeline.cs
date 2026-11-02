using System.Numerics;

namespace GameEngine.Rendering;

// ==========================================================================
// 描画パイプライン (Unity の Scriptable Render Pipeline に当たる)
//   1 フレームの描き方 (どの描画先に、何を、どの順番で描くか) を C# で組み立てる。
//   Graphics.Pipeline に入れると、次のフレームから使われる (前のパイプラインは Dispose される)
//   以前は C++ の中で「シーンをバックバッファに描く」1 通りに決まっていた
//
//   自分で作る時 :
//     Initialize : レンダーターゲットや全画面のシェーダーを作る (切り替えた時に 1 回)
//     Render     : 毎フレーム cmd に命令を積む (積むだけ。描くのはエンジンがまとめて C++ で行う)
// ==========================================================================
public abstract class RenderPipeline : IDisposable
{
    private FullscreenShader? _tonemap;

    // SSAO (Forward / Deferred 共通の部品)
    protected SsaoPass Ssao { get; } = new();

    public abstract string Name { get; }

    protected internal abstract void Initialize(GraphicsSettings settings);
    protected internal abstract void Render(RenderCommandBuffer cmd, GraphicsSettings settings);

    public virtual void Dispose()
    {
        Ssao.Dispose();
    }

    // SSAO を使うか (Unlit は環境光が無いので使わない)
    protected static bool UseSsao(GraphicsSettings settings) =>
        settings.Ssao.Enabled && settings.LightingModel != LightingModel.Unlit;

    // HDR (線形) → 画面 (露出 / トーンマップ / ガンマ)。最後にバックバッファへ描く
    //   ao があり Ssao.ShowOnly なら、代わりに遮蔽を白黒で出す (確認用)
    protected void Tonemap(RenderCommandBuffer cmd, RenderTexture hdr, GraphicsSettings settings, RenderTexture? ao = null)
    {
        if (ao != null && settings.Ssao.ShowOnly)
        {
            Ssao.Show(cmd, ao);
            return;
        }
        _tonemap ??= FullscreenShader.Load(ShaderPaths.PostProcess, "PSTonemap");
        // ライト無し (Unlit) は以前と同じ見た目になるよう、トーンマップを掛けない (色をそのまま出す)
        var unlit  = settings.LightingModel == LightingModel.Unlit;
        var method = unlit ? Tonemapper.None : settings.Tonemapper;
        cmd.SetRenderTarget(RenderTexture.BackBuffer);
        cmd.DrawFullscreen(_tonemap, [hdr], [unlit ? 1.0f : settings.Exposure, (float)method, settings.Gamma]);
    }

    // sRGB の色 → 線形 (シェーダーの toLinear と同じ近似)
    protected static Vector4 ToLinear(Vector3 srgb) =>
        new(MathF.Pow(srgb.X, 2.2f), MathF.Pow(srgb.Y, 2.2f), MathF.Pow(srgb.Z, 2.2f), 1.0f);
}

internal static class ShaderPaths
{
    public const string Scene       = "../Shader/hlslTemplate.shader";
    public const string PostProcess = "../Shader/postProcess.shader";
}

// ==========================================================================
// フォワード : 物を描く時にライティングまで済ませる (HDR に描いてトーンマップ)
//   ライトが少ない / 半透明が多い場面向き。G-Buffer が要らないのでメモリと帯域が少ない
//   SSAO あり : ライトの光 (HDR) と環境光 (R11G11B10F) を分けて描き、深度から遮蔽を求めて
//               「環境光 × 遮蔽」を HDR に足す (遮蔽が環境光にだけ掛かる。深度のプリパスも要らない)
// ==========================================================================
public sealed class ForwardRenderPipeline : RenderPipeline
{
    private RenderTexture? _hdr;
    private RenderTexture? _ambient;   // SSAO の時だけ作る
    private Vector3 _background;

    public override string Name => "Forward";

    protected internal override void Initialize(GraphicsSettings settings)
    {
        CreateTargets(settings);
    }

    private void CreateTargets(GraphicsSettings settings)
    {
        _hdr?.Dispose();
        _background = settings.BackgroundColor;
        // 背景の色を最適化クリア値にしておく (毎フレームその色で消すので速い)
        _hdr = RenderTexture.Create(RenderTextureFormat.RGBA16F, 1.0f, ToLinear(_background));
    }

    protected internal override void Render(RenderCommandBuffer cmd, GraphicsSettings settings)
    {
        if (settings.BackgroundColor != _background) CreateTargets(settings);
        var hdr = _hdr!;
        RenderTexture? ao = null;

        if (UseSsao(settings))
        {
            // 背景は環境光 0 (黒) で消す。足すのは環境光の分だけなので背景の色は変わらない
            _ambient ??= RenderTexture.Create(RenderTextureFormat.R11G11B10F);
            cmd.SetRenderTarget(_ambient);
            cmd.ClearColor(Vector4.Zero);
            cmd.SetRenderTarget(hdr, RenderTexture.SceneDepth);
            cmd.ClearColor(hdr.ClearColor);
            cmd.ClearDepth();
            cmd.SetRenderTargets([hdr, _ambient], RenderTexture.SceneDepth);
            cmd.DrawScene(ScenePass.ForwardSplitAmbient, RenderQueue.Opaque);

            ao = Ssao.Render(cmd, settings.Ssao, normal: null);   // 法線は深度から作る
            Ssao.ApplyAmbient(cmd, hdr, _ambient, ao);
        }
        else
        {
            cmd.SetRenderTarget(hdr, RenderTexture.SceneDepth);
            cmd.ClearColor(hdr.ClearColor);
            cmd.ClearDepth();
            cmd.DrawScene(ScenePass.Forward, RenderQueue.Opaque);
        }

        // 半透明 (遮蔽は掛けない)
        cmd.SetRenderTarget(hdr, RenderTexture.SceneDepth);
        cmd.DrawScene(ScenePass.Forward, RenderQueue.Transparent);

        Tonemap(cmd, hdr, settings, ao);
    }

    public override void Dispose()
    {
        _hdr?.Dispose();
        _ambient?.Dispose();
        _hdr = _ambient = null;
        base.Dispose();
    }
}

// ==========================================================================
// ディファード : 先に G-Buffer (アルベド / 法線 / 金属・粗さ) と深度だけを描き、
//               ライティングは全画面で 1 ピクセル 1 回だけ行う
//   ライトが多い / 重なりが多い場面ほど得 (隠れた面のライティングをしない)。
//   半透明は G-Buffer に書けないので、ライティングの後にフォワードで重ねる
//   G-Buffer : RT0 RGBA8_SRGB アルベド / RT1 RGBA16F 法線 / RT2 RGBA8 金属・粗さ (1280x720 で約 14MB)
//   SSAO あり : G-Buffer の法線と深度から遮蔽を求め、ライティングが環境光に掛ける
// ==========================================================================
public sealed class DeferredRenderPipeline : RenderPipeline
{
    private RenderTexture? _albedo;
    private RenderTexture? _normal;
    private RenderTexture? _material;
    private RenderTexture? _hdr;
    private FullscreenShader? _lighting;

    public override string Name => "Deferred";

    protected internal override void Initialize(GraphicsSettings settings)
    {
        _albedo   = RenderTexture.Create(RenderTextureFormat.RGBA8_SRGB);
        _normal   = RenderTexture.Create(RenderTextureFormat.RGBA16F);
        _material = RenderTexture.Create(RenderTextureFormat.RGBA8);
        _hdr      = RenderTexture.Create(RenderTextureFormat.RGBA16F);
        _lighting = FullscreenShader.Load(ShaderPaths.Scene, "PSDeferredLighting");
    }

    protected internal override void Render(RenderCommandBuffer cmd, GraphicsSettings settings)
    {
        var hdr = _hdr!;

        // 1. G-Buffer (不透明だけ)。消すのは深度だけでよい (何も描かれていない所はライティングが深度で見分ける)
        cmd.SetRenderTargets([_albedo!, _normal!, _material!], RenderTexture.SceneDepth);
        cmd.ClearDepth();
        cmd.DrawScene(ScenePass.GBuffer, RenderQueue.Opaque);

        // 2. SSAO (G-Buffer の法線を使う)。使わない時は入力を渡さない (シェーダーは白 = 遮蔽なし を読む)
        var ao = UseSsao(settings) ? Ssao.Render(cmd, settings.Ssao, _normal) : null;

        // 3. ライティング (全画面。背景もここで塗る)
        var background = ToLinear(settings.BackgroundColor);
        cmd.SetRenderTarget(hdr);
        if (ao != null)
        {
            cmd.DrawFullscreen(_lighting!, [_albedo!, _normal!, _material!, RenderTexture.SceneDepth, ao], [background.X, background.Y, background.Z]);
        }
        else
        {
            cmd.DrawFullscreen(_lighting!, [_albedo!, _normal!, _material!, RenderTexture.SceneDepth], [background.X, background.Y, background.Z]);
        }

        // 4. 半透明はフォワードで重ねる (深度は G-Buffer のものを使う)
        cmd.SetRenderTarget(hdr, RenderTexture.SceneDepth);
        cmd.DrawScene(ScenePass.Forward, RenderQueue.Transparent);

        Tonemap(cmd, hdr, settings, ao);
    }

    public override void Dispose()
    {
        _albedo?.Dispose();
        _normal?.Dispose();
        _material?.Dispose();
        _hdr?.Dispose();
        _albedo = _normal = _material = _hdr = null;
        base.Dispose();
    }
}
