using System.Diagnostics;
using System.Reflection;
using System.Text;
using GameEngine;

namespace GameEngine.Player;

// ==========================================================================
// Player (ED8 後半) : 書き出したゲームを動かす
//   書き出したフォルダ :
//     <名前>.exe / GameEngine.Player.dll / GameEngine.Runtime.dll / Game.dll / C++ の DLL
//     Shader/        : シェーダー (RHI は作業フォルダの ../Shader から読む)
//     Data/          : 作業フォルダ = プロジェクトのフォルダ (player.json / ProjectSettings.json / Assets (使っている物だけ))
//   起動 : player.json → ProjectSettings → Game.dll の型と [GameSystem] → 最初のシーン → 再生 (エディターの再生と同じ順)
//
// 引数 (確かめる時用。普通は付けない)
//   --data <folder>     : Data フォルダ (既定は exe の隣の Data)
//   --scene <path>      : 最初のシーン (アセットのパス。既定は player.json の StartScene)
//   --capture-dt <秒>   : 決まった時間刻みで進める (Time.CaptureDeltaTime)
//   --frames N          : N フレーム進めたら終わる (--dump があれば、その時のシーンの様子を書く)
//   --dump <file>       : SceneState.Dump の結果を書く (エディターの再生と比べる)
//   --log <file>        : ログの書き先 (既定は exe の隣の Player.log)
//   --warmup-scene <path> --warmup-frames N : 先に別のシーンを N フレーム動かしてから最初のシーンを開いて再生する
//                         (エディターで前に別のシーンを使った後の再生と同じ状態。物理の決まった動きの確認用)
// ==========================================================================
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var options = new Dictionary<string, string>();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i].StartsWith("--") && !args[i + 1].StartsWith("--")) options[args[i]] = args[++i];

        var exeDirectory = AppContext.BaseDirectory;
        var log = openLog(options.GetValueOrDefault("--log", Path.Combine(exeDirectory, "Player.log")));
        try
        {
            return run(options, exeDirectory);
        }
        catch (Exception ex)
        {
            // 窓の無い exe なので、落ちた理由をログと箱で知らせる
            Console.WriteLine($"[Player] Fatal : {ex}");
            log?.Flush();
            if (!options.ContainsKey("--frames")) MessageBox.Show(ex.Message, "Game", MessageBoxButtons.OK, MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            log?.Dispose();
        }
    }

    private static int run(Dictionary<string, string> options, string exeDirectory)
    {
        var start = Stopwatch.GetTimestamp();
        var data = Path.GetFullPath(options.GetValueOrDefault("--data", Path.Combine(exeDirectory, "Data")));
        if (!Directory.Exists(data)) throw new DirectoryNotFoundException($"Data folder not found : {data}");
        var settings = PlayerSettings.Load(Path.Combine(data, PlayerSettings.FileName));
        Console.WriteLine($"[Player] {settings.ProductName} : data {data}");
        // どの .NET で動いているか (exe の隣の dotnet か、PC に入っている物か。Package 化で足した)
        Console.WriteLine($"[Player] .NET {Environment.Version} : {Path.GetDirectoryName(typeof(object).Assembly.Location)}");

        using var engine = new Engine(new EngineSettings
        {
            Title  = settings.ProductName,
            Width  = settings.Width,
            Height = settings.Height,
            VSync  = settings.VSync,
            WorkingDirectory = data,   // アセットのパス ("Assets/...") とシェーダー (../Shader) の起点
        });
        engine.Graphics.ProfilerVisible = settings.ShowProfiler;

        // アセット (書き出した物だけ。Id は .meta から)
        var database = new AssetDatabase(data);
        database.Refresh();
        engine.Assets.Database = database;

        // プロジェクトの設定 (時間 / 物理 / レイヤー / 入力)
        ProjectSettings.Load(Path.Combine(data, ProjectSettings.FileName)).Apply(engine);

        // ゲームのコード : exe の隣の Game.dll (型を先に知らせてからシーンを読む)
        var gameDll = Path.Combine(exeDirectory, "Game.dll");
        if (File.Exists(gameDll))
        {
            var game = Assembly.LoadFrom(gameDll);
            ComponentRegistry.KnownComponents(rescan: true).ToList();
            var systems = engine.AddGameSystems(game);
            Console.WriteLine($"[Player] Game.dll : {game.GetTypes().Count(t => typeof(IComponent).IsAssignableFrom(t))} components, {systems.Count} systems " +
                              $"({string.Join(", ", systems.Select(s => s.GetType().Name))})");
        }
        else
        {
            Console.WriteLine("[Player] Game.dll not found (engine components only)");
        }

        // 最初のシーン
        var scene = options.GetValueOrDefault("--scene", settings.FirstScene);
        if (string.IsNullOrEmpty(scene)) throw new InvalidOperationException("No start scene (player.json)");
        var scenePath = database.FullPath(scene);
        if (!File.Exists(scenePath)) throw new FileNotFoundException($"Scene not found : {scene}", scenePath);
        // --warmup-scene <path> --warmup-frames N (確かめる時用) : 先に別のシーンを N フレーム動かしてから、最初のシーンを開いて
        //   2 フレーム止めたまま描き (エディターの編集中と同じ)、再生を始める。エディターのように「前のシーンを使った後」の状態を作る
        var warmup = options.GetValueOrDefault("--warmup-scene");
        var warmupFrames = int.TryParse(options.GetValueOrDefault("--warmup-frames", "60"), out var w) ? w : 60;
        var warming = warmup != null;
        var skipStartFrame = false;   // 再生を始めたフレーム (LateUpdate の中) はまだ 1 フレームも進んでいないので数えない
        if (warmup != null)
        {
            engine.LoadScene(database.FullPath(warmup));
            engine.BeginPlay();
            var warmed = 0;
            engine.LateUpdate += () =>
            {
                if (!warming) return;
                ++warmed;
                if (warmed == warmupFrames) { engine.LoadScene(scenePath); engine.IsPlaying = false; }
                else if (warmed == warmupFrames + 2) { engine.BeginPlay(); warming = false; skipStartFrame = true; }
            };
        }
        else
        {
            engine.LoadScene(scenePath);
            engine.BeginPlay();   // 再生の時間を 0 から (エディターの再生ボタンと同じ)
        }
        Console.WriteLine($"[Player] Started in {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F0} ms");

        // 確かめる時用 : 決まった刻みで N フレーム進めて、様子を書いて終わる
        if (options.TryGetValue("--capture-dt", out var dtText) && float.TryParse(dtText, System.Globalization.CultureInfo.InvariantCulture, out var dt))
            Time.CaptureDeltaTime = dt;
        if (options.TryGetValue("--frames", out var framesText) && int.TryParse(framesText, out var frames))
        {
            var dump = options.GetValueOrDefault("--dump");
            var count = 0;
            engine.LateUpdate += () =>
            {
                if (warming) return;   // 数えるのは最初のシーンの再生から
                if (skipStartFrame) { skipStartFrame = false; return; }
                if (++count != frames) return;
                if (dump != null) File.WriteAllText(dump, SceneState.Dump(engine.World), new UTF8Encoding(false));
                Console.WriteLine($"[Player] {frames} frames done{(dump != null ? $" (dump : {dump})" : "")}");
                engine.Quit();
            };
        }

        engine.Run();
        Console.WriteLine("[Player] Exit");
        return 0;
    }

    // ログ (Console の出力) をファイルにも書く。書けない場所 (読み取り専用) なら書かない
    private static StreamWriter? openLog(string file)
    {
        try
        {
            var writer = new StreamWriter(file, false, new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(writer);
            return writer;
        }
        catch (Exception)
        {
            return null;
        }
    }
}
