namespace GameEngine;

// ForEach に渡す処理 (コンポーネントは ref で受け取るので、その場で書き換えられる)
public delegate void ForEachAction<T1>(Entity entity, ref T1 c1);
public delegate void ForEachAction<T1, T2>(Entity entity, ref T1 c1, ref T2 c2);
public delegate void ForEachAction<T1, T2, T3>(Entity entity, ref T1 c1, ref T2 c2, ref T3 c3);

// ==========================================================================
// ECS の世界 (エンティティと、型ごとのコンポーネントの入れ物を持つ)
//
//   var e = world.CreateEntity();
//   world.Add(e, new Transform { Position = new Vector3(0, 1, 0) });
//   world.Add(e, new MeshRenderer(model));
//   world.ForEach((Entity e, ref Transform t, ref Spin s) => t.Rotation *= ...);
//
//   ※ForEach の中でエンティティ / コンポーネントを追加・削除しないこと (配列の順番が変わるため)
//     必要なら一度リストに集めて、ForEach の後で行う
// ==========================================================================
public sealed class World
{
    private int[]      _versions = new int[256];   // 番号ごとの世代 (生きている間の値)
    private bool[]     _alive    = new bool[256];
    private bool[]     _inactive = new bool[256];   // 無効 (自分か親の EntityInfo.Active が false。ED2)
    private int        _nextId;
    private readonly Stack<int> _freeIds = new();

    private readonly Dictionary<Type, IComponentPool> _pools    = new();
    private readonly List<IComponentPool>             _poolList = new();

    public int EntityCount { get; private set; }

    // ---------------------------------------------------------------------
    // エンティティ
    // ---------------------------------------------------------------------

    public Entity CreateEntity()
    {
        int id;
        if (_freeIds.Count > 0)
        {
            id = _freeIds.Pop();
        }
        else
        {
            id = _nextId++;
            if (id >= _versions.Length)
            {
                Array.Resize(ref _versions, _versions.Length * 2);
                Array.Resize(ref _alive,    _alive.Length * 2);
                Array.Resize(ref _inactive, _inactive.Length * 2);
            }
        }

        _versions[id]++;          // 使い回した番号は世代が上がる (古い Entity は IsAlive = false になる)
        _alive[id] = true;
        _inactive[id] = false;
        EntityCount++;
        return new Entity(id, _versions[id]);
    }

    // 名前を付けて作る (EntityInfo が付く。保存しても変わらない Id も付く。ED1)
    public Entity CreateEntity(string name)
    {
        var entity = CreateEntity();
        Add(entity, new EntityInfo(name));
        return entity;
    }

    // 生きている全てのエンティティ (番号の順)
    public IEnumerable<Entity> Entities
    {
        get
        {
            for (int id = 0; id < _nextId; id++)
            {
                if (_alive[id]) yield return new Entity(id, _versions[id]);
            }
        }
    }

    // 全ての型の入れ物 (保存 / エディター用)
    public IReadOnlyList<IComponentPool> Pools => _poolList;

    // 型を知らずに入れ物を取る (無ければ作る。保存 / エディター用)
    public IComponentPool GetPool(Type type)
    {
        if (_pools.TryGetValue(type, out var pool)) return pool;
        var method = typeof(World).GetMethod(nameof(GetPool), 1, Type.EmptyTypes)!.MakeGenericMethod(type);
        return (IComponentPool)method.Invoke(this, null)!;
    }

    // 全てのエンティティを削除する (シーンを読み込み直す前など)
    public void Clear()
    {
        foreach (var entity in Entities.ToList()) DestroyEntity(entity);
    }

    // エンティティと、その全てのコンポーネントを削除する
    public void DestroyEntity(Entity entity)
    {
        if (!IsAlive(entity)) return;

        foreach (var pool in _poolList)
        {
            pool.Remove(entity.Id);
        }
        _alive[entity.Id] = false;
        _freeIds.Push(entity.Id);
        EntityCount--;
    }

    public bool IsAlive(Entity entity) =>
        !entity.IsNull && entity.Id < _nextId && _alive[entity.Id] && _versions[entity.Id] == entity.Version;

    // ---------------------------------------------------------------------
    // 有効 / 無効 (ED2。Unity の activeInHierarchy / SetActive と同じ)
    //   無効な物は、エンジンの System (描画 / 物理 / ライト / カメラ / 音 / パーティクル) も ForEach も飛ばす
    //   自分の EntityInfo.Active が false か、親 (Transform.Parent を辿る) のどれかが無効なら無効
    //   親から子への伝わりは TransformSystem が毎フレーム計算する (SetActive の自分の分はすぐ効く)
    // ---------------------------------------------------------------------

