using System.Numerics;
using GameEngine;
using GameEngine.Rendering;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// コリジョンアセットの窓 (v1.9)。Project で .collision.json をダブルクリック (View > Collision Editor でも)
//   左   : Model (プロジェクトのモデルを選んで一緒に出す。アセットに覚える) / Hierarchy (親子。ドラッグで親を変える。
//          右クリック : 子を足す / 複製 / 消す / 親を外す) / Details (ID / 名前 / 形 / 位置 / 回転 / 大きさ / トリガー / レイヤー …)
//   右   : プレビュー (シーンとは別の小さな世界 PreviewScene)。コライダーは線で描く
//          選んだノードは黄色、その子孫は橙色、他は緑 (トリガーは水色)。クリックで選ぶ / ギズモで動かす
//          右ドラッグ : 回す / 中ドラッグ : ずらす / ホイール : 近づく / F : 全体 / W E R : 移動 回転 大きさ
//   アセットはシーンと同じ CollisionAsset を直に変える (再生中も CompoundCollider がすぐ作り直す)。
//   少し後に自動で保存 (Ctrl+S ですぐ)。Ctrl+Z / Ctrl+Y : 元に戻す / やり直す (JSON を積む)。Delete / Ctrl+D
// ==========================================================================
public sealed class CollisionEditor
{
    private readonly Engine _engine;
    private readonly AssetDatabase _database;

    public string? Path { get; private set; }
    public CollisionAsset? Asset { get; private set; }
    public CollisionNode? Selected { get; set; }
    public bool HasFocus { get; private set; }
    public bool Dirty => _dirty;
    public int SaveCount { get; private set; }
    public PreviewScene? Preview { get; private set; }
    /// <summary>プレビューに出しているモデルのエンティティ (無ければ Null)</summary>
    public Entity ModelEntity { get; private set; } = Entity.Null;
    /// <summary>前のフレームで描いたコライダーの線の数 (確認用)</summary>
    public int LineCount { get; private set; }
    /// <summary>前のフレームで「選んだ物 / 子孫」の色で描いた部品の数 (確認用)</summary>
    public int HighlightedCount { get; private set; }

    public bool ShowModel = true;
    public bool XRay = true;     // コライダーをモデルの手前に描く
    public bool ShowGrid = true;

    // 色 (DebugDraw の ABGR)
    public static readonly uint SelectedColor   = DebugDraw.Color(1.0f, 0.9f, 0.15f);
    public static readonly uint DescendantColor = DebugDraw.Color(1.0f, 0.55f, 0.1f);
    public static readonly uint NormalColor     = DebugDraw.Color(0.3f, 1.0f, 0.4f);
    public static readonly uint TriggerColor    = DebugDraw.Color(0.3f, 0.85f, 1.0f);
    private static readonly uint LinkColor      = DebugDraw.Color(0.6f, 0.6f, 0.6f, 0.6f);

    private bool _dirty;
    private double _changedAt;
    private const double SaveDelay = 0.5;
    private readonly List<string> _undo = new(), _redo = new();
    private string _committed = "";
    private bool _pendingCommit;
    private bool _frameRequested;
    private string? _modelError;
    private string? _loadedModel;
    private Vector3 _modelMin, _modelMax;
    private bool _hasModelBounds;

    private readonly Gizmo _gizmo = new();
    private Entity _proxy = Entity.Null;   // ギズモ用 (選んだノードの持ち主から見た位置に置く。親は無し)
    private bool _wasDragging;
    private Vector3 _dragStartSize;
    private static CollisionNode? _draggedNode;
    private readonly Dictionary<CollisionNode, Vector3> _eulerCache = new();

    public CollisionEditor(Engine engine, AssetDatabase database)
    {
        _engine = engine;
        _database = database;
    }

    public bool IsOpen => Path != null;

    // ---------------------------------------------------------------------
    // 開く / 閉じる / 保存
    // ---------------------------------------------------------------------

    public bool Open(string path)
    {
        Flush();
        CollisionAsset asset;
        try
        {
            asset = _engine.Assets.LoadCollisionAsset(path);
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.WriteLine($"[Collision] Cannot open {path} : {ex.Message}");
            return false;
        }
        Path = asset.Path;
        Asset = asset;
        Selected = asset.Nodes.FirstOrDefault();
        _undo.Clear();
        _redo.Clear();
        _committed = asset.ToJson();
        _dirty = false;
        _eulerCache.Clear();
        Preview ??= _engine.CreatePreview();
        _loadedModel = null;
        loadModel();
        _frameRequested = true;
        return true;
    }

    public void Close()
    {
        Flush();
        Path = null;
        Asset = null;
        Selected = null;
        Preview?.Dispose();
        Preview = null;
        ModelEntity = Entity.Null;
        _proxy = Entity.Null;
        _loadedModel = null;
    }

    /// <summary>変えた物があればすぐ保存する</summary>
    public void Flush()
    {
        commit();
        if (!_dirty || Path == null || Asset == null) return;
        _dirty = false;
        var file = _database.FullPath(Path);
        try
        {
            _engine.Assets.NoteOwnWrite(file);   // ファイルの見張りで読み直さない (同じ CollisionAsset を直に変えている)
            File.WriteAllText(file, Asset.ToJson(), new System.Text.UTF8Encoding(false));
            SaveCount++;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[Collision] Cannot save {Path} : {ex.Message}");
        }
    }

    // 変えた : 元に戻す記録を積み、少し後に保存する
    private void commit()
    {
        if (Asset == null) return;
        _pendingCommit = false;
        var json = Asset.ToJson();
        if (json == _committed) return;
        _undo.Add(_committed);
        if (_undo.Count > 200) _undo.RemoveAt(0);
        _redo.Clear();
        _committed = json;
        _dirty = true;
        _changedAt = ImGui.GetTime();
    }

