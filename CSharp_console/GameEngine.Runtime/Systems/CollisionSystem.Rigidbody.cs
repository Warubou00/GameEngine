using System.Numerics;
using GameEngine.Native;

namespace GameEngine;

// ==========================================================================
// 剛体とジョイント (CollisionSystem の続き。P1)
//   1 フレームの流れ (CollisionSystem.Update の中)
//     1. コライダーと一緒に剛体の値を送る (変わった時だけ)。Transform を自分で動かした剛体は瞬間移動 /
//        キネマティックは「次の刻みでそこへ動く」目標にする
//     2. ジョイントを作る / 作り直す
//     3. 固定の時間刻み (Time.FixedDeltaTime) で、足りない分だけ進める (1 フレームで最大 Time.MaxFixedSteps 回)
//          IFixedSystem.FixedUpdate → 力 / 速度の命令を送る → Physics_Simulate → 状態を受け取る
//     4. 剛体の位置を Transform に書く (Interpolate なら、前後の刻みの間を Time.FixedAlpha で補間)
//   剛体もジョイントも IFixedSystem も無い間は、以前と同じく毎フレーム Physics_Step (衝突判定だけ)
// ==========================================================================
internal sealed unsafe partial class CollisionSystem
{
    // 剛体の値 (エンティティの番号で引く。剛体の物だけ使う)
    private struct RigidState
    {
        public bool       DynamicsSent;
        public PhysicsNativeMethods.BodyDynamics Dynamics;
        public bool       Interpolate;
        public bool       UserDriven;     // このフレームで自分で動かしたキネマティック (Transform に書かない)
        public Vector3    PrevPosition, CurPosition, WrittenPosition;      // 補間の前後と、最後に Transform に書いた値
        public Quaternion PrevRotation, CurRotation, WrittenRotation;
    }

    private RigidState[] _rigidStates = new RigidState[64];

    private ref RigidState rigidStateOf(int id)
    {
        if (id >= _rigidStates.Length) Array.Resize(ref _rigidStates, Math.Max(_rigidStates.Length * 2, id + 1));
        return ref _rigidStates[id];
    }

    private struct JointRecord
    {
        public uint  Handle;
        public Joint Sent;
        public uint  BodyA, BodyB;
        public bool  Active;
    }

    private JointRecord[] _jointRecords = new JointRecord[16];   // エンティティの番号 → ジョイント
    private readonly List<uint> _pendingJointDestroy = new();
    private readonly List<PhysicsNativeMethods.BodyCommand> _commandList = new();
    private PhysicsNativeMethods.BodyCommand[] _commands = new PhysicsNativeMethods.BodyCommand[64];
    private PhysicsNativeMethods.BodyState[]   _bodyStates = new PhysicsNativeMethods.BodyState[64];
    private float _accumulator;

    private void initializeRigidbodies(World world)
    {
        world.GetPool<Joint>().Removed += (entity, _) =>
        {
            if ((uint)entity.Id >= (uint)_jointRecords.Length) return;
            ref var record = ref _jointRecords[entity.Id];
            if (record.Active) _pendingJointDestroy.Add(record.Handle);
            record = default;
        };
    }

    // 剛体の値を送る (変わった時だけ)。剛体になった時は補間の位置を今の Transform から始める
    private void syncRigidbody(Entity entity, ref BodyState state, ref Transform transform, ComponentPool<Rigidbody> rigidbodies)
    {
        ref var rb = ref rigidbodies.TryGet(entity.Id, out var has);
        if (!has && state.Rigid == 0) return;   // 剛体でない物 (大半) は何もしない (C++ の既定も「剛体なし」)
        ref var rigid = ref rigidStateOf(entity.Id);
        var motion = has && !state.IsStatic ? (uint)rb.Type : 0u;
        var dynamics = motion == 0 ? default : new PhysicsNativeMethods.BodyDynamics
        {
            Motion = motion, Mass = rb.Mass, LinearDamping = rb.LinearDamping, AngularDamping = rb.AngularDamping,
            GravityScale = rb.GravityScale, Locks = (uint)rb.Constraints, InertiaScale = rb.InertiaScale,
        };
        if (!rigid.DynamicsSent || !dynamics.Same(rigid.Dynamics))
        {
            PhysicsNativeMethods.Physics_SetBodyDynamics(state.Body, ref dynamics);
            rigid.Dynamics     = dynamics;
            rigid.DynamicsSent = true;
            if (motion != 0 && state.Rigid == 0)
            {
                rigid.PrevPosition = rigid.CurPosition = rigid.WrittenPosition = transform.Position;
                rigid.PrevRotation = rigid.CurRotation = rigid.WrittenRotation = transform.Rotation;
                rb._position = transform.Position;
                rb._rotation = transform.Rotation;
            }
            state.Rigid = motion;
        }
        if (has) rigid.Interpolate = rb.Interpolate;
    }

