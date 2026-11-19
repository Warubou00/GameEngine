using System.Numerics;
using System.Text;
using System.Text.Json;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// v2.1 : 窓の並びのプリセット (View > Layout) と、前に開いていたシーン
//   - View > Layout > Default / 登録した並び (5 つまで) を選ぶと、その並びにする
//     Save Layout... / Manage Layouts... で「Layouts」の窓を出す (登録 / 上書き / 名前の変更 / 消す / 並べ替え)
//     以前は Reset Layout (既定の並びに戻す) だけだった (Reset Layout は Default と同じ)
//   - 前に開いていたシーン : シーンを開く / 保存した時に、プロジェクトの Library/EditorState.json に覚える。
//     次にエディターを開いた時 (ランチャーから --project で開いた時も) --scene が無ければそのシーンを開く
//     以前はいつも showcase (無ければ Scenes の最初のシーン) を開いていた
// ==========================================================================
internal sealed partial class EditorApp
{
    // ---------------------------------------------------------------------
    // 窓の並びのプリセット
    // ---------------------------------------------------------------------
    private LayoutPresets? _layouts;
    /// <summary>登録した並び (確認では別のファイルに差し替える)</summary>
    internal LayoutPresets Layouts { get => _layouts ??= new LayoutPresets(LayoutPresets.DefaultFile()); set => _layouts = value; }

    private bool    _showLayouts;
    private string  _layoutName = "";
    private string? _layoutMessage;
    private int     _renamingLayout = -1;
    private string  _renameLayoutText = "";
    private int     _confirmDelete = -1;

    // ini に入らない「窓を出すか」(View の印)
    internal Dictionary<string, bool> LayoutWindows() => new()
    {
        ["Hierarchy"] = _showHierarchy, ["Inspector"] = _showInspector, ["Console"] = _showConsole, ["Project"] = _showProject,
        ["Profiler"] = _profiler.ShowOverview, ["CPU Usage"] = _profiler.ShowCpu, ["GPU Usage"] = _profiler.ShowGpu,
        ["Rendering Stats"] = _profiler.ShowRendering, ["Memory"] = _profiler.ShowMemory,
        ["Render Graph Viewer"] = _renderGraph.Show, ["Frame Debugger"] = _frameDebugger.Show,
    };

    internal void SetLayoutWindows(Dictionary<string, bool> windows)
    {
        bool get(string name, bool current) => windows.TryGetValue(name, out var v) ? v : current;   // 入っていない窓はそのまま
        _showHierarchy = get("Hierarchy", _showHierarchy);
        _showInspector = get("Inspector", _showInspector);
        _showConsole   = get("Console", _showConsole);
        _showProject   = get("Project", _showProject);
        _profiler.ShowOverview  = get("Profiler", _profiler.ShowOverview);
        _profiler.ShowCpu       = get("CPU Usage", _profiler.ShowCpu);
        _profiler.ShowGpu       = get("GPU Usage", _profiler.ShowGpu);
        _profiler.ShowRendering = get("Rendering Stats", _profiler.ShowRendering);
        _profiler.ShowMemory    = get("Memory", _profiler.ShowMemory);
        _renderGraph.Show   = get("Render Graph Viewer", _renderGraph.Show);
        _frameDebugger.Show = get("Frame Debugger", _frameDebugger.Show);
    }

    /// <summary>既定の並び (Hierarchy / Inspector / Console / Project / Profiler / Scene) にする</summary>
    internal void ApplyDefaultLayout()
    {
        SetLayoutWindows(new()
        {
            ["Hierarchy"] = true, ["Inspector"] = true, ["Console"] = true, ["Project"] = true, ["Profiler"] = true,
            ["CPU Usage"] = false, ["GPU Usage"] = false, ["Rendering Stats"] = false, ["Memory"] = false,
            ["Render Graph Viewer"] = false, ["Frame Debugger"] = false,
        });
        _resetLayout = true;
        CurrentLayout = LayoutPresets.DefaultName;
    }

    /// <summary>登録した並びにする (窓の位置は次のフレームの頭で変わる)</summary>
    internal bool ApplyLayoutPreset(int index)
    {
        if ((uint)index >= (uint)Layouts.Presets.Count) return false;
        var preset = Layouts.Presets[index];
        SetLayoutWindows(preset.Windows);
        _imgui.LoadLayout(preset.Ini);
        _resetLayout = false;
        CurrentLayout = preset.Name;
        Console.WriteLine($"[Layout] {preset.Name}");
        return true;
    }

    /// <summary>今の並びを新しく登録する。戻り値 : 断った理由 (登録できれば null)</summary>
    internal string? SaveLayoutPreset(string name)
    {
        var error = Layouts.Add(name, _imgui.SaveLayout(), LayoutWindows());
        if (error == null) { CurrentLayout = name.Trim(); Console.WriteLine($"[Layout] Saved \"{CurrentLayout}\""); }
        return error;
    }