    /// <summary>値を変えた (すぐ記録する。確認用 / 拡張から)</summary>
    public void Changed()
    {
        Asset?.MarkChanged();
        commit();
    }

    // 値を変えている途中 (シーンにはすぐ出し、記録は欄を離した時)
    private void editing()
    {
        Asset?.MarkChanged();
        _pendingCommit = true;
    }

    public void Undo() => restore(_undo, _redo);
    public void Redo() => restore(_redo, _undo);
    public int UndoCount => _undo.Count;

    private void restore(List<string> from, List<string> to)
    {
        commit();
        if (from.Count == 0 || Asset == null) return;
        var index = Selected != null ? Asset.Nodes.IndexOf(Selected) : -1;
        to.Add(_committed);
        _committed = from[^1];
        from.RemoveAt(from.Count - 1);
        Asset.SetJson(_committed);   // Version も上がる (使っている CompoundCollider は作り直す)
        Selected = index >= 0 && index < Asset.Nodes.Count ? Asset.Nodes[index] : Asset.Nodes.FirstOrDefault();
        _eulerCache.Clear();
        _dirty = true;
        _changedAt = ImGui.GetTime();
        if (_loadedModel != Asset.PreviewModel) loadModel();
    }

    // ---------------------------------------------------------------------
    // 編集 (確認用にも public)
    // ---------------------------------------------------------------------

    /// <summary>ノードを足す (parent の子。null なら一番上)。戻り値 = 足した物</summary>
    public CollisionNode? AddNode(CollisionNodeShape shape, CollisionNode? parent)
    {
        if (Asset == null) return null;
        var node = new CollisionNode
        {
            Id = Asset.NextId(), Name = uniqueName(shape == CollisionNodeShape.Empty ? "Group" : shape.ToString()), Shape = shape, Parent = parent,
            Size = shape switch { CollisionNodeShape.Sphere => new Vector3(0.25f, 0, 0), CollisionNodeShape.Capsule => new Vector3(0.15f, 0.3f, 0), _ => new Vector3(0.25f) },
        };
        // 親の子の最後 (一覧の順 = 表示の順。子孫の後ろに入れる)
        var insertAt = Asset.Nodes.Count;
        if (parent != null)
        {
            var last = Asset.Nodes.IndexOf(parent);
            for (int i = last + 1; i < Asset.Nodes.Count; i++) if (parent.IsSelfOrAncestorOf(Asset.Nodes[i])) last = i;
            insertAt = last + 1;
        }
        Asset.Nodes.Insert(insertAt, node);
        Selected = node;
        Changed();
        return node;
    }

    private string uniqueName(string baseName)
    {
        var names = Asset?.Nodes.Select(n => n.Name).ToHashSet() ?? new HashSet<string>();
        if (!names.Contains(baseName)) return baseName;
        for (int i = 1; ; i++) if (!names.Contains($"{baseName}_{i}")) return $"{baseName}_{i}";
    }

    /// <summary>選んだノードと子孫を消す</summary>
    public void DeleteSelected()
    {
        if (Asset == null || Selected == null) return;
        var index = Asset.Nodes.IndexOf(Selected);
        var parent = Selected.Parent;
        var removed = Selected;
        Asset.Nodes.RemoveAll(n => removed.IsSelfOrAncestorOf(n));
        Selected = parent ?? (Asset.Nodes.Count > 0 ? Asset.Nodes[Math.Clamp(index, 0, Asset.Nodes.Count - 1)] : null);
        Changed();
    }

    /// <summary>選んだノードを子孫ごと複製する (新しい ID)。戻り値 = 複製した根</summary>
    public CollisionNode? DuplicateSelected()
    {
        if (Asset == null || Selected == null) return null;
        var source = Asset.Nodes.Where(n => Selected.IsSelfOrAncestorOf(n)).ToList();
        var map = new Dictionary<CollisionNode, CollisionNode>();
        var nextId = Asset.NextId();
        var used = new HashSet<int>(Asset.Nodes.Select(n => n.Id));
        foreach (var node in source)
        {
            var copy = node.Clone();
            while (used.Contains(nextId)) nextId++;
            copy.Id = nextId;
            used.Add(nextId);
            copy.Name = uniqueName(node.Name);
            map[node] = copy;
        }
        foreach (var copy in map.Values) if (copy.Parent != null && map.TryGetValue(copy.Parent, out var p)) copy.Parent = p;
        var insertAt = Asset.Nodes.IndexOf(source[^1]) + 1;
        Asset.Nodes.InsertRange(insertAt, source.Select(n => map[n]));
        Selected = map[source[0]];
        Selected.Position += new Vector3(0.1f, 0, 0);
        Changed();
        return Selected;
    }