    // 剛体の Transform を自分で書き換えたか (物理が書いた値と違う)。戻り値 : 瞬間移動として送る
    private bool userMovedRigidbody(int id, ref BodyState state, ref Transform transform)
    {
        ref var rigid = ref rigidStateOf(id);
        if (transform.Position == rigid.WrittenPosition && transform.Rotation == rigid.WrittenRotation) return false;
        rigid.WrittenPosition = transform.Position;
        rigid.WrittenRotation = transform.Rotation;
        if (state.Rigid == (uint)RigidbodyType.Kinematic)
        {
            // キネマティック : 次の刻みでそこへ動く (途中の物を押す)。このフレームは Transform をそのまま使う
            _commandList.Add(new PhysicsNativeMethods.BodyCommand
            {
                Handle = state.Body, Flags = PhysicsNativeMethods.CommandKinematicTarget,
                TargetPosition = transform.Position, TargetRotation = transform.Rotation,
            });
            rigid.UserDriven = true;
            return false;
        }
        // Dynamic : 瞬間移動 (速度はそのまま)。補間も今の位置から
        rigid.PrevPosition = rigid.CurPosition = transform.Position;
        rigid.PrevRotation = rigid.CurRotation = transform.Rotation;
        return true;
    }

    private uint bodyHandleOf(World world, Entity entity)
    {
        if (entity.IsNull || !world.IsAlive(entity) || (uint)entity.Id >= (uint)_states.Length) return PhysicsNativeMethods.InvalidHandle;
        ref var state = ref _states[entity.Id];
        return state.Active ? state.Body : PhysicsNativeMethods.InvalidHandle;
    }

    // ジョイントを作る / 作り直す (値か繋ぐ体が変わった時。体を作り直すと C++ では壊れた扱いになるので作り直す)
    private void syncJoints(World world)
    {
        foreach (var handle in _pendingJointDestroy) PhysicsNativeMethods.Physics_DestroyJoint(handle);
        _pendingJointDestroy.Clear();

        var joints   = world.GetPool<Joint>();
        var entities = joints.Entities;
        var items    = joints.Components;
        for (int i = 0; i < items.Length; i++)
        {
            var entity = entities[i];
            ref var joint = ref items[i];
            if (entity.Id >= _jointRecords.Length) Array.Resize(ref _jointRecords, Math.Max(_jointRecords.Length * 2, entity.Id + 1));
            ref var record = ref _jointRecords[entity.Id];

            var bodyA = bodyHandleOf(world, joint.Body.IsNull ? entity : joint.Body);
            var bodyB = joint.ConnectedBody.IsNull ? PhysicsNativeMethods.InvalidHandle : bodyHandleOf(world, joint.ConnectedBody);
            var ready = bodyA != PhysicsNativeMethods.InvalidHandle && (joint.ConnectedBody.IsNull || bodyB != PhysicsNativeMethods.InvalidHandle);
            if (record.Active && record.Sent.SameSettings(joint) && record.BodyA == bodyA && record.BodyB == bodyB) continue;

            if (record.Active) PhysicsNativeMethods.Physics_DestroyJoint(record.Handle);
            record = default;
            if (!ready) continue;   // 体がまだ無い (次のフレームでもう一度)
            var desc = new PhysicsNativeMethods.JointDesc
            {
                Type = (uint)joint.Type, BodyA = bodyA, BodyB = bodyB, Flags = joint.CollideConnected ? 1u : 0u,
                AnchorA = joint.Anchor, AnchorB = joint.ConnectedAnchor,
                Length = joint.Length, MinLength = joint.MinLength, MaxLength = joint.MaxLength,
                Hertz = joint.SpringHertz, DampingRatio = joint.SpringDamping, BreakForce = joint.BreakForce,
            };
            var handle = PhysicsNativeMethods.Physics_CreateJoint(ref desc);
            if (handle == PhysicsNativeMethods.InvalidHandle) continue;
            record = new JointRecord { Handle = handle, Sent = joint, BodyA = bodyA, BodyB = bodyB, Active = true };
            joint._broken = false;
        }
    }

