using System.Numerics;
using GameEngine;

namespace GameEngine_console;

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