    /// <summary>最後に選んだ / 登録した並びの名前 (メニューの印)</summary>
    internal string? CurrentLayout { get; private set; }

    private void drawLayoutMenu()
    {
        if (!ImGui.BeginMenu("Layout")) return;
        if (ImGui.MenuItem(LayoutPresets.DefaultName, null, CurrentLayout == LayoutPresets.DefaultName)) ApplyDefaultLayout();
        var presets = Layouts.Presets;
        if (presets.Count > 0) ImGui.Separator();
        for (int i = 0; i < presets.Count; i++)
            if (ImGui.MenuItem($"{presets[i].Name}##layout{i}", null, CurrentLayout == presets[i].Name)) ApplyLayoutPreset(i);
        ImGui.Separator();
        if (ImGui.MenuItem("Save Layout...")) openLayoutsWindow(focusName: true);
        if (ImGui.MenuItem("Manage Layouts...")) openLayoutsWindow(focusName: false);
        ImGui.EndMenu();
    }

    private bool _focusLayoutName;
    internal void openLayoutsWindow(bool focusName)
    {
        _showLayouts = true;
        _focusLayoutName = focusName;
        _layoutMessage = null;
        _renamingLayout = _confirmDelete = -1;
        if (_layoutName.Length == 0) _layoutName = $"Layout {Layouts.Presets.Count + 1}";
    }

