using System.Numerics;
using GameEngine;

namespace GameEngine_console;

// ==========================================================================
// v2.2 : 点光源 / スポットライトの見本と計測
//   LightDemo  : --scene lights。床と柱と球。影を落とす点光源 1 つ (暖色) / スポットライト 2 つ (青・赤。首を振る) /
//                影なしの小さな色つきの点光源 16 個 (回る)。平行光源は暗い月明かり
//                --view spot で近くから見る
//   LightBench : --light-bench N。40 m 四方の床に N 個の点光源 (影なし) をばらまいて動かす (計測用)。
//                [LightBench] に 起動 3 秒以降の GPU の時間 (描画パスの合計) を 120 フレームごとに出す
//                --clustered on|off (既定 on) で、クラスター有り / 無しを比べる
//   操作 (両方) : L でクラスターの ON / OFF、K で点光源 / スポットライトの影の ON / OFF (コンソールに状態を出す)
// ==========================================================================
internal static class LightDemo
{
    public static void Spawn(Engine engine, Model box, bool smallLights = true)
    {
        var world = engine.World;
        ProjectSettings.Current.Rendering.AmbientColor = new Vector3(0.03f, 0.035f, 0.05f);   // 暗くして、ライトの光を見やすく (Project Settings の値が毎フレーム上書きするので、そちらを変える)
        var sphere = engine.Assets.LoadPrimitive(PrimitiveShape.Sphere);

        // ---------- 床・柱・球 (box.obj は x, z : -1〜1 / y : 0〜2) ----------
        AddBox(world, box, new Vector3(0, -0.5f, 0), new Vector3(20, 0.25f, 20));
        for (int x = -2; x <= 2; x++)
            for (int z = -2; z <= 2; z++)
                if ((x + z) % 2 == 0 && !(x == 0 && z == 0)) AddBox(world, box, new Vector3(x * 4.0f, 0, z * 4.0f), new Vector3(0.4f, 1.2f, 0.4f));
        for (int i = 0; i < 6; i++)
        {
            var a = i * MathF.Tau / 6;
            var entity = world.CreateEntity($"Sphere {i}");
            world.Add(entity, new Transform(new Vector3(MathF.Cos(a) * 2.2f, 0.5f, MathF.Sin(a) * 2.2f)) { Scale = new Vector3(0.5f) });
            world.Add(entity, new MeshRenderer(sphere));
            world.Add(entity, new Static());
        }

        // ---------- 月明かり (暗い平行光源。影あり) ----------
        var moon = world.CreateEntity("Moon");
        world.Add(moon, Transform.LookingAt(Vector3.Zero, new Vector3(0.3f, -1.0f, 0.5f)));
        world.Add(moon, new DirectionalLight { Color = new Vector3(0.6f, 0.7f, 1.0f), Intensity = 0.08f });

        // ---------- 影を落とす点光源 (真ん中の上) ----------
        var bulb = world.CreateEntity("Point Light (shadow)");
        world.Add(bulb, new Transform(new Vector3(0, 2.2f, 0)));
        world.Add(bulb, new PointLight { Color = new Vector3(1.0f, 0.75f, 0.45f), Intensity = 6.0f, Range = 12.0f, SourceRadius = 0.1f, CastShadows = true });

        // ---------- 影を落とすスポットライト (首を振る) ----------
        AddSpot(world, "Spot Light (blue)", new Vector3(-7, 6, -7), new Vector3(0.4f, 0.6f, 1.0f), 40.0f, 0.0f);
        AddSpot(world, "Spot Light (red)",  new Vector3( 7, 6, -7), new Vector3(1.0f, 0.3f, 0.25f), 40.0f, MathF.PI);

        // ---------- 影なしの小さな点光源 (回る) ----------
        for (int i = 0; i < (smallLights ? 16 : 0); i++)
        {
            var hue = i / 16.0f;
            var entity = world.CreateEntity($"Small Light {i}");
            world.Add(entity, new Transform(Vector3.Zero));
            world.Add(entity, new PointLight { Color = Hue(hue), Intensity = 2.0f, Range = 4.0f, UseInverseSquaredFalloff = false, FalloffExponent = 2.0f });
            world.Add(entity, new LightOrbit { Radius = 9.0f + (i % 2) * 2.5f, Height = 0.6f, Speed = 0.25f, Phase = i * MathF.Tau / 16 });
        }
        engine.AddSystem(new LightOrbitSystem());
        engine.AddSystem(new LightHotkeySystem(engine.Graphics));
    }

    public static Transform CameraFor(string view) => view switch
    {
        "spot" => Transform.LookingAt(new Vector3(-3, 3.0f, -6), new Vector3(-1, 0.5f, -1)),
        _      => Transform.LookingAt(new Vector3(0, 9, -17), new Vector3(0, 0, 0)),
    };

    private static void AddSpot(World world, string name, Vector3 position, Vector3 color, float outer, float phase)
    {
        var entity = world.CreateEntity(name);
        world.Add(entity, Transform.LookingAt(position, Vector3.Zero));
        world.Add(entity, new SpotLight { Color = color, Intensity = 40.0f, Range = 20.0f, InnerConeAngle = outer * 0.6f, OuterConeAngle = outer * 0.5f + 8.0f, CastShadows = true });
        world.Add(entity, new SpotSweep { Origin = position, Phase = phase });
    }

    internal static void AddBox(World world, Model box, Vector3 bottom, Vector3 half)
    {
        var entity = world.CreateEntity();
        world.Add(entity, new Transform(bottom) { Scale = half });
        world.Add(entity, new MeshRenderer(box));
        world.Add(entity, new Static());
    }

