namespace GameEngine;

// ==========================================================================
// 名前 / タグ / Id / コンポーネントの型で物を探す (v1.18。Unity の GameObject.Find / FindWithTag / FindObjectOfType)
//
//   var player = world.Find("Player");                 // 名前 (有効な物だけ。無ければ Entity.Null)
//   var hand   = world.Find("Body/Arm/Hand");          // 親子のパス (先頭が / なら一番上の物から)
//   var enemy  = world.FindWithTag("Enemy");           // タグ
//   world.FindAllWithTag("Enemy", list);               // タグの全部 (list に足す。毎フレームでも配列を作らない)
//   var cam    = world.FindObjectOfType<Camera>();     // その型のコンポーネントを持つ最初の物
//
//   - 名前 / タグ / Id は索引 (辞書) で引くので、物が何万あっても速い (O(1))。型で探すのは型ごとの入れ物を見るだけ
//     索引は最初に探した時に作り、その後は物を作る / 消す / EntityInfo を置き換える度に少しずつ直す
//     (一度も探さないゲームには手間がかからない)
//   - 名前 / タグを変えるのは GameObject.Name / Tag か World.SetName / SetTag で (索引も直す)
//     ref EntityInfo で直接書き換えた時 : 古い名前で見つかった物は確かめて捨てる (間違った物は返さない) が、
//     新しい名前ではその物が見つからないことがある。その時は World.Reindex(entity) を呼ぶ
//   - 同じ名前が何個もある時は、索引に入った順 (シーンの中の順) の最初の物
//   - 既定では無効な物 (自分か親が無効 / 待機中のシーンの物) を返さない (Unity と同じ)。includeInactive = true で返す
// ==========================================================================
public sealed partial class World
{
    // 索引 (作るまでは null)
    private Dictionary<string, List<Entity>>? _byName;
    private Dictionary<string, List<Entity>>? _byTag;
    private Dictionary<Guid, Entity>?         _byGuid;
    // 番号ごとに、索引に入れた時の名前 / タグ / Id (外す時に、どこから外すか分かるように)
    private string?[] _indexedNames = Array.Empty<string?>();
    private string?[] _indexedTags  = Array.Empty<string?>();
    private Guid[]    _indexedGuids = Array.Empty<Guid>();
    private bool[]    _indexed      = Array.Empty<bool>();
    private readonly List<Entity> _staleScratch = new();

    // ---------------------------------------------------------------------
    // 探す
    // ---------------------------------------------------------------------

    /// <summary>名前 (または "親/子" のパス) で探す。無ければ Entity.Null</summary>
    public Entity Find(string nameOrPath, bool includeInactive = false)
    {
        if (string.IsNullOrEmpty(nameOrPath)) return Entity.Null;
        if (nameOrPath.Contains('/')) return findPath(nameOrPath, includeInactive);
        ensureIndex();
        return first(_byName!, nameOrPath, includeInactive, byTag: false);
    }

    /// <summary>名前が同じ物を全部 results に足す (足した数を返す)</summary>
    public int FindAll(string name, List<Entity> results, bool includeInactive = false)
    {
        ensureIndex();
        return all(_byName!, name, results, includeInactive, byTag: false);
    }

    /// <summary>タグで探す (最初の物)。無ければ Entity.Null</summary>
    public Entity FindWithTag(string tag, bool includeInactive = false)
    {
        if (string.IsNullOrEmpty(tag)) return Entity.Null;
        ensureIndex();
        return first(_byTag!, tag, includeInactive, byTag: true);
    }

    /// <summary>タグの物を全部 results に足す (足した数を返す)</summary>
    public int FindAllWithTag(string tag, List<Entity> results, bool includeInactive = false)
    {
        if (string.IsNullOrEmpty(tag)) return 0;
        ensureIndex();
        return all(_byTag!, tag, results, includeInactive, byTag: true);
    }

    /// <summary>タグの物を全部 (新しいリスト)</summary>
    public List<Entity> FindAllWithTag(string tag, bool includeInactive = false)
    {
        var results = new List<Entity>();
        FindAllWithTag(tag, results, includeInactive);
        return results;
    }

    /// <summary>保存しても変わらない Id (EntityInfo.Id) で探す (有効 / 無効を問わない)。無ければ Entity.Null</summary>
    public Entity FindById(Guid id)
    {
        if (id == Guid.Empty) return Entity.Null;
        ensureIndex();
        if (!_byGuid!.TryGetValue(id, out var entity)) return Entity.Null;
        var infos = GetPool<EntityInfo>();
        if (IsAlive(entity) && infos.Has(entity.Id) && infos.Get(entity.Id).Id == id) return entity;
        Reindex(entity);   // ref で Id を書き換えられていた (古い)
        return Entity.Null;
    }

