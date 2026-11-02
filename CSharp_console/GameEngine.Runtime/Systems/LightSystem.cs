using System.Runtime.InteropServices;
using GameEngine.Native;

namespace GameEngine;

// ==========================================================================
// ライトを集めて C++ へ送る (エンジン組み込み。TransformSystem の後に動く)
//   前のフレームと同じなら送らない (止まっているシーンでは何もしない)
//   ライトの数は MaxLights まで (超えた分は使わない)
// ==========================================================================
internal sealed unsafe class LightSystem : ISystem
{
    public const int MaxLights = 256;   // C++ の Buffer::MAX_LIGHTS と同じ

    private readonly NativeMethods.LightData[] _lights = new NativeMethods.LightData[MaxLights];
    private readonly NativeMethods.LightData[] _sent   = new NativeMethods.LightData[MaxLights];
    private int  _sentCount = -1;
    private bool _warned;

    public int LightCount { get; private set; }

    public void Update(World world)
    {
        var transforms = world.GetPool<Transform>();
        var count = 0;

        var directional = world.GetPool<DirectionalLight>();
        var entities    = directional.Entities;
        var lights      = directional.Components;
        for (int i = 0; i < lights.Length && count < MaxLights; i++)
        {
            ref var transform = ref transforms.TryGet(entities[i].Id, out var found);
            var forward = found ? transform.Forward : System.Numerics.Vector3.UnitZ;
            _lights[count++] = new NativeMethods.LightData
            {
                Direction = System.Numerics.Vector3.Normalize(forward),
                Type      = 0,
                Color     = lights[i].Color,
                Intensity = lights[i].Intensity,
                Range     = 0.0f,
            };
        }

        var point = world.GetPool<PointLight>();
        var pointEntities = point.Entities;
        var points        = point.Components;
        for (int i = 0; i < points.Length && count < MaxLights; i++)
        {
            ref var transform = ref transforms.TryGet(pointEntities[i].Id, out var found);
            _lights[count++] = new NativeMethods.LightData
            {
                Position  = found ? transform.Position : default,
                Range     = points[i].Range,
                Direction = new System.Numerics.Vector3(0, -1, 0),
                Type      = 1,
                Color     = points[i].Color,
                Intensity = points[i].Intensity,
            };
        }

        if (!_warned && directional.Count + point.Count > MaxLights)
        {
            Console.WriteLine($"[LightSystem] Too many lights. Only {MaxLights} are used.");
            _warned = true;
        }

        LightCount = count;
        var current = _lights.AsSpan(0, count);
        if (count == _sentCount && MemoryMarshal.AsBytes(current).SequenceEqual(MemoryMarshal.AsBytes(_sent.AsSpan(0, count)))) return;

        fixed (NativeMethods.LightData* data = _lights)
        {
            NativeMethods.Renderer_SetLights(data, (uint)count);
        }
        current.CopyTo(_sent);
        _sentCount = count;
    }
}
