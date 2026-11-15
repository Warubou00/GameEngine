using System.Numerics;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// Render Graph Viewer (v1.12 P3。Unity 6 の Render Graph Viewer に当たる窓)
//   横 = 描画パス (実行した順)、縦 = リソース (レンダーターゲット / 画面 / シーンの深度 / テクスチャ) の格子で、
//   どのパスがどのリソースをどう使ったか (書く / 読む / 消す / 深度 / MSAA の解決) と、遷移バリア (D3D12 の状態) を出す
//   - セル : 赤 = 書く / 緑 = 読む / 黄 = 読み書き / C = 消す / R = MSAA の解決 / 輪郭だけの緑 = 命令に無い読み (影) / 橙の三角 = バリア
//            灰色の帯 = 寿命 (最初に使ったパス 〜 最後に使ったパス)
//   - パスの番号が青 : 一つ前のパスと描画先が同じ (まとめられる候補。Unity の Native Render Pass の合体に当たる)
//   - 右 : 選んだパス / リソースの詳細 (状態の移り変わり、中身の絵)、パスの一覧、気になる所 (書いたのに読まれない など)
//   - Live : 一定の間隔 (既定 0.5 秒) で表を差し替える。その間に記録したフレームのうち、一番多くのパスを含む物を代表にし、
//            形 (パスの並び / 読み書き / バリア / リソース) が前と変わった時だけ作り直す
//            (以前は毎フレーム差し替えていたので、影の遠い段を間引くフレームと描くフレームでパスの列が出たり消えたりしてチラついた)
//   C++ (CommandContext_Passes.cpp) が窓が開いている間だけ記録する。エンジンは自動のバリア / パスの削除をしないので、
//   これは「実際に実行した命令の列」をそのまま見せる物 (Unity の RenderGraph のようなパスの削除やメモリの使い回しは無い)
// ==========================================================================
internal sealed class RenderGraphViewer
{
    public bool Show;

    private RenderGraphSnapshot? _snapshot;
    private bool   _live = true;
    private bool   _hideUnused = true;
    private bool   _showBarriers = true;
    private string _filter = "";
    private int    _selectedPass = -1;          // 記録の中のパスの番号
    private uint?  _selectedResource;           // リソースの Id
    private bool   _previewOn = true;

    // 表にする形 (記録が変わった時だけ作り直す)
    private int[]  _columns = [];               // 列 → パスの番号 (直接使った物があるパスだけ。-1 = パスの外)
    private RenderGraphResourceInfo[] _rows = [];
    private Dictionary<(int Column, uint Resource), (RenderGraphAccessKind Kind, List<RenderGraphAccessInfo>? Barriers)> _cells = new();
    private Dictionary<uint, (int First, int Last)> _lifetimes = new();
    private bool[] _mergeWithPrevious = [];
    private List<string> _warnings = new();
    private int _builtFrame = -1;
    private readonly List<PassTimingSample> _timings = new();

    // Live の差し替え (チラつかないように)
    private static readonly float[] LiveIntervals = [0.25f, 0.5f, 1.0f, 2.0f];
    private int    _liveInterval = 1;                 // LiveIntervals の番号 (既定 0.5 秒)
    private RenderGraphSnapshot? _candidate;          // 今の間隔の中で一番多くのパスを含む記録
    private long   _intervalStart;                    // Stopwatch の時刻
    private long   _signature;                        // 表に出している記録の形
    private int    _replaceCount;                     // 表を差し替えた回数 (確認用)

    /// <summary>最後に読んだ記録 (Memory の窓も使う)</summary>
    public RenderGraphSnapshot? Snapshot => _snapshot;