    /// <summary>T を持つ最初の物 (Unity の FindObjectOfType)。無ければ Entity.Null</summary>
    public Entity FindObjectOfType<T>(bool includeInactive = false) where T : struct
    {
        var entities = GetPool<T>().Entities;
        for (int i = 0; i < entities.Length; i++)
        {
            if (includeInactive || !_inactive[entities[i].Id]) return entities[i];
        }
        return Entity.Null;
    }

    /// <summary>T を持つ物を全部 results に足す (Unity の FindObjectsOfType。足した数を返す)</summary>
    public int FindObjectsOfType<T>(List<Entity> results, bool includeInactive = false) where T : struct
    {
        var entities = GetPool<T>().Entities;
        var added = 0;
        for (int i = 0; i < entities.Length; i++)
        {
            if (!includeInactive && _inactive[entities[i].Id]) continue;
            results.Add(entities[i]);
            added++;
        }
        return added;
    }

    // ---------------------------------------------------------------------
    // 名前 / タグを変える (索引も直す)
    // ---------------------------------------------------------------------

    /// <summary>名前 (EntityInfo.Name)。EntityInfo が無ければ空</summary>
    public string GetName(Entity entity) => Has<EntityInfo>(entity) ? GetPool<EntityInfo>().Get(entity.Id).Name ?? "" : "";

    /// <summary>名前を変える (EntityInfo が無ければ付ける)</summary>
    public void SetName(Entity entity, string name)
    {
        ThrowIfDead(entity);
        var infos = GetPool<EntityInfo>();
        if (!infos.Has(entity.Id)) { infos.Add(entity, new EntityInfo(name)); return; }   // Added で索引に入る
        infos.Get(entity.Id).Name = name;
        Reindex(entity);
    }

    /// <summary>タグ (null = Untagged)</summary>
    public string? GetTag(Entity entity) => Has<EntityInfo>(entity) ? GetPool<EntityInfo>().Get(entity.Id).Tag : null;

    /// <summary>タグを変える (null / 空 / "Untagged" = タグ無し)</summary>
    public void SetTag(Entity entity, string? tag)
    {
        ThrowIfDead(entity);
        if (string.IsNullOrEmpty(tag) || tag == EntityTags.Untagged) tag = null;
        var infos = GetPool<EntityInfo>();
        if (!infos.Has(entity.Id)) infos.Add(entity, new EntityInfo($"Entity {entity.Id}") { Tag = tag });
        else
        {
            infos.Get(entity.Id).Tag = tag;
            Reindex(entity);
        }
    }

    /// <summary>タグが同じか (Unity の CompareTag。"Untagged" はタグ無しと同じ)</summary>
    public bool CompareTag(Entity entity, string tag)
    {
        var current = GetTag(entity);
        return string.IsNullOrEmpty(current) ? string.IsNullOrEmpty(tag) || tag == EntityTags.Untagged : current == tag;
    }

    /// <summary>この物の索引を今の EntityInfo に合わせる (ref で名前 / タグ / Id を直接書き換えた後に)</summary>
    public void Reindex(Entity entity)
    {
        if (_byName == null) return;   // 索引をまだ作っていない (作る時に全部を見る)
        unindex(entity.Id);
        if (IsAlive(entity) && GetPool<EntityInfo>().Has(entity.Id)) index(entity);
    }

    // ---------------------------------------------------------------------
    // 索引
    // ---------------------------------------------------------------------

    private void ensureIndex()
    {
        if (_byName != null) return;
        _byName = new Dictionary<string, List<Entity>>(StringComparer.Ordinal);
        _byTag  = new Dictionary<string, List<Entity>>(StringComparer.Ordinal);
        _byGuid = new Dictionary<Guid, Entity>();
        var infos = GetPool<EntityInfo>();
        // 番号の順に入れる (シーンを読んだ物はファイルの順に番号が付くので、同じ名前ならファイルで前の物が先)
        for (int id = 0; id < _nextId; id++)
        {
            if (_alive[id] && infos.Has(id)) index(new Entity(id, _versions[id]));
        }
        // これからの変化を受け取る (EntityInfo の入れ物は捨てられない)
        infos.Added    += index;
        infos.Replaced += Reindex;
        infos.Removed  += (entity, _) => unindex(entity.Id);
    }

