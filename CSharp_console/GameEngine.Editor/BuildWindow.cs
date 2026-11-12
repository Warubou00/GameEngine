using System.Diagnostics;
using System.Numerics;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// Build Game の窓 (ED8 後半。File > Build Game...)
//   ゲームの名前 / 窓の大きさ / 入れるシーン (上が最初のシーン) / 書き出し先 を決めて「Build」
//   入れるアセットは、窓を開いた時とシーンを変えた時に探し直して一覧に出す (何から使われているかも)
//   ビルドは別のスレッド (エディターは止まらない)。終わったら結果 (数 / 大きさ / 時間 / 注意) を出す
// ==========================================================================
internal sealed class BuildWindow
{
    private readonly AssetDatabase _database;
    private readonly GameBuilder _builder;
    private readonly string _settingsFile;
    private readonly Func<(string? Scene, bool Dirty)> _currentScene;
    public BuildSettings Settings { get; private set; }

    private Task<BuildResult>? _task;
    private volatile string _progress = "";
    public BuildResult? LastResult { get; private set; }
    private bool _runAfterBuild;
    private AssetDependencies? _preview;
    private int _previewVersion = -1;

    public bool IsBuilding => _task != null;
    public GameBuilder Builder => _builder;

    public BuildWindow(AssetDatabase database, Func<(string? Scene, bool Dirty)> currentScene)
    {
        _database = database;
        _builder = new GameBuilder(database);
        _currentScene = currentScene;
        _settingsFile = Path.Combine(database.ProjectRoot, BuildSettings.FileName);
        Settings = BuildSettings.Load(_settingsFile);
        // 初めて : Scenes の全部のシーン (showcase があれば最初)
        if (Settings.Scenes.Count == 0)
            Settings.Scenes = database.Entries.Where(e => e.Kind == AssetKind.Scene).Select(e => e.Path)
                .OrderBy(p => p.EndsWith("showcase.scene.json") ? 0 : 1).ThenBy(p => p, StringComparer.OrdinalIgnoreCase).ToList();
    }

    private void save()
    {
        try { Settings.Save(_settingsFile); }
        catch (IOException ex) { Console.WriteLine($"[Build] Cannot save {_settingsFile} : {ex.Message}"); }
        _previewVersion = -1;
    }

    // アセットの名前が変わった時 (ED10) : 入れるシーンの並びのパスを付け替える
    public void OnAssetRenamed(Func<string, string?> map)
    {
        var changed = false;
        for (int i = 0; i < Settings.Scenes.Count; i++)
            if (map(Settings.Scenes[i]) is { } moved) { Settings.Scenes[i] = moved; changed = true; }
        if (changed && File.Exists(_settingsFile)) save();
        _previewVersion = -1;
    }

    // ---------------------------------------------------------------------
    // 書き出す (窓のボタン / テスト)
    // ---------------------------------------------------------------------

    public void StartBuild(bool run)
    {
        if (_task != null) return;
        save();
        var settings = Settings;
        var dependencies = _builder.CollectAssets(settings);   // AssetDatabase を読むのはメインのスレッドで
        _runAfterBuild = run;
        _progress = "Starting...";
        Console.WriteLine($"[Build] Building {settings.ProductName} ({settings.Scenes.Count} scenes, {dependencies.Assets.Count} assets)...");
        _task = Task.Run(() => _builder.Build(settings, dependencies, p => _progress = p));
    }

    // 毎フレーム : ビルドが終わったら結果を受け取る
    public void Update()
    {
        if (_task is not { IsCompleted: true } done) return;
        _task = null;
        LastResult = done.IsFaulted ? new BuildResult { Error = done.Exception!.GetBaseException().Message } : done.Result;
        if (!LastResult.Ok) { Console.WriteLine($"[Build] Failed : {LastResult.Error}"); return; }
        if (_runAfterBuild) Launch();
    }

    public void Launch()
    {
        if (LastResult is not { Ok: true } result || !File.Exists(result.ExePath)) return;
        Process.Start(new ProcessStartInfo(result.ExePath) { WorkingDirectory = result.OutputFolder, UseShellExecute = false });
        Console.WriteLine($"[Build] Started {result.ExePath}");
    }

    // ---------------------------------------------------------------------
    // 窓
    // ---------------------------------------------------------------------

