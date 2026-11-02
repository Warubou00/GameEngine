using System.Numerics;
using GameEngine.Native;

namespace GameEngine;

// ==========================================================================
// Collider を C++ (Physics.dll) の体へ反映して、重なりを調べる (エンジン組み込み。TransformSystem の後)
//   1. まだ体の無い Collider は作る (形 / 大きさ / レイヤー / Static を変えた時は作り直す)
//   2. 動いた物 (Transform.Changed かつ行列が前に送った物と違う) だけ、位置を配列でまとめて 1 回で送る
//   3. Physics_Step で重なりを調べ、イベントを受け取って engine.Physics.Events に入れる
//   4. 外された / 削除された Collider の体は次の更新で消す
//   体の状態はエンティティの番号で引く表に持つ (AudioSystem と同じ)
// ==========================================================================
internal sealed unsafe class CollisionSystem
{
    private struct BodyState
    {
        public uint          Body;
        public ColliderShape Shape;
        public Vector3       Size;
        public Vector3       Center;
        public uint          Mesh;
        public int           Layer;
        public uint          Mask;
        public bool          IsStatic;
        public Matrix4x4     Sent;       // 最後に送った行列 (同じなら送らない)
        public int           Overlaps;   // 今重なっている相手の数 (確認用の色)
        public bool          Active;
    }

    private readonly PhysicsSettings _settings;
    private readonly bool _enabled;
    private BodyState[] _states = new BodyState[64];      // エンティティの番号 → 体
    private Entity[]    _bodyEntities = new Entity[64];   // 体の番号 (ハンドルの下位 16bit) → エンティティ
    private uint[]      _bodyHandles  = new uint[64];
    private readonly List<uint> _pendingDestroy = new();

    // 毎フレームの割り当てを避けるため、配列は使い回す
    private PhysicsNativeMethods.BodyTransform[]  _batch  = new PhysicsNativeMethods.BodyTransform[64];
    private PhysicsNativeMethods.CollisionEvent[] _events = new PhysicsNativeMethods.CollisionEvent[64];

    public CollisionSystem(World world, PhysicsSettings settings, bool enabled)
    {
        _settings = settings;
        _enabled  = enabled;
        _settings.EntityOf = entityOf;
        world.GetPool<Collider>().Removed += (entity, _) =>
        {
            if ((uint)entity.Id >= (uint)_states.Length) return;
            ref var state = ref _states[entity.Id];
            if (state.Active) _pendingDestroy.Add(state.Body);
            state = default;
        };
    }