    // 「Layouts」の窓 (この窓自身は並びに入れない : NoSavedSettings / NoDocking)
    private void drawLayoutsWindow()
    {
        if (!_showLayouts) return;
        ImGui.SetNextWindowSize(new Vector2(520, 0), ImGuiCond.Appearing);
        ImGui.SetNextWindowPos(ImGui.GetMainViewport().GetCenter(), ImGuiCond.Appearing, new Vector2(0.5f, 0.5f));
        if (!ImGui.Begin("Layouts", ref _showLayouts, ImGuiWindowFlags.NoSavedSettings | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoCollapse | ImGuiWindowFlags.AlwaysAutoResize))
        {
            ImGui.End();
            return;
        }
        var layouts = Layouts;
        var presets = layouts.Presets;

        // 今の並びを新しく登録する
        ImGui.Text($"Save the current layout ({presets.Count} / {LayoutPresets.MaxPresets})");
        if (_focusLayoutName) { ImGui.SetKeyboardFocusHere(); _focusLayoutName = false; }
        ImGui.SetNextItemWidth(300);
        var entered = ImGui.InputText("##layoutName", ref _layoutName, LayoutPresets.MaxNameLength * 4 + 1, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
        ImGui.SameLine();
        var nameError = layouts.IsFull ? $"Up to {LayoutPresets.MaxPresets} layouts (delete or overwrite one)" : layouts.CheckName(_layoutName, -1);
        ImGui.BeginDisabled(nameError != null);
        if (ImGui.Button("Save As New") || (entered && nameError == null))
        {
            _layoutMessage = SaveLayoutPreset(_layoutName);
            if (_layoutMessage == null) _layoutName = "";
        }
        ImGui.EndDisabled();
        if (nameError != null && _layoutName.Length > 0) ImGui.TextColored(new Vector4(1.0f, 0.75f, 0.4f, 1.0f), nameError);
        if (_layoutMessage != null) ImGui.TextColored(new Vector4(1.0f, 0.45f, 0.4f, 1.0f), _layoutMessage);

        ImGui.Separator();
        ImGui.TextDisabled($"{LayoutPresets.DefaultName} (built-in)");
        ImGui.SameLine(330);
        if (ImGui.SmallButton($"Load##layoutDefault")) ApplyDefaultLayout();

        int? moveFrom = null, moveTo = null, remove = null;
        for (int i = 0; i < presets.Count; i++)
        {
            ImGui.PushID(i);
            var preset = presets[i];
            if (_renamingLayout == i)
            {
                ImGui.SetNextItemWidth(300);
                if (_focusLayoutName) { ImGui.SetKeyboardFocusHere(); _focusLayoutName = false; }
                var done = ImGui.InputText("##rename", ref _renameLayoutText, LayoutPresets.MaxNameLength * 4 + 1, ImGuiInputTextFlags.EnterReturnsTrue | ImGuiInputTextFlags.AutoSelectAll);
                ImGui.SameLine();
                var error = layouts.CheckName(_renameLayoutText, i);
                ImGui.BeginDisabled(error != null);
                if (ImGui.SmallButton("OK") || (done && error == null))
                {
                    var wasCurrent = CurrentLayout == preset.Name;
                    _layoutMessage = layouts.Rename(i, _renameLayoutText);
                    if (_layoutMessage == null) { if (wasCurrent) CurrentLayout = preset.Name; _renamingLayout = -1; }
                }
                ImGui.EndDisabled();
                ImGui.SameLine();
                if (ImGui.SmallButton("Cancel") || ImGui.IsKeyPressed(ImGuiKey.Escape)) _renamingLayout = -1;
                if (error != null) ImGui.TextColored(new Vector4(1.0f, 0.75f, 0.4f, 1.0f), error);
            }
            else
            {
                ImGui.Text($"{i + 1}. {preset.Name}");
                ImGui.SameLine(330);
                if (ImGui.SmallButton("Load")) ApplyLayoutPreset(i);
                ImGui.SameLine();
                if (ImGui.SmallButton("Overwrite"))
                {
                    layouts.Overwrite(i, _imgui.SaveLayout(), LayoutWindows());
                    CurrentLayout = preset.Name;
                    Console.WriteLine($"[Layout] Overwrote \"{preset.Name}\"");
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Replace this layout with the current window layout");
                ImGui.SameLine();
                if (ImGui.SmallButton("Rename")) { _renamingLayout = i; _renameLayoutText = preset.Name; _focusLayoutName = true; _confirmDelete = -1; }
                ImGui.SameLine();
                if (_confirmDelete == i)
                {
                    if (ImGui.SmallButton("Delete?")) remove = i;
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip("Click again to delete");
                }
                else if (ImGui.SmallButton("Delete")) _confirmDelete = i;
                ImGui.SameLine();
                ImGui.BeginDisabled(i == 0);
                if (ImGui.ArrowButton("##up", ImGuiDir.Up)) { moveFrom = i; moveTo = i - 1; }
                ImGui.EndDisabled();
                ImGui.SameLine();
                ImGui.BeginDisabled(i == presets.Count - 1);
                if (ImGui.ArrowButton("##down", ImGuiDir.Down)) { moveFrom = i; moveTo = i + 1; }
                ImGui.EndDisabled();
            }
            ImGui.PopID();
        }
        if (presets.Count == 0) ImGui.TextDisabled("(no saved layouts yet)");
        // 回している間は並びを変えない (最後にまとめて)
        if (remove is { } r)
        {
            if (CurrentLayout == presets[r].Name) CurrentLayout = null;
            Console.WriteLine($"[Layout] Deleted \"{presets[r].Name}\"");
            layouts.Remove(r);
            _confirmDelete = -1;
        }
        if (moveFrom is { } from && moveTo is { } to) { layouts.Move(from, to); _confirmDelete = -1; }
        ImGui.Spacing();
        ImGui.TextDisabled($"Saved in {layouts.FilePath}");
        ImGui.End();
    }

    // ---------------------------------------------------------------------
    // 前に開いていたシーン (プロジェクトの Library/EditorState.json)
    // ---------------------------------------------------------------------
    private const string EditorStateFile = "EditorState.json";
    private sealed class EditorState
    {
        public string? LastScene { get; set; }   // Assets/... (プロジェクトからの相対パス)
    }

    /// <summary>開いた / 保存したシーンを覚えるか (自動の確認 / --resave-scenes では覚えない。リポジトリの Project の次の起動を変えないため)</summary>
    internal bool RememberLastScene { get; set; }

    private static string editorStatePath(string projectRoot) => Path.Combine(projectRoot, "Library", EditorStateFile);

    /// <summary>前に開いていたシーンのファイル (無い / もう消えていれば null)</summary>
    public static string? ReadLastScene(AssetDatabase database)
    {
        try
        {
            var file = editorStatePath(database.ProjectRoot);
            if (!File.Exists(file)) return null;
            var state = JsonSerializer.Deserialize<EditorState>(File.ReadAllText(file, Encoding.UTF8));
            if (string.IsNullOrWhiteSpace(state?.LastScene)) return null;
            var full = Path.GetFullPath(database.FullPath(state.LastScene));
            // プロジェクトの外は開かない (手で書き換えられた時のため)
            if (!full.StartsWith(Path.GetFullPath(database.AssetsRoot) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) return null;
            return File.Exists(full) ? full : null;
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            Console.WriteLine($"[Editor] Cannot read {EditorStateFile} : {ex.Message}");
            return null;
        }
    }

    private string? _rememberedScene;
    private void rememberScene()
    {
        if (!RememberLastScene || _scenePath == null) return;
        var asset = _database.ToAssetPath(_scenePath);
        if (asset == null || asset == _rememberedScene) return;
        try
        {
            var file = editorStatePath(_database.ProjectRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            File.WriteAllText(file, JsonSerializer.Serialize(new EditorState { LastScene = asset }, new JsonSerializerOptions { WriteIndented = true }), new UTF8Encoding(false));
            _rememberedScene = asset;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[Editor] Cannot save {EditorStateFile} : {ex.Message}");
        }
    }
}
