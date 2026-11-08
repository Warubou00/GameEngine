using System.Diagnostics;

namespace GameEngine.Launcher;

// ==========================================================================
// ランチャーの自動の確認 (ED9。GameEngine.Launcher.exe --test --data <一時フォルダ>)
//   開発版を見つける → 版の名前のフォルダに入れる → 入れた版のエディターで空のプロジェクトを開いて、N フレーム描いて閉じる →
//   プロジェクトに版が書かれ、ランチャーが覚える → 入れた版を消す
//   入れた版のエディターはリポジトリの外で動く (シェーダー / Player も入れた物を使う) ことの確認も兼ねる
// ==========================================================================
internal static class LauncherTest
{
    private static int _pass, _fail;

    private static void check(bool ok, string name)
    {
        if (ok) _pass++; else _fail++;
        Console.WriteLine($"[Test] {(ok ? "PASS" : "FAIL")} {name}");
    }

    private static bool args_keep;   // --keep

    public static int Run(EditorInstalls installs, Dictionary<string, string> options, bool keep = false)
    {
        args_keep = keep;
        if (!installs.DataFolder.Contains("Temp", StringComparison.OrdinalIgnoreCase) && !options.ContainsKey("--force"))
        {
            Console.WriteLine("[Test] Use --data <a temp folder> (the test installs and removes editors there)");
            return 1;
        }
        var frames = int.Parse(options.GetValueOrDefault("--frames", "120"));

        // ---------- 見つける ----------
        var source = installs.All.FirstOrDefault(i => i.Kind == InstallKind.Development && i.Label.Contains("Release"));
        check(source != null && source.Version.Length > 0 && source.Version != "0.0.0", $"find the development build ({source?.Display ?? "none"})");
        if (source == null) return finish();

        // ---------- 入れる ----------
        var start = Stopwatch.GetTimestamp();
        EditorInstall? installed = null;
        try { installed = installs.Install(source, p => Console.WriteLine($"[Launcher] {p}")); }
        catch (Exception ex) { Console.WriteLine($"[Launcher] {ex.Message}"); }
        var root = installed != null ? Path.GetDirectoryName(installed.Folder)! : "";
        check(installed is { Kind: InstallKind.Installed } && installed.Version == source.Version && installed.Commit == source.Commit &&
              File.Exists(Path.Combine(root, "Shader", "hlslTemplate.shader")) && File.Exists(Path.Combine(root, "Player", "GameEngine.Player.exe")) &&
              !File.Exists(Path.Combine(installed.Folder, "imgui.ini")),
              $"install the build as {installed?.Display} (editor + shaders + player, {Stopwatch.GetElapsedTime(start).TotalSeconds:F1} s, " +
              $"{(installed != null ? Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories).Sum(f => new FileInfo(f).Length) / 1024 / 1024 : 0)} MB)");
        if (installed == null) return finish();
        check(installs.All.Count(i => i.Version == source.Version) >= 2 && installs.All[0].Number >= installs.All[^1].Number, "the list has both versions (newest first)");

        // ---------- 入れた版で開く (リポジトリの外のエディター) ----------
        // v1.3 : New Project... と同じ作り方 (Assets / Scripts の csproj / .gitignore)。入れた版で開くと Main.scene.json ができ、スクリプトがビルドされる
        var project = Path.Combine(installs.DataFolder, "TestProject");
        if (Directory.Exists(project)) Directory.Delete(project, true);
        check(NewProject.Validate(installs.DataFolder, "Bad:Name") != null && NewProject.Validate(installs.DataFolder, "") != null, "new project : names that cannot be folders are refused");
        var created = installs.CreateProject(installs.DataFolder, "TestProject");
        check(created == Path.GetFullPath(project) && File.Exists(Path.Combine(project, "Scripts", "Game.csproj")) &&
              File.Exists(Path.Combine(project, "Scripts", "Editor", "GameEditor.csproj")) && Directory.Exists(Path.Combine(project, "Assets", "Scenes")) &&
              File.Exists(Path.Combine(project, ".gitignore")) && installs.Settings.Projects.Any(p => p.Path == project) &&
              NewProject.Validate(installs.DataFolder, "TestProject") != null,
              "new project : Assets, Scripts (Game / GameEditor csproj), .gitignore, added to the list (the same name is refused next time)");
        start = Stopwatch.GetTimestamp();
        var process = installs.Launch(installed, project, new[] { "--quit-after", frames.ToString() });
        var exited = process.WaitForExit(120_000);
        if (!exited) process.Kill();
        var seconds = Stopwatch.GetElapsedTime(start).TotalSeconds;
        check(exited && process.ExitCode == 0, $"the installed editor opens a project and closes after {frames} frames ({seconds:F1} s, exit {(exited ? process.ExitCode : -1)})");
        check(EditorInstalls.ProjectVersion(project) == installed.Version && Directory.Exists(Path.Combine(project, "Assets")),
              $"the project records the editor version ({EditorInstalls.ProjectVersion(project) ?? "none"})");
        check(File.Exists(Path.Combine(project, "Assets", "Scenes", "Main.scene.json")) &&
              Directory.EnumerateFiles(Path.Combine(project, "Scripts"), "Game.dll", SearchOption.AllDirectories).Any(),
              "new project : the editor makes Main.scene.json and builds the scripts (outside the repository)");
        var entry = installs.Settings.Projects.FirstOrDefault(p => p.Path == project);
        check(entry != null && entry.LastOpened != null && installs.Suggest(entry) == installed, "the launcher remembers the project and its editor");
        if (entry != null)
        {
            var remembered = entry.Editor;
            entry.Editor = null;   // 選んだ版を忘れても、プロジェクトの版 (入れた版を先に) を選ぶ
            check(installs.Suggest(entry)?.Kind == InstallKind.Installed && installs.Suggest(entry)?.Version == installed.Version, "suggest the version the project was opened with");
            entry.Editor = remembered;
            if (!keep) installs.Settings.Projects.Remove(entry);
            installs.Save();
        }

        // --keep : 入れた版を残す (窓の見た目を確かめる時)
        if (args_keep) return finish();

        // ---------- 消す ----------
        installs.Remove(installed);
        check(!Directory.Exists(root) && installs.All.All(i => i.Kind != InstallKind.Installed), "remove the installed version");
        Directory.Delete(project, true);
        return finish();
    }

    private static int finish()
    {
        Console.WriteLine($"[Test] launcher : pass {_pass} / fail {_fail}");
        return _fail == 0 ? 0 : 1;
    }
}
