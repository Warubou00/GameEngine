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
internal sealed unsafe partial class CollisionSystem
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
        public float         Friction;
        public float         Restitution;
        public bool          IsTrigger;
        public Matrix4x4     Sent;       // 最後に送った行列 (同じなら送らない)
        public int           Overlaps;   // 今重なっている相手の数 (確認用の色)
        public bool          Active;

        // 剛体 (P1)。0 : 剛体なし / 1 : Dynamic / 2 : Kinematic。剛体の値は _rigidStates (同じ番号) に分けた
        // (コライダーだけの物が多い時に、この表が大きくなって毎フレームの見回りが遅くならないように)
        public uint          Rigid;
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
            if ((uint)entity.Id < (uint)_rigidStates.Length) _rigidStates[entity.Id] = default;
        };
        initializeRigidbodies(world);
    }

    // simulate : 物理の時間を進めるか (エディターの編集中は false。衝突判定の形は作り、重なりは毎フレーム調べる。ED3)
    public void Update(World world, IReadOnlyList<IFixedSystem> fixedSystems, bool simulate = true)
    {
        _settings.EventList.Clear();
        _settings.EventFrame++;
        Time.FixedStepsThisFrame = 0;
        TransformsWritten = 0;
        if (!_enabled)
        {
            // Physics.dll が無い時も、FixedUpdate だけは固定の刻みで呼ぶ
            if (fixedSystems.Count > 0) runFixedSystemsOnly(world, fixedSystems);
            return;
        }
        var start = System.Diagnostics.Stopwatch.GetTimestamp();

        foreach (var body in _pendingDestroy) PhysicsNativeMethods.Physics_DestroyBody(body);
        _pendingDestroy.Clear();

        if (_settings.BroadphaseDirty)
        {
            PhysicsNativeMethods.Physics_SetBroadphase((uint)_settings.Broadphase);
            _settings.BroadphaseDirty = false;
        }
        if (_settings.Solver.Dirty)
        {
            var solver = _settings.Solver.ToNative();
            PhysicsNativeMethods.Physics_SetSolverSettings(ref solver);
            _settings.Solver.Dirty = false;
        }

        var colliders   = world.GetPool<Collider>();
        var transforms  = world.GetPool<Transform>();
        var statics     = world.GetPool<Static>();
        var rigidbodies = world.GetPool<Rigidbody>();
        var hasRigidbodies = rigidbodies.Count > 0;
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

            // 無効な物 (ED2) : 体を消す (有効に戻ると作り直す。繋いだジョイントも作り直される)
            if (!world.IsActive(entity))
            {
                if (state.Active)
                {
                    PhysicsNativeMethods.Physics_DestroyBody(state.Body);
                    state = default;
                    if ((uint)entity.Id < (uint)_rigidStates.Length) _rigidStates[entity.Id] = default;
                }
                continue;
            }
            var isStatic = statics.Has(entity.Id);
            var mesh     = collider.Shape == ColliderShape.Mesh && collider.Mesh != null ? collider.Mesh.Handle : 0u;

            var changedShape = !state.Active || state.Shape != collider.Shape || state.Size != collider.Size || state.Center != collider.Center ||
                               state.Mesh != mesh || state.Layer != collider.Layer || state.Mask != collider.CollideWith || state.IsStatic != isStatic ||
                               state.Friction != collider.Friction || state.Restitution != collider.Restitution || state.IsTrigger != collider.IsTrigger;
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
                    Position    = transform.WorldPosition,   // 以前はローカル (親子は ED2)
                    Rotation    = transform.WorldRotation,
                    Scale       = transform.WorldScale,
                    Friction    = collider.Friction,
                    Restitution = collider.Restitution,
                    Flags       = collider.IsTrigger ? 1u : 0u,
                };
                var body = PhysicsNativeMethods.Physics_CreateBody(ref desc);
                state = new BodyState
                {
                    Body = body, Shape = collider.Shape, Size = collider.Size, Center = collider.Center, Mesh = mesh, Layer = collider.Layer,
                    Mask = collider.CollideWith, IsStatic = isStatic, Friction = collider.Friction, Restitution = collider.Restitution,
                    IsTrigger = collider.IsTrigger, Sent = transform.LocalToWorld, Active = body != PhysicsNativeMethods.InvalidHandle,
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
                    syncRigidbody(entity, ref state, ref transform, rigidbodies);
                }
                continue;
            }
            if (state.Rigid != 0 || hasRigidbodies) syncRigidbody(entity, ref state, ref transform, rigidbodies);

            if (state.Rigid != 0)
            {
                // 剛体 : 物理が書いた位置から自分で動かした時だけ送る (瞬間移動。キネマティックは目標)
                if (!userMovedRigidbody(entity.Id, ref state, ref transform)) continue;
            }
            // 動いた物だけ送る (Static で描画しない物は Changed が下りないので、行列でも比べる)
            else if (!transform.Changed || transform.LocalToWorld == state.Sent) continue;
            state.Sent = transform.LocalToWorld;
            if (batchCount == _batch.Length) Array.Resize(ref _batch, _batch.Length * 2);
            _batch[batchCount++] = new PhysicsNativeMethods.BodyTransform
            {
                Handle = state.Body, Position = transform.WorldPosition, Rotation = transform.WorldRotation, Scale = transform.WorldScale,
            };
        }

        if (batchCount > 0)
        {
            fixed (PhysicsNativeMethods.BodyTransform* items = _batch)
            {
                PhysicsNativeMethods.Physics_SetTransforms(items, (uint)batchCount);
            }
        }

        syncJoints(world);

        // 剛体 / ジョイント / FixedUpdate がある時は固定の刻みで進める (重なりも刻みごとに調べる)
        // 無い時は以前と同じく、毎フレーム重なりだけ調べる
        var useFixedSteps = simulate && (rigidbodies.Count > 0 || world.GetPool<Joint>().Count > 0 || fixedSystems.Count > 0);
        if (useFixedSteps)
        {
            var steps = runFixedSteps(world, fixedSystems);
            Time.FixedStepsThisFrame = steps;
            if (steps == 0 && batchCount > 0) collectEvents((int)PhysicsNativeMethods.Physics_Step());   // 動かした物の重なりはすぐ知らせる
            writeRigidbodyTransforms(world, Time.FixedAlpha);
            readJoints(world);
        }
        else
        {
            collectEvents((int)PhysicsNativeMethods.Physics_Step());
        }
        _settings.FixedSteps = Time.FixedStepsThisFrame;

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
        _settings.SolverMs           = stats.SolverMs;
        _settings.RigidBodies        = (int)stats.RigidBodies;
        _settings.ContactConstraints = (int)stats.ContactConstraints;
        _settings.Joints             = (int)stats.Joints;
        _settings.AwakeBodies        = (int)stats.AwakeBodies;
        _settings.Islands            = (int)stats.Islands;
        _settings.IslandMs           = stats.IslandMs;
        _settings.SystemMs      = (float)System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;   // 表示の線を描く前まで

        if (_settings.ShowColliders) drawColliders(world);
        if (_settings.ShowColliders || _settings.ShowJoints) drawJoints(world);   // ED6 : ジョイントだけも出せる
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

    // イベントを受け取る (固定の刻みが 1 フレームに何回あっても、全部 Events に入る)
    private void collectEvents(int eventCount)
    {
        if (eventCount <= 0) return;
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

    // Physics.dll が無い時 : FixedUpdate だけ
    private void runFixedSystemsOnly(World world, IReadOnlyList<IFixedSystem> fixedSystems)
    {
        var dt = Time.FixedDeltaTime;
        _accumulator += Time.DeltaTime;
        var steps = 0;
        while (_accumulator >= dt && steps < Time.MaxFixedSteps)
        {
            Time.BeginFixedStep();
            foreach (var system in fixedSystems) system.FixedUpdate(world);
            Time.EndFixedStep();
            _accumulator -= dt;
            steps++;
        }
        if (steps == Time.MaxFixedSteps && _accumulator > dt) _accumulator = dt;
        Time.FixedAlpha = Math.Clamp(_accumulator / dt, 0.0f, 1.0f);
        Time.FixedStepsThisFrame = steps;
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
        var sleepColor   = DebugDraw.Color(0.35f, 0.55f, 1.0f);   // 眠っている剛体 (P2)
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
            if (!world.IsActive(entities[i])) continue;
            ref var c = ref components[i];
            var sleeping = state.Rigid != 0 && (uint)entities[i].Id < (uint)_rigidStates.Length && _rigidStates[entities[i].Id].Sleeping;
            var color  = sleeping ? sleepColor : state.Overlaps > 0 ? overlapColor : state.IsStatic ? staticColor : freeColor;
            var worldScale    = transform.WorldScale;
            var worldRotation = transform.WorldRotation;
            var scale  = Vector3.Abs(worldScale);
            var center = transform.WorldPosition + Vector3.Transform(c.Center * worldScale, worldRotation);
            switch (c.Shape)
            {
            case ColliderShape.Sphere:
                DebugDraw.WireSphere(center, c.Size.X * MathF.Max(MathF.Max(scale.X, scale.Y), scale.Z), color);
                break;
            case ColliderShape.Box:
                DebugDraw.WireBox(center, worldRotation, c.Size * scale, color);
                break;
            case ColliderShape.Capsule:
                DebugDraw.WireCapsule(center, worldRotation, c.Size.X * MathF.Max(scale.X, scale.Z), c.Size.Y * scale.Y, color);
                break;
            case ColliderShape.Mesh:
                // メッシュは三角形が多いので、形は描かない (モデルそのものが見えている)。位置に小さな印だけ
                DebugDraw.WireBox(transform.WorldPosition, worldRotation, new Vector3(0.1f), color);
                break;
            }
        }
    }
}
