namespace GameEngine.Editor;

// ==========================================================================
// エディター (ED3)
//   GameEngine.Editor.exe [--project <folder>] [--scene <path>] [--editor-test] [--resave-scenes]
//     プロジェクト : 既定は CSharp_console/Project (中の Assets フォルダがアセットの置き場。ED5)
//       最初の 1 回だけ、見本のモデル (見本の出力フォルダの Model) を Assets/Models へ写す (元のファイルは変えない)
//     既定は Assets/Scenes/showcase.scene.json を開く (無ければ空のシーン)
//     --resave-scenes : Assets の全部のシーンを読んで今の形式 (アセットの Id 付き) で保存し直して終わる
//   作業フォルダは見本の出力フォルダ (シェーダー ../Shader を読むため)
// ==========================================================================
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var options = new Dictionary<string, string>();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i].StartsWith("--") && !args[i + 1].StartsWith("--")) options[args[i]] = args[++i];

        // リポジトリのルート (GameEngine.sln のあるフォルダ) を探す
        var exeDirectory = AppContext.BaseDirectory;
        var root = new DirectoryInfo(exeDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "GameEngine.sln"))) root = root.Parent;
        var configuration = exeDirectory.Contains($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}") ? "Debug" : "Release";
        // シェーダー (../Shader) は見本の出力フォルダから読む
        string? workingDirectory = null;
        if (root != null)
        {
            var sampleOutput = Path.Combine(root.FullName, "CSharp_console", "GameEngine_console", "bin", "x64", configuration, "net9.0-windows");
            if (Directory.Exists(sampleOutput)) workingDirectory = sampleOutput;
        }

        var log = new ConsoleLog(Console.Out);
        Console.SetOut(log);

        // プロジェクト (ED5)
        var projectRoot = options.GetValueOrDefault("--project", root != null ? Path.Combine(root.FullName, "CSharp_console", "Project") : Path.Combine(exeDirectory, "Project"));
        var database = new AssetDatabase(projectRoot);
        if (workingDirectory != null) seedSampleAssets(Path.GetFullPath(Path.Combine(workingDirectory, "..", "Model")), Path.Combine(database.AssetsRoot, "Models"));
        var created = database.Refresh();
        Console.WriteLine($"[Assets] {database.AssetsRoot} : {database.Entries.Count()} assets{(created > 0 ? $" ({created} new .meta)" : "")}");
        var scenes = Path.Combine(database.AssetsRoot, "Scenes");

        // 見本のコンポーネント (Spin など) の入った DLL を読み込んでおく (シーンの読み込み / コンポーネントを足すメニュー用。ED7 でゲームの DLL に)
        // (以前は typeof(Spin) を捨てる式で読ませていたが、Release ではその式ごと消えて読み込まれなかった)
        try { System.Reflection.Assembly.Load("GameEngine_console"); }
        catch (Exception ex) { Console.WriteLine($"[Editor] Game assembly not loaded : {ex.Message}"); }

        using var engine = new Engine(new EngineSettings { Title = "GameEngine Editor", Width = 1600, Height = 900, WorkingDirectory = workingDirectory });
        engine.IsPlaying       = false;   // 編集中 (シーンを動かさない)
        engine.Graphics.ProfilerVisible = false;   // C++ のプロファイラーは View メニュー / F1 で
        engine.Assets.Database = database;

        // 再生中に動かすゲームの System (見本の物。ED7 でゲームの DLL から自動で登録する)。編集中は Engine が止めている
        engine.AddSystem(new GameEngine_console.SpinSystem());
        engine.AddSystem(new GameEngine_console.OrbitMoverSystem());
        engine.AddSystem(new GameEngine_console.FireworkSystem());   // パーティクルの見本 (ED6)
        engine.AddSystem(new GameEngine_console.FlyerSystem());

        using var imgui = new ImGuiBackend(engine, Path.Combine(exeDirectory, "imgui.ini"));
        var editor = new EditorApp(engine, imgui, log, database, scenes);
        if (args.Contains("--resave-scenes"))
        {
            foreach (var entry in database.Entries.Where(e => e.Kind == AssetKind.Scene).ToList())
            {
                editor.OpenScene(database.FullPath(entry.Path));
                editor.SaveSceneTo(database.FullPath(entry.Path));
            }
            return;
        }
        // シェーダーの読み直し (ED5) : 実行中に読むフォルダ (../Shader) と、元のファイル (RHI/Source/Shader) を見張る
        editor.WatchShaders(Path.GetFullPath(Path.Combine(Environment.CurrentDirectory, "..", "Shader")),
                            root != null ? Path.Combine(root.FullName, "RHI", "Source", "Shader") : null);
        if (args.Contains("--editor-test")) editor.FrameHook = new EditorTest(editor).Frame;   // 自動の確認 (ED4)
        var scenePath = options.GetValueOrDefault("--scene", Path.Combine(scenes, "showcase.scene.json"));
        if (File.Exists(scenePath)) editor.OpenScene(scenePath);
        else Console.WriteLine($"[Editor] Scene not found : {scenePath} (File > Open Scene)");

        engine.Run();
    }

    // 見本のモデルを Assets/Models へ写す (Assets/Models がまだ無い時だけ。DDS のキャッシュは写さない)
    private static void seedSampleAssets(string source, string target)
    {
        if (Directory.Exists(target) || !Directory.Exists(source)) return;
        var count = 0;
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            if (file.EndsWith(".dds", StringComparison.OrdinalIgnoreCase) && Path.GetFileName(file).Split('.').Length >= 4) continue;
            var copy = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy);
            count++;
        }
        Console.WriteLine($"[Assets] Copied {count} sample files : {source} -> {target}");
    }
}
