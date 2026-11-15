using System.Numerics;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// Frame Debugger (v1.13 P4。Unity の Frame Debugger に当たる窓)
//   View > Analysis > Frame Debugger。Enable で、1 フレームの描画をイベント (消す / シーンの描画 1 回 / 全画面 / パーティクル /
//   自分のシェーダーの 1 個 …) ごとに止めて、その時の描画先 (色の何枚目か / 深度) を窓の中に出す
//   - 左 : イベントの一覧 (描画パスごと)。スライダー / ◀ ▶ / 窓にフォーカスがある時の ← → (↑ ↓) で 1 つずつ動かす
//   - 右 : 描画先の絵 (RT 0〜3 / Depth、R G B A、Levels)、イベントの詳細 (パス / 描画先 / メッシュの数 / 物 (Hierarchy で選ぶ) /
//          シェーダー / 入力 / 値)
//   - Enable の間 : 再生中なら一時停止する (閉じる / Disable で元に戻す)。ExecuteIndirect / GPU カリングを使わず 1 個ずつ描く
//   - 止めている間、シーンビューは最後まで描いたフレームの絵のまま (止めた所から後は描かないので)
//   C++ : CommandContext_FrameDebug.cpp
// ==========================================================================
internal sealed class FrameDebuggerWindow
{
    private readonly EditorApp _app;
    public bool Show;

    private bool _enabled;
    private bool _pausedByUs;
    private FrameDebugSnapshot? _list;      // 止めずに描いたフレームの全部のイベント
    private FrameDebugSnapshot? _current;   // 最後のフレーム (止めた所まで + 写した結果)
    private int  _selected = -1;
    private int  _listRequest;              // > 0 : 止めずに描いて一覧を取り直す (C++ の結果は 1 フレーム遅れるので数フレーム待つ)
    private bool _channelR = true, _channelG = true, _channelB = true, _channelA;
    private float _levelMin, _levelMax = 1.0f;
    private int  _slot;

    public FrameDebuggerWindow(EditorApp app) => _app = app;

    // ---------- 確認用 ----------
    public bool Enabled => _enabled;
    public FrameDebugSnapshot? List => _list;
    public FrameDebugSnapshot? Current => _current;
    public int Selected { get => _selected; set => _selected = value; }
    public int Slot { get => _slot; set => _slot = value; }
    public bool WaitingForList => _listRequest > 0;

    public void SetEnabled(bool enabled)
    {
        if (_enabled == enabled) return;
        _enabled = enabled;
        if (enabled)
        {
            // 再生中ならゲームを止める (毎フレーム同じフレームを描き直して止めるため)
            if (_app.IsPlaying && !_app.IsPaused) { _app.TogglePause(); _pausedByUs = true; }
            RequestList();
        }
        else
        {
            if (_pausedByUs && _app.IsPlaying && _app.IsPaused) _app.TogglePause();
            _pausedByUs = false;
            _list = _current = null;
        }
    }

    /// <summary>イベントの一覧を取り直す (シーンが変わった時)</summary>
    public void RequestList() => _listRequest = 4;

    /// <summary>毎フレーム (窓を描く前)。C++ に設定を送り、最後のフレームの結果を読む</summary>
    public void Update()
    {
        if (!Show && _enabled) SetEnabled(false);
        FrameDebugger.Enabled = _enabled;
        if (!_enabled)
        {
            FrameDebugger.StopEvent = -1;
            FrameDebugger.Apply();
            return;
        }
        _current = FrameDebugger.Read();
        if (_listRequest > 0)
        {
            // 止めずに描く。設定が効いたフレームの結果 (止めていない) が読めたら一覧にする
            _listRequest--;
            if (_listRequest == 0 && _current is { Result.Stopped: false } full)
            {
                _list = full;
                if (_selected < 0 || _selected >= full.Events.Length) _selected = full.Events.Length - 1;
            }
            else if (_listRequest == 0) _listRequest = 2;   // まだ読めない : もう少し待つ
            FrameDebugger.StopEvent = -1;
        }
        else
        {
            FrameDebugger.StopEvent = _list != null && _selected >= 0 ? Math.Min(_selected, _list.Events.Length - 1) : -1;
        }
        FrameDebugger.Slot        = _slot;
        FrameDebugger.ChannelMask = (_channelR ? 1u : 0u) | (_channelG ? 2u : 0u) | (_channelB ? 4u : 0u) | (_channelA ? 8u : 0u);
        FrameDebugger.LevelMin    = _levelMin;
        FrameDebugger.LevelMax    = _levelMax;
        FrameDebugger.Apply();
    }

