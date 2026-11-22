using System.Numerics;
using System.Runtime.InteropServices;
using GameEngine.Native;
using GameEngine.Rendering;

namespace GameEngine;

// ==========================================================================
// ライトを集めて C++ へ送る (エンジン組み込み。TransformSystem の後に動く)
//   前のフレームと同じなら送らない (止まっているシーンでは何もしない)
//   ライトの数は MaxLights まで (超えた分は使わない)
//   並び : 平行光源 → 点光源 → スポットライト (C++ は先頭の平行光源をいつも回し、残りはクラスターで分ける。v2.2)
//   v2.2 : 影を落とす点光源 / スポットライトに、シャドーマップの枚を割り当てる
//     カメラに近い (届く球の端までの距離が近い) ライトから、Graphics.LocalLights.MaxShadowSlices 枚まで
//     (スポット 1 枚 / 点光源 6 枚。入りきらないライトは影なしで光だけ)
// ==========================================================================
internal sealed unsafe class LightSystem : ISystem
{
    public const int MaxLights = 256;   // C++ の Buffer::MAX_LIGHTS と同じ

    private readonly NativeMethods.LightData[] _lights = new NativeMethods.LightData[MaxLights];
    private readonly NativeMethods.LightData[] _sent   = new NativeMethods.LightData[MaxLights];
    private readonly float[] _shadowKeys  = new float[MaxLights];   // 影の候補 : カメラからの近さ
    private readonly int[]   _shadowOrder = new int[MaxLights];     // 影の候補 : _lights の番号
    private int  _sentCount = -1;
    private bool _warned;

    public int LightCount { get; private set; }
    // 影を落とすライトの番号 (C++ へ送った並びの中。CastShadows の付いた最初の平行光源。無ければ -1)
    public int ShadowLightIndex { get; private set; } = -1;
    // v2.2 : 点光源 / スポットライトの影に使った枚数 (ShadowPass が描く枚数)
    public int LocalShadowSlices { get; private set; }
    public int LocalShadowLights { get; private set; }   // 影を持ったライトの数 (確認用)

    public void Update(World world) => Update(world, null, Vector3.Zero);

