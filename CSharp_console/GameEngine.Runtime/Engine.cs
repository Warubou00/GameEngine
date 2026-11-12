using System.Diagnostics;
using System.Numerics;
using GameEngine.Native;
using GameEngine.Rendering;

namespace GameEngine;

// ==========================================================================
// エンジンの起動設定
// ==========================================================================
public sealed class EngineSettings
{
    public string Title  { get; init; } = EngineInfo.Name;   // 以前は "GameEngine"
    public int    Width  { get; init; } = 1280;   // 描画領域 (クライアント領域) の大きさ
    public int    Height { get; init; } = 720;
    public bool   VSync  { get; init; } = false;  // false : 対応環境ではテアリングを許可して上限なしで描く
    // 相対パス (モデル / シェーダー / キャッシュ) の起点。null なら実行ファイルのフォルダ (ED3 : エディターは見本の出力フォルダを使う)
    public string? WorkingDirectory { get; init; }
}

// シーンのカメラの代わりに使うカメラ (エディターのカメラ。ED3)
public struct CameraOverride
{
    public Vector3    Position;
    public Quaternion Rotation;
    public float      FieldOfView;   // 縦の視野角 (度)
    public float      NearClip;
    public float      FarClip;
}

// ==========================================================================
// 描画の設定 (プロファイラー (F1) のチェックボックスでも切り替えられる)
// ==========================================================================
public enum LightingModel : uint
{
    Unlit   = 0,   // ライト無し (テクスチャの色そのまま。以前の見た目)
    Lambert = 1,   // 拡散反射だけ
    PBR     = 2,   // 物理ベース (GGX / 金属・粗さ)
}

public enum Tonemapper : uint
{
    None     = 0,   // 1 を超えた所は白く飛ぶ
    Reinhard = 1,
    ACES     = 2,   // 映画の色 (明るい所をなだらかに丸める)
}

public sealed class GraphicsSettings
{
    private bool _indirectDraw = true;
    private bool _instancing   = true;
    private bool _gpuCulling   = true;
    private bool _occlusion    = true;

    // 描画パイプライン : 切り替えは次のフレームの始めに行う (C++ の準備ができる前に設定してもよいように)
    private RenderPipeline? _pipeline;
    private RenderPipeline? _pendingPipeline = new ForwardRenderPipeline();
    private LightingModel   _lightingModel   = LightingModel.PBR;
    private Vector3         _ambient         = new(0.25f, 0.27f, 0.30f);
    private bool            _lightingDirty   = true;
    private bool            _infoDirty       = true;

    internal GraphicsSettings()
    {
    }

    // ---------- 描画パイプライン / ライティング ----------

    // 1 フレームの描き方 (Forward / Deferred / 自作)。前のパイプラインは Dispose される
    public RenderPipeline Pipeline
    {
        get => _pendingPipeline ?? _pipeline!;
        set => _pendingPipeline = value ?? throw new ArgumentNullException(nameof(value));
    }

    public LightingModel LightingModel
    {
        get => _lightingModel;
        set { _lightingModel = value; _lightingDirty = true; }
    }

    // 環境光 (空の明るさ。線形)
    public Vector3 AmbientColor
    {
        get => _ambient;
        set { _ambient = value; _lightingDirty = true; }
    }

    // ---------- 複数のカメラ (v1.5) ----------
    //   メインのカメラ (Depth が一番小さい) の値は、このクラスの値 (Exposure / Ssao など) そのもの
    //   2 台目からは、描く間だけ、そのカメラの値に入れ替える (パイプラインはカメラごとに 1 つ。メインは Pipeline)

    /// <summary>このフレームに描くカメラの数 (シーンにカメラが無ければ 1 = 前のカメラのまま)</summary>
    public int CameraCount => Math.Max(1, RenderCameras.Count);

    /// <summary>今描いているカメラの消し方 (パイプラインが見る。DepthOnly なら背景を描かず、透明のまま前のカメラに重ねる)</summary>
    public CameraClearFlags ClearFlags { get; internal set; } = CameraClearFlags.Skybox;

    /// <summary>今描いているカメラの描き込み先 (トーンマップ / FXAA の行き先。画面全体のカメラならバックバッファ)</summary>
    public RenderTexture CameraTarget { get; internal set; } = RenderTexture.BackBuffer;

    /// <summary>今描いているカメラの番号 (0 = メイン)</summary>
    public int CurrentCamera { get; private set; }

    /// <summary>カメラ i の画面の範囲 (ピクセル。左上が原点。Viewport Rect は左下が原点)</summary>
    public (int X, int Y, int Width, int Height) GetCameraViewport(int index)
    {
        var rect = index >= 0 && index < RenderCameras.Count ? CameraSystem.ClampRect(RenderCameras[index].ViewportRect) : new Vector4(0, 0, 1, 1);
        var (w, h) = ScreenSize;
        var x0 = (int)MathF.Round(rect.X * w);
        var x1 = (int)MathF.Round((rect.X + rect.Z) * w);
        var y0 = (int)MathF.Round((1.0f - rect.Y - rect.W) * h);
        var y1 = (int)MathF.Round((1.0f - rect.Y) * h);
        return (x0, y0, Math.Max(1, x1 - x0), Math.Max(1, y1 - y0));
    }

    /// <summary>カメラ i の描き方の値 (Volume を混ぜた後。確認用)</summary>
    public Camera GetCameraSettings(int index) => index >= 0 && index < RenderCameras.Count ? RenderCameras[index] : new Camera();

    /// <summary>カメラ i のパイプライン (0 = Pipeline。2 台目からはカメラの RenderPath で作った物。まだ描いていなければ null)</summary>
    public RenderPipeline? GetCameraPipeline(int index) => index == 0 ? _pipeline : index > 0 && index < _cameraPipelines.Length ? _cameraPipelines[index] : null;

    /// <summary>最後のフレームで、自分の絵に描いてから画面の範囲に重ねたカメラの数 (範囲が一部 / DepthOnly)</summary>
    public int CompositedCameras { get; private set; }

    internal readonly List<Camera> RenderCameras = new();   // このフレームのカメラ (Volume を混ぜた値。RenderSettingsSystem が入れる)
    internal (int Width, int Height) ScreenSize { get; set; }
    private readonly RenderPipeline?[] _cameraPipelines = new RenderPipeline?[NativeMethods.MaxCameras];   // 2 台目から
    private readonly RenderTexture?[]  _cameraTargets   = new RenderTexture?[NativeMethods.MaxCameras];   // 範囲が一部 / DepthOnly のカメラの絵
    private FullscreenShader? _composite;
    private int _shownCameraCount = 1;

    public float      Exposure        { get; set; } = 1.0f;     // HDR の明るさの倍率
    public Tonemapper Tonemapper      { get; set; } = Tonemapper.ACES;
    public float      Gamma           { get; set; } = 2.2f;
    public Vector3    BackgroundColor { get; set; } = new(0.1f, 0.2f, 0.4f);   // 何も無い所の色 (sRGB。以前のクリア色)。Skybox を切った時
    public SkyboxSettings Skybox { get; } = new();   // v1.4 : 空 (既定で出す。画像が無ければエンジンの既定の空)