    public void Draw()
    {
        if (!Show) return;
        ImGui.SetNextWindowSize(new Vector2(1100, 620), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowBgAlpha(1.0f);
        if (TestPlacement is { } placement)
        {
            ImGui.SetNextWindowDockID(0, ImGuiCond.Always);
            ImGui.SetNextWindowPos(placement.Position, ImGuiCond.Always);
            ImGui.SetNextWindowSize(placement.Size, ImGuiCond.Always);
            ImGui.SetNextWindowFocus();
            TestPlacement = null;
        }
        if (ImGui.Begin("Frame Debugger", ref Show))
        {
            drawToolbar();
            if (!_enabled)
            {
                ImGui.TextWrapped("Enable to step through the frame one draw at a time and see the render target at each event. " +
                                  "While enabled the game is paused and the scene is drawn without ExecuteIndirect / GPU culling.");
            }
            else if (_list == null)
            {
                ImGui.TextDisabled("(collecting the events of the frame...)");
            }
            else
            {
                handleKeys();
                var avail = ImGui.GetContentRegionAvail();
                var listWidth = Math.Min(420.0f, avail.X * 0.4f);
                if (ImGui.BeginChild("##events", new Vector2(listWidth, avail.Y), ImGuiChildFlags.Borders)) drawEvents(_list);
                ImGui.EndChild();
                ImGui.SameLine();
                if (ImGui.BeginChild("##view", new Vector2(avail.X - listWidth - 6.0f, avail.Y), ImGuiChildFlags.Borders)) drawView(_list);
                ImGui.EndChild();
            }
        }
        ImGui.End();
    }

    public (Vector2 Position, Vector2 Size)? TestPlacement;

    // ---------------------------------------------------------------------
    private void drawToolbar()
    {
        if (ImGui.Button(_enabled ? "Disable" : "Enable")) SetEnabled(!_enabled);
        if (!_enabled) return;
        ImGui.SameLine();
        if (ImGui.Button("Refresh")) RequestList();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Collect the event list again (after the scene changed)");
        if (_list == null) return;
        var count = _list.Events.Length;
        ImGui.SameLine();
        if (ImGui.ArrowButton("##prev", ImGuiDir.Left)) step(-1);
        ImGui.SameLine();
        if (ImGui.ArrowButton("##next", ImGuiDir.Right)) step(1);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Math.Max(120.0f, ImGui.GetContentRegionAvail().X - 140.0f));
        var selected = _selected;
        if (ImGui.SliderInt("##event", ref selected, 0, Math.Max(0, count - 1), $"{selected + 1} / {count}")) _selected = selected;
        ImGui.SameLine();
        ImGui.TextDisabled(_app.IsPlaying ? "(game paused)" : "");
    }

    private void step(int delta)
    {
        if (_list == null || _list.Events.Length == 0) return;
        _selected = Math.Clamp(_selected + delta, 0, _list.Events.Length - 1);
    }

