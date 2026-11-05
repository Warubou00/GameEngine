using System.Numerics;
using GameEngine;

namespace GameEngine_console;

// ==========================================================================
// 衝突判定の見本のゲームのコード (ED7 : GameEngine_console から移した)
// ==========================================================================

// 原点の周りを回る (向きも回す。カプセル / 箱が回転しても判定が合うかを見る)
public struct OrbitMover : IComponent
{
    public float Radius;
    public float Height;
    public float DegreesPerSecond;
    public float Angle;
}

[GameSystem]
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
// レイキャストの見本 : 原点の上から斜め下へレーザーを回す。当たった所に印と法線を描く
//   (地面 / 箱 / 岩のメッシュ / キャラクターのカプセル / 回る物に当たる)
// ==========================================================================
[GameSystem]
public sealed class LaserDemoSystem : ISystem
{
    private readonly PhysicsSettings _physics;
    private float _angle;

    public LaserDemoSystem(PhysicsSettings physics) => _physics = physics;
    public LaserDemoSystem(Engine engine) : this(engine.Physics) { }   // [GameSystem] で作る時 (ED7)

    public void Update(World world)
    {
        if (world.GetPool<OrbitMover>().Count == 0) return;   // 衝突判定の見本 (周回する物) のあるシーンだけ (ED7 : どのシーンでも登録されるので)
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