    internal static Vector3 Hue(float h)
    {
        var r = MathF.Abs(h * 6 - 3) - 1;
        var g = 2 - MathF.Abs(h * 6 - 2);
        var b = 2 - MathF.Abs(h * 6 - 4);
        return new Vector3(Math.Clamp(r, 0, 1), Math.Clamp(g, 0, 1), Math.Clamp(b, 0, 1));
    }
}

// 円を回る (小さな点光源)
internal struct LightOrbit : IComponent
{
    public float Radius, Height, Speed, Phase;
}

// スポットライトの首振り (真ん中の周りを見回す)
internal struct SpotSweep : IComponent
{
    public Vector3 Origin;
    public float   Phase;
}

internal sealed class LightOrbitSystem : ISystem
{
    public void Update(World world)
    {
        var t = (float)Time.TotalTime;
        var orbits = world.GetPool<LightOrbit>();
        var entities = orbits.Entities;
        var values = orbits.Components;
        for (int i = 0; i < entities.Length; i++)
        {
            ref var transform = ref world.Get<Transform>(entities[i]);
            var a = values[i].Phase + t * values[i].Speed;
            transform.Position = new Vector3(MathF.Cos(a) * values[i].Radius, values[i].Height + MathF.Sin(a * 3.0f) * 0.3f, MathF.Sin(a) * values[i].Radius);
        }
        var sweeps = world.GetPool<SpotSweep>();
        var sweepEntities = sweeps.Entities;
        var sweepValues = sweeps.Components;
        for (int i = 0; i < sweepEntities.Length; i++)
        {
            var a = sweepValues[i].Phase + t * 0.4f;
            var target = new Vector3(MathF.Cos(a) * 4.0f, 0, MathF.Sin(a) * 4.0f);
            ref var transform = ref world.Get<Transform>(sweepEntities[i]);
            transform = Transform.LookingAt(sweepValues[i].Origin, target);
        }
    }
}

// L : クラスター / K : 点光源・スポットライトの影
internal sealed class LightHotkeySystem : ISystem
{
    private readonly GraphicsSettings _graphics;
    public LightHotkeySystem(GraphicsSettings graphics) { _graphics = graphics; }

    public void Update(World world)
    {
        if (Input.GetKeyDown(Keys.L)) { _graphics.LocalLights.Clustered = !_graphics.LocalLights.Clustered; Print(); }
        if (Input.GetKeyDown(Keys.K)) { _graphics.LocalLights.Shadows   = !_graphics.LocalLights.Shadows;   Print(); }
    }

    private void Print() =>
        Console.WriteLine($"[Lights] clustered {(_graphics.LocalLights.Clustered ? "ON" : "OFF")} / local shadows {(_graphics.LocalLights.Shadows ? "ON" : "OFF")} ({_graphics.LocalShadowSlices} slices)");
}

// ==========================================================================
// 計測 : N 個の点光源 (影なし) を 40 m 四方の床の上で動かす。箱を 20 x 20 個並べる
// ==========================================================================
internal static class LightBench
{
    public static void Spawn(Engine engine, Model box, int count)
    {
        var world = engine.World;
        ProjectSettings.Current.Rendering.AmbientColor = new Vector3(0.02f);
        LightDemo.AddBox(world, box, new Vector3(0, -0.5f, 0), new Vector3(20, 0.25f, 20));
        for (int x = 0; x < 20; x++)
            for (int z = 0; z < 20; z++)
                LightDemo.AddBox(world, box, new Vector3(-19 + x * 2, 0, -19 + z * 2), new Vector3(0.3f, 0.3f + ((x * 7 + z * 3) % 5) * 0.15f, 0.3f));

        var random = new Random(1234);
        for (int i = 0; i < count; i++)
        {
            var entity = world.CreateEntity($"Light {i}");
            world.Add(entity, new Transform(Vector3.Zero));
            world.Add(entity, new PointLight { Color = LightDemo.Hue((float)random.NextDouble()), Intensity = 1.5f, Range = 4.0f });
            world.Add(entity, new LightOrbit { Radius = 2.0f + (float)random.NextDouble() * 17.0f, Height = 0.4f + (float)random.NextDouble(), Speed = 0.05f + (float)random.NextDouble() * 0.2f, Phase = (float)(random.NextDouble() * Math.Tau) });
        }
        engine.AddSystem(new LightOrbitSystem());
        engine.AddSystem(new LightHotkeySystem(engine.Graphics));
        engine.AddSystem(new LightBenchSystem(engine.Graphics, count));
        Profiler.PassTimingEnabled = true;
    }
}

internal sealed class LightBenchSystem : ISystem
{
    private readonly GraphicsSettings _graphics;
    private readonly int _count;
    private readonly List<PassTimingSample> _passes = new();
    private int _frames;
    private double _gpu;

    public LightBenchSystem(GraphicsSettings graphics, int count) { _graphics = graphics; _count = count; }

    public void Update(World world)
    {
        if (Time.TotalTime < 3.0) return;
        _passes.Clear();
        Profiler.ReadPassTimings(_passes);
        double total = 0;
        foreach (var pass in _passes) if (pass.Depth == 0 && pass.HasGpu) total += pass.GpuMilliseconds;
        if (total <= 0.0) return;
        _gpu += total;
        _frames++;
        if (_frames % 120 == 0)
            Console.WriteLine($"[LightBench] {_count} lights / clustered {(_graphics.LocalLights.Clustered ? "ON" : "OFF")} : {_frames} frames, GPU {_gpu / _frames:F3} ms");
    }
}
