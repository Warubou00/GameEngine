using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace GameEngine.Editor;

// ==========================================================================
// ゲームの書き出し (ED8 後半)。File > Build Game
//   Player の exe + エンジンの DLL + ゲームのコード (Game.dll) + 入れるシーン + 使っているアセットだけを 1 つのフォルダにまとめる
//   エディターの物 (GameEngine.Editor / ImGui / GameEditor.dll / Scripts/Editor) は入れない (最後に確かめる)
//   手順 : 1. 使っているアセットを探す (AssetDependencies。メインのスレッド)
//          2. Player と Game を Release でビルド (dotnet build。別のスレッド)
//          3. 書き出し先を空にして写す (前に書き出したフォルダ (Data/player.json がある) か、空のフォルダだけ。他の物は消さない)
//   書き出したフォルダ : <名前>.exe ... / Shader / Data (player.json / ProjectSettings.json / Assets) /
//                        dotnet (.NET の動かすだけの物。exe はまずここを探す。.NET を入れていない PC でも動かすため。Package 化で足した)
//   v1.3 : Data / Shader / Game.dll を Data.pak (独自の形。Brotli で詰めて混ぜる。Pak.cs) にまとめる (PackAssets)。
//          .NET は使わない物 (WPF / 言語ごとの文字 / デバッグの道具) を入れない (TrimDotnet)。exe のアイコンは Project Settings の Icon
// ==========================================================================

// 書き出しの設定 (プロジェクトのフォルダの BuildSettings.json に覚える)
internal sealed class BuildSettings
{
    public const string FileName = "BuildSettings.json";

    public string       ProductName  { get; set; } = "Game";
    public int          Width        { get; set; } = 1280;
    public int          Height       { get; set; } = 720;
    public bool         VSync        { get; set; } = true;
    public bool         ShowProfiler { get; set; }
    public bool         RunInBackground { get; set; }            // v1.8 : 後ろにある時も動かすか (player.json へ)
    public List<string> Scenes      { get; set; } = new();   // 入れるシーン (アセットのパス。0 番が最初のシーン)
    public string       OutputFolder { get; set; } = "Build"; // プロジェクトのフォルダからの相対パスか絶対パス
    public bool         DebugSymbols { get; set; }            // .pdb も入れるか
    public bool         IncludeDotnet { get; set; } = true;   // .NET (動かすだけの物) を exe の隣の dotnet に入れるか (.NET を入れていない PC でも動く。約 100 MB)
    public bool         PackAssets    { get; set; } = true;   // v1.3 : アセット / シェーダー / Game.dll を Data.pak にまとめる (小さく、外から読みにくく)
    public bool         TrimDotnet    { get; set; } = true;   // v1.3 : .NET から使わない物 (WPF / 言語ごとの文字 / デバッグの道具) を外す

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static BuildSettings Load(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<BuildSettings>(File.ReadAllText(file, Encoding.UTF8), JsonOptions) ?? new() : new(); }
        catch (JsonException ex) { Console.WriteLine($"[Build] Broken {file} : {ex.Message} (using defaults)"); return new(); }
    }
    public void Save(string file) => File.WriteAllText(file, JsonSerializer.Serialize(this, JsonOptions), new UTF8Encoding(false));
}

internal sealed class BuildResult
{
    public bool    Ok;
    public string? Error;
    public string  OutputFolder = "";
    public string  ExePath = "";
    public int     AssetCount;      // 入れたアセットの数 (.meta とキャッシュを除く)
    public int     FileCount;       // 書き出した全部のファイルの数
    public int     ShaderCacheFiles;   // 入れたコンパイル済みのシェーダーの数
    public long    Bytes;
    public long    AssetBytes;
    public long    PakBytes, PakOriginalBytes;   // v1.3 : Data.pak の大きさ / 詰める前の合計 (同じ物を 1 つにする前)
    public int     PakFiles, PakUniqueFiles;
    public double  PackMs;
    public long    DotnetBytes;                  // 入れた .NET の大きさ
    public string? IconFile;                     // exe に付けたアイコン (Project Settings の Icon。無ければ null)
    public double  CompileMs, CopyMs, TotalMs;
    public List<string> Assets = new();
    public List<string> Warnings = new();
}