    public void Update(World world)
    {
        _settings.EventList.Clear();
        if (!_enabled) return;
        var start = System.Diagnostics.Stopwatch.GetTimestamp();

        foreach (var body in _pendingDestroy) PhysicsNativeMethods.Physics_DestroyBody(body);
        _pendingDestroy.Clear();

        if (_settings.BroadphaseDirty)
        {
            PhysicsNativeMethods.Physics_SetBroadphase((uint)_settings.Broadphase);
            _settings.BroadphaseDirty = false;
        }

        var colliders  = world.GetPool<Collider>();
        var transforms = world.GetPool<Transform>();
        var statics    = world.GetPool<Static>();
        var entities   = colliders.Entities;
        var components = colliders.Components;
        var batchCount = 0;

        for (int i = 0; i < components.Length; i++)
        {
            var entity = entities[i];
            ref var transform = ref transforms.TryGet(entity.Id, out var found);
            if (!found) continue;
            ref var collider = ref components[i];
            if (entity.Id >= _states.Length) Array.Resize(ref _states, Math.Max(_states.Length * 2, entity.Id + 1));
            ref var state = ref _states[entity.Id];
            var isStatic = statics.Has(entity.Id);
            var mesh     = collider.Shape == ColliderShape.Mesh && collider.Mesh != null ? collider.Mesh.Handle : 0u;

            var changedShape = !state.Active || state.Shape != collider.Shape || state.Size != collider.Size || state.Center != collider.Center ||
                               state.Mesh != mesh || state.Layer != collider.Layer || state.Mask != collider.CollideWith || state.IsStatic != isStatic;
            if (changedShape)
            {
                if (state.Active) PhysicsNativeMethods.Physics_DestroyBody(state.Body);
                var desc = new PhysicsNativeMethods.BodyDesc
                {
                    Shape       = (uint)collider.Shape,
                    Layer       = (uint)Math.Clamp(collider.Layer, 0, 31),
                    CollideMask = collider.CollideWith,
                    IsStatic    = isStatic ? 1u : 0u,
                    Center      = collider.Center,
                    Mesh        = mesh,
                    Size        = collider.Size,
                    Position    = transform.Position,
                    Rotation    = transform.Rotation,
                    Scale       = transform.Scale,
                };
                var body = PhysicsNativeMethods.Physics_CreateBody(ref desc);
                state = new BodyState
                {
                    Body = body, Shape = collider.Shape, Size = collider.Size, Center = collider.Center, Mesh = mesh, Layer = collider.Layer,
                    Mask = collider.CollideWith, IsStatic = isStatic, Sent = transform.LocalToWorld, Active = body != PhysicsNativeMethods.InvalidHandle,
                };
                if (state.Active)
                {
                    var index = (int)(body & 0xFFFF);
                    if (index >= _bodyEntities.Length)
                    {
                        Array.Resize(ref _bodyEntities, Math.Max(_bodyEntities.Length * 2, index + 1));
                        Array.Resize(ref _bodyHandles,  _bodyEntities.Length);
                    }
                    _bodyEntities[index] = entity;
                    _bodyHandles[index]  = body;
                }
                continue;
            }

            // 動いた物だけ送る (Static で描画しない物は Changed が下りないので、行列でも比べる)
            if (!transform.Changed || transform.LocalToWorld == state.Sent) continue;
            state.Sent = transform.LocalToWorld;
            if (batchCount == _batch.Length) Array.Resize(ref _batch, _batch.Length * 2);
            _batch[batchCount++] = new PhysicsNativeMethods.BodyTransform
            {
                Handle = state.Body, Position = transform.Position, Rotation = transform.Rotation, Scale = transform.Scale,
            };
        }

        if (batchCount > 0)
        {
            fixed (PhysicsNativeMethods.BodyTransform* items = _batch)
            {
                PhysicsNativeMethods.Physics_SetTransforms(items, (uint)batchCount);
            }
        }

        // 重なりを調べて、イベントを受け取る
        var eventCount = (int)PhysicsNativeMethods.Physics_Step();
        if (eventCount > 0)
        {
            if (eventCount > _events.Length) Array.Resize(ref _events, Math.Max(_events.Length * 2, eventCount));
            fixed (PhysicsNativeMethods.CollisionEvent* events = _events)
            {
                eventCount = (int)PhysicsNativeMethods.Physics_GetEvents(events, (uint)_events.Length);
            }
            for (int i = 0; i < eventCount; i++)
            {
                ref var e = ref _events[i];
                var a = entityOf(e.BodyA);
                var b = entityOf(e.BodyB);
                var type = (CollisionEventType)e.Type;
                _settings.EventList.Add(new CollisionEvent(a, b, type));
                addOverlap(a, e.BodyA, type == CollisionEventType.Enter ? 1 : -1);
                addOverlap(b, e.BodyB, type == CollisionEventType.Enter ? 1 : -1);
            }
        }

        PhysicsNativeMethods.Physics_GetStats(out var stats);
        _settings.Bodies        = (int)stats.Bodies;
        _settings.MovedBodies   = (int)stats.MovedBodies;
        _settings.Candidates    = (int)stats.Candidates;
        _settings.Overlaps      = (int)stats.Overlaps;
        _settings.BroadphaseMs  = stats.BroadphaseMs;
        _settings.NarrowphaseMs = stats.NarrowphaseMs;
        _settings.StepMs        = stats.StepMs;
        _settings.SyncMs        = stats.SyncMs;
        _settings.TreeMs        = stats.TreeMs;
        _settings.EventsMs      = stats.EventsMs;
        _settings.Workers       = (int)stats.Workers;
        _settings.SystemMs      = (float)System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;   // 表示の線を描く前まで

        if (_settings.ShowColliders) drawColliders(world);
        if (_settings.ShowContacts)  drawContacts();
    }