    // SSAO (環境光の遮蔽。Forward / Deferred の両方で使える。Unlit では使わない)
    public SsaoSettings Ssao { get; } = new();
    private int _shownSsaoState = -1;

    // 影 (CastShadows の付いた最初の DirectionalLight。Forward / Deferred の両方。Unlit では使わない)
    public ShadowSettings Shadows { get; } = new();
    // GPU パーティクル (ParticleEmitter。Forward / Deferred の両方。容量 / ソート / 衝突)
    public ParticleSettings Particles { get; } = new();
    // 影を落とすライトの番号 (LightSystem が決める。-1 なら影を落とすライトが無い)
    public int ShadowLightIndex { get; internal set; } = -1;
    private int _shownShadowState = -1;

    // アンチエイリアス (FXAA = ポストエフェクト / MSAA = 固定機能。Deferred では MSAA の代わりに FXAA)
    public AntiAliasing AntiAliasing { get; set; } = AntiAliasing.Fxaa;
    public FxaaSettings Fxaa { get; } = new();
    // GPU が MSAA で使えるサンプル数の上限 (1 なら MSAA は使えない。C++ の準備ができた後に分かる)
    public int MaxMsaaSamples { get; private set; } = 1;
    private bool _maxSamplesQueried;
    private string _shownAntiAliasing = "";

    // フレームの始め : パイプラインの切り替えとライティングの設定を C++ へ (変わった時だけ)
    internal void BeginFrame()
    {
        if (!_maxSamplesQueried)
        {
            MaxMsaaSamples     = (int)Math.Max(1u, NativeMethods.Renderer_GetMaxSampleCount());
            _maxSamplesQueried = true;
        }
        if (_pendingPipeline != null)
        {
            _pipeline?.Dispose();
            _pipeline = _pendingPipeline;
            _pendingPipeline = null;
            _pipeline.Initialize(this);
            _infoDirty = true;
        }
        if (_lightingDirty)
        {
            var settings = new NativeMethods.LightingSettings { Model = (uint)_lightingModel, Ambient = _ambient };
            NativeMethods.Renderer_SetLightingSettings(ref settings);
            _lightingDirty = false;
            _infoDirty     = true;
        }
        // SSAO の ON / OFF も表示する (変わった時だけ文字列を作る)
        var ssaoState = !Ssao.Enabled ? 0 : Ssao.ShowOnly ? 2 : 1;
        var shadowState = Shadows.Enabled && ShadowLightIndex >= 0 && _lightingModel != LightingModel.Unlit ? Shadows.Resolution * 8 + Shadows.CascadeCount : 0;
        var antiAliasing = _pipeline!.AntiAliasingName(this);   // 定数の文字列 (毎フレーム作らない)
        if (_infoDirty || ssaoState != _shownSsaoState || antiAliasing != _shownAntiAliasing || shadowState != _shownShadowState || CameraCount != _shownCameraCount)
        {
            var ssao   = ssaoState switch { 0 => "", 1 => " / SSAO", _ => " / SSAO (AO only)" };
            var shadow = shadowState > 0 ? $" / Shadows {Shadows.Resolution} x{Shadows.CascadeCount}" : "";
            var count  = CameraCount > 1 ? $" / {CameraCount} cameras" : "";   // v1.5
            NativeMethods.Renderer_SetProfilerInfo("Render pipeline", $"{_pipeline!.Name} / {_lightingModel}{ssao}{shadow} / {antiAliasing}{count}");
            _shownCameraCount  = CameraCount;
            _infoDirty         = false;
            _shownSsaoState    = ssaoState;
            _shownAntiAliasing = antiAliasing;
            _shownShadowState  = shadowState;
        }
    }

    // ---------- エディター (ED3) : シーンを描く大きさ / UI ----------

    /// <summary>シーンを描いたテクスチャ (RenderSize を決めた時。UI の Texture に入れるとシーンビューになる)</summary>
    public const uint ViewTexture = 0xFFFFFFF0u;

    /// <summary>シーンを描く大きさ (0 x 0 = 画面と同じ。以前と同じ)。決めると、シーンは ViewTexture に描かれ、画面には UI だけ</summary>
    public (int Width, int Height) RenderSize { get; private set; }

    public void SetRenderSize(int width, int height)
    {
        width  = Math.Max(0, width);
        height = Math.Max(0, height);
        if (width == 0 || height == 0) width = height = 0;
        if ((width, height) == RenderSize) return;
        if (NativeMethods.Renderer_SetRenderSize((uint)width, (uint)height)) RenderSize = (width, height);
    }

    /// <summary>UI の描画 (毎フレーム呼ぶ。空なら消える)。描くのはこのフレームの一番最後</summary>
    public unsafe void SubmitUI(ReadOnlySpan<UIVertex> vertices, ReadOnlySpan<ushort> indices, ReadOnlySpan<UIDrawCommand> commands)
    {
        fixed (UIVertex* v = vertices)
        fixed (ushort* i = indices)
        fixed (UIDrawCommand* c = commands)
        {
            NativeMethods.Renderer_SetUIDrawData(v, (uint)vertices.Length, i, (uint)indices.Length, c, (uint)commands.Length);
        }
    }

    /// <summary>UI のテクスチャ (RGBA8。フォントなど)。戻り値は UIDrawCommand.Texture に入れる番号 (失敗時 0xFFFFFFFF)</summary>
    public unsafe uint CreateUITexture(int width, int height, ReadOnlySpan<byte> rgba)
    {
        if (rgba.Length < width * height * 4) throw new ArgumentException("rgba is too small.");
        fixed (byte* p = rgba) return NativeMethods.Renderer_CreateUITexture((uint)width, (uint)height, p);
    }

    /// <summary>シェーダーを読み直す (ED5。GPU を待ってからコンパイルし直す。失敗したシェーダーは前の物のまま)。戻り値 = 全部成功したか</summary>
    public bool ReloadShaders() => NativeMethods.Renderer_ReloadShaders();

    /// <summary>GPU のメガバッファで使っている頂点 / インデックスの量 (byte。解放した分は除く。ED5)</summary>
    public long MeshBytesUsed => (long)NativeMethods.Renderer_GetMeshBytesUsed();

    /// <summary>C++ に読み込んであるモデルの数 (ED5)</summary>
    public int LoadedModelCount => (int)NativeMethods.Renderer_GetLoadedModelCount();

    /// <summary>C++ のプロファイラー (F1) を出すか</summary>
    /// <summary>最後のフレームで自分のシェーダー (SH2) で描いた物の数 (確認用)</summary>
    public int CustomDrawCount => (int)NativeMethods.Renderer_GetCustomDrawCount();

    public bool ProfilerVisible
    {
        get => NativeMethods.Renderer_IsProfilerVisible();
        set => NativeMethods.Renderer_SetProfilerVisible(value);
    }

    /// <summary>
    /// シーンのカメラの代わりに使うカメラ (エディターのカメラ。null ならシーンの最初の Camera)
    /// </summary>
    public CameraOverride? CameraOverride { get; set; }

