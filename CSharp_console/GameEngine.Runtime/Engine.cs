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
    public string Title  { get; init; } = "GameEngine";
    public int    Width  { get; init; } = 1280;   // 描画領域 (クライアント領域) の大きさ
    public int    Height { get; init; } = 720;
    public bool   VSync  { get; init; } = false;  // false : 対応環境ではテアリングを許可して上限なしで描く
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

    internal void RenderFrame(RenderCommandBuffer commands)
    {
        commands.Clear();
        _pipeline!.Render(commands, this);
        commands.Submit();
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
//   var model = engine.Assets.LoadModel("../Model/box.obj");
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
    private readonly ParticleSystem   _particleSystem;
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
        // 相対パス (モデル / シェーダー / キャッシュ) は実行ファイルのフォルダから辿る
        Environment.CurrentDirectory = AppContext.BaseDirectory;

        // Width / Height は枠とタイトルバーを含んだサイズなので、描画領域 (ClientSize) で指定する (2-4)
        _window = new Form
        {
            ClientSize = new Size(settings.Width, settings.Height),
            Text       = settings.Title,
        };
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
    }

    // 毎フレーム呼ばれる System を足す (登録した順に呼ばれる)。IFixedSystem も実装していれば FixedUpdate も呼ぶ
    public void AddSystem(ISystem system)
    {
        _systems.Add(system);
        if (system is IFixedSystem fixedSystem) _fixedSystems.Add(fixedSystem);
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

            foreach (var system in _systems) system.Update(World);
            _transformSystem.Update(World);
            _collisionSystem.Update(World, _fixedSystems);   // Transform.Changed を見るので RenderSyncSystem (Changed を下ろす) より前。剛体はここで動く
            _transformSystem.ChangedCount += _collisionSystem.TransformsWritten;
            _cameraSystem.Update(World);
            _renderSyncSystem.Update(World);
            _lightSystem.Update(World);
            _particleSystem.Update(World);   // GPU パーティクルのエミッター (出す数) を C++ へ
            _audioSystem.Update(World);

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