    /// <summary>毎フレーム (窓を描く前)。memoryWantsCapture = Memory の窓がリソースの一覧に使う</summary>
    public void Update(bool memoryWantsCapture)
    {
        RenderGraphCapture.Capture = Show || memoryWantsCapture;
        if (!RenderGraphCapture.Capture)
        {
            RenderGraphCapture.SetPreviewTarget(null);
            return;
        }
        if (_live || _snapshot == null || !Show) updateLive();
        // 中身の絵 : 選んだリソースだけを UI の前に読める状態にする
        RenderGraphResourceInfo? preview = null;
        if (Show && _previewOn && _selectedResource is { } id && _snapshot != null)
            foreach (var r in _snapshot.Resources) if (r.Id == id) preview = r;
        RenderGraphCapture.SetPreviewTarget(preview);
    }

    // Live : 毎フレーム読むが、表は間隔ごとに、形が変わった時だけ差し替える
    private void updateLive()
    {
        var frame = RenderGraphCapture.Read();
        if (frame != null && (_candidate == null || score(frame) > score(_candidate))) _candidate = frame;
        var now = System.Diagnostics.Stopwatch.GetTimestamp();
        var elapsed = (now - _intervalStart) / (double)System.Diagnostics.Stopwatch.Frequency;
        if (_snapshot != null && elapsed < LiveIntervals[_liveInterval]) return;
        _intervalStart = now;
        if (_candidate == null) return;
        var signature = signatureOf(_candidate);
        if (_snapshot == null || signature != _signature)
        {
            _snapshot  = _candidate;
            _signature = signature;
            _replaceCount++;
        }
        _candidate = null;
    }

    // 代表の選び方 : パスの多い物 (影の段を全部描いたフレーム) → 読み書きの多い物
    private static long score(RenderGraphSnapshot s) => s.Passes.Length * 1_000_000L + s.Accesses.Length;

    // 形 : パスの名前と親 / 読み書きとバリア / リソースの大きさと形式 (中身の値は含まない)
    private static long signatureOf(RenderGraphSnapshot s)
    {
        var hash = new HashCode();
        foreach (var p in s.Passes) { hash.Add(p.Name); hash.Add(p.Parent); }
        foreach (var a in s.Accesses) { hash.Add(a.Pass); hash.Add(a.ResourceId); hash.Add((uint)a.Kind); hash.Add(a.StateBefore); hash.Add(a.StateAfter); }
        foreach (var r in s.Resources) { hash.Add(r.Id); hash.Add(r.Name); hash.Add(r.Width); hash.Add(r.Height); hash.Add(r.Format); hash.Add((uint)r.Flags); hash.Add(r.Bytes); }
        return ((long)hash.ToHashCode() << 32) ^ (s.Passes.Length * 7919L + s.Accesses.Length);
    }

