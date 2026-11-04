using System.Numerics;
using GameEngine;

namespace GameEngine_console;

// ==========================================================================
// GPU パーティクルの見本
//   ParticleDemo  : --scene particles。噴水 (箱と床で跳ねる) / 花火 (バースト・火花) / たき火と煙 (半透明・ソート) /
//                   飛び回る光の群れ (動くエミッター)。全部で十数万個を GPU だけで動かして描く
//   ParticleBench : --particle-bench N。N 個を保ち続ける (計測用。[ParticleBench] を 120 フレームごとに出す)
//   操作 : P でソート、O で衝突、K で花火をまとめて打つ、U でパーティクルの ON / OFF (コンソールに状態を出す)
// ==========================================================================
internal static class ParticleDemo
{
    public static void Spawn(Engine engine, Model box)
    {
        var world = engine.World;

        // ---------- 地面と障害物 (噴水の水が跳ねる。box.obj は x, z : -1〜1 / y : 0〜2) ----------
        AddBox(world, box, new Vector3(0, -1.0f, 0), new Vector3(25, 0.5f, 25));
        AddBox(world, box, new Vector3(-1.6f, 0, 1.2f), new Vector3(0.8f, 0.6f, 0.8f));
        AddBox(world, box, new Vector3(1.8f, 0, -0.6f), new Vector3(0.6f, 1.0f, 0.6f));
        AddBox(world, box, new Vector3(9, 0, 5), new Vector3(1.5f, 0.3f, 1.5f));   // たき火の台

        // ---------- 噴水 : 上に吹き上げて、箱と床で跳ねる (約 7 万個) ----------
        var fountain = world.CreateEntity();
        world.Add(fountain, new Transform(new Vector3(0, 0.2f, 0)));
        world.Add(fountain, new Fountain());
        world.Add(fountain, new ParticleEmitter
        {
            Rate        = 20000.0f,
            Shape       = ParticleShape.Disc,
            ShapeRadius = 0.15f,
            ConeAngle   = 9.0f,
            SpeedMin    = 7.5f,
            SpeedMax    = 9.0f,
            LifeMin     = 3.0f,
            LifeMax     = 4.0f,
            Drag        = 0.1f,
            ColorStart  = new Vector4(0.45f, 0.75f, 1.6f, 0.35f),   // 加算で 7 万個重なるので薄く
            ColorEnd    = new Vector4(0.10f, 0.25f, 0.9f, 0.0f),
            SizeStart   = 0.035f,
            SizeEnd     = 0.025f,
            Blend       = ParticleBlend.Additive,
            Collide     = true,
            Restitution = 0.35f,
        });

        // ---------- 花火 : 空でバーストする (FireworkSystem が場所と色を変えて打つ) ----------
        for (int i = 0; i < 3; i++)
        {
            var firework = world.CreateEntity();
            world.Add(firework, new Transform(new Vector3(-8 + i * 8, 12, 12)));
            world.Add(firework, new ParticleEmitter
            {
                Rate         = 0.0f,
                ConeAngle    = 180.0f,
                SpeedMin     = 5.0f,
                SpeedMax     = 7.0f,
                LifeMin      = 1.4f,
                LifeMax      = 2.2f,
                Gravity      = new Vector3(0, -3.0f, 0),
                Drag         = 1.2f,
                SizeStart    = 0.06f,
                SizeEnd      = 0.02f,
                Sprite       = ParticleSprite.Spark,
                StretchScale = 0.05f,
                Blend        = ParticleBlend.Additive,
            });
            world.Add(firework, new Firework { Interval = 1.6f + i * 0.35f, Timer = i * 0.5f });
        }

        // ---------- たき火 (加算) と煙 (半透明。ソートで前後が正しくなる) ----------
        var fire = world.CreateEntity();
        world.Add(fire, new Transform(new Vector3(9, 0.7f, 5)));
        world.Add(fire, new ParticleEmitter
        {
            Rate         = 1500.0f,
            Shape        = ParticleShape.Disc,
            ShapeRadius  = 0.6f,
            ConeAngle    = 10.0f,
            SpeedMin     = 0.8f,
            SpeedMax     = 1.6f,
            LifeMin      = 0.6f,
            LifeMax      = 1.1f,
            Gravity      = new Vector3(0, 1.5f, 0),
            Turbulence   = 2.0f,
            ColorStart   = new Vector4(3.0f, 1.2f, 0.3f, 0.8f),
            ColorEnd     = new Vector4(1.2f, 0.15f, 0.02f, 0.0f),
            SizeStart    = 0.22f,
            SizeEnd      = 0.05f,
            Blend        = ParticleBlend.Additive,
            AngularSpeed = 90.0f,
        });
        var smoke = world.CreateEntity();
        world.Add(smoke, new Transform(new Vector3(9, 1.6f, 5)));
        world.Add(smoke, new ParticleEmitter
        {
            Rate         = 350.0f,
            Shape        = ParticleShape.Sphere,
            ShapeRadius  = 0.4f,
            ConeAngle    = 15.0f,
            SpeedMin     = 0.6f,
            SpeedMax     = 1.2f,
            LifeMin      = 5.0f,
            LifeMax      = 7.0f,
            Gravity      = new Vector3(0.25f, 0.35f, 0),   // 浮く + 少し風下へ
            Drag         = 0.4f,
            Turbulence   = 1.2f,
            ColorStart   = new Vector4(0.10f, 0.10f, 0.11f, 0.55f),
            ColorEnd     = new Vector4(0.35f, 0.35f, 0.37f, 0.0f),
            SizeStart    = 0.35f,
            SizeEnd      = 1.6f,
            Blend        = ParticleBlend.Alpha,
            AngularSpeed = 25.0f,
        });

        // ---------- 光の群れ : 円を描いて飛ぶエミッターが光の尾を残す (約 7 万個) ----------
        var swarm = world.CreateEntity();
        world.Add(swarm, new Transform(new Vector3(0, 4, 0)));
        world.Add(swarm, new ParticleEmitter
        {
            Rate        = 30000.0f,
            ConeAngle   = 180.0f,
            SpeedMin    = 0.2f,
            SpeedMax    = 1.0f,
            LifeMin     = 1.8f,
            LifeMax     = 2.8f,
            Gravity     = Vector3.Zero,
            Drag        = 0.6f,
            Turbulence  = 3.0f,
            ColorStart  = new Vector4(0.3f, 1.8f, 1.0f, 1.0f),
            ColorEnd    = new Vector4(0.6f, 0.2f, 1.6f, 0.0f),
            SizeStart   = 0.03f,
            SizeEnd     = 0.01f,
            Blend       = ParticleBlend.Additive,
        });
        world.Add(swarm, new Flyer { Center = new Vector3(0, 4.5f, 0), Radius = 6.0f, Height = 1.5f, Speed = 0.9f });

        engine.AddSystem(new FireworkSystem());
        engine.AddSystem(new FlyerSystem());
    }

