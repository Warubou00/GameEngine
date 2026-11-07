using System.Numerics;
using GameEngine;
using GameEngine.Fbx;
using GameEngine.Rendering;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// FBX のプレビューの窓 (AN5)。Project で .mod.fbx / .skl.fbx / .anm.fbx をダブルクリック (または View > FBX Preview)
//   シーンとは別の小さな世界 (PreviewScene) に、モデル / 骨 / アニメーションを作って、この窓の中だけに描く
//   (シーンの物は窓に出ず、窓の物はシーンに出ない。描画オブジェクトに視点の番号が付く)
//   - 右ドラッグ : 回す / 中ドラッグ : ずらす / ホイール : 近づく・離れる / F : 全体を見る
//   - 表示 : 面 / ワイヤーフレーム / 法線 / 骨 / 床の格子
//   - モデル : マテリアルの一覧、頂点 / 三角形の数
//   - スケルトン : 骨の木 (選ぶとその骨が光る)、名前
//   - アニメーション : モデル + スケルトン + クリップを選んで 再生 / 止める / 時間のバー / 速さ / ループ、今の時間とフレーム
//   アセットを読み直すと作り直す (時間はそのまま)。窓を閉じると世界ごと消える (描画オブジェクトも残らない)
// ==========================================================================
public sealed class PreviewWindow
{
    private const int MaxNormals = 20000;

    private readonly Engine _engine;
    private readonly AssetDatabase _database;
    private bool _rebuildRequested;
    private bool _frameRequested;
    private string? _error;
    private Skeleton? _skeleton;
    private AnimationClip? _clip;
    private FbxModelData? _modelData;   // モデル全体 (情報 / マテリアル / モデルだけの時の法線)
    private readonly Dictionary<Entity, FbxModelData?> _partData = new();   // 部品ごと (骨で動く時の法線)
    private Vector3 _boundsCenter;
    private float _boundsRadius = 1;

    public bool IsOpen { get; private set; }
    public PreviewScene? Preview { get; private set; }
    public string? ModelPath { get; private set; }
    public string? SkeletonPath { get; private set; }
    public string? ClipPath { get; private set; }
    /// <summary>窓の世界の根 (Animator か MeshRenderer が付いている)</summary>
    public Entity Root { get; private set; } = Entity.Null;
    /// <summary>骨のエンティティ (Skeleton の骨の順)</summary>
    public List<Entity> BoneEntities { get; } = new();
    public List<Entity> Parts { get; } = new();
    public int SelectedBone = -1;

    public bool ShowBones = true;
    public bool ShowNormals;
    public bool ShowGrid = true;
    public bool Shaded = true;
    public bool Wireframe;
    // 前のフレームで描いた線の数 (確認用)
    public int BoneLineCount { get; private set; }
    public int NormalLineCount { get; private set; }
    public bool HasFocus { get; private set; }

    public PreviewWindow(Engine engine, AssetDatabase database)
    {
        _engine = engine;
        _database = database;
        engine.Assets.AssetReloaded += path =>
        {
            if (!IsOpen) return;
            bool same(string? p) => p != null && (path.Equals(p, StringComparison.OrdinalIgnoreCase) || path.StartsWith(p + "#", StringComparison.OrdinalIgnoreCase));
            if (same(ModelPath) || same(SkeletonPath) || same(ClipPath)) _rebuildRequested = true;
        };
    }

    // ---------- 開く / 閉じる ----------

