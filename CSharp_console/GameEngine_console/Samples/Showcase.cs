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

        // ---------- 点光源 (暖色 / 寒色。PBR のハイライトと距離の減衰を見る) ----------
        var lights = new (Vector3 Position, Vector3 Color, float Intensity, float Range)[]
        {
            (new Vector3( 6.0f, 2.0f, -1.0f), new Vector3(1.0f, 0.55f, 0.25f), 10.0f, 8.0f),
            (new Vector3(-5.5f, 2.5f, -1.5f), new Vector3(0.3f, 0.55f, 1.0f),  10.0f, 9.0f),
        };
        foreach (var (position, color, intensity, range) in lights)
        {
            var light = world.CreateEntity();
            world.Add(light, new Transform(position));
            world.Add(light, new PointLight { Color = color, Intensity = intensity, Range = range });
        }
    }
}
