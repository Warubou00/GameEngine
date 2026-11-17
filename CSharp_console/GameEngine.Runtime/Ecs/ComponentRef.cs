using System.Reflection;

namespace GameEngine;

// ==========================================================================
// ComponentRef<T> (v1.20 GO3。Unity で「public Rigidbody body;」と書くのに当たる)
//   「T を持つ物」への参照。コンポーネントのフィールドに書くと、Inspector の欄は T を持つ物だけを受け付ける
//
//   public struct Turret : IComponent
//   {
//       public ComponentRef<Rigidbody> Body;        // Inspector で Rigidbody を持つ物を落とす
//   }
//   if (turret.Body) turret.Body.Value.Velocity = Vector3.Zero;   // Value は T への参照 (ref)
//
//   - 保存は相手の Id (Entity / GameObject と同じ)。読み込み先の World の物になる
//   - 中身は World と Entity だけ。相手が T を外した / 消えた時は IsValid = false (Value は例外)
// ==========================================================================
public readonly struct ComponentRef<T> : IEntityRef, IEquatable<ComponentRef<T>> where T : struct
{
    private readonly World? _world;
    private readonly Entity _entity;

    public ComponentRef(World world, Entity entity)
    {
        _world  = entity.IsNull ? null : world;
        _entity = entity;
    }

    public World? World  => _world;
    public Entity Entity => _entity;
    Type IEntityRef.ComponentType => typeof(T);

    /// <summary>指している物が生きていて T を持っているか</summary>
    public bool IsValid => _world != null && _world.Has<T>(_entity);

    /// <summary>T への参照 (書き換えるとそのまま反映される。IsValid でなければ例外)</summary>
    public ref T Value
    {
        get
        {
            if (!IsValid) throw new InvalidOperationException($"ComponentRef<{typeof(T).Name}> : {_entity} is not alive or has no {typeof(T).Name}.");
            return ref _world!.Get<T>(_entity);
        }
    }

    /// <summary>T の値 (コピー) を取る (無ければ false)</summary>
    public bool TryGet(out T value)
    {
        if (IsValid) { value = _world!.Get<T>(_entity); return true; }
        value = default;
        return false;
    }

    /// <summary>持ち主の GameObject</summary>
    public GameObject GameObject => _world == null ? GameObject.None : new GameObject(_world, _entity);

    public static implicit operator bool(ComponentRef<T> reference) => reference.IsValid;
    public static implicit operator Entity(ComponentRef<T> reference) => reference._entity;

    public bool Equals(ComponentRef<T> other) => _entity == other._entity && (_entity.IsNull || ReferenceEquals(_world, other._world));
    public override bool Equals(object? obj) => obj is ComponentRef<T> other && Equals(other);
    public override int GetHashCode() => _entity.GetHashCode();
    public static bool operator ==(ComponentRef<T> a, ComponentRef<T> b) => a.Equals(b);
    public static bool operator !=(ComponentRef<T> a, ComponentRef<T> b) => !a.Equals(b);
    public override string ToString() => IsValid ? $"ComponentRef<{typeof(T).Name}>({_world!.GetName(_entity)}, {_entity})" : $"ComponentRef<{typeof(T).Name}>(None)";
}

/// <summary>
/// 物への参照の型 (ComponentRef&lt;T&gt;) を型引数を知らずに扱う口 (保存 / Inspector 用。v1.20)
/// ゲームの型 T で MakeGenericMethod をしないため (読み直した古いゲームの DLL が解放されなくなる。ED7)
/// </summary>
public interface IEntityRef
{
    World? World { get; }
    Entity Entity { get; }
    /// <summary>相手に要るコンポーネントの型</summary>
    Type ComponentType { get; }
}

/// <summary>IEntityRef の型の値を作る (v1.20)</summary>
public static class EntityRefs
{
    /// <summary>type (ComponentRef&lt;T&gt; など IEntityRef の struct) の値を作る。entity が Null なら空の値</summary>
    public static object Create(Type type, World world, Entity entity) =>
        entity.IsNull ? Activator.CreateInstance(type)! :
        Activator.CreateInstance(type, BindingFlags.Public | BindingFlags.Instance, null, new object[] { world, entity }, null)!;

    /// <summary>IEntityRef の struct の型か</summary>
    public static bool IsEntityRef(Type type) => type.IsValueType && typeof(IEntityRef).IsAssignableFrom(type);
}