    /// <summary>FBX のアセットを開く (同じ名前の モデル / スケルトン / 最初のクリップ も選ぶ)。戻り値 = 作れたか</summary>
    public bool Open(string assetPath)
    {
        var type = FbxAsset.TypeOf(assetPath);
        if (type == null) return false;
        var folder = assetPath.Contains('/') ? assetPath[..assetPath.LastIndexOf('/')] : "";
        var stem = StemOf(assetPath);
        ModelPath = SkeletonPath = ClipPath = null;
        switch (type)
        {
        case FbxAssetType.Model:
            ModelPath = assetPath;
            SkeletonPath = findSibling(folder, stem, AssetKind.FbxSkeleton);
            break;
        case FbxAssetType.Skeleton:
            SkeletonPath = assetPath;
            ModelPath = findSibling(folder, stem, AssetKind.FbxModel);
            break;
        case FbxAssetType.Animation:
            ClipPath = assetPath;
            // Arm_Wave.anm.fbx → Arm.skl.fbx / Arm.mod.fbx (名前の _ の前を順に短くして探す)
            for (var s = stem; s.Length > 0 && SkeletonPath == null; s = s.Contains('_') ? s[..s.LastIndexOf('_')] : "")
            {
                SkeletonPath = findSibling(folder, s, AssetKind.FbxSkeleton);
                if (SkeletonPath != null) ModelPath = findSibling(folder, s, AssetKind.FbxModel);
            }
            break;
        }
        if (type != FbxAssetType.Animation && SkeletonPath != null) ClipPath = firstClip(folder, StemOf(SkeletonPath));
        IsOpen = true;
        SelectedBone = -1;
        return Rebuild(keepTime: false);
    }

    /// <summary>何も選ばずに窓を開く (Model / Skeleton / Clip の欄で選ぶ)</summary>
    public void OpenEmpty()
    {
        Close();
        IsOpen = true;
        _error = null;
        Preview = _engine.CreatePreview();
    }

    /// <summary>選んでいる物を入れ替えて作り直す (null = 使わない)</summary>
    public bool Select(string? model, string? skeleton, string? clip)
    {
        ModelPath = model;
        SkeletonPath = skeleton;
        ClipPath = clip;
        return Rebuild(keepTime: true);
    }

    /// <summary>窓を閉じる (世界と描画オブジェクトとレンダーターゲットを消す)</summary>
    public void Close()
    {
        IsOpen = false;
        Preview?.Dispose();
        Preview = null;
        Root = Entity.Null;
        BoneEntities.Clear();
        Parts.Clear();
        _partData.Clear();
    }

    public static string StemOf(string path)
    {
        var name = path.Contains('/') ? path[(path.LastIndexOf('/') + 1)..] : path;
        foreach (var ext in new[] { FbxAsset.ModelExtension, FbxAsset.SkeletonExtension, FbxAsset.AnimationExtension })
            if (name.EndsWith(ext, StringComparison.OrdinalIgnoreCase)) return name[..^ext.Length];
        return name;
    }

    private string? findSibling(string folder, string stem, AssetKind kind)
    {
        var ext = kind switch { AssetKind.FbxModel => FbxAsset.ModelExtension, AssetKind.FbxSkeleton => FbxAsset.SkeletonExtension, _ => FbxAsset.AnimationExtension };
        var path = folder.Length > 0 ? $"{folder}/{stem}{ext}" : $"{stem}{ext}";
        return _database.Find(path) is { } entry && entry.Kind == kind ? entry.Path : null;
    }

