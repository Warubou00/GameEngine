using System.Diagnostics;
using System.Reflection;
using System.Runtime.Loader;

namespace GameEngine.Editor;

// ==========================================================================
// ゲームのコード (Project/Scripts/Game.csproj) のビルドと読み込み (ED7)
//   - ビルド : dotnet build (別のスレッドで。エラーは Console に出す)。.cs を保存すると 0.5 秒後にビルドする
//   - 読み込み : 捨てられる AssemblyLoadContext に、Game.dll をファイルではなくバイト列で読む (ファイルを掴まないのでビルドし直せる)
//     エンジン (GameEngine.Runtime) はエディターが読み込んだ物を使う (同じ型にするため)
//   - 読み直し (EditorApp.ReloadScripts) : シーンを JSON にする → [GameSystem] を外す → 古い型の入れ物と型の情報を捨てる →
//     古い DLL を捨てる → 新しい DLL を読み、[GameSystem] を作る → JSON からシーンを作り直す (値は型の名前で引き継ぐ)
//   - エディターの拡張 (ED7 後半) : Scripts/Editor/GameEditor.csproj があれば、そちらをビルドし (Game も一緒にビルドされる)、
//     Game.dll と GameEditor.dll を同じ AssemblyLoadContext に読む。GameEditor.dll の [CustomEditor] などを EditorExtensions に登録する
// ==========================================================================
internal sealed class ScriptHost : IDisposable
{
    private readonly Engine _engine;
    private readonly string _project;          // Game.csproj
    private readonly string _editorProject;    // Editor/GameEditor.csproj (無ければ Game だけ)
    private readonly string _folder;
    private readonly FileSystemWatcher? _watcher;
    private volatile bool _changed;
    private double _changedAt;
    private Task<(bool Ok, string Log, double Ms)>? _build;

    private GameLoadContext? _context;
    private List<object> _systems = new();
    private WeakReference? _lastContext;        // 捨てた DLL が本当に解放されたか (確認用)

    public Assembly? Assembly { get; private set; }
    public bool IsBuilding => _build != null;
    public int  LoadCount  { get; private set; }
    public string? LastError { get; private set; }
    public double LastBuildMs { get; private set; }
    public int SystemCount => _systems.Count;
    public bool Available => File.Exists(_project);

    // ビルドが終わって読み直す時 (EditorApp がシーンの引き継ぎをする)
    public event Action<ScriptHost>? BuildSucceeded;

