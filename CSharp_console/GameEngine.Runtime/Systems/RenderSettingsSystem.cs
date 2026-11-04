using System.Numerics;
using GameEngine.Rendering;

namespace GameEngine;

// ==========================================================================
// RenderSettings + Volume → engine.Graphics (エンジン組み込み。カメラの後、描画パイプラインの前。ED6)
//   1. シーンの最初の (有効な) RenderSettings を元の値にする
//   2. カメラの位置で効いている Volume を、Priority の小さい順に重みで混ぜる
//   3. 前のフレームに書いた値と違う物だけ engine.Graphics へ書く
//      (同じなら書かない : キーやコードで変えた値は、設定かカメラの位置が変わるまでそのまま。C++ へ毎フレーム送らない)
//   RenderSettings も Volume も無いシーンでは何もしない (前のシーンにあった時だけ、既定の値に 1 回戻す)
//   元の RenderSettings のエンティティが替わった時 (シーンを開いた時など) は、全部の値を書き直す
//   (以前は値が同じなら書かなかったので、コードで変えた値が開き直したシーンでも残った)
// ==========================================================================
internal sealed class RenderSettingsSystem
{
    private RenderSettings _applied;
    private bool _hasApplied;
    private Entity _source;   // 前のフレームに元にした RenderSettings のエンティティ
    private readonly List<(int Priority, float Weight, int Index)> _active = new();

    public void Update(World world, GraphicsSettings graphics, Vector3 cameraPosition)
    {
        var settingsPool = world.GetPool<RenderSettings>();
        var volumes = world.GetPool<Volume>();
        if (settingsPool.Count == 0 && volumes.Count == 0)
        {
            if (_hasApplied) apply(graphics, new RenderSettings(), force: false);   // 前のシーンの設定を既定に戻す
            _hasApplied = false;
            _source = Entity.Null;
            return;
        }

        // 1. 元の値
        var result = new RenderSettings();
        var source = Entity.Null;
        var entities = settingsPool.Entities;
        for (int i = 0; i < entities.Length; i++)
        {
            if (!world.IsActive(entities[i])) continue;
            result = settingsPool.Components[i];
            source = entities[i];
            break;
        }
        var sourceChanged = source != _source;
        _source = source;

        // 2. Volume (効いている物を Priority の順に)
        if (volumes.Count > 0)
        {
            _active.Clear();
            var volumeEntities = volumes.Entities;
            var transforms = world.GetPool<Transform>();
            for (int i = 0; i < volumeEntities.Length; i++)
            {
                if (!world.IsActive(volumeEntities[i])) continue;
                ref var volume = ref volumes.Components[i];
                var weight = volume.Weight * influence(volume, transforms.Has(volumeEntities[i].Id) ? transforms.Get(volumeEntities[i].Id).LocalToWorld : Matrix4x4.Identity,
                                                       transforms.Has(volumeEntities[i].Id), cameraPosition);
                if (weight > 0.0f) _active.Add((volume.Priority, Math.Clamp(weight, 0.0f, 1.0f), i));
            }
            _active.Sort((a, b) => a.Priority.CompareTo(b.Priority));
            foreach (var (_, weight, index) in _active) blend(ref result, volumes.Components[index], weight);
        }

        // 3. 変わった物だけ書く
        apply(graphics, result, force: !_hasApplied || sourceChanged);
        _hasApplied = true;
    }

    // カメラの位置での効き (0〜1)。範囲の中なら 1、外側 BlendDistance の間でだんだん 0 へ
    private static float influence(in Volume volume, in Matrix4x4 world, bool hasTransform, Vector3 camera)
    {
        if (volume.Shape == VolumeShape.Global) return 1.0f;
        if (!hasTransform) return 0.0f;
        float distance;
        if (volume.Shape == VolumeShape.Sphere)
        {
            var scale = new Vector3(world.M11, world.M12, world.M13).Length();
            distance = Vector3.Distance(camera, world.Translation) - volume.Size.X * scale;
        }
        else
        {
            // 箱 : カメラを箱のローカルへ移して、箱の外へはみ出した距離 (ワールドの長さで測るため拡大縮小は掛け直す)
            if (!Matrix4x4.Decompose(world, out var scale, out var rotation, out var translation)) return 0.0f;
            var local = Vector3.Transform(camera - translation, Quaternion.Conjugate(rotation));
            var half = volume.Size * 0.5f * scale;
            var outside = Vector3.Max(Vector3.Abs(local) - half, Vector3.Zero);
            distance = outside.Length();
        }
        if (distance <= 0.0f) return 1.0f;
        return volume.BlendDistance > 0.0f ? Math.Max(0.0f, 1.0f - distance / volume.BlendDistance) : 0.0f;
    }

    private static void blend(ref RenderSettings s, in Volume v, float t)
    {
        if (v.OverrideExposure)       s.Exposure         = lerp(s.Exposure, v.Exposure, t);
        if (v.OverrideAmbient)        s.AmbientColor     = Vector3.Lerp(s.AmbientColor, v.AmbientColor, t);
        if (v.OverrideBackground)     s.BackgroundColor  = Vector3.Lerp(s.BackgroundColor, v.BackgroundColor, t);
        if (v.OverrideSsao)           s.Ssao.Intensity   = lerp(s.Ssao.Intensity, v.SsaoIntensity, t);
        if (v.OverrideShadowDistance) s.Shadows.Distance = lerp(s.Shadows.Distance, v.ShadowDistance, t);
    }

