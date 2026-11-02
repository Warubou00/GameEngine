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
//
// 操作 (orbit 以外) : 右ボタンを押しながらマウスで向き、WASD で移動、E / Q で上下、Shift で速く、F1 でプロファイラー
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

        // ---------- モデル (読み込み〜 GPU への転送完了までの時間をプロファイラーに出す) ----------
        var stopwatch = Stopwatch.StartNew();
        var box = engine.Assets.LoadModel("../Model/box.obj");
        engine.WaitForGpu();
        engine.ReportProfilerValue("LoadModel (ms)", stopwatch.Elapsed.TotalMilliseconds);

        var world = engine.World;

        // 原点に 1 個 (動かないので Static)
        var origin = world.CreateEntity();
        world.Add(origin, new Transform());
        world.Add(origin, new MeshRenderer(box));
        world.Add(origin, new Static());

        // ---------- カメラ ----------
        var camera = world.CreateEntity();
        world.Add(camera, Transform.LookingAt(new Vector3(0, 5, -10), Vector3.Zero));
        world.Add(camera, new Camera());

        // ---------- 引数 ----------
        var options     = ParseArguments(args);
        var stressCount = options.TryGetValue("--stress", out var stress) && int.TryParse(stress, out var n) ? n : 0;
        var view        = options.GetValueOrDefault("--view", "");

        var animate = options.GetValueOrDefault("--animate", "off") == "on";
        if (animate) engine.AddSystem(new SpinSystem());

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
