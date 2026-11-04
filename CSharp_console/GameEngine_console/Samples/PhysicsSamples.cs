using System.Numerics;
using GameEngine;

namespace GameEngine_console;

// ==========================================================================
// 物理 (P1) の見本
//   PhysicsDemo  : --scene physics。積み木 / 坂 (摩擦) / 反発 / 振り子の鎖 / ばね / 固く繋いだ物 / 壊れるジョイント /
//                  動く床 (キネマティック) / トリガー。スペースでカメラから球を撃つ。R で積み木を作り直す
//   PhysicsBench : --physics-bench N。箱を N 個落として積む (計測用。[PhysicsBench] を 120 フレームごとに出す)
//   球のモデルは無いので、球はコライダーの線で見る (C で表示)。眠っている剛体は青 (P2)
// ==========================================================================
internal static class PhysicsDemo
{
    // box.obj は x, z : -1〜1 / y : 0〜2。中心 center、半分の大きさ half の箱にする (重心 = 箱の中心)
    public static Entity SpawnBox(World world, Model box, Vector3 center, Vector3 half, Quaternion rotation, Rigidbody? body,
                                  float friction = 0.5f, float restitution = 0.0f)
    {
        var entity = world.CreateEntity();
        var position = center - Vector3.Transform(new Vector3(0, half.Y, 0), rotation);
        world.Add(entity, new Transform(position) { Rotation = rotation, Scale = half });
        world.Add(entity, new MeshRenderer(box));
        var collider = Collider.Box(Vector3.One, new Vector3(0, 1, 0));
        collider.Friction    = friction;
        collider.Restitution = restitution;
        world.Add(entity, collider);
        if (body is { } rb) world.Add(entity, rb);
        else world.Add(entity, new Static());
        return entity;
    }

    public static Entity SpawnSphere(World world, Vector3 center, float radius, Rigidbody body, float friction = 0.5f, float restitution = 0.0f)
    {
        var entity = world.CreateEntity();
        world.Add(entity, new Transform(center));
        var collider = Collider.Sphere(radius);
        collider.Friction    = friction;
        collider.Restitution = restitution;
        world.Add(entity, collider);
        world.Add(entity, body);
        return entity;
    }

    public static void Spawn(Engine engine, Model box)
    {
        var world = engine.World;
        var identity = Quaternion.Identity;

        // ---------- 地面 (上の面が y = 0) ----------
        SpawnBox(world, box, new Vector3(0, -0.5f, 0), new Vector3(30, 0.5f, 30), identity, null, friction: 0.6f);

        // ---------- 積み木 : ピラミッド (下 8 段) と塔 (12 段) ----------
        SpawnStacks(world, box);

        // ---------- 坂 (25 度) : 摩擦 0.05 / 0.3 / 0.8 の箱 (0.8 は止まる) と、転がる球 ----------
        var slope = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 25.0f * MathEx.Deg2Rad);
        SpawnBox(world, box, new Vector3(8, 2.0f, 0), new Vector3(5, 0.2f, 4), slope, null, friction: 1.0f);
        var up = Vector3.Transform(Vector3.UnitY, slope);
        var along = Vector3.Transform(Vector3.UnitX, slope);
        var top = new Vector3(8, 2.0f, 0) + along * 3.5f + up * 0.2f;
        float[] frictions = { 0.05f, 0.3f, 0.8f };
        for (int i = 0; i < frictions.Length; i++)
        {
            SpawnBox(world, box, top + up * 0.3f + new Vector3(0, 0, -2.5f + i * 1.5f), new Vector3(0.3f), slope, Rigidbody.Dynamic(), friction: frictions[i]);
        }
        SpawnSphere(world, top + up * 0.4f + new Vector3(0, 0, 2.5f), 0.4f, Rigidbody.Dynamic(), friction: 0.8f);

        // ---------- 反発 : 0 / 0.5 / 0.9 の球を 5 m から落とす ----------
        float[] bounces = { 0.0f, 0.5f, 0.9f };
        for (int i = 0; i < bounces.Length; i++)
        {
            SpawnSphere(world, new Vector3(-2 + i * 2, 5, 9), 0.4f, Rigidbody.Dynamic(), restitution: bounces[i]);
        }

