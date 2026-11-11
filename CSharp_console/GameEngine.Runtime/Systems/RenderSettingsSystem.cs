using System.Numerics;
using GameEngine.Rendering;

namespace GameEngine;

// ==========================================================================
// カメラの描き方 / Project Settings の Rendering + Volume → engine.Graphics (エンジン組み込み。カメラの後、描画パイプラインの前)
//   v1.5 : 元の値はカメラ (描き方) と Project Settings の Rendering (全体の値)
//     (以前はシーンの最初の RenderSettings の部品が元の値だった。RenderSettings の部品は無くした)
//   1. メインのカメラ (Depth が一番小さいカメラ) の値に、その位置で効いている Volume を混ぜて、前と違う物だけ engine.Graphics へ
//      (同じなら書かない : キーやコードで変えた値は、設定かカメラの位置が変わるまでそのまま。C++ へ毎フレーム送らない)
//   2. Project Settings の Rendering に、メインのカメラの位置の Volume (環境光 / 影の距離) を混ぜて、同じく違う物だけ
//   3. 2 台目からのカメラは、それぞれの位置で Volume を混ぜた値を Graphics の描くカメラの表へ (描く時に入れ替える)
//   メインのカメラのエンティティが替わった時 (シーンを開いた時など) は、カメラの値を全部書き直す
// ==========================================================================
internal sealed class RenderSettingsSystem
{
    private Camera _applied = new();
    private RenderingProjectValues _appliedProject = new();
    private bool   _hasApplied;
    private Entity _source;   // 前のフレームのメインのカメラ
    private readonly List<(int Priority, float Weight, int Index)> _active = new();

    public void Update(World world, GraphicsSettings graphics, CameraSystem cameraSystem)
    {
        var cameras = cameraSystem.Cameras;
        var volumes = world.GetPool<Volume>();
        graphics.RenderCameras.Clear();

        // 1. メインのカメラ
        var main = cameras.Count > 0 ? cameras[0] : new CameraFrame { Camera = new Camera(), Position = cameraSystem.Position };
        var project = ProjectSettings.Current.Rendering;
        var ambient = project.AmbientColor;
        var shadowDistance = project.Shadows.Distance;
        var mainValues = main.Camera;
        blendVolumes(world, volumes, main.Position, ref mainValues, ref ambient, ref shadowDistance);
        if (cameras.Count > 0)
        {
            // シーンのカメラが替わったら全部書き直す。最初の 1 回は既定の値と比べる (コードで決めた値を消さない)
            var sourceChanged = _hasApplied && main.Entity != _source;
            applyCamera(graphics, mainValues, force: sourceChanged);
            _source = main.Entity;
            _hasApplied = true;
        }
        graphics.RenderCameras.Add(mainValues);

        // 2. プロジェクト全体の値
        project.AmbientColor     = ambient;
        project.Shadows.Distance = shadowDistance;
        applyProject(graphics, project);

        // 3. 2 台目から
        for (int i = 1; i < cameras.Count; i++)
        {
            var values = cameras[i].Camera;
            var unusedAmbient = ambient;
            var unusedShadow  = shadowDistance;
            blendVolumes(world, volumes, cameras[i].Position, ref values, ref unusedAmbient, ref unusedShadow);
            graphics.RenderCameras.Add(values);
        }
    }

