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
//   --shadows on|off / --shadow-res N / --shadow-distance D : 影 (既定 on / 2048 / 60)
//   --shadow-cascades N (1〜4。既定 4) / --shadow-union on (全段で 1 回のカリング) / --shadow-throttle on (遠い段を間引く)
//   --shadow-show on : 段ごとに色を付ける
//   --colliders on|off          : コライダーを線で表示 (既定 on。見本のシーンで重なると赤)
//   --collision-bench N         : コライダーだけの物を N 個動かして衝突判定を計測 (--broadphase bvh|brute|grid)
//                                 --bench-moving P : 毎フレーム動く物の割合 (%。既定 100)
//                                 --bench-layers N : レイヤーを N 個に分け、違うレイヤーどうしを調べない (--layer-pruning off で木での間引きを切る)
//                                 --physics-workers N : 衝突判定の追加のスレッドの数 (既定 論理コア - 1、3 本まで) / --tree-rebuild off
//   --scene physics             : 物理 (剛体 / ジョイント) の見本のシーン。スペースで球を撃つ / R で積み木を作り直す
//   --physics-bench N           : 箱を N 個落として積む (物理の計測。--physics-workers N / --sleep off も使える)
//   --scene particles           : GPU パーティクルの見本 (噴水 / 花火 / たき火と煙 / 光の群れ)。P ソート / O 衝突 / K 花火 / U ON・OFF
//                                 J 噴水のエミッターを外す・付け直す。--view fountain|smoke で近くから見る
//   --particle-bench N          : GPU パーティクルを N 個保ち続ける (計測。[ParticleBench] を出す)
//   --particle-sort on|off / --particle-collision on|off / --particle-capacity N / --particle-half on|off : GPU パーティクルの設定
//   見本のシーン (ED6) : 見本 (既定 / --scene physics / --scene particles) は、プロジェクトのシーンのファイル
//                       (CSharp_console/Project/Assets/Scenes/showcase|physics|particles.scene.json) から読む。
//                       --view を付けた時 / 計測の時はコードで作る
//   モデル / テクスチャは CSharp_console/Project/Assets だけにある (以前は出力の ../Model に元があり、Assets が無ければそこから読んだ)
//   --code-scene on             : 見本をシーンのファイルではなくコードで作る (シーンのファイルを作り直す時 : --code-scene on --save-scene <path>)
//   --save-scene <path>         : 起動時に作ったシーンを JSON に保存する (ED1)
//   --load-scene <path>         : 起動時に作ったシーンを消して、JSON のシーンを読み込む (System は同じ引数の時と同じ物が入る)
//   --serialize-test            : 全部のコンポーネントと見本のシーンで、保存 → 読み込み → 同じ値 を確かめて終わる
//   --hierarchy-test            : 親子 / 有効・無効 を確かめて終わる (ED2)
//   --sound <path>              : 鳴らす wav (既定は ../Sound/bgm.wav。無ければ鳴らさない)
//   --sound-loop on|off         : ループするか (既定は on)
//
// 操作 (orbit 以外) : 右ボタンを押しながらマウスで向き、WASD で移動、E / Q で上下、Shift で速く、F1 でプロファイラー
//                     F2 で Forward / Deferred、F3 で Unlit / Lambert / PBR、F4 で SSAO、F5 で遮蔽だけを表示
//                     F6 でアンチエイリアス (なし → FXAA → MSAA 2x → 4x → 8x)、F11 で影、F12 で影の段の色分け
//                     C でコライダーの表示、X で接触点の表示、B で衝突判定の探し方 (BVH → 総当たり → グリッド)
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

        // ---------- プロジェクト (ED6) : 見本のシーンのファイルとアセット (Assets/Models) ----------
        //   モデル / テクスチャは Project/Assets だけに置く (以前は出力の ../Model にも元があった)。無ければ動かせないので終わる
        var project = findProject();
        if (project == null)
        {
            Console.WriteLine("[Sample] CSharp_console/Project/Assets/Models が見つかりません (モデル / テクスチャは Project/Assets にだけ置く)。");
            Environment.ExitCode = 1;
            return;
        }
        engine.Assets.Database = new AssetDatabase(project);
        engine.Assets.Database.Refresh();
        var stressCount = options.TryGetValue("--stress", out var stress) && int.TryParse(stress, out var n) ? n : 0;
        var view        = options.GetValueOrDefault("--view", "");

        // ---------- モデル (読み込み〜 GPU への転送完了までの時間をプロファイラーに出す) ----------
        // モデルは Assets/Models/obj と Assets/Models/pmx/<モデル名> に置く
        var stopwatch = Stopwatch.StartNew();
        var box = engine.Assets.LoadModel("Assets/Models/obj/box.obj");
        engine.WaitForGpu();
        engine.ReportProfilerValue("LoadModel (ms)", stopwatch.Elapsed.TotalMilliseconds);

        var world = engine.World;

        var benchCount = options.TryGetValue("--collision-bench", out var bench) && int.TryParse(bench, out var b) ? b : 0;
        var physicsBench = options.TryGetValue("--physics-bench", out var pbench) && int.TryParse(pbench, out var pb) ? pb : 0;
        var physicsScene = options.GetValueOrDefault("--scene", "") == "physics";
        var particleScene = options.GetValueOrDefault("--scene", "") == "particles";
        var particleBench = options.TryGetValue("--particle-bench", out var particleText) && int.TryParse(particleText, out var pn) ? pn : 0;
        // 物理 / パーティクルの見本は、見本のシーン (Showcase) と原点の箱を置かない
        var physicsOnly  = physicsScene || physicsBench > 0 || particleScene || particleBench > 0;

        // 原点に 1 個 (動かないので Static)
        if (!physicsOnly)
        {
            var origin = world.CreateEntity();
            world.Add(origin, new Transform());
            world.Add(origin, new MeshRenderer(box));
            world.Add(origin, new Static());
            world.Add(origin, Collider.Box(new Vector3(1, 1, 1), new Vector3(0, 1, 0)));
        }

        // 物理 (P1) の見本 / 計測
        if (physicsScene)
        {
            PhysicsDemo.Spawn(engine, box);
            engine.Physics.ShowColliders = true;
        }
        if (physicsBench > 0)
        {
            PhysicsBench.Spawn(engine, box, physicsBench);
            if (options.TryGetValue("--physics-workers", out var pworkerText) && int.TryParse(pworkerText, out var pworkers)) engine.Physics.SetWorkerCount(pworkers);
            if (options.GetValueOrDefault("--sleep", "on") == "off") engine.Physics.Solver.EnableSleep = false;
        }

        // GPU パーティクルの見本 / 計測
        if (particleScene) ParticleDemo.Spawn(engine, box);
        if (particleBench > 0) ParticleBench.Spawn(engine, box, particleBench);
        if (particleScene || particleBench > 0) engine.AddSystem(new ParticleHotkeySystem(engine.Graphics.Particles));
        if (options.TryGetValue("--particle-sort", out var particleSort))           engine.Graphics.Particles.Sorting   = particleSort != "off";
        if (options.TryGetValue("--particle-collision", out var particleCollision)) engine.Graphics.Particles.Collision = particleCollision != "off";
        if (options.TryGetValue("--particle-half", out var particleHalf))           engine.Graphics.Particles.HalfResolution = particleHalf != "off";
        if (options.TryGetValue("--particle-capacity", out var capacityText) && int.TryParse(capacityText, out var particleCapacity))
        {
            engine.Graphics.Particles.Capacity = particleCapacity;
        }

        // 負荷テストでない時は、複数のモデル (OBJ / PMX) を並べた見本のシーン + 衝突判定の見本
        if (stressCount == 0 && benchCount == 0 && !physicsOnly)
        {
            Showcase.Spawn(engine);
            CollisionDemo.Spawn(world);
            engine.AddSystem(new OrbitMoverSystem());
            engine.AddSystem(new LaserDemoSystem(engine.Physics));
            engine.Physics.ShowColliders = true;
            engine.Physics.ShowContacts  = true;
        }
        if (benchCount > 0)
        {
            var moving = options.TryGetValue("--bench-moving", out var movingText) && int.TryParse(movingText, out var m) ? m : 100;
            var layerCount = options.TryGetValue("--bench-layers", out var layerText) && int.TryParse(layerText, out var l) ? Math.Clamp(l, 1, 32) : 1;
            var half = CollisionBench.Spawn(world, benchCount, moving, layerCount);
            // レイヤーを分けた時 : 同じレイヤーどうしだけ調べる (床のレイヤー 0 とは全部当たる)
            for (int a = 1; a < layerCount; a++)
                for (int c = 1; c < layerCount; c++)
                    if (a != c) engine.Physics.SetLayerCollision(a, c, false);
            if (options.GetValueOrDefault("--layer-pruning", "on") == "off") engine.Physics.SetLayerPruning(false);
            if (options.GetValueOrDefault("--tree-rebuild", "on") == "off") engine.Physics.SetTreeRebuild(false);
            if (options.TryGetValue("--physics-workers", out var workerText) && int.TryParse(workerText, out var workers)) engine.Physics.SetWorkerCount(workers);
            engine.AddSystem(new BenchMoverSystem(half, engine.Physics));
        }
        if (options.TryGetValue("--colliders", out var colliders)) engine.Physics.ShowColliders = colliders == "on";
        if (options.TryGetValue("--broadphase", out var broadphaseName) && PhysicsHotkeySystem.Parse(broadphaseName) is { } broadphase)
        {
            engine.Physics.Broadphase = broadphase;
        }
        engine.AddSystem(new PhysicsHotkeySystem(engine.Physics));

        // ---------- カメラ ----------
        var camera = world.CreateEntity();
        var cameraStart = particleScene ? ParticleDemo.CameraFor(view)
                        : particleBench > 0 ? Transform.LookingAt(new Vector3(0, 6, -20), new Vector3(0, 4, 4))
                        : physicsOnly ? Transform.LookingAt(new Vector3(0, 9, -24), new Vector3(0, 2, 0))
                        : Transform.LookingAt(new Vector3(0, 5, -10), Vector3.Zero);
        world.Add(camera, cameraStart);
        world.Add(camera, new Camera());

        var animate = options.GetValueOrDefault("--animate", "off") == "on";
        if (animate || (stressCount == 0 && !physicsOnly)) engine.AddSystem(new SpinSystem());   // 見本のシーンのキャラクターも回す

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
        if (options.TryGetValue("--shadows", out var shadows)) engine.Graphics.Shadows.Enabled = shadows != "off";
        if (options.TryGetValue("--shadow-res", out var shadowRes) && int.TryParse(shadowRes, out var res)) engine.Graphics.Shadows.Resolution = res;
        if (options.TryGetValue("--shadow-distance", out var shadowDistance) && float.TryParse(shadowDistance, out var distance)) engine.Graphics.Shadows.Distance = distance;
        if (options.TryGetValue("--shadow-cascades", out var cascadeText) && int.TryParse(cascadeText, out var cascades)) engine.Graphics.Shadows.CascadeCount = cascades;
        if (options.TryGetValue("--shadow-union", out var union))       engine.Graphics.Shadows.UnionCulling        = union == "on";
        if (options.TryGetValue("--shadow-throttle", out var throttle)) engine.Graphics.Shadows.ThrottleFarCascades = throttle == "on";
        if (options.TryGetValue("--shadow-show", out var show))         engine.Graphics.Shadows.ShowCascades        = show == "on";
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

        // ---------- 親子のテスト (ED2) : 何も無いシーンで、フレームを進めながら確かめて終わる ----------
        if (args.Contains("--hierarchy-test"))
        {
            world.Clear();
            var testCamera = world.CreateEntity("Camera");
            world.Add(testCamera, Transform.LookingAt(new Vector3(0, 8, -20), Vector3.Zero));
            world.Add(testCamera, new Camera());
            engine.AddSystem(new HierarchyTest(engine, box));
            engine.Run();
            return;
        }

        // ---------- シーンの保存 / 読み込み (ED1) ----------
        if (args.Contains("--serialize-test"))
        {
            Environment.ExitCode = SerializationTest.Run(engine);
            return;
        }
        if (options.TryGetValue("--save-scene", out var savePath))
        {
            var saveStart = Stopwatch.GetTimestamp();
            engine.SaveScene(savePath);
            Console.WriteLine($"[Scene] Saved {Path.GetFullPath(savePath)} ({world.EntityCount} entities, {Stopwatch.GetElapsedTime(saveStart).TotalMilliseconds:F1} ms)");
        }
        if (options.TryGetValue("--load-scene", out var loadPath)) engine.LoadScene(loadPath);
        else if (sampleSceneFile(project, options, stressCount, benchCount, physicsBench, particleBench) is { } sceneFile)
        {
            // 見本のシーンはファイルから (コードで作った物と置き換える。System はコードの時と同じ物が入っている。ED6)
            engine.LoadScene(sceneFile);
            Console.WriteLine($"[Sample] Scene from file : {sceneFile} (--code-scene on : build it in code)");
        }

        engine.Run();
    }

    // リポジトリの CSharp_console/Project (Assets/Models がある時だけ。無ければ null)
    private static string? findProject()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "GameEngine.sln"))) root = root.Parent;
        if (root == null) return null;
        var project = Path.Combine(root.FullName, "CSharp_console", "Project");
        return Directory.Exists(Path.Combine(project, "Assets", "Models")) ? project : null;
    }

    // 見本のシーンのファイル (使わない時は null)
    private static string? sampleSceneFile(string? project, Dictionary<string, string> options, int stressCount, int benchCount, int physicsBench, int particleBench)
    {
        if (project == null || options.GetValueOrDefault("--code-scene", "off") == "on" || options.ContainsKey("--view")) return null;
        if (stressCount > 0 || benchCount > 0 || physicsBench > 0 || particleBench > 0) return null;
        var name = options.GetValueOrDefault("--scene", "showcase");
        var file = Path.Combine(project, "Assets", "Scenes", $"{name}.scene.json");
        return File.Exists(file) ? file : null;
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
