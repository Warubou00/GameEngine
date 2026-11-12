using System.Numerics;
using System.Reflection;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// Widget Designer (v1.7。UE の UMG Designer)。.widget.json を作る
//   左上 : Palette (部品の一覧。クリック = 選んでいる Panel の中へ足す / Hierarchy へドラッグ)
//   左下 : Hierarchy (親子。ドラッグで親 / 順番を変える。右クリック : 複製 / 消す / 上へ / 下へ)
//   中   : 画面 (選んだ解像度での見た目。クリックで選ぶ / CanvasPanel の子はドラッグで動かす、右下の四角で大きさ。
//          ホイール : 拡大 / 右ドラッグ : 動かす / F : 全体)
//   右   : Details (部品の値 + 親の中での置き方 (Slot))
//   変えると少し後に自動で保存する (Ctrl+S ですぐ)。Ctrl+Z / Ctrl+Y : 元に戻す / やり直す (この窓の中だけ。JSON を積む)
//   Delete : 選んだ物を消す / Ctrl+D : 複製
// ==========================================================================
public sealed class WidgetDesigner
{
    private readonly Engine _engine;
    private readonly AssetDatabase _database;

    public string? Path { get; private set; }
    public UWidget? Root { get; private set; }
    public Vector2 DesignSize { get; set; } = WidgetAsset.DefaultDesignSize;
    public UWidget? Selected { get; set; }
    public bool HasFocus { get; private set; }
    public bool Dirty => _dirty;
    public int SaveCount { get; private set; }
    /// <summary>画面の見本の解像度 (ピクセル)</summary>
    public Vector2 PreviewResolution { get; set; } = new(1920, 1080);
    /// <summary>最後に描いた四角の数 (確認用)</summary>
    public int LastQuadCount => _drawList.Count;

    private readonly UIDrawList _drawList = new();
    private bool _dirty;
    private double _changedAt;
    private const double SaveDelay = 0.5;
    private readonly List<string> _undo = new(), _redo = new();
    private string _committed = "";
    private bool _pendingCommit;

    // 画面
    private float _zoom = 1.0f;          // 「全体が入る大きさ」に掛ける
    private Vector2 _pan;
    private Vector2 _canvasOrigin;       // 解像度の左上 (窓の座標)
    private float _viewScale = 1.0f;     // 画面のピクセル → 窓のピクセル
    private enum DragMode { None, Move, Resize, Pan }
    private DragMode _drag;
    private Vector2 _dragLast;
    private bool _dragChanged;
    private static UWidget? _draggedWidget;
    private static string? _draggedType;

    private static readonly (string Name, Vector2 Size)[] Resolutions =
    {
        ("1920 x 1080 (16:9)", new(1920, 1080)), ("1280 x 720 (16:9)", new(1280, 720)), ("2560 x 1440 (16:9)", new(2560, 1440)),
        ("2560 x 1080 (21:9)", new(2560, 1080)), ("1024 x 768 (4:3)", new(1024, 768)), ("1080 x 1920 (portrait)", new(1080, 1920)),
    };

    private static readonly (string Group, string[] Types)[] Palette =
    {
        ("Panel",  new[] { "CanvasPanel", "HorizontalBox", "VerticalBox", "Overlay", "UniformGridPanel", "ScrollBox", "SizeBox", "ScaleBox", "Border", "WidgetSwitcher" }),
        ("Common", new[] { "TextBlock", "Image", "Button", "ProgressBar", "Spacer" }),
        ("Input",  new[] { "CheckBox", "Slider", "EditableTextBox" }),
    };

    public WidgetDesigner(Engine engine, AssetDatabase database)
    {
        _engine = engine;
        _database = database;
    }

    // ---------------------------------------------------------------------
    // 開く / 保存
    // ---------------------------------------------------------------------

    public bool Open(string path)
    {
        Flush();
        try
        {
            var (root, size) = WidgetSerializer.Load(File.ReadAllText(_database.FullPath(path), System.Text.Encoding.UTF8));
            Root = root ?? new UCanvasPanel { Name = "Root" };
            DesignSize = size;
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[Widget] Cannot open {path} : {ex.Message}");
            return false;
        }
        Path = path;
        Selected = Root;
        _undo.Clear();
        _redo.Clear();
        _committed = toJson();
        _dirty = false;
        _zoom = 1.0f;
        _pan = Vector2.Zero;
        return true;
    }