    internal void RenderFrame(RenderCommandBuffer commands)
    {
        commands.Clear();
        renderCameras(commands);
        foreach (var preview in _previews) preview.Submit(commands);   // AN5 : プレビューの窓 (シーンの後、UI の前)
        commands.Submit();
        // AN5 : 窓を閉じた / 大きさを変えたレンダーターゲットは、このフレームを描いた後で消す
        //       (以前はその場で消していたので、同じフレームの UI がもう消えたテクスチャを読み、GPU が止まった (デバイスの削除))
        foreach (var texture in _retiredTextures) texture.Dispose();
        _retiredTextures.Clear();
    }

    // v1.5 : カメラを Depth の順に描く (以前はパイプラインを 1 回だけ描いた)
    //   画面全体で背景を消すカメラ : バックバッファへ直接 (1 台だけの時は以前と同じ)
    //   範囲が一部 / DepthOnly のカメラ : 自分の絵 (RGBA8。画面と同じ大きさ) に描いてから、範囲に重ねる
    //     (DepthOnly は透明の所 (何も写っていない所) が前のカメラの絵のまま)
    private void renderCameras(RenderCommandBuffer cmd)
    {
        var count = Math.Min(CameraCount, NativeMethods.MaxCameras);
        var main  = RenderCameras.Count > 0 ? RenderCameras[0] : new Camera();
        var mainClear = RenderCameras.Count > 0 ? main.ClearFlags : CameraClearFlags.Skybox;

        // 使わなくなったカメラのパイプライン / 絵を片付ける
        for (int i = count; i < _cameraPipelines.Length; i++)
        {
            _cameraPipelines[i]?.Dispose();
            _cameraPipelines[i] = null;
        }
        for (int i = count; i < _cameraTargets.Length; i++)
        {
            RetireTexture(_cameraTargets[i]);
            _cameraTargets[i] = null;
        }

        // 1 台目が画面全体を塗らない時は、先にバックバッファを黒で消す
        if (!CameraSystem.IsFullRect(CameraSystem.ClampRect(main.ViewportRect)) || mainClear == CameraClearFlags.DepthOnly)
        {
            cmd.SetRenderTarget(RenderTexture.BackBuffer);
            cmd.ClearColor(new Vector4(0.0f, 0.0f, 0.0f, 1.0f));
        }

        // メインの値 (2 台目からの間、入れ替えるので覚えておく)
        var saved = count > 1 ? captureValues() : default;
        var composited = 0;
        for (int i = 0; i < count; i++)
        {
            var camera   = i < RenderCameras.Count ? RenderCameras[i] : main;
            var pipeline = i == 0 ? _pipeline! : pipelineOf(i, camera.RenderPath);
            pipeline.SharedShadows = i == 0 ? null : _pipeline!.OwnShadows;   // v1.5 : シャドーマップは 1 つを使い回す
            _pipeline!.OwnShadows.DrawEveryCascade = count > 1;
            if (i > 0)
            {
                applyValues(camera);
                cmd.SetCamera(i);   // C++ : このカメラの行列 / 定数に替えて、見える物を選び直す
            }
            CurrentCamera = i;
            ClearFlags = i == 0 ? mainClear : camera.ClearFlags;
            var rect   = CameraSystem.ClampRect(camera.ViewportRect);
            var direct = CameraSystem.IsFullRect(rect) && ClearFlags != CameraClearFlags.DepthOnly;
            CameraTarget = direct ? RenderTexture.BackBuffer : (_cameraTargets[i] ??= RenderTexture.Create(RenderTextureFormat.RGBA8));
            pipeline.Render(cmd, this);
            if (!direct)
            {
                // 範囲へ重ねる (UV で読むので、画面の大きさの絵が範囲に縮む。投影は範囲の比で作ってある)
                _composite ??= FullscreenShader.Load(ShaderPaths.PostProcess, "PSComposite");
                var (x, y, w, h) = GetCameraViewport(i);
                cmd.SetRenderTarget(RenderTexture.BackBuffer);
                cmd.SetViewport(x, y, w, h);
                cmd.DrawFullscreen(_composite, [CameraTarget], [],
                                   ClearFlags == CameraClearFlags.DepthOnly ? FullscreenBlend.Premultiplied : FullscreenBlend.None);
                cmd.SetRenderTarget(RenderTexture.BackBuffer);   // 範囲を画面全体に戻す
                composited++;
            }
        }
        if (count > 1)
        {
            restoreValues(saved);
            cmd.SetCamera(0);   // この後の線 / プレビュー / UI はメインのカメラで
        }
        CompositedCameras = composited;
        CurrentCamera = 0;
        ClearFlags    = mainClear;
        CameraTarget  = RenderTexture.BackBuffer;
    }

    private RenderPipeline pipelineOf(int index, RenderPath path)
    {
        var pipeline = _cameraPipelines[index];
        var deferred = path == RenderPath.Deferred;
        if (pipeline != null && (pipeline is DeferredRenderPipeline) == deferred) return pipeline;
        pipeline?.Dispose();
        pipeline = deferred ? new DeferredRenderPipeline() : new ForwardRenderPipeline();
        pipeline.Initialize(this);
        _cameraPipelines[index] = pipeline;
        return pipeline;
    }

    // カメラごとの値 (入れ替える物だけ)
    private struct CameraValues
    {
        public AntiAliasing AntiAliasing;
        public Vector3 Background;
        public float Exposure, Gamma;
        public Tonemapper Tonemapper;
        public bool Sky;
        public SsaoValues Ssao;
        public FxaaValues Fxaa;
    }

    private CameraValues captureValues() => new()
    {
        AntiAliasing = AntiAliasing, Background = BackgroundColor, Exposure = Exposure, Gamma = Gamma, Tonemapper = Tonemapper, Sky = Skybox.Enabled,
        Ssao = new SsaoValues { Enabled = Ssao.Enabled, Radius = Ssao.Radius, Intensity = Ssao.Intensity, Bias = Ssao.Bias, SampleCount = Ssao.SampleCount, ResolutionScale = Ssao.ResolutionScale },
        Fxaa = new FxaaValues { Subpixel = Fxaa.Subpixel, EdgeThreshold = Fxaa.EdgeThreshold, EdgeThresholdMin = Fxaa.EdgeThresholdMin },
    };

    private void applyValues(in Camera c)
    {
        AntiAliasing    = c.AntiAliasing;
        BackgroundColor = c.BackgroundColor;
        Exposure        = c.Exposure;
        Gamma           = c.Gamma;
        Tonemapper      = c.Tonemapper;
        Skybox.Enabled  = c.ClearFlags == CameraClearFlags.Skybox;
        RenderSettingsSystem.ApplySsao(Ssao, c.Ssao, default, force: true);
        RenderSettingsSystem.ApplyFxaa(Fxaa, c.Fxaa, default, force: true);
    }

    private void restoreValues(in CameraValues v)
    {
        AntiAliasing    = v.AntiAliasing;
        BackgroundColor = v.Background;
        Exposure        = v.Exposure;
        Gamma           = v.Gamma;
        Tonemapper      = v.Tonemapper;
        Skybox.Enabled  = v.Sky;
        RenderSettingsSystem.ApplySsao(Ssao, v.Ssao, default, force: true);
        RenderSettingsSystem.ApplyFxaa(Fxaa, v.Fxaa, default, force: true);
    }

