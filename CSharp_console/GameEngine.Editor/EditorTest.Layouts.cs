using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;

namespace GameEngine.Editor;

// 確認用 (v2.1) : Update で毎フレーム動かす (Behaviour と一緒に使う時に描画へ送られるか)
internal sealed class LateMoveSystem : ISystem
{
    public Entity Target;
    public float X;
    public void Update(World world)
    {
        if (!world.IsAlive(Target)) return;
        X += 0.25f;
        ref var t = ref world.Get<Transform>(Target);
        t.Position = new Vector3(X, 1, 0);
    }
}

// ==========================================================================
// v2.1 の確認 (--editor-test-section layouts)
//   1. Behaviour (ILateSystem) があっても、Update で動かした物が描画へ送られる (LateUpdate の後に Changed を下ろさない)
//   2. 窓の並びのプリセット : 登録 (日本語の名前) / 5 つまで / 名前の確かめ / 読み直し / 並びと窓の出す・出さないが戻る
//   3. 前に開いていたシーン : Library/EditorState.json に覚え、プロジェクトの外 / 無いファイルは開かない
// ==========================================================================
internal sealed partial class EditorTest
{
    private static readonly FieldInfo TransformChanged = typeof(Transform).GetField("Changed", BindingFlags.Instance | BindingFlags.NonPublic)!;

    private IEnumerable<int> layoutsAndLastScene()
    {
        foreach (var step in lateUpdateRenderSync()) yield return step;
        foreach (var step in layoutPresets()) yield return step;
        foreach (var step in lastScene()) yield return step;
    }

    // ---------- 1. Behaviour があっても動いた物が描画へ送られる ----------
    private IEnumerable<int> lateUpdateRenderSync()
    {
        var engine = _app.Engine;
        _app.TogglePlay();
        yield return 0;
        var mover = World.CreateEntity("LateMoveTarget");
        World.Add(mover, new Transform { Position = new Vector3(0, 1, 0), Rotation = Quaternion.Identity, Scale = Vector3.One });
        var move = new LateMoveSystem { Target = mover };
        var behaviour = new CollisionProbeSystem();   // Behaviour<T> = ILateSystem (物が無くても LateUpdate の後の計算が走る)
        engine.AddSystem(move);
        engine.AddSystem(behaviour);
        // エディターの GUI (engine.LateUpdate) は LateUpdate の後の計算の後、RenderSyncSystem の前に呼ばれる
        int seen = 0, changed = 0;
        void probe()
        {
            if (!World.IsAlive(mover)) return;
            seen++;
            if ((bool)TransformChanged.GetValue(World.Get<Transform>(mover))!) changed++;
        }
        engine.LateUpdate += probe;
        for (int i = 0; i < 6; i++) yield return 0;
        engine.LateUpdate -= probe;
        var world = World.Get<Transform>(mover).Matrix.Translation;
        check(seen >= 5 && changed == seen && MathF.Abs(world.X - move.X) < 1e-4f,
              $"[layouts] with a Behaviour (LateUpdate), an object moved in Update stays marked for the render sync ({changed} / {seen} frames, x {world.X:F2} = {move.X:F2})");
        engine.RemoveSystems(new object[] { move, behaviour });
        World.DestroyEntity(mover);
        _app.TogglePlay();
        yield return 0;
    }

    // ini の [Window][name] の DockId (無ければ null)
    private static string? dockIdOf(string ini, string window)
    {
        var m = Regex.Match(ini, @"\[Window\]\[" + Regex.Escape(window) + @"\][^\[]*?DockId=(0x[0-9A-Fa-f]+)");
        return m.Success ? m.Groups[1].Value : null;
    }

