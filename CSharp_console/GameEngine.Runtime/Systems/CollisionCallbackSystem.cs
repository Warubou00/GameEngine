using System.Numerics;

namespace GameEngine;

// ==========================================================================
// 衝突のメソッドを呼ぶ (v1.2。Behaviour<T> の OnCollisionEnter / Stay / Exit と OnTrigger*)
//   物理の Enter / Exit のイベント (engine.Physics.Events) から、今重なっている組の表を持つ
//   呼ぶ時 : 再生中、毎フレームの System の Update の前 (前のフレームの物理の刻みの結果。Unity と同じ順)
//     1. 新しいイベントを順に : Enter → 組を足して OnXxxEnter / Exit → 組を外して OnXxxExit
//     2. 前のフレームより前から重なっている組 : OnXxxStay (Enter と同じフレームには呼ばない)
//     組の両方に呼ぶ (Self / Other を入れ替えて)。Behaviour が T を持つ方だけ受け取る
//   速さ : 衝突のメソッドを上書きした Behaviour が無ければ、組の表を直すだけ。Stay を上書きした物が無ければ Stay の見回りもしない
//          接触点は、Collision.TryGetContact で初めて聞かれた時に 1 回だけ取る
//   トリガー : どちらかの Collider が IsTrigger (重なり始めた時の値で決める)
// ==========================================================================
internal sealed class CollisionCallbackSystem
{
    private struct Pair
    {
        public Entity A, B;
        public bool   Trigger;
        public int    EnterFrame;
    }

    private readonly List<Pair> _pairs = new();
    private readonly Dictionary<(Entity, Entity), int> _index = new();   // 組 (小さい番号が先) → _pairs の位置
    private readonly List<ICollisionReceiver> _receivers = new();
    private int _lastEventFrame = -1;
    private int _frame;
    private CollisionCallbacks _wanted;

    // 接触点 (聞かれた時に作る)
    private readonly List<Contact> _contacts = new();
    private readonly Dictionary<(Entity, Entity), int> _contactIndex = new();
    private int _contactFrame = -1;
    private PhysicsSettings? _physics;

    public World World { get; private set; } = null!;

    /// <summary>今重なっている組の数 (確認用)</summary>
    public int PairCount => _pairs.Count;

    /// <summary>再生を始めた時 : 表を空にし、編集中のイベントは呼ばない</summary>
    public void Reset(PhysicsSettings physics)
    {
        _pairs.Clear();
        _index.Clear();
        _lastEventFrame = physics.EventFrame;
    }

    private static (Entity, Entity) keyOf(Entity a, Entity b) => a.Id < b.Id || (a.Id == b.Id && a.Version <= b.Version) ? (a, b) : (b, a);

    public void Dispatch(World world, PhysicsSettings physics, List<ISystem> systems)
    {
        World = world;
        _physics = physics;
        _frame++;
        _receivers.Clear();
        _wanted = CollisionCallbacks.None;
        foreach (var system in systems)
        {
            if (system is ICollisionReceiver receiver && receiver.Callbacks != CollisionCallbacks.None)
            {
                _receivers.Add(receiver);
                _wanted |= receiver.Callbacks;
            }
        }

        // 1. 新しいイベント (同じイベントを 2 回呼ばない)
        if (physics.EventFrame != _lastEventFrame)
        {
            _lastEventFrame = physics.EventFrame;
            foreach (var e in physics.Events)
            {
                var key = keyOf(e.A, e.B);
                if (e.Type == CollisionEventType.Enter)
                {
                    if (_index.ContainsKey(key) || e.A.IsNull || e.B.IsNull) continue;
                    var trigger = isTrigger(world, e.A) || isTrigger(world, e.B);
                    _index[key] = _pairs.Count;
                    _pairs.Add(new Pair { A = key.Item1, B = key.Item2, Trigger = trigger, EnterFrame = _frame });
                    send(trigger ? CollisionCallbacks.TriggerEnter : CollisionCallbacks.CollisionEnter, key.Item1, key.Item2, trigger);
                }
                else if (_index.TryGetValue(key, out var index))
                {
                    var pair = _pairs[index];
                    removeAt(index);
                    send(pair.Trigger ? CollisionCallbacks.TriggerExit : CollisionCallbacks.CollisionExit, pair.A, pair.B, pair.Trigger);
                }
            }
        }

        // 2. Stay (消えたエンティティの組は外す。Exit は呼ばない : Unity と同じ)
        var stay = (_wanted & (CollisionCallbacks.CollisionStay | CollisionCallbacks.TriggerStay)) != 0;
        for (int i = _pairs.Count - 1; i >= 0; i--)
        {
            var pair = _pairs[i];
            if (!world.IsAlive(pair.A) || !world.IsAlive(pair.B)) { removeAt(i); continue; }
            if (stay && pair.EnterFrame < _frame)
                send(pair.Trigger ? CollisionCallbacks.TriggerStay : CollisionCallbacks.CollisionStay, pair.A, pair.B, pair.Trigger);
        }

        foreach (var receiver in _receivers) receiver.AfterCollisions(world);
        // 持ち続けない (ゲームの DLL を読み直した時に、古い DLL の System が残って解放されなくなるため。ED7 と同じ)
        _receivers.Clear();
    }

    private void send(CollisionCallbacks phase, Entity a, Entity b, bool trigger)
    {
        if ((_wanted & phase) == 0) return;
        var ab = new Collision(this, a, b, trigger);
        var ba = new Collision(this, b, a, trigger);
        foreach (var receiver in _receivers)
        {
            if ((receiver.Callbacks & phase) == 0) continue;
            receiver.Receive(World, phase, ab);
            receiver.Receive(World, phase, ba);
        }
    }

    // 位置 index の組を外す (最後の組を空いた所へ移す)
    private void removeAt(int index)
    {
        var removed = _pairs[index];
        _index.Remove((removed.A, removed.B));
        var last = _pairs.Count - 1;
        if (index != last)
        {
            var moved = _pairs[last];
            _pairs[index] = moved;
            _index[(moved.A, moved.B)] = index;
        }
        _pairs.RemoveAt(last);
    }

    private static bool isTrigger(World world, Entity entity)
    {
        var pool = world.GetPool<Collider>();
        return world.IsAlive(entity) && pool.Has(entity.Id) && pool.Get(entity.Id).IsTrigger;
    }

    // 接触点 : このフレームで初めて聞かれた時に、今の接触を全部取って組で引ける表にする
    public bool TryGetContact(Entity self, Entity other, out Vector3 point, out Vector3 normal)
    {
        point = normal = Vector3.Zero;
        if (_physics == null) return false;
        if (_contactFrame != _frame)
        {
            _contactFrame = _frame;
            _contactIndex.Clear();
            _physics.GetContacts(_contacts);
            for (int i = 0; i < _contacts.Count; i++) _contactIndex.TryAdd(keyOf(_contacts[i].A, _contacts[i].B), i);
        }
        if (!_contactIndex.TryGetValue(keyOf(self, other), out var index)) return false;
        var contact = _contacts[index];
        point = contact.Position;
        // Contact の法線は A → B。自分が押し返される向き (相手 → 自分) にする
        normal = contact.A == self ? -contact.Normal : contact.Normal;
        return true;
    }
}