    private readonly List<RenderTexture> _retiredTextures = new();

    // このフレームの UI が読んでいるかもしれないレンダーターゲットを、描いた後で消す
    internal void RetireTexture(RenderTexture? texture)
    {
        if (texture != null) _retiredTextures.Add(texture);
    }

    // ---------- プレビューの窓 (AN5) ----------
    private readonly List<PreviewScene> _previews = new();

    /// <summary>今あるプレビューの窓の世界</summary>
    public IReadOnlyList<PreviewScene> Previews => _previews;

    /// <summary>プレビューの窓の世界を作る (視点の番号は 1〜15 の空いている物。全部使っていれば null)。使い終わったら Dispose</summary>
    internal PreviewScene? CreatePreview(Engine engine)
    {
        for (int view = 1; view < NativeMethods.MaxPreviewViews; view++)
        {
            if (_previews.Any(p => p.View == view)) continue;
            var preview = new PreviewScene(engine, view);
            _previews.Add(preview);
            return preview;
        }
        return null;
    }

    internal void RemovePreview(PreviewScene preview) => _previews.Remove(preview);

    internal void UpdatePreviews(float deltaTime)
    {
        for (int i = 0; i < _previews.Count; i++) _previews[i].Update(deltaTime);
    }

    internal void Shutdown()
    {
        _pipeline?.Dispose();
        _pipeline = null;
        for (int i = 0; i < _cameraPipelines.Length; i++) { _cameraPipelines[i]?.Dispose(); _cameraPipelines[i] = null; }
        for (int i = 0; i < _cameraTargets.Length; i++)   { _cameraTargets[i]?.Dispose();   _cameraTargets[i] = null; }
    }

    // ExecuteIndirect で描くか (false なら描画ごとに DrawIndexedInstanced)
    public bool IndirectDraw
    {
        get => _indirectDraw;
        set { _indirectDraw = value; NativeMethods.Renderer_SetIndirectDraw(value); }
    }

    // 同じメッシュを 1 回の描画にまとめるか
    public bool Instancing
    {
        get => _instancing;
        set { _instancing = value; NativeMethods.Renderer_SetInstancing(value); }
    }

    // GPU カリング (視錐台。ExecuteIndirect の時だけ効く)
    public bool GpuCulling
    {
        get => _gpuCulling;
        set { _gpuCulling = value; NativeMethods.Renderer_SetGpuCulling(value); }
    }

    // オクルージョンカリング (2 段階。GPU カリングの時だけ効く)
    public bool OcclusionCulling
    {
        get => _occlusion;
        set { _occlusion = value; NativeMethods.Renderer_SetOcclusionCulling(value); }
    }
}

// ==========================================================================
// 音の設定 (C++ は Audio.dll)
// ==========================================================================
public sealed class AudioSettings
{
    private readonly bool _loaded;
    private float _masterVolume = 1.0f;

    internal AudioSettings(bool loaded, bool available)
    {
        _loaded   = loaded;
        Available = available;
    }

    // 音を鳴らせるか (Audio.dll が無い / 音の出る機器が無い時は false。その時 AudioSource は何もしない)
    public bool Available { get; }

    // 鳴っている AudioSource の数 (v1.6。最後の更新の時。確認 / プロファイラー用)
    public int PlayingCount => System?.PlayingCount ?? 0;
    internal AudioSystem? System;

    // 全体の音量 (1 = そのまま)
    public float MasterVolume
    {
        get => _masterVolume;
        set
        {
            _masterVolume = Math.Max(0.0f, value);
            if (_loaded) AudioNativeMethods.Audio_SetMasterVolume(_masterVolume);
        }
    }
}

// ==========================================================================
// エンジン本体 : ウィンドウ・C++ の初期化・メインループ・ECS の World と System
//
//   using var engine = new Engine(new EngineSettings { Title = "Sample" });
//   var model = engine.Assets.LoadModel("Assets/Models/obj/box.obj");
//   var e = engine.World.CreateEntity();
//   engine.World.Add(e, new Transform());
//   engine.World.Add(e, new MeshRenderer(model));
//   engine.AddSystem(new MySystem());
//   engine.Run();
//
//   毎フレームの順番 :
//     ウィンドウのメッセージ → Time → ゲームパッド → 登録した System (登録順) → Transform の行列 → カメラ → 描画オブジェクトへ反映
//     → ライト → GPU パーティクルのエミッター → 音 (AudioSource) → 描画パイプライン (Graphics.Pipeline) が命令を積む → C++ で描画 → 入力のフレーム終わりの処理
//   以前 (GameEngine_console の RHI.cs) は、ウィンドウ・P/Invoke・テスト用の関数が 1 つのクラスに入っていた
// ==========================================================================
public sealed class Engine : IDisposable
{
    private readonly Form _window;
    private readonly List<ISystem> _systems = new();
    private readonly List<IFixedSystem> _fixedSystems = new();   // 固定の刻みで呼ぶ (物理の前。P1)
    private readonly List<ILateSystem>  _lateSystems  = new();   // Update と物理の後に呼ぶ (v1.4。Unity の LateUpdate)
    private readonly TransformSystem  _transformSystem = new();
    private readonly CameraSystem     _cameraSystem    = new();
    private readonly RenderSyncSystem _renderSyncSystem;
    private readonly LightSystem      _lightSystem     = new();
    private readonly RenderSettingsSystem _renderSettingsSystem = new();   // カメラ / Project Settings / Volume → Graphics (ED6 / v1.5)
    private readonly ParticleSystem   _particleSystem;
    private readonly AnimationSystem  _animationSystem = new();   // AN3
    private readonly SkinningSystem   _skinningSystem = new();    // AN4

    /// <summary>確認用 (AN4) : 曲げる物の GPU で曲げた頂点を読み戻す。Request の 2 フレーム以上後に Read (GPU を待つ。戻り値 = 位置と法線、まだなら空)</summary>
    public void RequestSkinReadback(Entity entity)
    {
        if (World.IsAlive(entity) && World.Has<MeshRenderer>(entity) && World.Get<MeshRenderer>(entity).IsCreated)
            NativeMethods.Renderer_RequestSkinReadback(World.Get<MeshRenderer>(entity).Handle);
    }

    public unsafe (Vector3 Position, Vector3 Normal)[] ReadSkinReadback(int maxCount)
    {
        var vertices = new Primitives.NativeVertex[Math.Max(0, maxCount)];
        uint count;
        fixed (Primitives.NativeVertex* v = vertices) count = NativeMethods.Renderer_ReadSkinReadback(v, (uint)vertices.Length);
        return vertices.Take((int)count).Select(v => (v.Position, v.Normal)).ToArray();
    }