    // カメラの置き場所 (--view fountain : 噴水の水が箱で跳ねる所 / smoke : 煙の重なり (ソートの確認))
    public static Transform CameraFor(string view) => view switch
    {
        "fountain" => Transform.LookingAt(new Vector3(3.0f, 2.2f, -4.2f), new Vector3(0, 1.0f, 0.3f)),
        "smoke"    => Transform.LookingAt(new Vector3(6.0f, 2.0f, -1.5f), new Vector3(9.5f, 3.0f, 5)),
        _          => Transform.LookingAt(new Vector3(0, 6, -20), new Vector3(0, 4, 4)),
    };

    private static void AddBox(World world, Model box, Vector3 bottom, Vector3 half)
    {
        var entity = world.CreateEntity();
        world.Add(entity, new Transform(bottom) { Scale = half });
        world.Add(entity, new MeshRenderer(box));
        world.Add(entity, new Static());
    }
}

// 花火 : Interval 秒ごとに、場所と色を変えて Burst を出す
public struct Firework : IComponent
{
    public float Interval;
    public float Timer;
}

public sealed class FireworkSystem : ISystem
{
    private readonly Random _random = new(7);

    public void Update(World world)
    {
        var fireAll = Input.GetKeyDown(Keys.K);
        world.ForEach((Entity _, ref Transform transform, ref ParticleEmitter emitter, ref Firework firework) =>
        {
            firework.Timer -= Time.DeltaTime;
            if (firework.Timer > 0.0f && !fireAll) return;
            firework.Timer = firework.Interval;
            transform.Position = new Vector3(_random.NextSingle() * 24 - 12, 10 + _random.NextSingle() * 4, 8 + _random.NextSingle() * 8);
            // 明るい色 (線形で 1 を超える = トーンマップで白く光る)
            var hue = _random.NextSingle();
            var color = HueToRgb(hue) * 4.0f;
            emitter.ColorStart = new Vector4(color, 1.0f);
            emitter.ColorEnd   = new Vector4(color * 0.3f + new Vector3(0.6f, 0.3f, 0.1f), 0.0f);
            emitter.Burst      = 4000;
            emitter.ResetTrail();
        });
    }