    private string toJson() => WidgetSerializer.Save(Root, DesignSize);

    /// <summary>変えた物があればすぐ保存する</summary>
    public void Flush()
    {
        commit();
        if (!_dirty || Path == null) return;
        _dirty = false;
        var file = _database.FullPath(Path);
        try
        {
            _engine.Assets.NoteOwnWrite(file);
            File.WriteAllText(file, toJson(), new System.Text.UTF8Encoding(false));
            SaveCount++;
            _engine.Assets.ReloadWidget(Path);   // これから作る物 (再生) が新しい中身になる
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[Widget] Cannot save {Path} : {ex.Message}");
        }
    }

    // 変えた : 元に戻す記録を積み、少し後に保存する
    private void commit()
    {
        if (Root == null) return;
        var json = toJson();
        _pendingCommit = false;
        if (json == _committed) return;
        _undo.Add(_committed);
        if (_undo.Count > 200) _undo.RemoveAt(0);
        _redo.Clear();
        _committed = json;
        _dirty = true;
        _changedAt = ImGui.GetTime();
    }

    /// <summary>値を変えた (すぐ記録する。確認用 / 拡張から)</summary>
    public void Changed() => commit();

    public void Undo() => restore(_undo, _redo);
    public void Redo() => restore(_redo, _undo);
    public int UndoCount => _undo.Count;

    private void restore(List<string> from, List<string> to)
    {
        commit();
        if (from.Count == 0) return;
        var path = pathOf(Selected);
        to.Add(_committed);
        _committed = from[^1];
        from.RemoveAt(from.Count - 1);
        var (root, size) = WidgetSerializer.Load(_committed);
        Root = root;
        DesignSize = size;
        Selected = find(path) ?? Root;
        _dirty = true;
        _changedAt = ImGui.GetTime();
    }

    // 選んだ物の位置 (根からの子の番号) : 元に戻した後に同じ物を選ぶ
    private static List<int> pathOf(UWidget? widget)
    {
        var path = new List<int>();
        for (var w = widget; w?.Parent != null; w = w.Parent) path.Insert(0, w.Parent.GetChildIndex(w));
        return path;
    }

    private UWidget? find(List<int> path)
    {
        var w = Root;
        foreach (var index in path) w = (w as UPanelWidget)?.GetChildAt(index);
        return w;
    }

    // ---------------------------------------------------------------------
    // 編集 (確認用にも public)
    // ---------------------------------------------------------------------

    /// <summary>部品を足す (選んでいる物が子を持てればその中、持てなければその後ろ)。戻り値 = 足した物</summary>
    public UWidget? AddWidget(string typeName, UWidget? target = null)
    {
        if (Root == null) return null;
        var widget = WidgetTypes.Create(typeName);
        widget.Name = uniqueName(typeName);
        prepareNew(widget);
        target ??= Selected ?? Root;
        if (!insertInto(widget, target)) return null;
        Selected = widget;
        commit();
        return widget;
    }

    // 足す所 : target が Panel で空きがあれば中 (最後)、無ければ target の後ろ
    private bool insertInto(UWidget widget, UWidget target)
    {
        if (target is UPanelWidget panel && panel.CanAddMoreChildren) return panel.AddChild(widget) != null;
        if (target.Parent is { } parent && parent.CanAddMoreChildren) return parent.InsertChildAt(parent.GetChildIndex(target) + 1, widget) != null;
        return false;
    }

    // 足したばかりの物の見た目 (UE の Designer と同じく、すぐ見える物にする)
    private static void prepareNew(UWidget widget)
    {
        switch (widget)
        {
        case UButton button: button.AddChild(new UTextBlock { Name = "Text", Text = "Button", Font = new UIFont(20) }); break;
        case UCheckBox check: check.AddChild(new UTextBlock { Name = "Label", Text = "Check Box", Font = new UIFont(20) }); break;
        case UBorder border: border.Background = UIBrush.Rounded(new Vector4(0.1f, 0.1f, 0.12f, 0.85f), 6); break;
        }
    }

    private string uniqueName(string baseName)
    {
        var names = Root?.Descendants().Select(w => w.Name).ToHashSet() ?? new HashSet<string>();
        if (!names.Contains(baseName)) return baseName;
        for (int i = 1; ; i++) if (!names.Contains($"{baseName}_{i}")) return $"{baseName}_{i}";
    }