    private static float lerp(float a, float b, float t) => a + (b - a) * t;

    // 前に書いた値と違う物だけ書く (force : 最初の 1 回は全部)
    private void apply(GraphicsSettings g, in RenderSettings s, bool force)
    {
        ref readonly var last = ref _applied;
        if (force || s.Path != last.Path)
        {
            var deferred = g.Pipeline is DeferredRenderPipeline;
            if (s.Path == RenderPath.Deferred && !deferred) g.Pipeline = new DeferredRenderPipeline();
            else if (s.Path == RenderPath.Forward && deferred) g.Pipeline = new ForwardRenderPipeline();
        }
        if (force || s.LightingModel != last.LightingModel)     g.LightingModel   = s.LightingModel;
        if (force || s.AntiAliasing != last.AntiAliasing)       g.AntiAliasing    = s.AntiAliasing;
        if (force || s.AmbientColor != last.AmbientColor)       g.AmbientColor    = s.AmbientColor;
        if (force || s.BackgroundColor != last.BackgroundColor) g.BackgroundColor = s.BackgroundColor;
        if (force || s.Exposure != last.Exposure)               g.Exposure        = s.Exposure;
        if (force || s.Tonemapper != last.Tonemapper)           g.Tonemapper      = s.Tonemapper;
        if (force || s.Gamma != last.Gamma)                     g.Gamma           = s.Gamma;

        var ssao = g.Ssao;
        if (force || s.Ssao.Enabled != last.Ssao.Enabled)                 ssao.Enabled         = s.Ssao.Enabled;
        if (force || s.Ssao.Radius != last.Ssao.Radius)                   ssao.Radius          = s.Ssao.Radius;
        if (force || s.Ssao.Intensity != last.Ssao.Intensity)             ssao.Intensity       = s.Ssao.Intensity;
        if (force || s.Ssao.Bias != last.Ssao.Bias)                       ssao.Bias            = s.Ssao.Bias;
        if (force || s.Ssao.SampleCount != last.Ssao.SampleCount)         ssao.SampleCount     = Math.Clamp(s.Ssao.SampleCount, 1, 32);
        if (force || s.Ssao.ResolutionScale != last.Ssao.ResolutionScale) ssao.ResolutionScale = Math.Clamp(s.Ssao.ResolutionScale, 0.25f, 1.0f);

        var shadows = g.Shadows;
        if (force || s.Shadows.Enabled != last.Shadows.Enabled)           shadows.Enabled      = s.Shadows.Enabled;
        if (force || s.Shadows.Resolution != last.Shadows.Resolution)     shadows.Resolution   = Math.Clamp(s.Shadows.Resolution, 256, 8192);
        if (force || s.Shadows.Distance != last.Shadows.Distance)         shadows.Distance     = Math.Max(s.Shadows.Distance, 1.0f);
        if (force || s.Shadows.NormalBias != last.Shadows.NormalBias)     shadows.NormalBias   = s.Shadows.NormalBias;
        if (force || s.Shadows.Softness != last.Shadows.Softness)         shadows.Softness     = s.Shadows.Softness;
        if (force || s.Shadows.CascadeCount != last.Shadows.CascadeCount) shadows.CascadeCount = Math.Clamp(s.Shadows.CascadeCount, 1, 4);
        if (force || s.Shadows.SplitLambda != last.Shadows.SplitLambda)   shadows.SplitLambda  = s.Shadows.SplitLambda;
        if (force || s.Shadows.CascadeBlend != last.Shadows.CascadeBlend) shadows.CascadeBlend = s.Shadows.CascadeBlend;

        var fxaa = g.Fxaa;
        if (force || s.Fxaa.Subpixel != last.Fxaa.Subpixel)                 fxaa.Subpixel         = s.Fxaa.Subpixel;
        if (force || s.Fxaa.EdgeThreshold != last.Fxaa.EdgeThreshold)       fxaa.EdgeThreshold    = s.Fxaa.EdgeThreshold;
        if (force || s.Fxaa.EdgeThresholdMin != last.Fxaa.EdgeThresholdMin) fxaa.EdgeThresholdMin = s.Fxaa.EdgeThresholdMin;

        var particles = g.Particles;
        if (force || s.Particles.Enabled != last.Particles.Enabled)                       particles.Enabled            = s.Particles.Enabled;
        if (force || s.Particles.Capacity != last.Particles.Capacity)                     particles.Capacity           = Math.Clamp(s.Particles.Capacity, 1024, 1 << 21);
        if (force || s.Particles.Sorting != last.Particles.Sorting)                       particles.Sorting            = s.Particles.Sorting;
        if (force || s.Particles.Collision != last.Particles.Collision)                   particles.Collision          = s.Particles.Collision;
        if (force || s.Particles.CollisionThickness != last.Particles.CollisionThickness) particles.CollisionThickness = s.Particles.CollisionThickness;
        if (force || s.Particles.SoftDistance != last.Particles.SoftDistance)             particles.SoftDistance       = s.Particles.SoftDistance;
        if (force || s.Particles.HalfResolution != last.Particles.HalfResolution)         particles.HalfResolution     = s.Particles.HalfResolution;

        _applied = s;
    }
}