    /// <summary>親を変える (null = 一番上)。持ち主から見た位置 / 回転は変えない。戻り値 = 変えたか</summary>
    public bool SetParent(CollisionNode node, CollisionNode? parent)
    {
        if (Asset == null || node.Parent == parent) return false;
        if (parent != null && node.IsSelfOrAncestorOf(parent)) return false;   // 自分の子孫の下には入れない (輪になる)
        var (position, rotation) = Asset.OwnerPoseOf(node);
        node.Parent = parent;
        if (parent != null)
        {
            var (pp, pr) = Asset.OwnerPoseOf(parent);
            var inverse = Quaternion.Inverse(pr);
            node.Position = Vector3.Transform(position - pp, inverse);
            node.Rotation = Quaternion.Normalize(Quaternion.Concatenate(rotation, inverse));
        }
        else
        {
            node.Position = position;
            node.Rotation = rotation;
        }
        // 一覧の順 : 子孫ごと新しい親の子の最後へ
        var moved = Asset.Nodes.Where(n => node.IsSelfOrAncestorOf(n)).ToList();
        Asset.Nodes.RemoveAll(n => node.IsSelfOrAncestorOf(n));
        var insertAt = Asset.Nodes.Count;
        if (parent != null)
        {
            var last = Asset.Nodes.IndexOf(parent);
            for (int i = last + 1; i < Asset.Nodes.Count; i++) if (parent.IsSelfOrAncestorOf(Asset.Nodes[i])) last = i;
            insertAt = last + 1;
        }
        Asset.Nodes.InsertRange(insertAt, moved);
        _eulerCache.Remove(node);
        Changed();
        return true;
    }

    /// <summary>プレビューに出すモデル (null = 出さない)。アセットに覚える</summary>
    public void SelectModel(string? path)
    {
        if (Asset == null) return;
        Asset.PreviewModel = path;
        commit();   // 形は変わらないので Version は上げない
        loadModel();
        _frameRequested = true;
    }

    // node の色 : 選んだ物は黄色 / 選んだ物の子孫は橙色 / トリガーは水色 / 他は緑
    public uint ColorOf(CollisionNode node)
    {
        if (Selected != null)
        {
            if (node == Selected) return SelectedColor;
            if (Selected.IsSelfOrAncestorOf(node)) return DescendantColor;
        }
        return node.IsTrigger ? TriggerColor : NormalColor;
    }

    // ---------------------------------------------------------------------
    // プレビューの世界
    // ---------------------------------------------------------------------

    private void loadModel()
    {
        _modelError = null;
        _hasModelBounds = false;
        if (Preview == null || Asset == null) return;
        var world = Preview.World;
        if (!ModelEntity.IsNull && world.IsAlive(ModelEntity)) world.DestroyEntity(ModelEntity);
        ModelEntity = Entity.Null;
        _loadedModel = Asset.PreviewModel;
        if (string.IsNullOrEmpty(Asset.PreviewModel)) return;
        try
        {
            var model = _engine.Assets.LoadModel(Asset.PreviewModel);
            ModelEntity = world.CreateEntity("Model");
            world.Add(ModelEntity, new Transform(Vector3.Zero));
            world.Add(ModelEntity, new MeshRenderer(model));
            _hasModelBounds = new Picker(_engine).TryGetBounds(model, out _modelMin, out _modelMax);
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or ArgumentException or InvalidOperationException)
        {
            _modelError = ex.Message;
            Console.WriteLine($"[Collision] {ex.Message}");
        }
    }

    /// <summary>モデルとコライダーの全体が見えるようにカメラを合わせる</summary>
    public void FrameAll()
    {
        if (Preview == null || Asset == null) return;
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        if (_hasModelBounds) { min = _modelMin; max = _modelMax; }
        foreach (ref readonly var part in Asset.Parts)
        {
            var (center, _, size) = CollisionDraw.WorldShape(part, Vector3.Zero, Quaternion.Identity, Vector3.One);
            var r = part.Shape == ColliderShape.Box ? size.Length() : part.Shape == ColliderShape.Capsule ? size.X + size.Y : size.X;
            min = Vector3.Min(min, center - new Vector3(r));
            max = Vector3.Max(max, center + new Vector3(r));
        }
        if (min.X > max.X) { min = -Vector3.One; max = Vector3.One; }
        var radius = Math.Max(0.1f, (max - min).Length() * 0.5f);
        Preview.Frame((min + max) * 0.5f, radius);
        Preview.GridSize = Math.Max(4, MathF.Ceiling(radius * 4));
    }

    // ---------------------------------------------------------------------
    // 窓
    // ---------------------------------------------------------------------