    private void readJoints(World world)
    {
        var joints   = world.GetPool<Joint>();
        var entities = joints.Entities;
        var items    = joints.Components;
        for (int i = 0; i < items.Length; i++)
        {
            var id = entities[i].Id;
            if ((uint)id >= (uint)_jointRecords.Length || !_jointRecords[id].Active) continue;
            if (!PhysicsNativeMethods.Physics_GetJointInfo(_jointRecords[id].Handle, out var info)) continue;
            ref var joint = ref items[i];
            joint._broken = info.Broken != 0;
            joint._force  = info.Force;
            joint._length = info.Length;
        }
    }

    // 力 / 速度 / 目標の命令をまとめて送る (Transform で動かしたキネマティックの目標も)
    private void flushCommands(World world)
    {
        var rigidbodies = world.GetPool<Rigidbody>();
        var entities    = rigidbodies.Entities;
        var items       = rigidbodies.Components;
        for (int i = 0; i < items.Length; i++)
        {
            ref var rb = ref items[i];
            if (!rb._hasCommand) continue;
            var id = entities[i].Id;
            if ((uint)id >= (uint)_states.Length || !_states[id].Active || _states[id].Rigid == 0) continue;   // 体がまだ無い (できてから送る)
            uint flags = 0;
            if (rb._force != Vector3.Zero || rb._torque != Vector3.Zero) flags |= PhysicsNativeMethods.CommandForce;
            if (rb._impulse != Vector3.Zero || rb._angularImpulse != Vector3.Zero) flags |= PhysicsNativeMethods.CommandImpulse;
            if (rb._setVelocity) flags |= PhysicsNativeMethods.CommandSetVelocity;
            var angular = rb._angularVelocity + rb._angularVelocityChange;
            if (rb._setAngularVelocity || rb._angularVelocityChange != Vector3.Zero) flags |= PhysicsNativeMethods.CommandSetAngular;
            if (rb._hasTarget) flags |= PhysicsNativeMethods.CommandKinematicTarget;
            _commandList.Add(new PhysicsNativeMethods.BodyCommand
            {
                Handle = _states[id].Body, Flags = flags,
                Force = rb._force, Torque = rb._torque, Impulse = rb._impulse, AngularImpulse = rb._angularImpulse,
                Velocity = rb._velocity, AngularVelocity = angular,
                TargetPosition = rb._targetPosition, TargetRotation = rb._targetRotation,
            });
            rb.ClearCommands();
        }
        if (_commandList.Count == 0) return;
        if (_commands.Length < _commandList.Count) _commands = new PhysicsNativeMethods.BodyCommand[_commandList.Count * 2];
        _commandList.CopyTo(_commands);
        fixed (PhysicsNativeMethods.BodyCommand* commands = _commands)
        {
            PhysicsNativeMethods.Physics_ApplyCommands(commands, (uint)_commandList.Count);
        }
        _commandList.Clear();
    }

    // 1 刻みの後 : 剛体の状態を受け取る (補間の前後を 1 つずらす)
    private void readBodyStates(World world)
    {
        var count = (int)PhysicsNativeMethods.Physics_GetBodyStates(null, 0);
        if (count == 0) return;
        if (_bodyStates.Length < count) _bodyStates = new PhysicsNativeMethods.BodyState[count * 2];
        fixed (PhysicsNativeMethods.BodyState* states = _bodyStates)
        {
            count = (int)PhysicsNativeMethods.Physics_GetBodyStates(states, (uint)_bodyStates.Length);
        }
        var rigidbodies = world.GetPool<Rigidbody>();
        for (int i = 0; i < count; i++)
        {
            ref var s = ref _bodyStates[i];
            var entity = entityOf(s.Handle);
            if (entity.IsNull || (uint)entity.Id >= (uint)_states.Length) continue;
            ref var rigid = ref rigidStateOf(entity.Id);
            rigid.PrevPosition = rigid.CurPosition;
            rigid.PrevRotation = rigid.CurRotation;
            rigid.CurPosition  = s.Position;
            rigid.CurRotation  = s.Rotation;
            ref var rb = ref rigidbodies.TryGet(entity.Id, out var has);
            if (!has) continue;
            rb._position        = s.Position;
            rb._rotation        = s.Rotation;
            rb._velocity        = s.Velocity;
            rb._angularVelocity = s.AngularVelocity;
            rb._centerOfMass    = s.CenterOfMass;
        }
    }