    public ScriptHost(Engine engine, string project)
    {
        _engine  = engine;
        _project = Path.GetFullPath(project);
        _folder  = Path.GetDirectoryName(_project)!;
        _editorProject = Path.Combine(_folder, "Editor", "GameEditor.csproj");
        if (!Directory.Exists(_folder)) return;
        try
        {
            _watcher = new FileSystemWatcher(_folder, "*.cs") { IncludeSubdirectories = true, EnableRaisingEvents = true,
                                                                  NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName };
            FileSystemEventHandler handler = (_, e) =>
            {
                if (e.FullPath.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
                    e.FullPath.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}")) return;
                _changed = true;
            };
            _watcher.Changed += handler;
            _watcher.Created += handler;
            _watcher.Deleted += handler;
            _watcher.Renamed += (_, e) => handler(null!, e);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Scripts] Cannot watch {_folder} : {ex.Message}");
        }
    }

    // ---------------------------------------------------------------------
    // ビルド
    // ---------------------------------------------------------------------

    // 出来上がる DLL (dotnet build の既定の出力。エディターの拡張があれば、その出力に Game.dll も写される)
    private bool   hasEditorProject => File.Exists(_editorProject);
    private string outputFolder => hasEditorProject ? Path.Combine(_folder, "Editor", "bin", "Debug", "net9.0-windows") : Path.Combine(_folder, "bin", "Debug", "net9.0-windows");
    private string outputDll => Path.Combine(outputFolder, "Game.dll");
    private string editorDll => Path.Combine(outputFolder, "GameEditor.dll");
    public Assembly? EditorAssembly { get; private set; }

    // 起動時 : ソースより新しい DLL が無ければビルドして待つ。読み込む
    public bool BuildAndLoadBlocking()
    {
        if (!Available) { Console.WriteLine($"[Scripts] No game project : {_project}"); return false; }
        // ソースとフォルダの一番新しい日時 (フォルダの日時はファイルを足す / 消すと変わる : 消しただけの時もビルドし直すため)
        bool source(string path) => !path.Contains($"{Path.DirectorySeparatorChar}obj") && !path.Contains($"{Path.DirectorySeparatorChar}bin");
        var newest = Directory.EnumerateFiles(_folder, "*.cs*", SearchOption.AllDirectories).Where(source).Select(File.GetLastWriteTimeUtc)
            .Concat(Directory.EnumerateDirectories(_folder, "*", SearchOption.AllDirectories).Where(source).Select(Directory.GetLastWriteTimeUtc))
            .Append(Directory.GetLastWriteTimeUtc(_folder))
            .DefaultIfEmpty(DateTime.MinValue).Max();
        var built = hasEditorProject ? editorDll : outputDll;
        if (!File.Exists(built) || !File.Exists(outputDll) || File.GetLastWriteTimeUtc(built) < newest)
        {
            var (ok, log, ms) = runBuild();
            LastBuildMs = ms;
            if (!ok) { report(log); return false; }
            Console.WriteLine($"[Scripts] Built Game.dll ({ms:F0} ms)");
        }
        Load();
        return true;
    }

    public void RequestBuild() => _changed = true;

    // 毎フレーム (メインのスレッド) : 保存から 0.5 秒後にビルドを始め、終わったら BuildSucceeded
    public void Update()
    {
        if (_changed) { _changed = false; _changedAt = Time.TotalTime; }
        if (_changedAt > 0 && Time.TotalTime - _changedAt > 0.5 && _build == null)
        {
            _changedAt = 0;
            Console.WriteLine("[Scripts] Compiling...");
            _build = Task.Run(runBuild);
        }
        if (_build is { IsCompleted: true } done)
        {
            _build = null;
            var (ok, log, ms) = done.Result;
            LastBuildMs = ms;
            if (!ok) { report(log); return; }
            LastError = null;
            Console.WriteLine($"[Scripts] Built Game.dll ({ms:F0} ms)");
            BuildSucceeded?.Invoke(this);
        }
    }

    private (bool Ok, string Log, double Ms) runBuild()
    {
        var start = Stopwatch.GetTimestamp();
        try
        {
            // エディターの拡張は、今動いているエディターの GameEngine.Editor.dll / ImGui.NET.dll を参照してビルドする
            var target = hasEditorProject ? _editorProject : _project;
            var editorDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var info = new ProcessStartInfo("dotnet")
            {
                RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
                WorkingDirectory = _folder,
            };
            foreach (var argument in new[] { "build", target, "-c", "Debug", "-nologo", "-v", "q", "-clp:NoSummary", $"-p:EditorAssemblyDir={editorDir}" })
                info.ArgumentList.Add(argument);   // 引数は 1 つずつ渡す (空白や \ を含むパスでも引用符を気にしなくてよい)
            using var process = Process.Start(info)!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode == 0, output.Result + error, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
        catch (Exception ex)
        {
            return (false, $"dotnet build could not run : {ex.Message}", Stopwatch.GetElapsedTime(start).TotalMilliseconds);
        }
    }

    private void report(string log)
    {
        var errors = log.Split('\n').Select(l => l.Trim()).Where(l => l.Contains(": error ")).Distinct().ToList();
        LastError = errors.FirstOrDefault() ?? log.Trim();
        Console.WriteLine($"[Scripts] Build failed ({errors.Count} errors). The previous Game.dll is kept.");
        foreach (var line in errors.Take(20)) Console.WriteLine($"  {line}");
        if (errors.Count == 0) Console.WriteLine(log);
    }

    // ---------------------------------------------------------------------
    // 読み込み / 捨てる
    // ---------------------------------------------------------------------

    // 新しい Game.dll を読み、[GameSystem] を作る (前の物は Unload してから呼ぶこと)
    public void Load()
    {
        var dll = File.ReadAllBytes(outputDll);
        var pdbPath = Path.ChangeExtension(outputDll, ".pdb");
        var pdb = File.Exists(pdbPath) ? File.ReadAllBytes(pdbPath) : null;
        _context = new GameLoadContext();
        Assembly = pdb != null ? _context.LoadFromStream(new MemoryStream(dll), new MemoryStream(pdb)) : _context.LoadFromStream(new MemoryStream(dll));
        ComponentRegistry.KnownComponents(rescan: true).ToList();   // 新しい型を探す
        _systems = _engine.AddGameSystems(Assembly);
        // エディターの拡張 (ED7 後半)
        if (hasEditorProject && File.Exists(editorDll))
        {
            var editorPdb = Path.ChangeExtension(editorDll, ".pdb");
            EditorAssembly = File.Exists(editorPdb)
                ? _context.LoadFromStream(new MemoryStream(File.ReadAllBytes(editorDll)), new MemoryStream(File.ReadAllBytes(editorPdb)))
                : _context.LoadFromStream(new MemoryStream(File.ReadAllBytes(editorDll)));
            EditorExtensions.Scan(EditorAssembly);
        }
        LoadCount++;
        Console.WriteLine($"[Scripts] Loaded {Assembly.GetName().Name} #{LoadCount} : {Assembly.GetTypes().Count(t => typeof(IComponent).IsAssignableFrom(t))} components, {_systems.Count} systems");
    }

    // 再生を始める時 : [GameSystem] を作り直す (ED8 後半)
    //   前の再生で System の中に残った値 (乱数 / 角度 / フラグなど) を持ち越さない。書き出したゲームは毎回新しい System で始まるので、同じにする
    //   (以前は エディターを起動した時に作った System を使い続け、2 回目からの再生が書き出したゲームと違う動きになることがあった)
    public void RecreateSystems()
    {
        if (Assembly == null) return;
        _engine.RemoveSystems(_systems);
        _systems = _engine.AddGameSystems(Assembly);
    }

    // 今の DLL を捨てる : System を外し、World から古い型の入れ物を外し、型の情報を忘れ、AssemblyLoadContext を捨てる
    // (World のエンティティはそのまま。呼ぶ側が先に World.Clear しておくこと)
    public void Unload(World world)
    {
        if (Assembly == null) return;
        var old = Assembly;
        EditorExtensions.Clear();   // 拡張 (古い型を持っている) を先に捨てる
        if (EditorAssembly != null) ComponentRegistry.Forget(EditorAssembly);   // 型の一覧を作り直す時に、古い GameEditor.dll も見ないように
        EditorAssembly = null;
        _engine.RemoveSystems(_systems);
        _systems.Clear();
        world.RemovePools(t => t.Assembly == old);
        ComponentRegistry.Forget(old);
        Assembly = null;
        _lastContext = new WeakReference(_context);
        _context!.Unload();
        _context = null;
    }

    // 前に捨てた DLL が解放されたか (GC を回して確かめる。確認用)
    public bool? PreviousUnloaded()
    {
        if (_lastContext == null) return null;
        for (int i = 0; i < 10 && _lastContext.IsAlive; i++)
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }
        return !_lastContext.IsAlive;
    }

    public void Dispose() => _watcher?.Dispose();

    // ゲームの DLL を読む場所。エンジンなど、エディターが読み込んだ物はそれを使う (Load が null を返すと既定の場所から探す)
    private sealed class GameLoadContext : AssemblyLoadContext
    {
        public GameLoadContext() : base("Game", isCollectible: true) { }
        // GameEditor.dll が参照する Game.dll は、この中に読んだ物を使う。
        // 読んだアセンブリはフィールドに持たず、Assemblies から探す (以前はフィールドに持っていたので、
        // 捨てた AssemblyLoadContext が解放されなかった : 読み直すたびに古い Game.dll / GameEditor.dll が残った)
        protected override Assembly? Load(AssemblyName name) =>
            name.Name == "Game" ? Assemblies.FirstOrDefault(a => a.GetName().Name == "Game") : null;
    }
}
