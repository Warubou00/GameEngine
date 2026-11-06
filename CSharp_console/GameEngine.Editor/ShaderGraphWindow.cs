using System.Globalization;
using System.Numerics;
using GameEngine;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// シェーダーグラフの窓 (SH4)。.shadergraph.json をノードで作る
//   左ドラッグ : ノードを動かす / ピンから線を引く (入力のピンから引くと今の線を外して持つ) / 何も無い所は範囲で選ぶ
//   右クリック : ノードを足す (検索できる) / ノードの上ならそのノードのメニュー。右ドラッグ / 中ドラッグ : 画面を動かす。ホイール : 拡大
//   Delete : 消す / Ctrl+Z / Ctrl+Y : 元に戻す / やり直す / Ctrl+D : 複製 / F : 全部 (選んだ物) を見る / Ctrl+S : すぐ保存
//   変えると少し後に自動で保存してコンパイルする (マテリアルの見た目がすぐ変わる)。エラーのあるノードは赤い
//   元に戻すのはグラフの中だけ (シーンの UndoStack とは別。グラフの JSON を積む)
// ==========================================================================
public sealed class ShaderGraphWindow
{
    private readonly Engine _engine;
    private readonly AssetDatabase _database;

    public string? Path { get; private set; }
    public ShaderGraph? Graph { get; private set; }
    /// <summary>窓 (とその子) にフォーカスがある (EditorApp はショートカットを使わない)</summary>
    public bool HasFocus { get; private set; }
    public HashSet<int> Selected { get; } = new();
    public float Zoom { get; private set; } = 1.0f;
    public Vector2 Pan { get; private set; } = new(40, 40);
    /// <summary>ノード → エラー (グラフを作る時の物 + コンパイルの物)</summary>
    public IReadOnlyDictionary<int, string> Errors => _errors;
    public ShaderGraphResult? Result => _result;
    public ShaderAsset? Shader => _shader;
    public int SaveCount { get; private set; }
    /// <summary>最後の保存でのグラフ → HLSL → コンパイルの時間</summary>
    public double LastCompileMs { get; private set; }
    public bool Dirty => _dirty;

    private ShaderGraphResult? _result;
    private Dictionary<int, string> _errors = new();
    private ShaderAsset? _shader;
    private string _status = "";

    // 元に戻す (グラフの JSON)
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private string _committed = "";
    private const int UndoLimit = 200;

    // 保存 / コンパイル (変えてから少し待つ)
    private bool _dirty;
    private double _changedAt;
    private const double CompileDelay = 0.25;

    // 触っている物
    private enum DragMode { None, Move, Link, Box, Pan }
    private DragMode _drag;
    private Vector2 _dragStart;
    private (int Node, string Port, bool IsOutput) _linkFrom;
    private bool _moved;
    private Vector2 _rightDown;
    private bool _rightDragged;
    private Vector2 _popupPosition;   // ノードを足す所 (グラフの座標)
    private (int Node, string Port, bool IsOutput)? _pendingLink;   // 線を何も無い所で離した時 : 足したノードに繋ぐ
    private int _contextNode;
    private string _search = "";
    private bool _focusSearch;
    private bool _showSource;
    private bool _frameRequest;

    // 見た目 (グラフの単位。画面には Zoom を掛ける)
    private const float HeaderHeight = 24.0f, RowHeight = 20.0f, PinRadius = 5.0f;

    public ShaderGraphWindow(Engine engine, AssetDatabase database)
    {
        _engine = engine;
        _database = database;
    }

    // ---------------------------------------------------------------------
    // 開く / 保存 / コンパイル
    // ---------------------------------------------------------------------

    public bool Open(string path)
    {
        Flush();
        try
        {
            Graph = ShaderGraph.Load(_database.FullPath(path));
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[ShaderGraph] Cannot open {path} : {ex.Message}");
            return false;
        }
        Path = path;
        Selected.Clear();
        _undo.Clear();
        _redo.Clear();
        _committed = Graph.ToJson();
        _dirty = false;
        _frameRequest = true;
        _shader = loadShader();
        refresh();
        return true;
    }

    /// <summary>変えた物があればすぐ保存してコンパイルする</summary>
    public void Flush()
    {
        commit();
        if (!_dirty || Graph == null || Path == null) return;
        _dirty = false;
        var file = _database.FullPath(Path);
        try
        {
            _engine.Assets.NoteOwnWrite(file);   // Project の見張りが読み直さない (同じ中身なので読み直しても何もしないが)
            File.WriteAllText(file, Graph.ToJson(), new System.Text.UTF8Encoding(false));
            SaveCount++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _status = $"Cannot save : {ex.Message}";
            return;
        }
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        _shader = loadShader();
        if (_shader != null) _engine.Assets.ReloadShader(Path);   // 中身が変わった時だけコンパイルし、使っているマテリアルが送り直す
        LastCompileMs = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        refresh();
    }

    private ShaderAsset? loadShader()
    {
        if (Path == null) return null;
        try { return _engine.Assets.LoadShader(Path); }
        catch (Exception ex) when (ex is IOException) { _status = ex.Message; return null; }
    }

    // ピンの型とエラーを作り直す (グラフを作るのは速い。コンパイルのエラーは最後のコンパイルの物)
    private void refresh()
    {
        if (Graph == null) return;
        _result = Graph.Generate();
        _errors = new Dictionary<int, string>(_result.Errors);
        if (_shader?.Error is { } error && _result.Error == null)
            foreach (var (id, message) in ShaderGraph.ErrorsByNode(error)) _errors[id] = _errors.TryGetValue(id, out var before) ? $"{before}\n{message}" : message;
        _status = _shader == null ? "" : _shader.Error == null ? $"Compiled (version {_shader.Version}{(LastCompileMs > 0 ? $", {LastCompileMs:F0} ms" : "")})" : _result.Error != null ? "Graph error" : "Compile error";
    }

    // 変えた : 少し後に保存 / コンパイル (元に戻すの区切りは commit)
    private void changed()
    {
        _dirty = true;
        _changedAt = ImGui.GetTime();
        refresh();
    }

