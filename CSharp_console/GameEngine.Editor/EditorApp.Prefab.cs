using System.Numerics;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace GameEngine.Editor;

// ==========================================================================
// プレハブの操作 (ED8)
//   作る (CreatePrefab)   : 選んだ物 (子ごと) を Assets/Prefabs/名前.prefab.json にし、選んだ物をその置いた物にする
//   置く (InstantiatePrefab) : Project からシーンビュー / Hierarchy へドラッグ (元に戻せる)
//   作り直す (SyncPrefab) : プレハブが変わった時 (Apply / ファイルの書き換え / シーンを開いた時) に、置いた物を全部プレハブに合わせる
//     - 自分で変えた値 (PrefabLink.Overrides) と、根の Position / Rotation / Parent / 名前はそのまま
//     - プレハブに増えた物は作る / 無くなった物は消す。置いた物に自分で足したコンポーネントは残す
//   Apply  : 置いた物の今の値をプレハブに書く (他の置いた物にも効く)。Revert : 自分で変えた値を捨ててプレハブに戻す
//   Unpack : プレハブとの繋がりを切る (PrefabLink を外す。元に戻せる)
//   自分で変えた値の印 : インスペクター / ギズモ / EditorApi.Modify / マテリアルのドロップで値を変えると、変えたフィールドを Overrides に足す
//   ※プレハブ (アセット) の変更 (作る / Apply) は元に戻せない
// ==========================================================================
internal sealed partial class EditorApp
{
    private const string PrefabFolder = "Assets/Prefabs";
    private static readonly string[] RootOwnFields = { "Position", "Rotation", "Parent" };

    // 置いた物の根 (プレハブの根から作った物) を探す (無ければ Entity.Null)
    public Entity PrefabRootOf(Entity entity)
    {
        var current = entity;
        for (int depth = 0; depth < 64 && World.IsAlive(current) && World.Has<PrefabLink>(current); depth++)
        {
            var link = World.Get<PrefabLink>(current);
            if (_engine.Assets.LoadPrefab(link.Prefab) is { } prefab && link.Source == prefab.RootId) return current;
            current = World.Has<Transform>(current) ? World.Get<Transform>(current).Parent : Entity.Null;
        }
        return Entity.Null;
    }

    // 置いた物の中身 : Source → エンティティ (同じプレハブの子孫だけ)
    private Dictionary<Guid, Entity> prefabMembers(Entity root, Guid prefabId)
    {
        var map = new Dictionary<Guid, Entity>();
        foreach (var e in EditorWorld.WithDescendants(World, root))
        {
            if (World.Has<PrefabLink>(e) && World.Get<PrefabLink>(e).Prefab == prefabId) map[World.Get<PrefabLink>(e).Source] = e;
        }
        return map;
    }

    // ---------------------------------------------------------------------
    // 作る
    // ---------------------------------------------------------------------
    public Prefab? CreatePrefab(Entity root)
    {
        if (!World.IsAlive(root)) return null;
        if (World.Has<PrefabLink>(root)) { Console.WriteLine("[Prefab] Already a prefab instance (Unpack it first)."); return null; }
        var entities = EditorWorld.WithDescendants(World, root);
        var json = stripForPrefab(SceneSerializer.Save(World, entities), keepRootTransformFrom: null);
        var name = NameOf(World, root);
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        Directory.CreateDirectory(_database.FullPath(PrefabFolder));
        var path = $"{PrefabFolder}/{name}.prefab.json";
        for (int i = 1; File.Exists(_database.FullPath(path)); i++) path = $"{PrefabFolder}/{name} {i}.prefab.json";
        var file = _database.FullPath(path);
        _engine.Assets.NoteOwnWrite(file);
        File.WriteAllText(file, json, new System.Text.UTF8Encoding(false));
        _database.Refresh();
        var prefab = _engine.Assets.LoadPrefab(path);

        // 選んだ物を置いた物にする (プレハブの中の Id = 今の Id。元に戻せる)
        var edit = new CompoundEdit("Create Prefab");
        foreach (var e in entities)
        {
            var id = EditorWorld.IdOf(World, e);
            World.Add(e, new PrefabLink { Prefab = prefab.AssetId, Source = id, Overrides = Array.Empty<string>() });
            edit.Commands.Add(new ComponentEdit(id, typeof(PrefabLink), null, SceneSerializer.SaveComponent(World, e, typeof(PrefabLink))));
        }
        _undo.Record(edit);
        Console.WriteLine($"[Prefab] Created {path} ({entities.Count} entities)");
        return prefab;
    }

