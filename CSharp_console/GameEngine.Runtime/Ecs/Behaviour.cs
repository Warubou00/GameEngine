using System.Numerics;
using System.Reflection;

namespace GameEngine;

// ==========================================================================
// Behaviour<T> : Unity の MonoBehaviour に当たる書き方 (v1.2)
//   T (自分のコンポーネント。値を持つ struct) を持つエンティティごとに、Update と衝突のメソッドが呼ばれる System
//
//   [GameSystem]
//   public sealed class PlayerSystem : Behaviour<Player>
//   {
//       protected override void Update(ref Player player)
//       {
//           ref var transform = ref Transform;                    // = GetComponent<Transform>()
//           if (TryGetComponent<Rigidbody>(out var body)) { ... }   // 無いかもしれない物
//       }
//       protected override void OnCollisionEnter(ref Player player, in Collision collision)
//       {
//           if (collision.HasComponent<Enemy>()) collision.GetComponent<Enemy>().Hp -= 1;   // 相手のコンポーネント
//       }
//   }
//
//   - GetComponent<U>() : 今のエンティティ (Entity) の U への参照 (ref)。書き換えるとそのまま反映される。持っていなければ例外
//     (Unity は null を返すが、struct なので参照を返す。無いかもしれない時は TryGetComponent / HasComponent)
//     型ごとの入れ物は World が覚えておく (辞書を引かない) ので、毎フレーム何千回呼んでも速い
//   - 衝突 : OnCollisionEnter / OnCollisionStay / OnCollisionExit (押し返す Collider 同士)
//            OnTriggerEnter / OnTriggerStay / OnTriggerExit (どちらかが IsTrigger)
//     自分と相手の両方に T があれば両方で呼ばれる (Collision.Self が自分、Collision.Other が相手)
//     呼ばれる時 : 物理の刻みの後、次のフレームの Update の前 (Unity と同じ順)。Stay は重なっている間、毎フレーム 1 回
//                  (Enter と同じフレームには呼ばない)。上書きしていないメソッドは呼ばない (調べる手間もかけない)
//   - 呼ばれる順 (v1.4。Unity と同じ) : 新しい物には Awake → Start (最初の Update / FixedUpdate / LateUpdate の前に 1 回ずつ)
//       → 毎フレーム : OnCollision* → Update → (物理の刻みごとに FixedUpdate) → LateUpdate
//     Awake : 物ができて最初に 1 回 (この System の全部の新しい物の Awake の後に、Start が呼ばれる)
//     Start : 最初の Update の前に 1 回 / FixedUpdate : 物理の固定の刻みごと (Time.FixedDeltaTime) /
//     LateUpdate : 全部の Update と物理の後 (カメラを追いかけさせる時など)。無効な物は呼ばない (有効になったら Awake / Start から)
//   - Destroy(entity) : フレームの区切り (この Behaviour の Update / 衝突の呼び出しが全部終わった後) で消す (Unity と同じ)
//     Update の途中で World.DestroyEntity を呼ぶと、回している配列が変わるので、こちらを使う
//   - 注意 : Update / OnCollision の中で、同じ T を別のエンティティに足す / 外すと、ref の data が古い場所を指すことがある
// ==========================================================================
public abstract class Behaviour<T> : ISystem, IFixedSystem, ILateSystem, ICollisionReceiver where T : struct
{
    /// <summary>今動いている World</summary>
    protected World World { get; private set; } = null!;

    /// <summary>今処理しているエンティティ (Unity の gameObject に当たる)</summary>
    protected Entity Entity { get; private set; }

    private readonly CollisionCallbacks _callbacks;
    private readonly bool _hasUpdate, _hasAwake, _hasStart, _hasFixedUpdate, _hasLateUpdate;
    // Awake / Start を呼んだ物 (エンティティの番号 → その時の版。番号が使い回されたら版が違うので、新しい物として呼ぶ)
    private int[] _awoken = new int[64];
    private int[] _started = new int[64];
    private readonly List<Entity> _destroyQueue = new();

