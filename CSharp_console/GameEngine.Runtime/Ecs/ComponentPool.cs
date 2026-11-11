namespace GameEngine;

// 型を問わずに扱うための口 (World がエンティティを削除する時に、全ての型から外すのに使う)
public interface IComponentPool
{
    Type ComponentType { get; }
    int  Count { get; }
    bool Has(int entityId);
    void Remove(int entityId);

    // 型を知らずに読み書きする (保存 / 読み込み / エディター用。値は箱に入れたコピー。ED1)
    object GetBoxed(int entityId);
    void   SetBoxed(Entity entity, object component);   // 持っていれば置き換え、無ければ追加
}

// ==========================================================================
// 1 つの型のコンポーネントを詰めて持つ入れ物 (スパースセット)
//   _components : 詰めた配列 (隙間が無いので、全部を順に処理するのが速い)
//   _sparse     : エンティティの番号 → 詰めた配列の位置 + 1 (0 = 持っていない)
//   削除は「最後の要素を空いた場所へ移す」ので O(1)。その代わり順番は変わる
// ==========================================================================
public sealed class ComponentPool<T> : IComponentPool where T : struct
{
    private int[]    _sparse     = new int[64];
    private Entity[] _entities   = new Entity[64];
    private T[]      _components = new T[64];
    private int      _count;

    // 外されたときに呼ばれる (例 : MeshRenderer が外れたら C++ の描画オブジェクトを削除する)
    public event Action<Entity, T>? Removed;
    // 付けられたときに呼ばれる (v1.6。例 : AudioSource に持ち主のエンティティを書く)。置き換え (SetBoxed / Get への代入) では呼ばれない
    public event Action<Entity>? Added;

    public Type ComponentType => typeof(T);
    public int  Count => _count;

    // 詰めた配列 (Components[i] は Entities[i] のもの)。処理中に追加 / 削除しないこと
    public ReadOnlySpan<Entity> Entities   => new(_entities, 0, _count);
    public Span<T>              Components => new(_components, 0, _count);

    public bool Has(int entityId) => (uint)entityId < (uint)_sparse.Length && _sparse[entityId] != 0;

    // 持っていない時は例外 (TryGet を使うか、先に Has で確かめる)
    public ref T Get(int entityId)
    {
        if (!Has(entityId)) throw new InvalidOperationException($"Entity {entityId} does not have {typeof(T).Name}.");
        return ref _components[_sparse[entityId] - 1];
    }

    // 持っていれば中身への参照、無ければ null 参照 (Unsafe.IsNullRef で確かめる)
    public ref T TryGet(int entityId, out bool found)
    {
        found = Has(entityId);
        if (!found) return ref System.Runtime.CompilerServices.Unsafe.NullRef<T>();
        return ref _components[_sparse[entityId] - 1];
    }

    internal void Add(Entity entity, in T component)
    {
        if (Has(entity.Id)) throw new InvalidOperationException($"{entity} already has {typeof(T).Name}.");

        if (entity.Id >= _sparse.Length) Array.Resize(ref _sparse, Math.Max(entity.Id + 1, _sparse.Length * 2));
        if (_count == _components.Length)
        {
            Array.Resize(ref _components, _count * 2);
            Array.Resize(ref _entities,   _count * 2);
        }

        _components[_count] = component;
        _entities[_count]   = entity;
        _count++;
        _sparse[entity.Id]  = _count;
        Added?.Invoke(entity);
    }

    public object GetBoxed(int entityId) => Get(entityId);

    public void SetBoxed(Entity entity, object component)
    {
        if (Has(entity.Id)) Get(entity.Id) = (T)component;
        else                Add(entity, (T)component);
    }

    public void Remove(int entityId)
    {
        if (!Has(entityId)) return;

        var index   = _sparse[entityId] - 1;
        var entity  = _entities[index];
        var removed = _components[index];

        // 最後の要素を空いた場所へ移す
        var last = _count - 1;
        if (index != last)
        {
            _components[index] = _components[last];
            _entities[index]   = _entities[last];
            _sparse[_entities[index].Id] = index + 1;
        }
        _components[last] = default;
        _entities[last]   = default;
        _sparse[entityId] = 0;
        _count--;

        Removed?.Invoke(entity, removed);
    }
}