    public void DeleteSelected()
    {
        if (Selected == null || Selected == Root || Selected.Parent is not { } parent) return;
        var index = parent.GetChildIndex(Selected);
        parent.RemoveChild(Selected);
        Selected = parent.GetChildAt(Math.Min(index, parent.ChildrenCount - 1)) ?? parent;
        commit();
    }

    public UWidget? DuplicateSelected()
    {
        if (Selected == null || Selected == Root || Selected.Parent is not { } parent || !parent.CanAddMoreChildren) return null;
        var copy = WidgetSerializer.Clone(Selected);
        foreach (var w in copy.Descendants()) w.Name = uniqueName(w.Name);
        var slot = Selected.Slot.Clone();
        if (parent is UCanvasPanel) slot.Position += new Vector2(20, 20);
        parent.InsertChildAt(parent.GetChildIndex(Selected) + 1, copy, slot);
        Selected = copy;
        commit();
        return copy;
    }

    /// <summary>親 / 順番を変える (into = その中へ。そうでなければ before の前へ)</summary>
    public bool Move(UWidget widget, UWidget target, bool into)
    {
        if (widget == Root || widget == target) return false;
        for (var p = target; p != null; p = p.Parent) if (p == widget) return false;   // 自分の子孫の中へは入れない
        bool done;
        if (into && target is UPanelWidget panel && (panel.CanAddMoreChildren || widget.Parent == panel)) done = panel.AddChild(widget) != null;
        else if (target.Parent is { } parent && (parent.CanAddMoreChildren || widget.Parent == parent)) done = parent.InsertChildAt(parent.GetChildIndex(target), widget) != null;
        else done = false;
        if (done) commit();
        return done;
    }

    private void moveBy(int delta)
    {
        if (Selected?.Parent is not { } parent) return;
        var index = parent.GetChildIndex(Selected) + delta;
        if (index < 0 || index >= parent.ChildrenCount) return;
        parent.InsertChildAt(index, Selected);
        commit();
    }

    // ---------------------------------------------------------------------
    // 窓
    // ---------------------------------------------------------------------

