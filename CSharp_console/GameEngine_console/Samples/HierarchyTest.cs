using System.Numerics;
using GameEngine;

namespace GameEngine_console;

// ==========================================================================
// 親子と有効 / 無効のテスト (--hierarchy-test。ED2)
//   フレームを進めながら確かめて、結果をコンソールに出して終わる (失敗があれば終了コード 1)
// ==========================================================================
internal sealed class HierarchyTest : ISystem
{
    private readonly Engine _engine;
    private readonly Model  _box;
    private int _frame, _pass, _fail;
    private Entity _parent, _child, _grandChild, _moved, _support, _body, _light, _counter;
    private int _bodiesBefore;
    private double _fixedStart;

    public HierarchyTest(Engine engine, Model box)
    {
        _engine = engine;
        _box    = box;
    }

    private void check(string name, bool ok, string detail = "")
    {
        if (ok) _pass++; else _fail++;
        Console.WriteLine($"[Test] {(ok ? "ok  " : "FAIL")} {name}{(detail == "" ? "" : " : " + detail)}");
    }

    private static bool near(Vector3 a, Vector3 b, float eps = 1e-4f) => Vector3.Distance(a, b) <= eps;
    private static string v(Vector3 a) => $"({a.X:F4}, {a.Y:F4}, {a.Z:F4})";

    public void Update(World world)
    {
        var transforms = world.GetPool<Transform>();
        // 剛体の確認は、物理の時間 (固定の刻み) で 2.5 秒たってから (フレーム数だと速い PC では落ちきらない)
        if (_frame == 9 && Time.FixedTime - _fixedStart < 2.5) return;
        if (_frame == 1) _fixedStart = Time.FixedTime;
        switch (_frame++)
        {
        case 0:
        {
            // 床 / 親 (回転 90 度 + 大きさ 2) / 子 / 孫
            var ground = world.CreateEntity("Ground");
            world.Add(ground, new Transform(new Vector3(0, -0.5f, 0)) { Scale = new Vector3(30, 0.5f, 30) });
            world.Add(ground, Collider.Box(Vector3.One));
            world.Add(ground, new Static());

            _parent = world.CreateEntity("Parent");
            world.Add(_parent, new Transform(new Vector3(10, 0, 0)) { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, MathF.PI * 0.5f), Scale = new Vector3(2) });
            world.Add(_parent, new MeshRenderer(_box));
            _child = world.CreateEntity("Child");
            world.Add(_child, new Transform(new Vector3(1, 0, 0)) { Parent = _parent, Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.3f) });
            world.Add(_child, new MeshRenderer(_box));
            world.Add(_child, Collider.Sphere(0.25f));
            world.Add(_child, new Static());
            _grandChild = world.CreateEntity("Grandchild");
            world.Add(_grandChild, new Transform(new Vector3(0, 1, 0.5f)) { Parent = _child, Scale = new Vector3(0.5f) });
            world.Add(_grandChild, new PointLight());
            world.Add(_grandChild, new Spin { DegreesPerSecond = 0 });
            _light = _grandChild;

            // 親を変える (ワールドの位置を保つ)
            _moved = world.CreateEntity("Moved");
            world.Add(_moved, new Transform(new Vector3(3, 4, 5)) { Rotation = Quaternion.CreateFromYawPitchRoll(0.4f, 0.2f, 0.1f), Scale = new Vector3(1.5f) });

            // 剛体の子 : 回した親の下に置いた箱が、ワールドで床に落ちる
            _support = world.CreateEntity("Rigid parent");
            world.Add(_support, new Transform(new Vector3(-5, 3, 0)) { Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitZ, 0.5f) });
            _body = world.CreateEntity("Rigid child");
            world.Add(_body, new Transform(new Vector3(0, 1, 0)) { Parent = _support });
            world.Add(_body, Collider.Box(new Vector3(0.5f)));
            world.Add(_body, Rigidbody.Dynamic());

