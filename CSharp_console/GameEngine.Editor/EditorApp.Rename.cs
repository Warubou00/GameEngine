using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// 名前を変える (ED10)
//   Hierarchy : 物を選んで F2 / 右クリック > Rename → その場で名前を書く (Enter か外をクリックで決める、Esc でやめる)。元に戻せる
//   Project   : ProjectPanel が同じように (アセットのファイルの名前。.meta も一緒に動かすので Id は変わらない)
//   アセットの名前が変わったら、開いているシーンのパスと書き出しのシーンの並びも付け替える
// ==========================================================================
internal sealed partial class EditorApp
{
    private Entity _renaming;          // Hierarchy で名前を書いている物 (無ければ Null)
    private string _renameText = "";
    private bool   _renameFocus;       // 次のフレームで入力の欄に文字を入れられるようにする

    /// <summary>物の名前を変える (元に戻せる)。プレハブの置いた物なら「変えた値」の印も付く</summary>
    public void RenameEntity(Entity entity, string name)
    {
        if (!World.IsAlive(entity)) return;
        name = name.Trim();
        if (name.Length == 0) return;   // 空の名前にはしない (前のまま)
        if (!World.Has<EntityInfo>(entity)) World.Add(entity, new EntityInfo(NameOf(World, entity)));
        if (World.Get<EntityInfo>(entity).Name == name) return;
        var before = SceneSerializer.SaveComponent(World, entity, typeof(EntityInfo));
        World.Get<EntityInfo>(entity).Name = name;
        var after = SceneSerializer.SaveComponent(World, entity, typeof(EntityInfo));
        _undo.Record(new ComponentEdit(EditorWorld.IdOf(World, entity), typeof(EntityInfo), before, after));
        MarkOverrides(entity, typeof(EntityInfo), before, after);
    }

    /// <summary>Hierarchy で名前を書き始める (F2 / 右クリック > Rename)</summary>
    public void BeginRename(Entity entity)
    {
        if (!World.IsAlive(entity)) return;
        Selected     = entity;
        _renaming    = entity;
        _renameText  = NameOf(World, entity);
        _renameFocus = true;
    }

    public bool IsRenaming => !_renaming.IsNull;

    // Hierarchy の 1 行の、名前を書く欄 (drawNode が呼ぶ。決めたら / やめたら _renaming を消す)
    private void drawRenameField(Entity entity)
    {
        ImGui.SetNextItemWidth(Math.Max(80.0f, ImGui.GetContentRegionAvail().X));
        if (_renameFocus) { ImGui.SetKeyboardFocusHere(); _renameFocus = false; }
        ImGui.InputText($"##rename{entity.Id}", ref _renameText, 256, ImGuiInputTextFlags.AutoSelectAll);
        if (ImGui.IsItemDeactivated())
        {
            if (!ImGui.IsKeyPressed(ImGuiKey.Escape)) RenameEntity(entity, _renameText);   // Enter / 外をクリック = 決める
            _renaming = Entity.Null;
        }
    }

    // アセットの名前が変わった時 (AssetDatabase.Renamed)
    private void onAssetRenamed(string oldPath, string newPath)
    {
        string? map(string path) =>
            path.Equals(oldPath, StringComparison.OrdinalIgnoreCase) ? newPath :
            path.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase) ? newPath + path[oldPath.Length..] : null;
        // 開いているシーン (またはそれを含むフォルダ) の名前が変わった : 保存先を新しいパスに
        if (_scenePath != null && map(Path.GetRelativePath(_database.ProjectRoot, _scenePath).Replace('\\', '/')) is { } moved)
            _scenePath = _database.FullPath(moved);
        _build.OnAssetRenamed(map);
        Console.WriteLine($"[Assets] Renamed {oldPath} -> {newPath}");
    }
}