    public void Draw(ref bool open)
    {
        HasFocus = false;
        if (!open) return;
        ImGui.SetNextWindowSize(new Vector2(1280, 760), ImGuiCond.FirstUseEver);
        var title = Path != null ? $"Widget Designer - {System.IO.Path.GetFileName(Path)}{(_dirty ? " *" : "")}###WidgetDesigner" : "Widget Designer###WidgetDesigner";
        if (!ImGui.Begin(title, ref open, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse))
        {
            ImGui.End();
            if (!open) Flush();
            return;
        }
        HasFocus = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        if (Root == null)
        {
            ImGui.TextDisabled("Open a widget : double-click a .widget.json in Project (right-click > Create > Widget to make one)");
            ImGui.End();
            return;
        }
        drawToolbar();
        var avail = ImGui.GetContentRegionAvail();
        const float LeftWidth = 230, RightWidth = 330;
        ImGui.BeginChild("left", new Vector2(LeftWidth, avail.Y), ImGuiChildFlags.None);
        drawPalette(avail.Y * 0.4f);
        drawHierarchy();
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("canvas", new Vector2(Math.Max(50, avail.X - LeftWidth - RightWidth - 16), avail.Y), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        drawCanvas();
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("details", new Vector2(RightWidth, avail.Y), ImGuiChildFlags.Borders);
        drawDetails();
        ImGui.EndChild();

        if (HasFocus) handleKeys();
        // 値の欄を離した時に記録 (打っている間は 1 文字ずつ積まない)
        if (_pendingCommit && !ImGui.IsAnyItemActive()) commit();
        if (_dirty && ImGui.GetTime() - _changedAt > SaveDelay && !ImGui.IsAnyItemActive()) Flush();
        ImGui.End();
        if (!open) Flush();
    }

    private void handleKeys()
    {
        var io = ImGui.GetIO();
        if (io.WantTextInput) return;
        if (io.KeyCtrl)
        {
            if (ImGui.IsKeyPressed(ImGuiKey.Z)) { if (io.KeyShift) Redo(); else Undo(); }
            if (ImGui.IsKeyPressed(ImGuiKey.Y)) Redo();
            if (ImGui.IsKeyPressed(ImGuiKey.S)) Flush();
            if (ImGui.IsKeyPressed(ImGuiKey.D)) DuplicateSelected();
            return;
        }
        if (ImGui.IsKeyPressed(ImGuiKey.Delete)) DeleteSelected();
        if (ImGui.IsKeyPressed(ImGuiKey.F)) { _zoom = 1; _pan = Vector2.Zero; }
    }

    private void drawToolbar()
    {
        if (ImGui.Button("Save")) Flush();
        ImGui.SameLine();
        ImGui.BeginDisabled(_undo.Count == 0);
        if (ImGui.Button("Undo")) Undo();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(_redo.Count == 0);
        if (ImGui.Button("Redo")) Redo();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.SetNextItemWidth(190);
        var current = Resolutions.FirstOrDefault(r => r.Size == PreviewResolution).Name ?? $"{PreviewResolution.X} x {PreviewResolution.Y}";
        if (ImGui.BeginCombo("Screen", current))
        {
            foreach (var (name, size) in Resolutions)
                if (ImGui.Selectable(name, size == PreviewResolution)) PreviewResolution = size;
            ImGui.EndCombo();
        }
        ImGui.SameLine();
        var design = DesignSize;
        ImGui.SetNextItemWidth(130);
        if (ImGui.DragFloat2("Design size", ref design, 1, 64, 8192, "%.0f")) { DesignSize = Vector2.Max(design, new Vector2(64)); _pendingCommit = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("The resolution this widget is made for. It is scaled to the screen by the shorter side.");
        ImGui.SameLine();
        ImGui.SetNextItemWidth(110);
        var zoom = _zoom * 100;
        if (ImGui.SliderFloat("Zoom", ref zoom, 10, 400, "%.0f %%")) _zoom = zoom / 100;
        ImGui.SameLine();
        if (ImGui.Button("Fit")) { _zoom = 1; _pan = Vector2.Zero; }
        ImGui.SameLine();
        ImGui.TextDisabled($"   UI scale {UIManager.ScaleFor(PreviewResolution, DesignSize):F2}");
    }

    private void drawPalette(float height)
    {
        ImGui.SeparatorText("Palette");
        ImGui.BeginChild("palette", new Vector2(0, height), ImGuiChildFlags.None);
        foreach (var (group, types) in Palette)
        {
            if (!ImGui.TreeNodeEx(group, ImGuiTreeNodeFlags.DefaultOpen)) continue;
            foreach (var type in types)
            {
                if (ImGui.Selectable(type)) AddWidget(type);
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Click : add into the selected panel (or after the selected widget)\nDrag : drop on the hierarchy");
                if (ImGui.BeginDragDropSource())
                {
                    _draggedType = type;
                    _draggedWidget = null;
                    ImGui.SetDragDropPayload("UI_WIDGET", IntPtr.Zero, 0);
                    ImGui.Text($"+ {type}");
                    ImGui.EndDragDropSource();
                }
            }
            ImGui.TreePop();
        }
        // 自分で登録した型 (WidgetTypes.Register)
        var custom = WidgetTypes.All.Where(t => t.Type.Assembly != typeof(UWidget).Assembly).ToList();
        if (custom.Count > 0 && ImGui.TreeNodeEx("Custom", ImGuiTreeNodeFlags.DefaultOpen))
        {
            foreach (var (name, _) in custom) if (ImGui.Selectable(name)) AddWidget(name);
            ImGui.TreePop();
        }
        ImGui.EndChild();
    }

    private void drawHierarchy()
    {
        ImGui.SeparatorText("Hierarchy");
        ImGui.BeginChild("hierarchy");
        if (Root != null) drawNode(Root);
        ImGui.EndChild();
    }

    private void drawNode(UWidget widget)
    {
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (widget.Children.Count == 0) flags |= ImGuiTreeNodeFlags.Leaf;
        if (widget == Selected) flags |= ImGuiTreeNodeFlags.Selected;
        ImGui.PushID(widget.GetHashCode());
        if (!widget.IsVisible) ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
        var opened = ImGui.TreeNodeEx("node", flags, $"{(string.IsNullOrEmpty(widget.Name) ? "(no name)" : widget.Name)}  [{widget.TypeName}]");
        if (!widget.IsVisible) ImGui.PopStyleColor();
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen()) Selected = widget;
        // ドラッグ : 親 / 順番を変える。Panel の上は中へ、それ以外はその前へ
        if (widget != Root && ImGui.BeginDragDropSource())
        {
            _draggedWidget = widget;
            _draggedType = null;
            ImGui.SetDragDropPayload("UI_WIDGET", IntPtr.Zero, 0);
            ImGui.Text(widget.Name);
            ImGui.EndDragDropSource();
        }
        if (ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                var payload = ImGui.AcceptDragDropPayload("UI_WIDGET");
                if (payload.NativePtr != null)
                {
                    var into = widget is UPanelWidget && !ImGui.GetIO().KeyShift;   // Shift : 中ではなく前へ
                    if (_draggedType != null) { if (into) AddWidget(_draggedType, widget); else if (widget.Parent != null) { var w = WidgetTypes.Create(_draggedType); w.Name = uniqueName(_draggedType); prepareNew(w); widget.Parent.InsertChildAt(widget.Parent.GetChildIndex(widget), w); Selected = w; commit(); } }
                    else if (_draggedWidget != null) Move(_draggedWidget, widget, into);
                    _draggedWidget = null;
                    _draggedType = null;
                }
            }
            ImGui.EndDragDropTarget();
        }
        if (ImGui.BeginPopupContextItem("menu"))
        {
            Selected = widget;
            if (ImGui.MenuItem("Duplicate", "Ctrl+D", false, widget != Root)) DuplicateSelected();
            if (ImGui.MenuItem("Delete", "Delete", false, widget != Root)) DeleteSelected();
            ImGui.Separator();
            if (ImGui.MenuItem("Move Up", null, false, widget != Root)) moveBy(-1);
            if (ImGui.MenuItem("Move Down", null, false, widget != Root)) moveBy(1);
            if (widget is UPanelWidget && ImGui.BeginMenu("Add Child"))
            {
                foreach (var (_, types) in Palette)
                {
                    foreach (var type in types) if (ImGui.MenuItem(type)) AddWidget(type, widget);
                    ImGui.Separator();
                }
                ImGui.EndMenu();
            }
            ImGui.EndPopup();
        }
        if (opened)
        {
            // 子を回している間に並びが変わることがあるので、写しで回す
            foreach (var child in widget.Children.ToArray()) drawNode(child);
            ImGui.TreePop();
        }
        ImGui.PopID();
    }

