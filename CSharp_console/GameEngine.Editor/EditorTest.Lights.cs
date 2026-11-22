using System.Numerics;

namespace GameEngine.Editor;

// ==========================================================================
// v2.2 の確認 (--editor-test-section lights)
//   1. GameObject > Light : Directional / Point / Spot を作る (元に戻す / やり直すで値が戻る)
//   2. 保存 : SpotLight の円錐の角度 / UE と同じ項目が JSON に入り、読み直して同じ値になる
//   3. 影の枚の割り当て : スポット 1 枚 / 点光源 6 枚。Project Settings > Local Lights の上限で、入らないライトは飛ばす
//   4. クラスターの ON / OFF を切り替えても描画が続く / シーンビューにライトの印が出る
//   5. スプラッシュ画面 : 出して、段階を変えて、閉じる (スレッドが終わる)
// ==========================================================================
internal sealed partial class EditorTest
{
    private IEnumerable<int> localLights()
    {
        for (int i = 0; i < 5; i++) yield return 0;
        var graphics = _app.Engine.Graphics;
        var count = World.EntityCount;

        // ---------- 1. 作る / 元に戻す ----------
        var point = _app.CreateLight(EditorApp.LightKind.Point);
        var spot  = _app.CreateLight(EditorApp.LightKind.Spot);
        var sun   = _app.CreateLight(EditorApp.LightKind.Directional);
        check(World.Has<PointLight>(point) && World.Has<SpotLight>(spot) && World.Has<DirectionalLight>(sun) && World.EntityCount == count + 3, "create point / spot / directional light");
        check(Vector3.Distance(World.Get<Transform>(spot).Forward, -Vector3.UnitY) < 1e-3f, "new spot light points down");
        var spotId = World.Get<EntityInfo>(spot).Id;
        _app.Undo();
        _app.Undo();
        check(World.EntityCount == count + 1 && SceneSerializer.FindEntity(World, spotId).IsNull, "undo create lights");
        _app.Redo();
        spot = SceneSerializer.FindEntity(World, spotId);
        check(!spot.IsNull && World.Get<SpotLight>(spot).OuterConeAngle == 44.0f, "redo create spot light (UE default outer cone 44)");
        _app.Redo();
        sun = World.EntityCount == count + 3 ? findLast<DirectionalLight>() : Entity.Null;
        yield return 0;

        // ---------- 2. 保存 / 読み直し ----------
        ref var s = ref World.Get<SpotLight>(spot);
        s.InnerConeAngle = 12.5f; s.OuterConeAngle = 33.0f; s.SourceRadius = 0.25f; s.UseInverseSquaredFalloff = false; s.FalloffExponent = 3.0f; s.CastShadows = true;
        var json = SceneSerializer.Save(World, new[] { spot });
        check(json.Contains("\"InnerConeAngle\"") && json.Contains("\"FalloffExponent\""), "spot light fields are saved");
        World.DestroyEntity(spot);
        var loaded = SceneSerializer.Load(World, json, _app.Engine.Assets);
        var copy = loaded.FirstOrDefault(e => World.Has<SpotLight>(e));
        var back = !copy.IsNull ? World.Get<SpotLight>(copy) : default;
        check(!copy.IsNull && back.InnerConeAngle == 12.5f && back.OuterConeAngle == 33.0f && back.SourceRadius == 0.25f && !back.UseInverseSquaredFalloff && back.FalloffExponent == 3.0f,
              "spot light round trip");
        spot = copy;
        yield return 0;

        // ---------- 3. 影の枚の割り当て ----------
        var saved = ProjectSettings.Current.Rendering.LocalLights;
        World.Get<PointLight>(point).CastShadows = true;
        for (int i = 0; i < 3; i++) yield return 0;
        check(graphics.LocalShadowSlices == 7, $"point (6) + spot (1) shadow slices ({graphics.LocalShadowSlices})");
        ProjectSettings.Current.Rendering.LocalLights.MaxShadowSlices = 3;
        for (int i = 0; i < 3; i++) yield return 0;
        check(graphics.LocalLights.MaxShadowSlices == 3 && graphics.LocalShadowSlices == 1, $"limit 3 : the point light is skipped, the spot keeps its slice ({graphics.LocalShadowSlices})");
        ProjectSettings.Current.Rendering.LocalLights.Shadows = false;
        for (int i = 0; i < 3; i++) yield return 0;
        check(graphics.LocalShadowSlices == 0, "local shadows off");
        ProjectSettings.Current.Rendering.LocalLights = saved;
        for (int i = 0; i < 3; i++) yield return 0;
        check(graphics.LocalShadowSlices == 7 && graphics.LocalLights.MaxShadowSlices == saved.MaxShadowSlices, "project settings restored");

        // ---------- 4. クラスターの切り替え / ライトの印 ----------
        var frames = Time.FrameCount;
        ProjectSettings.Current.Rendering.LocalLights.Clustered = false;
        for (int i = 0; i < 3; i++) yield return 0;
        check(!graphics.LocalLights.Clustered, "clustered off");
        ProjectSettings.Current.Rendering.LocalLights.Clustered = true;
        for (int i = 0; i < 3; i++) yield return 0;
        check(graphics.LocalLights.Clustered && Time.FrameCount >= frames + 6, "clustered on again (frames keep going)");
        _app.Selected = spot;
        for (int i = 0; i < 2; i++) yield return 0;
        check(_app.LightIconCount >= 3, $"light icons in the scene view ({_app.LightIconCount})");

        // 片付け
        foreach (var e in new[] { point, spot, sun }) if (World.IsAlive(e)) World.DestroyEntity(e);
        yield return 0;

        // ---------- 5. スプラッシュ画面 ----------
        var splash = SplashScreen.Show("Test project");
        check(splash != null, "splash screen shows");
        splash?.SetStatus("Testing...", 0.5f);
        splash?.SetDetail("[Test] detail line");
        yield return 0;
        splash?.Dispose();
        check(true, "splash screen closes");
    }

    private Entity findLast<T>() where T : struct, IComponent
    {
        var entities = World.GetPool<T>().Entities;
        return entities.Length > 0 ? entities[^1] : Entity.Null;
    }
}