    public void Draw()
    {
        if (!Show) return;
        ImGui.SetNextWindowSize(new Vector2(1100, 560), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(1.0f);   // 格子の行は背景が無いので、浮かせた時に後ろの窓が透けないように
        if (TestPlacement is { } placement)
        {
            ImGui.SetNextWindowDockID(0, ImGuiCond.Always);
            ImGui.SetNextWindowPos(placement.Position, ImGuiCond.Always);
            ImGui.SetNextWindowSize(placement.Size, ImGuiCond.Always);
            ImGui.SetNextWindowFocus();
            TestPlacement = null;
        }
        if (ImGui.Begin("Render Graph Viewer", ref Show))
        {
            drawToolbar();
            if (_snapshot == null)
            {
                ImGui.TextDisabled("(waiting for the first captured frame...)");
            }
            else
            {
                if (_builtFrame != _snapshot.FrameNumber) build(_snapshot);
                var avail = ImGui.GetContentRegionAvail();
                var detailWidth = Math.Min(380.0f, avail.X * 0.4f);
                if (ImGui.BeginChild("##grid", new Vector2(avail.X - detailWidth - 6.0f, avail.Y), ImGuiChildFlags.Borders))
                    drawGrid(_snapshot);
                ImGui.EndChild();
                ImGui.SameLine();
                if (ImGui.BeginChild("##details", new Vector2(detailWidth, avail.Y), ImGuiChildFlags.Borders))
                    drawDetails(_snapshot);
                ImGui.EndChild();
            }
        }
        ImGui.End();
    }

    // ---------------------------------------------------------------------
    // 上の帯
    // ---------------------------------------------------------------------
    private void drawToolbar()
    {
        ImGui.Checkbox("Live", ref _live);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Update the graph at the interval on the right, using the most complete frame of that interval\n(only when its structure changed). Off : keep showing the captured frame.");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(70.0f);
        if (ImGui.BeginCombo("##interval", $"{LiveIntervals[_liveInterval]:0.##} s"))
        {
            for (int i = 0; i < LiveIntervals.Length; i++) if (ImGui.Selectable($"{LiveIntervals[i]:0.##} s", i == _liveInterval)) _liveInterval = i;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        if (ImGui.Button("Capture"))
        {
            // 今のフレームで止める (Live を切る)。差し替えの判定はしない
            if (RenderGraphCapture.Read() is { } frame) { _snapshot = frame; _signature = signatureOf(frame); _replaceCount++; }
            _live = false;
        }
        ImGui.SameLine();
        if (ImGui.Checkbox("Hide unused", ref _hideUnused)) _builtFrame = -1;   // 行を作り直す (以前は次の記録まで変わらなかった)
        ImGui.SameLine();
        if (ImGui.Checkbox("Barriers", ref _showBarriers)) _builtFrame = -1;
        ImGui.SameLine();
        ImGui.SetNextItemWidth(160.0f);
        if (ImGui.InputTextWithHint("##filter", "filter resources", ref _filter, 64)) _builtFrame = -1;
        if (_snapshot != null)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"{_snapshot.Passes.Length} passes, {_snapshot.Resources.Count(r => r.IsUsed)} resources used, {_snapshot.Accesses.Count(a => a.IsBarrier)} barriers");
        }
        // 凡例
        var draw = ImGui.GetWindowDrawList();
        void legend(uint color, string text, bool outline = false)
        {
            var p = ImGui.GetCursorScreenPos() + new Vector2(0, 3);
            if (outline) draw.AddRect(p, p + new Vector2(10, 10), color, 0, 0, 2.0f);
            else draw.AddRectFilled(p, p + new Vector2(10, 10), color);
            ImGui.Dummy(new Vector2(12, 14));
            ImGui.SameLine();
            ImGui.TextDisabled(text);
            ImGui.SameLine();
        }
        legend(WriteColor, "write");
        legend(ReadColor, "read");
        legend(ReadWriteColor, "read + write");
        legend(ReadColor, "implicit read", outline: true);
        legend(BarrierColor, "barrier");
        legend(LifetimeColor, "lifetime");
        legend(MergeColor, "pass # : same targets as the previous pass (merge candidate)");
        ImGui.NewLine();
    }

    private const uint WriteColor     = 0xFF4050E0u;   // ABGR
    private const uint ReadColor      = 0xFF50C850u;
    private const uint ReadWriteColor = 0xFF30C8E8u;
    private const uint BarrierColor   = 0xFF2090FFu;
    private const uint LifetimeColor  = 0x50A0A0A0u;
    private const uint MergeColor     = 0xFFFFA040u;