    private void handleKeys()
    {
        if (!ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows) || ImGui.GetIO().WantTextInput) return;
        if (ImGui.IsKeyPressed(ImGuiKey.LeftArrow) || ImGui.IsKeyPressed(ImGuiKey.UpArrow)) step(-1);
        if (ImGui.IsKeyPressed(ImGuiKey.RightArrow) || ImGui.IsKeyPressed(ImGuiKey.DownArrow)) step(1);
    }

    // ---------------------------------------------------------------------
    // イベントの一覧 (パスが変わる所に親をたどった名前の見出し)
    // ---------------------------------------------------------------------
    private int _scrolledTo = -1;

    private void drawEvents(FrameDebugSnapshot list)
    {
        var lastPass = -2;
        for (int i = 0; i < list.Events.Length; i++)
        {
            var e = list.Events[i];
            if (e.Pass != lastPass)
            {
                lastPass = e.Pass;
                ImGui.TextColored(new Vector4(0.6f, 0.75f, 1.0f, 1.0f), passPath(list, e.Pass));
            }
            var depth = e.Pass >= 0 && e.Pass < list.Passes.Length ? list.Passes[e.Pass].Depth + 1 : 0;
            ImGui.Indent(depth * 8.0f + 8.0f);
            if (ImGui.Selectable($"{i + 1,4}  {Summary(e)}##ev{i}", i == _selected)) _selected = i;
            if (i == _selected && _scrolledTo != _selected) { ImGui.SetScrollHereY(0.5f); _scrolledTo = _selected; }
            ImGui.Unindent(depth * 8.0f + 8.0f);
        }
    }

    private static string passPath(FrameDebugSnapshot s, int pass)
    {
        if (pass < 0 || pass >= s.Passes.Length) return "(outside passes)";
        var names = new List<string>();
        for (var p = pass; p >= 0 && names.Count < 16; p = s.Passes[p].Parent) names.Add(s.Passes[p].Name);
        names.Reverse();
        return string.Join(" / ", names);
    }

    private static readonly string[] ScenePassNames = ["Forward", "GBuffer", "Forward (split ambient)", "Shadow"];
    private static readonly string[] QueueNames = ["Opaque", "Transparent"];
    private static readonly string[] GroupNames = ["opaque", "alpha blend", "opaque double-sided"];

    /// <summary>イベントの 1 行の説明</summary>
    public static string Summary(FrameDebugEventInfo e) => e.Type switch
    {
        FrameDebugEventType.ClearColor      => $"Clear color ({e.Clear.X:F2}, {e.Clear.Y:F2}, {e.Clear.Z:F2}, {e.Clear.W:F2})",
        FrameDebugEventType.ClearDepth      => $"Clear depth ({e.Clear.X:F2})",
        FrameDebugEventType.SceneDraw       => $"Draw mesh  {e.IndexCount / 3:N0} tris{(e.InstanceCount > 1 ? $" x{e.InstanceCount}" : "")}  [{name(GroupNames, e.Group)}]",
        FrameDebugEventType.Fullscreen      => $"Draw fullscreen  {FrameDebugger.FullscreenShaderName(e.Arg0)}",
        FrameDebugEventType.UpdateParticles => "Update particles (compute)",
        FrameDebugEventType.DrawParticles   => "Draw particles",
        FrameDebugEventType.CustomDraw      => $"Draw custom shader  {e.IndexCount / 3:N0} tris",
        FrameDebugEventType.Preview         => $"Draw preview (view {e.View})",
        _ => e.Type.ToString(),
    };

    private static string name(string[] names, uint index) => index < names.Length ? names[index] : index.ToString();

    // ---------------------------------------------------------------------
    // 右 : 描画先の絵 + 詳細
    // ---------------------------------------------------------------------
    private void drawView(FrameDebugSnapshot list)
    {
        if (_selected < 0 || _selected >= list.Events.Length) { ImGui.TextDisabled("Select an event."); return; }
        var e = list.Events[_selected];

        // 描画先の選び方
        ImGui.SetNextItemWidth(130.0f);
        var slotNames = new List<string>();
        for (int c = 0; c < e.Colors.Length; c++) slotNames.Add($"RT {c} : {FrameDebugger.TargetName(e.Colors[c])}");
        var slotLabel = _slot < 4 ? (_slot < e.Colors.Length ? slotNames[_slot] : $"RT {_slot} (none)") : $"Depth : {FrameDebugger.TargetName(e.Depth)}";
        if (ImGui.BeginCombo("##slot", slotLabel))
        {
            for (int c = 0; c < slotNames.Count; c++) if (ImGui.Selectable(slotNames[c], _slot == c)) _slot = c;
            if (e.Depth != 0xFFFFFFFFu && ImGui.Selectable($"Depth : {FrameDebugger.TargetName(e.Depth)}", _slot == 4)) _slot = 4;
            ImGui.EndCombo();
        }
        ImGui.SameLine(); ImGui.Checkbox("R", ref _channelR);
        ImGui.SameLine(); ImGui.Checkbox("G", ref _channelG);
        ImGui.SameLine(); ImGui.Checkbox("B", ref _channelB);
        ImGui.SameLine(); ImGui.Checkbox("A", ref _channelA);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(Math.Max(120.0f, ImGui.GetContentRegionAvail().X));
        ImGui.DragFloatRange2("##levels", ref _levelMin, ref _levelMax, 0.005f, 0.0f, 64.0f, "Levels %.3f", "%.3f");

        // 絵 (止めた時に C++ が写した物。数フレーム遅れて追いつく)
        var result = _current?.Result;
        var shown = _current != null && result is { Captured: true } && _current.Events.Length == _selected + 1;
        var avail = ImGui.GetContentRegionAvail();
        var imageHeight = Math.Max(120.0f, avail.Y * 0.55f);
        if (result is { Captured: true } r && FrameDebugger.OutputTextureId != 0xFFFFFFFFu)
        {
            var aspect = r.SourceHeight > 0 ? r.SourceWidth / (float)r.SourceHeight : 16.0f / 9.0f;
            var size = new Vector2(Math.Min(avail.X, imageHeight * aspect), 0);
            size.Y = size.X / aspect;
            ImGui.Image((IntPtr)FrameDebugger.OutputTextureId, size);
            ImGui.TextDisabled($"{FrameDebugger.TargetName(r.Source)}  {r.SourceFormat}  {r.SourceWidth} x {r.SourceHeight}{(shown ? "" : "  (updating...)")}");
        }
        else
        {
            ImGui.Dummy(new Vector2(avail.X, 40));
            ImGui.TextDisabled(result?.Status switch
            {
                2 => "This event has no target in the selected slot.",
                3 => "This target cannot be shown (back buffer / shadow map array).",
                _ => "(waiting for the frame...)",
            });
        }

        // 詳細
        ImGui.SeparatorText($"Event {_selected + 1} : {Summary(e)}");
        if (!ImGui.BeginTable("##detail", 2, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp)) return;
        row("Pass", passPath(list, e.Pass));
        row("Render targets", e.Colors.Length == 0 ? "-" : string.Join(", ", e.Colors.Select(FrameDebugger.TargetName)));
        row("Depth", FrameDebugger.TargetName(e.Depth));
        switch (e.Type)
        {
            case FrameDebugEventType.SceneDraw:
                row("Scene pass", $"{name(ScenePassNames, e.Arg0)} / {name(QueueNames, e.Arg1)} / {name(GroupNames, e.Group)}");
                row("View", e.View == 0 ? "camera" : $"shadow cascade {e.View - 1}");
                row("Indices / triangles", $"{e.IndexCount:N0} / {e.IndexCount / 3:N0}");
                row("Instances", e.InstanceCount > 1 ? $"{e.InstanceCount} (instanced : the object below is the first one)" : "1");
                drawObjectRow(e);
                row("Material", e.Material == 0xFFFFFFFFu ? "-" : $"#{e.Material}");
                break;
            case FrameDebugEventType.CustomDraw:
                row("Queue", name(QueueNames, e.Arg1));
                row("Custom shader", $"#{e.Arg0}");
                row("Indices / triangles", $"{e.IndexCount:N0} / {e.IndexCount / 3:N0}");
                drawObjectRow(e);
                row("Material", $"#{e.Material}");
                row("Time", $"{e.Params[0]:F3}");
                break;
            case FrameDebugEventType.Fullscreen:
                row("Shader", FrameDebugger.FullscreenShaderName(e.Arg0));
                row("Blend", e.Arg1 switch { 0 => "none", 1 => "additive", 2 => "alpha", 3 => "premultiplied", _ => e.Arg1.ToString() });
                for (int k = 0; k < 8; k++) if (e.Inputs[k] != 0xFFFFFFFFu) row($"g_Inputs[{k / 4}].{"xyzw"[k % 4]}", FrameDebugger.TargetName(e.Inputs[k]));
                row("g_Params[0]", $"{e.Params[0]:G4}, {e.Params[1]:G4}, {e.Params[2]:G4}, {e.Params[3]:G4}");
                row("g_Params[1]", $"{e.Params[4]:G4}, {e.Params[5]:G4}, {e.Params[6]:G4}, {e.Params[7]:G4}");
                break;
            case FrameDebugEventType.ClearColor:
                row("Color", $"{e.Clear.X:F3}, {e.Clear.Y:F3}, {e.Clear.Z:F3}, {e.Clear.W:F3}");
                break;
            case FrameDebugEventType.ClearDepth:
                row("Depth", $"{e.Clear.X:F3}");
                break;
        }
        ImGui.EndTable();
    }

    private void drawObjectRow(FrameDebugEventInfo e)
    {
        var entity = FrameDebugger.FindEntity(_app.Engine.World, e.Object);
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted("Object");
        ImGui.TableSetColumnIndex(1);
        if (entity.IsNull) { ImGui.TextDisabled(e.Object == 0xFFFFFFFFu ? "-" : $"render object #{e.Object}"); return; }
        var label = _app.Engine.World.Has<EntityInfo>(entity) ? _app.Engine.World.Get<EntityInfo>(entity).Name : $"entity {entity.Id}";
        if (ImGui.SmallButton($"{label}##select")) _app.Selected = entity;   // Hierarchy で選ぶ
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Select in Hierarchy");
    }

    private static void row(string name, string value)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(name);
        ImGui.TableSetColumnIndex(1); ImGui.TextWrapped(value);
    }
}