    private readonly List<Contact> _contacts = new();

    // 確認用 : 接触点 (黄色の小さな十字) と法線 (A → B の向きに 0.3 の線)
    private void drawContacts()
    {
        var pointColor  = DebugDraw.Color(1.0f, 0.9f, 0.1f);
        var normalColor = DebugDraw.Color(1.0f, 0.6f, 0.1f);
        _settings.GetContacts(_contacts);
        foreach (var c in _contacts)
        {
            const float s = 0.05f;
            DebugDraw.Line(c.Position - Vector3.UnitX * s, c.Position + Vector3.UnitX * s, pointColor);
            DebugDraw.Line(c.Position - Vector3.UnitY * s, c.Position + Vector3.UnitY * s, pointColor);
            DebugDraw.Line(c.Position - Vector3.UnitZ * s, c.Position + Vector3.UnitZ * s, pointColor);
            DebugDraw.Line(c.Position, c.Position + c.Normal * 0.3f, normalColor);
        }
    }

    private Entity entityOf(uint body)
    {
        var index = (int)(body & 0xFFFF);
        return index < _bodyHandles.Length && _bodyHandles[index] == body ? _bodyEntities[index] : default;
    }

    private void addOverlap(Entity entity, uint body, int delta)
    {
        if ((uint)entity.Id >= (uint)_states.Length) return;
        ref var state = ref _states[entity.Id];
        if (state.Active && state.Body == body) state.Overlaps = Math.Max(0, state.Overlaps + delta);
    }

    // 確認用 : コライダーを線で描く (C++ と同じ計算でワールドの形にする)
    private void drawColliders(World world)
    {
        var overlapColor = DebugDraw.Color(1.0f, 0.25f, 0.2f);
        var freeColor    = DebugDraw.Color(0.3f, 1.0f, 0.4f);
        var staticColor  = DebugDraw.Color(0.85f, 0.85f, 0.85f, 0.9f);
        var colliders  = world.GetPool<Collider>();
        var transforms = world.GetPool<Transform>();
        var entities   = colliders.Entities;
        var components = colliders.Components;
        for (int i = 0; i < components.Length; i++)
        {
            ref var transform = ref transforms.TryGet(entities[i].Id, out var found);
            if (!found || (uint)entities[i].Id >= (uint)_states.Length) continue;
            ref var state = ref _states[entities[i].Id];
            if (!state.Active) continue;
            ref var c = ref components[i];
            var color  = state.Overlaps > 0 ? overlapColor : state.IsStatic ? staticColor : freeColor;
            var scale  = Vector3.Abs(transform.Scale);
            var center = transform.Position + Vector3.Transform(c.Center * transform.Scale, transform.Rotation);
            switch (c.Shape)
            {
            case ColliderShape.Sphere:
                DebugDraw.WireSphere(center, c.Size.X * MathF.Max(MathF.Max(scale.X, scale.Y), scale.Z), color);
                break;
            case ColliderShape.Box:
                DebugDraw.WireBox(center, transform.Rotation, c.Size * scale, color);
                break;
            case ColliderShape.Capsule:
                DebugDraw.WireCapsule(center, transform.Rotation, c.Size.X * MathF.Max(scale.X, scale.Z), c.Size.Y * scale.Y, color);
                break;
            case ColliderShape.Mesh:
                // メッシュは三角形が多いので、形は描かない (モデルそのものが見えている)。位置に小さな印だけ
                DebugDraw.WireBox(transform.Position, transform.Rotation, new Vector3(0.1f), color);
                break;
            }
        }
    }
}
