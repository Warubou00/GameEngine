namespace GameEngine.Editor;

// ==========================================================================
// v2.2 の確認 (--editor-test-section viewmodes) : シーンビューの表示モード
//   1. 一覧 : Default / DrawMap / CollisionMap + プロジェクトの物 (SampleViewModes.cs の Unlit (Albedo) / Light Ranges)
//   2. DrawMap : メインのカメラのパイプラインが差し替わり、描画が続く
//   3. CollisionMap : コライダーを塗りつぶしの三角形で描き (送った三角形 > 0)、外すと設定が戻る
//   4. プロジェクトのモード : 設定を変えて戻す / ゲームのコードを読み直しても同じモードに戻り、古い DLL が解放される
// ==========================================================================
internal sealed partial class EditorTest
{
    // 撮影用 (--editor-test-section viewmodes-wire / viewmodes-solidwire) : CollisionMap をその描き方で出したまま 30 秒待つ
    private IEnumerable<int> collisionStyleView(CollisionMapViewMode.DrawStyle style)
    {
        for (int i = 0; i < 5; i++) yield return 0;
        _app.ViewModes.Select("CollisionMap");
        _app.ViewModes.Modes.OfType<CollisionMapViewMode>().First().Style = style;
        Console.WriteLine($"[Test] viewmodes-view : CollisionMap {style}");
        var start = DateTime.Now;
        while ((DateTime.Now - start).TotalSeconds < 30) yield return 0;
    }

    private IEnumerable<int> sceneViewModes()
    {
        for (int i = 0; i < 5; i++) yield return 0;
        var modes = _app.ViewModes;
        var graphics = _app.Engine.Graphics;
        var physics = _app.Engine.Physics;
        var names = modes.Names.ToList();
        check(names.Take(3).SequenceEqual(["Default", "DrawMap", "CollisionMap"]) && names.Contains("Unlit (Albedo)") && names.Contains("Light Ranges"),
              $"[viewmodes] list ({string.Join(", ", names)})");
        check(!modes.Select("No Such Mode") && modes.CurrentName == "Default", "[viewmodes] unknown name is refused");

        // DrawMap
        var frames = Time.FrameCount;
        check(modes.Select("DrawMap") && graphics.ViewPipelineOverride?.Name == "DrawMap", "[viewmodes] DrawMap replaces the main camera pipeline");
        for (int i = 0; i < 4; i++) yield return 0;
        check(Time.FrameCount >= frames + 4, "[viewmodes] DrawMap keeps rendering");

        // CollisionMap
        var linesBefore = physics.ShowColliders;
        check(modes.Select("CollisionMap") && graphics.ViewPipelineOverride?.Name == "CollisionMap", "[viewmodes] CollisionMap pipeline");
        for (int i = 0; i < 3; i++) yield return 0;
        check(physics.ShowCollidersSolid && DebugDraw.LastTriangleCount > 0, $"[viewmodes] CollisionMap draws solid colliders ({DebugDraw.LastTriangleCount} triangles)");
        // v2.2 : 描き方 (ワイヤーフレーム / 塗りつぶし + ワイヤーフレーム) を切り替えても描画が続く
        var collisionMap = modes.Modes.OfType<CollisionMapViewMode>().First();
        foreach (var style in new[] { CollisionMapViewMode.DrawStyle.Wireframe, CollisionMapViewMode.DrawStyle.SolidWireframe, CollisionMapViewMode.DrawStyle.Solid })
        {
            collisionMap.Style = style;
            var before = Time.FrameCount;
            for (int i = 0; i < 3; i++) yield return 0;
            check(Time.FrameCount >= before + 3 && DebugDraw.LastTriangleCount > 0, $"[viewmodes] CollisionMap style {style}");
        }
        modes.Select("Default");
        for (int i = 0; i < 2; i++) yield return 0;
        check(graphics.ViewPipelineOverride == null && !physics.ShowCollidersSolid && physics.ShowColliders == linesBefore && DebugDraw.LastTriangleCount == 0,
              "[viewmodes] back to Default restores the settings");

        // プロジェクトのモード
        var lighting = graphics.LightingModel;
        check(modes.Select("Unlit (Albedo)") && graphics.LightingModel == LightingModel.Unlit && graphics.ViewPipelineOverride == null, "[viewmodes] project mode (Unlit) changes the settings");
        yield return 0;
        _app.ReloadScripts();
        for (int i = 0; i < 3; i++) yield return 0;
        check(modes.CurrentName == "Unlit (Albedo)" && graphics.LightingModel == LightingModel.Unlit, "[viewmodes] the project mode comes back after reloading the scripts");
        var unloaded = false;
        var waitStart = Time.TotalTime;
        while (!(unloaded = _app.Scripts.PreviousUnloaded() == true) && Time.TotalTime - waitStart < 3.0) yield return 0;
        check(unloaded, "[viewmodes] the previous GameEditor.dll is freed (the old mode is not kept)");
        modes.Select("Default");
        yield return 0;
        check(graphics.LightingModel == lighting, "[viewmodes] leaving the project mode restores the lighting model");
        _app.OpenScene(_app.Database.FullPath("Assets/Scenes/showcase.scene.json"));
        yield return 0;
    }
}