            _counter = world.CreateEntity("Counter");
            world.Add(_counter, new Transform { Parent = _parent });
            world.Add(_counter, new Spin());
            break;
        }
        case 2:
        {
            // ワールドの位置 = ローカルの行列を親から順に掛けた物
            ref var c = ref transforms.Get(_child.Id);
            ref var g = ref transforms.Get(_grandChild.Id);
            var p = transforms.Get(_parent.Id);
            var expectedChild = p.LocalMatrix;
            expectedChild = c.LocalMatrix * expectedChild;
            var expectedGrand = g.LocalMatrix * expectedChild;
            check("child world position = local × parent", near(c.WorldPosition, expectedChild.Translation), $"{v(c.WorldPosition)} vs {v(expectedChild.Translation)}");
            check("grandchild world position (3 levels)", near(g.WorldPosition, expectedGrand.Translation), $"{v(g.WorldPosition)} vs {v(expectedGrand.Translation)}");
            check("world matrix sent to renderer is the chained matrix", Matrix4x4Equal(g.Matrix, expectedGrand));
            check("world scale = parent scale × local scale", near(g.WorldScale, new Vector3(1.0f)), v(g.WorldScale));
            check("Hierarchy.GetWorldMatrix matches", Matrix4x4Equal(Hierarchy.GetWorldMatrix(world, _grandChild), expectedGrand));
            check("children list", Hierarchy.GetChildren(world, _parent).Count == 2 && Hierarchy.GetChildren(world, _child).Count == 1);

            // 親を変える : ワールドの値を保つ / 輪になる親子は拒む
            var before = Hierarchy.GetWorldMatrix(world, _moved);
            check("SetParent keeps world transform", Hierarchy.SetParent(world, _moved, _child) && Matrix4x4Equal(Hierarchy.GetWorldMatrix(world, _moved), before, 1e-4f));
            check("SetParent refuses a cycle", !Hierarchy.SetParent(world, _parent, _grandChild));

            // 親を動かす
            transforms.Get(_parent.Id).Position = new Vector3(10, 2, 0);
            _bodiesBefore = _engine.Physics.Bodies;
            break;
        }
        case 4:
        {
            ref var c = ref transforms.Get(_child.Id);
            ref var m = ref transforms.Get(_moved.Id);
            var expected = c.LocalMatrix * transforms.Get(_parent.Id).LocalMatrix;
            check("moving the parent moves the child", near(c.WorldPosition, expected.Translation) && c.WorldPosition.Y > 1.9f, v(c.WorldPosition));
            check("re-parented entity follows its new parent", near(m.WorldPosition, (m.LocalMatrix * Hierarchy.GetWorldMatrix(world, _child)).Translation));

            // 無効にする : 子 / 孫も無効。描画 / 物理 / ForEach から外れる
            world.SetActive(_parent, false);
            break;
        }
        case 6:
        {
            var renderer = world.Get<MeshRenderer>(_child);
            var spins = 0;
            world.ForEach((Entity _, ref Spin _) => spins++);
            check("inactive parent makes children inactive", !world.IsActive(_parent) && !world.IsActive(_child) && !world.IsActive(_grandChild) && world.IsActive(_body));
            check("inactive renderer is destroyed", !renderer.IsCreated);
            check("inactive collider body is destroyed", _engine.Physics.Bodies == _bodiesBefore - 1, $"bodies {_engine.Physics.Bodies} (was {_bodiesBefore})");
            check("ForEach skips inactive entities", spins == 0, $"{spins} visited");
            world.SetActive(_parent, true);
            break;
        }
        case 8:
        {
            var spins = 0;
            world.ForEach((Entity _, ref Spin _) => spins++);
            check("re-activated entities come back", world.IsActive(_child) && world.Get<MeshRenderer>(_child).IsCreated && _engine.Physics.Bodies == _bodiesBefore && spins == 2,
                  $"bodies {_engine.Physics.Bodies}, spins {spins}");
            break;
        }
        case 9:
        {
            // 剛体の子 : 物理の位置 (ワールド) と Transform のワールドの値が同じ / ローカルは親から見た値
            ref var t = ref transforms.Get(_body.Id);
            var rb = world.Get<Rigidbody>(_body);
            var parentMatrix = Hierarchy.GetWorldMatrix(world, _support);
            var fromLocal = Vector3.Transform(t.Position, parentMatrix);
            check("rigidbody child falls in world space", MathF.Abs(rb.Position.Y - 0.5f) < 0.02f, $"rigidbody y {rb.Position.Y:F4}");
            check("rigidbody child : transform world = physics, local = inverse(parent) × world",
                  near(t.WorldPosition, rb.Position, 1e-3f) && near(fromLocal, rb.Position, 1e-3f), $"world {v(t.WorldPosition)} physics {v(rb.Position)} from local {v(fromLocal)}");

            // 保存 → 読み込み : 親子が保たれる
            var json = SceneSerializer.Save(world);
            var loaded = new World();
            SceneSerializer.Load(loaded, json, _engine.Assets);
            Entity find(World w, string name) => w.Entities.First(e => w.Get<EntityInfo>(e).Name == name);
            var lg = find(loaded, "Grandchild");
            check("hierarchy survives save / load", Matrix4x4Equal(Hierarchy.GetWorldMatrix(loaded, lg), Hierarchy.GetWorldMatrix(world, _grandChild), 1e-4f) &&
                  loaded.Get<Transform>(lg).Parent == find(loaded, "Child"));
            break;
        }
        case 10:
            Console.WriteLine($"[Test] hierarchy : pass {_pass} / fail {_fail}");
            Environment.ExitCode = _fail == 0 ? 0 : 1;
            _engine.Quit();
            break;
        }
    }

    private static bool Matrix4x4Equal(Matrix4x4 a, Matrix4x4 b, float eps = 1e-5f)
    {
        for (int r = 0; r < 4; r++)
            for (int c = 0; c < 4; c++)
                if (MathF.Abs(a[r, c] - b[r, c]) > eps) return false;
        return true;
    }
}
