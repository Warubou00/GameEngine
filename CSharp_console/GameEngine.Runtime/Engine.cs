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

    public float      Exposure        { get; set; } = 1.0f;     // HDR の明るさの倍率
    public Tonemapper Tonemapper      { get; set; } = Tonemapper.ACES;
    public float      Gamma           { get; set; } = 2.2f;
    public Vector3    BackgroundColor { get; set; } = new(0.1f, 0.2f, 0.4f);   // 何も無い所の色 (sRGB。以前のクリア色)

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
        if (_infoDirty || ssaoState != _shownSsaoState || antiAliasing != _shownAntiAliasing || shadowState != _shownShadowState)
        {
            var ssao   = ssaoState switch { 0 => "", 1 => " / SSAO", _ => " / SSAO (AO only)" };
            var shadow = shadowState > 0 ? $" / Shadows {Shadows.Resolution} x{Shadows.CascadeCount}" : "";
            NativeMethods.Renderer_SetProfilerInfo("Render pipeline", $"{_pipeline!.Name} / {_lightingModel}{ssao}{shadow} / {antiAliasing}");
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
        _pipeline!.Render(commands, this);
        foreach (var preview in _previews) preview.Submit(commands);   // AN5 : プレビューの窓 (シーンの後、UI の前)
        commands.Submit();
        // AN5 : 窓を閉じた / 大きさを変えたレンダーターゲットは、このフレームを描いた後で消す
        //       (以前はその場で消していたので、同じフレームの UI がもう消えたテクスチャを読み、GPU が止まった (デバイスの削除))
        foreach (var texture in _retiredTextures) texture.Dispose();
        _retiredTextures.Clear();
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
    private readonly TransformSystem  _transformSystem = new();
    private readonly CameraSystem     _cameraSystem    = new();
    private readonly RenderSyncSystem _renderSyncSystem;
    private readonly LightSystem      _lightSystem     = new();
    private readonly RenderSettingsSystem _renderSettingsSystem = new();   // RenderSettings / Volume → Graphics (ED6)
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
        _particleSystem   = new ParticleSystem(World, Graphics.Particles);

        // カメラを動かす System は既定で入れておく (コンポーネントを付けた物だけが動く)
        AddSystem(new FreeCameraSystem());
        AddSystem(new OrbitCameraSystem());
        InputActions.Set(ProjectSettings.Current.InputActions, ProjectSettings.Current.InputAxes);   // 入力のアクションの既定 (ED6。ProjectSettings.Apply で置き換わる)
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
        IsPlaying = true;
    }

    /// <summary>System と物理の後、カメラと描画への反映の前に毎フレーム呼ばれる (エディターの GUI はここで作る)</summary>
    public event Action? LateUpdate;

    /// <summary>描画領域 (クライアント領域) の大きさ (ピクセル)</summary>
    public (int Width, int Height) ClientSize => (_window.ClientSize.Width, _window.ClientSize.Height);

    /// <summary>ウィンドウ (エディターがファイルのダイアログなどの親にする)</summary>
    public Form Window => _window;

    // 毎フレーム呼ばれる System を足す (登録した順に呼ばれる)。IFixedSystem も実装していれば FixedUpdate も呼ぶ
    public void AddSystem(ISystem system)
    {
        _systems.Add(system);
        if (system is IFixedSystem fixedSystem) _fixedSystems.Add(fixedSystem);
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

    /// <summary>System を外す (ED7 : ゲームの DLL を読み直す前)</summary>
    public void RemoveSystems(IEnumerable<object> systems)
    {
        foreach (var system in systems)
        {
            if (system is ISystem s) _systems.Remove(s);
            if (system is IFixedSystem f) _fixedSystems.Remove(f);
        }
    }

    // 固定の刻み (Time.FixedDeltaTime) で呼ばれる System を足す (物理の前。剛体に力をかけるのはここ)
    public void AddFixedSystem(IFixedSystem system)
    {
        if (!_fixedSystems.Contains(system)) _fixedSystems.Add(system);
    }

    // ウィンドウが閉じられるまで回す
    public void Run()
    {
        while (!_closed)
        {
            Application.DoEvents();
            if (_closed) break;

            // 最小化中は描画しても見えないので、CPU / GPU を休ませる
            if (_window.WindowState == FormWindowState.Minimized)
            {
                Thread.Sleep(16);
                continue;
            }

            Time.Tick();
            var start = Stopwatch.GetTimestamp();

            // ゲームパッドはフレームの頭で 1 回、全パッド分をまとめて読む (System の中ではこの値を見る)
            Gamepad.Update();

            if (IsPlaying) Time.AdvancePlayTime();   // ED8 後半
            if (IsPlaying) updateSystems();   // 編集中 (ED3) は止める
            _animationSystem.Update(World, IsPlaying, Time.DeltaTime);   // AN3 : 骨のエンティティに姿勢を書く (編集中は時間を進めず、その時間の姿勢)
            _transformSystem.Update(World);
            _collisionSystem.Update(World, _fixedSystems, IsPlaying);   // Transform.Changed を見るので RenderSyncSystem (Changed を下ろす) より前。剛体はここで動く
            _transformSystem.ChangedCount += _collisionSystem.TransformsWritten;
            // 剛体の子 : 剛体が位置を書いた後に、子のワールドの行列を計算し直す (ED2。親子が無ければ何もしない)
            if (_collisionSystem.TransformsWritten > 0 && _transformSystem.HasHierarchy) _transformSystem.UpdateAgain(World);
            _animationSystem.DrawBones(World);   // AN3 : ShowBones の骨を線で (行列ができた後)
            LateUpdate?.Invoke();   // エディターの GUI (ED3)
            _cameraSystem.Update(World, Graphics.CameraOverride);
            _renderSettingsSystem.Update(World, Graphics, _cameraSystem.Position);   // カメラの位置で Volume を混ぜる (ED6)
            _renderSyncSystem.Update(World);
            _skinningSystem.Update(World, Assets);   // AN4 : 曲げる物の骨の行列を C++ へ (描画オブジェクトができた後)
            Graphics.UpdatePreviews(Time.DeltaTime);   // AN5 : プレビューの窓の世界 (骨 / 行列 / 描画オブジェクト / 曲げる)
            _lightSystem.Update(World);
            _particleSystem.Update(World);   // GPU パーティクルのエミッター (出す数) を C++ へ
            if (IsPlaying) _audioSystem.Update(World, _cameraSystem.Position);   // 編集中は鳴らさない (ED3)。聞く位置が無ければカメラ (ED6)

            // 描画パイプラインが 1 フレーム分の命令を積む (以前は C++ が決まった描き方で描いていた)
            Graphics.ShadowLightIndex = _lightSystem.ShadowLightIndex;
            Graphics.BeginFrame();

            UpdateMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (Time.TotalTime >= 1.0) { _updateTotalMs += UpdateMilliseconds; _updateFrames++; }   // 起動直後は除く
            NativeMethods.Renderer_ReportProfilerValue("C# update (ms)", UpdateMilliseconds);
            NativeMethods.Renderer_ReportProfilerValue("C# transforms sent", _renderSyncSystem.TransformsSentLastFrame);
            NativeMethods.Renderer_ReportProfilerValue("Lights", _lightSystem.LightCount);
            NativeMethods.Renderer_ReportProfilerValue("Sounds playing", _audioSystem.PlayingCount);
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
        if (!additive) World.Clear();
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