    /// <summary>GPU スキニング (AN4) : 曲げる物の数 / 骨の行列の数 / C# の時間 (ms) / 前のフレームで GPU が曲げた物と頂点の数 /
    /// GPU の時間 (ms。数フレーム前の値。曲げなかったフレームは 0)</summary>
    public (int Objects, int Matrices, double Milliseconds, int GpuInstances, int GpuVertices, double GpuMilliseconds) SkinningStats
    {
        get
        {
            uint instances = 0, vertices = 0;
            float gpuMs = 0;
            try { NativeMethods.Renderer_GetSkinStats(out instances, out vertices, out gpuMs); } catch (EntryPointNotFoundException) { }
            return (_skinningSystem.ObjectCount, _skinningSystem.MatrixCount, _skinningSystem.LastMilliseconds, (int)instances, (int)vertices, gpuMs);
        }
    }

    /// <summary>プレビューの窓の世界を作る (AN5。視点の番号 1〜15 の空いている物。全部使っていれば null)。使い終わったら Dispose</summary>
    public PreviewScene? CreatePreview() => Graphics.CreatePreview(this);

    /// <summary>確認用 (AN5) : 描画リストの数 (view 0 = シーン / 1〜 = プレビューの窓)</summary>
    public int DrawItemCount(int view = 0) => (int)NativeMethods.Renderer_GetDrawItemCount((uint)Math.Max(0, view));
    /// <summary>確認用 (AN5) : C++ の生きている描画オブジェクトの数 (シーン + 窓)</summary>
    public int RenderObjectCount => (int)NativeMethods.Renderer_GetRenderObjectCount();

    /// <summary>アニメーション (AN3) : 動かした Animator の数 / このフレームで書いた骨の数 / かかった時間 (ms)</summary>
    public (int Animators, int BonesWritten, double Milliseconds) AnimationStats => (_animationSystem.AnimatorCount, _animationSystem.BonesWritten, _animationSystem.LastMilliseconds);
    private readonly AudioSystem      _audioSystem;
    private readonly bool             _audioLoaded;
    private readonly CollisionSystem  _collisionSystem;
    private readonly CollisionCallbackSystem _collisionCallbacks = new();   // v1.2 : Behaviour<T> の OnCollisionEnter / Stay / Exit
    private readonly bool             _physicsLoaded;
    private readonly RenderCommandBuffer _commands     = new();
    private bool _closed;
    private bool _disposed;
    private double _updateTotalMs;
    private long   _updateFrames;

    // C++ からログを受け取る delegate (GC に回収されないよう持ち続ける)
    private static NativeMethods.LogCallback?  _logCallback;
    private static NativeMethods.WLogCallback? _wLogCallback;

    public World            World    { get; } = new();
    public AssetManager     Assets   { get; } = new();
    public GraphicsSettings Graphics { get; } = new();
    public AudioSettings    Audio    { get; }
    public PhysicsSettings  Physics  { get; }

    // エンジンが毎フレーム C# 側でかけた時間 (System の更新 + C++ への反映。描画は含まない)
    public double UpdateMilliseconds { get; private set; }

    public Engine(EngineSettings settings)
    {
        // 相対パス (モデル / シェーダー / キャッシュ) は実行ファイルのフォルダ (か WorkingDirectory) から辿る
        // (以前は必ず実行ファイルのフォルダだった。エディターが先に変えた作業フォルダも上書きされていた)
        Environment.CurrentDirectory = settings.WorkingDirectory ?? AppContext.BaseDirectory;

        // Width / Height は枠とタイトルバーを含んだサイズなので、描画領域 (ClientSize) で指定する (2-4)
        _window = new Form
        {
            ClientSize = new Size(settings.Width, settings.Height),
            Text       = settings.Title,
        };
        if (EngineInfo.ExeIcon() is { } icon) _window.Icon = icon;   // exe のアイコンを窓にも (以前は WinForms の既定のアイコン)
        _window.FormClosed += (_, _) => _closed = true;
        _window.Show();

        _logCallback  = message => Console.WriteLine(message);
        _wLogCallback = message => Console.WriteLine(message);
        NativeMethods.Renderer_SetLogCallback(_logCallback, _wLogCallback);

        var window = new NativeMethods.WindowDesc
        {
            HWnd        = _window.Handle,
            Width       = (uint)_window.ClientSize.Width,
            Height      = (uint)_window.ClientSize.Height,
            EnableVSync = settings.VSync ? 1u : 0u,
        };
        NativeMethods.Renderer_Initialize(out var result, ref window);
        Console.WriteLine($"[Engine] Device {result.Device} / CommandContext {result.CommandContext} / Buffer {result.Buffer} / Texture {result.Texture} / Pipeline {result.Pipeline} (1 = OK)");
        if (!result.AllSucceeded)
        {
            NativeMethods.Renderer_CleanUp();
            _window.Dispose();
            throw new InvalidOperationException("Failed to initialize the renderer. See the log above.");
        }

        // 描画領域のサイズが変わったらスワップチェーンを作り直す (最小化中は 0 になるので送らない)
        _window.ClientSizeChanged += (_, _) =>
        {
            if (_window.WindowState == FormWindowState.Minimized) return;
            if (_window.ClientSize.Width <= 0 || _window.ClientSize.Height <= 0) return;
            NativeMethods.Renderer_Resize((uint)_window.ClientSize.Width, (uint)_window.ClientSize.Height);
        };

        // 音 (Audio.dll)。無い / 音の出る機器が無い時も、描画はそのまま動かす (音だけ鳴らない)
        var audioAvailable = false;
        try
        {
            audioAvailable = AudioNativeMethods.Audio_Initialize();
            _audioLoaded   = true;
        }
        catch (DllNotFoundException)
        {
            Console.WriteLine("[Engine] Audio.dll was not found. Sound is disabled.");
        }
        Console.WriteLine($"[Engine] Audio {(audioAvailable ? "OK" : "unavailable")}");
        Audio = new AudioSettings(_audioLoaded, audioAvailable);
        Assets.AudioLoaded = _audioLoaded;
        _audioSystem = new AudioSystem(World, _audioLoaded);
        Audio.System = _audioSystem;

        // 衝突判定 (Physics.dll)。無い時も描画はそのまま動かす (衝突判定だけしない)
        try
        {
            _physicsLoaded = PhysicsNativeMethods.Physics_Initialize();
        }
        catch (DllNotFoundException)
        {
            Console.WriteLine("[Engine] Physics.dll was not found. Collision detection is disabled.");
        }
        Physics = new PhysicsSettings(_physicsLoaded);
        Assets.PhysicsLoaded = _physicsLoaded;
        _collisionSystem = new CollisionSystem(World, Physics, _physicsLoaded);

        Input.Attach(_window);
        // ゲームパッド (Core.dll。XInput + DirectInput)。パッドが無くてもそのまま動く (何も繋がっていない扱い)
        Gamepad.Initialize();
        _renderSyncSystem = new RenderSyncSystem(World, _transformSystem);
        GameEngine.UI.Manager = new UIManager(this);   // v1.7 : UI (Widget)
        _particleSystem   = new ParticleSystem(World, Graphics.Particles);

        // カメラを動かす System は既定で入れておく (コンポーネントを付けた物だけが動く)
        AddSystem(new FreeCameraSystem());
        AddSystem(new OrbitCameraSystem());
        InputActions.Set(ProjectSettings.Current.InputActions, ProjectSettings.Current.InputAxes);   // 入力のアクションの既定 (ED6。ProjectSettings.Apply で置き換わる)

        // v1.8 : クラッシュレポートに書くエンジンの様子 (落ちた時だけ呼ばれる)
        CrashReporter.StateProvider = () =>
            $"frame {Time.FrameCount}, total {Time.TotalTime:F1} s, play {Time.PlayTime:F1} s, dt {Time.DeltaTime * 1000:F2} ms, playing {IsPlaying}, " +
            $"background {IsInBackground}, entities {World.EntityCount}, systems {_systems.Count}, client {ClientSize.Width}x{ClientSize.Height}";
        CrashReporter.SetInfo("Playing", "False");
    }

