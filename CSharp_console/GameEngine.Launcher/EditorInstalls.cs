using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace GameEngine.Launcher;

// ==========================================================================
// エディターの版 (ED9)
//   見つけ方 :
//     Installed   : 入れた版のフォルダ (InstallRoot/<版>_<コミット>/Editor/GameEngine.Editor.exe)。ランチャーの「Install Current Build」で作る
//     Development : リポジトリのビルド (CSharp_console/GameEngine.Editor/bin/x64/Release|Debug/...)。ビルドし直すと中身が変わる
//     Added       : 手で足した GameEngine.Editor.exe (どこにあってもよい)
//   版 : GameEngine.Editor.dll のファイルの版 (ProductVersion = "0.9.0+コミット")。エディターの csproj の <Version>
//   入れた版のフォルダ : Editor (エディターの出力を全部) / Shader (エディターは作業フォルダの ../Shader を読む) /
//                        Player (ビルド済み。リポジトリの外でもゲームを書き出せる)
// ==========================================================================
internal enum InstallKind { Installed, Development, Added }

internal sealed record EditorInstall(string Exe, string Version, string Commit, InstallKind Kind, string Label)
{
    public string Folder => Path.GetDirectoryName(Exe)!;
    public string Display => $"{Version}{(Commit.Length > 0 ? $" ({Commit[..Math.Min(7, Commit.Length)]})" : "")}  {Label}";

    // 版を比べる ("0.10.0" > "0.9.0"。読めない物は 0)
    public System.Version Number => System.Version.TryParse(Version, out var v) ? v : new System.Version(0, 0);

    public static EditorInstall? Read(string exe, InstallKind kind, string label)
    {
        var dll = Path.Combine(Path.GetDirectoryName(exe)!, "GameEngine.Editor.dll");
        if (!File.Exists(exe) || !File.Exists(dll)) return null;
        var product = FileVersionInfo.GetVersionInfo(dll).ProductVersion ?? "0.0.0";
        var parts = product.Split('+');
        return new EditorInstall(Path.GetFullPath(exe), parts[0], parts.Length > 1 ? parts[1] : "", kind, label);
    }
}

// ランチャーの設定 (プロジェクトの一覧 / 手で足したエディター)。<データのフォルダ>/launcher.json
internal sealed class LauncherSettings
{
    public sealed class ProjectEntry
    {
        public string    Path       { get; set; } = "";
        public string?   Editor     { get; set; }   // 最後に選んだエディターの exe
        public DateTime? LastOpened { get; set; }
    }

    public List<ProjectEntry> Projects { get; set; } = new();
    public List<string> AddedEditors { get; set; } = new();
    public bool CloseAfterLaunch { get; set; }

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    public static LauncherSettings Load(string file)
    {
        try { return File.Exists(file) ? JsonSerializer.Deserialize<LauncherSettings>(File.ReadAllText(file, Encoding.UTF8), JsonOptions) ?? new() : new(); }
        catch (JsonException) { return new(); }
    }
    public void Save(string file)
    {
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file)!);
        File.WriteAllText(file, JsonSerializer.Serialize(this, JsonOptions), new UTF8Encoding(false));
    }
}

internal sealed class EditorInstalls
{
    public string DataFolder  { get; }   // 設定と入れた版の置き場
    public string InstallRoot => Path.Combine(DataFolder, "Editors");
    public string SettingsFile => Path.Combine(DataFolder, "launcher.json");
    public string? RepoRoot   { get; }   // ランチャーのあるリポジトリ (開発版を探す)
    public LauncherSettings Settings { get; private set; }
    public List<EditorInstall> All { get; private set; } = new();

