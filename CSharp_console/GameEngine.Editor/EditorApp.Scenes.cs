using System.Numerics;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// シーンの管理 (v1.14 S3) : エディターでのサブシーン / 足したシーン / 永続シーン
//   - Hierarchy : SubScene を持つ物に [Sub Scene 名前 · 状態]、中の物は紫がかった色で持ち主の下に並ぶ (Transform の無い物も)
//                 メインでない一番上のシーン (足したシーン / DontDestroyOnLoad / 読み込み中) は見出しの下にまとめる
//                 右クリック > Sub Scene : Load / Unload / Activate / Standby / Open
//   - 編集中は、サブシーンの中の物は見るだけ (メインシーンに保存されないので)。消す / 複製 / 親を変える / 名前 / 動かす / Inspector を止める
//     → 「Open Its Scene」でそのシーンを開いて直す (RE Engine のように開いたまま直すのは、まだしない)
//     再生中は触れる (止めると元に戻るのは他の物と同じ)
//   - Project の .scene.json を Hierarchy へドラッグ : その場所に SubScene を持つ物を作って読む (元に戻せる)
//   - SubScene の Scene を変えたら読み直す。元に戻す / やり直す / 複製の後は LoadOnStart の物を読む
// ==========================================================================
internal sealed partial class EditorApp
{
    private SceneManager Scenes => _engine.Scenes;
    private static readonly Vector4 SubSceneMemberColor = new(0.78f, 0.68f, 1.0f, 1.0f);

    /// <summary>今は編集できないか (編集中の、メインシーンでないシーンの物)</summary>
    public bool IsReadOnly(Entity entity) => !_playing && World.IsAlive(entity) && SceneSerializer.IsOutsideMainScene(World, entity);

    // Inspector の上に出す理由 (編集できる物は null)
    private string? readOnlyReason(Entity entity)
    {
        if (!IsReadOnly(entity)) return null;
        var scene = Scenes.GetScene(entity);
        return scene is { IsSubScene: true }
            ? $"Part of the sub scene '{scene.Name}' (held by '{NameOf(World, scene.Owner)}'). It is not saved with this scene, so it is read-only here. Open the scene to edit it."
            : $"Part of the scene '{scene?.Name}', not the open scene. Read-only here.";
    }

    // 編集できない物を触ろうとした (Console に 1 回だけ出す)
    private bool refuseReadOnly(Entity entity, string action)
    {
        if (!IsReadOnly(entity)) return false;
        reportOnce($"read-only:{action}", new InvalidOperationException(
            $"Cannot {action} '{NameOf(World, entity)}' : it belongs to the sub scene '{Scenes.GetScene(entity)?.Name}'. Open that scene to edit it."));
        return true;
    }

    /// <summary>物のシーンのファイルを開く (サブシーンの中の物 / 持ち主。保存していない変更があれば聞く)</summary>
    public void OpenOwningScene(Entity entity)
    {
        var scene = World.Has<SubScene>(entity) && !IsReadOnly(entity) ? Scenes.GetSubScene(entity) : Scenes.GetScene(entity);
        string? path = scene?.Path;
        if (scene == null && World.Has<SubScene>(entity)) path = World.Get<SubScene>(entity).Scene;
        if (string.IsNullOrEmpty(path)) return;
        var file = _database.ToAssetPath(path) is { } assetPath ? _database.FullPath(assetPath) : path;
        if (File.Exists(file)) RequestOpenScene(file);
    }

