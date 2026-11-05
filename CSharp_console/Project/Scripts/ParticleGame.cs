using System.Numerics;
using GameEngine;

namespace GameEngine_console;

// ==========================================================================
// パーティクルの見本のゲームのコード (ED7 : GameEngine_console から移した)
// ==========================================================================

// 花火 : Interval 秒ごとに、場所と色を変えて Burst を出す
public struct Firework : IComponent
{
    public float Interval;
    public float Timer;
}

[GameSystem]
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

[GameSystem]
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