    // ---------- エディター (ED3) ----------

    /// <summary>
    /// 再生中か (既定 true)。false (エディターの編集中) の間は、登録した System / FixedUpdate / 物理の時間 / 音を止める
    /// (描画 / 衝突判定の形 / パーティクルの見た目は動く)
    /// </summary>
    public bool IsPlaying { get; set; } = true;

    /// <summary>
    /// 再生を始める (ED8 後半)。再生の時間 (Time.PlayTime / FixedTime) と物理の刻みの余りを 0 に戻す
    /// エディターは再生ボタンで、Player は最初のシーンを読んだ後に呼ぶ (一時停止からの再開では呼ばない)
    /// 以前は時間が起動から数えられ、エディターで再生した時と書き出したゲームで、時間で動く物の位置が違った
    /// 物理の世界も作った直後と同じにして、体を次のフレームで作り直す (前に開いたシーンで体の番号が変わり、動きがずれたため)
    /// </summary>
    public void BeginPlay()
    {
        Time.ResetPlayTime();
        _collisionSystem.ResetForPlay();
        _collisionCallbacks.Reset(Physics);
        IsPlaying = true;
        CrashReporter.SetInfo("Playing", "True");   // v1.8 : C++ で落ちた時のレポートにも書く
    }

    /// <summary>
    /// 再生を終える (v1.6)。鳴っている音を全部止めて、ボイスを削除する (エディターの Stop が、シーンを作り直す前に呼ぶ)
    /// 以前は Stop でシーンを消しても、音の削除が再生中の更新でしか行われず、鳴ったままになった
    /// </summary>
    public void EndPlay()
    {
        IsPlaying = false;
        CrashReporter.SetInfo("Playing", "False");   // v1.8
        _audioSystem.StopAll();
        GameEngine.UI.Manager?.Clear();   // v1.7 : 画面の UI も全部外す
    }

    /// <summary>
    /// ゲームのコード (System / Behaviour / FixedUpdate / LateUpdate / UI のイベント) が投げた例外を受け取る (v1.7)
    /// null (既定。Player) なら今まで通り投げる (ゲームが止まり、理由をログと箱で出す)。エディターは受け取って Console に出し、再生を一時停止する
    /// 例外の起きたフレームは、その System の残りの物は呼ばれない (次のフレームはまた呼ぶ)
    /// </summary>
    public Action<Exception>? GameCodeException { get; set; }

    /// <summary>System と物理の後、カメラと描画への反映の前に毎フレーム呼ばれる (エディターの GUI はここで作る)</summary>
    public event Action? LateUpdate;

    /// <summary>描画領域 (クライアント領域) の大きさ (ピクセル)</summary>
    public (int Width, int Height) ClientSize => (_window.ClientSize.Width, _window.ClientSize.Height);

    /// <summary>ウィンドウ (エディターがファイルのダイアログなどの親にする)</summary>
    public Form Window => _window;

    // 毎フレーム呼ばれる System を足す (登録した順に呼ばれる)。IFixedSystem も実装していれば FixedUpdate も呼ぶ
    /// <summary>今重なっている組の数 (Behaviour の衝突のメソッドの表。確認用。v1.2)</summary>
    public int CollisionPairCount => _collisionCallbacks.PairCount;

    public void AddSystem(ISystem system)
    {
        _systems.Add(system);
        if (system is IFixedSystem fixedSystem) _fixedSystems.Add(fixedSystem);
        if (system is ILateSystem lateSystem) _lateSystems.Add(lateSystem);   // v1.4
    }