    // プレハブのファイルにする JSON : PrefabLink を外し、根の Parent を消す。keepRootTransformFrom があれば根の位置と回転はそのプレハブのまま
    private static string stripForPrefab(string json, Prefab? keepRootTransformFrom)
    {
        var root = JsonNode.Parse(json)!;
        var entities = root["entities"]!.AsArray();
        JsonObject? oldRootTransform = null;
        if (keepRootTransformFrom != null)
        {
            var old = JsonNode.Parse(keepRootTransformFrom.Json)!["entities"]!.AsArray();
            if (old.Count > 0) oldRootTransform = findComponent(old[0]!.AsObject(), typeof(Transform).FullName!);
        }
        for (int i = 0; i < entities.Count; i++)
        {
            var components = entities[i]!["components"]!.AsArray();
            for (int c = components.Count - 1; c >= 0; c--)
            {
                if ((string?)components[c]!["type"] == typeof(PrefabLink).FullName) components.RemoveAt(c);
            }
            if (i != 0) continue;
            if (findComponent(entities[i]!.AsObject(), typeof(Transform).FullName!) is { } transform)
            {
                transform["Parent"] = null;
                if (oldRootTransform != null)
                {
                    transform["Position"] = oldRootTransform["Position"]?.DeepClone();
                    transform["Rotation"] = oldRootTransform["Rotation"]?.DeepClone();
                }
            }
        }
        return root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }

    private static JsonObject? findComponent(JsonObject entity, string typeName)
    {
        foreach (var component in entity["components"]!.AsArray())
            if ((string?)component!["type"] == typeName) return component.AsObject();
        return null;
    }

    // ---------------------------------------------------------------------
    // 置く
    // ---------------------------------------------------------------------
    public Entity InstantiatePrefab(AssetEntry asset, Vector3 position, Entity parent)
    {
        Prefab prefab;
        try { prefab = _engine.Assets.LoadPrefab(asset.Path); }
        catch (Exception ex) { Console.WriteLine($"[Prefab] {ex.Message}"); return Entity.Null; }
        var root = prefab.Instantiate(World, position, parent);
        if (root.IsNull) return root;
        var created = EditorWorld.WithDescendants(World, root);
        _undo.Record(new EntitiesEdit($"Place {prefab.RootName}", SceneSerializer.Save(World, created), created.Select(e => EditorWorld.IdOf(World, e)).ToList(), create: true));
        Selected = root;
        return root;
    }

    // ---------------------------------------------------------------------
    // 作り直す (プレハブに合わせる)
    // ---------------------------------------------------------------------

    // シーンの全部の置いた物を、それぞれのプレハブに合わせる (シーンを開いた時)
    public int SyncAllPrefabs()
    {
        var prefabs = World.GetPool<PrefabLink>().Components.ToArray().Select(l => l.Prefab).Distinct().ToList();
        var count = 0;
        foreach (var id in prefabs)
        {
            // ファイルから読み直してから合わせる (覚えている中身が古いことがある : ファイルの見張りが読み直す前にシーンを開いた時など)
            if (_engine.Assets.LoadPrefab(id) is { } prefab && _engine.Assets.ReloadPrefab(prefab.Path) is { } fresh) count += SyncPrefab(fresh);
            else Console.WriteLine($"[Prefab] Prefab not found : {id} (instances are kept as they are)");
        }
        return count;
    }