    public void Draw(ref bool open)
    {
        Update();
        if (!open) return;
        ImGui.SetNextWindowSize(new Vector2(560, 620), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Build Game", ref open)) { ImGui.End(); return; }
        var changed = false;
        ImGui.BeginDisabled(_task != null);

        // ---------- ゲーム ----------
        ImGui.SeparatorText("Player");
        var name = Settings.ProductName;
        if (ImGui.InputText("Product Name", ref name, 64)) { Settings.ProductName = name; changed = true; }
        var size = new[] { Settings.Width, Settings.Height };
        if (ImGui.InputInt2("Resolution", ref size[0])) { Settings.Width = Math.Clamp(size[0], 320, 7680); Settings.Height = Math.Clamp(size[1], 240, 4320); changed = true; }
        var vsync = Settings.VSync;
        if (ImGui.Checkbox("VSync", ref vsync)) { Settings.VSync = vsync; changed = true; }
        ImGui.SameLine();
        var profiler = Settings.ShowProfiler;
        if (ImGui.Checkbox("Show Profiler", ref profiler)) { Settings.ShowProfiler = profiler; changed = true; }
        ImGui.SameLine();
        var background = Settings.RunInBackground;   // v1.8 : 切ると、後ろにある間はゲームを止めて CPU / GPU を休ませる
        if (ImGui.Checkbox("Run In Background", ref background)) { Settings.RunInBackground = background; changed = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Keep running while another application is in front (off = pause to save power)");
        ImGui.SameLine();
        var symbols = Settings.DebugSymbols;
        if (ImGui.Checkbox("Debug Symbols (.pdb)", ref symbols)) { Settings.DebugSymbols = symbols; changed = true; }
        ImGui.SameLine();
        var dotnet = Settings.IncludeDotnet;   // .NET を入れていない PC でも動くように .NET を入れる (Package 化で足した)
        if (ImGui.Checkbox("Include .NET", ref dotnet)) { Settings.IncludeDotnet = dotnet; changed = true; }
        ImGui.SameLine();
        var trim = Settings.TrimDotnet;   // v1.3
        if (ImGui.Checkbox("Trim .NET", ref trim)) { Settings.TrimDotnet = trim; changed = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Leave out the parts of .NET the game does not use (WPF, translations, debugging tools).");
        ImGui.SameLine();
        var pack = Settings.PackAssets;   // v1.3
        if (ImGui.Checkbox("Pack Assets", ref pack)) { Settings.PackAssets = pack; changed = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Pack the assets, shaders and Game.dll into one compressed Data.pak.\nSmaller, and the files cannot be opened directly. The game unpacks it when it starts.");
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Copy the .NET runtime next to the exe (about 100 MB).\nThe game then runs on PCs without .NET installed.");

        // ---------- シーン ----------
        ImGui.SeparatorText("Scenes (the first one starts)");
        changed |= drawScenes();

        // ---------- 書き出し先 ----------
        ImGui.SeparatorText("Output");
        var folder = Settings.OutputFolder;
        if (ImGui.InputText("Folder", ref folder, 512)) { Settings.OutputFolder = folder; changed = true; }
        ImGui.SameLine();
        if (ImGui.Button("..."))
        {
            using var dialog = new FolderBrowserDialog { SelectedPath = _builder.OutputFolderOf(Settings), ShowNewFolderButton = true };
            if (dialog.ShowDialog() == DialogResult.OK) { Settings.OutputFolder = dialog.SelectedPath; changed = true; }
        }
        ImGui.TextDisabled(_builder.OutputFolderOf(Settings));
        ImGui.EndDisabled();
        if (changed) save();

        // 開いているシーンを保存していない時 : 書き出すのはファイルの方
        var (scene, dirty) = _currentScene();
        if (dirty && scene != null && Settings.Scenes.Contains(scene, StringComparer.OrdinalIgnoreCase))
            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.3f, 1.0f), $"{Path.GetFileName(scene)} has unsaved changes (the saved file is built)");

        // ---------- ボタン ----------
        ImGui.Spacing();
        ImGui.BeginDisabled(_task != null || Settings.Scenes.Count == 0 || !_builder.CanBuild);
        if (ImGui.Button("Build", new Vector2(120, 0))) StartBuild(run: false);
        ImGui.SameLine();
        if (ImGui.Button("Build and Run", new Vector2(120, 0))) StartBuild(run: true);
        ImGui.EndDisabled();
        if (LastResult is { Ok: true } && _task == null)
        {
            ImGui.SameLine();
            if (ImGui.Button("Run")) Launch();
            ImGui.SameLine();
            if (ImGui.Button("Open Folder")) Process.Start("explorer.exe", $"\"{LastResult.OutputFolder}\"");
        }
        if (!_builder.CanBuild) ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), "GameEngine.Player not found");

        // ---------- 状態 / 結果 ----------
        if (_task != null) ImGui.Text($"Building... {_progress}");
        else if (LastResult is { } result) drawResult(result);

        // ---------- 入れるアセット ----------
        drawPreview();
        ImGui.End();
    }