    private static Vector3 HueToRgb(float h)
    {
        var r = Math.Clamp(MathF.Abs(h * 6 - 3) - 1, 0, 1);
        var g = Math.Clamp(2 - MathF.Abs(h * 6 - 2), 0, 1);
        var b = Math.Clamp(2 - MathF.Abs(h * 6 - 4), 0, 1);
        return new Vector3(r, g, b);
    }
}

// 円を描いて飛ぶ (高さは波打つ)
public struct Flyer : IComponent
{
    public Vector3 Center;
    public float   Radius;
    public float   Height;
    public float   Speed;   // ラジアン / 秒
}

public sealed class FlyerSystem : ISystem
{
    public void Update(World world)
    {
        var t = (float)Time.TotalTime;
        world.ForEach((Entity _, ref Transform transform, ref Flyer flyer) =>
        {
            var angle = t * flyer.Speed;
            transform.Position = flyer.Center + new Vector3(MathF.Cos(angle) * flyer.Radius, MathF.Sin(angle * 2.3f) * flyer.Height,
                                                            MathF.Sin(angle) * flyer.Radius * 0.7f);
        });
    }
}

// 噴水の印 (J でエミッターを外す / 付け直す確認用)
public struct Fountain : IComponent
{
}

// P : ソート / O : 衝突 / U : パーティクルの ON / OFF / J : 噴水のエミッターを外す・付け直す / H : 半分の解像度で描く
public sealed class ParticleHotkeySystem : ISystem
{
    private readonly ParticleSettings _settings;
    private double _lastPrint;
    private ParticleEmitter? _detached;   // 外した噴水のエミッター (付け直す時に使う)

    public ParticleHotkeySystem(ParticleSettings settings)
    {
        _settings = settings;
    }

    public void Update(World world)
    {
        if (Input.GetKeyDown(Keys.P)) { _settings.Sorting   = !_settings.Sorting;   Print(); }
        if (Input.GetKeyDown(Keys.O)) { _settings.Collision = !_settings.Collision; Print(); }
        if (Input.GetKeyDown(Keys.U)) { _settings.Enabled   = !_settings.Enabled;   Print(); }
        if (Input.GetKeyDown(Keys.H)) { _settings.HalfResolution = !_settings.HalfResolution; Print(); }
        if (Input.GetKeyDown(Keys.J))
        {
            // 外した後も、出た水は寿命まで動き続ける (エミッターの番号は ParticleSystem が寿命の間は取っておく)
            var fountains = world.GetPool<Fountain>().Entities;
            if (fountains.Length > 0)
            {
                var fountain = fountains[0];
                if (world.Has<ParticleEmitter>(fountain))
                {
                    _detached = world.Get<ParticleEmitter>(fountain);
                    world.Remove<ParticleEmitter>(fountain);
                    Console.WriteLine("[Particles] Fountain emitter removed");
                }
                else if (_detached is { } emitter)
                {
                    world.Add(fountain, emitter);
                    Console.WriteLine("[Particles] Fountain emitter added again");
                }
            }
        }
        if (Time.TotalTime - _lastPrint >= 5.0)
        {
            _lastPrint = Time.TotalTime;
            Print();
        }
    }

