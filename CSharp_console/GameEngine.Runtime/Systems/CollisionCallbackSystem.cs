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
//   v1.9 : CompoundCollider (コリジョンアセット) の部品は、部品の組ごとに 1 つの組 (Collision.SelfPart / OtherPart で分かる)
//          組の鍵は (エンティティ, 部品の番号) の 2 つ (以前はエンティティの 2 つ)
// ==========================================================================
internal sealed class CollisionCallbackSystem
{
    private struct Pair
    {
        public Entity A, B;
        public ColliderPart PartA, PartB;   // v1.9
        public bool   Trigger;
        public int    EnterFrame;
    }

    // 組の鍵 : (エンティティ, 部品の番号 (ふつうの Collider は -1)) の小さい方が先
    private readonly record struct PairKey(Entity A, int CodeA, Entity B, int CodeB);

    private readonly List<Pair> _pairs = new();
    private readonly Dictionary<PairKey, int> _index = new();   // 組 → _pairs の位置
    private readonly List<ICollisionReceiver> _receivers = new();
    private int _lastEventFrame = -1;
    private int _frame;
    private CollisionCallbacks _wanted;

    // 接触点 (聞かれた時に作る)
    private readonly List<Contact> _contacts = new();
    private readonly Dictionary<PairKey, int> _contactIndex = new();
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

    private static bool firstIs(Entity a, int codeA, Entity b, int codeB) =>
        a.Id != b.Id ? a.Id < b.Id : a.Version != b.Version ? a.Version < b.Version : codeA <= codeB;

    // 鍵 (swapped : a / b を入れ替えた)
    private static PairKey keyOf(Entity a, ColliderPart pa, Entity b, ColliderPart pb, out bool swapped)
    {
        swapped = !firstIs(a, pa.Code, b, pb.Code);
        return swapped ? new PairKey(b, pb.Code, a, pa.Code) : new PairKey(a, pa.Code, b, pb.Code);
    }

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
                var key = keyOf(e.A, e.PartA, e.B, e.PartB, out var swapped);
                if (e.Type == CollisionEventType.Enter)
                {
                    if (_index.ContainsKey(key) || e.A.IsNull || e.B.IsNull) continue;
                    var trigger = isTrigger(world, e.A, e.PartA) || isTrigger(world, e.B, e.PartB);
                    var pair = swapped
                        ? new Pair { A = e.B, PartA = e.PartB, B = e.A, PartB = e.PartA, Trigger = trigger, EnterFrame = _frame }
                        : new Pair { A = e.A, PartA = e.PartA, B = e.B, PartB = e.PartB, Trigger = trigger, EnterFrame = _frame };
                    _index[key] = _pairs.Count;
                    _pairs.Add(pair);
                    send(trigger ? CollisionCallbacks.TriggerEnter : CollisionCallbacks.CollisionEnter, pair);
                }
                else if (_index.TryGetValue(key, out var index))
                {
                    var pair = _pairs[index];
                    removeAt(index);
                    send(pair.Trigger ? CollisionCallbacks.TriggerExit : CollisionCallbacks.CollisionExit, pair);
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
                send(pair.Trigger ? CollisionCallbacks.TriggerStay : CollisionCallbacks.CollisionStay, pair);
        }

        foreach (var receiver in _receivers) receiver.AfterCollisions(world);
        // 持ち続けない (ゲームの DLL を読み直した時に、古い DLL の System が残って解放されなくなるため。ED7 と同じ)
        _receivers.Clear();
    }

    private void send(CollisionCallbacks phase, in Pair pair)
    {
        if ((_wanted & phase) == 0) return;
        var ab = new Collision(this, pair.A, pair.B, pair.Trigger, pair.PartA, pair.PartB);
        var ba = new Collision(this, pair.B, pair.A, pair.Trigger, pair.PartB, pair.PartA);
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
        _index.Remove(new PairKey(removed.A, removed.PartA.Code, removed.B, removed.PartB.Code));
        var last = _pairs.Count - 1;
        if (index != last)
        {
            var moved = _pairs[last];
            _pairs[index] = moved;
            _index[new PairKey(moved.A, moved.PartA.Code, moved.B, moved.PartB.Code)] = index;
        }
        _pairs.RemoveAt(last);
    }

    private static bool isTrigger(World world, Entity entity, ColliderPart part)
    {
        if (part.IsPart) return part.IsTrigger;   // v1.9 : コリジョンアセットの部品はノードの値
        var pool = world.GetPool<Collider>();
        return world.IsAlive(entity) && pool.Has(entity.Id) && pool.Get(entity.Id).IsTrigger;
    }

    // 接触点 : このフレームで初めて聞かれた時に、今の接触を全部取って組で引ける表にする
    public bool TryGetContact(Entity self, ColliderPart selfPart, Entity other, ColliderPart otherPart, out Vector3 point, out Vector3 normal)
    {
        point = normal = Vector3.Zero;
        if (_physics == null) return false;
        if (_contactFrame != _frame)
        {
            _contactFrame = _frame;
            _contactIndex.Clear();
            _physics.GetContacts(_contacts);
            for (int i = 0; i < _contacts.Count; i++)
            {
                var c = _contacts[i];
                _contactIndex.TryAdd(keyOf(c.A, c.PartA, c.B, c.PartB, out _), i);
            }
        }
        if (!_contactIndex.TryGetValue(keyOf(self, selfPart, other, otherPart, out _), out var index)) return false;
        var contact = _contacts[index];
        point = contact.Position;
        // Contact の法線は A → B。自分が押し返される向き (相手 → 自分) にする
        normal = contact.A == self && contact.PartA.Code == selfPart.Code ? -contact.Normal : contact.Normal;
        return true;
    }
}
