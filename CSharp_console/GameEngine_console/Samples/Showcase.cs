using System.Diagnostics;
using System.Numerics;
using GameEngine;

namespace GameEngine_console;

// ==========================================================================
// 複数のモデル・マテリアルを並べた見本のシーン
//   box.obj   : 不透明 (マテリアル 2 つ : 草 / 砂利)
//   rock.obj  : 不透明 (マテリアル 3 つ。同じマテリアルを 2 回使う / テクスチャの拡張子違いを含む)
//   Tomimi.pmx: 半透明 (PMX の材質。不透明な物の後に、材質の順番でブレンドして描く)
// ==========================================================================
internal static class Showcase
{
    public static void Spawn(Engine engine)
    {
        var world = engine.World;

        // ---------- 岩 (小さいモデルなので大きくして、原点の箱の周りに置く) ----------
        var stopwatch = Stopwatch.StartNew();
        var rock = engine.Assets.LoadModel("../Model/obj/rock.obj");
        engine.WaitForGpu();
        engine.ReportProfilerValue("Load rock.obj (ms)", stopwatch.Elapsed.TotalMilliseconds);

        var rocks = new (Vector3 Position, float Yaw, float Scale)[]
        {
            (new Vector3(-4.0f, 0, -1.0f),  20, 4.0f),
            (new Vector3(-6.5f, 0,  3.0f), 130, 6.0f),
            (new Vector3( 3.0f, 0, -2.5f), 250, 3.0f),
            (new Vector3(-1.5f, 0,  5.0f),  75, 5.0f),
        };
        foreach (var (position, yaw, scale) in rocks)
        {
            var entity = world.CreateEntity();
            world.Add(entity, new Transform(position)
            {
                Rotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, yaw * MathEx.Deg2Rad),
                Scale    = new Vector3(scale),
            });
            world.Add(entity, new MeshRenderer(rock));
            world.Add(entity, new Static());
        }

        // ---------- PMX のキャラクター (MMD の 1 単位 ≒ 8cm。身長 20 前後なので小さくして箱の横に立たせる) ----------
        stopwatch.Restart();
        var tomimi = engine.Assets.LoadModel("../Model/pmx/Sour式トミミ/Tomimi.pmx");
        engine.WaitForGpu();
        engine.ReportProfilerValue("Load Tomimi.pmx (ms)", stopwatch.Elapsed.TotalMilliseconds);

        var character = world.CreateEntity();
        world.Add(character, new Transform(new Vector3(4.0f, 0, 1.0f))
        {
            Scale = new Vector3(0.25f),
        });
        world.Add(character, new MeshRenderer(tomimi));
        // ゆっくり回す (マテリアルごとの描画と半透明が、向きを変えても崩れないか見る。SpinSystem は Program が登録する)
        world.Add(character, new Spin { DegreesPerSecond = 20.0f });
    }
}