    protected Behaviour()
    {
        // 上書きしたメソッドだけ呼ぶ (1 回だけ調べる)
        var dataType = typeof(T).MakeByRefType();
        var collisionType = typeof(Collision).MakeByRefType();
        bool overrides(string name, params Type[] parameters) =>
            GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, parameters, null)?.DeclaringType != typeof(Behaviour<T>);
        bool overridesCollision(string name) => overrides(name, dataType, collisionType);
        _hasUpdate = overrides(nameof(Update), dataType);
        _hasAwake = overrides(nameof(Awake), dataType);
        _hasStart = overrides(nameof(Start), dataType);
        _hasFixedUpdate = overrides(nameof(FixedUpdate), dataType);
        _hasLateUpdate = overrides(nameof(LateUpdate), dataType);
        if (overridesCollision(nameof(OnCollisionEnter))) _callbacks |= CollisionCallbacks.CollisionEnter;
        if (overridesCollision(nameof(OnCollisionStay)))  _callbacks |= CollisionCallbacks.CollisionStay;
        if (overridesCollision(nameof(OnCollisionExit)))  _callbacks |= CollisionCallbacks.CollisionExit;
        if (overridesCollision(nameof(OnTriggerEnter)))   _callbacks |= CollisionCallbacks.TriggerEnter;
        if (overridesCollision(nameof(OnTriggerStay)))    _callbacks |= CollisionCallbacks.TriggerStay;
        if (overridesCollision(nameof(OnTriggerExit)))    _callbacks |= CollisionCallbacks.TriggerExit;
    }

    // ---------------------------------------------------------------------
    // 書くメソッド (上書きした物だけ呼ばれる)
    // ---------------------------------------------------------------------

    /// <summary>物ができて最初に 1 回 (Start より前。v1.4)</summary>
    protected virtual void Awake(ref T data) { }

    /// <summary>最初の Update (/ FixedUpdate / LateUpdate) の前に 1 回 (v1.4)</summary>
    protected virtual void Start(ref T data) { }

    /// <summary>毎フレーム、T を持つ (有効な) エンティティごとに呼ばれる。data は T への参照</summary>
    protected virtual void Update(ref T data) { }

    /// <summary>物理の固定の刻みごと (Time.FixedDeltaTime)。剛体に力をかけるのはここ (v1.4)</summary>
    protected virtual void FixedUpdate(ref T data) { }

    /// <summary>全部の Update と物理の後 (カメラを追いかけさせる時など。v1.4)</summary>
    protected virtual void LateUpdate(ref T data) { }

    protected virtual void OnCollisionEnter(ref T data, in Collision collision) { }
    protected virtual void OnCollisionStay(ref T data, in Collision collision) { }
    protected virtual void OnCollisionExit(ref T data, in Collision collision) { }
    protected virtual void OnTriggerEnter(ref T data, in Collision other) { }
    protected virtual void OnTriggerStay(ref T data, in Collision other) { }
    protected virtual void OnTriggerExit(ref T data, in Collision other) { }

    // ---------------------------------------------------------------------
    // GetComponent
    // ---------------------------------------------------------------------

    /// <summary>今のエンティティの U への参照 (持っていなければ例外)</summary>
    protected ref U GetComponent<U>() where U : struct => ref World.GetPool<U>().Get(Entity.Id);

    /// <summary>今のエンティティが U を持っていれば、その値 (コピー) を返す</summary>
    protected bool TryGetComponent<U>(out U component) where U : struct => ComponentAccess.TryGet(World, Entity, out component);

    /// <summary>今のエンティティが U を持っているか</summary>
    protected bool HasComponent<U>() where U : struct => World.GetPool<U>().Has(Entity.Id);

    /// <summary>他のエンティティの U への参照 (持っていなければ例外)</summary>
    protected ref U GetComponent<U>(Entity entity) where U : struct
    {
        if (!World.IsAlive(entity)) throw new InvalidOperationException($"{entity} is not alive.");
        return ref World.GetPool<U>().Get(entity.Id);
    }

    protected bool TryGetComponent<U>(Entity entity, out U component) where U : struct => ComponentAccess.TryGet(World, entity, out component);
    protected bool HasComponent<U>(Entity entity) where U : struct => World.Has<U>(entity);

    /// <summary>今のエンティティの Transform (= GetComponent&lt;Transform&gt;()。Unity の transform)</summary>
    protected ref Transform Transform => ref World.GetPool<Transform>().Get(Entity.Id);

    /// <summary>エンティティを (子も一緒に) 消す。消すのは、この Behaviour の呼び出しが全部終わった後</summary>
    protected void Destroy(Entity entity) { if (!_destroyQueue.Contains(entity)) _destroyQueue.Add(entity); }

    // ---------------------------------------------------------------------
    // エンジンから
    // ---------------------------------------------------------------------

    void ISystem.Update(World world)
    {
        World = world;
        awakeAndStart(world);
        if (_hasUpdate)
        {
            // 一番多く呼ばれるので、デリゲートを通さずに回す (v1.2 と同じ速さ)
            var pool = world.GetPool<T>();
            var entities = pool.Entities;
            var components = pool.Components;
            for (int i = 0; i < entities.Length; i++)
            {
                var entity = entities[i];
                if (!world.IsActive(entity)) continue;
                Entity = entity;
                Update(ref components[i]);
            }
            Entity = default;
        }
        flushDestroy(world);
    }

    void IFixedSystem.FixedUpdate(World world)
    {
        if (!_hasFixedUpdate) return;
        World = world;
        awakeAndStart(world);
        forEach(world, static (Behaviour<T> self, ref T data) => self.FixedUpdate(ref data));
        flushDestroy(world);
    }

    void ILateSystem.LateUpdate(World world)
    {
        if (!_hasLateUpdate) return;
        World = world;
        awakeAndStart(world);
        forEach(world, static (Behaviour<T> self, ref T data) => self.LateUpdate(ref data));
        flushDestroy(world);
    }

    private delegate void Call(Behaviour<T> self, ref T data);

    // 詰めた配列をそのまま回す (ForEach と同じ速さ)。回している間に増えた物は次から
    private void forEach(World world, Call call)
    {
        var pool = world.GetPool<T>();
        var entities = pool.Entities;
        var components = pool.Components;
        for (int i = 0; i < entities.Length; i++)
        {
            var entity = entities[i];
            if (!world.IsActive(entity)) continue;
            Entity = entity;
            call(this, ref components[i]);
        }
        Entity = default;
    }

    // 新しい (有効な) 物 : 全部の Awake → 全部の Start (Unity と同じく、Awake を全部終えてから Start)。
    //   どちらも書いていなければ見回らない。見回りは番号で引く表なので、物が多くても速い
    private void awakeAndStart(World world)
    {
        if (!_hasAwake && !_hasStart) return;
        var pool = world.GetPool<T>();
        var entities = pool.Entities;
        var components = pool.Components;
        var newOnes = false;
        for (int i = 0; i < entities.Length; i++)
        {
            var entity = entities[i];
            if (entity.Id >= _started.Length) { Array.Resize(ref _awoken, Math.Max(entity.Id + 1, _awoken.Length * 2)); Array.Resize(ref _started, _awoken.Length); }
            if (_started[entity.Id] == entity.Version || !world.IsActive(entity)) continue;
            newOnes = true;
            if (_awoken[entity.Id] == entity.Version) continue;
            _awoken[entity.Id] = entity.Version;
            Entity = entity;
            if (_hasAwake) Awake(ref components[i]);
        }
        if (!newOnes) { Entity = default; return; }
        entities = pool.Entities;   // Awake の中で増えた / 減った時のため
        components = pool.Components;
        for (int i = 0; i < entities.Length; i++)
        {
            var entity = entities[i];
            if (entity.Id >= _started.Length || _started[entity.Id] == entity.Version || _awoken[entity.Id] != entity.Version || !world.IsActive(entity)) continue;
            _started[entity.Id] = entity.Version;
            Entity = entity;
            if (_hasStart) Start(ref components[i]);
        }
        Entity = default;
    }

    CollisionCallbacks ICollisionReceiver.Callbacks => _callbacks;

    void ICollisionReceiver.Receive(World world, CollisionCallbacks phase, in Collision collision)
    {
        var pool = world.GetPool<T>();
        if (!pool.Has(collision.Self.Id) || !world.IsActive(collision.Self)) return;
        World = world;
        Entity = collision.Self;
        ref var data = ref pool.Get(collision.Self.Id);
        switch (phase)
        {
            case CollisionCallbacks.CollisionEnter: OnCollisionEnter(ref data, collision); break;
            case CollisionCallbacks.CollisionStay:  OnCollisionStay(ref data, collision);  break;
            case CollisionCallbacks.CollisionExit:  OnCollisionExit(ref data, collision);  break;
            case CollisionCallbacks.TriggerEnter:   OnTriggerEnter(ref data, collision);   break;
            case CollisionCallbacks.TriggerStay:    OnTriggerStay(ref data, collision);    break;
            case CollisionCallbacks.TriggerExit:    OnTriggerExit(ref data, collision);    break;
        }
        Entity = default;
    }

    void ICollisionReceiver.AfterCollisions(World world) => flushDestroy(world);

    private void flushDestroy(World world)
    {
        if (_destroyQueue.Count == 0) return;
        foreach (var entity in _destroyQueue)
            if (world.IsAlive(entity)) Hierarchy.DestroyWithChildren(world, entity);
        _destroyQueue.Clear();
    }
}