    private void blendVolumes(World world, ComponentPool<Volume> volumes, Vector3 position, ref Camera camera, ref Vector3 ambient, ref float shadowDistance)
    {
        if (volumes.Count == 0) return;
        _active.Clear();
        var volumeEntities = volumes.Entities;
        var transforms = world.GetPool<Transform>();
        for (int i = 0; i < volumeEntities.Length; i++)
        {
            if (!world.IsActive(volumeEntities[i])) continue;
            ref var volume = ref volumes.Components[i];
            var hasTransform = transforms.Has(volumeEntities[i].Id);
            var weight = volume.Weight * influence(volume, hasTransform ? transforms.Get(volumeEntities[i].Id).LocalToWorld : Matrix4x4.Identity, hasTransform, position);
            if (weight > 0.0f) _active.Add((volume.Priority, Math.Clamp(weight, 0.0f, 1.0f), i));
        }
        _active.Sort((a, b) => a.Priority.CompareTo(b.Priority));
        foreach (var (_, t, index) in _active)
        {
            ref var v = ref volumes.Components[index];
            if (v.OverrideExposure)       camera.Exposure        = lerp(camera.Exposure, v.Exposure, t);
            if (v.OverrideBackground)     camera.BackgroundColor = Vector3.Lerp(camera.BackgroundColor, v.BackgroundColor, t);
            if (v.OverrideSsao)           camera.Ssao.Intensity  = lerp(camera.Ssao.Intensity, v.SsaoIntensity, t);
            if (v.OverrideAmbient)        ambient                = Vector3.Lerp(ambient, v.AmbientColor, t);
            if (v.OverrideShadowDistance) shadowDistance         = lerp(shadowDistance, v.ShadowDistance, t);
        }
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

    private static float lerp(float a, float b, float t) => a + (b - a) * t;

    // メインのカメラの値 : 前に書いた値と違う物だけ書く (force : シーンのカメラが替わった時は全部)
    private void applyCamera(GraphicsSettings g, in Camera s, bool force)
    {
        ref readonly var last = ref _applied;
        if (force || s.RenderPath != last.RenderPath)
        {
            var deferred = g.Pipeline is DeferredRenderPipeline;
            if (s.RenderPath == RenderPath.Deferred && !deferred) g.Pipeline = new DeferredRenderPipeline();
            else if (s.RenderPath == RenderPath.Forward && deferred) g.Pipeline = new ForwardRenderPipeline();
        }
        if (force || s.ClearFlags != last.ClearFlags)           g.Skybox.Enabled  = s.ClearFlags == CameraClearFlags.Skybox;
        if (force || s.AntiAliasing != last.AntiAliasing)       g.AntiAliasing    = s.AntiAliasing;
        if (force || s.BackgroundColor != last.BackgroundColor) g.BackgroundColor = s.BackgroundColor;
        if (force || s.Exposure != last.Exposure)               g.Exposure        = s.Exposure;
        if (force || s.Tonemapper != last.Tonemapper)           g.Tonemapper      = s.Tonemapper;
        if (force || s.Gamma != last.Gamma)                     g.Gamma           = s.Gamma;
        ApplySsao(g.Ssao, s.Ssao, last.Ssao, force);
        ApplyFxaa(g.Fxaa, s.Fxaa, last.Fxaa, force);
        _applied = s;
    }

    internal static void ApplySsao(SsaoSettings ssao, in SsaoValues s, in SsaoValues last, bool force)
    {
        if (force || s.Enabled != last.Enabled)                 ssao.Enabled         = s.Enabled;
        if (force || s.Radius != last.Radius)                   ssao.Radius          = s.Radius;
        if (force || s.Intensity != last.Intensity)             ssao.Intensity       = s.Intensity;
        if (force || s.Bias != last.Bias)                       ssao.Bias            = s.Bias;
        if (force || s.SampleCount != last.SampleCount)         ssao.SampleCount     = Math.Clamp(s.SampleCount, 1, 32);
        if (force || s.ResolutionScale != last.ResolutionScale) ssao.ResolutionScale = Math.Clamp(s.ResolutionScale, 0.25f, 1.0f);
    }

    internal static void ApplyFxaa(FxaaSettings fxaa, in FxaaValues s, in FxaaValues last, bool force)
    {
        if (force || s.Subpixel != last.Subpixel)                 fxaa.Subpixel         = s.Subpixel;
        if (force || s.EdgeThreshold != last.EdgeThreshold)       fxaa.EdgeThreshold    = s.EdgeThreshold;
        if (force || s.EdgeThresholdMin != last.EdgeThresholdMin) fxaa.EdgeThresholdMin = s.EdgeThresholdMin;
    }

    // Project Settings の Rendering : 前に書いた値と違う物だけ書く (最初は既定の値と比べる)
    private void applyProject(GraphicsSettings g, in RenderingProjectValues s)
    {
        ref readonly var last = ref _appliedProject;
        if (s.LightingModel != last.LightingModel)     g.LightingModel    = s.LightingModel;
        if (s.AmbientColor != last.AmbientColor)       g.AmbientColor     = s.AmbientColor;
        if (s.SkyboxTexture != last.SkyboxTexture)     g.Skybox.Texture   = string.IsNullOrWhiteSpace(s.SkyboxTexture) ? null : s.SkyboxTexture;
        if (s.SkyboxIntensity != last.SkyboxIntensity) g.Skybox.Intensity = s.SkyboxIntensity;
        if (s.SkyboxRotation != last.SkyboxRotation)   g.Skybox.Rotation  = s.SkyboxRotation;

        var shadows = g.Shadows;
        if (s.Shadows.Enabled != last.Shadows.Enabled)           shadows.Enabled      = s.Shadows.Enabled;
        if (s.Shadows.Resolution != last.Shadows.Resolution)     shadows.Resolution   = Math.Clamp(s.Shadows.Resolution, 256, 8192);
        if (s.Shadows.Distance != last.Shadows.Distance)         shadows.Distance     = Math.Max(s.Shadows.Distance, 1.0f);
        if (s.Shadows.NormalBias != last.Shadows.NormalBias)     shadows.NormalBias   = s.Shadows.NormalBias;
        if (s.Shadows.Softness != last.Shadows.Softness)         shadows.Softness     = s.Shadows.Softness;
        if (s.Shadows.CascadeCount != last.Shadows.CascadeCount) shadows.CascadeCount = Math.Clamp(s.Shadows.CascadeCount, 1, 4);
        if (s.Shadows.SplitLambda != last.Shadows.SplitLambda)   shadows.SplitLambda  = s.Shadows.SplitLambda;
        if (s.Shadows.CascadeBlend != last.Shadows.CascadeBlend) shadows.CascadeBlend = s.Shadows.CascadeBlend;

        var particles = g.Particles;
        if (s.Particles.Enabled != last.Particles.Enabled)                       particles.Enabled            = s.Particles.Enabled;
        if (s.Particles.Capacity != last.Particles.Capacity)                     particles.Capacity           = Math.Clamp(s.Particles.Capacity, 1024, 1 << 21);
        if (s.Particles.Sorting != last.Particles.Sorting)                       particles.Sorting            = s.Particles.Sorting;
        if (s.Particles.Collision != last.Particles.Collision)                   particles.Collision          = s.Particles.Collision;
        if (s.Particles.CollisionThickness != last.Particles.CollisionThickness) particles.CollisionThickness = s.Particles.CollisionThickness;
        if (s.Particles.SoftDistance != last.Particles.SoftDistance)             particles.SoftDistance       = s.Particles.SoftDistance;
        if (s.Particles.HalfResolution != last.Particles.HalfResolution)         particles.HalfResolution     = s.Particles.HalfResolution;

        _appliedProject = s;
    }
}