    /// <summary>有効か (自分と親が全部有効)。生きていなければ false</summary>
    public bool IsActive(Entity entity) => IsAlive(entity) && !_inactive[entity.Id];

    /// <summary>自分の有効 / 無効を変える (EntityInfo.Active。無ければ付ける)</summary>
    public void SetActive(Entity entity, bool active)
    {
        ThrowIfDead(entity);
        var infos = GetPool<EntityInfo>();
        if (!infos.Has(entity.Id)) infos.Add(entity, new EntityInfo($"Entity {entity.Id}") { Active = active });
        else infos.Get(entity.Id).Active = active;
        SetActiveInHierarchy(entity.Id, active);
    }

    // 有効 / 無効が変わった回数 (描画などが「作り直す物があるか」を見るのに使う)
    public int ActivityVersion { get; private set; }

    internal void SetActiveInHierarchy(int id, bool active)
    {
        if (_inactive[id] != active) return;   // 変わっていない
        _inactive[id] = !active;
        ActivityVersion++;
    }

    // ---------------------------------------------------------------------
    // コンポーネント
    // ---------------------------------------------------------------------

    // 型ごとの入れ物 (無ければ作る)。System が詰めた配列を直接回す時に使う
    public ComponentPool<T> GetPool<T>() where T : struct
    {
        if (_pools.TryGetValue(typeof(T), out var pool)) return (ComponentPool<T>)pool;

        var created = new ComponentPool<T>();
        _pools.Add(typeof(T), created);
        _poolList.Add(created);
        return created;
    }

    public void Add<T>(Entity entity, in T component) where T : struct
    {
        ThrowIfDead(entity);
        GetPool<T>().Add(entity, component);
    }

    // 持っていれば置き換え、無ければ追加する
    public void Set<T>(Entity entity, in T component) where T : struct
    {
        ThrowIfDead(entity);
        var pool = GetPool<T>();
        if (pool.Has(entity.Id)) pool.Get(entity.Id) = component;
        else                     pool.Add(entity, component);
    }

    public bool Has<T>(Entity entity) where T : struct => IsAlive(entity) && GetPool<T>().Has(entity.Id);

    // コンポーネントへの参照 (書き換えるとそのまま反映される)
    public ref T Get<T>(Entity entity) where T : struct
    {
        ThrowIfDead(entity);
        return ref GetPool<T>().Get(entity.Id);
    }

    public void Remove<T>(Entity entity) where T : struct
    {
        if (!IsAlive(entity)) return;
        GetPool<T>().Remove(entity.Id);
    }

    // ---------------------------------------------------------------------
    // まとめて処理する (一番数の少ない型の配列を回し、他の型は番号で引く)
    // ---------------------------------------------------------------------

    public void ForEach<T1>(ForEachAction<T1> action) where T1 : struct
    {
        var pool       = GetPool<T1>();
        var entities   = pool.Entities;
        var components = pool.Components;
        for (int i = 0; i < entities.Length; i++)
        {
            if (_inactive[entities[i].Id]) continue;   // 無効な物は飛ばす (ED2)
            action(entities[i], ref components[i]);
        }
    }

    public void ForEach<T1, T2>(ForEachAction<T1, T2> action) where T1 : struct where T2 : struct
    {
        var p1 = GetPool<T1>();
        var p2 = GetPool<T2>();
        var entities = p1.Count <= p2.Count ? p1.Entities : p2.Entities;
        for (int i = 0; i < entities.Length; i++)
        {
            var id = entities[i].Id;
            if (!p1.Has(id) || !p2.Has(id) || _inactive[id]) continue;
            action(entities[i], ref p1.Get(id), ref p2.Get(id));
        }
    }

    public void ForEach<T1, T2, T3>(ForEachAction<T1, T2, T3> action) where T1 : struct where T2 : struct where T3 : struct
    {
        var p1 = GetPool<T1>();
        var p2 = GetPool<T2>();
        var p3 = GetPool<T3>();
        var smallest = Math.Min(p1.Count, Math.Min(p2.Count, p3.Count));
        var entities = smallest == p1.Count ? p1.Entities : smallest == p2.Count ? p2.Entities : p3.Entities;
        for (int i = 0; i < entities.Length; i++)
        {
            var id = entities[i].Id;
            if (!p1.Has(id) || !p2.Has(id) || !p3.Has(id) || _inactive[id]) continue;
            action(entities[i], ref p1.Get(id), ref p2.Get(id), ref p3.Get(id));
        }
    }

    private void ThrowIfDead(Entity entity)
    {
        if (!IsAlive(entity)) throw new InvalidOperationException($"{entity} is not alive.");
    }
}