    // そのプレハブの置いた物を全部作り直す。戻り値 = 置いた物の数
    public int SyncPrefab(Prefab prefab)
    {
        using var document = JsonDocument.Parse(prefab.Json);
        var source = document.RootElement.GetProperty("entities").EnumerateArray().ToList();
        var roots = World.Entities.Where(e => World.Has<PrefabLink>(e) && World.Get<PrefabLink>(e).Prefab == prefab.AssetId && World.Get<PrefabLink>(e).Source == prefab.RootId).ToList();
        foreach (var root in roots) syncInstance(prefab, root, source);
        if (roots.Count > 0) _picker.Clear();
        return roots.Count;
    }

    private void syncInstance(Prefab prefab, Entity root, List<JsonElement> source)
    {
        var members = prefabMembers(root, prefab.AssetId);
        // 1. プレハブから無くなった物を消す
        var ids = new HashSet<Guid>(prefab.EntityIds);
        foreach (var (id, entity) in members.ToList())
        {
            if (ids.Contains(id) || entity == root) continue;
            if (World.IsAlive(entity)) Hierarchy.DestroyWithChildren(World, entity);
            members.Remove(id);
        }
        // 2. プレハブに増えた物を作る (親はコンポーネントの所で繋ぐ)
        for (int i = 0; i < source.Count && i < prefab.EntityIds.Count; i++)
        {
            var id = prefab.EntityIds[i];
            if (members.ContainsKey(id)) continue;
            var entity = World.CreateEntity(source[i].TryGetProperty("name", out var n) ? n.GetString() ?? "" : "");
            World.Add(entity, new PrefabLink { Prefab = prefab.AssetId, Source = id, Overrides = Array.Empty<string>() });
            members[id] = entity;
        }
        // プレハブの中の Id → この置いた物の Id (参照 (親など) を繋ぎ直すため)
        var replace = members.ToDictionary(m => m.Key.ToString(), m => EditorWorld.IdOf(World, m.Value).ToString());

        // 3. 値を合わせる (自分で変えた値と、根の位置 / 回転 / 親 / 名前はそのまま)
        for (int i = 0; i < source.Count && i < prefab.EntityIds.Count; i++)
        {
            var entity = members[prefab.EntityIds[i]];
            var isRoot = entity == root;
            var link = World.Get<PrefabLink>(entity);
            var element = source[i];
            if (!isRoot && !link.IsOverridden("GameEngine.EntityInfo.Name") && element.TryGetProperty("name", out var name))
                World.Get<EntityInfo>(entity).Name = name.GetString() ?? "";
            if (!link.IsOverridden("GameEngine.EntityInfo.Active") && element.TryGetProperty("active", out var active) && World.IsActive(entity) != active.GetBoolean())
                World.SetActive(entity, active.GetBoolean());
            if (!element.TryGetProperty("components", out var components)) continue;
            foreach (var component in components.EnumerateArray())
            {
                var typeName = component.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
                if (ComponentRegistry.Find(typeName) is not { } type || type.Type == typeof(PrefabLink)) continue;
                var fields = new JsonObject();
                foreach (var property in component.EnumerateObject())
                {
                    if (property.Name == "type" || link.IsOverridden($"{typeName}.{property.Name}")) continue;
                    if (isRoot && type.Type == typeof(Transform) && RootOwnFields.Contains(property.Name)) continue;
                    var text = property.Value.GetRawText();
                    foreach (var (from, to) in replace) text = text.Replace(from, to);
                    fields[property.Name] = JsonNode.Parse(text);
                }
                var pool = World.GetPool(type.Type);
                var boxed = SceneSerializer.LoadComponent(World, type.Type, fields.ToJsonString(), pool.Has(entity.Id) ? pool.GetBoxed(entity.Id) : null, _engine.Assets);
                EditorWorld.WriteComponent(World, entity, type.Type, boxed);
            }
        }
    }