internal sealed class GameBuilder
{
    private readonly AssetDatabase _database;
    public string? PlayerProject { get; }   // GameEngine.Player.csproj (エンジンのリポジトリの中)
    // ビルド済みの Player (ランチャーが入れた版のエディター : <版>/Editor の隣の <版>/Player。ED9)。リポジトリの外でも書き出せる
    public string? PrebuiltPlayer { get; }
    public bool    CanBuild => PlayerProject != null || PrebuiltPlayer != null;
    public string  GameProject  { get; }    // Project/Scripts/Game.csproj

    // エディターの物 (書き出したフォルダにあってはいけない名前)
    private static readonly string[] EditorOnly = { "GameEngine.Editor", "ImGui", "cimgui", "GameEditor" };

    public GameBuilder(AssetDatabase database)
    {
        _database = database;
        GameProject = Path.Combine(database.ProjectRoot, "Scripts", "Game.csproj");
        // エディターの exe から上に辿って CSharp_console/GameEngine.Player を探す (Package のエディターは探さない : 同梱のビルド済みの Player を使う)
        for (var dir = DotnetTool.IsPackaged ? null : new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "CSharp_console", "GameEngine.Player", "GameEngine.Player.csproj");
            if (File.Exists(candidate)) { PlayerProject = candidate; break; }
        }
        var prebuilt = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "Player"));
        if (PlayerProject == null && File.Exists(Path.Combine(prebuilt, "GameEngine.Player.exe"))) PrebuiltPlayer = prebuilt;
    }

    public string OutputFolderOf(BuildSettings settings) =>
        Path.GetFullPath(Path.IsPathRooted(settings.OutputFolder) ? settings.OutputFolder : Path.Combine(_database.ProjectRoot, settings.OutputFolder));

    // 1. 使っているアセット (メインのスレッドで。AssetDatabase を読むので)
    public AssetDependencies CollectAssets(BuildSettings settings)
    {
        var dependencies = new AssetDependencies(_database);
        foreach (var scene in settings.Scenes) dependencies.AddScene(scene);
        dependencies.AddScripts(Path.Combine(_database.ProjectRoot, "Scripts"));
        dependencies.AddResourcesFolders();
        return dependencies;
    }

    /// <summary>書き出す (別のスレッドで動かせる。dependencies は CollectAssets の結果)</summary>
    public BuildResult Build(BuildSettings settings, AssetDependencies dependencies, Action<string>? progress = null)
    {
        var start = Stopwatch.GetTimestamp();
        var result = new BuildResult { OutputFolder = OutputFolderOf(settings) };
        result.Warnings.AddRange(dependencies.Warnings);
        BuildResult fail(string error) { result.Ok = false; result.Error = error; result.TotalMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds; return result; }

        if (settings.Scenes.Count == 0) return fail("No scenes to build (add at least one)");
        foreach (var scene in settings.Scenes)
            if (!File.Exists(_database.FullPath(scene))) return fail($"Scene not found : {scene}");
        if (!CanBuild) return fail("GameEngine.Player not found (run the editor from the engine repository, or install it with the launcher)");
        var output = result.OutputFolder;
        var projectRoot = Path.GetFullPath(_database.ProjectRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if ((output + Path.DirectorySeparatorChar).StartsWith(Path.Combine(projectRoot, "Assets") + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) ||
            projectRoot.StartsWith(output + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            return fail($"The output folder must not be inside Assets or contain the project : {output}");

        // ---------- 2. ビルド (Release) ----------
        progress?.Invoke("Compiling Player...");
        var compile = Stopwatch.GetTimestamp();
        var (ok, log) = PlayerProject != null ? dotnetBuild(PlayerProject) : (true, "");   // ビルド済みの Player はそのまま使う
        if (!ok) return fail("Player build failed :\n" + errorsOf(log));
        var playerOutput = PlayerProject != null ? Path.Combine(Path.GetDirectoryName(PlayerProject)!, "bin", "Release", "net9.0-windows") : PrebuiltPlayer!;
        string? gameOutput = null;
        if (File.Exists(GameProject))
        {
            progress?.Invoke("Compiling Game.dll...");
            (ok, log) = dotnetBuild(GameProject);
            if (!ok) return fail("Game.dll build failed :\n" + errorsOf(log));
            gameOutput = Path.Combine(Path.GetDirectoryName(GameProject)!, "bin", "Release", "net9.0-windows");
        }
        else result.Warnings.Add("No Scripts/Game.csproj (no game code)");
        result.CompileMs = Stopwatch.GetElapsedTime(compile).TotalMilliseconds;
        if (!File.Exists(Path.Combine(playerOutput, "RHI.dll"))) return fail("RHI.dll (Release) is missing. Build the C++ solution (GameEngine.sln) in Release first.");

        // ---------- 3. 写す ----------
        progress?.Invoke("Copying...");
        var copy = Stopwatch.GetTimestamp();
        if (prepareOutput(output) is { } error) return fail(error);
        bool wanted(string file) => settings.DebugSymbols || !file.EndsWith(".pdb", StringComparison.OrdinalIgnoreCase);

        // Player (exe の名前はゲームの名前に。中の GameEngine.Player.dll は同じ名前のまま)
        var exeName = safeFileName(settings.ProductName) + ".exe";
        foreach (var file in Directory.EnumerateFiles(playerOutput).Where(wanted))
        {
            var name = Path.GetFileName(file);
            if (name.Equals("Player.log", StringComparison.OrdinalIgnoreCase)) continue;
            copyFile(file, Path.Combine(output, name.Equals("GameEngine.Player.exe", StringComparison.OrdinalIgnoreCase) ? exeName : name));
        }
        result.ExePath = Path.Combine(output, exeName);
        // .NET (エディターが今使っている物と同じ版。Player の exe は隣の dotnet を先に探す)
        if (settings.IncludeDotnet)
        {
            progress?.Invoke("Copying .NET...");
            if (copyDotnetRuntime(Path.Combine(output, "dotnet"), settings.TrimDotnet) is { } dotnetWarning) result.Warnings.Add(dotnetWarning);
            result.DotnetBytes = Directory.Exists(Path.Combine(output, "dotnet")) ? Directory.EnumerateFiles(Path.Combine(output, "dotnet"), "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) : 0;
        }
        // ゲームのコード (Game.dll と、ゲームが使うライブラリ。エンジンは Player の物を使う)
        if (gameOutput != null)
        {
            foreach (var file in Directory.EnumerateFiles(gameOutput).Where(wanted))
            {
                var name = Path.GetFileName(file);
                if (name.StartsWith("GameEngine.", StringComparison.OrdinalIgnoreCase) || name.EndsWith(".deps.json", StringComparison.OrdinalIgnoreCase)) continue;
                copyFile(file, Path.Combine(output, name));
            }
        }
        // シェーダー (Player の出力の ../Shader)
        var shaders = Directory.EnumerateFiles(Path.Combine(playerOutput, "..", "Shader"), "*.shader").ToList();
        foreach (var shader in shaders) copyFile(shader, Path.Combine(output, "Shader", Path.GetFileName(shader)));
        // コンパイル済みのシェーダー : エディターの ShaderCache から、今のシェーダーのソースの物だけ
        //   (無いと書き出したゲームの初めの起動で全部コンパイルする : 計測で 2.3 秒)
        //   Player は Release の C++ を使うので、Debug のエディター (キャッシュは Debug 用) の時は Release の見本の出力のキャッシュを見る
        var cacheFolder = Path.Combine(Environment.CurrentDirectory, "ShaderCache");
        var sep = Path.DirectorySeparatorChar;
        var releaseCache = cacheFolder.Replace($"{sep}Debug{sep}", $"{sep}Release{sep}");
        result.ShaderCacheFiles = copyShaderCache(Directory.Exists(releaseCache) ? releaseCache : cacheFolder, Path.Combine(output, "Data", "ShaderCache"), shaders);

        // Data : player.json / ProjectSettings.json / アセット
        var data = Path.Combine(output, "Data");
        Directory.CreateDirectory(data);
        new PlayerSettings
        {
            ProductName = settings.ProductName, Width = settings.Width, Height = settings.Height, VSync = settings.VSync,
            ShowProfiler = settings.ShowProfiler, RunInBackground = settings.RunInBackground, StartScene = settings.Scenes[0], Scenes = settings.Scenes.ToList(),
        }.Save(Path.Combine(data, PlayerSettings.FileName));
        var projectSettings = Path.Combine(_database.ProjectRoot, ProjectSettings.FileName);
        if (File.Exists(projectSettings)) copyFile(projectSettings, Path.Combine(data, ProjectSettings.FileName));

        var folders = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var asset in dependencies.Assets.OrderBy(a => a, StringComparer.OrdinalIgnoreCase))
        {
            var source = _database.FullPath(asset);
            var target = Path.Combine(data, asset.Replace('/', Path.DirectorySeparatorChar));
            copyFile(source, target);
            result.AssetBytes += new FileInfo(source).Length;
            if (File.Exists(source + ".meta")) copyFile(source + ".meta", target + ".meta");   // Id (シーンは guid でアセットを探す)
            result.Assets.Add(asset);
            // 親のフォルダの .meta (無いと Player が新しい Id を作って書く)
            for (var folder = Path.GetDirectoryName(asset.Replace('/', Path.DirectorySeparatorChar)); !string.IsNullOrEmpty(folder) && folders.Add(folder); folder = Path.GetDirectoryName(folder))
            {
                var meta = Path.Combine(_database.ProjectRoot, folder + ".meta");
                if (File.Exists(meta)) copyFile(meta, Path.Combine(data, folder + ".meta"));
            }
        }
        foreach (var cache in dependencies.CacheFiles)
        {
            if (_database.ToAssetPath(cache) is { } path) copyFile(cache, Path.Combine(data, path.Replace('/', Path.DirectorySeparatorChar)));
        }
        // エンジンが作った 3D ノイズのキャッシュ (SH3。builtin:PerlinWorley3D など)。無いとゲームの初めの起動で作り直す (128^3 で約 2 秒)
        var noiseCache = Path.Combine(_database.ProjectRoot, "Library", "Noise3D");
        if (Directory.Exists(noiseCache))
        {
            foreach (var file in Directory.EnumerateFiles(noiseCache, "*.bin"))
                copyFile(file, Path.Combine(data, "Library", "Noise3D", Path.GetFileName(file)));
        }
        result.AssetCount = result.Assets.Count;

        // ---------- v1.3 : exe のアイコン (Project Settings の Icon) ----------
        if (File.Exists(projectSettings) && ProjectSettings.Load(projectSettings).Icon is { Length: > 0 } icon)
        {
            progress?.Invoke("Setting the icon...");
            var iconFile = _database.FullPath(icon);
            if (!File.Exists(iconFile)) result.Warnings.Add($"Icon not found : {icon}");
            else if (ExeIcon.Apply(result.ExePath, iconFile) is { } iconError) result.Warnings.Add($"Icon : {iconError}");
            else result.IconFile = icon;
        }

        // ---------- v1.3 : Data / Shader / Game.dll を Data.pak にまとめる ----------
        if (settings.PackAssets)
        {
            progress?.Invoke("Packing...");
            var packed = new List<(string Name, string File)>();
            foreach (var folder in new[] { "Data", "Shader" })
            {
                var root = Path.Combine(output, folder);
                if (!Directory.Exists(root)) continue;
                foreach (var file in Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories))
                    packed.Add((Path.GetRelativePath(output, file).Replace('\\', '/'), file));
            }
            var gameDll = Path.Combine(output, "Game.dll");
            if (File.Exists(gameDll)) packed.Add(("Game.dll", gameDll));
            Pak.WriteResult pak;
            try { pak = Pak.Write(Path.Combine(output, Pak.FileName), packed); }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or AggregateException or UnauthorizedAccessException)
            {
                return fail($"Cannot write {Pak.FileName} : {ex.Message}");   // 書き出しを止めるだけ (以前の試しでは例外でエディターが落ちた)
            }
            foreach (var folder in new[] { "Data", "Shader" }) if (Directory.Exists(Path.Combine(output, folder))) Directory.Delete(Path.Combine(output, folder), true);
            if (File.Exists(gameDll)) File.Delete(gameDll);
            (result.PakBytes, result.PakOriginalBytes, result.PakFiles, result.PakUniqueFiles, result.PackMs) = (pak.PakBytes, pak.OriginalBytes, pak.Files, pak.UniqueFiles, pak.Milliseconds);
        }
        result.CopyMs = Stopwatch.GetElapsedTime(copy).TotalMilliseconds;

        // ---------- 4. 確かめる : エディターの物が入っていない ----------
        var files = Directory.EnumerateFiles(output, "*", SearchOption.AllDirectories).ToList();
        result.FileCount = files.Count;
        result.Bytes = files.Sum(f => new FileInfo(f).Length);
        var leaked = files.Where(f => EditorOnly.Any(e => Path.GetFileName(f).StartsWith(e, StringComparison.OrdinalIgnoreCase))).ToList();
        if (leaked.Count > 0) return fail($"Editor files in the build : {string.Join(", ", leaked.Select(Path.GetFileName))}");

        result.Ok = true;
        result.TotalMs = Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        if (settings.PackAssets)
            Console.WriteLine($"[Build] Data.pak : {result.PakFiles} files ({result.PakUniqueFiles} unique), {result.PakOriginalBytes / 1024.0 / 1024.0:F1} MB -> {result.PakBytes / 1024.0 / 1024.0:F1} MB, {result.PackMs:F0} ms");
        Console.WriteLine($"[Build] {settings.ProductName} -> {output} : {result.AssetCount} assets ({result.AssetBytes / 1024.0 / 1024.0:F1} MB), " +
                          $"{result.FileCount} files ({result.Bytes / 1024.0 / 1024.0:F1} MB), {result.ShaderCacheFiles} compiled shaders, compile {result.CompileMs:F0} ms, copy {result.CopyMs:F0} ms");
        foreach (var warning in result.Warnings) Console.WriteLine($"[Build] warning : {warning}");
        return result;
    }

    // 書き出し先を空にする。前に書き出したフォルダ (Data/player.json がある) か空のフォルダだけ (他のフォルダを間違って消さない)
    private static string? prepareOutput(string output)
    {
        if (Directory.Exists(output) && Directory.EnumerateFileSystemEntries(output).Any())
        {
            if (!File.Exists(Path.Combine(output, "Data", PlayerSettings.FileName)) && !File.Exists(Path.Combine(output, Pak.FileName)))   // v1.3 : まとめた物も前の書き出し
                return $"The output folder is not empty and is not an earlier build : {output}";
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(output)) Directory.Delete(dir, true);
                foreach (var file in Directory.EnumerateFiles(output)) File.Delete(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                return $"Cannot clean the output folder (is the game still running?) : {ex.Message}";
            }
        }
        Directory.CreateDirectory(output);
        return null;
    }

    // ShaderCache の名前 : シェーダー_入り口_版_ハッシュ.bin。ハッシュは C++ (RHI の ShaderCompiler::Compile) と同じ計算で、
    //   FNV-1a 64bit (ソース → 入り口 → 版 → "DXC" / "FXC" → "release" と終わりの 0)。今のソースから計算した値と同じ物だけを写す
    //   (以前の試し : シェーダーごとに一番新しいファイルを写していたが、シェーダーの読み直しを試した後は古いソースの物を選んでいた)
    //   戻り値 = 写した数
    private static int copyShaderCache(string source, string target, IReadOnlyList<string> shaderFiles)
    {
        if (!Directory.Exists(source)) return 0;
        var name = new System.Text.RegularExpressions.Regex("^(?<entry>.+)_(?<target>(?:vs|ps|cs)_[0-9]+_[0-9]+)_(?<hash>[0-9a-f]{16})$");
        var count = 0;
        foreach (var shader in shaderFiles)
        {
            var stem = Path.GetFileNameWithoutExtension(shader);
            var sourceHash = fnv1a(File.ReadAllBytes(shader));
            foreach (var file in Directory.EnumerateFiles(source, stem + "_*.bin"))
            {
                var match = name.Match(Path.GetFileNameWithoutExtension(file)[(stem.Length + 1)..]);
                if (!match.Success) continue;
                var key = fnv1a(Encoding.ASCII.GetBytes(match.Groups["target"].Value), fnv1a(Encoding.ASCII.GetBytes(match.Groups["entry"].Value), sourceHash));
                var hash = Convert.ToUInt64(match.Groups["hash"].Value, 16);
                if (hash != fnv1a(ReleaseTag, fnv1a("DXC"u8.ToArray(), key)) && hash != fnv1a(ReleaseTag, fnv1a("FXC"u8.ToArray(), key))) continue;
                copyFile(file, Path.Combine(target, Path.GetFileName(file)));
                count++;
            }
        }
        return count;
    }
    private static readonly byte[] ReleaseTag = "release\0"u8.ToArray();   // C++ の sizeof(BUILD_TAG) は終わりの 0 も入る

    private static ulong fnv1a(byte[] data, ulong hash = 14695981039346656037ul)
    {
        foreach (var b in data) { hash ^= b; hash *= 1099511628211ul; }
        return hash;
    }

    // .NET の動かすだけの物 (host/fxr + shared の Microsoft.NETCore.App と Microsoft.WindowsDesktop.App) を写す
    //   元 : エディターが今動いている .NET (Package では同梱の dotnet、開発では PC の dotnet)。戻り値 = 警告 (写せなかった時)
    // v1.3 : 使わない物 (Player は WinForms だけ。WPF は使わない)。言語ごとの文字 (英語で出る) / デバッグの道具 (ダンプ / デバッガー)
    private static readonly string[] TrimmedFiles =
    {
        "createdump.exe", "mscordbi.dll", "mscordaccore.dll", "mscordaccore_*.dll", "Microsoft.DiaSymReader.Native.*.dll",
        "Presentation*.dll", "PresentationNative_cor3.dll", "wpfgfx_cor3.dll", "PenImc_cor3.dll", "D3DCompiler_47_cor3.dll",
        "ReachFramework.dll", "System.Windows.Controls.Ribbon.dll", "System.Xaml.dll", "UIAutomation*.dll", "System.Printing.dll",
        "DirectWriteForwarder.dll", "WindowsFormsIntegration.dll", "System.Windows.Forms.Design*.dll", "System.Windows.Presentation.dll",
        "System.Windows.Input.Manipulations.dll", "WindowsBase.dll",
    };

    // 外した物を、その .NET の deps.json からも外す (書いてある物が無いと .NET が起動しないため)。戻り値 = 外した大きさ
    private static long trimRuntime(string frameworkFolder)
    {
        long removed = 0;
        var removedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pattern in TrimmedFiles)
            foreach (var file in Directory.EnumerateFiles(frameworkFolder, pattern))
            {
                removed += new FileInfo(file).Length;
                removedNames.Add(Path.GetFileName(file));
                File.Delete(file);
            }
        // 言語ごとのフォルダ (cs / de / ja ...。中は resources.dll だけ)
        foreach (var dir in Directory.EnumerateDirectories(frameworkFolder))
        {
            var files = Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToList();
            if (files.Count == 0 || !files.All(f => f.EndsWith(".resources.dll", StringComparison.OrdinalIgnoreCase))) continue;
            foreach (var f in files) { removed += new FileInfo(f).Length; removedNames.Add($"{Path.GetFileName(dir)}/{Path.GetFileName(f)}"); }
            Directory.Delete(dir, true);
        }
        var deps = Directory.EnumerateFiles(frameworkFolder, "*.deps.json").FirstOrDefault();
        if (deps != null && removedNames.Count > 0)
        {
            var root = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(deps))!;
            foreach (var target in root["targets"]!.AsObject())
                foreach (var library in target.Value!.AsObject())
                    foreach (var group in new[] { "runtime", "native", "resources" })
                        if (library.Value?[group] is System.Text.Json.Nodes.JsonObject assets)
                            foreach (var name in assets.Select(a => a.Key).Where(k => removedNames.Contains(k.Replace("\\", "/")) || removedNames.Contains(Path.GetFileName(k)) && !k.Contains('/')).ToList())
                                assets.Remove(name);
            File.WriteAllText(deps, root.ToJsonString(new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
        }
        return removed;
    }

    private static string? copyDotnetRuntime(string target, bool trim = false)
    {
        var core    = Path.GetDirectoryName(typeof(object).Assembly.Location)!;                    // <root>/shared/Microsoft.NETCore.App/<版>
        var desktop = Path.GetDirectoryName(typeof(System.Windows.Forms.Form).Assembly.Location)!; // <root>/shared/Microsoft.WindowsDesktop.App/<版>
        var root    = Path.GetFullPath(Path.Combine(core, "..", "..", ".."));
        var fxrRoot = Path.Combine(root, "host", "fxr");
        if (!Directory.Exists(fxrRoot) || !File.Exists(Path.Combine(core, "coreclr.dll")))
            return $"The .NET runtime was not copied (not found next to {core}). The game needs .NET 9 installed.";
        // hostfxr は今の .NET と同じ版 (無ければ一番新しい物)
        var version = Path.GetFileName(core);
        var fxr = Directory.Exists(Path.Combine(fxrRoot, version)) ? Path.Combine(fxrRoot, version)
            : Directory.EnumerateDirectories(fxrRoot).OrderByDescending(d => System.Version.TryParse(Path.GetFileName(d), out var v) ? v : new System.Version()).First();
        void copyTree(string from, string to)
        {
            foreach (var file in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
                copyFile(file, Path.Combine(to, Path.GetRelativePath(from, file)));
        }
        copyTree(fxr, Path.Combine(target, "host", "fxr", Path.GetFileName(fxr)));
        copyTree(core, Path.Combine(target, "shared", "Microsoft.NETCore.App", Path.GetFileName(core)));
        copyTree(desktop, Path.Combine(target, "shared", "Microsoft.WindowsDesktop.App", Path.GetFileName(desktop)));
        foreach (var notice in new[] { "LICENSE.txt", "ThirdPartyNotices.txt" })
            if (File.Exists(Path.Combine(root, notice))) copyFile(Path.Combine(root, notice), Path.Combine(target, notice));
        if (trim)
        {
            var saved = trimRuntime(Path.Combine(target, "shared", "Microsoft.NETCore.App", Path.GetFileName(core))) +
                        trimRuntime(Path.Combine(target, "shared", "Microsoft.WindowsDesktop.App", Path.GetFileName(desktop)));
            Console.WriteLine($"[Build] .NET : left out {saved / 1024.0 / 1024.0:F1} MB (WPF / languages / debugging)");
        }
        return null;
    }

    private static void copyFile(string source, string target)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        File.Copy(source, target, true);
    }

    private static string safeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var safe = new string(name.Select(c => invalid.Contains(c) ? '_' : c).ToArray()).Trim();
        return safe.Length > 0 ? safe : "Game";
    }

    // Release でビルド (dotnet は DotnetTool : Package では同梱の SDK。以前は PATH の dotnet)
    private static (bool Ok, string Log) dotnetBuild(string project) => DotnetTool.Build(project, "Release");

    private static string errorsOf(string log)
    {
        var errors = log.Split('\n').Select(l => l.Trim()).Where(l => l.Contains(": error ")).Distinct().Take(10).ToList();
        return errors.Count > 0 ? string.Join("\n", errors) : log.Trim();
    }
}