        // ---------- 振り子の鎖 (Ball × 8。カプセル) : ワールドの点から吊るして、横に引いた所から放す ----------
        var anchor = new Vector3(-12, 9, -6);
        Entity previous = Entity.Null;
        for (int i = 0; i < 8; i++)
        {
            var link = world.CreateEntity();
            world.Add(link, new Transform(anchor + new Vector3(0.25f + i * 0.5f, 0, 0)) { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, MathF.PI * 0.5f) });
            world.Add(link, Collider.Capsule(0.08f, 0.17f));
            world.Add(link, new Rigidbody { Mass = 0.5f });
            // カプセルのローカルの Y が鎖の向き (90 度回して横に寝かせてある)
            world.Add(link, previous.IsNull
                ? Joint.Ball(Entity.Null, new Vector3(0, 0.25f, 0), anchor)
                : Joint.Ball(previous, new Vector3(0, 0.25f, 0), new Vector3(0, -0.25f, 0)));
            previous = link;
        }

        // ---------- ばね (Hertz 1.5) で吊るした箱 / 硬い棒の振り子 ----------
        var springBox = SpawnBox(world, box, new Vector3(-6, 5, -6), new Vector3(0.3f), identity, Rigidbody.Dynamic(2.0f));
        world.Add(springBox, Joint.Spring(Entity.Null, new Vector3(0, 1, 0), new Vector3(-6, 9, -6), 3.0f, 1.5f, 0.2f));
        var bob = SpawnSphere(world, new Vector3(-3, 8, -6), 0.3f, Rigidbody.Dynamic());
        world.Add(bob, Joint.Distance(Entity.Null, Vector3.Zero, new Vector3(-3, 9.5f, -9)));

        // ---------- 固く繋いだ物 (Fixed) : 2 つの箱をダンベルのように繋いで落とす ----------
        // 点は 2 つの箱の中心の真ん中 (-0.4, 6.2)。ローカルの点 = (真ん中 - 箱の中心) / 大きさ + (0, 1, 0) (box.obj の中心)
        var left  = SpawnBox(world, box, new Vector3(-1, 6, -6), new Vector3(0.3f), identity, Rigidbody.Dynamic());
        var right = SpawnBox(world, box, new Vector3(0.2f, 6.4f, -6), new Vector3(0.3f), identity, Rigidbody.Dynamic());
        world.Add(left, Joint.Fixed(right, new Vector3(2.0f, 1.0f + 0.2f / 0.3f, 0), new Vector3(-2.0f, 1.0f - 0.2f / 0.3f, 0)));

        // ---------- 壊れるジョイント : 吊るした台 (Ball 2 本、25 N で壊れる) に、上から重い箱を落とす ----------
        var shelf = SpawnBox(world, box, new Vector3(3, 4, -6), new Vector3(1.0f, 0.1f, 0.5f), identity, Rigidbody.Dynamic(1.0f));
        var leftRope = world.CreateEntity();
        world.Add(leftRope, Joint.Rope(Entity.Null, new Vector3(-1, 1, 0), new Vector3(2, 6, -6), 2.0f) with { Body = shelf, BreakForce = 25.0f });
        var rightRope = world.CreateEntity();
        world.Add(rightRope, Joint.Rope(Entity.Null, new Vector3(1, 1, 0), new Vector3(4, 6, -6), 2.0f) with { Body = shelf, BreakForce = 25.0f });
        SpawnBox(world, box, new Vector3(3, 8, -6), new Vector3(0.35f), identity, Rigidbody.Dynamic(5.0f));

        // ---------- 動く床 (キネマティック。FixedUpdate で MovePosition) と、上に乗った箱 ----------
        var platform = SpawnBox(world, box, new Vector3(0, 0.6f, -13), new Vector3(2, 0.15f, 1.5f), identity, Rigidbody.Kinematic(), friction: 0.8f);
        world.Add(platform, new MovingPlatform { Origin = new Vector3(0, 0.45f, -13), Amplitude = 4.0f, Period = 6.0f });   // Transform の位置 (箱の底)
        SpawnBox(world, box, new Vector3(0, 1.1f, -13), new Vector3(0.3f), identity, Rigidbody.Dynamic(), friction: 0.8f);
        SpawnBox(world, box, new Vector3(0.8f, 1.1f, -12.8f), new Vector3(0.25f), identity, Rigidbody.Dynamic(), friction: 0.8f);

        // ---------- トリガー (通った物をコンソールへ。押し戻さない) ----------
        var trigger = world.CreateEntity();
        world.Add(trigger, new Transform(new Vector3(-1, 1.5f, 9)));
        world.Add(trigger, Collider.Box(new Vector3(3, 1.5f, 1)) with { IsTrigger = true });
        world.Add(trigger, new Static());
        world.Add(trigger, new TriggerZone());

        engine.AddSystem(new PhysicsDemoSystem(engine, box));
    }

    public static readonly List<Entity> StackEntities = new();

    public static void SpawnStacks(World world, Model box)
    {
        StackEntities.Clear();
        var identity = Quaternion.Identity;
        const float h = 0.4f;
        for (int row = 0; row < 8; row++)
        {
            for (int i = 0; i < 8 - row; i++)
            {
                var x = -8.0f + (i - (8 - row - 1) * 0.5f) * (2 * h + 0.02f);
                StackEntities.Add(SpawnBox(world, box, new Vector3(x, h + row * 2 * h, 0), new Vector3(h), identity, Rigidbody.Dynamic()));
            }
        }
        for (int i = 0; i < 12; i++)
        {
            StackEntities.Add(SpawnBox(world, box, new Vector3(-2, 0.3f + i * 0.6f, 2), new Vector3(0.3f), identity, Rigidbody.Dynamic()));
        }
    }
}