    /// <summary>
    /// シーンのアセットを、SubScene を持つ物として置く (Hierarchy へドラッグ)。parent があればその子に。読み込みまで行う。元に戻せる
    /// 開いているシーン自身は置けない (自分を読む輪になる)
    /// </summary>
    public Entity CreateSubScene(AssetEntry scene, Entity parent)
    {
        if (_scenePath != null && _database.ToAssetPath(_scenePath) is { } open && open.Equals(scene.Path, StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine($"[Editor] Cannot put {scene.Name} inside itself.");
            return Entity.Null;
        }
        if (World.IsAlive(parent) && refuseReadOnly(parent, "add a child to")) return Entity.Null;
        var entity = World.CreateEntity(SceneManager.NameOf(scene.Path));
        World.Add(entity, new Transform { Parent = World.IsAlive(parent) && World.Has<Transform>(parent) ? parent : Entity.Null });
        World.Add(entity, new SubScene { Scene = scene.Path });
        Selected = entity;
        _undo.Record(new EntitiesEdit($"Create Sub Scene {scene.Name}", SceneSerializer.Save(World, new[] { entity }), new List<Guid> { EditorWorld.IdOf(World, entity) }, create: true));
        if (Scenes.LoadSubScene(entity) == null) Console.WriteLine($"[Editor] Sub scene {scene.Path} was not loaded (see the messages above).");
        return entity;
    }

    // 元に戻す / やり直す / 複製 / プレハブの後 : 戻った SubScene の Scene が読んでいる物と違えば読み直し、新しく戻った持ち主の LoadOnStart のサブシーンを読む
    //   (S3 の最初は読んでいない物だけを読んだので、Scene を変えたのを元に戻しても、前のシーンが出てこなかった)
    private void syncSubScenes()
    {
        var subs = World.GetPool<SubScene>();
        if (subs.Count == 0) return;
        foreach (var owner in subs.Entities.ToArray()) if (Scenes.GetSubScene(owner) != null) reloadSubSceneIfChanged(owner);
        Scenes.LoadStartSubScenes(_scenePath);
    }

    // Inspector で SubScene を変えた (Written) : シーンが変わったら読み直す
    private void onComponentWritten(Entity entity, Type type, string? before, string after)
    {
        MarkOverrides(entity, type, before, after);
        if (type == typeof(SubScene)) reloadSubSceneIfChanged(entity);
    }

    // SubScene の Scene が読んでいる物と違えば外して読み直す (Inspector / ModifyComponent / 名前の付け替えの後)
    private void reloadSubSceneIfChanged(Entity entity)
    {
        if (!World.IsAlive(entity) || !World.Has<SubScene>(entity)) return;
        var sub = World.Get<SubScene>(entity);
        var loaded = Scenes.GetSubScene(entity);
        var wanted = string.IsNullOrWhiteSpace(sub.Scene) ? null : _database.ToAssetPath(sub.Scene) ?? sub.Scene;
        if (loaded != null && !string.Equals(loaded.Path, wanted, StringComparison.OrdinalIgnoreCase))
        {
            Scenes.UnloadSubScene(entity);
            loaded = null;
        }
        if (loaded == null && wanted != null && sub.LoadOnStart) Scenes.LoadSubScene(entity);
    }

    // アセットの名前が変わった : 開いているシーンの SubScene のパスも付け替える (元に戻せる。他のシーンのファイルは開いた時に AssetDatabase が読み替える)
    private void remapSubScenePaths(Func<string, string?> map)
    {
        var subs = World.GetPool<SubScene>();
        foreach (var owner in subs.Entities.ToArray())
        {
            if (IsReadOnly(owner)) continue;
            var path = World.Get<SubScene>(owner).Scene;
            if (string.IsNullOrEmpty(path) || map(path) is not { } moved) continue;
            ModifyComponent<SubScene>(owner, s => s with { Scene = moved });
        }
    }

    // ---------------------------------------------------------------------
    // Inspector : SubScene の状態とボタン
    // ---------------------------------------------------------------------

    private void drawSubSceneInfo(Entity owner)
    {
        var scene = Scenes.GetSubScene(owner);
        var op = scene == null ? null : Scenes.Operations.FirstOrDefault(o => o.Scene == scene);
        var state = scene == null ? "Unloaded" : op != null ? $"Loading {op.Progress:P0}" : scene.IsActive ? "Active" : scene.IsActiveSelf ? "Active (owner inactive)" : "Standby";
        ImGui.TextDisabled($"State : {state}{(scene is { State: SceneStatus.Loaded } ? $"   {Scenes.GetEntities(scene).Count} entities{(scene.Children.Count > 0 ? $", {scene.Children.Count} sub scenes" : "")}" : "")}");
        if (scene == null)
        {
            if (ImGui.Button("Load")) Scenes.LoadSubScene(owner);
        }
        else if (op == null)
        {
            if (ImGui.Button("Unload")) Scenes.UnloadSubScene(owner);
            ImGui.SameLine();
            if (ImGui.Button(scene.IsActiveSelf ? "Standby" : "Activate")) Scenes.SetActive(scene, !scene.IsActiveSelf);
        }
        ImGui.SameLine();
        if (ImGui.Button("Open")) OpenOwningScene(owner);
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Open this sub scene's file to edit it");
    }

    // ---------------------------------------------------------------------
    // Hierarchy
    // ---------------------------------------------------------------------

    private readonly Dictionary<Scene, List<Entity>> _sceneRoots = new();

    // 親の無い物の行き先 : サブシーンの物 (Transform の無い物) は持ち主の下、メインでない一番上のシーンの物はその見出しの下
    // 戻り値 = 親の代わり (無ければ Null)。otherScene = 見出しの下に入れるシーン
    private Entity hierarchyParentOf(Entity entity, out Scene? otherScene)
    {
        otherScene = null;
        if (!_multiScene) return Entity.Null;   // 速い道 (いつもの 1 つのシーン。物ごとにシーンを引かない)
        var scene = Scenes.GetScene(entity);
        if (scene == null || scene == Scenes.ActiveScene) return Entity.Null;
        if (scene.IsSubScene && World.IsAlive(scene.Owner)) return scene.Owner;
        var top = scene;
        while (top.Parent != null) top = top.Parent;
        otherScene = top;
        return Entity.Null;
    }

    private bool _multiScene;   // このフレームの Hierarchy で、シーンごとに分ける必要があるか

    private void beginHierarchyScenes()
    {
        foreach (var list in _sceneRoots.Values) list.Clear();
        _multiScene = World.GetPool<SubScene>().Count > 0 || Scenes.Scenes.Count > 1 || Scenes.Operations.Count > 0 || (_playing && hasPersistentMembers());
    }

    // 永続シーンの物があるか (再生中だけ見る。EntityInfo の配列を 1 回見る)
    private bool hasPersistentMembers()
    {
        foreach (var e in World.GetPool<EntityInfo>().Entities) if (Scenes.GetScene(e) == Scenes.Persistent) return true;
        return false;
    }

    private void addSceneRoot(Scene scene, Entity entity)
    {
        if (!_sceneRoots.TryGetValue(scene, out var list)) _sceneRoots[scene] = list = new List<Entity>();
        list.Add(entity);
    }

    // メインでない一番上のシーンを見出しの下に描く (足したシーン → 永続シーンの順)
    private void drawOtherSceneRoots()
    {
        foreach (var (scene, roots) in _sceneRoots.OrderBy(p => p.Key.IsPersistent ? 1 : 0).ThenBy(p => p.Key.Id))
        {
            if (roots.Count == 0) continue;
            var op = Scenes.Operations.FirstOrDefault(o => o.Scene == scene);
            var kind = scene.IsPersistent ? "" : op != null ? $"  (loading {op.Progress:P0})" : scene.IsActive ? "  (additive)" : "  (standby)";
            ImGui.PushStyleColor(ImGuiCol.Text, SubSceneMemberColor);
            var open = ImGui.TreeNodeEx($"{scene.Name}{kind}##scene{scene.Id}", ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.SpanAvailWidth | ImGuiTreeNodeFlags.Framed);
            ImGui.PopStyleColor();
            if (!open) continue;
            foreach (var root in roots) drawNode(root);
            ImGui.TreePop();
        }
    }

    // 1 行の後ろ : 持ち主なら [Sub Scene 名前 · 状態]
    private void drawSubSceneSuffix(Entity entity)
    {
        if (!World.Has<SubScene>(entity)) return;
        var scene = Scenes.GetSubScene(entity);
        var name = scene?.Name ?? SceneManager.NameOf(World.Get<SubScene>(entity).Scene ?? "");
        var op = scene == null ? null : Scenes.Operations.FirstOrDefault(o => o.Scene == scene);
        var state = scene == null ? "unloaded" : op != null ? $"loading {op.Progress:P0}" : scene.IsActive ? "active" : "standby";
        ImGui.SameLine();
        ImGui.TextColored(new Vector4(SubSceneMemberColor.X, SubSceneMemberColor.Y, SubSceneMemberColor.Z, 0.75f), $"[Sub Scene {name} · {state}]");
    }

    // 右クリック : 持ち主の Sub Scene のメニュー
    private void drawSubSceneMenu(Entity owner)
    {
        if (!World.Has<SubScene>(owner) || !ImGui.BeginMenu("Sub Scene")) return;
        var scene = Scenes.GetSubScene(owner);
        if (ImGui.MenuItem("Load", null, false, scene == null)) Scenes.LoadSubScene(owner);
        if (ImGui.MenuItem("Unload", null, false, scene is { State: SceneStatus.Loaded })) Scenes.UnloadSubScene(owner);
        if (ImGui.MenuItem("Activate", null, false, scene is { State: SceneStatus.Loaded, IsActiveSelf: false })) Scenes.SetActive(scene!, true);
        if (ImGui.MenuItem("Standby", null, false, scene is { State: SceneStatus.Loaded, IsActiveSelf: true })) Scenes.SetActive(scene!, false);
        ImGui.Separator();
        if (ImGui.MenuItem("Open")) OpenOwningScene(owner);
        ImGui.EndMenu();
    }
}