    private void Print()
    {
        Console.WriteLine($"[Particles] {(_settings.Enabled ? "ON" : "OFF")} / half {(_settings.HalfResolution ? "ON" : "OFF")} / sort {(_settings.Sorting ? "ON" : "OFF")} / collision {(_settings.Collision ? "ON" : "OFF")}" +
                          $" / alive {_settings.AliveCount} (visible {_settings.VisibleCount}) / capacity {_settings.AppliedCapacity} / emitters {_settings.EmitterCount}" +
                          $" / GPU update {_settings.GpuUpdateMs:F3} ms (sort {_settings.GpuSortMs:F3}) / draw {_settings.GpuDrawMs:F3} ms");
    }
}

// ==========================================================================
// 計測 : N 個を保ち続ける (寿命 2 秒ちょうど × Rate = N / 2)。床の上の 40 m 四方の箱の中にばらまき、乱れで動かす
//   [ParticleBench] に 起動 3 秒以降の平均 (生存数 / GPU の更新・ソート・描画) を 120 フレームごとに出す
// ==========================================================================
internal static class ParticleBench
{
    public static void Spawn(Engine engine, Model box, int count)
    {
        var world = engine.World;
        engine.Graphics.Particles.Capacity = (int)Math.Min(NativeCapacity(count), 1 << 21);
        var ground = world.CreateEntity();
        world.Add(ground, new Transform(new Vector3(0, -1.0f, 0)) { Scale = new Vector3(25, 0.5f, 25) });
        world.Add(ground, new MeshRenderer(box));
        world.Add(ground, new Static());

        var emitter = world.CreateEntity();
        world.Add(emitter, new Transform(new Vector3(0, 6, 0)));
        world.Add(emitter, new ParticleEmitter
        {
            Rate        = count / 2.0f,
            Shape       = ParticleShape.Box,
            ShapeRadius = 20.0f,
            ConeAngle   = 180.0f,
            SpeedMin    = 0.5f,
            SpeedMax    = 1.5f,
            LifeMin     = 2.0f,
            LifeMax     = 2.0f,
            Gravity     = new Vector3(0, -1.0f, 0),
            Turbulence  = 1.0f,
            ColorStart  = new Vector4(1.6f, 0.9f, 0.4f, 1.0f),
            ColorEnd    = new Vector4(0.4f, 0.6f, 1.6f, 0.0f),
            SizeStart   = 0.05f,
            SizeEnd     = 0.03f,
            Blend       = ParticleBlend.Alpha,   // ソートの計測のため半透明 (--particle-sort off で切れる)
            Collide     = true,
        });
        engine.AddSystem(new ParticleBenchSystem(engine.Graphics.Particles));
    }

    // 生存数の揺れ (フレームの区切り) の分だけ余裕を持たせる
    private static long NativeCapacity(int count) => (long)(count * 1.05) + 1024;
}

public sealed class ParticleBenchSystem : ISystem
{
    private readonly ParticleSettings _settings;
    private long   _frames;
    private double _alive, _visible, _update, _sort, _draw;

    public ParticleBenchSystem(ParticleSettings settings)
    {
        _settings = settings;
    }

    public void Update(World world)
    {
        if (Time.TotalTime < 3.0 || !_settings.StatsValid) return;
        _frames++;
        _alive  += _settings.AliveCount;
        _visible += _settings.VisibleCount;
        _update += _settings.GpuUpdateMs;
        _sort   += _settings.GpuSortMs;
        _draw   += _settings.GpuDrawMs;
        if (_frames % 120 == 0)
        {
            Console.WriteLine($"[ParticleBench] {_frames} frames : alive {_alive / _frames:F0} (visible {_visible / _frames:F0}) / GPU update {(_update - _sort) / _frames:F3} ms" +
                              $" + sort {_sort / _frames:F3} ms / draw {_draw / _frames:F3} ms (half {(_settings.HalfResolution ? "ON" : "OFF")}, sort {(_settings.Sorting ? "ON" : "OFF")}," +
                              $" collision {(_settings.Collision ? "ON" : "OFF")}, capacity {_settings.AppliedCapacity})");
        }
    }
}