    // ---------------------------------------------------------------------
    // Apply / Revert / Unpack
    // ---------------------------------------------------------------------
    public bool ApplyPrefab(Entity entity)
    {
        var root = PrefabRootOf(entity);
        if (root.IsNull) return false;
        var prefab = _engine.Assets.LoadPrefab(World.Get<PrefabLink>(root).Prefab)!;
        var entities = EditorWorld.WithDescendants(World, root);
        // 置いた物の Id → プレハブの中の Id (自分で足した子は、今の Id のまま新しいプレハブの物になる)
        var json = SceneSerializer.Save(World, entities);
        foreach (var e in entities)
        {
            if (!World.Has<PrefabLink>(e) || World.Get<PrefabLink>(e).Prefab != prefab.AssetId) continue;
            json = json.Replace(EditorWorld.IdOf(World, e).ToString(), World.Get<PrefabLink>(e).Source.ToString());
        }
        prefab.SetJson(stripForPrefab(json, keepRootTransformFrom: prefab));
        var file = _database.FullPath(prefab.Path);
        _engine.Assets.NoteOwnWrite(file);
        prefab.Save(file);
        // この置いた物 : 自分で変えた値は無くなる / 足した子は繋ぐ
        foreach (var e in entities)
        {
            var id = EditorWorld.IdOf(World, e);
            var link = World.Has<PrefabLink>(e) && World.Get<PrefabLink>(e).Prefab == prefab.AssetId
                ? World.Get<PrefabLink>(e) : new PrefabLink { Prefab = prefab.AssetId, Source = id };
            link.Overrides = Array.Empty<string>();
            World.Set(e, link);
        }
        var others = SyncPrefab(prefab);
        Console.WriteLine($"[Prefab] Applied to {prefab.Path} ({others} instances)");
        return true;
    }

    public bool RevertPrefab(Entity entity)
    {
        var root = PrefabRootOf(entity);
        if (root.IsNull) return false;
        var prefab = _engine.Assets.LoadPrefab(World.Get<PrefabLink>(root).Prefab)!;
        foreach (var e in prefabMembers(root, prefab.AssetId).Values)
        {
            ref var link = ref World.Get<PrefabLink>(e);
            link.Overrides = Array.Empty<string>();
        }
        using var document = JsonDocument.Parse(prefab.Json);
        syncInstance(prefab, root, document.RootElement.GetProperty("entities").EnumerateArray().ToList());
        _undo.Clear();   // 元に戻すの記録と食い違うので消す
        return true;
    }

    public bool UnpackPrefab(Entity entity)
    {
        var root = PrefabRootOf(entity);
        if (root.IsNull) return false;
        var prefabId = World.Get<PrefabLink>(root).Prefab;
        var edit = new CompoundEdit("Unpack Prefab");
        foreach (var e in prefabMembers(root, prefabId).Values)
        {
            var id = EditorWorld.IdOf(World, e);
            edit.Commands.Add(new ComponentEdit(id, typeof(PrefabLink), SceneSerializer.SaveComponent(World, e, typeof(PrefabLink)), null));
            World.Remove<PrefabLink>(e);
        }
        _undo.Record(edit);
        return true;
    }

    // ---------------------------------------------------------------------
    // 自分で変えた値の印 (値を書いた所が呼ぶ。before / after は SaveComponent の JSON)
    // ---------------------------------------------------------------------
    public void MarkOverrides(Entity entity, Type type, string? before, string after)
    {
        if (type == typeof(PrefabLink) || !World.IsAlive(entity) || !World.Has<PrefabLink>(entity)) return;
        using var a = JsonDocument.Parse(after);
        Dictionary<string, string>? old = null;
        if (before != null)
        {
            using var b = JsonDocument.Parse(before);
            old = b.RootElement.EnumerateObject().ToDictionary(p => p.Name, p => p.Value.GetRawText());
        }
        ref var link = ref World.Get<PrefabLink>(entity);
        var overrides = new List<string>(link.Overrides ?? Array.Empty<string>());
        foreach (var property in a.RootElement.EnumerateObject())
        {
            if (old != null && old.TryGetValue(property.Name, out var was) && was == property.Value.GetRawText()) continue;
            var key = $"{type.FullName}.{property.Name}";
            if (!overrides.Contains(key)) overrides.Add(key);
        }
        link.Overrides = overrides.ToArray();
    }
}