    // ---------- 2. 窓の並びのプリセット ----------
    private IEnumerable<int> layoutPresets()
    {
        var file = Path.Combine(Path.GetTempPath(), $"TsumugiLayoutTest_{Environment.ProcessId}", "layouts.json");
        if (Directory.Exists(Path.GetDirectoryName(file))) Directory.Delete(Path.GetDirectoryName(file)!, true);
        var original = _app.Layouts;
        _app.Layouts = new LayoutPresets(file);
        var imgui = _app.ImGuiHost;

        _app.ApplyDefaultLayout();
        for (int i = 0; i < 3; i++) yield return 0;
        var defaultIni = imgui.SaveLayout();
        var hierarchyDock = dockIdOf(defaultIni, "Hierarchy");
        var consoleDock = dockIdOf(defaultIni, "Console");
        check(hierarchyDock != null && consoleDock != null && hierarchyDock != consoleDock, $"[layouts] Default docks Hierarchy and Console apart ({hierarchyDock}, {consoleDock})");

        // 名前の確かめ
        var layouts = _app.Layouts;
        check(layouts.CheckName("", -1) != null && layouts.CheckName("   ", -1) != null && layouts.CheckName("default", -1) != null &&
              layouts.CheckName(new string('あ', LayoutPresets.MaxNameLength + 1), -1) != null && layouts.CheckName(new string('あ', LayoutPresets.MaxNameLength), -1) == null,
              "[layouts] names : empty / 'Default' / too long are refused, 32 Japanese characters are fine");

        // Console を Hierarchy の所へ動かし、Console を隠して「作業用」として登録
        DockBuilder.DockWindow("Console", Convert.ToUInt32(hierarchyDock, 16));
        _app.SetLayoutWindows(new() { ["Project"] = false, ["Rendering Stats"] = true });
        for (int i = 0; i < 3; i++) yield return 0;
        const string name = "作業用 レイアウト①";
        check(_app.SaveLayoutPreset("  " + name + " ") == null && layouts.Presets.Count == 1 && layouts.Presets[0].Name == name && _app.CurrentLayout == name,
              "[layouts] save the current layout with a Japanese name (spaces trimmed)");
        var saved = layouts.Presets[0];
        check(dockIdOf(saved.Ini, "Console") == dockIdOf(saved.Ini, "Hierarchy") && saved.Windows["Project"] == false && saved.Windows["Rendering Stats"],
              "[layouts] the preset keeps the docking and which windows are shown");
        check(_app.SaveLayoutPreset("作業用 レイアウト①") != null && layouts.Presets.Count == 1, "[layouts] a duplicate name is refused");

        // Default に戻す → 並びも窓も戻る
        _app.ApplyDefaultLayout();
        for (int i = 0; i < 3; i++) yield return 0;
        var back = imgui.SaveLayout();
        check(dockIdOf(back, "Console") != dockIdOf(back, "Hierarchy") && _app.LayoutWindows()["Project"] && !_app.LayoutWindows()["Rendering Stats"],
              "[layouts] Default restores the built-in docking and windows");

        // 登録した並びにする
        check(_app.ApplyLayoutPreset(0), "[layouts] apply the saved preset");
        for (int i = 0; i < 3; i++) yield return 0;
        var applied = imgui.SaveLayout();
        check(dockIdOf(applied, "Console") == dockIdOf(applied, "Hierarchy") && !_app.LayoutWindows()["Project"] && _app.LayoutWindows()["Rendering Stats"],
              $"[layouts] ... the docking and windows come back (Console {dockIdOf(applied, "Console")}, Hierarchy {dockIdOf(applied, "Hierarchy")})");

        // 5 つまで
        for (int i = 2; i <= LayoutPresets.MaxPresets; i++) _app.SaveLayoutPreset($"並び {i}");
        check(layouts.Presets.Count == LayoutPresets.MaxPresets && layouts.IsFull && _app.SaveLayoutPreset("6 つめ") != null && layouts.Presets.Count == LayoutPresets.MaxPresets,
              $"[layouts] up to {LayoutPresets.MaxPresets} presets (the 6th is refused)");

        // 名前の変更 / 並べ替え / 消す / ファイルから読み直す
        check(layouts.Rename(1, "並び 3") != null && layouts.Rename(1, "ステージ編集") == null && layouts.Presets[1].Name == "ステージ編集", "[layouts] rename (a duplicate is refused)");
        layouts.Move(1, 0);
        layouts.Remove(4);
        var reloaded = new LayoutPresets(file);
        check(reloaded.Presets.Select(p => p.Name).SequenceEqual(new[] { "ステージ編集", name, "並び 3", "並び 4" }) && reloaded.Presets[1].Ini == saved.Ini,
              $"[layouts] the presets are saved to a file and read back ({string.Join(", ", reloaded.Presets.Select(p => p.Name))})");
        check(File.ReadAllText(file).Contains("作業用"), "[layouts] the file keeps Japanese names readable (not \\u escaped)");

        // 片付け
        _app.Layouts = original;
        _app.ApplyDefaultLayout();
        for (int i = 0; i < 3; i++) yield return 0;
        Directory.Delete(Path.GetDirectoryName(file)!, true);
    }

    // ---------- 3. 前に開いていたシーン ----------
    private IEnumerable<int> lastScene()
    {
        var database = _app.Database;
        var stateFile = Path.Combine(database.ProjectRoot, "Library", "EditorState.json");
        var backup = File.Exists(stateFile) ? File.ReadAllText(stateFile) : null;
        var opened = _app.ScenePath;
        var other = database.FullPath("Assets/Scenes/physics.scene.json");

        _app.RememberLastScene = true;
        _app.OpenScene(other);
        yield return 0;
        check(EditorApp.ReadLastScene(database) is { } last && Path.GetFullPath(last) == Path.GetFullPath(other) && File.ReadAllText(stateFile).Contains("Assets/Scenes/physics.scene.json"),
              "[layouts] opening a scene remembers it in Library/EditorState.json");
        File.WriteAllText(stateFile, "{ \"LastScene\": \"Assets/Scenes/__missing__.scene.json\" }");
        check(EditorApp.ReadLastScene(database) == null, "[layouts] a remembered scene that no longer exists is ignored");
        File.WriteAllText(stateFile, "{ \"LastScene\": \"../../outside.scene.json\" }");
        check(EditorApp.ReadLastScene(database) == null, "[layouts] a path outside Assets is ignored");
        File.WriteAllText(stateFile, "not json");
        check(EditorApp.ReadLastScene(database) == null, "[layouts] a broken EditorState.json is ignored");

        // 片付け (リポジトリの Project の次の起動を変えない)
        _app.RememberLastScene = false;
        if (backup != null) File.WriteAllText(stateFile, backup); else File.Delete(stateFile);
        if (opened != null) _app.OpenScene(opened);
        yield return 0;
    }

    // 撮影用 (--editor-test-section layouts-view) : 日本語の名前の並びを 3 つ登録して Layouts の窓を出す
    private IEnumerable<int> layoutsView()
    {
        var file = Path.Combine(Path.GetTempPath(), $"TsumugiLayoutView_{Environment.ProcessId}", "layouts.json");
        var original = _app.Layouts;
        _app.Layouts = new LayoutPresets(file);
        _app.SaveLayoutPreset("作業用 レイアウト①");
        _app.SaveLayoutPreset("★ステージ編集 → 確認※");
        _app.SaveLayoutPreset("UI 調整 (大きい画面)");
        _app.openLayoutsWindow(focusName: true);
        for (int i = 0; i < 20; i++) yield return 0;
        Console.WriteLine("[Test] layouts-view : window");
        var start = DateTime.Now;
        while ((DateTime.Now - start).TotalSeconds < 4) yield return 0;
        _app.Layouts = original;
        Directory.Delete(Path.GetDirectoryName(file)!, true);
    }
}
