using System.Numerics;
using GameEngine;

namespace GameEngine_console;

// ==========================================================================
// 自分で書くコンポーネントと System の例 (Unity のスクリプトに当たるもの)
//   Spin を付けた物を、Y 軸の周りに回し続ける
// ==========================================================================
public struct Spin : IComponent
{
    public float DegreesPerSecond;
}

[GameSystem]
public sealed class SpinSystem : ISystem
{
    public void Update(World world)
    {
        var angle = Time.DeltaTime * MathEx.Deg2Rad;
        world.ForEach((Entity _, ref Transform transform, ref Spin spin) =>
        {
            transform.Rotation = Quaternion.Normalize(transform.Rotation * Quaternion.CreateFromAxisAngle(Vector3.UnitY, spin.DegreesPerSecond * angle));
        });
    }
}