    /// <summary>
    /// アセンブリの [GameSystem] の付いた System を作って登録する (ED7)。戻り値 = 作った物 (RemoveSystems で外す)
    /// コンストラクターは (Engine) か ()。作れなかった物は Console に出して飛ばす
    /// </summary>
    public List<object> AddGameSystems(System.Reflection.Assembly assembly)
    {
        var created = new List<object>();
        var types = assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && t.IsDefined(typeof(GameSystemAttribute), false) &&
                        (typeof(ISystem).IsAssignableFrom(t) || typeof(IFixedSystem).IsAssignableFrom(t)))
            .OrderBy(t => t.GetCustomAttributes(typeof(GameSystemAttribute), false).Cast<GameSystemAttribute>().First().Order)
            .ThenBy(t => t.FullName, StringComparer.Ordinal);
        foreach (var type in types)
        {
            try
            {
                var system = type.GetConstructor(new[] { typeof(Engine) }) is { } withEngine ? withEngine.Invoke(new object[] { this })
                           : Activator.CreateInstance(type)!;
                if (system is ISystem s) AddSystem(s);
                else AddFixedSystem((IFixedSystem)system);
                created.Add(system);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Engine] Cannot create {type.FullName} : {(ex.InnerException ?? ex).Message}");
            }
        }
        return created;
    }

    // 登録した System を順に回す (ED7 : 別のメソッドにした。Run の中に書くと、Debug のビルドでは
    //  foreach の変数 (最後のゲームの System) が Run が終わるまで残り、読み直した古いゲームの DLL が解放されなかった)
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void updateSystems()
    {
        foreach (var system in _systems) system.Update(World);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    private void updateLateSystems()
    {
        foreach (var system in _lateSystems) system.LateUpdate(World);
    }

    /// <summary>System を外す (ED7 : ゲームの DLL を読み直す前)</summary>
    public void RemoveSystems(IEnumerable<object> systems)
    {
        foreach (var system in systems)
        {
            if (system is ISystem s) _systems.Remove(s);
            if (system is IFixedSystem f) _fixedSystems.Remove(f);
            if (system is ILateSystem l) _lateSystems.Remove(l);
        }
    }

    // 固定の刻み (Time.FixedDeltaTime) で呼ばれる System を足す (物理の前。剛体に力をかけるのはここ)
    public void AddFixedSystem(IFixedSystem system)
    {
        if (!_fixedSystems.Contains(system)) _fixedSystems.Add(system);
    }

    // ---------- バックグラウンド (v1.8) ----------

    /// <summary>
    /// 後ろにある時 (他のアプリが前にある時) も動かし続けるか (既定 false。Unity の Run In Background と同じ)
    /// false なら、最小化中 / 後ろにある間は Update・物理・アニメーション・UI・描画・音の処理を全部止めて、CPU / GPU を休ませる
    /// 同じプロセスの窓 (ファイルのダイアログ / 箱) が前にある時は止めない。自動の確認 (テスト) では true にする
    /// true でも最小化中は止める (以前と同じ。描いても見えないため)
    /// </summary>
    public bool RunInBackground { get; set; }

    /// <summary>今、後ろにあって止めているか</summary>
    public bool IsInBackground => _backgroundSince != 0;

    /// <summary>止めた / 再開した時 (引数 = 止めたか)。エディターが表示を変えたりする</summary>
    public event Action<bool>? BackgroundChanged;

    private long _backgroundSince;      // 止めた時の Stopwatch の値 (0 = 動いている)
    private bool _audioSuspended;
    private bool _audioCanSuspend = true;
    private readonly uint _processId = (uint)Environment.ProcessId;

    // 前の窓がこのプロセスの物か
    private bool isForeground()
    {
        var foreground = Win32NativeMethods.GetForegroundWindow();
        if (foreground == IntPtr.Zero) return false;
        Win32NativeMethods.GetWindowThreadProcessId(foreground, out var processId);
        return processId == _processId;
    }

    // 止める必要があるか : 最小化中は常に。後ろにある時は RunInBackground = false の時だけ
    private bool shouldPause()
    {
        if (_window.WindowState == FormWindowState.Minimized) return true;
        return !RunInBackground && !isForeground();
    }

    private void enterBackground()
    {
        _backgroundSince = Stopwatch.GetTimestamp();
        // 以前は最小化中も音の処理とパッドの振動が続いた。RunInBackground = true の最小化 (以前と同じ動き) だけは音を止めない
        if (!RunInBackground || _window.WindowState != FormWindowState.Minimized)
        {
            if (_audioLoaded && _audioCanSuspend)
            {
                try { AudioNativeMethods.Audio_Suspend(); _audioSuspended = true; }
                catch (EntryPointNotFoundException) { _audioCanSuspend = false; }   // 古い Audio.dll (v1.7 まで) : 音は止めない
            }
            for (int pad = 0; pad < GamepadNativeMethods.MaxPads; pad++) Gamepad.StopVibration(pad);
            Gamepad.Update();   // 振動を止めるのは次の Controller_Update で送られるため
        }
        Console.WriteLine($"[Engine] Paused ({(_window.WindowState == FormWindowState.Minimized ? "minimized" : "in background")})");
        BackgroundChanged?.Invoke(true);
    }

    private void leaveBackground()
    {
        var seconds = Stopwatch.GetElapsedTime(_backgroundSince).TotalSeconds;
        _backgroundSince = 0;
        if (_audioSuspended) { AudioNativeMethods.Audio_Resume(); _audioSuspended = false; }
        Time.ResetFrameClock();   // 止めていた間を DeltaTime に入れない (以前は 0.1 秒で切った分、1 フレームだけ大きく進んだ)
        Console.WriteLine($"[Engine] Resumed after {seconds:F1} s");
        BackgroundChanged?.Invoke(false);
    }

    // ウィンドウが閉じられるまで回す
    public void Run()
    {
        while (!_closed)
        {
            Application.DoEvents();
            if (_closed) break;

            // v1.8 : 最小化中 / 後ろにある間は何もしない (電力と CPU / GPU を使わない)
            //   以前は最小化中だけ止め、Thread.Sleep(16) で 1 秒に 60 回起きていた。後ろにある時は全部動いていた
            //   止めている間はメッセージが来るまで眠る (前に戻った / 窓の操作はすぐ分かる。100 ms ごとにも見直す)
            if (shouldPause())
            {
                if (!IsInBackground) enterBackground();
                Win32NativeMethods.MsgWaitForMultipleObjectsEx(0, IntPtr.Zero, 100, Win32NativeMethods.QS_ALLINPUT, Win32NativeMethods.MWMO_INPUTAVAILABLE);
                continue;
            }
            if (IsInBackground) leaveBackground();

            Time.Tick();
            var start = Stopwatch.GetTimestamp();

            // ゲームパッドはフレームの頭で 1 回、全パッド分をまとめて読む (System の中ではこの値を見る)
            Gamepad.Update();

            if (IsPlaying) Time.AdvancePlayTime();   // ED8 後半
            // v1.7 : ゲームのコード (System / Behaviour / UI のイベント) の例外は、GameCodeException があればそこへ渡して続ける (エディター)
            //   以前はどこでも受け取らず、スクリプトの例外 (無い Widget を読んだ等) でエディターごと落ちていた
            try
            {
                if (IsPlaying) _collisionCallbacks.Dispatch(World, Physics, _systems);   // v1.2 : 前の物理の刻みの衝突 → Behaviour の OnCollision* (Update の前。Unity と同じ順)
                if (IsPlaying) updateSystems();   // 編集中 (ED3) は止める
            }
            catch (Exception ex) when (GameCodeException != null) { GameCodeException(ex); }
            _animationSystem.Update(World, IsPlaying, Time.DeltaTime);   // AN3 : 骨のエンティティに姿勢を書く (編集中は時間を進めず、その時間の姿勢)
            _transformSystem.Update(World);
            try
            {
                _collisionSystem.Update(World, _fixedSystems, IsPlaying);   // Transform.Changed を見るので RenderSyncSystem (Changed を下ろす) より前。剛体はここで動く
            }
            catch (Exception ex) when (GameCodeException != null) { GameCodeException(ex); }   // v1.7 : FixedUpdate の例外
            _transformSystem.ChangedCount += _collisionSystem.TransformsWritten;
            // 剛体の子 : 剛体が位置を書いた後に、子のワールドの行列を計算し直す (ED2。親子が無ければ何もしない)
            if (_collisionSystem.TransformsWritten > 0 && _transformSystem.HasHierarchy) _transformSystem.UpdateAgain(World);
            _animationSystem.DrawBones(World);   // AN3 : ShowBones の骨を線で (行列ができた後)
            // v1.4 : ゲームの LateUpdate (Update / アニメーション / 物理の後)。変えた Transform の行列を計算し直して、このフレームの描画に間に合わせる
            if (IsPlaying && _lateSystems.Count > 0)
            {
                try { updateLateSystems(); }
                catch (Exception ex) when (GameCodeException != null) { GameCodeException(ex); }   // v1.7
                _transformSystem.Update(World);
            }
            // v1.7 : UI (シーンの UIWidget の反映 / 並べる / クリック / 描く)。エディターの GUI より前 (シーンビューの絵の上に描くため)
            //   クリックなどのイベントはゲームのコードを呼ぶので、例外は上と同じく受け取る
            try { GameEngine.UI.Manager!.Update(World, IsPlaying, Time.DeltaTime); }
            catch (Exception ex) when (GameCodeException != null) { GameCodeException(ex); }
            LateUpdate?.Invoke();   // エディターの GUI (ED3)
            Graphics.ScreenSize = Graphics.RenderSize is { Width: > 0 } size ? size : ClientSize;   // v1.5 : カメラの範囲の比 / 描く範囲のピクセル
            _cameraSystem.Update(World, Graphics.CameraOverride, Graphics.ScreenSize);   // v1.5 : 全部のカメラ (Depth の順)
            _renderSettingsSystem.Update(World, Graphics, _cameraSystem);   // カメラの描き方 / Project Settings + Volume (ED6 / v1.5)
            _renderSyncSystem.Update(World);
            _skinningSystem.Update(World, Assets);   // AN4 : 曲げる物の骨の行列を C++ へ (描画オブジェクトができた後)
            Graphics.UpdatePreviews(Time.DeltaTime);   // AN5 : プレビューの窓の世界 (骨 / 行列 / 描画オブジェクト / 曲げる)
            _lightSystem.Update(World);
            _particleSystem.Update(World);   // GPU パーティクルのエミッター (出す数) を C++ へ
            if (IsPlaying) _audioSystem.Update(World, _cameraSystem.Position);   // 編集中は鳴らさない (ED3)。聞く位置が無ければカメラ (ED6)
            else           _audioSystem.UpdateStopped();   // v1.6 : 外された AudioSource のボイスは編集中も消す (以前は Stop 後も鳴り続けた)

            // 描画パイプラインが 1 フレーム分の命令を積む (以前は C++ が決まった描き方で描いていた)
            Graphics.ShadowLightIndex = _lightSystem.ShadowLightIndex;
            Graphics.BeginFrame();

            UpdateMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (Time.TotalTime >= 1.0) { _updateTotalMs += UpdateMilliseconds; _updateFrames++; }   // 起動直後は除く
            NativeMethods.Renderer_ReportProfilerValue("C# update (ms)", UpdateMilliseconds);
            NativeMethods.Renderer_ReportProfilerValue("C# transforms sent", _renderSyncSystem.TransformsSentLastFrame);
            NativeMethods.Renderer_ReportProfilerValue("Lights", _lightSystem.LightCount);
            NativeMethods.Renderer_ReportProfilerValue("Sounds playing", _audioSystem.PlayingCount);
            if (GameEngine.UI.Manager!.Widgets.Count > 0)
            {
                NativeMethods.Renderer_ReportProfilerValue("UI widgets", GameEngine.UI.Manager.Widgets.Count);
                NativeMethods.Renderer_ReportProfilerValue("UI quads", GameEngine.UI.Manager.LastQuadCount);
                NativeMethods.Renderer_ReportProfilerValue("UI update (ms)", GameEngine.UI.Manager.LastMilliseconds);
            }
            if (_physicsLoaded)
            {
                NativeMethods.Renderer_ReportProfilerValue("Physics step (ms)", Physics.StepMs);
                NativeMethods.Renderer_ReportProfilerValue("Physics broadphase (ms)", Physics.BroadphaseMs);
                NativeMethods.Renderer_ReportProfilerValue("Physics narrowphase (ms)", Physics.NarrowphaseMs);
                NativeMethods.Renderer_ReportProfilerValue("Physics bodies", Physics.Bodies);
                NativeMethods.Renderer_ReportProfilerValue("Physics moved", Physics.MovedBodies);
                NativeMethods.Renderer_ReportProfilerValue("Physics candidates", Physics.Candidates);
                NativeMethods.Renderer_ReportProfilerValue("Physics overlaps", Physics.Overlaps);
                if (Physics.RigidBodies > 0 || Physics.Joints > 0)
                {
                    NativeMethods.Renderer_ReportProfilerValue("Physics solver (ms)", Physics.SolverMs);
                    NativeMethods.Renderer_ReportProfilerValue("Physics rigid bodies", Physics.RigidBodies);
                    NativeMethods.Renderer_ReportProfilerValue("Physics awake bodies", Physics.AwakeBodies);
                    NativeMethods.Renderer_ReportProfilerValue("Physics islands", Physics.Islands);
                    NativeMethods.Renderer_ReportProfilerValue("Physics contacts solved", Physics.ContactConstraints);
                    NativeMethods.Renderer_ReportProfilerValue("Physics joints", Physics.Joints);
                    NativeMethods.Renderer_ReportProfilerValue("Physics fixed steps", Physics.FixedSteps);
                }
            }
            DebugDraw.Flush();   // 確認用の線 (このフレームの描画の最後に描かれる)

            Graphics.RenderFrame(_commands);
            NativeMethods.Renderer_ReportProfilerValue("Render commands", _commands.Count);
            Input.EndFrame();
        }
    }

    // ---------- シーンの保存 / 読み込み (JSON。ED1) ----------

    /// <summary>今の World の全部のエンティティを JSON のシーンに保存する</summary>
    public void SaveScene(string path) => SceneSerializer.SaveFile(World, path);

    /// <summary>
    /// シーンを読み込む。additive = false なら今のエンティティを全部消してから読む
    /// 読めなかった物 (知らない型 / 無いアセット) はコンソールに出して飛ばす。戻り値は作ったエンティティ
    /// </summary>
    public List<Entity> LoadScene(string path, bool additive = false)
    {
        var start = Stopwatch.GetTimestamp();
        CrashReporter.SetInfo(additive ? "Scene (additive)" : "Scene", path);   // v1.8 : どのシーンで落ちたか
        if (!additive)
        {
            World.Clear();
            GameEngine.UI.Manager?.Clear();   // v1.7 : シーンを替えると画面の UI は外れる (UE のレベルの切り替えと同じ)
        }
        var warnings = new List<string>();
        var created = SceneSerializer.LoadFile(World, path, Assets, warnings);
        foreach (var warning in warnings) Console.WriteLine($"[Scene] {warning}");
        Console.WriteLine($"[Scene] Loaded {path} : {created.Count} entities{(warnings.Count > 0 ? $", {warnings.Count} warnings" : "")}, " +
                          $"{Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms");
        return created;
    }

    // ウィンドウを閉じる (Run が終わる)
    public void Quit()
    {
        if (!_window.IsDisposed) _window.Close();
    }

    // GPU の処理 (描画 + 転送) が全て終わるまで待つ (読み込み時間の計測など)
    public void WaitForGpu() => NativeMethods.Renderer_WaitForGpu();

    // プロファイラー (F1) に任意の値を表示する (同じ名前なら上書き)
    public void ReportProfilerValue(string name, double value) => NativeMethods.Renderer_ReportProfilerValue(name, value);

    public void Dispose()
    {
        // 2 回呼ばれても C++ 側を 2 回解放しないようにする
        if (_disposed) return;
        _disposed = true;

        if (_updateFrames > 0)
        {
            Console.WriteLine($"[Engine] C# update average {_updateTotalMs / _updateFrames:F3} ms ({_updateFrames} frames, entities {World.EntityCount})");
        }
        Gamepad.Shutdown();                 // 振動を止めてから解放される
        if (_audioLoaded) AudioNativeMethods.Audio_Shutdown();   // ボイス → XAudio2 → PCM の順に解放される
        if (_physicsLoaded) PhysicsNativeMethods.Physics_Shutdown();
        Graphics.Shutdown();                // レンダーターゲットを先に返す
        NativeMethods.Renderer_CleanUp();   // GPU の完了を待ってから解放される
        if (!_window.IsDisposed) _window.Dispose();
    }
}
