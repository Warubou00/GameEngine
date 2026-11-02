using System.Numerics;
using GameEngine;

namespace GameEngine_console;

// ==========================================================================
// 衝突判定の見本
//   CollisionDemo   : 見本のシーンの物 (地面・箱・岩・キャラクター) にコライダーを付け、
//                     球 / 箱 / カプセルを周りに回す。重なると線が赤くなる (C でコライダーの表示、B で探し方の切り替え)
//   CollisionBench  : --collision-bench N で、コライダーだけの物を N 個ばらまいて動かし続ける (計測用。描画は無し)
//                     --broadphase bvh|brute|grid で探し方を選ぶ
// ==========================================================================
internal static class CollisionDemo
{
    // 回る物 : (形, 回る半径, 高さ, 速さ (度 / 秒))
    private static readonly (Collider Collider, float Radius, float Height, float Speed)[] Movers =
    {
        (Collider.Sphere(0.5f),                     1.6f, 0.8f,  40.0f),   // 原点の箱をくぐる
        (Collider.Box(new Vector3(0.4f)),           2.6f, 0.6f, -30.0f),
        (Collider.Capsule(0.3f, 0.5f),              4.0f, 1.0f,  25.0f),   // 岩とキャラクターに当たる
        (Collider.Sphere(0.6f),                     4.0f, 0.7f, -35.0f),   // カプセルとすれ違う
        (Collider.Box(new Vector3(0.6f, 0.3f, 0.5f)), 5.3f, 0.9f, 18.0f),   // 回転しても地面に潜らない高さ
    };

    public static void Spawn(World world)
    {
        foreach (var (collider, radius, height, speed) in Movers)
        {
            var entity = world.CreateEntity();
            world.Add(entity, new Transform(new Vector3(radius, height, 0)));
            world.Add(entity, collider);
            world.Add(entity, new OrbitMover { Radius = radius, Height = height, DegreesPerSecond = speed });
        }
    }
}

// 原点の周りを回る (向きも回す。カプセル / 箱が回転しても判定が合うかを見る)
public struct OrbitMover
{
    public float Radius;
    public float Height;
    public float DegreesPerSecond;
    public float Angle;
}

public sealed class OrbitMoverSystem : ISystem
{
    public void Update(World world)
    {
        world.ForEach(static (Entity _, ref Transform transform, ref OrbitMover mover) =>
        {
            mover.Angle += mover.DegreesPerSecond * MathEx.Deg2Rad * Time.DeltaTime;
            transform.Position = new Vector3(MathF.Cos(mover.Angle) * mover.Radius, mover.Height, MathF.Sin(mover.Angle) * mover.Radius);
            transform.Rotation = Quaternion.CreateFromYawPitchRoll(mover.Angle * 1.5f, mover.Angle * 0.7f, 0.0f);
        });
    }
}

// ==========================================================================
// 計測用 : N 個の物を箱の中でまっすぐ動かし、壁で跳ね返す (全部が毎フレーム動く)
// ==========================================================================
public struct BenchMover
{
    public Vector3 Velocity;
    public bool    Moving;   // 止まっている物もある (--bench-moving で割合)
}

internal static class CollisionBench
{
    /// <param name="movingPercent">毎フレーム動く物の割合 (%)。残りは止まっている (止まった物は C++ へ送らない)</param>
    /// <param name="layers">レイヤーの数 (1 なら全部 0)。2 以上なら番号順にレイヤーを振る (違うレイヤーどうしを調べない設定は Program が行う)</param>
    public static float Spawn(World world, int count, int movingPercent = 100, int layers = 1)
    {
        // 密度 : 1 辺 2.5 の立方体に 1 個 (大きさ 0.3〜0.8 なので、いつも少し重なっている物がある)
        var half = MathF.Cbrt(count) * 2.5f * 0.5f;
        var random = new Random(1234);
        for (int i = 0; i < count; i++)
        {
            var entity = world.CreateEntity();
            var position = new Vector3(rand(-half, half), rand(0.5f, 2.0f * half), rand(-half, half));
            world.Add(entity, new Transform(position));
            var size = rand(0.3f, 0.8f);
            var collider = (i & 1) == 0 ? Collider.Sphere(size) : Collider.Box(new Vector3(size, size * 0.7f, size * 0.9f));
            collider.Layer = layers > 1 ? i % layers : 0;
            world.Add(entity, collider);
            var direction = Vector3.Normalize(new Vector3(rand(-1, 1), rand(-1, 1), rand(-1, 1)) + new Vector3(1e-3f));
            world.Add(entity, new BenchMover { Velocity = direction * rand(1.0f, 3.0f), Moving = (i % 100) < movingPercent });
        }

        // 動かない物 : 床
        var floor = world.CreateEntity();
        world.Add(floor, new Transform(new Vector3(0, -0.5f, 0)));
        world.Add(floor, Collider.Box(new Vector3(half + 2.0f, 0.5f, half + 2.0f)));
        world.Add(floor, new Static());
        return half;

        float rand(float min, float max) => min + (float)random.NextDouble() * (max - min);
    }
}