    // ---------------------------------------------------------------------
    // 記録 → 表の形
    // ---------------------------------------------------------------------
    private void build(RenderGraphSnapshot s)
    {
        _builtFrame = s.FrameNumber;
        // 列 : 読み書きかバリアがあるパス (実行した順 = 番号の順)。パスの外は最後
        var usedPasses = new SortedSet<int>();
        var outside = false;
        foreach (var a in s.Accesses)
        {
            if (a.IsBarrier && !_showBarriers) continue;
            if (a.Pass < 0) outside = true; else usedPasses.Add(a.Pass);
        }
        _columns = usedPasses.Concat(outside ? [-1] : Array.Empty<int>()).ToArray();
        var columnOf = new Dictionary<int, int>();
        for (int c = 0; c < _columns.Length; c++) columnOf[_columns[c]] = c;

        _cells = new();
        _lifetimes = new();
        foreach (var a in s.Accesses)
        {
            if (a.IsBarrier && !_showBarriers) continue;
            var column = columnOf[a.Pass];
            _cells.TryGetValue((column, a.ResourceId), out var cell);
            if (a.IsBarrier) (cell.Barriers ??= new()).Add(a);
            else
            {
                cell.Kind |= a.Kind;
                var life = _lifetimes.TryGetValue(a.ResourceId, out var l) ? l : (column, column);
                _lifetimes[a.ResourceId] = (Math.Min(life.Item1, column), Math.Max(life.Item2, column));
            }
            _cells[(column, a.ResourceId)] = cell;
        }

        // 行 : 使った物 (隠さない時は全部)。名前で絞る。並びは最初に使った列の順 (使っていない物は後ろ)
        _rows = s.Resources
            .Where(r => (!_hideUnused || r.IsUsed) && (_filter.Length == 0 || r.Name.Contains(_filter, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(r => _lifetimes.TryGetValue(r.Id, out var l) ? l.First : int.MaxValue)
            .ThenBy(r => r.Name, StringComparer.Ordinal)
            .ToArray();

        // まとめられる候補 : 一つ前の列と、書いた描画先 (色 + 深度) の組が同じ
        _mergeWithPrevious = new bool[_columns.Length];
        HashSet<uint>? previous = null;
        for (int c = 0; c < _columns.Length; c++)
        {
            var targets = new HashSet<uint>();
            foreach (var ((column, resource), cell) in _cells)
                if (column == c && (cell.Kind & (RenderGraphAccessKind.Write | RenderGraphAccessKind.DepthWrite | RenderGraphAccessKind.DepthTest)) != 0) targets.Add(resource);
            _mergeWithPrevious[c] = previous != null && targets.Count > 0 && _columns[c] >= 0 && targets.SetEquals(previous);
            previous = targets.Count > 0 ? targets : null;
        }

        // 気になる所
        _warnings = new();
        foreach (var r in s.Resources)
        {
            if (!r.IsUsed || r.IsImported) continue;
            // 最後に書いた後に読まれていない (このフレームの中で)。描いた絵を次のフレームで使う物 (履歴) なら問題ない
            int lastWrite = -1, lastRead = -1, firstRead = -1, firstWrite = -1;
            foreach (var a in s.Accesses)
            {
                if (a.ResourceId != r.Id || a.IsBarrier) continue;
                var c = columnOf[a.Pass];
                if ((a.Kind & RenderGraphAccessKind.AnyWrite) != 0) { lastWrite = Math.Max(lastWrite, c); if (firstWrite < 0) firstWrite = c; }
                if ((a.Kind & RenderGraphAccessKind.AnyRead) != 0)  { lastRead = Math.Max(lastRead, c);  if (firstRead < 0) firstRead = c; }
            }
            if (lastWrite >= 0 && lastRead < lastWrite && !r.IsDepth)
                _warnings.Add($"{r.Name} : written in {passLabel(s, _columns[lastWrite])} but not read afterwards in this frame (unused output, or kept for the next frame)");
            if (firstRead >= 0 && (firstWrite < 0 || firstRead < firstWrite))
                _warnings.Add($"{r.Name} : read in {passLabel(s, _columns[firstRead])} before it is written this frame (uses the previous frame's contents)");
        }
        if (outside) _warnings.Add("Some accesses happen outside any BeginPass / EndPass (column '-').");
    }

    private static string passLabel(RenderGraphSnapshot s, int pass) => pass < 0 ? "(outside passes)" : $"#{pass} {s.Passes[pass].Name}";

    // 親のパスもたどった名前 (例 : "Camera 0 / Shadows / Cascade 0")
    private static string passPath(RenderGraphSnapshot s, int pass)
    {
        if (pass < 0) return "(outside passes)";
        var names = new List<string>();
        for (var p = pass; p >= 0 && names.Count < 16; p = s.Passes[p].Parent) names.Add(s.Passes[p].Name);
        names.Reverse();
        return string.Join(" / ", names);
    }

    // ---------------------------------------------------------------------
    // 格子
    // ---------------------------------------------------------------------
    private void drawGrid(RenderGraphSnapshot s)
    {
        const float cell = 24.0f;
        if (_columns.Length == 0 || _rows.Length == 0) { ImGui.TextDisabled("(nothing recorded)"); return; }
        var flags = ImGuiTableFlags.ScrollX | ImGuiTableFlags.ScrollY | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.RowBg | ImGuiTableFlags.SizingFixedFit;
        if (!ImGui.BeginTable("##graph", _columns.Length + 1, flags)) return;
        ImGui.TableSetupScrollFreeze(1, 2);
        ImGui.TableSetupColumn("Resource", ImGuiTableColumnFlags.WidthFixed, 200.0f);
        for (int c = 0; c < _columns.Length; c++) ImGui.TableSetupColumn($"##c{c}", ImGuiTableColumnFlags.WidthFixed, cell);

        // 1 行目 : 一番外の親のパス (Camera 0 など) の名前を、そのパスの最初の列に出す
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        ImGui.TableSetColumnIndex(0);
        ImGui.TextDisabled("pass group");
        var lastRoot = -2;
        for (int c = 0; c < _columns.Length; c++)
        {
            var root = rootOf(s, _columns[c]);
            if (root == lastRoot) continue;
            lastRoot = root;
            ImGui.TableSetColumnIndex(c + 1);
            ImGui.TextColored(new Vector4(0.75f, 0.75f, 0.85f, 1.0f), root < 0 ? "-" : s.Passes[root].Name);   // 列の幅を越えて右へはみ出して書く
        }

        // 2 行目 : パスの番号 (青 = 前のパスとまとめられる候補)。クリックで選ぶ
        ImGui.TableNextRow(ImGuiTableRowFlags.Headers);
        ImGui.TableSetColumnIndex(0);
        ImGui.TextDisabled("resource \\ pass #");
        for (int c = 0; c < _columns.Length; c++)
        {
            ImGui.TableSetColumnIndex(c + 1);
            var pass = _columns[c];
            var label = pass < 0 ? "-" : pass.ToString();
            if (_mergeWithPrevious[c]) ImGui.PushStyleColor(ImGuiCol.Text, 0xFFFFA040u);
            if (ImGui.Selectable($"{label}##p{c}", _selectedPass == pass && pass >= 0, ImGuiSelectableFlags.None, new Vector2(cell - 4, 0))) _selectedPass = pass;
            if (_mergeWithPrevious[c]) ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(passPath(s, pass) + (_mergeWithPrevious[c] ? "\nSame render targets as the previous pass (merge candidate)" : ""));
        }

        // リソースの行
        var draw = ImGui.GetWindowDrawList();
        foreach (var r in _rows)
        {
            ImGui.TableNextRow(ImGuiTableRowFlags.None, cell);
            ImGui.TableSetColumnIndex(0);
            var selectedRow = _selectedResource == r.Id;
            var color = r.IsImported ? new Vector4(0.6f, 0.8f, 1.0f, 1.0f) : r.IsUsed ? new Vector4(1, 1, 1, 1) : new Vector4(0.55f, 0.55f, 0.55f, 1.0f);
            ImGui.PushStyleColor(ImGuiCol.Text, color);
            if (ImGui.Selectable($"{r.Name}##r{r.Id}", selectedRow, ImGuiSelectableFlags.None, new Vector2(0, cell - 4))) _selectedResource = r.Id;
            ImGui.PopStyleColor();
            if (ImGui.IsItemHovered()) ImGui.SetTooltip($"{r.Name}\n{r.Format} {r.Width}x{r.Height}{(r.Samples > 1 ? $" MSAA {r.Samples}x" : "")}{(r.ArraySize > 1 ? $" x{r.ArraySize}" : "")}\n{ProfilerWindows.Bytes(r.Bytes)}{(r.IsImported ? "\nimported (not owned by the pipeline)" : "")}");

            var life = _lifetimes.TryGetValue(r.Id, out var l) ? l : (First: -1, Last: -1);
            for (int c = 0; c < _columns.Length; c++)
            {
                ImGui.TableSetColumnIndex(c + 1);
                var min = ImGui.GetCursorScreenPos();
                var max = min + new Vector2(cell - 2, cell - 4);
                if (ImGui.InvisibleButton($"##cell{r.Id}_{c}", max - min))
                {
                    _selectedResource = r.Id;
                    if (_columns[c] >= 0) _selectedPass = _columns[c];
                }
                var hovered = ImGui.IsItemHovered();
                if (_selectedPass == _columns[c] && _columns[c] >= 0) draw.AddRectFilled(min, max, 0x30FFFFFFu);
                if (selectedRow) draw.AddRectFilled(min, max, 0x20FFFFFFu);
                if (c >= life.First && c <= life.Last && life.First >= 0) draw.AddRectFilled(min + new Vector2(0, 8), max - new Vector2(0, 8), LifetimeColor);
                if (!_cells.TryGetValue((c, r.Id), out var data)) continue;
                drawCell(draw, min, max, data.Kind, data.Barriers);
                if (hovered) ImGui.SetTooltip(cellTooltip(s, _columns[c], r, data.Kind, data.Barriers));
            }
        }
        ImGui.EndTable();
    }

    private static int rootOf(RenderGraphSnapshot s, int pass)
    {
        if (pass < 0) return -1;
        var p = pass;
        for (int guard = 0; guard < 32 && s.Passes[p].Parent >= 0; guard++) p = s.Passes[p].Parent;
        return p;
    }

    private static void drawCell(ImDrawListPtr draw, Vector2 min, Vector2 max, RenderGraphAccessKind kind, List<RenderGraphAccessInfo>? barriers)
    {
        var write = (kind & RenderGraphAccessKind.AnyWrite) != 0;
        var read  = (kind & RenderGraphAccessKind.AnyRead) != 0;
        var inner = new Vector2(3, 3);
        if (write || read)
        {
            var color = write && read ? ReadWriteColor : write ? WriteColor : ReadColor;
            if (!write && (kind & RenderGraphAccessKind.Implicit) != 0) draw.AddRect(min + inner, max - inner, color, 2.0f, 0, 2.0f);
            else draw.AddRectFilled(min + inner, max - inner, color, 2.0f);
            // 深度は斜線、消すは C、解決は R
            if ((kind & (RenderGraphAccessKind.DepthWrite | RenderGraphAccessKind.DepthTest)) != 0)
                draw.AddLine(new Vector2(min.X + 4, max.Y - 4), new Vector2(max.X - 4, min.Y + 4), 0xC0000000u, 2.0f);
            if ((kind & RenderGraphAccessKind.Clear) != 0) draw.AddText(min + new Vector2(6, 2), 0xFF000000u, "C");
            else if ((kind & RenderGraphAccessKind.Resolve) != 0) draw.AddText(min + new Vector2(6, 2), 0xFF000000u, "R");
        }
        if (barriers is { Count: > 0 })
            draw.AddTriangleFilled(min, min + new Vector2(9, 0), min + new Vector2(0, 9), BarrierColor);
    }

    private static string kindText(RenderGraphAccessKind kind)
    {
        var parts = new List<string>();
        if ((kind & RenderGraphAccessKind.Clear) != 0)      parts.Add("Clear");
        if ((kind & RenderGraphAccessKind.Write) != 0)      parts.Add("Write (render target)");
        if ((kind & RenderGraphAccessKind.DepthWrite) != 0) parts.Add("Depth write");
        if ((kind & RenderGraphAccessKind.DepthTest) != 0)  parts.Add("Depth test");
        if ((kind & RenderGraphAccessKind.Read) != 0)       parts.Add((kind & RenderGraphAccessKind.Implicit) != 0 ? "Read (implicit : shadow map)" : "Read (shader resource)");
        if ((kind & RenderGraphAccessKind.Resolve) != 0)    parts.Add("MSAA resolve");
        return parts.Count == 0 ? "-" : string.Join(", ", parts);
    }

    private static string barrierText(RenderGraphAccessInfo b) =>
        $"{RenderGraphCapture.StateName(b.StateBefore)} -> {RenderGraphCapture.StateName(b.StateAfter)}{((b.Kind & RenderGraphAccessKind.ResolvedCopy) != 0 ? "  (resolved copy)" : "")}";

    private static string cellTooltip(RenderGraphSnapshot s, int pass, RenderGraphResourceInfo r, RenderGraphAccessKind kind, List<RenderGraphAccessInfo>? barriers)
    {
        var text = $"{passPath(s, pass)}\n{r.Name}\n{kindText(kind)}";
        if (barriers != null) foreach (var b in barriers) text += "\nbarrier : " + barrierText(b);
        return text;
    }

    // ---------------------------------------------------------------------
    // 右 : 詳細
    // ---------------------------------------------------------------------
    private void drawDetails(RenderGraphSnapshot s)
    {
        if (!ImGui.BeginTabBar("##details")) return;
        if (ImGui.BeginTabItem("Resource"))
        {
            drawResourceDetails(s);
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Pass"))
        {
            drawPassDetails(s);
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem("Passes"))
        {
            for (int i = 0; i < s.Passes.Length; i++)
            {
                var p = s.Passes[i];
                if (ImGui.Selectable($"{new string(' ', p.Depth * 3)}#{i} {p.Name}##list{i}", _selectedPass == i)) _selectedPass = i;
            }
            ImGui.EndTabItem();
        }
        if (ImGui.BeginTabItem($"Warnings ({_warnings.Count})"))
        {
            if (_warnings.Count == 0) ImGui.TextDisabled("(none)");
            foreach (var w in _warnings) ImGui.TextWrapped("- " + w);
            ImGui.EndTabItem();
        }
        ImGui.EndTabBar();
    }

    private void drawResourceDetails(RenderGraphSnapshot s)
    {
        RenderGraphResourceInfo? found = null;
        foreach (var r in s.Resources) if (r.Id == _selectedResource) found = r;
        if (found is not { } res) { ImGui.TextDisabled("Click a resource (row) or a cell."); return; }

        ImGui.TextUnformatted(res.Name);
        ImGui.TextDisabled($"{res.Format}  {res.Width} x {res.Height}{(res.Samples > 1 ? $"  MSAA {res.Samples}x" : "")}{(res.ArraySize > 1 ? $"  array {res.ArraySize}" : "")}");
        ImGui.TextDisabled($"{ProfilerWindows.Bytes(res.Bytes)}  {(res.IsImported ? "imported" : "pipeline")}{(res.IsDepth ? "  depth" : "")}  id {res.Id:X}");
        if (_lifetimes.TryGetValue(res.Id, out var life))
            ImGui.TextDisabled($"lifetime : {passLabel(s, _columns[life.First])}  ->  {passLabel(s, _columns[life.Last])}");

        // 中身 (フレームの終わり = UI を描く時の絵)
        if (res.CanPreview)
        {
            ImGui.Checkbox("Preview (end of frame)", ref _previewOn);
            if (_previewOn)
            {
                var width = ImGui.GetContentRegionAvail().X;
                var aspect = res.Height > 0 ? res.Width / (float)res.Height : 1.0f;
                ImGui.Image((IntPtr)res.TextureId, new Vector2(width, width / Math.Max(aspect, 0.01f)));
                if (res.Format is "RGBA16F" or "R11G11B10F" or "RG16F" or "R16F") ImGui.TextDisabled("HDR values above 1 are clipped in the preview.");
            }
        }
        else ImGui.TextDisabled(res.IsDepth ? "(depth : no preview)" : "(no preview for this resource)");

        // 使われ方の一覧 (実行した順)
        ImGui.SeparatorText("Accesses");
        if (!ImGui.BeginTable("##acc", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp)) return;
        for (int i = 0; i < s.Accesses.Length; i++)
        {
            var a = s.Accesses[i];
            if (a.ResourceId != res.Id) continue;
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            if (ImGui.Selectable($"{passLabel(s, a.Pass)}##a{i}", _selectedPass == a.Pass, ImGuiSelectableFlags.SpanAllColumns)) _selectedPass = a.Pass;
            ImGui.TableSetColumnIndex(1);
            if (a.IsBarrier) ImGui.TextColored(new Vector4(1.0f, 0.6f, 0.2f, 1.0f), barrierText(a));
            else ImGui.TextUnformatted(kindText(a.Kind));
        }
        ImGui.EndTable();
    }

    private void drawPassDetails(RenderGraphSnapshot s)
    {
        if (_selectedPass < 0 || _selectedPass >= s.Passes.Length) { ImGui.TextDisabled("Click a pass number or a cell."); return; }
        var pass = s.Passes[_selectedPass];
        ImGui.TextUnformatted($"#{_selectedPass} {pass.Name}");
        ImGui.TextDisabled(passPath(s, _selectedPass));
        // 時間 (P2 の記録。数フレーム前の値。同じ並びのフレームなら同じ番号で引く)
        Profiler.ReadPassTimings(_timings);
        if (_selectedPass < _timings.Count && _timings[_selectedPass].Name == pass.Name)
        {
            var t = _timings[_selectedPass];
            ImGui.TextDisabled($"GPU {(t.HasGpu ? $"{t.GpuMilliseconds:F3} ms" : "- (open GPU Usage to measure)")}   CPU record {t.CpuMilliseconds:F3} ms");
        }
        // 子のパス
        var children = s.Passes.Where(p => p.Parent == _selectedPass).ToArray();
        if (children.Length > 0)
        {
            ImGui.SeparatorText("Sub passes");
            foreach (var c in children) if (ImGui.Selectable($"#{c.Index} {c.Name}##child{c.Index}")) _selectedPass = c.Index;
        }
        ImGui.SeparatorText("Resources");
        if (!ImGui.BeginTable("##passacc", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp)) return;
        for (int i = 0; i < s.Accesses.Length; i++)
        {
            var a = s.Accesses[i];
            if (a.Pass != _selectedPass) continue;
            var name = $"#{a.ResourceId:X}";
            foreach (var r in s.Resources) if (r.Id == a.ResourceId) name = r.Name;
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            if (ImGui.Selectable($"{name}##pa{i}", _selectedResource == a.ResourceId, ImGuiSelectableFlags.SpanAllColumns)) _selectedResource = a.ResourceId;
            ImGui.TableSetColumnIndex(1);
            if (a.IsBarrier) ImGui.TextColored(new Vector4(1.0f, 0.6f, 0.2f, 1.0f), barrierText(a));
            else ImGui.TextUnformatted(kindText(a.Kind));
        }
        ImGui.EndTable();
    }

    // ---------- 確認用 ----------
    public (Vector2 Position, Vector2 Size)? TestPlacement;   // 次のフレームで窓をこの位置 / 大きさに置く
    public int ColumnCount => _columns.Length;
    public int RowCount => _rows.Length;
    public IReadOnlyList<string> Warnings => _warnings;
    public bool Live { get => _live; set => _live = value; }
    public int ReplaceCount => _replaceCount;
    /// <summary>次の Update で、間隔を待たずに差し替えを判定する (確認用)</summary>
    public void UpdateNow() { _intervalStart = 0; _candidate = null; }   // それまでの候補 (古いフレーム) は使わない
    public uint? SelectedResource { get => _selectedResource; set => _selectedResource = value; }
    public bool MergeCandidateExists => _mergeWithPrevious.Any(m => m);
}