    // ---------------------------------------------------------------------
    // 画面 (選んだ解像度の見た目)
    // ---------------------------------------------------------------------

    private void drawCanvas()
    {
        if (Root == null || UI.Manager is not { } manager) return;
        var regionMin = ImGui.GetCursorScreenPos();
        var region = Vector2.Max(ImGui.GetContentRegionAvail(), new Vector2(10));
        var regionMax = regionMin + region;
        ImGui.InvisibleButton("canvas", region, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        var hovered = ImGui.IsItemHovered();
        var io = ImGui.GetIO();
        var draw = ImGui.GetWindowDrawList();

        // 解像度の四角を窓の真ん中に (全体が入る大きさ x Zoom)
        var fit = Math.Min((region.X - 24) / PreviewResolution.X, (region.Y - 24) / PreviewResolution.Y);
        _viewScale = Math.Max(0.01f, fit * _zoom);
        var screenSize = PreviewResolution * _viewScale;
        _canvasOrigin = regionMin + (region - screenSize) * 0.5f + _pan;
        draw.PushClipRect(regionMin, regionMax, true);
        draw.AddRectFilled(regionMin, regionMax, 0xFF1A1A1Au);
        // 市松模様 (透明な所が分かるように)
        const float Checker = 32;
        for (float y = 0; y < screenSize.Y; y += Checker)
        for (float x = 0; x < screenSize.X; x += Checker)
        {
            var dark = ((int)(x / Checker) + (int)(y / Checker)) % 2 == 0;
            var a = _canvasOrigin + new Vector2(x, y);
            draw.AddRectFilled(a, Vector2.Min(a + new Vector2(Checker), _canvasOrigin + screenSize), dark ? 0xFF2A2A2Au : 0xFF323232u);
        }

        // 並べて描く (画面の UI と同じ並べ方。ゲームの絵の上と同じ見た目)
        var uiScale = UIManager.ScaleFor(PreviewResolution, DesignSize);
        var clip = new Vector4(Math.Max(_canvasOrigin.X, regionMin.X), Math.Max(_canvasOrigin.Y, regionMin.Y),
                               Math.Min(_canvasOrigin.X + screenSize.X, regionMax.X), Math.Min(_canvasOrigin.Y + screenSize.Y, regionMax.Y));
        manager.PaintTree(Root, PreviewResolution / uiScale, _canvasOrigin, uiScale * _viewScale, _drawList, clip);
        UIImGui.Draw(draw, _drawList);
        draw.AddRect(_canvasOrigin, _canvasOrigin + screenSize, 0xFF808080u);

        // マウスの下の物 (一番奥の子) / 選んだ物の枠
        var mouse = io.MousePos;
        var under = hovered ? pick(Root, mouse) : null;
        if (under != null && under != Selected) outline(draw, under, 0x90FFFFFFu, 1);
        if (Selected != null && Selected.Root() == Root)
        {
            outline(draw, Selected, 0xFF20A0FFu, 2);
            if (Selected.Parent is UCanvasPanel) draw.AddRectFilled(handleOf(Selected) - new Vector2(5), handleOf(Selected) + new Vector2(5), 0xFF20A0FFu);
        }
        draw.AddText(regionMin + new Vector2(8, 6), 0xA0FFFFFFu, $"{PreviewResolution.X} x {PreviewResolution.Y}   Click : select / drag : move (CanvasPanel)   Right drag : pan   Wheel : zoom");
        draw.PopClipRect();

        // 操作
        if (hovered && io.MouseWheel != 0)
        {
            var before = (mouse - _canvasOrigin) / _viewScale;
            _zoom = Math.Clamp(_zoom * (io.MouseWheel > 0 ? 1.1f : 1 / 1.1f), 0.1f, 4.0f);
            var newScale = Math.Max(0.01f, fit * _zoom);
            var newOrigin = regionMin + (region - PreviewResolution * newScale) * 0.5f;
            _pan = mouse - before * newScale - newOrigin;
        }
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Right)) { _drag = DragMode.Pan; _dragLast = mouse; }
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (Selected?.Parent is UCanvasPanel && Vector2.Distance(mouse, handleOf(Selected)) < 8) _drag = DragMode.Resize;
            else
            {
                Selected = under ?? Root;
                _drag = Selected?.Parent is UCanvasPanel ? DragMode.Move : DragMode.None;
            }
            _dragLast = mouse;
            _dragChanged = false;
        }
        if (_drag != DragMode.None)
        {
            var delta = mouse - _dragLast;
            _dragLast = mouse;
            if (_drag == DragMode.Pan) _pan += delta;
            else if (Selected?.Parent is UCanvasPanel canvas && delta != Vector2.Zero)
            {
                // 窓のピクセル → 親の単位 (並べた時の変換の逆)
                var parentScale = MathF.Sqrt(MathF.Abs(canvas.Transform.GetDeterminant()));
                var d = delta / Math.Max(1e-4f, parentScale);
                var slot = Selected.Slot;
                if (_drag == DragMode.Move)
                {
                    slot.Offsets.Left += d.X; slot.Offsets.Top += d.Y;
                    if (slot.Anchors.StretchX) slot.Offsets.Right -= d.X;
                    if (slot.Anchors.StretchY) slot.Offsets.Bottom -= d.Y;
                }
                else
                {
                    slot.Offsets.Right += slot.Anchors.StretchX ? -d.X : d.X;
                    slot.Offsets.Bottom += slot.Anchors.StretchY ? -d.Y : d.Y;
                    if (!slot.Anchors.StretchX) slot.Offsets.Right = Math.Max(0, slot.Offsets.Right);
                    if (!slot.Anchors.StretchY) slot.Offsets.Bottom = Math.Max(0, slot.Offsets.Bottom);
                    slot.AutoSize = false;
                }
                _dragChanged = true;
            }
            var released = _drag == DragMode.Pan ? !ImGui.IsMouseDown(ImGuiMouseButton.Right) : !ImGui.IsMouseDown(ImGuiMouseButton.Left);
            if (released)
            {
                // 動かした値は整数に (保存の差分が見やすいように)
                if (_dragChanged && Selected != null)
                {
                    var o = Selected.Slot.Offsets;
                    Selected.Slot.Offsets = new UIMargin(MathF.Round(o.Left), MathF.Round(o.Top), MathF.Round(o.Right), MathF.Round(o.Bottom));
                    commit();
                }
                _drag = DragMode.None;
            }
        }
    }

    private static Vector2 handleOf(UWidget widget) => Vector2.Transform(widget.Size, widget.Transform);

    private static void outline(ImDrawListPtr draw, UWidget widget, uint color, float thickness)
    {
        var t = widget.Transform;
        var s = widget.Size;
        draw.AddQuad(Vector2.Transform(Vector2.Zero, t), Vector2.Transform(new Vector2(s.X, 0), t), Vector2.Transform(s, t), Vector2.Transform(new Vector2(0, s.Y), t), color, thickness);
    }

    /// <summary>窓の位置の一番手前 (奥の子) の物 (見えない物は除く)</summary>
    public static UWidget? pick(UWidget widget, Vector2 point)
    {
        if (!widget.IsVisible) return null;
        foreach (var child in widget.ChildrenFrontToBack)
            if (pick(child, point) is { } hit) return hit;
        return widget.Contains(point) ? widget : null;
    }

    // ---------------------------------------------------------------------
    // Details
    // ---------------------------------------------------------------------

    private void drawDetails()
    {
        if (Selected is not { } widget) { ImGui.TextDisabled("Select a widget"); return; }
        ImGui.TextDisabled(widget.TypeName);
        var name = widget.Name;
        if (ImGui.InputText("Name", ref name, 128)) { widget.Name = name; _pendingCommit = true; }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Scripts find it by this name : userWidget.GetWidget<UTextBlock>(\"Name\")");

        // 親の中での置き方
        if (widget.Parent is { } parent)
        {
            ImGui.SeparatorText($"Slot ({parent.TypeName})");
            if (parent is UCanvasPanel) drawAnchorPresets(widget.Slot);
            foreach (var fieldName in parent.SlotFields)
            {
                var field = typeof(UIPanelSlot).GetField(fieldName)!;
                var value = field.GetValue(widget.Slot);
                var label = fieldName == nameof(UIPanelSlot.Offsets) && !widget.Slot.Anchors.StretchX && !widget.Slot.Anchors.StretchY ? "Position / Size" : fieldName;
                if (drawValue(label, field.FieldType, ref value, field))
                {
                    field.SetValue(widget.Slot, value);
                    _pendingCommit = true;
                }
            }
        }

        // 部品の値 (種類ごと → 共通)
        var common = false;
        ImGui.SeparatorText(widget.TypeName);
        foreach (var field in WidgetTypes.Fields(widget.GetType()))
        {
            if (field.DeclaringType == typeof(UWidget) && !common) { ImGui.SeparatorText("Behavior / Render"); common = true; }
            var value = field.GetValue(widget);
            if (drawValue(field.Name, field.FieldType, ref value, field))
            {
                field.SetValue(widget, value);
                _pendingCommit = true;
            }
        }
    }

    private void drawAnchorPresets(UIPanelSlot slot)
    {
        var presets = new (string Name, UIAnchors Anchors)[]
        {
            ("Top Left", new(0, 0)), ("Top", new(0.5f, 0)), ("Top Right", new(1, 0)),
            ("Left", new(0, 0.5f)), ("Center", new(0.5f, 0.5f)), ("Right", new(1, 0.5f)),
            ("Bottom Left", new(0, 1)), ("Bottom", new(0.5f, 1)), ("Bottom Right", new(1, 1)),
            ("Top Stretch", new(0, 0, 1, 0)), ("Bottom Stretch", new(0, 1, 1, 1)), ("Left Stretch", new(0, 0, 0, 1)), ("Right Stretch", new(1, 0, 1, 1)),
            ("Fill", new(0, 0, 1, 1)),
        };
        ImGui.SetNextItemWidth(-1);
        if (!ImGui.BeginCombo("##anchors", "Anchor preset...")) return;
        foreach (var (name, anchors) in presets)
        {
            if (!ImGui.Selectable(name)) continue;
            slot.Anchors = anchors;
            // Fill などの伸びる軸は端からの距離 0、点の軸は今の大きさのまま、揃えをアンカーと同じに (UE と同じ動き)
            var o = slot.Offsets;
            if (anchors.StretchX) { o.Left = 0; o.Right = 0; } else { o.Left = 0; slot.Alignment.X = anchors.Minimum.X; if (o.Right <= 0) o.Right = 100; }
            if (anchors.StretchY) { o.Top = 0; o.Bottom = 0; } else { o.Top = 0; slot.Alignment.Y = anchors.Minimum.Y; if (o.Bottom <= 0) o.Bottom = 30; }
            if (anchors.StretchX) slot.Alignment.X = 0;
            if (anchors.StretchY) slot.Alignment.Y = 0;
            slot.Offsets = o;
            _pendingCommit = true;
        }
        ImGui.EndCombo();
    }

    // 値の欄 (戻り値 = 変えた)
    private bool drawValue(string label, Type type, ref object? value, FieldInfo? field)
    {
        ImGui.PushID(label);
        var changed = false;
        var range = field?.GetCustomAttribute<RangeAttribute>();
        switch (value)
        {
        case string s:
        {
            var image = field?.GetCustomAttribute<AssetPathAttribute>() != null;
            if (label == nameof(UTextBlock.Text) && field?.DeclaringType == typeof(UTextBlock))
                changed = ImGui.InputTextMultiline(label, ref s, 4096, new Vector2(-1, 60));
            else changed = ImGui.InputText(label, ref s, 1024);
            if (image && ImGui.BeginDragDropTarget())
            {
                if (ProjectPanel.AcceptDrop(AssetKind.Texture) is { } asset) { s = asset.Path; changed = true; }
                ImGui.EndDragDropTarget();
            }
            if (image && ImGui.IsItemHovered()) ImGui.SetTooltip("Drop a texture from Project (empty = white)");
            if (changed) value = s;
            break;
        }
        case bool b:
            if (ImGui.Checkbox(label, ref b)) { value = b; changed = true; }
            break;
        case float f:
            if (range != null ? ImGui.SliderFloat(label, ref f, range.Min, range.Max) : ImGui.DragFloat(label, ref f, 0.5f)) { value = f; changed = true; }
            break;
        case int i:
            if (ImGui.DragInt(label, ref i, 0.2f)) { value = i; changed = true; }
            break;
        case Enum e:
        {
            var names = Enum.GetNames(type);
            var index = Array.IndexOf(names, e.ToString());
            if (ImGui.Combo(label, ref index, names, names.Length)) { value = Enum.Parse(type, names[index]); changed = true; }
            break;
        }
        case Vector2 v2:
            if (ImGui.DragFloat2(label, ref v2, label.Contains("Scale") || label.Contains("Pivot") || label == "Alignment" ? 0.01f : 0.5f)) { value = v2; changed = true; }
            break;
        case Vector4 v4:
            if (label.Contains("Color") || label == "Tint" ? ImGui.ColorEdit4(label, ref v4, ImGuiColorEditFlags.AlphaBar) : ImGui.DragFloat4(label, ref v4, 0.01f)) { value = v4; changed = true; }
            break;
        case UIMargin m:
        {
            var v = new Vector4(m.Left, m.Top, m.Right, m.Bottom);
            if (ImGui.DragFloat4(label, ref v, 0.5f)) { value = new UIMargin(v.X, v.Y, v.Z, v.W); changed = true; }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip(label == "Position / Size" ? "X, Y, Width, Height" : "Left, Top, Right, Bottom");
            break;
        }
        default:
            if (value != null && type.IsValueType)
            {
                // 入れ子の struct (ブラシ / フォント / アンカー / 変換)
                if (ImGui.TreeNodeEx(label, ImGuiTreeNodeFlags.None))
                {
                    foreach (var inner in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    {
                        var innerValue = inner.GetValue(value);
                        if (drawValue(inner.Name, inner.FieldType, ref innerValue, inner)) { inner.SetValue(value, innerValue); changed = true; }
                    }
                    ImGui.TreePop();
                }
            }
            else ImGui.LabelText(label, value?.ToString() ?? "(null)");
            break;
        }
        ImGui.PopID();
        return changed;
    }
}

internal static class WidgetDesignerExtensions
{
    public static UWidget Root(this UWidget widget)
    {
        while (widget.Parent != null) widget = widget.Parent;
        return widget;
    }
}