public sealed class BenchMoverSystem : ISystem
{
    private readonly float _half;
    private readonly PhysicsSettings _physics;
    private double _step, _broad, _narrow, _candidates, _overlaps, _sync, _events, _system, _tree;
    private int _frames;

    public BenchMoverSystem(float half, PhysicsSettings physics)
    {
        _half    = half;
        _physics = physics;
    }

    public void Update(World world)
    {
        // 前のフレームの Step の時間を平均して、2 秒ごとにコンソールへ (起動直後の 1 秒は除く)
        if (Time.TotalTime > 1.0)
        {
            _step += _physics.StepMs; _broad += _physics.BroadphaseMs; _narrow += _physics.NarrowphaseMs;
            _candidates += _physics.Candidates; _overlaps += _physics.Overlaps; _frames++;
            _sync += _physics.SyncMs; _events += _physics.EventsMs; _system += _physics.SystemMs; _tree += _physics.TreeMs;
            if (_frames == 120)
            {
                Console.WriteLine($"[Bench] {_physics.Broadphase} bodies {_physics.Bodies} : step {_step / _frames:F3} ms (broad {_broad / _frames:F3} [tree {_tree / _frames:F3}] / narrow {_narrow / _frames:F3} / events {_events / _frames:F3}), " +
                                  $"sync {_sync / _frames:F3}, C# system {_system / _frames:F3}, workers {_physics.Workers}, candidates {_candidates / _frames:F0}, overlaps {_overlaps / _frames:F0}");
                _step = _broad = _narrow = _candidates = _overlaps = _sync = _events = _system = _tree = 0;
                _frames = 0;
            }
        }

        var half = _half;
        var dt   = Time.DeltaTime;
        world.ForEach((Entity _, ref Transform transform, ref BenchMover mover) =>
        {
            if (!mover.Moving) return;
            var p = transform.Position + mover.Velocity * dt;
            var v = mover.Velocity;
            if (p.X < -half || p.X > half)   v.X = -v.X;
            if (p.Y < 0.5f || p.Y > 2 * half) v.Y = -v.Y;
            if (p.Z < -half || p.Z > half)   v.Z = -v.Z;
            mover.Velocity = v;
            transform.Position = Vector3.Clamp(p, new Vector3(-half, 0.5f, -half), new Vector3(half, 2 * half, half));
        });
    }
}

// C : コライダーの表示 / X : 接触点の表示 / B : 探し方 (BVH → 総当たり → グリッド)
public sealed class PhysicsHotkeySystem : ISystem
{
    private readonly PhysicsSettings _physics;
    public PhysicsHotkeySystem(PhysicsSettings physics) => _physics = physics;

    public void Update(World world)
    {
        if (Input.GetKeyDown(Keys.C)) _physics.ShowColliders = !_physics.ShowColliders;
        if (Input.GetKeyDown(Keys.X)) _physics.ShowContacts  = !_physics.ShowContacts;
        if (Input.GetKeyDown(Keys.B))
        {
            _physics.Broadphase = (Broadphase)(((int)_physics.Broadphase + 1) % 3);
            Console.WriteLine($"[Physics] Broadphase : {_physics.Broadphase}");
        }

        // 重なりのイベントをコンソールへ (数が多い計測の時は出さない)
        if (_physics.Events.Count is > 0 and <= 8)
        {
            foreach (var e in _physics.Events) Console.WriteLine($"[Physics] {e.Type} {e.A} {e.B}");
        }
    }

    public static Broadphase? Parse(string name) => name switch
    {
        "bvh"   => Broadphase.Bvh,
        "brute" => Broadphase.Brute,
        "grid"  => Broadphase.Grid,
        _       => null,
    };
}

// ==========================================================================
// レイキャストの見本 : 原点の上から斜め下へレーザーを回す。当たった所に印と法線を描く
//   (地面 / 箱 / 岩のメッシュ / キャラクターのカプセル / 回る物に当たる)
// ==========================================================================
public sealed class LaserDemoSystem : ISystem
{
    private readonly PhysicsSettings _physics;
    private float _angle;

    public LaserDemoSystem(PhysicsSettings physics) => _physics = physics;

    public void Update(World world)
    {
        _angle += 30.0f * MathEx.Deg2Rad * Time.DeltaTime;
        var origin    = new Vector3(0, 4.0f, 0);
        var direction = Vector3.Normalize(new Vector3(MathF.Cos(_angle), -0.45f, MathF.Sin(_angle)));
        var laser     = DebugDraw.Color(1.0f, 0.2f, 0.9f);
        if (_physics.Raycast(origin, direction, out var hit, 30.0f))
        {
            DebugDraw.Line(origin, hit.Point, laser);
            DebugDraw.WireSphere(hit.Point, 0.08f, laser);
            DebugDraw.Line(hit.Point, hit.Point + hit.Normal * 0.6f, DebugDraw.Color(0.2f, 0.9f, 1.0f));
        }
        else
        {
            DebugDraw.Line(origin, origin + direction * 30.0f, DebugDraw.Color(1.0f, 0.2f, 0.9f, 0.4f));
        }
    }
}