// 往復する床 (キネマティック)
public struct MovingPlatform : IComponent
{
    public Vector3 Origin;
    public float   Amplitude;
    public float   Period;
}

public struct TriggerZone : IComponent
{
}

// ==========================================================================
// 見本の操作 (Update) と、固定の刻みでの動き (FixedUpdate) を 1 つの System に書いた例
// ==========================================================================
public sealed class PhysicsDemoSystem : ISystem, IFixedSystem
{
    private readonly Engine _engine;
    private readonly Model  _box;
    private bool _shoot;

    public PhysicsDemoSystem(Engine engine, Model box)
    {
        _engine = engine;
        _box    = box;
    }

    public void Update(World world)
    {
        if (Input.GetKeyDown(Keys.Space)) _shoot = true;   // 撃つのは FixedUpdate で (速度を変えるのは物理の前)
        if (Input.GetKeyDown(Keys.R))
        {
            foreach (var entity in PhysicsDemo.StackEntities) world.DestroyEntity(entity);
            PhysicsDemo.SpawnStacks(world, _box);
        }

        // トリガーを通った物
        var triggers = world.GetPool<TriggerZone>();
        foreach (var e in _engine.Physics.Events)
        {
            if (triggers.Has(e.A.Id) || triggers.Has(e.B.Id)) Console.WriteLine($"[Trigger] {e.Type} {(triggers.Has(e.A.Id) ? e.B : e.A)}");
        }
    }

    public void FixedUpdate(World world)
    {
        // 動く床 : 時間で決まる位置へ MovePosition (上に乗った物は摩擦で一緒に動く)
        var t = (float)Time.FixedTime;
        world.ForEach((Entity _, ref Rigidbody body, ref MovingPlatform platform) =>
        {
            var offset = MathF.Sin(t * MathF.Tau / platform.Period) * platform.Amplitude;
            body.MovePosition(platform.Origin + new Vector3(offset, 0, 0));
        });

        if (_shoot)
        {
            _shoot = false;
            // カメラの位置と向き (ForEach の中では物を足さない)
            var origin = Vector3.Zero;
            var forward = Vector3.UnitZ;
            world.ForEach((Entity _, ref Transform camera, ref Camera _) => { origin = camera.Position; forward = camera.Forward; });
            var ball = PhysicsDemo.SpawnSphere(world, origin + forward * 1.5f, 0.3f, Rigidbody.Dynamic(3.0f), restitution: 0.3f);
            // 作った直後は C++ の体がまだ無いが、速度は Rigidbody に書いておけば、体ができた後の刻みで送られる
            world.Get<Rigidbody>(ball).Velocity = forward * 25.0f;
        }
    }
}

// ==========================================================================
// 計測用 : 箱を N 個、格子に並べて落とす (積み重なる)。描画も行う
// ==========================================================================
internal static class PhysicsBench
{
    public static void Spawn(Engine engine, Model box, int count)
    {
        var world = engine.World;
        PhysicsDemo.SpawnBox(world, box, new Vector3(0, -0.5f, 0), new Vector3(40, 0.5f, 40), Quaternion.Identity, null);
        var side = (int)MathF.Ceiling(MathF.Sqrt(count / 10.0f));   // 10 段くらいの山
        for (int i = 0; i < count; i++)
        {
            var layer = i / (side * side);
            var x = (i % side - side * 0.5f) * 0.9f;
            var z = ((i / side) % side - side * 0.5f) * 0.9f;
            PhysicsDemo.SpawnBox(world, box, new Vector3(x + (layer & 1) * 0.2f, 0.5f + layer * 0.85f, z), new Vector3(0.4f),
                                 Quaternion.CreateFromAxisAngle(Vector3.UnitY, i * 0.37f), Rigidbody.Dynamic());
        }
        engine.AddSystem(new PhysicsBenchSystem(engine.Physics));
    }
}

public sealed class PhysicsBenchSystem : ISystem
{
    private readonly PhysicsSettings _physics;
    private double _system, _solver, _step;
    private int _frames, _steps;

    public PhysicsBenchSystem(PhysicsSettings physics) => _physics = physics;

    public void Update(World world)
    {
        // 前のフレームの値 (System は物理の前に動く)
        _system += _physics.SystemMs;
        if (_physics.FixedSteps > 0) { _solver += _physics.SolverMs * _physics.FixedSteps; _step += _physics.StepMs * _physics.FixedSteps; }
        _steps += _physics.FixedSteps;
        if (++_frames < 120) return;
        Console.WriteLine($"[PhysicsBench] rigid {_physics.RigidBodies} awake {_physics.AwakeBodies} islands {_physics.Islands} : per step solver {_solver / Math.Max(1, _steps):F3} ms, collision {_step / Math.Max(1, _steps):F3} ms " +
                          $"(contacts {_physics.ContactConstraints}), C# system {_system / _frames:F3} ms per frame, {_steps} steps / {_frames} frames");
        _system = _solver = _step = 0;
        _frames = _steps = 0;
    }
}