    private void index(Entity entity)
    {
        if (_byName == null) return;
        var id = entity.Id;
        if (id >= _indexed.Length)
        {
            var size = Math.Max(id + 1, Math.Max(64, _indexed.Length * 2));
            Array.Resize(ref _indexed, size);
            Array.Resize(ref _indexedNames, size);
            Array.Resize(ref _indexedTags, size);
            Array.Resize(ref _indexedGuids, size);
        }
        else if (_indexed[id]) unindex(id);   // 置き換え
        ref var info = ref GetPool<EntityInfo>().Get(id);
        var name = info.Name ?? "";
        add(_byName, name, entity);
        if (!string.IsNullOrEmpty(info.Tag)) add(_byTag!, info.Tag, entity);
        if (info.Id != Guid.Empty) _byGuid![info.Id] = entity;
        _indexed[id] = true;
        _indexedNames[id] = name;
        _indexedTags[id] = string.IsNullOrEmpty(info.Tag) ? null : info.Tag;
        _indexedGuids[id] = info.Id;

        static void add(Dictionary<string, List<Entity>> map, string key, Entity entity)
        {
            if (!map.TryGetValue(key, out var list)) map[key] = list = new List<Entity>(1);
            list.Add(entity);
        }
    }

    private void unindex(int id)
    {
        if (_byName == null || id >= _indexed.Length || !_indexed[id]) return;
        remove(_byName, _indexedNames[id]!, id);
        if (_indexedTags[id] is { } tag) remove(_byTag!, tag, id);
        var guid = _indexedGuids[id];
        if (guid != Guid.Empty && _byGuid!.TryGetValue(guid, out var e) && e.Id == id) _byGuid.Remove(guid);
        _indexed[id] = false;
        _indexedNames[id] = null;
        _indexedTags[id] = null;
        _indexedGuids[id] = Guid.Empty;

        static void remove(Dictionary<string, List<Entity>> map, string key, int id)
        {
            if (!map.TryGetValue(key, out var list)) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (list[i].Id != id) continue;
                list.RemoveAt(i);   // 順番を保つ (同じ名前の「最初の物」が変わらないように)
                break;
            }
            if (list.Count == 0) map.Remove(key);
        }
    }

    // 見つかった物が今も同じ名前 / タグか確かめる (ref で書き換えられていたら索引を直して飛ばす)
    private bool matches(Entity entity, string key, bool byTag)
    {
        var infos = GetPool<EntityInfo>();
        if (!IsAlive(entity) || !infos.Has(entity.Id)) return false;
        ref var info = ref infos.Get(entity.Id);
        return byTag ? info.Tag == key : (info.Name ?? "") == key;
    }

    private Entity first(Dictionary<string, List<Entity>> map, string key, bool includeInactive, bool byTag)
    {
        if (!map.TryGetValue(key, out var list)) return Entity.Null;
        var result = Entity.Null;
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (!matches(e, key, byTag)) { _staleScratch.Add(e); continue; }
            if (!includeInactive && _inactive[e.Id]) continue;
            result = e;
            break;
        }
        fixStale();
        return result;
    }

    private int all(Dictionary<string, List<Entity>> map, string key, List<Entity> results, bool includeInactive, bool byTag)
    {
        if (!map.TryGetValue(key, out var list)) return 0;
        var added = 0;
        for (int i = 0; i < list.Count; i++)
        {
            var e = list[i];
            if (!matches(e, key, byTag)) { _staleScratch.Add(e); continue; }
            if (!includeInactive && _inactive[e.Id]) continue;
            results.Add(e);
            added++;
        }
        fixStale();
        return added;
    }

    private void fixStale()
    {
        if (_staleScratch.Count == 0) return;
        foreach (var e in _staleScratch) Reindex(e);   // 回し終わってから直す (回しているリストが変わらないように)
        _staleScratch.Clear();
    }

    // "A/B/C" : 名前が C の物のうち、親が B、その親が A の物 (先頭が / なら A は一番上の物)
    private Entity findPath(string path, bool includeInactive)
    {
        var rooted = path.StartsWith('/');
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0) return Entity.Null;
        ensureIndex();
        if (!_byName!.TryGetValue(parts[^1], out var list)) return Entity.Null;
        var transforms = GetPool<Transform>();
        var infos = GetPool<EntityInfo>();
        for (int i = 0; i < list.Count; i++)
        {
            var candidate = list[i];
            if (!matches(candidate, parts[^1], byTag: false)) continue;
            if (!includeInactive && _inactive[candidate.Id]) continue;
            var current = candidate;
            var ok = true;
            for (int p = parts.Length - 2; p >= 0 && ok; p--)
            {
                var parent = transforms.Has(current.Id) ? transforms.Get(current.Id).Parent : Entity.Null;
                ok = IsAlive(parent) && infos.Has(parent.Id) && infos.Get(parent.Id).Name == parts[p];
                current = parent;
            }
            if (ok && rooted) ok = !transforms.Has(current.Id) || !IsAlive(transforms.Get(current.Id).Parent);
            if (ok) return candidate;
        }
        return Entity.Null;
    }
}

/// <summary>タグの決まった名前 (v1.18)</summary>
public static class EntityTags
{
    /// <summary>タグ無し (EntityInfo.Tag = null と同じ)</summary>
    public const string Untagged = "Untagged";
}