    // 元に戻すの区切り (ドラッグの終わり / ボタン など)。前と違えば積む
    private void commit()
    {
        if (Graph == null) return;
        var json = Graph.ToJson();
        if (json == _committed) return;
        _undo.Add(_committed);
        if (_undo.Count > UndoLimit) _undo.RemoveAt(0);
        _redo.Clear();
        _committed = json;
    }

    public bool CanUndo => _undo.Count > 0 || _dirty;
    public bool CanRedo => _redo.Count > 0;

    public void Undo()
    {
        commit();
        if (_undo.Count == 0 || Graph == null) return;
        _redo.Add(_committed);
        _committed = _undo[^1];
        _undo.RemoveAt(_undo.Count - 1);
        restore();
    }

    public void Redo()
    {
        if (_redo.Count == 0 || Graph == null) return;
        _undo.Add(_committed);
        _committed = _redo[^1];
        _redo.RemoveAt(_redo.Count - 1);
        restore();
    }

    private void restore()
    {
        Graph = ShaderGraph.FromJson(_committed);
        Selected.RemoveWhere(id => Graph.Find(id) == null);
        changed();
    }

    // ---------------------------------------------------------------------
    // 編集 (テストからも使う。どれも 1 回の「元に戻す」)
    // ---------------------------------------------------------------------

    public ShaderGraphNode? AddNode(string type, Vector2 position)
    {
        if (Graph == null || !ShaderGraph.Library.ContainsKey(type)) return null;
        var node = Graph.Add(type, position);
        Selected.Clear();
        Selected.Add(node.Id);
        changed();
        commit();
        return node;
    }

    public bool Connect(int fromNode, string fromPort, int toNode, string toPort)
    {
        if (Graph == null) return false;
        var ok = Graph.Connect(fromNode, fromPort, toNode, toPort);
        if (!ok) _status = "Cannot connect : the link would make a loop";
        changed();
        commit();
        return ok;
    }

    public void Disconnect(int toNode, string toPort)
    {
        if (Graph == null) return;
        Graph.Links.RemoveAll(l => l.ToNode == toNode && l.ToPort == toPort);
        changed();
        commit();
    }

    public void SetValue(int nodeId, string key, string? value)
    {
        if (Graph?.Find(nodeId) is not { } node) return;
        if (value == null) node.Values.Remove(key); else node.Values[key] = value;
        changed();
        commit();
    }

    public void DeleteSelected()
    {
        if (Graph == null || Selected.Count == 0) return;
        foreach (var id in Selected) Graph.Remove(id);
        Selected.Clear();
        changed();
        commit();
    }

    public void DuplicateSelected()
    {
        if (Graph == null || Selected.Count == 0) return;
        var map = new Dictionary<int, int>();
        foreach (var id in Selected.ToList())
        {
            var source = Graph.Find(id)!;
            var copy = Graph.Add(source.Type, source.Position + new Vector2(30, 30));
            foreach (var (key, value) in source.Values) if (key != "Name") copy.Values[key] = value;   // パラメーターの名前は新しい物 (Add が作る)
            map[id] = copy.Id;
        }
        // 選んだ物どうしの線も写す
        foreach (var link in Graph.Links.Where(l => map.ContainsKey(l.FromNode) && map.ContainsKey(l.ToNode)).ToList())
            Graph.Links.Add(link with { FromNode = map[link.FromNode], ToNode = map[link.ToNode] });
        Selected.Clear();
        Selected.UnionWith(map.Values);
        changed();
        commit();
    }

    public void FrameAll() => _frameRequest = true;

    // ---------------------------------------------------------------------
    // 描く
    // ---------------------------------------------------------------------

    public void Draw(ref bool open)
    {
        HasFocus = false;
        if (!open) { if (_dirty) Flush(); return; }
        // 初めて開く時はエディターの 9 割の大きさで真ん中 (以前は 1100 × 640 で、全部を見せると字が小さすぎた)
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowSize(viewport.WorkSize * 0.9f, ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(viewport.WorkPos + viewport.WorkSize * 0.5f, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));
        if (!ImGui.Begin(Path != null ? $"Shader Graph - {System.IO.Path.GetFileName(Path)}###ShaderGraph" : "Shader Graph###ShaderGraph", ref open))
        {
            ImGui.End();
            return;
        }
        HasFocus = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        if (Graph == null)
        {
            ImGui.TextWrapped("Double-click a .shadergraph.json in Project, or press Create Shader Graph there.");
            ImGui.End();
            return;
        }
        if (_dirty && ImGui.GetTime() - _changedAt > CompileDelay && _drag == DragMode.None) Flush();

        drawToolbar();
        var avail = ImGui.GetContentRegionAvail();
        if (ImGui.BeginChild("##side", new Vector2(290, avail.Y), ImGuiChildFlags.Borders)) drawSide();
        ImGui.EndChild();
        ImGui.SameLine();
        if (ImGui.BeginChild("##canvas", new Vector2(0, avail.Y), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoMove))
            drawCanvas();
        ImGui.EndChild();
        if (HasFocus) handleKeys();
        ImGui.End();
    }

