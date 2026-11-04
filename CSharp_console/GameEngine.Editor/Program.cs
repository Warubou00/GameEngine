namespace GameEngine.Editor;

// ==========================================================================
// エディター (ED3)
//   GameEngine.Editor.exe [--scene <path>]
//     既定は CSharp_console/Scenes/showcase.scene.json を開く (無ければ空のシーン)
//   アセットの置き場 : 今は見本 (GameEngine_console) の出力フォルダを作業フォルダにして、../Model を読む
//   (Assets フォルダは ED5)
// ==========================================================================
internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        var options = new Dictionary<string, string>();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i].StartsWith("--")) options[args[i]] = args[++i];

        // リポジトリのルート (GameEngine.sln のあるフォルダ) を探す
        var exeDirectory = AppContext.BaseDirectory;
        var root = new DirectoryInfo(exeDirectory);
        while (root != null && !File.Exists(Path.Combine(root.FullName, "GameEngine.sln"))) root = root.Parent;
        var configuration = exeDirectory.Contains($"{Path.DirectorySeparatorChar}Debug{Path.DirectorySeparatorChar}") ? "Debug" : "Release";
        var scenes = root != null ? Path.Combine(root.FullName, "CSharp_console", "Scenes") : Path.Combine(exeDirectory, "Scenes");
        // モデル (../Model) とシェーダー (../Shader) は見本の出力フォルダから読む
        string? workingDirectory = null;
        if (root != null)
        {
            var sampleOutput = Path.Combine(root.FullName, "CSharp_console", "GameEngine_console", "bin", "x64", configuration, "net9.0-windows");
            if (Directory.Exists(sampleOutput)) workingDirectory = sampleOutput;
        }

        var log = new ConsoleLog(Console.Out);
        Console.SetOut(log);

        // 見本のコンポーネント (Spin など) の入った DLL を読み込んでおく (シーンの読み込み / コンポーネントを足すメニュー用。ED7 でゲームの DLL に)
        // (以前は typeof(Spin) を捨てる式で読ませていたが、Release ではその式ごと消えて読み込まれなかった)
        try { System.Reflection.Assembly.Load("GameEngine_console"); }
        catch (Exception ex) { Console.WriteLine($"[Editor] Game assembly not loaded : {ex.Message}"); }

        using var engine = new Engine(new EngineSettings { Title = "GameEngine Editor", Width = 1600, Height = 900, WorkingDirectory = workingDirectory });
        engine.IsPlaying       = false;   // 編集中 (シーンを動かさない)
        engine.Graphics.ProfilerVisible = false;   // C++ のプロファイラーは View メニュー / F1 で

        using var imgui = new ImGuiBackend(engine, Path.Combine(exeDirectory, "imgui.ini"));
        var editor = new EditorApp(engine, imgui, log, scenes);
        var scenePath = options.GetValueOrDefault("--scene", Path.Combine(scenes, "showcase.scene.json"));
        if (File.Exists(scenePath)) editor.OpenScene(scenePath);
        else Console.WriteLine($"[Editor] Scene not found : {scenePath} (File > Open Scene)");

        engine.Run();
    }
}