    public void Update(World world, GraphicsSettings? graphics, Vector3 viewPosition)
    {
        var transforms = world.GetPool<Transform>();
        var count = 0;
        var shadowLight = -1;

        var directional = world.GetPool<DirectionalLight>();
        var entities    = directional.Entities;
        var lights      = directional.Components;
        for (int i = 0; i < lights.Length && count < MaxLights; i++)
        {
            if (!world.IsActive(entities[i])) continue;   // 無効なライトは使わない (ED2)
            ref var transform = ref transforms.TryGet(entities[i].Id, out var found);
            var forward = found ? transform.Forward : Vector3.UnitZ;
            if (shadowLight < 0 && lights[i].CastShadows) shadowLight = count;
            _lights[count++] = new NativeMethods.LightData
            {
                Direction   = Vector3.Normalize(forward),
                Type        = 0,
                Color       = lights[i].Color,
                Intensity   = lights[i].Intensity,
                Range       = 0.0f,
                ShadowIndex = NativeMethods.NoShadow,
            };
        }

        // 影の候補 (点光源 / スポットライト)
        var local = graphics?.LocalLights;
        var shadowsOn = local != null && local.Shadows && graphics!.LightingModel != LightingModel.Unlit && local.MaxShadowSlices > 0;
        var candidates = 0;

        var point = world.GetPool<PointLight>();
        var pointEntities = point.Entities;
        var points        = point.Components;
        for (int i = 0; i < points.Length && count < MaxLights; i++)
        {
            if (!world.IsActive(pointEntities[i])) continue;
            ref var transform = ref transforms.TryGet(pointEntities[i].Id, out var found);
            ref readonly var p = ref points[i];
            var position = found ? transform.WorldPosition : default;   // 以前はローカル (Position)
            if (shadowsOn && p.CastShadows) addCandidate(ref candidates, count, position, p.Range, viewPosition);
            _lights[count++] = new NativeMethods.LightData
            {
                Position        = position,
                Range           = MathF.Max(p.Range, 0.01f),
                Direction       = new Vector3(0, -1, 0),
                Type            = 1,
                Color           = p.Color,
                Intensity       = p.Intensity,
                SourceRadius    = MathF.Max(p.SourceRadius, 0.0f),
                FalloffExponent = falloff(p.UseInverseSquaredFalloff, p.FalloffExponent),
                ShadowIndex     = NativeMethods.NoShadow,
                ShadowBias      = MathF.Max(p.ShadowBias, 0.0f),
                ShadowNear      = shadowNear(p.Range),
                ShadowTan       = 1.0f + 4.0f / Math.Max(local?.ShadowResolution ?? 1024, 64),   // 90 度 + 縁の余白 (PCF が面の外を読まない)
            };
        }

        // v2.2 : スポットライト
        var spot = world.GetPool<SpotLight>();
        var spotEntities = spot.Entities;
        var spots        = spot.Components;
        for (int i = 0; i < spots.Length && count < MaxLights; i++)
        {
            if (!world.IsActive(spotEntities[i])) continue;
            ref var transform = ref transforms.TryGet(spotEntities[i].Id, out var found);
            ref readonly var s = ref spots[i];
            var position = found ? transform.WorldPosition : default;
            var forward  = found ? transform.Forward : Vector3.UnitZ;
            // 円錐 (UE と同じ) : 外側は 1〜80 度、内側は外側より少し小さく (同じだと割り算が 0 になる)
            var outer = Math.Clamp(s.OuterConeAngle, 1.0f, 80.0f) * (MathF.PI / 180.0f);
            var inner = Math.Clamp(s.InnerConeAngle, 0.0f, 80.0f) * (MathF.PI / 180.0f);
            var cosOuter = MathF.Cos(outer);
            var cosInner = MathF.Max(MathF.Cos(MathF.Min(inner, outer)), cosOuter + 1e-3f);
            var scale    = 1.0f / (cosInner - cosOuter);
            if (shadowsOn && s.CastShadows) addCandidate(ref candidates, count, position, s.Range, viewPosition);
            _lights[count++] = new NativeMethods.LightData
            {
                Position        = position,
                Range           = MathF.Max(s.Range, 0.01f),
                Direction       = forward.LengthSquared() > 0.0f ? Vector3.Normalize(forward) : Vector3.UnitZ,
                Type            = 2,
                Color           = s.Color,
                Intensity       = s.Intensity,
                SpotScale       = scale,
                SpotOffset      = -cosOuter * scale,
                SourceRadius    = MathF.Max(s.SourceRadius, 0.0f),
                FalloffExponent = falloff(s.UseInverseSquaredFalloff, s.FalloffExponent),
                ShadowIndex     = NativeMethods.NoShadow,
                ShadowBias      = MathF.Max(s.ShadowBias, 0.0f),
                ShadowNear      = shadowNear(s.Range),
                ShadowTan       = MathF.Tan(outer),
            };
        }

        var total = directional.Count + point.Count + spot.Count;
        if (!_warned && total > MaxLights)
        {
            Console.WriteLine($"[LightSystem] Too many lights. Only {MaxLights} are used.");
            _warned = true;
        }

        // 影の枚を割り当てる (近い順。入りきらないライトは飛ばして、次の小さいライトを入れる)
        var slices = 0;
        var shadowed = 0;
        if (candidates > 0)
        {
            Array.Sort(_shadowKeys, _shadowOrder, 0, candidates);
            var budget = Math.Clamp(local!.MaxShadowSlices, 0, NativeMethods.MaxLocalShadowSlices);
            for (int c = 0; c < candidates; c++)
            {
                ref var light = ref _lights[_shadowOrder[c]];
                var need = light.Type == 1 ? 6 : 1;
                if (slices + need > budget) continue;
                light.ShadowIndex = (uint)slices;
                slices += need;
                shadowed++;
            }
        }

        LightCount        = count;
        ShadowLightIndex  = shadowLight;
        LocalShadowSlices = slices;
        LocalShadowLights = shadowed;
        var current = _lights.AsSpan(0, count);
        if (count == _sentCount && MemoryMarshal.AsBytes(current).SequenceEqual(MemoryMarshal.AsBytes(_sent.AsSpan(0, count)))) return;

        fixed (NativeMethods.LightData* data = _lights)
        {
            NativeMethods.Renderer_SetLights(data, (uint)count);
        }
        current.CopyTo(_sent);
        _sentCount = count;
    }

    private void addCandidate(ref int candidates, int lightIndex, Vector3 position, float range, Vector3 viewPosition)
    {
        // 近さ = カメラから届く球の端まで (カメラが球の中なら 0)。同じなら先に見つかったライト
        _shadowKeys[candidates]  = MathF.Max(Vector3.Distance(position, viewPosition) - range, 0.0f) + candidates * 1e-6f;
        _shadowOrder[candidates] = lightIndex;
        candidates++;
    }

    // 距離の減衰 : 0 = 距離の 2 乗 (UE の Inverse Squared Falloff) / それ以外は指数
    private static float falloff(bool inverseSquared, float exponent) => inverseSquared ? 0.0f : Math.Clamp(exponent, 0.1f, 16.0f);

    // 影の手前の面 : 届く距離に合わせる (近すぎると奥の深度の細かさが足りない / 遠すぎるとライトのすぐ近くの物の影が消える)
    private static float shadowNear(float range) => Math.Clamp(range * 0.005f, 0.02f, 0.5f);
}
