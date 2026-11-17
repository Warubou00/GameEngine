namespace GameEngine;

// ==========================================================================
// GameObject (v1.18。Unity の GameObject に当たる、エンティティを扱う窓口)
//   中身は World と Entity だけの小さな struct (作っても GC の手間はかからない)。データはこれまで通り World のコンポーネント
//
//   var player = GameObject.Find("Player");                  // 名前で探す (World の索引で引くので速い)
//   if (player) player.Transform.Position += Vector3.UnitY;  // 中身があるか (消えていたら false)
//   ref var body = ref player.GetComponent<Rigidbody>();
//   foreach (var enemy in GameObject.FindGameObjectsWithTag("Enemy")) { ... }
//
//   - コンポーネントのフィールドにも書ける (public GameObject Target;)。保存すると相手の Id (Entity と同じ)
//   - Entity とは暗黙に行き来できる (Destroy(gameObject) / world.Get<T>(gameObject) など)
//   - static の Find は今のエンジン (SceneManager.Current) の World を探す。World を指定するなら world.Find
//   - 名前 / タグは Name / Tag で変える (探す時の索引も直す)
// ==========================================================================
public readonly struct GameObject : IEquatable<GameObject>
{
    private readonly World? _world;
    private readonly Entity _entity;

    public GameObject(World world, Entity entity)
    {
        _world  = entity.IsNull ? null : world;
        _entity = entity;
    }

    /// <summary>何も指していない GameObject (Unity の null)</summary>
    public static GameObject None => default;

    public World? World  => _world;
    public Entity Entity => _entity;

    /// <summary>指している物が生きているか (Unity の if (gameObject))</summary>
    public bool IsValid => _world != null && _world.IsAlive(_entity);

    public static implicit operator bool(GameObject gameObject) => gameObject.IsValid;
    public static implicit operator Entity(GameObject gameObject) => gameObject._entity;

    // ---------------------------------------------------------------------
    // 名前 / タグ / レイヤー / 有効
    // ---------------------------------------------------------------------

    public string Name
    {
        get => IsValid ? _world!.GetName(_entity) : "";
        set => world.SetName(_entity, value);
    }

    /// <summary>タグ (タグ無しは "Untagged")</summary>
    public string Tag
    {
        get => (IsValid ? _world!.GetTag(_entity) : null) ?? EntityTags.Untagged;
        set => world.SetTag(_entity, value);
    }

    public bool CompareTag(string tag) => IsValid && _world!.CompareTag(_entity, tag);

    public int Layer
    {
        get => IsValid && _world!.Has<EntityInfo>(_entity) ? _world.Get<EntityInfo>(_entity).Layer : 0;
        set
        {
            if (!world.Has<EntityInfo>(_entity)) _world!.Add(_entity, new EntityInfo($"Entity {_entity.Id}"));
            _world!.Get<EntityInfo>(_entity).Layer = value;
        }
    }

    /// <summary>自分の有効 (Unity の activeSelf)</summary>
    public bool ActiveSelf => IsValid && (!_world!.Has<EntityInfo>(_entity) || _world.Get<EntityInfo>(_entity).Active);

    /// <summary>自分と親が全部有効か (Unity の activeInHierarchy)</summary>
    public bool ActiveInHierarchy => _world != null && _world.IsActive(_entity);

    public void SetActive(bool active) => world.SetActive(_entity, active);

    // ---------------------------------------------------------------------
    // コンポーネント
    // ---------------------------------------------------------------------

    /// <summary>Transform への参照 (持っていなければ例外)</summary>
    public ref Transform Transform => ref world.Get<Transform>(_entity);

    /// <summary>T への参照 (持っていなければ例外。無いかもしれない時は TryGetComponent / HasComponent)</summary>
    public ref T GetComponent<T>() where T : struct => ref world.Get<T>(_entity);

    public bool TryGetComponent<T>(out T component) where T : struct
    {
        if (_world != null) return ComponentAccess.TryGet(_world, _entity, out component);
        component = default;
        return false;
    }

    public bool HasComponent<T>() where T : struct => _world != null && _world.Has<T>(_entity);

    /// <summary>T を付ける (持っていれば置き換える)。付けた物への参照を返す</summary>
    public ref T AddComponent<T>(in T component = default) where T : struct
    {
        world.Set(_entity, component);
        return ref _world!.Get<T>(_entity);
    }

    public void RemoveComponent<T>() where T : struct => _world?.Remove<T>(_entity);

    /// <summary>T への参照 (ComponentRef。v1.20)。持っていなければ IsValid = false の物</summary>
    public ComponentRef<T> GetComponentRef<T>() where T : struct => _world != null && _world.Has<T>(_entity) ? new ComponentRef<T>(_world, _entity) : default;

    // ---------------------------------------------------------------------
    // 親子
    // ---------------------------------------------------------------------

    /// <summary>親 (無ければ None)</summary>
    public GameObject Parent =>
        IsValid && _world!.Has<Transform>(_entity) ? new GameObject(_world, _world.Get<Transform>(_entity).Parent) : None;

    /// <summary>子の一覧 (全部の Transform を見るので、毎フレーム呼ぶ物ではない)</summary>
    public List<GameObject> GetChildren()
    {
        var result = new List<GameObject>();
        if (!IsValid) return result;
        foreach (var child in Hierarchy.GetChildren(_world!, _entity)) result.Add(new GameObject(_world!, child));
        return result;
    }

    /// <summary>子 (孫は "子/孫" のパス) を名前で探す (Unity の transform.Find。無効な子も返す)</summary>
    public GameObject FindChild(string path)
    {
        if (!IsValid || string.IsNullOrEmpty(path)) return None;
        var current = _entity;
        foreach (var part in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var next = Entity.Null;
            foreach (var child in Hierarchy.GetChildren(_world!, current))
            {
                if (_world!.GetName(child) == part) { next = child; break; }
            }
            if (next.IsNull) return None;
            current = next;
        }
        return new GameObject(_world!, current);
    }

    /// <summary>
    /// 子も一緒にすぐ消す。Behaviour の Update / 衝突の中では Behaviour の Destroy(gameObject) を使う (呼び出しが終わってから消す)
    /// </summary>
    public void Destroy()
    {
        if (IsValid) Hierarchy.DestroyWithChildren(_world!, _entity);
    }

    // ---------------------------------------------------------------------
    // 探す (今のエンジンの World。Unity の GameObject.Find など)
    // ---------------------------------------------------------------------

    /// <summary>名前 (または "親/子" のパス) で探す。有効な物だけ</summary>
    public static GameObject Find(string nameOrPath) => wrap(currentWorld.Find(nameOrPath));

    public static GameObject FindWithTag(string tag) => wrap(currentWorld.FindWithTag(tag));

    public static List<GameObject> FindGameObjectsWithTag(string tag)
    {
        var world = currentWorld;
        var entities = new List<Entity>();
        world.FindAllWithTag(tag, entities);
        return entities.ConvertAll(e => new GameObject(world, e));
    }

    /// <summary>T を持つ最初の (有効な) 物</summary>
    public static GameObject FindObjectOfType<T>() where T : struct => wrap(currentWorld.FindObjectOfType<T>());

    /// <summary>T を持つ (有効な) 物を全部</summary>
    public static List<GameObject> FindObjectsOfType<T>() where T : struct
    {
        var world = currentWorld;
        var entities = new List<Entity>();
        world.FindObjectsOfType<T>(entities);
        return entities.ConvertAll(e => new GameObject(world, e));
    }

    private static World currentWorld =>
        SceneManager.Current?.World ?? throw new InvalidOperationException("No engine has been created (GameObject.Find needs SceneManager.Current).");

    private static GameObject wrap(Entity entity) => entity.IsNull ? None : new GameObject(currentWorld, entity);

    // 生きていなければ例外 (書き換える時)
    private World world =>
        _world != null && _world.IsAlive(_entity) ? _world : throw new InvalidOperationException($"GameObject {_entity} is not alive.");

    // ---------------------------------------------------------------------

    public bool Equals(GameObject other) => _entity == other._entity && (_entity.IsNull || ReferenceEquals(_world, other._world));
    public override bool Equals(object? obj) => obj is GameObject other && Equals(other);
    public override int GetHashCode() => _entity.GetHashCode();
    public static bool operator ==(GameObject a, GameObject b) => a.Equals(b);
    public static bool operator !=(GameObject a, GameObject b) => !a.Equals(b);
    public override string ToString() => IsValid ? $"GameObject({Name}, {_entity})" : "GameObject(None)";
}
