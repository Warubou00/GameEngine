namespace GameEngine.Editor;

// ==========================================================================
// エディター (ED3)
//   GameEngine.Editor.exe [--project <folder>] [--scene <path>] [--open-graph <Assets/....shadergraph.json>] [--editor-test] [--resave-scenes] [--quit-after N]
//     --open-graph : シェーダーグラフの窓でそのグラフを開いて始める (SH4)
//     --crash-test <managed|native|thread|stackoverflow> : 10 フレーム後にわざと落とす (v1.8。クラッシュレポートは %LOCALAPPDATA%\<エンジン>\CrashReports\Editor)
//     ランチャー (ED9) は --project で開くプロジェクトを渡す
//     プロジェクト : 既定は CSharp_console/Project (中の Assets フォルダがアセットの置き場。ED5)
//       最初の 1 回だけ、見本のモデル (見本の出力フォルダの Model) を Assets/Models へ写す (元のファイルは変えない)
//     既定は Assets/Scenes/showcase.scene.json を開く (無ければ空のシーン)
//     --resave-scenes : Assets の全部のシーンを読んで今の形式 (アセットの Id 付き) で保存し直して終わる
//   作業フォルダは見本の出力フォルダ (シェーダー ../Shader を読むため)
//   Package のエディター (exe の 1 つ上に同梱の dotnet がある) : リポジトリを探さない。作業フォルダは exe のフォルダ (シェーダーは <版>/Shader)
//     プロジェクトの既定は Package の Project/SampleProject (<Package>/Editor/<版>/Editor/exe から辿る)
// ==========================================================================
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // v1.8 : クラッシュレポート (%LOCALAPPDATA%\<エンジンの名前>\CrashReports\Editor)。窓を作る前に
        //   以前はエディターが落ちると何も残らなかった (ゲームのコードの例外は v1.7 から Console に出して続ける)
        CrashReporter.ShowDialog = !args.Contains("--editor-test") && !args.Contains("--quit-after") && !args.Contains("--resave-scenes");
        CrashReporter.Install("Editor", continueOnUIException: true);   // WinForms のイベント (ドラッグ & ドロップ 等) の例外は書いて続ける
        try
        {
            run(args);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Editor] Fatal : {ex}");
            CrashReporter.Report(ex, "Fatal exception (Editor)");
            Environment.ExitCode = 1;
        }
        finally
        {
            CrashReporter.Shutdown();
        }
    }

    private static void run(string[] args)
    {
        var options = new Dictionary<string, string>();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i].StartsWith("--") && !args[i + 1].StartsWith("--")) options[args[i]] = args[++i];

        // リポジトリのルート (GameEngine.sln のあるフォルダ) を探す
        var exeDirectory = AppContext.BaseDirectory;
        // Package のエディターはリポジトリの中に置かれていても、リポジトリの物 (見本の出力 / Player のソース) を使わない (Package 化で足した)
        var root = DotnetTool.IsPackaged ? null : new DirectoryInfo(exeDirectory);
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
        var packageSample = Path.GetFullPath(Path.Combine(exeDirectory, "..", "..", "..", "Project", "SampleProject"));   // Package の見本
        var projectRoot = options.GetValueOrDefault("--project",
            root != null ? Path.Combine(root.FullName, "CSharp_console", "Project") :
            DotnetTool.IsPackaged && Directory.Exists(packageSample) ? packageSample : Path.Combine(exeDirectory, "Project"));
        var database = new AssetDatabase(projectRoot);
        var created = database.Refresh();
        EditorVersion.WriteProjectVersion(database.ProjectRoot);   // どの版のエディターで開いたか (ED9。ランチャーが出す)
        Console.WriteLine($"[Editor] {EngineInfo.Name} Editor {EditorVersion.Display}{(DotnetTool.IsPackaged ? $" (package, .NET SDK : {DotnetTool.Bundled})" : "")}");
        Console.WriteLine($"[Assets] {database.AssetsRoot} : {database.Entries.Count()} assets{(created > 0 ? $" ({created} new .meta)" : "")}");
        var scenes = Path.Combine(database.AssetsRoot, "Scenes");

        using var engine = new Engine(new EngineSettings { Title = $"{EngineInfo.Name} Editor", Width = 1600, Height = 900, WorkingDirectory = workingDirectory });
        engine.IsPlaying       = false;   // 編集中 (シーンを動かさない)
        engine.Graphics.ProfilerVisible = false;   // C++ のプロファイラーは View メニュー / F1 で
        engine.Assets.Database = database;
        // v1.8 : 後ろにある時も動かすか (View > Run In Background。自動の確認では止めない : 他の窓を使っている間も進めるため)
        var preferences = EditorPreferences.Load(Path.Combine(exeDirectory, EditorPreferences.FileName));
        engine.RunInBackground = preferences.RunInBackground || args.Contains("--editor-test") || options.ContainsKey("--quit-after");
        UI.Manager!.AutoSubmit = false;   // v1.7 : ゲームの UI は ImGui の中 (シーンビューの絵の上) に描く (自分で SubmitUI すると ImGui を消してしまう)

        using var imgui = new ImGuiBackend(engine, Path.Combine(exeDirectory, "imgui.ini"));
        var editor = new EditorApp(engine, imgui, log, database, scenes);
        editor.Preferences = preferences;
        // ゲームのコード (ED7) : Project/Scripts/Game.csproj をビルドして読み込む (コンポーネントの型と [GameSystem])。シーンを開く前に
        //   (以前は見本 (GameEngine_console) を参照して型を読み、System をここで 1 つずつ登録していた)
        editor.LoadScripts();
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
        // --quit-after N : N フレーム描いたら閉じる (ランチャーの確認用。ED9)
        if (options.TryGetValue("--quit-after", out var quitText) && int.TryParse(quitText, out var quitFrames))
        {
            var frames = 0;
            engine.LateUpdate += () => { if (++frames == quitFrames) { Console.WriteLine($"[Editor] {frames} frames, quit"); editor.ForceQuit(); } };
        }
        // 既定のシーン : showcase → Scenes の最初のシーン → 1 つも無ければ (新しいプロジェクト。v1.3) 光とカメラだけの Main.scene.json を作る
        var defaultScene = Path.Combine(scenes, "showcase.scene.json");
        if (!File.Exists(defaultScene) && Directory.Exists(scenes))
            defaultScene = Directory.EnumerateFiles(scenes, "*.scene.json").Order(StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? defaultScene;
        var scenePath = options.GetValueOrDefault("--scene", defaultScene);
        if (File.Exists(scenePath)) editor.OpenScene(scenePath);
        else if (!options.ContainsKey("--scene") && !Directory.EnumerateFiles(database.AssetsRoot, "*.scene.json", SearchOption.AllDirectories).Any())
        {
            var main = Path.Combine(scenes, "Main.scene.json");
            editor.RequestNewScene();   // 光とカメラだけのシーン (起動したばかりなので聞かずに作る)
            editor.SaveSceneTo(main);
            database.Refresh();
            editor.OpenScene(main);
            Console.WriteLine($"[Editor] New project : created {main}");
        }
        else Console.WriteLine($"[Editor] Scene not found : {scenePath} (File > Open Scene)");
        if (options.TryGetValue("--open-graph", out var graphPath)) editor.OpenShaderGraph(graphPath);   // SH4
        if (options.TryGetValue("--open-widget", out var widgetPath)) editor.OpenWidget(widgetPath);   // v1.7 : Widget Designer
        // v1.8 : --crash-test <managed|native|thread|stackoverflow> : 10 フレーム後にわざと落とす (クラッシュレポートの確認用)
        if (options.TryGetValue("--crash-test", out var crashKind))
        {
            var crashFrames = 0;
            engine.LateUpdate += () => { if (++crashFrames == 10) CrashReporter.TestCrash(crashKind); };
        }

        engine.Run();
        DotnetTool.ShutdownBuildServers();   // Package : 同梱の SDK のビルドのサーバーを残さない (フォルダを掴んだままになるため)
    }
}
