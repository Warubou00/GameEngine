using System.Diagnostics;
using System.Numerics;
using GameEngine;

namespace GameEngine_console;

// ==========================================================================
// サンプル (エンジンの使い方の例 + 計測用の負荷テスト)
//   以前は RHI.cs の Test_LoadModel / Test_SpawnGrid / Test_InsideView / Test_GroundView で、
//   C++ に直書きされたモデル・格子・カメラを呼んでいた。今はすべてここ (C#) で作る
//
// コマンドライン引数 (計測用)
//   --stress N                  : モデルを N 個、格子状に並べる (描画数を増やす負荷テスト)
//   --view inside|ground|orbit  : カメラの置き方 (--stress と一緒に使う)
//                                 inside : 格子の中から前を見る / ground : 地面すれすれ / orbit : 回り続ける
//   --draw direct|indirect      : 描画の方法 (既定は indirect = ExecuteIndirect)
//   --instancing on|off         : 同じメッシュを 1 回の描画にまとめるか (既定は on)
//   --culling on|off            : GPU カリング (既定は on。ExecuteIndirect の時だけ効く)
//   --occlusion on|off          : オクルージョンカリング (既定は on。GPU カリングの時だけ効く)
//   --animate on|off            : 並べたモデルを全部回し続ける (毎フレーム全部の行列を C++ へ送る負荷テスト)
//   --pipeline forward|deferred : 描画パイプライン (既定は forward)
//   --lighting unlit|lambert|pbr: ライティング (既定は pbr)
//   --ssao on|off|show          : SSAO (既定は on。show は遮蔽だけを白黒で出す)
//   --ssao-scale S / --ssao-samples N : SSAO の解像度の倍率 (既定 0.5) / 点の数 (既定 12)
//   --aa none|fxaa|msaa2|msaa4|msaa8 : アンチエイリアス (既定は fxaa。Deferred の MSAA は FXAA になる)
//   --sound <path>              : 鳴らす wav (既定は ../Sound/bgm.wav。無ければ鳴らさない)
//   --sound-loop on|off         : ループするか (既定は on)
//
// 操作 (orbit 以外) : 右ボタンを押しながらマウスで向き、WASD で移動、E / Q で上下、Shift で速く、F1 でプロファイラー
//                     F2 で Forward / Deferred、F3 で Unlit / Lambert / PBR、F4 で SSAO、F5 で遮蔽だけを表示
//                     F6 でアンチエイリアス (なし → FXAA → MSAA 2x → 4x → 8x)
//                     F7 で音を鳴らす / 止める、F8 で音量
//                     F9 で DirectInput のパッドを探し直す、F10 で DirectInput のパッドのボタンの割り当て
// ゲームパッド      : 右スティックで向き、左スティックで移動、RT / LT で上下、RB で速く、A で振動 (状態はプロファイラーとコンソール)
// ==========================================================================
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        using var engine = new Engine(new EngineSettings
        {
            Title  = "C# DirectX12 Engine",
            Width  = 1280,
            Height = 720,
        });

        // ---------- 引数 ----------
        var options     = ParseArguments(args);
        var stressCount = options.TryGetValue("--stress", out var stress) && int.TryParse(stress, out var n) ? n : 0;
        var view        = options.GetValueOrDefault("--view", "");

        // ---------- モデル (読み込み〜 GPU への転送完了までの時間をプロファイラーに出す) ----------
        // モデルは Model/obj と Model/pmx/<モデル名> に分けて置く (以前は Model の直下)
        var stopwatch = Stopwatch.StartNew();
        var box = engine.Assets.LoadModel("../Model/obj/box.obj");
        engine.WaitForGpu();
        engine.ReportProfilerValue("LoadModel (ms)", stopwatch.Elapsed.TotalMilliseconds);

        var world = engine.World;

        // 原点に 1 個 (動かないので Static)
        var origin = world.CreateEntity();
        world.Add(origin, new Transform());
        world.Add(origin, new MeshRenderer(box));
        world.Add(origin, new Static());

        // 負荷テストでない時は、複数のモデル (OBJ / PMX) を並べた見本のシーン
        if (stressCount == 0)
        {
            Showcase.Spawn(engine);
        }

        // ---------- カメラ ----------
        var camera = world.CreateEntity();
        world.Add(camera, Transform.LookingAt(new Vector3(0, 5, -10), Vector3.Zero));
        world.Add(camera, new Camera());

        var animate = options.GetValueOrDefault("--animate", "off") == "on";
        if (animate || stressCount == 0) engine.AddSystem(new SpinSystem());   // 見本のシーンのキャラクターも回す

        if (stressCount > 0)
        {
            var extent = StressTest.SpawnGrid(world, box, stressCount, spacing: 3.0f, animate);
            ref var cameraTransform = ref world.Get<Transform>(camera);
            cameraTransform = StressTest.CameraFor(view, extent);
        }

        if (view == "orbit")
        {
            var radius = stressCount > 0 ? MathF.Max(20.0f, MathF.Sqrt(stressCount) * 3.0f * 0.6f) : 12.0f;
            world.Add(camera, new OrbitCamera { Target = Vector3.Zero, Radius = radius, Height = radius * 0.35f, DegreesPerSecond = 30.0f });
        }
        else
        {
            world.Add(camera, new FreeCamera { MoveSpeed = stressCount > 0 ? 30.0f : 10.0f });
        }

        if (options.TryGetValue("--draw", out var draw))           engine.Graphics.IndirectDraw     = draw != "direct";
        if (options.TryGetValue("--instancing", out var inst))     engine.Graphics.Instancing       = inst != "off";
        if (options.TryGetValue("--culling", out var culling))     engine.Graphics.GpuCulling       = culling != "off";
        if (options.TryGetValue("--occlusion", out var occlusion)) engine.Graphics.OcclusionCulling = occlusion != "off";

        // ---------- ライト / 描画パイプライン ----------
        // 太陽 (斜め上から)。見本のシーンには点光源も置く (Showcase)
        var sun = world.CreateEntity();
        world.Add(sun, Transform.LookingAt(Vector3.Zero, new Vector3(0.4f, -1.0f, 0.6f)));
        world.Add(sun, new DirectionalLight { Color = new Vector3(1.0f, 0.95f, 0.88f), Intensity = 1.6f });

        if (options.TryGetValue("--pipeline", out var pipelineName) && GraphicsHotkeySystem.ParsePipeline(pipelineName) is { } pipeline)
        {
            engine.Graphics.Pipeline = pipeline;
        }
        if (options.TryGetValue("--lighting", out var lightingName) && GraphicsHotkeySystem.ParseLighting(lightingName) is { } lighting)
        {
            engine.Graphics.LightingModel = lighting;
        }
        if (options.TryGetValue("--ssao", out var ssao))
        {
            engine.Graphics.Ssao.Enabled  = ssao != "off";
            engine.Graphics.Ssao.ShowOnly = ssao == "show";
        }
        if (options.TryGetValue("--ssao-scale", out var ssaoScale) && float.TryParse(ssaoScale, out var scale))       engine.Graphics.Ssao.ResolutionScale = scale;
        if (options.TryGetValue("--ssao-samples", out var ssaoSamples) && int.TryParse(ssaoSamples, out var samples)) engine.Graphics.Ssao.SampleCount     = samples;
        if (options.TryGetValue("--aa", out var aaName) && GraphicsHotkeySystem.ParseAntiAliasing(aaName) is { } antiAliasing)
        {
            engine.Graphics.AntiAliasing = antiAliasing;
        }
        engine.AddSystem(new GraphicsHotkeySystem(engine.Graphics));
        engine.AddSystem(new GamepadMonitorSystem(engine));

        // ---------- 音 ----------
        // wav は ../Sound/ に置く (モデルの ../Model/ と同じ並び)。無ければ鳴らさずに続ける
        var soundPath = options.GetValueOrDefault("--sound", "../Sound/bgm.wav");
        if (File.Exists(soundPath))
        {
            try
            {
                var bgm     = engine.Assets.LoadSound(soundPath);
                var speaker = world.CreateEntity();
                world.Add(speaker, new AudioSource(bgm) { Loop = options.GetValueOrDefault("--sound-loop", "on") != "off", Volume = 0.5f });
                engine.AddSystem(new SoundHotkeySystem(speaker));
                Console.WriteLine($"[Sample] {bgm} (F7 : play / stop, F8 : volume)");
            }
            catch (Exception e) when (e is FileNotFoundException or InvalidOperationException)
            {
                Console.WriteLine($"[Sample] {e.Message}");
            }
        }
        else
        {
            Console.WriteLine($"[Sample] No sound file ({Path.GetFullPath(soundPath)}). Put a wav there or use --sound <path>.");
        }

        engine.Run();
    }

    // "--name value" の組を読む
    private static Dictionary<string, string> ParseArguments(string[] args)
    {
        var options = new Dictionary<string, string>();
        for (int i = 0; i + 1 < args.Length; i++)
        {
            if (args[i].StartsWith("--")) options[args[i]] = args[++i];
        }
        return options;
    }
}
