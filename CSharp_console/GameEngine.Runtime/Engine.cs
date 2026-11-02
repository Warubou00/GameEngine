using System.Diagnostics;
using GameEngine.Native;

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
public sealed class GraphicsSettings
{
    private bool _indirectDraw = true;
    private bool _instancing   = true;
    private bool _gpuCulling   = true;
    private bool _occlusion    = true;

    internal GraphicsSettings()
    {
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
//     ウィンドウのメッセージ → Time → 登録した System (登録順) → Transform の行列 → カメラ → 描画オブジェクトへ反映
//     → C++ で描画 → 入力のフレーム終わりの処理
//   以前 (GameEngine_console の RHI.cs) は、ウィンドウ・P/Invoke・テスト用の関数が 1 つのクラスに入っていた
// ==========================================================================
public sealed class Engine : IDisposable
{
    private readonly Form _window;
    private readonly List<ISystem> _systems = new();
    private readonly TransformSystem  _transformSystem = new();
    private readonly CameraSystem     _cameraSystem    = new();
    private readonly RenderSyncSystem _renderSyncSystem;
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

        Input.Attach(_window);
        _renderSyncSystem = new RenderSyncSystem(World, _transformSystem);

        // カメラを動かす System は既定で入れておく (コンポーネントを付けた物だけが動く)
        AddSystem(new FreeCameraSystem());
        AddSystem(new OrbitCameraSystem());
    }

    // 毎フレーム呼ばれる System を足す (登録した順に呼ばれる)
    public void AddSystem(ISystem system) => _systems.Add(system);

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

            foreach (var system in _systems) system.Update(World);
            _transformSystem.Update(World);
            _cameraSystem.Update(World);
            _renderSyncSystem.Update(World);

            UpdateMilliseconds = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
            if (Time.TotalTime >= 1.0) { _updateTotalMs += UpdateMilliseconds; _updateFrames++; }   // 起動直後は除く
            NativeMethods.Renderer_ReportProfilerValue("C# update (ms)", UpdateMilliseconds);
            NativeMethods.Renderer_ReportProfilerValue("C# transforms sent", _renderSyncSystem.TransformsSentLastFrame);

            NativeMethods.Renderer_Render();
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
        NativeMethods.Renderer_CleanUp();   // GPU の完了を待ってから解放される
        if (!_window.IsDisposed) _window.Dispose();
    }
}