internal static class ComponentAccess
{
    public static bool TryGet<U>(World world, Entity entity, out U component) where U : struct
    {
        var pool = world.GetPool<U>();
        if (world.IsAlive(entity) && pool.Has(entity.Id)) { component = pool.Get(entity.Id); return true; }
        component = default;
        return false;
    }
}

// 衝突のメソッドの種類 (Behaviour が上書きした物の印。エンジンが呼ぶ時の種類にも使う)
[Flags]
public enum CollisionCallbacks
{
    None = 0,
    CollisionEnter = 1, CollisionStay = 2, CollisionExit = 4,
    TriggerEnter = 8, TriggerStay = 16, TriggerExit = 32,
}

// 衝突のメソッドを受け取る物 (Behaviour<T>)。エンジンの CollisionCallbackSystem が呼ぶ
internal interface ICollisionReceiver
{
    CollisionCallbacks Callbacks { get; }
    void Receive(World world, CollisionCallbacks phase, in Collision collision);
    void AfterCollisions(World world);
}

// ==========================================================================
// 衝突の情報 (Unity の Collision に当たる)。Self = 自分 / Other = 相手
//   GetComponent<U>() は相手のコンポーネント (Unity の collision.gameObject.GetComponent)
//   TryGetContact : 接触点と法線 (相手から自分へ向く = 自分が押し返される向き)。Exit とトリガーには無い
//   接触点は、初めて聞かれた時にそのフレームの分を 1 回だけ C++ から取る (聞かなければ取らない)
// ==========================================================================
public readonly struct Collision
{
    private readonly CollisionCallbackSystem _source;

    public Entity Self { get; }
    public Entity Other { get; }
    public bool IsTrigger { get; }

    internal Collision(CollisionCallbackSystem source, Entity self, Entity other, bool isTrigger)
    {
        _source = source;
        Self = self;
        Other = other;
        IsTrigger = isTrigger;
    }

    public World World => _source.World;

    /// <summary>相手の U への参照 (持っていなければ例外)</summary>
    public ref U GetComponent<U>() where U : struct
    {
        if (!World.IsAlive(Other)) throw new InvalidOperationException($"{Other} is not alive.");
        return ref World.GetPool<U>().Get(Other.Id);
    }

    public bool TryGetComponent<U>(out U component) where U : struct => ComponentAccess.TryGet(World, Other, out component);
    public bool HasComponent<U>() where U : struct => World.Has<U>(Other);

    /// <summary>接触点と法線 (相手から自分へ向く単位ベクトル)。今の接触が無ければ false</summary>
    public bool TryGetContact(out Vector3 point, out Vector3 normal) => _source.TryGetContact(Self, Other, out point, out normal);

    public override string ToString() => $"Collision({Self} - {Other}{(IsTrigger ? ", trigger" : "")})";
}