    // シーンの並び : チェックで入れる / 外す、▲▼ で順番 (0 番が最初のシーン)
    private bool drawScenes()
    {
        var changed = false;
        var all = _database.Entries.Where(e => e.Kind == AssetKind.Scene).Select(e => e.Path).ToList();
        var ordered = Settings.Scenes.Concat(all.Where(p => !Settings.Scenes.Contains(p, StringComparer.OrdinalIgnoreCase))).ToList();
        for (int i = 0; i < ordered.Count; i++)
        {
            var path = ordered[i];
            var index = Settings.Scenes.FindIndex(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
            var included = index >= 0;
            var missing = !all.Contains(path, StringComparer.OrdinalIgnoreCase);
            ImGui.PushID(path);
            if (ImGui.Checkbox("##in", ref included))
            {
                if (included) Settings.Scenes.Add(path); else Settings.Scenes.RemoveAt(index);
                changed = true;
            }
            ImGui.SameLine();
            ImGui.BeginDisabled(!included || index <= 0);
            if (ImGui.ArrowButton("##up", ImGuiDir.Up)) { (Settings.Scenes[index - 1], Settings.Scenes[index]) = (Settings.Scenes[index], Settings.Scenes[index - 1]); changed = true; }
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(!included || index < 0 || index >= Settings.Scenes.Count - 1);
            if (ImGui.ArrowButton("##down", ImGuiDir.Down)) { (Settings.Scenes[index + 1], Settings.Scenes[index]) = (Settings.Scenes[index], Settings.Scenes[index + 1]); changed = true; }
            ImGui.EndDisabled();
            ImGui.SameLine();
            var label = included ? $"{index}  {path}" : path;
            if (missing) ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), $"{label} (missing)");
            else if (included) ImGui.Text(label);
            else ImGui.TextDisabled(label);
            ImGui.PopID();
        }
        if (ordered.Count == 0) ImGui.TextDisabled("No scenes in Assets");
        return changed;
    }

    private void drawResult(BuildResult result)
    {
        if (!result.Ok)
        {
            ImGui.PushTextWrapPos();
            ImGui.TextColored(new Vector4(1, 0.4f, 0.4f, 1), $"Build failed : {result.Error}");
            ImGui.PopTextWrapPos();
            return;
        }
        ImGui.TextColored(new Vector4(0.5f, 1.0f, 0.5f, 1), $"Built {Path.GetFileName(result.ExePath)}");
        ImGui.Text($"{result.AssetCount} assets ({result.AssetBytes / 1024.0 / 1024.0:F1} MB) / {result.FileCount} files ({result.Bytes / 1024.0 / 1024.0:F1} MB)");
        if (result.PakBytes > 0) ImGui.Text($"Data.pak : {result.PakOriginalBytes / 1024.0 / 1024.0:F1} MB -> {result.PakBytes / 1024.0 / 1024.0:F1} MB ({result.PakFiles} files)");
        if (result.IconFile != null) ImGui.Text($"Icon : {result.IconFile}");
        ImGui.Text($"compile {result.CompileMs / 1000:F1} s, copy {result.CopyMs:F0} ms, total {result.TotalMs / 1000:F1} s");
        if (result.Warnings.Count > 0 && ImGui.TreeNode($"Warnings ({result.Warnings.Count})##warnings"))
        {
            foreach (var warning in result.Warnings) ImGui.TextWrapped(warning);
            ImGui.TreePop();
        }
    }

    // 入れるアセットの一覧 (シーン / 設定 / アセットが変わったら探し直す)
    private void drawPreview()
    {
        ImGui.SeparatorText("Included Assets");
        if (_previewVersion != _database.Version || _preview == null)
        {
            _preview = _builder.CollectAssets(Settings);
            _previewVersion = _database.Version;
        }
        var total = _preview.Assets.Sum(a => { try { return new FileInfo(_database.FullPath(a)).Length; } catch (IOException) { return 0L; } });
        ImGui.Text($"{_preview.Assets.Count} of {_database.Entries.Count(e => e.Kind != AssetKind.Folder)} assets, {total / 1024.0 / 1024.0:F1} MB");
        if (_preview.Warnings.Count > 0)
        {
            ImGui.SameLine();
            ImGui.TextColored(new Vector4(1.0f, 0.8f, 0.3f, 1.0f), $"({_preview.Warnings.Count} warnings)");
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(string.Join("\n", _preview.Warnings.Take(20)));
        }
        if (ImGui.BeginChild("##assets", new Vector2(0, 0), ImGuiChildFlags.Borders))
        {
            foreach (var asset in _preview.Assets.OrderBy(a => a, StringComparer.OrdinalIgnoreCase))
            {
                ImGui.Selectable(asset);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("used by\n" + string.Join("\n", _preview.ReasonsFor(asset)));
            }
        }
        ImGui.EndChild();
    }
}