    public void Draw()
    {
        HasFocus = false;
        if (!IsOpen || Asset == null) return;
        // 外でファイルを書き換えて読み直された (ノードが作り直された) : 選んでいる物を選び直し、元に戻す記録の基準もそこからにする
        if (Selected != null && !Asset.Nodes.Contains(Selected))
        {
            Selected = Asset.FindById(Selected.Id) ?? Asset.Nodes.FirstOrDefault();
            _committed = Asset.ToJson();
            _eulerCache.Clear();
        }
        if (_loadedModel != Asset.PreviewModel) loadModel();
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowSize(new Vector2(Math.Min(1200, viewport.WorkSize.X * 0.75f), Math.Min(760, viewport.WorkSize.Y * 0.75f)), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(viewport.WorkPos + viewport.WorkSize * 0.5f, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));
        var open = true;
        var title = $"Collision Editor - {System.IO.Path.GetFileName(Path)}{(_dirty ? " *" : "")}###CollisionEditor";
        var visible = ImGui.Begin(title, ref open, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!open)
        {
            ImGui.End();
            Close();
            return;
        }
        if (!visible) { ImGui.End(); return; }
        HasFocus = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        drawToolbar();
        var avail = ImGui.GetContentRegionAvail();
        const float SideWidth = 340;
        ImGui.BeginChild("##side", new Vector2(SideWidth, avail.Y), ImGuiChildFlags.Borders);
        drawSide(avail.Y);
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginChild("##view", new Vector2(Math.Max(50, avail.X - SideWidth - 8), avail.Y), ImGuiChildFlags.None, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        drawView(ImGui.GetContentRegionAvail());
        ImGui.EndChild();

        if (HasFocus) handleKeys();
        if (_pendingCommit && !ImGui.IsAnyItemActive() && !_gizmo.Dragging) commit();
        if (_dirty && ImGui.GetTime() - _changedAt > SaveDelay && !ImGui.IsAnyItemActive() && !_gizmo.Dragging) Flush();
        ImGui.End();
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
        if (ImGui.IsKeyPressed(ImGuiKey.F)) FrameAll();
        if (ImGui.IsKeyPressed(ImGuiKey.W)) _gizmo.Mode = GizmoMode.Translate;
        if (ImGui.IsKeyPressed(ImGuiKey.E)) _gizmo.Mode = GizmoMode.Rotate;
        if (ImGui.IsKeyPressed(ImGuiKey.R)) _gizmo.Mode = GizmoMode.Scale;
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
        ImGui.TextDisabled("|");
        ImGui.SameLine();
        if (ImGui.RadioButton("Move (W)", _gizmo.Mode == GizmoMode.Translate)) _gizmo.Mode = GizmoMode.Translate;
        ImGui.SameLine();
        if (ImGui.RadioButton("Rotate (E)", _gizmo.Mode == GizmoMode.Rotate)) _gizmo.Mode = GizmoMode.Rotate;
        ImGui.SameLine();
        if (ImGui.RadioButton("Size (R)", _gizmo.Mode == GizmoMode.Scale)) _gizmo.Mode = GizmoMode.Scale;
        ImGui.SameLine();
        ImGui.Checkbox("Local", ref _gizmo.Local);
        ImGui.SameLine();
        ImGui.TextDisabled("|");
        ImGui.SameLine();
        ImGui.Checkbox("Model", ref ShowModel);
        ImGui.SameLine();
        ImGui.Checkbox("X-Ray", ref XRay);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Draw colliders in front of the model");
        ImGui.SameLine();
        ImGui.Checkbox("Grid", ref ShowGrid);
        ImGui.SameLine();
        if (ImGui.Button("Frame (F)")) FrameAll();
    }

    private void drawSide(float height)
    {
        var asset = Asset!;
        // モデル
        ImGui.SetNextItemWidth(250);
        if (ImGui.BeginCombo("Model", asset.PreviewModel != null ? System.IO.Path.GetFileName(asset.PreviewModel) : "(none)"))
        {
            if (ImGui.Selectable("(none)", asset.PreviewModel == null)) SelectModel(null);
            foreach (var entry in _database.Entries.Where(e => e.Kind is AssetKind.Model or AssetKind.FbxModel).OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
                if (ImGui.Selectable(entry.Path, entry.Path == asset.PreviewModel)) SelectModel(entry.Path);
            ImGui.EndCombo();
        }
        if (ImGui.BeginDragDropTarget())
        {
            if (ProjectPanel.AcceptDrop(AssetKind.Model, AssetKind.FbxModel) is { } dropped) SelectModel(dropped.Path);
            ImGui.EndDragDropTarget();
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("A model in the project shown with the colliders (preview only)");
        if (_modelError != null) ImGui.TextColored(new Vector4(1, 0.4f, 0.3f, 1), _modelError);

        // 階層
        ImGui.SeparatorText("Hierarchy");
        if (ImGui.Button("+ Box")) AddNode(CollisionNodeShape.Box, Selected);
        ImGui.SameLine();
        if (ImGui.Button("+ Capsule")) AddNode(CollisionNodeShape.Capsule, Selected);
        ImGui.SameLine();
        if (ImGui.Button("+ Sphere")) AddNode(CollisionNodeShape.Sphere, Selected);
        ImGui.SameLine();
        if (ImGui.Button("+ Empty")) AddNode(CollisionNodeShape.Empty, Selected);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("A node without a shape (groups its children)");
        var duplicates = asset.DuplicateIds();
        ImGui.BeginChild("##tree", new Vector2(0, Math.Max(120, height * 0.38f)), ImGuiChildFlags.Borders);
        foreach (var node in asset.Nodes.Where(n => n.Parent == null).ToList()) drawTree(node, duplicates);
        // 何も無い所に落とす : 一番上へ
        ImGui.Dummy(new Vector2(ImGui.GetContentRegionAvail().X, Math.Max(20, ImGui.GetContentRegionAvail().Y)));
        if (ImGui.BeginDragDropTarget())
        {
            if (acceptNodeDrop() is { } dropped) SetParent(dropped, null);
            ImGui.EndDragDropTarget();
        }
        if (ImGui.IsItemClicked()) Selected = null;
        ImGui.EndChild();
        if (duplicates.Count > 0) ImGui.TextColored(new Vector4(1, 0.35f, 0.3f, 1), $"Duplicate IDs : {string.Join(", ", duplicates)}");
        ImGui.TextDisabled($"{asset.Nodes.Count} nodes / {asset.Parts.Length} colliders");

        // 詳細
        ImGui.SeparatorText("Details");
        if (Selected != null) drawDetails(Selected, duplicates);
        else ImGui.TextDisabled("Select a node");
    }

    private void drawTree(CollisionNode node, HashSet<int> duplicates)
    {
        var asset = Asset!;
        var children = asset.Nodes.Where(n => n.Parent == node).ToList();
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (children.Count == 0) flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        if (node == Selected) flags |= ImGuiTreeNodeFlags.Selected;
        var highlight = Selected != null && node != Selected && Selected.IsSelfOrAncestorOf(node);   // 選んだ物の子孫は橙色の字
        var duplicate = duplicates.Contains(node.Id);
        if (duplicate) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.35f, 0.3f, 1));
        else if (highlight) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.65f, 0.25f, 1));
        var shape = node.Shape == CollisionNodeShape.Empty ? "" : $" [{node.Shape}]";
        var open = ImGui.TreeNodeEx($"{node.Name}  (ID {node.Id}){shape}{(node.IsTrigger ? " trigger" : "")}##node{asset.Nodes.IndexOf(node)}", flags);
        if (duplicate || highlight) ImGui.PopStyleColor();
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen()) Selected = node;
        if (ImGui.BeginDragDropSource())
        {
            _draggedNode = node;
            ImGui.SetDragDropPayload("COLLISION_NODE", IntPtr.Zero, 0);
            ImGui.TextUnformatted(node.Name);
            ImGui.EndDragDropSource();
        }
        if (ImGui.BeginDragDropTarget())
        {
            if (acceptNodeDrop() is { } dropped) SetParent(dropped, node);
            ImGui.EndDragDropTarget();
        }
        if (ImGui.BeginPopupContextItem($"##ctx{asset.Nodes.IndexOf(node)}"))
        {
            Selected = node;
            if (ImGui.MenuItem("Add Box"))     AddNode(CollisionNodeShape.Box, node);
            if (ImGui.MenuItem("Add Capsule")) AddNode(CollisionNodeShape.Capsule, node);
            if (ImGui.MenuItem("Add Sphere"))  AddNode(CollisionNodeShape.Sphere, node);
            if (ImGui.MenuItem("Add Empty"))   AddNode(CollisionNodeShape.Empty, node);
            ImGui.Separator();
            if (ImGui.MenuItem("Duplicate", "Ctrl+D")) DuplicateSelected();
            if (ImGui.MenuItem("Unparent", null, false, node.Parent != null)) SetParent(node, null);
            if (ImGui.MenuItem("Delete", "Delete")) DeleteSelected();
            ImGui.EndPopup();
        }
        if (children.Count == 0 || !open) return;
        foreach (var child in children) drawTree(child, duplicates);
        ImGui.TreePop();
    }

    private static unsafe CollisionNode? acceptNodeDrop()
    {
        var payload = ImGui.AcceptDragDropPayload("COLLISION_NODE");
        if (payload.NativePtr == null) return null;
        var node = _draggedNode;
        _draggedNode = null;
        return node;
    }

    private void drawDetails(CollisionNode node, HashSet<int> duplicates)
    {
        var name = node.Name;
        if (ImGui.InputText("Name", ref name, 128)) { node.Name = name; _pendingCommit = true; }
        var id = node.Id;
        if (duplicates.Contains(node.Id)) ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(1, 0.35f, 0.3f, 1));
        if (ImGui.InputInt("ID", ref id)) { node.Id = id; editing(); }
        if (duplicates.Contains(node.Id)) ImGui.PopStyleColor();
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Collision.SelfPart.Id / OtherPart.Id in OnCollisionEnter etc. Must be unique in this asset.");
        var shape = (int)node.Shape;
        var shapes = Enum.GetNames<CollisionNodeShape>();
        if (ImGui.Combo("Shape", ref shape, shapes, shapes.Length)) { node.Shape = (CollisionNodeShape)shape; Changed(); }
        var position = node.Position;
        if (ImGui.DragFloat3("Position", ref position, 0.005f)) { node.Position = position; editing(); }
        if (!_eulerCache.TryGetValue(node, out var euler) || MathF.Abs(Quaternion.Dot(fromEuler(euler), node.Rotation)) < 0.99999f) euler = toEuler(node.Rotation);
        var edited = euler;
        if (ImGui.DragFloat3("Rotation", ref edited, 0.5f)) { node.Rotation = fromEuler(edited); editing(); }
        _eulerCache[node] = edited;
        switch (node.Shape)
        {
        case CollisionNodeShape.Box:
        {
            var size = node.Size;
            if (ImGui.DragFloat3("Half Size", ref size, 0.005f, 0.001f, 1000)) { node.Size = Vector3.Max(size, new Vector3(0.001f)); editing(); }
            break;
        }
        case CollisionNodeShape.Sphere:
        {
            var radius = node.Size.X;
            if (ImGui.DragFloat("Radius", ref radius, 0.005f, 0.001f, 1000)) { node.Size = node.Size with { X = Math.Max(radius, 0.001f) }; editing(); }
            break;
        }
        case CollisionNodeShape.Capsule:
        {
            var radius = node.Size.X;
            var half = node.Size.Y;
            if (ImGui.DragFloat("Radius", ref radius, 0.005f, 0.001f, 1000)) { node.Size = node.Size with { X = Math.Max(radius, 0.001f) }; editing(); }
            if (ImGui.DragFloat("Half Height", ref half, 0.005f, 0, 1000)) { node.Size = node.Size with { Y = Math.Max(half, 0) }; editing(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Half of the cylinder part along the node's Y (the caps are not included)");
            break;
        }
        }
        if (node.Shape != CollisionNodeShape.Empty)
        {
            var trigger = node.IsTrigger;
            if (ImGui.Checkbox("Is Trigger", ref trigger)) { node.IsTrigger = trigger; Changed(); }
            var settings = ProjectSettings.Current;
            ImGui.SetNextItemWidth(200);
            if (ImGui.BeginCombo("Layer", settings.LayerName(node.Layer)))
            {
                for (int i = 0; i < 32; i++)
                    if (ImGui.Selectable($"{i} : {settings.LayerName(i)}", i == node.Layer)) { node.Layer = i; Changed(); }
                ImGui.EndCombo();
            }
            ImGui.SetNextItemWidth(200);
            if (ImGui.BeginCombo("Collide With", node.CollideWith == 0xFFFFFFFFu ? "Everything" : node.CollideWith == 0 ? "Nothing" : $"0x{node.CollideWith:X8}"))
            {
                if (ImGui.Selectable("Everything")) { node.CollideWith = 0xFFFFFFFFu; Changed(); }
                if (ImGui.Selectable("Nothing")) { node.CollideWith = 0; Changed(); }
                for (int i = 0; i < 32; i++)
                {
                    var on = (node.CollideWith & (1u << i)) != 0;
                    if (ImGui.Checkbox($"{i} : {settings.LayerName(i)}", ref on)) { node.CollideWith = on ? node.CollideWith | (1u << i) : node.CollideWith & ~(1u << i); Changed(); }
                }
                ImGui.EndCombo();
            }
            var friction = node.Friction;
            if (ImGui.DragFloat("Friction", ref friction, 0.01f, 0, 10)) { node.Friction = Math.Max(0, friction); editing(); }
            var restitution = node.Restitution;
            if (ImGui.DragFloat("Restitution", ref restitution, 0.01f, 0, 1)) { node.Restitution = Math.Clamp(restitution, 0, 1); editing(); }
        }
        var (ownerPosition, _) = Asset!.OwnerPoseOf(node);
        ImGui.TextDisabled($"Owner space : {ownerPosition.X:F3}, {ownerPosition.Y:F3}, {ownerPosition.Z:F3}");
        if (node.Parent != null) ImGui.TextDisabled($"Parent : {node.Parent.Name} (ID {node.Parent.Id})");
    }

    private static Quaternion fromEuler(Vector3 degrees) =>
        Quaternion.CreateFromYawPitchRoll(degrees.Y * MathEx.Deg2Rad, degrees.X * MathEx.Deg2Rad, degrees.Z * MathEx.Deg2Rad);

    private static Vector3 toEuler(Quaternion q)
    {
        var sinPitch = 2.0f * (q.W * q.X - q.Y * q.Z);
        float pitch = MathF.Abs(sinPitch) >= 1.0f ? MathF.CopySign(MathF.PI / 2, sinPitch) : MathF.Asin(sinPitch);
        float yaw   = MathF.Atan2(2.0f * (q.W * q.Y + q.X * q.Z), 1.0f - 2.0f * (q.X * q.X + q.Y * q.Y));
        float roll  = MathF.Atan2(2.0f * (q.W * q.Z + q.X * q.Y), 1.0f - 2.0f * (q.X * q.X + q.Z * q.Z));
        return new Vector3(pitch, yaw, roll) * MathEx.Rad2Deg;
    }

    // ---------------------------------------------------------------------
    // プレビュー
    // ---------------------------------------------------------------------

    private SceneCamera cameraOf(PreviewScene preview, Vector2 min, Vector2 size)
    {
        var eye = preview.CameraPosition;
        var z = Vector3.Normalize(preview.Target - eye);
        var x = Vector3.Cross(Vector3.UnitY, z);
        x = x.LengthSquared() < 1e-8f ? Vector3.UnitX : Vector3.Normalize(x);
        var y = Vector3.Cross(z, x);
        var rotation = Quaternion.CreateFromRotationMatrix(new Matrix4x4(x.X, x.Y, x.Z, 0, y.X, y.Y, y.Z, 0, z.X, z.Y, z.Z, 0, 0, 0, 0, 1));
        return new SceneCamera(eye, rotation, preview.FieldOfView, min, size);
    }

    private void drawView(Vector2 size)
    {
        if (Preview == null) { ImGui.TextDisabled("No free preview view (up to 15)."); return; }
        var preview = Preview;
        preview.SetSize((int)Math.Max(1, size.X), (int)Math.Max(1, size.Y));
        if (_frameRequested) { FrameAll(); _frameRequested = false; }
        preview.Grid = ShowGrid;
        if (!ModelEntity.IsNull && preview.World.IsAlive(ModelEntity) && preview.World.IsActive(ModelEntity) != ShowModel)
            preview.World.SetActive(ModelEntity, ShowModel);   // 無効な物は描かない
        if (preview.TextureId != 0xFFFFFFFFu) ImGui.Image((IntPtr)preview.TextureId, size);
        else ImGui.Dummy(size);
        var hovered = ImGui.IsItemHovered();
        var min = ImGui.GetItemRectMin();
        var camera = cameraOf(preview, min, size);
        var io = ImGui.GetIO();
        var draw = ImGui.GetWindowDrawList();

        // ギズモ (選んだノード)。代わりのエンティティを持ち主から見た位置に置いて、シーンと同じギズモを使う
        var gizmoUsed = false;
        if (Selected != null)
        {
            var world = preview.World;
            if (_proxy.IsNull || !world.IsAlive(_proxy))
            {
                _proxy = world.CreateEntity("GizmoProxy");
                world.Add(_proxy, new Transform(Vector3.Zero));
            }
            var (ownerPosition, ownerRotation) = Asset!.OwnerPoseOf(Selected);
            ref var proxy = ref world.Get<Transform>(_proxy);
            if (!_gizmo.Dragging) { proxy.Position = ownerPosition; proxy.Rotation = ownerRotation; proxy.Scale = Vector3.One; }
            if (_gizmo.Dragging && !_wasDragging) _dragStartSize = Selected.Size;
            if (_gizmo.Update(camera, draw, world, _proxy, hovered, out var result))
            {
                ref var t = ref world.Get<Transform>(_proxy);
                t.Position = result.Position;
                t.Rotation = result.Rotation;
                t.Scale = result.Scale;
                applyGizmo(Selected, result);
            }
            gizmoUsed = _gizmo.Dragging || _gizmo.Hovered;
            _wasDragging = _gizmo.Dragging;
        }

        if (hovered)
        {
            if (io.MouseWheel != 0) preview.Distance = Math.Clamp(preview.Distance * MathF.Pow(0.88f, io.MouseWheel), 0.05f, 1000);
            if (ImGui.IsMouseDown(ImGuiMouseButton.Right))
            {
                preview.Yaw += io.MouseDelta.X * 0.4f;
                preview.Pitch = Math.Clamp(preview.Pitch + io.MouseDelta.Y * 0.4f, -89, 89);
            }
            if (ImGui.IsMouseDown(ImGuiMouseButton.Middle))
            {
                var yaw = preview.Yaw * MathF.PI / 180;
                var right = new Vector3(MathF.Cos(yaw), 0, -MathF.Sin(yaw));
                var scale = preview.Distance * 0.0025f;
                preview.Target += (-right * io.MouseDelta.X + Vector3.UnitY * io.MouseDelta.Y) * scale;
            }
            // クリックで選ぶ (ギズモを触っていない時)
            if (!gizmoUsed && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
                Selected = Pick(camera.Position, camera.RayDirection(io.MousePos)) ?? Selected;
        }
        addLines(preview);

        // 選んだノードの名前
        if (Selected != null && preview.WorldToPixel(Asset!.OwnerPoseOf(Selected).Position) is { } pixel)
            draw.AddText(min + pixel + new Vector2(8, -20), 0xFF40E0FFu, $"{Selected.Name} (ID {Selected.Id})");
        draw.AddText(min + new Vector2(8, size.Y - 22), 0xFFB0B0B0u, "RMB : orbit  MMB : pan  Wheel : zoom  F : frame  Click : select");
    }

    // ギズモの結果 (持ち主から見た値) → ノードの値 (親から見た値。大きさのモードは形の大きさ)
    private void applyGizmo(CollisionNode node, Transform result)
    {
        if (_gizmo.Mode == GizmoMode.Scale)
        {
            var s = Vector3.Abs(result.Scale);
            node.Size = node.Shape switch
            {
                CollisionNodeShape.Sphere  => _dragStartSize with { X = Math.Max(0.001f, _dragStartSize.X * MathF.Max(MathF.Max(s.X, s.Y), s.Z)) },
                CollisionNodeShape.Capsule => new Vector3(Math.Max(0.001f, _dragStartSize.X * MathF.Max(s.X, s.Z)), Math.Max(0, _dragStartSize.Y * s.Y), _dragStartSize.Z),
                _                          => Vector3.Max(_dragStartSize * s, new Vector3(0.001f)),
            };
        }
        else
        {
            var position = result.Position;
            var rotation = result.Rotation;
            if (node.Parent != null)
            {
                var (pp, pr) = Asset!.OwnerPoseOf(node.Parent);
                var inverse = Quaternion.Inverse(pr);
                position = Vector3.Transform(position - pp, inverse);
                rotation = Quaternion.Concatenate(rotation, inverse);
            }
            node.Position = position;
            node.Rotation = Quaternion.Normalize(rotation);
            _eulerCache.Remove(node);
        }
        editing();
    }

    /// <summary>光線に一番近く当たる部品のノード (無ければ null)</summary>
    public CollisionNode? Pick(Vector3 origin, Vector3 direction)
    {
        if (Asset == null) return null;
        CollisionNode? best = null;
        var bestT = float.MaxValue;
        foreach (ref readonly var part in Asset.Parts)
        {
            var (center, rotation, size) = CollisionDraw.WorldShape(part, Vector3.Zero, Quaternion.Identity, Vector3.One);
            float? t = part.Shape switch
            {
                ColliderShape.Sphere  => raySphere(origin, direction, center, size.X),
                ColliderShape.Capsule => rayCapsule(origin, direction, center, rotation, size.X, size.Y),
                _                     => rayBox(origin, direction, center, rotation, size),
            };
            if (t is { } hit && hit < bestT) { bestT = hit; best = Asset.Nodes[part.NodeIndex]; }
        }
        return best;
    }

    private static float? raySphere(Vector3 o, Vector3 d, Vector3 c, float r)
    {
        var m = o - c;
        var b = Vector3.Dot(m, d);
        var cc = Vector3.Dot(m, m) - r * r;
        if (cc > 0 && b > 0) return null;
        var disc = b * b - cc;
        if (disc < 0) return null;
        return Math.Max(0, -b - MathF.Sqrt(disc));
    }

    private static float? rayBox(Vector3 o, Vector3 d, Vector3 c, Quaternion q, Vector3 h)
    {
        var inverse = Quaternion.Inverse(q);
        var lo = Vector3.Transform(o - c, inverse);
        var ld = Vector3.Transform(d, inverse);
        float tmin = 0, tmax = float.MaxValue;
        for (int i = 0; i < 3; i++)
        {
            var p = i == 0 ? lo.X : i == 1 ? lo.Y : lo.Z;
            var v = i == 0 ? ld.X : i == 1 ? ld.Y : ld.Z;
            var e = i == 0 ? h.X : i == 1 ? h.Y : h.Z;
            if (MathF.Abs(v) < 1e-8f) { if (p < -e || p > e) return null; continue; }
            var t1 = (-e - p) / v;
            var t2 = (e - p) / v;
            if (t1 > t2) (t1, t2) = (t2, t1);
            tmin = Math.Max(tmin, t1);
            tmax = Math.Min(tmax, t2);
            if (tmin > tmax) return null;
        }
        return tmin;
    }

    private static float? rayCapsule(Vector3 o, Vector3 d, Vector3 c, Quaternion q, float r, float halfHeight)
    {
        // 線分と光線の一番近い点で近似 (選ぶだけなので十分)
        var axis = Vector3.Transform(Vector3.UnitY, q);
        var a = c - axis * halfHeight;
        var b = c + axis * halfHeight;
        float? best = null;
        const int Steps = 8;
        for (int i = 0; i <= Steps; i++)
        {
            var p = Vector3.Lerp(a, b, i / (float)Steps);
            if (raySphere(o, d, p, r) is { } t && (best == null || t < best)) best = t;
        }
        return best;
    }

    // ---------- 線 ----------

    private void addLines(PreviewScene preview)
    {
        LineCount = 0;
        HighlightedCount = 0;
        var asset = Asset!;
        // 親と子の中心を細い線で結ぶ (親子が分かるように)
        foreach (var node in asset.Nodes)
        {
            if (node.Parent == null) continue;
            line(preview, asset.OwnerPoseOf(node.Parent).Position, asset.OwnerPoseOf(node).Position, Selected != null && Selected.IsSelfOrAncestorOf(node) ? DescendantColor : LinkColor);
        }
        // 形の無いノード : 小さな十字
        foreach (var node in asset.Nodes)
        {
            if (node.Shape != CollisionNodeShape.Empty) continue;
            var (p, q) = asset.OwnerPoseOf(node);
            var color = ColorOf(node);
            const float s = 0.05f;
            line(preview, p - Vector3.Transform(Vector3.UnitX, q) * s, p + Vector3.Transform(Vector3.UnitX, q) * s, color);
            line(preview, p - Vector3.Transform(Vector3.UnitY, q) * s, p + Vector3.Transform(Vector3.UnitY, q) * s, color);
            line(preview, p - Vector3.Transform(Vector3.UnitZ, q) * s, p + Vector3.Transform(Vector3.UnitZ, q) * s, color);
        }
        // 形 (選んだ物を最後に描いて上に重ねる)
        var parts = asset.Parts;
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (ref readonly var part in parts)
            {
                var node = asset.Nodes[part.NodeIndex];
                var color = ColorOf(node);
                var highlighted = color == SelectedColor || color == DescendantColor;
                if (highlighted != (pass == 1)) continue;
                if (highlighted) HighlightedCount++;
                var (center, rotation, size) = CollisionDraw.WorldShape(part, Vector3.Zero, Quaternion.Identity, Vector3.One);
                switch (part.Shape)
                {
                case ColliderShape.Sphere:  wireSphere(preview, center, rotation, size.X, color); break;
                case ColliderShape.Capsule: wireCapsule(preview, center, rotation, size.X, size.Y, color); break;
                default:                    wireBox(preview, center, rotation, size, color); break;
                }
            }
        }
    }

    private void line(PreviewScene preview, Vector3 a, Vector3 b, uint color)
    {
        preview.Line(a, b, color, overlay: XRay);
        LineCount++;
    }

    private void wireBox(PreviewScene preview, Vector3 c, Quaternion q, Vector3 h, uint color)
    {
        Span<Vector3> p = stackalloc Vector3[8];
        for (int i = 0; i < 8; i++)
            p[i] = c + Vector3.Transform(new Vector3((i & 1) != 0 ? h.X : -h.X, (i & 2) != 0 ? h.Y : -h.Y, (i & 4) != 0 ? h.Z : -h.Z), q);
        for (int i = 0; i < 8; i++)
        {
            if ((i & 1) == 0) line(preview, p[i], p[i | 1], color);
            if ((i & 2) == 0) line(preview, p[i], p[i | 2], color);
            if ((i & 4) == 0) line(preview, p[i], p[i | 4], color);
        }
    }

    private const int CircleSegments = 24;

    private void circle(PreviewScene preview, Vector3 c, Vector3 u, Vector3 v, float r, uint color, float from = 0, float to = MathF.Tau)
    {
        var previous = c + (u * MathF.Cos(from) + v * MathF.Sin(from)) * r;
        var steps = Math.Max(2, (int)MathF.Ceiling(CircleSegments * (to - from) / MathF.Tau));
        for (int i = 1; i <= steps; i++)
        {
            var angle = from + (to - from) * i / steps;
            var point = c + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * r;
            line(preview, previous, point, color);
            previous = point;
        }
    }

    private void wireSphere(PreviewScene preview, Vector3 c, Quaternion q, float r, uint color)
    {
        var x = Vector3.Transform(Vector3.UnitX, q);
        var y = Vector3.Transform(Vector3.UnitY, q);
        var z = Vector3.Transform(Vector3.UnitZ, q);
        circle(preview, c, x, y, r, color);
        circle(preview, c, y, z, r, color);
        circle(preview, c, z, x, r, color);
    }

    private void wireCapsule(PreviewScene preview, Vector3 c, Quaternion q, float r, float halfHeight, uint color)
    {
        var x = Vector3.Transform(Vector3.UnitX, q);
        var y = Vector3.Transform(Vector3.UnitY, q);
        var z = Vector3.Transform(Vector3.UnitZ, q);
        var top = c + y * halfHeight;
        var bottom = c - y * halfHeight;
        circle(preview, top, x, z, r, color);
        circle(preview, bottom, x, z, r, color);
        line(preview, top + x * r, bottom + x * r, color);
        line(preview, top - x * r, bottom - x * r, color);
        line(preview, top + z * r, bottom + z * r, color);
        line(preview, top - z * r, bottom - z * r, color);
        circle(preview, top, x, y, r, color, 0, MathF.PI);
        circle(preview, top, z, y, r, color, 0, MathF.PI);
        circle(preview, bottom, x, y, r, color, MathF.PI, MathF.Tau);
        circle(preview, bottom, z, y, r, color, MathF.PI, MathF.Tau);
    }
}