    private string? firstClip(string folder, string stem) => _database.Entries
        .Where(e => e.Kind == AssetKind.FbxAnimation && e.Path.LastIndexOf('/') == folder.Length && e.Path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) &&
                    (StemOf(e.Path).StartsWith(stem + "_", StringComparison.OrdinalIgnoreCase) || StemOf(e.Path).Equals(stem, StringComparison.OrdinalIgnoreCase)))
        .OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase).Select(e => e.Path).FirstOrDefault();

    /// <summary>選んでいるアセットで世界を作り直す</summary>
    public bool Rebuild(bool keepTime)
    {
        _rebuildRequested = false;
        Preview ??= _engine.CreatePreview();
        if (Preview == null) { _error = "No free preview view (up to 15)."; return false; }
        var world = Preview.World;
        var time = keepTime && World_IsAnimated(out var current) ? current : (float?)null;
        var playing = !keepTime || !World_IsAnimated(out _) || world.Get<Animator>(Root).Playing;
        var speed = keepTime && World_IsAnimated(out _) ? world.Get<Animator>(Root).Speed : 1.0f;
        var loop = !keepTime || !World_IsAnimated(out _) || world.Get<Animator>(Root).Loop;
        Preview.Clear();
        Root = Entity.Null;
        BoneEntities.Clear();
        Parts.Clear();
        _partData.Clear();
        _skeleton = null;
        _clip = null;
        _modelData = null;
        _error = null;
        var assets = _engine.Assets;
        try
        {
            if (ModelPath != null)
            {
                var fbx = assets.LoadFbx(ModelPath);
                if (!fbx.IsValid || fbx.Scene == null) throw new InvalidDataException(fbx.Error ?? $"{ModelPath} : can't read");
                _modelData = FbxModelImporter.Import(fbx.Scene, _database.FullPath(ModelPath));
            }
            if (SkeletonPath != null)
            {
                _skeleton = assets.LoadSkeleton(SkeletonPath);
                _clip = ClipPath != null ? assets.LoadAnimationClip(ClipPath) : null;
                Root = world.CreateEntity(StemOf(SkeletonPath));
                world.Add(Root, new Transform(Vector3.Zero));
                world.Add(Root, new Animator(_skeleton, _clip) { Playing = playing, Speed = speed, Loop = loop, Time = time ?? 0 });
                var created = AnimationRig.Build(world, assets, Root, _skeleton, ModelPath);
                BoneEntities.AddRange(created.Take(_skeleton.Bones.Count));
                Parts.AddRange(created.Skip(_skeleton.Bones.Count));
            }
            else if (ModelPath != null)
            {
                Root = world.CreateEntity(StemOf(ModelPath));
                world.Add(Root, new Transform(Vector3.Zero));
                world.Add(Root, new MeshRenderer(assets.LoadModel(ModelPath)));
                Parts.Add(Root);
            }
        }
        catch (Exception ex) when (ex is InvalidDataException or FileNotFoundException or ArgumentException)
        {
            _error = ex.Message;
            Console.WriteLine($"[Preview] {ex.Message}");
        }
        if (SelectedBone >= BoneEntities.Count) SelectedBone = -1;
        computeBounds();
        if (!keepTime) _frameRequested = true;
        return _error == null && Root != Entity.Null;
    }

    private bool World_IsAnimated(out float time)
    {
        time = 0;
        if (Preview == null || !Preview.World.IsAlive(Root) || !Preview.World.Has<Animator>(Root)) return false;
        time = Preview.World.Get<Animator>(Root).Time;
        return true;
    }

    private void computeBounds()
    {
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);
        if (_modelData != null && _modelData.Positions.Count > 0) { min = _modelData.BoundsMin; max = _modelData.BoundsMax; }
        if (Preview != null)
            foreach (var bone in BoneEntities)
            {
                var p = Hierarchy.GetWorldMatrix(Preview.World, bone).Translation;
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
        if (min.X > max.X) { min = -Vector3.One; max = Vector3.One; }
        _boundsCenter = (min + max) * 0.5f;
        _boundsRadius = Math.Max(0.1f, (max - min).Length() * 0.5f);
    }

    /// <summary>全体が見えるようにカメラを合わせる</summary>
    public void FrameAll()
    {
        if (Preview == null) return;
        Preview.Frame(_boundsCenter, _boundsRadius);
        Preview.GridSize = Math.Max(4, MathF.Ceiling(_boundsRadius * 4));
    }

    // ---------- アニメーション (確認用にも使う) ----------

    public bool Playing
    {
        get => World_IsAnimated(out _) && Preview!.World.Get<Animator>(Root).Playing;
        set { if (World_IsAnimated(out _)) Preview!.World.Get<Animator>(Root).Playing = value; }
    }

    public float Time
    {
        get => World_IsAnimated(out var t) ? t : 0;
        set { if (World_IsAnimated(out _)) Preview!.World.Get<Animator>(Root).Time = Math.Clamp(value, 0, _clip?.Duration ?? 0); }
    }

    public AnimationClip? Clip => _clip;
    public Skeleton? Skeleton => _skeleton;
    public FbxModelData? ModelData => _modelData;

    public bool SelectBone(string name)
    {
        SelectedBone = _skeleton?.IndexOf(name) ?? -1;
        return SelectedBone >= 0;
    }

    // ---------- 窓 ----------

    public void Draw()
    {
        HasFocus = false;
        if (!IsOpen) return;
        if (_rebuildRequested) Rebuild(keepTime: true);
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowSize(new Vector2(Math.Min(1000, viewport.WorkSize.X * 0.7f), Math.Min(640, viewport.WorkSize.Y * 0.7f)), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(viewport.WorkPos + viewport.WorkSize * 0.5f, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));
        var open = true;
        var title = ClipPath ?? SkeletonPath ?? ModelPath;
        var visible = ImGui.Begin(title != null ? $"FBX Preview - {Path.GetFileName(title)}###FbxPreview" : "FBX Preview###FbxPreview", ref open);
        if (!open)
        {
            ImGui.End();
            Close();
            return;
        }
        if (!visible) { ImGui.End(); return; }
        HasFocus = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
        if (Preview == null)
        {
            ImGui.TextWrapped(_error ?? "Double-click a .mod.fbx / .skl.fbx / .anm.fbx in Project.");
            ImGui.End();
            return;
        }
        var avail = ImGui.GetContentRegionAvail();
        if (ImGui.BeginChild("##side", new Vector2(300, avail.Y), ImGuiChildFlags.Borders)) drawSide();
        ImGui.EndChild();
        ImGui.SameLine();
        ImGui.BeginGroup();
        drawView(new Vector2(Math.Max(1, avail.X - 308), avail.Y));
        ImGui.EndGroup();
        ImGui.End();
    }

    private void drawSide()
    {
        // 何を見るか
        assetCombo("Model", AssetKind.FbxModel, ModelPath, p => Select(p, SkeletonPath, ClipPath));
        assetCombo("Skeleton", AssetKind.FbxSkeleton, SkeletonPath, p => Select(ModelPath, p, ClipPath));
        assetCombo("Clip", AssetKind.FbxAnimation, ClipPath, p => Select(ModelPath, SkeletonPath, p));
        if (_error != null) ImGui.TextColored(new Vector4(1, 0.4f, 0.3f, 1), _error);
        ImGui.Separator();
        // 表示
        ImGui.Checkbox("Shaded", ref Shaded); ImGui.SameLine();
        ImGui.Checkbox("Wireframe", ref Wireframe);
        ImGui.Checkbox("Normals", ref ShowNormals); ImGui.SameLine();
        ImGui.Checkbox("Bones", ref ShowBones); ImGui.SameLine();
        ImGui.Checkbox("Grid", ref ShowGrid);
        if (ImGui.Button("Frame (F)")) FrameAll();
        ImGui.Separator();
        // モデル
        if (_modelData != null && ImGui.CollapsingHeader("Model", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"{_modelData.Positions.Count:N0} vertices / {_modelData.TriangleCount:N0} triangles / {_modelData.MeshCount} meshes");
            var size = _modelData.BoundsMax - _modelData.BoundsMin;
            ImGui.Text($"Size {size.X:F2} x {size.Y:F2} x {size.Z:F2} m");
            ImGui.Text($"Materials ({_modelData.Materials.Count})");
            for (int i = 0; i < _modelData.Materials.Count; i++)
            {
                var m = _modelData.Materials[i];
                ImGui.ColorButton($"##mat{i}", m.Color, ImGuiColorEditFlags.NoTooltip, new Vector2(14, 14));
                ImGui.SameLine();
                var texture = m.TexturePath != null ? $" ({Path.GetFileName(m.TexturePath)})" : "";
                ImGui.TextUnformatted($"{m.Name}{texture}{(m.Transparent ? " / transparent" : "")}");
            }
        }
        // 骨
        if (_skeleton != null && ImGui.CollapsingHeader($"Skeleton ({_skeleton.Bones.Count} bones)", ImGuiTreeNodeFlags.DefaultOpen))
        {
            for (int i = 0; i < _skeleton.Bones.Count; i++)
                if (_skeleton.Bones[i].Parent < 0) drawBoneTree(i);
            if (SelectedBone >= 0 && SelectedBone < BoneEntities.Count && Preview != null)
            {
                var p = Hierarchy.GetWorldMatrix(Preview.World, BoneEntities[SelectedBone]).Translation;
                ImGui.TextUnformatted($"{_skeleton.Bones[SelectedBone].Name} : {p.X:F3}, {p.Y:F3}, {p.Z:F3}");
            }
        }
        // クリップ
        if (_clip != null && ImGui.CollapsingHeader("Clip", ImGuiTreeNodeFlags.DefaultOpen))
        {
            ImGui.Text($"{_clip.Name} : {_clip.Duration:F2} s / {_clip.FrameCount} frames @ {_clip.FrameRate:G4} fps");
            ImGui.Text($"{_clip.Tracks.Count} tracks / bones written {Preview?.BonesWritten ?? 0}");
        }
    }

    private void drawBoneTree(int index)
    {
        var bone = _skeleton!.Bones[index];
        var children = Enumerable.Range(0, _skeleton.Bones.Count).Where(i => _skeleton.Bones[i].Parent == index).ToList();
        var flags = ImGuiTreeNodeFlags.OpenOnArrow | ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth;
        if (children.Count == 0) flags |= ImGuiTreeNodeFlags.Leaf | ImGuiTreeNodeFlags.NoTreePushOnOpen;
        if (index == SelectedBone) flags |= ImGuiTreeNodeFlags.Selected;
        var open = ImGui.TreeNodeEx($"{bone.Name}{(bone.IsBone ? "" : " (null)")}##bone{index}", flags);
        if (ImGui.IsItemClicked() && !ImGui.IsItemToggledOpen()) SelectedBone = index == SelectedBone ? -1 : index;
        if (children.Count == 0 || !open) return;
        foreach (var child in children) drawBoneTree(child);
        ImGui.TreePop();
    }

    private void assetCombo(string label, AssetKind kind, string? current, Action<string?> select)
    {
        ImGui.SetNextItemWidth(220);
        if (!ImGui.BeginCombo(label, current != null ? Path.GetFileName(current) : "(none)")) return;
        if (ImGui.Selectable("(none)", current == null)) select(null);
        foreach (var entry in _database.Entries.Where(e => e.Kind == kind).OrderBy(e => e.Path, StringComparer.OrdinalIgnoreCase))
            if (ImGui.Selectable(entry.Path, entry.Path == current)) select(entry.Path);
        ImGui.EndCombo();
    }

    private void drawView(Vector2 size)
    {
        var preview = Preview!;
        var hasClip = _clip != null && World_IsAnimated(out _);
        var barHeight = hasClip ? ImGui.GetFrameHeightWithSpacing() * 2 : 0;
        var imageSize = new Vector2(size.X, Math.Max(1, size.Y - barHeight));
        preview.SetSize((int)imageSize.X, (int)imageSize.Y);
        if (_frameRequested) { FrameAll(); _frameRequested = false; }
        preview.Shaded = Shaded;
        preview.Wireframe = Wireframe;
        preview.Grid = ShowGrid;
        if (preview.TextureId != 0xFFFFFFFFu) ImGui.Image((IntPtr)preview.TextureId, imageSize);
        else ImGui.Dummy(imageSize);
        var hovered = ImGui.IsItemHovered();
        var io = ImGui.GetIO();
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
            if (ImGui.IsKeyPressed(ImGuiKey.F)) FrameAll();
        }
        addLines(preview);
        // 選んだ骨の名前を画面に
        if (ShowBones && SelectedBone >= 0 && SelectedBone < BoneEntities.Count &&
            preview.WorldToPixel(Hierarchy.GetWorldMatrix(preview.World, BoneEntities[SelectedBone]).Translation) is { } pixel)
        {
            var min = ImGui.GetItemRectMin();
            ImGui.GetWindowDrawList().AddText(min + pixel + new Vector2(6, -18), 0xFF40E0FFu, _skeleton!.Bones[SelectedBone].Name);
        }
        if (hasClip) drawTimeline(size.X);
    }

    private void drawTimeline(float width)
    {
        ref var animator = ref Preview!.World.Get<Animator>(Root);
        if (ImGui.Button(animator.Playing ? "Pause" : "Play", new Vector2(60, 0))) animator.Playing = !animator.Playing;
        ImGui.SameLine();
        if (ImGui.Button("Stop", new Vector2(50, 0))) { animator.Playing = false; animator.Time = 0; }
        ImGui.SameLine();
        ImGui.Checkbox("Loop", ref animator.Loop);
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        ImGui.SliderFloat("Speed", ref animator.Speed, 0, 3, "%.2fx");
        ImGui.SameLine();
        var frame = (int)MathF.Round(animator.Time * _clip!.FrameRate);
        ImGui.Text($"{animator.Time:F2} s / frame {frame} of {_clip.FrameCount}");
        ImGui.SetNextItemWidth(width);
        var time = animator.Time;
        if (ImGui.SliderFloat("##time", ref time, 0, _clip.Duration, "%.3f s")) { animator.Time = time; animator.Playing = false; }
    }

    // ---------- 線 (骨 / 法線) ----------

    private void addLines(PreviewScene preview)
    {
        var world = preview.World;
        BoneLineCount = 0;
        NormalLineCount = 0;
        if (ShowBones && _skeleton != null)
        {
            var axis = _boundsRadius * 0.08f;
            for (int i = 0; i < BoneEntities.Count; i++)
            {
                var m = Hierarchy.GetWorldMatrix(world, BoneEntities[i]);
                var p = m.Translation;
                var parent = _skeleton.Bones[i].Parent;
                var selected = i == SelectedBone;
                var color = selected ? DebugDraw.Color(1, 0.85f, 0.2f) : _skeleton.Bones[i].IsBone ? DebugDraw.Color(0.35f, 0.75f, 1) : DebugDraw.Color(0.6f, 0.6f, 0.6f);
                if (parent >= 0 && parent < BoneEntities.Count)
                {
                    preview.Line(Hierarchy.GetWorldMatrix(world, BoneEntities[parent]).Translation, p, color, overlay: true);
                    BoneLineCount++;
                }
                if (selected)
                {
                    // 選んだ骨 : 向き (X 赤 / Y 緑 / Z 青)
                    preview.Line(p, p + Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitX, m)) * axis, DebugDraw.Color(1, 0.2f, 0.2f), overlay: true);
                    preview.Line(p, p + Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitY, m)) * axis, DebugDraw.Color(0.2f, 1, 0.2f), overlay: true);
                    preview.Line(p, p + Vector3.Normalize(Vector3.TransformNormal(Vector3.UnitZ, m)) * axis, DebugDraw.Color(0.3f, 0.5f, 1), overlay: true);
                }
            }
        }
        if (ShowNormals) addNormals(preview);
    }

    private void addNormals(PreviewScene preview)
    {
        var world = preview.World;
        var length = _boundsRadius * 0.05f;
        var color = DebugDraw.Color(0.9f, 0.3f, 0.9f);
        var budget = MaxNormals;
        if (_skeleton == null)
        {
            // モデルだけ : 読んだ頂点 (モデル全体の空間 = 根の空間)
            if (_modelData == null || !world.IsAlive(Root)) return;
            var m = Hierarchy.GetWorldMatrix(world, Root);
            for (int v = 0; v < _modelData.Positions.Count && v < _modelData.Normals.Count && budget > 0; v++, budget--)
            {
                var p = Vector3.Transform(_modelData.Positions[v], m);
                preview.Line(p, p + Vector3.Normalize(Vector3.TransformNormal(_modelData.Normals[v], m)) * length, color);
                NormalLineCount++;
            }
            return;
        }
        // 骨で動く部品 : 部品ごとに読んで、固い部品は部品の行列、曲げる部品は骨の行列で (GPU と同じ式を CPU で)
        foreach (var part in Parts)
        {
            if (budget <= 0 || !world.IsAlive(part) || !world.Has<MeshRenderer>(part)) continue;
            var data = partData(part);
            if (data == null) continue;
            var partWorld = Hierarchy.GetWorldMatrix(world, part);
            Matrix4x4[]? bones = null;
            if (data.HasSkin)
            {
                bones = new Matrix4x4[data.SkinBones.Count];
                for (int b = 0; b < bones.Length; b++)
                {
                    var index = _skeleton.IndexOf(data.SkinBones[b]);
                    bones[b] = index >= 0 && index < BoneEntities.Count ? data.SkinInverseBind[b] * Hierarchy.GetWorldMatrix(world, BoneEntities[index]) : partWorld;
                }
            }
            for (int v = 0; v < data.Positions.Count && v < data.Normals.Count && budget > 0; v++, budget--)
            {
                Vector3 p, n;
                if (bones != null && v < data.SkinWeights.Count)
                {
                    var (b0, b1, b2, b3) = data.SkinIndices[v];
                    var w = data.SkinWeights[v];
                    p = n = Vector3.Zero;
                    var total = 0f;
                    foreach (var (b, weight) in new[] { (b0, w.X), (b1, w.Y), (b2, w.Z), (b3, w.W) })
                    {
                        if (weight <= 0) continue;
                        var m = b < bones.Length ? bones[b] : partWorld;   // 骨の数の番号 = 動かない骨 (部品の行列)
                        p += weight * Vector3.Transform(data.Positions[v], m);
                        n += weight * Vector3.TransformNormal(data.Normals[v], m);
                        total += weight;
                    }
                    if (total <= 0) { p = Vector3.Transform(data.Positions[v], partWorld); n = Vector3.TransformNormal(data.Normals[v], partWorld); }
                }
                else
                {
                    p = Vector3.Transform(data.Positions[v], partWorld);
                    n = Vector3.TransformNormal(data.Normals[v], partWorld);
                }
                if (n.LengthSquared() < 1e-12f) continue;
                preview.Line(p, p + Vector3.Normalize(n) * length, color);
                NormalLineCount++;
            }
        }
    }

    private FbxModelData? partData(Entity part)
    {
        if (_partData.TryGetValue(part, out var data)) return data;
        data = null;
        if (ModelPath != null && Preview != null)
        {
            var name = Preview.World.Has<EntityInfo>(part) ? Preview.World.Get<EntityInfo>(part).Name : "";
            var fbx = _engine.Assets.LoadFbx(ModelPath);
            if (fbx.IsValid && fbx.Scene != null)
            {
                try { data = FbxModelImporter.Import(fbx.Scene, _database.FullPath(ModelPath), onlyNode: name); }
                catch (Exception ex) when (ex is InvalidDataException or ArgumentException) { Console.WriteLine($"[Preview] {ex.Message}"); }
            }
        }
        _partData[part] = data;
        return data;
    }
}