    // このフレームで剛体の位置を書いた数 (Engine が TransformSystem.ChangedCount に足す。描画へ送る物を探させるため)
    public int TransformsWritten { get; private set; }

    // 剛体の位置を Transform に書く (Interpolate なら前後の刻みの間)
    private void writeRigidbodyTransforms(World world, float alpha)
    {
        TransformsWritten = 0;
        var rigidbodies = world.GetPool<Rigidbody>();
        var transforms  = world.GetPool<Transform>();
        var entities    = rigidbodies.Entities;
        for (int i = 0; i < entities.Length; i++)
        {
            var id = entities[i].Id;
            if ((uint)id >= (uint)_states.Length) continue;
            ref var state = ref _states[id];
            if (!state.Active || state.Rigid == 0) continue;
            ref var rigid = ref rigidStateOf(id);
            if (rigid.UserDriven) { rigid.UserDriven = false; continue; }   // このフレームは自分で動かしたキネマティック
            ref var transform = ref transforms.TryGet(id, out var found);
            if (!found) continue;
            var position = rigid.Interpolate ? Vector3.Lerp(rigid.PrevPosition, rigid.CurPosition, alpha) : rigid.CurPosition;
            var rotation = rigid.Interpolate ? Quaternion.Slerp(rigid.PrevRotation, rigid.CurRotation, alpha) : rigid.CurRotation;
            rigid.WrittenPosition = position;
            rigid.WrittenRotation = rotation;
            if (transform.Position == position && transform.Rotation == rotation) continue;
            transform.Position = position;
            transform.Rotation = rotation;
            transform.UpdateMatrix();   // TransformSystem は先に動いたので、ここで行列を作り直す (描画へ送る印も立つ)
            TransformsWritten++;
        }
    }

    // 確認用 : ジョイントの 2 点を線で結ぶ (黄色。壊れたら赤)
    private void drawJoints(World world)
    {
        var activeColor = DebugDraw.Color(1.0f, 0.85f, 0.2f);
        var brokenColor = DebugDraw.Color(1.0f, 0.2f, 0.2f);
        var transforms = world.GetPool<Transform>();
        var joints     = world.GetPool<Joint>();
        var entities   = joints.Entities;
        var items      = joints.Components;
        for (int i = 0; i < items.Length; i++)
        {
            ref var joint = ref items[i];
            var bodyEntity = joint.Body.IsNull ? entities[i] : joint.Body;
            ref var a = ref transforms.TryGet(bodyEntity.Id, out var hasA);
            if (!hasA) continue;
            var pointA = a.Position + Vector3.Transform(joint.Anchor * a.Scale, a.Rotation);
            var pointB = joint.ConnectedAnchor;
            if (!joint.ConnectedBody.IsNull)
            {
                ref var b = ref transforms.TryGet(joint.ConnectedBody.Id, out var hasB);
                if (!hasB) continue;
                pointB = b.Position + Vector3.Transform(joint.ConnectedAnchor * b.Scale, b.Rotation);
            }
            var color = joint.IsBroken ? brokenColor : activeColor;
            DebugDraw.Line(pointA, pointB, color);
            DebugDraw.WireSphere(pointA, 0.04f, color);
            DebugDraw.WireSphere(pointB, 0.04f, color);
        }
    }

    // 固定の時間刻みで進める。戻り値 : 進めた回数
    private int runFixedSteps(World world, IReadOnlyList<IFixedSystem> fixedSystems)
    {
        var dt = Time.FixedDeltaTime;
        _accumulator += Time.DeltaTime;
        var steps = 0;
        while (_accumulator >= dt && steps < Time.MaxFixedSteps)
        {
            Time.BeginFixedStep();
            foreach (var system in fixedSystems) system.FixedUpdate(world);
            flushCommands(world);
            collectEvents((int)PhysicsNativeMethods.Physics_Simulate(dt));
            readBodyStates(world);
            Time.EndFixedStep();
            _accumulator -= dt;
            steps++;
        }
        // 追いつけない時 (重い / 止まっていた) は、溜まった分を捨てる (どんどん遅れて固まらないように)
        if (steps == Time.MaxFixedSteps && _accumulator > dt) _accumulator = dt;
        Time.FixedAlpha = Math.Clamp(_accumulator / dt, 0.0f, 1.0f);
        return steps;
    }
}