    private void drawToolbar()
    {
        if (ImGui.Button("Save")) Flush();
        ImGui.SameLine();
        ImGui.BeginDisabled(!CanUndo);
        if (ImGui.Button("Undo")) Undo();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!CanRedo);
        if (ImGui.Button("Redo")) Redo();
        ImGui.EndDisabled();
        ImGui.SameLine();
        if (ImGui.Button("Frame")) FrameAll();
        ImGui.SameLine();
        ImGui.Checkbox("HLSL", ref _showSource);
        ImGui.SameLine();
        ImGui.TextDisabled($"zoom {Zoom * 100:F0}%");
        ImGui.SameLine();
        var ok = _shader != null && _shader.Error == null && _result?.Error == null;
        ImGui.TextColored(ok ? new Vector4(0.5f, 1.0f, 0.5f, 1.0f) : new Vector4(1.0f, 0.45f, 0.4f, 1.0f), _dirty ? "(editing)" : _status);
    }

    // ---------- 横 : 選んだノードの設定 / グラフの情報 ----------
    private void drawSide()
    {
        if (_showSource)
        {
            var source = _shader?.GraphSource is { Length: > 0 } s ? s : _result?.Source ?? "";
            ImGui.TextDisabled("Generated HLSL (read only)");
            ImGui.InputTextMultiline("##hlsl", ref source, (uint)source.Length + 1, ImGui.GetContentRegionAvail(), ImGuiInputTextFlags.ReadOnly);
            return;
        }
        if (Selected.Count == 1 && Graph!.Find(Selected.First()) is { } node && ShaderGraph.Library.TryGetValue(node.Type, out var def))
        {
            drawNodeSettings(node, def);
            return;
        }
        ImGui.Text(System.IO.Path.GetFileName(Path));
        ImGui.TextDisabled($"{Graph!.Nodes.Count} nodes / {Graph.Links.Count} links");
        ImGui.Separator();
        if (_shader?.Error is { } error)
        {
            ImGui.TextColored(new Vector4(1.0f, 0.45f, 0.4f, 1.0f), _shader.IsValid ? "Error (the last good version is used) :" : "Error :");
            ImGui.PushTextWrapPos();
            ImGui.TextUnformatted(Inspector.firstErrors(error));
            ImGui.PopTextWrapPos();
        }
        if (_errors.Count > 0)
        {
            ImGui.SeparatorText("Nodes with errors");
            foreach (var (id, message) in _errors)
            {
                var title = Graph.Find(id) is { } n && ShaderGraph.Library.TryGetValue(n.Type, out var d) ? d.Title : $"node {id}";
                if (ImGui.Selectable($"{title} #{id}##err{id}")) { Selected.Clear(); Selected.Add(id); _frameRequest = true; }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip(message);
            }
        }
        if (_shader is { Parameters.Count: > 0 })
        {
            ImGui.SeparatorText("Material parameters");
            foreach (var p in _shader.Parameters) ImGui.BulletText($"{p.Name} ({p.Type})");
        }
        ImGui.SeparatorText("How to use");
        ImGui.PushTextWrapPos();
        ImGui.TextDisabled("Right click : add a node. Drag from a pin to connect. Drag an input's link away to remove it. Delete / Ctrl+D / Ctrl+Z. " +
                           "Put this file in a material's Shader slot to use it.");
        ImGui.PopTextWrapPos();
    }

    private void drawNodeSettings(ShaderGraphNode node, NodeDef def)
    {
        ImGui.Text($"{def.Title}  #{node.Id}");
        ImGui.PushTextWrapPos();
        ImGui.TextDisabled(def.Help);
        if (_errors.TryGetValue(node.Id, out var error)) ImGui.TextColored(new Vector4(1.0f, 0.45f, 0.4f, 1.0f), error);
        ImGui.PopTextWrapPos();
        ImGui.PushID(node.Id);
        if (def.Properties.Length > 0) ImGui.SeparatorText("Settings");
        foreach (var prop in def.Properties) drawProperty(node, prop);
        var editable = def.Inputs.Where(p => p.Type is not (PortType.Texture2D or PortType.Texture3D) && !Graph!.Links.Any(l => l.ToNode == node.Id && l.ToPort == p.Name)).ToList();
        if (editable.Count > 0) ImGui.SeparatorText("Inputs (not connected)");
        foreach (var port in editable) drawInputValue(node, port);
        ImGui.PopID();
    }

    private string valueOf(ShaderGraphNode node, string key, string fallback) => node.Values.TryGetValue(key, out var v) ? v : fallback;

    // 値を書く (ドラッグ中も送る : 少し後にコンパイル。元に戻すの区切りは触り終わり)
    private void write(ShaderGraphNode node, string key, string value)
    {
        if (node.Values.TryGetValue(key, out var before) && before == value) return;
        node.Values[key] = value;
        changed();
    }

    private void endEdit()
    {
        if (ImGui.IsItemDeactivatedAfterEdit()) commit();
    }

    private void drawProperty(ShaderGraphNode node, NodePropertyDef prop)
    {
        var text = valueOf(node, prop.Name, prop.Default);
        ImGui.SetNextItemWidth(-90);
        switch (prop.Kind)
        {
        case NodePropertyKind.Float:
        {
            var f = float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0.0f;
            if (ImGui.DragFloat(prop.Name, ref f, 0.01f)) write(node, prop.Name, f.ToString("R", CultureInfo.InvariantCulture));
            endEdit();
            break;
        }
        case NodePropertyKind.Vector:
        {
            var count = Math.Clamp(componentCount(prop.Default), 2, 4);
            if (dragVector(prop.Name, text, count, out var result)) write(node, prop.Name, result);
            endEdit();
            break;
        }
        case NodePropertyKind.Color:
        {
            ShaderAsset.TryParseVector(text, out var c);
            if (ImGui.ColorEdit4(prop.Name, ref c, ImGuiColorEditFlags.Float | ImGuiColorEditFlags.AlphaBar)) write(node, prop.Name, vectorText(c, 4));
            endEdit();
            break;
        }
        case NodePropertyKind.Bool:
        {
            var b = text.Equals("true", StringComparison.OrdinalIgnoreCase);
            if (ImGui.Checkbox(prop.Name, ref b)) { write(node, prop.Name, b ? "true" : "false"); commit(); }
            break;
        }
        case NodePropertyKind.Enum:
            if (ImGui.BeginCombo(prop.Name, text))
            {
                foreach (var option in prop.Options ?? Array.Empty<string>())
                    if (ImGui.Selectable(option, option == text)) { write(node, prop.Name, option); commit(); }
                ImGui.EndCombo();
            }
            break;
        case NodePropertyKind.Texture2D:
            if (ImGui.InputText(prop.Name, ref text, 512)) write(node, prop.Name, text.Trim());
            endEdit();
            if (ImGui.BeginDragDropTarget())
            {
                if (ProjectPanel.AcceptDrop(AssetKind.Texture) is { } asset) { write(node, prop.Name, asset.Path); commit(); }
                ImGui.EndDragDropTarget();
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("white / black / drop a texture from Project");
            break;
        case NodePropertyKind.Texture3D:
            if (ImGui.BeginCombo(prop.Name, text))
            {
                foreach (var kind in Noise3D.Kinds)
                    foreach (var size in new[] { 32, 64, 128 })
                    {
                        var option = $"{Noise3D.Prefix}{kind}:{size}";
                        if (ImGui.Selectable(option, option == text)) { write(node, prop.Name, option); commit(); }
                    }
                ImGui.EndCombo();
            }
            break;
        default:
            if (ImGui.InputText(prop.Name, ref text, 128)) write(node, prop.Name, text);
            endEdit();
            break;
        }
    }

    private void drawInputValue(ShaderGraphNode node, PortDef port)
    {
        var text = valueOf(node, port.Name, port.Default);
        var isSpecial = text.TrimStart().StartsWith('@');
        if (isSpecial)
        {
            ImGui.TextUnformatted($"{port.Name} : {ShaderGraph.DescribeDefault(text)}");
            ImGui.SameLine();
            if (ImGui.SmallButton($"Value##{port.Name}")) { write(node, port.Name, port.Type == PortType.Float ? "0" : vectorText(Vector4.Zero, sizeOf(port, "0"))); commit(); }
            return;
        }
        var count = sizeOf(port, text);
        ImGui.SetNextItemWidth(port.Type == PortType.Dynamic ? -130 : -90);
        if (dragVector(port.Name, text, count, out var result)) write(node, port.Name, result);
        endEdit();
        if (port.Type == PortType.Dynamic)
        {
            // Dynamic の入力は大きさを選べる (繋いだ物が無い時のノードの大きさになる)
            ImGui.SameLine();
            ImGui.SetNextItemWidth(36);
            var size = count;
            if (ImGui.BeginCombo($"##size{port.Name}", size.ToString(CultureInfo.InvariantCulture), ImGuiComboFlags.NoArrowButton))
            {
                for (int n = 1; n <= 4; n++)
                    if (ImGui.Selectable(n.ToString(CultureInfo.InvariantCulture), n == size))
                    {
                        ShaderAsset.TryParseVector(text, out var v);
                        if (count == 1) v = new Vector4(v.X);
                        write(node, port.Name, vectorText(v, n));
                        commit();
                    }
                ImGui.EndCombo();
            }
        }
        if (node.Values.ContainsKey(port.Name))
        {
            ImGui.SameLine();
            if (ImGui.SmallButton($"R##{port.Name}")) { node.Values.Remove(port.Name); changed(); commit(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"Reset to {ShaderGraph.DescribeDefault(port.Default)}");
        }
    }

    private static int componentCount(string text) => text.Trim().TrimStart('(').TrimEnd(')').Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
    private static int sizeOf(PortDef port, string text) => port.Type == PortType.Dynamic ? Math.Clamp(componentCount(text), 1, 4) : Math.Clamp((int)port.Type, 1, 4);

    private static string vectorText(Vector4 v, int count)
    {
        string g(float x) => x.ToString("0.####", CultureInfo.InvariantCulture);
        return count switch
        {
            1 => g(v.X),
            2 => $"({g(v.X)}, {g(v.Y)})",
            3 => $"({g(v.X)}, {g(v.Y)}, {g(v.Z)})",
            _ => $"({g(v.X)}, {g(v.Y)}, {g(v.Z)}, {g(v.W)})",
        };
    }

    private static bool dragVector(string label, string text, int count, out string result)
    {
        ShaderAsset.TryParseVector(text, out var v);
        result = text;
        bool edited;
        switch (count)
        {
        case 1: { var x = v.X; edited = ImGui.DragFloat(label, ref x, 0.01f); v.X = x; break; }
        case 2: { var x = new Vector2(v.X, v.Y); edited = ImGui.DragFloat2(label, ref x, 0.01f); v = new Vector4(x, v.Z, v.W); break; }
        case 3: { var x = new Vector3(v.X, v.Y, v.Z); edited = ImGui.DragFloat3(label, ref x, 0.01f); v = new Vector4(x, v.W); break; }
        default: edited = ImGui.DragFloat4(label, ref v, 0.01f); break;
        }
        if (edited) result = vectorText(v, count);
        return edited;
    }

    // ---------- キャンバス ----------
    private Vector2 _origin;
    private Vector2 toScreen(Vector2 p) => _origin + (p + Pan) * Zoom;
    private Vector2 toGraph(Vector2 s) => (s - _origin) / Zoom - Pan;

    private float widthOf(NodeDef def)
    {
        var inputs = def.Inputs.Length == 0 ? 0.0f : def.Inputs.Max(p => ImGui.CalcTextSize(p.Name).X) + 56.0f;   // 値の表示の分
        var outputs = def.Outputs.Length == 0 ? 0.0f : def.Outputs.Max(p => ImGui.CalcTextSize(p.Name).X);
        return MathF.Max(MathF.Max(140.0f, ImGui.CalcTextSize(def.Title).X + 36.0f), inputs + outputs + 36.0f);
    }

    private static float heightOf(NodeDef def) => HeaderHeight + Math.Max(1, Math.Max(def.Inputs.Length, def.Outputs.Length)) * RowHeight + 8.0f;
    private static Vector2 inputPin(ShaderGraphNode node, int index) => node.Position + new Vector2(0, HeaderHeight + 4.0f + RowHeight * (index + 0.5f));
    private Vector2 outputPin(ShaderGraphNode node, NodeDef def, int index) => node.Position + new Vector2(widthOf(def), HeaderHeight + 4.0f + RowHeight * (index + 0.5f));

    private int typeOf(ShaderGraphNode node, NodeDef def, int output) =>
        _result != null && _result.OutputTypes.TryGetValue(node.Id, out var types) && output < types.Length ? types[output] : (int)def.Outputs[output].Type;

    private int inputTypeOf(ShaderGraphNode node, PortDef port) =>
        port.Type == PortType.Dynamic ? (_result != null && _result.DynamicSizes.TryGetValue(node.Id, out var d) ? d : 1) : (int)port.Type;

    private static uint colorOf(int type) => type switch
    {
        1  => 0xFFB0B0B0u,   // float : 灰 (ABGR)
        2  => 0xFF60D060u,   // float2 : 緑
        3  => 0xFF40D0E8u,   // float3 : 黄
        4  => 0xFFD080E0u,   // float4 : 桃
        16 => 0xFF4080FFu,   // Texture2D : 橙
        17 => 0xFF4040E0u,   // Texture3D : 赤
        _  => 0xFFFFFFFFu,
    };

    private static uint headerColorOf(string category) => category switch
    {
        "Input"     => 0xFF7A4A2Au,
        "Constant"  => 0xFF4A4A4Au,
        "Parameter" => 0xFF2A6A3Au,
        "Texture"   => 0xFF2A4A8Au,
        "Math"      => 0xFF5A3A6Au,
        "Vector"    => 0xFF6A4A3Au,
        "Noise"     => 0xFF3A5A6Au,
        "Channel"   => 0xFF3A6A6Au,
        "Output"    => 0xFF2A2A9Au,
        _           => 0xFF444444u,
    };

    // ピンを探す : 一番近い物 (以前は最初に当たった物で、縮小すると隣の行のピンに繋がった。当たりの半径は行の半分まで)
    private (ShaderGraphNode Node, NodeDef Def, int Index, bool IsOutput)? pinAt(Vector2 graphPoint)
    {
        var radius = MathF.Min((PinRadius + 4.0f) / MathF.Min(Zoom, 1.0f), RowHeight * 0.5f);
        (ShaderGraphNode Node, NodeDef Def, int Index, bool IsOutput)? best = null;
        var bestDistance = radius;
        foreach (var node in Graph!.Nodes)
        {
            if (!ShaderGraph.Library.TryGetValue(node.Type, out var def)) continue;
            for (int i = 0; i < def.Inputs.Length; i++)
            {
                var d = Vector2.Distance(inputPin(node, i), graphPoint);
                if (d < bestDistance) { bestDistance = d; best = (node, def, i, false); }
            }
            for (int i = 0; i < def.Outputs.Length; i++)
            {
                var d = Vector2.Distance(outputPin(node, def, i), graphPoint);
                if (d < bestDistance) { bestDistance = d; best = (node, def, i, true); }
            }
        }
        return best;
    }

    private ShaderGraphNode? nodeAt(Vector2 graphPoint)
    {
        for (int n = Graph!.Nodes.Count - 1; n >= 0; n--)
        {
            var node = Graph.Nodes[n];
            var size = ShaderGraph.Library.TryGetValue(node.Type, out var def) ? new Vector2(widthOf(def), heightOf(def)) : new Vector2(140, 50);
            if (graphPoint.X >= node.Position.X && graphPoint.Y >= node.Position.Y && graphPoint.X <= node.Position.X + size.X && graphPoint.Y <= node.Position.Y + size.Y) return node;
        }
        return null;
    }

    private void drawCanvas()
    {
        var graph = Graph!;
        _origin = ImGui.GetCursorScreenPos();
        var size = Vector2.Max(ImGui.GetContentRegionAvail(), new Vector2(50, 50));
        ImGui.InvisibleButton("##canvasButton", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight | ImGuiButtonFlags.MouseButtonMiddle);
        var hovered = ImGui.IsItemHovered();
        var io = ImGui.GetIO();
        var mouse = io.MousePos;
        var mouseGraph = toGraph(mouse);
        if (_frameRequest) { frame(size); _frameRequest = false; }

        var draw = ImGui.GetWindowDrawList();
        draw.PushClipRect(_origin, _origin + size, true);
        draw.AddRectFilled(_origin, _origin + size, 0xFF262222u);
        // 格子
        var step = 32.0f * Zoom;
        if (step > 6.0f)
        {
            var offset = new Vector2(((Pan.X * Zoom) % step + step) % step, ((Pan.Y * Zoom) % step + step) % step);
            for (var x = offset.X; x < size.X; x += step) draw.AddLine(_origin + new Vector2(x, 0), _origin + new Vector2(x, size.Y), 0xFF302C2Cu);
            for (var y = offset.Y; y < size.Y; y += step) draw.AddLine(_origin + new Vector2(0, y), _origin + new Vector2(size.X, y), 0xFF302C2Cu);
        }

        handleMouse(hovered, mouse, mouseGraph, size);

        // 線
        var font = ImGui.GetFont();
        var fontSize = ImGui.GetFontSize() * Zoom;
        foreach (var link in graph.Links)
        {
            var from = graph.Find(link.FromNode);
            var to = graph.Find(link.ToNode);
            if (from == null || to == null || !ShaderGraph.Library.TryGetValue(from.Type, out var fromDef) || !ShaderGraph.Library.TryGetValue(to.Type, out var toDef)) continue;
            var o = Array.FindIndex(fromDef.Outputs, p => p.Name == link.FromPort);
            var i = Array.FindIndex(toDef.Inputs, p => p.Name == link.ToPort);
            if (o < 0 || i < 0) continue;
            var color = colorOf(typeOf(from, fromDef, o));
            var thick = Selected.Contains(from.Id) || Selected.Contains(to.Id) ? 3.5f : 2.2f;
            bezier(draw, toScreen(outputPin(from, fromDef, o)), toScreen(inputPin(to, i)), color, thick * MathF.Max(Zoom, 0.5f));
        }
        // 引いている線
        if (_drag == DragMode.Link && graph.Find(_linkFrom.Node) is { } linkNode && ShaderGraph.Library.TryGetValue(linkNode.Type, out var linkDef))
        {
            if (_linkFrom.IsOutput)
            {
                var o = Array.FindIndex(linkDef.Outputs, p => p.Name == _linkFrom.Port);
                if (o >= 0) bezier(draw, toScreen(outputPin(linkNode, linkDef, o)), mouse, colorOf(typeOf(linkNode, linkDef, o)), 2.5f);
            }
            else
            {
                var i = Array.FindIndex(linkDef.Inputs, p => p.Name == _linkFrom.Port);
                if (i >= 0) bezier(draw, mouse, toScreen(inputPin(linkNode, i)), 0xFFFFFFFFu, 2.5f);
            }
        }

        // ノード
        string? tooltip = null;
        foreach (var node in graph.Nodes)
        {
            if (!ShaderGraph.Library.TryGetValue(node.Type, out var def))
            {
                var bad = toScreen(node.Position);
                draw.AddRectFilled(bad, bad + new Vector2(140, 40) * Zoom, 0xFF2020A0u, 4.0f * Zoom);
                draw.AddText(font, fontSize, bad + new Vector2(6, 6) * Zoom, 0xFFFFFFFFu, $"? {node.Type}");
                continue;
            }
            var width = widthOf(def);
            var min = toScreen(node.Position);
            var max = toScreen(node.Position + new Vector2(width, heightOf(def)));
            var hasError = _errors.TryGetValue(node.Id, out var error);
            var selected = Selected.Contains(node.Id);
            var rounding = 5.0f * Zoom;
            draw.AddRectFilled(min, max, 0xF0302A2Au, rounding);
            draw.AddRectFilled(min, new Vector2(max.X, min.Y + HeaderHeight * Zoom), hasError ? 0xFF2828B8u : headerColorOf(def.Category), rounding, ImDrawFlags.RoundCornersTop);
            draw.AddText(font, fontSize, min + new Vector2(8, 4) * Zoom, 0xFFFFFFFFu, hasError ? $"! {def.Title}" : def.Title);
            var border = hasError ? 0xFF3C3CFFu : selected ? 0xFF40C0FFu : 0xFF000000u;
            draw.AddRect(min, max, border, rounding, ImDrawFlags.None, hasError || selected ? 2.5f : 1.0f);
            if (hasError && ImGui.IsMouseHoveringRect(min, max) && hovered) tooltip = error;

            for (int i = 0; i < def.Inputs.Length; i++)
            {
                var port = def.Inputs[i];
                var pin = toScreen(inputPin(node, i));
                var connected = graph.Links.Any(l => l.ToNode == node.Id && l.ToPort == port.Name);
                var color = colorOf(inputTypeOf(node, port));
                if (connected) draw.AddCircleFilled(pin, PinRadius * Zoom, color);
                else draw.AddCircle(pin, PinRadius * Zoom, color, 12, 1.5f * MathF.Max(Zoom, 0.5f));
                var label = port.Name;
                if (!connected && port.Type is not (PortType.Texture2D or PortType.Texture3D))
                    label += "  " + shortValue(valueOf(node, port.Name, port.Default));
                draw.AddText(font, fontSize, pin + new Vector2(PinRadius + 5.0f, -9.0f) * Zoom, connected ? 0xFFE0E0E0u : 0xFF909090u, label);
            }
            for (int o = 0; o < def.Outputs.Length; o++)
            {
                var port = def.Outputs[o];
                var pin = toScreen(outputPin(node, def, o));
                var connected = graph.Links.Any(l => l.FromNode == node.Id && l.FromPort == port.Name);
                var color = colorOf(typeOf(node, def, o));
                if (connected) draw.AddCircleFilled(pin, PinRadius * Zoom, color);
                else draw.AddCircle(pin, PinRadius * Zoom, color, 12, 1.5f * MathF.Max(Zoom, 0.5f));
                var textWidth = ImGui.CalcTextSize(port.Name).X * Zoom;
                draw.AddText(font, fontSize, pin + new Vector2(-(PinRadius + 5.0f) * Zoom - textWidth, -9.0f * Zoom), 0xFFE0E0E0u, port.Name);
            }
            // パラメーターは名前を見せる
            if (def.IsParameter && node.Values.TryGetValue("Name", out var name))
                draw.AddText(font, fontSize * 0.9f, new Vector2(min.X + 8 * Zoom, max.Y + 2 * Zoom), 0xFF80C080u, name);
        }

        // 範囲で選ぶ
        if (_drag == DragMode.Box)
        {
            var a = Vector2.Min(_dragStart, mouse);
            var b = Vector2.Max(_dragStart, mouse);
            draw.AddRectFilled(a, b, 0x30FFC040u);
            draw.AddRect(a, b, 0xC0FFC040u);
        }
        draw.PopClipRect();
        if (tooltip != null && _drag == DragMode.None) ImGui.SetTooltip(tooltip);

        drawPopups();
    }

    private static string shortValue(string text)
    {
        text = ShaderGraph.DescribeDefault(text);
        return text.Length > 14 ? text[..12] + ".." : text;
    }

    private static void bezier(ImDrawListPtr draw, Vector2 a, Vector2 b, uint color, float thickness)
    {
        var dx = MathF.Max(40.0f, MathF.Abs(b.X - a.X) * 0.5f);
        draw.AddBezierCubic(a, a + new Vector2(dx, 0), b - new Vector2(dx, 0), b, color, thickness);
    }

    private void frame(Vector2 size)
    {
        var nodes = Graph!.Nodes.Where(n => Selected.Count == 0 || Selected.Contains(n.Id)).ToList();
        if (nodes.Count == 0) { Pan = new Vector2(40, 40); Zoom = 1.0f; return; }
        var min = new Vector2(float.MaxValue);
        var max = new Vector2(float.MinValue);
        foreach (var node in nodes)
        {
            var extent = ShaderGraph.Library.TryGetValue(node.Type, out var def) ? new Vector2(widthOf(def), heightOf(def)) : new Vector2(140, 50);
            min = Vector2.Min(min, node.Position);
            max = Vector2.Max(max, node.Position + extent);
        }
        var bounds = max - min + new Vector2(80, 80);
        Zoom = Math.Clamp(MathF.Min(size.X / bounds.X, size.Y / bounds.Y), 0.3f, 1.25f);
        Pan = (size / Zoom - (max - min)) * 0.5f - min;
    }

    private void handleMouse(bool hovered, Vector2 mouse, Vector2 mouseGraph, Vector2 size)
    {
        var io = ImGui.GetIO();
        var graph = Graph!;
        // 拡大 (マウスの所を動かさない)
        if (hovered && io.MouseWheel != 0.0f)
        {
            var zoom = Math.Clamp(Zoom * MathF.Pow(1.15f, io.MouseWheel), 0.25f, 2.5f);
            Pan = (mouse - _origin) / zoom - mouseGraph;
            Zoom = zoom;
        }
        // 画面を動かす : 中ドラッグ / 右ドラッグ
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Middle)) _drag = DragMode.Pan;
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right)) { _rightDown = mouse; _rightDragged = false; }
        if (ImGui.IsMouseDown(ImGuiMouseButton.Right) && ImGui.IsItemActive())
        {
            if (Vector2.Distance(mouse, _rightDown) > 4.0f) _rightDragged = true;
            if (_rightDragged) Pan += io.MouseDelta / Zoom;
        }
        if (_drag == DragMode.Pan)
        {
            Pan += io.MouseDelta / Zoom;
            if (!ImGui.IsMouseDown(ImGuiMouseButton.Middle)) _drag = DragMode.None;
        }
        // 右クリック (動かさずに離した) : メニュー
        if (hovered && ImGui.IsMouseReleased(ImGuiMouseButton.Right) && !_rightDragged)
        {
            _popupPosition = mouseGraph;
            if (nodeAt(mouseGraph) is { } node)
            {
                _contextNode = node.Id;
                if (!Selected.Contains(node.Id)) { Selected.Clear(); Selected.Add(node.Id); }
                ImGui.OpenPopup("##nodeMenu");
            }
            else openAddMenu(null);
        }

        // 左
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            _dragStart = mouse;
            _moved = false;
            if (pinAt(mouseGraph) is { } pin)
            {
                if (pin.IsOutput) { _drag = DragMode.Link; _linkFrom = (pin.Node.Id, pin.Def.Outputs[pin.Index].Name, true); }
                else
                {
                    var port = pin.Def.Inputs[pin.Index].Name;
                    var existing = graph.Links.FirstOrDefault(l => l.ToNode == pin.Node.Id && l.ToPort == port);
                    if (existing != null)
                    {
                        // 繋がっている入力から引く : 線を外して、元の出力から引いている形にする
                        graph.Links.Remove(existing);
                        changed();
                        _drag = DragMode.Link;
                        _linkFrom = (existing.FromNode, existing.FromPort, true);
                    }
                    else { _drag = DragMode.Link; _linkFrom = (pin.Node.Id, port, false); }
                }
            }
            else if (nodeAt(mouseGraph) is { } node)
            {
                if (io.KeyCtrl) { if (!Selected.Remove(node.Id)) Selected.Add(node.Id); }
                else if (!Selected.Contains(node.Id)) { Selected.Clear(); Selected.Add(node.Id); }
                // 上に描く (一番後ろへ)
                graph.Nodes.Remove(node);
                graph.Nodes.Add(node);
                _drag = DragMode.Move;
            }
            else
            {
                if (!io.KeyCtrl) Selected.Clear();
                _drag = DragMode.Box;
            }
        }
        switch (_drag)
        {
        case DragMode.Move:
            if (io.MouseDelta != Vector2.Zero && ImGui.IsMouseDown(ImGuiMouseButton.Left))
            {
                foreach (var id in Selected) if (graph.Find(id) is { } n) n.Position += io.MouseDelta / Zoom;
                _moved = true;
            }
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                _drag = DragMode.None;
                if (_moved) { foreach (var id in Selected) if (graph.Find(id) is { } n) n.Position = new Vector2(MathF.Round(n.Position.X), MathF.Round(n.Position.Y)); changed(); commit(); }
            }
            break;
        case DragMode.Link:
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                _drag = DragMode.None;
                if (pinAt(mouseGraph) is { } target && target.IsOutput != _linkFrom.IsOutput)
                {
                    var ok = _linkFrom.IsOutput
                        ? tryConnect(_linkFrom.Node, _linkFrom.Port, target.Node.Id, target.Def.Inputs[target.Index].Name)
                        : tryConnect(target.Node.Id, target.Def.Outputs[target.Index].Name, _linkFrom.Node, _linkFrom.Port);
                    if (!ok) commit();
                }
                else if (hovered && nodeAt(mouseGraph) == null && Vector2.Distance(mouse, _dragStart) > 6.0f)
                {
                    // 何も無い所で離した : ノードを足して繋ぐ
                    _popupPosition = mouseGraph;
                    openAddMenu(_linkFrom);
                }
                else commit();   // 外した線
            }
            break;
        case DragMode.Box:
            if (ImGui.IsMouseReleased(ImGuiMouseButton.Left))
            {
                _drag = DragMode.None;
                var a = toGraph(Vector2.Min(_dragStart, mouse));
                var b = toGraph(Vector2.Max(_dragStart, mouse));
                foreach (var node in graph.Nodes)
                {
                    var extent = ShaderGraph.Library.TryGetValue(node.Type, out var def) ? new Vector2(widthOf(def), heightOf(def)) : new Vector2(140, 50);
                    if (node.Position.X <= b.X && node.Position.Y <= b.Y && node.Position.X + extent.X >= a.X && node.Position.Y + extent.Y >= a.Y) Selected.Add(node.Id);
                }
            }
            break;
        }
    }

    // 型が合わない物は繋がない (テクスチャ ↔ 数)
    private bool tryConnect(int fromNode, string fromPort, int toNode, string toPort)
    {
        var graph = Graph!;
        if (graph.Find(fromNode) is not { } from || graph.Find(toNode) is not { } to ||
            !ShaderGraph.Library.TryGetValue(from.Type, out var fromDef) || !ShaderGraph.Library.TryGetValue(to.Type, out var toDef)) return false;
        var o = Array.FindIndex(fromDef.Outputs, p => p.Name == fromPort);
        var i = Array.FindIndex(toDef.Inputs, p => p.Name == toPort);
        if (o < 0 || i < 0) return false;
        var outType = typeOf(from, fromDef, o);
        var inType = (int)toDef.Inputs[i].Type;
        if ((outType >= 16 || inType >= 16) && outType != inType) { _status = $"Cannot connect {(outType >= 16 ? ((PortType)outType).ToString() : "a number")} to {toPort}"; return false; }
        return Connect(fromNode, fromPort, toNode, toPort);
    }

    private void openAddMenu((int Node, string Port, bool IsOutput)? pending)
    {
        _pendingLink = pending;
        _search = "";
        _focusSearch = true;
        ImGui.OpenPopup("##addNode");
    }

    private void drawPopups()
    {
        if (ImGui.BeginPopup("##nodeMenu"))
        {
            if (ImGui.MenuItem("Delete", "Delete")) DeleteSelected();
            if (ImGui.MenuItem("Duplicate", "Ctrl+D")) DuplicateSelected();
            if (ImGui.MenuItem("Disconnect"))
            {
                Graph!.Links.RemoveAll(l => Selected.Contains(l.FromNode) || Selected.Contains(l.ToNode));
                changed();
                commit();
            }
            ImGui.EndPopup();
        }
        if (ImGui.BeginPopup("##addNode"))
        {
            if (_focusSearch) { ImGui.SetKeyboardFocusHere(); _focusSearch = false; }
            ImGui.SetNextItemWidth(220);
            ImGui.InputTextWithHint("##search", "Search nodes", ref _search, 64);
            string? chosen = null;
            if (_search.Length > 0)
            {
                foreach (var def in candidates().Where(d => d.Title.Contains(_search, StringComparison.OrdinalIgnoreCase) || d.Type.Contains(_search, StringComparison.OrdinalIgnoreCase)).Take(30))
                {
                    if (ImGui.Selectable($"{def.Title}##{def.Type}")) chosen = def.Type;
                    if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{def.Category} : {def.Help}");
                }
                if (ImGui.IsKeyPressed(ImGuiKey.Enter) && candidates().FirstOrDefault(d => d.Title.Contains(_search, StringComparison.OrdinalIgnoreCase)) is { } first) chosen = first.Type;
            }
            else
            {
                foreach (var category in ShaderGraph.Categories)
                {
                    var defs = candidates().Where(d => d.Category == category).ToList();
                    if (defs.Count == 0 || !ImGui.BeginMenu(category)) continue;
                    foreach (var def in defs)
                    {
                        if (ImGui.MenuItem(def.Title)) chosen = def.Type;
                        if (ImGui.IsItemHovered()) ImGui.SetTooltip(def.Help);
                    }
                    ImGui.EndMenu();
                }
            }
            if (chosen != null)
            {
                ImGui.CloseCurrentPopup();
                addFromMenu(chosen);
            }
            ImGui.EndPopup();
        }
        else _pendingLink = null;
    }

    // 線を引いている時は、その線を繋げられるノードだけ
    private IEnumerable<NodeDef> candidates() => ShaderGraph.Library.Values.Where(def =>
    {
        if (_pendingLink is not { } pending) return true;
        if (Graph!.Find(pending.Node) is not { } node || !ShaderGraph.Library.TryGetValue(node.Type, out var pendingDef)) return true;
        if (pending.IsOutput)
        {
            var o = Array.FindIndex(pendingDef.Outputs, p => p.Name == pending.Port);
            var type = o >= 0 ? typeOf(node, pendingDef, o) : 1;
            return def.Inputs.Any(p => type >= 16 ? (int)p.Type == type : (int)p.Type < 16);
        }
        var input = pendingDef.Inputs.FirstOrDefault(p => p.Name == pending.Port);
        var inType = input != null ? (int)input.Type : 1;
        return def.Outputs.Any(p => inType >= 16 ? (int)p.Type == inType : (int)p.Type < 16);
    });

    private void addFromMenu(string type)
    {
        var graph = Graph!;
        var def = ShaderGraph.Library[type];
        var position = _popupPosition;
        if (_pendingLink is { IsOutput: false }) position -= new Vector2(widthOf(def), 0);   // 入力から引いた : 新しいノードは左
        var node = graph.Add(type, new Vector2(MathF.Round(position.X), MathF.Round(position.Y - HeaderHeight)));
        Selected.Clear();
        Selected.Add(node.Id);
        if (_pendingLink is { } pending && graph.Find(pending.Node) is { } other && ShaderGraph.Library.TryGetValue(other.Type, out var otherDef))
        {
            if (pending.IsOutput)
            {
                var o = Array.FindIndex(otherDef.Outputs, p => p.Name == pending.Port);
                var outType = o >= 0 ? typeOf(other, otherDef, o) : 1;
                var input = def.Inputs.FirstOrDefault(p => outType >= 16 ? (int)p.Type == outType : (int)p.Type < 16);
                if (input != null) graph.Connect(other.Id, pending.Port, node.Id, input.Name);
            }
            else
            {
                var inType = (int)(otherDef.Inputs.FirstOrDefault(p => p.Name == pending.Port)?.Type ?? PortType.Float);
                var output = def.Outputs.FirstOrDefault(p => inType >= 16 ? (int)p.Type == inType : (int)p.Type < 16);
                if (output != null) graph.Connect(node.Id, output.Name, other.Id, pending.Port);
            }
        }
        _pendingLink = null;
        changed();
        commit();
    }

    private void handleKeys()
    {
        var io = ImGui.GetIO();
        if (io.WantTextInput) return;
        if (io.KeyCtrl)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Z)) { if (io.KeyShift) Redo(); else Undo(); }
            if (ImGui.IsKeyPressed(ImGuiKey.Y)) Redo();
            if (ImGui.IsKeyPressed(ImGuiKey.D)) DuplicateSelected();
            if (ImGui.IsKeyPressed(ImGuiKey.S)) Flush();
            if (ImGui.IsKeyPressed(ImGuiKey.A)) { Selected.Clear(); Selected.UnionWith(Graph!.Nodes.Select(n => n.Id)); }
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Delete) || ImGui.IsKeyPressed(ImGuiKey.Backspace)) DeleteSelected();
        if (ImGui.IsKeyPressed(ImGuiKey.F)) FrameAll();
    }
}