    public EditorInstalls(string dataFolder)
    {
        DataFolder = Path.GetFullPath(dataFolder);
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "GameEngine.sln"))) { RepoRoot = dir.FullName; break; }
        Settings = LauncherSettings.Load(SettingsFile);
        // 初めて : リポジトリの見本のプロジェクトを一覧に入れる
        if (!File.Exists(SettingsFile) && RepoRoot != null && Directory.Exists(Path.Combine(RepoRoot, "CSharp_console", "Project")))
            Settings.Projects.Add(new LauncherSettings.ProjectEntry { Path = Path.Combine(RepoRoot, "CSharp_console", "Project") });
        Refresh();
    }

    public void Save() => Settings.Save(SettingsFile);

    // 探し直す (新しい版が上)
    public void Refresh()
    {
        var found = new List<EditorInstall>();
        if (Directory.Exists(InstallRoot))
        {
            foreach (var dir in Directory.EnumerateDirectories(InstallRoot))
                if (EditorInstall.Read(Path.Combine(dir, "Editor", "GameEngine.Editor.exe"), InstallKind.Installed, "Installed") is { } install) found.Add(install);
        }
        if (RepoRoot != null)
        {
            foreach (var configuration in new[] { "Release", "Debug" })
            {
                var exe = Path.Combine(RepoRoot, "CSharp_console", "GameEngine.Editor", "bin", "x64", configuration, "net9.0-windows", "GameEngine.Editor.exe");
                if (EditorInstall.Read(exe, InstallKind.Development, $"Development ({configuration})") is { } install) found.Add(install);
            }
        }
        foreach (var exe in Settings.AddedEditors)
        {
            if (found.Any(f => string.Equals(f.Exe, Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase))) continue;
            if (EditorInstall.Read(exe, InstallKind.Added, "Added") is { } install) found.Add(install);
        }
        All = found.OrderByDescending(i => i.Number).ThenBy(i => i.Kind).ThenBy(i => i.Label, StringComparer.Ordinal).ToList();
    }

    public EditorInstall? Find(string? exe) =>
        exe == null ? null : All.FirstOrDefault(i => string.Equals(i.Exe, Path.GetFullPath(exe), StringComparison.OrdinalIgnoreCase));

    // プロジェクトを開いた版 (エディターが書いた ProjectVersion.txt の "EditorVersion: 0.9.0")
    public static string? ProjectVersion(string project)
    {
        var file = Path.Combine(project, "ProjectVersion.txt");
        if (!File.Exists(file)) return null;
        foreach (var line in File.ReadLines(file))
            if (line.StartsWith("EditorVersion:")) return line["EditorVersion:".Length..].Trim();
        return null;
    }

    // プロジェクトに合う版 : 前に選んだ物 → 同じ版 (入れた版を先に) → 一番新しい物
    public EditorInstall? Suggest(LauncherSettings.ProjectEntry project)
    {
        if (Find(project.Editor) is { } chosen) return chosen;
        var version = ProjectVersion(project.Path);
        return All.Where(i => i.Version == version).OrderBy(i => i.Kind).FirstOrDefault() ?? All.FirstOrDefault();
    }

    // ---------------------------------------------------------------------
    // 起動
    // ---------------------------------------------------------------------

    public Process Launch(EditorInstall install, string? project, IEnumerable<string>? extraArguments = null)
    {
        var info = new ProcessStartInfo(install.Exe) { WorkingDirectory = install.Folder, UseShellExecute = false };
        if (project != null) { info.ArgumentList.Add("--project"); info.ArgumentList.Add(project); }
        foreach (var argument in extraArguments ?? Array.Empty<string>()) info.ArgumentList.Add(argument);
        var process = Process.Start(info) ?? throw new InvalidOperationException($"Cannot start {install.Exe}");
        if (project != null)
        {
            var entry = Settings.Projects.FirstOrDefault(p => string.Equals(p.Path, project, StringComparison.OrdinalIgnoreCase));
            if (entry == null) Settings.Projects.Add(entry = new LauncherSettings.ProjectEntry { Path = project });
            entry.Editor = install.Exe;
            entry.LastOpened = DateTime.Now;
            Save();
        }
        return process;
    }

    // ---------------------------------------------------------------------
    // 入れる / 外す
    // ---------------------------------------------------------------------

    /// <summary>
    /// 開発版 (リポジトリの Release のビルド) を、版の名前のフォルダに写して入れる。Player も Release でビルドして入れる
    /// 戻り値 = 入れた版。同じ版とコミットが入っていれば入れ直す
    /// </summary>
    public EditorInstall Install(EditorInstall source, Action<string>? progress = null)
    {
        if (RepoRoot == null) throw new InvalidOperationException("The engine repository was not found");
        var name = source.Commit.Length > 0 ? $"{source.Version}_{source.Commit[..Math.Min(7, source.Commit.Length)]}" : source.Version;
        var target = Path.Combine(InstallRoot, name);
        var staging = target + ".installing";
        if (Directory.Exists(staging)) Directory.Delete(staging, true);

        // Player (ゲームの書き出しに使う。リポジトリの外のエディターはビルドできないので、ビルド済みを入れる)
        progress?.Invoke("Building the player...");
        var playerProject = Path.Combine(RepoRoot, "CSharp_console", "GameEngine.Player", "GameEngine.Player.csproj");
        var playerOutput = Path.Combine(Path.GetDirectoryName(playerProject)!, "bin", "Release", "net9.0-windows");
        if (File.Exists(playerProject))
        {
            var info = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
            foreach (var a in new[] { "build", playerProject, "-c", "Release", "-nologo", "-v", "q" }) info.ArgumentList.Add(a);
            using var build = Process.Start(info)!;
            var output = build.StandardOutput.ReadToEndAsync();
            var error = build.StandardError.ReadToEnd();
            build.WaitForExit();
            if (build.ExitCode != 0) throw new InvalidOperationException("Player build failed :\n" + output.Result + error);
        }

        progress?.Invoke("Copying...");
        copyFolder(source.Folder, Path.Combine(staging, "Editor"), file => !Path.GetFileName(file).Equals("imgui.ini", StringComparison.OrdinalIgnoreCase));
        var shader = Path.GetFullPath(Path.Combine(source.Folder, "..", "Shader"));
        if (Directory.Exists(shader)) copyFolder(shader, Path.Combine(staging, "Shader"), _ => true);
        if (Directory.Exists(playerOutput)) copyFolder(playerOutput, Path.Combine(staging, "Player"), file => !Path.GetFileName(file).Equals("Player.log", StringComparison.OrdinalIgnoreCase));

        // 入れ替え (途中で失敗しても、前に入れた物を壊さない)
        if (Directory.Exists(target)) Directory.Delete(target, true);
        Directory.Move(staging, target);
        Refresh();
        return Find(Path.Combine(target, "Editor", "GameEngine.Editor.exe")) ?? throw new InvalidOperationException("Installed editor not found");
    }

    /// <summary>入れた版を消す (Installed だけ) / 足した版を一覧から外す (Added。ファイルは消さない)</summary>
    public void Remove(EditorInstall install)
    {
        if (install.Kind == InstallKind.Installed)
        {
            var root = Path.GetFullPath(Path.Combine(install.Folder, ".."));
            if (!root.StartsWith(InstallRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) throw new InvalidOperationException("Not an installed version");
            Directory.Delete(root, true);
        }
        else if (install.Kind == InstallKind.Added)
        {
            Settings.AddedEditors.RemoveAll(e => string.Equals(Path.GetFullPath(e), install.Exe, StringComparison.OrdinalIgnoreCase));
            Save();
        }
        Refresh();
    }

    public void AddEditor(string exe)
    {
        if (!Settings.AddedEditors.Contains(exe, StringComparer.OrdinalIgnoreCase)) Settings.AddedEditors.Add(exe);
        Save();
        Refresh();
    }

    private static void copyFolder(string source, string target, Func<string, bool> include)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories).Where(include))
        {
            var copy = Path.Combine(target, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(copy)!);
            File.Copy(file, copy, true);
        }
    }
}
