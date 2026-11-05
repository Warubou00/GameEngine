using System.Numerics;
using System.Text.Json;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// エディターの自動の確認 (ED4。GameEngine.Editor.exe --editor-test)
//   ロードマップの確認 : 「物を置いて動かし、保存して開き直すと同じ」「再生して止めると元に戻る」「元に戻すが全部の操作で効く」
//   エディターの操作 (EditorApp の Create / Delete / Duplicate / SetParent / WriteTransform / Play・Stop) を同じ道で呼ぶ
//   1 フレームに 1 歩ずつ進める (再生は本当にフレームを回す)。最後に pass / fail を出して終わる
// ==========================================================================
internal sealed class EditorTest
{
    private readonly EditorApp _app;
    private readonly IEnumerator<int> _steps;
    private int _pass, _fail;
    private World World => _app.Engine.World;

    public EditorTest(EditorApp app)
    {
        _app = app;
        _steps = run().GetEnumerator();
    }

    public void Frame(EditorApp _)
    {
        if (_steps.MoveNext()) return;
        // 何回読み直しても、古い Game.dll が残り続けない (今の 1 つだけ)
        for (int i = 0; i < 10; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        var alive = AppDomain.CurrentDomain.GetAssemblies().Count(a => a.GetName().Name == "Game");
        check(alive == 1, $"old Game.dll copies do not pile up ({alive} alive after {_app.Scripts.LoadCount} loads)");
        Console.WriteLine($"[Test] editor : pass {_pass} / fail {_fail}");
        _app.FrameHook = null;
        _app.ForceQuit();   // 確認で変えたシーンは保存しない (ED10 : 聞く窓を出さない)
    }

    private void check(bool ok, string name)
    {
        if (ok) _pass++; else _fail++;
        Console.WriteLine($"[Test] {(ok ? "PASS" : "FAIL")} {name}");
    }

    // ゲームの DLL の型 (ED7 : エディターはゲームのコードを参照しないので、名前で引く)
    private Type gameType(string name) => ComponentRegistry.Find($"GameEngine_console.{name}")?.Type ?? throw new InvalidOperationException($"No game type {name}");
    private float getField(Entity e, string type, string field)
    {
        var t = gameType(type);
        return Convert.ToSingle(t.GetField(field)!.GetValue(World.GetPool(t).GetBoxed(e.Id)));
    }
    private void setField(Entity e, string type, string field, object value)
    {
        var t = gameType(type);
        var pool = World.GetPool(t);
        var boxed = pool.GetBoxed(e.Id);
        t.GetField(field)!.SetValue(boxed, value);
        pool.SetBoxed(e, boxed);
    }
    private bool has(Entity e, string type) => World.IsAlive(e) && World.GetPool(gameType(type)).Has(e.Id);

    private Entity find(string name) => World.Entities.FirstOrDefault(e => World.Has<EntityInfo>(e) && World.Get<EntityInfo>(e).Name == name);

    // シーンを「Id → そのエンティティの JSON」にする (番号の順に依らずに比べる)
    private static Dictionary<string, string> entitiesById(string json)
    {
        using var document = JsonDocument.Parse(json);
        var map = new Dictionary<string, string>();
        foreach (var e in document.RootElement.GetProperty("entities").EnumerateArray()) map[e.GetProperty("id").GetString()!] = e.GetRawText();
        return map;
    }

    private static bool sameScene(string a, string b, out string detail)
    {
        var x = entitiesById(a);
        var y = entitiesById(b);
        detail = $"{x.Count} vs {y.Count} entities";
        if (x.Count != y.Count) return false;
        foreach (var (id, text) in x)
        {
            if (!y.TryGetValue(id, out var other)) { detail = $"missing {id}"; return false; }
            if (other != text) { detail = $"differs {id}"; return false; }
        }
        return true;
    }

    private IEnumerable<int> run()
    {
        for (int i = 0; i < 10; i++) yield return 0;   // 読み込みと最初の描画を待つ
        var original = SceneSerializer.Save(World);
        var count = World.EntityCount;
        var undo = _app.UndoHistory;

        // ---------- 作る / 元に戻す / やり直す ----------
        var created = _app.CreateEntity(Entity.Null);
        var createdId = World.Get<EntityInfo>(created).Id;
        check(World.EntityCount == count + 1, "create entity");
        _app.Undo();
        check(World.EntityCount == count && SceneSerializer.FindEntity(World, createdId).IsNull, "undo create");
        _app.Redo();
        check(World.EntityCount == count + 1 && !SceneSerializer.FindEntity(World, createdId).IsNull, "redo create (same id)");
        _app.Undo();
        check(!_app.IsDirty, "undo back to the opened state is not dirty");
        yield return 0;

        // ---------- 動かす (ギズモと同じ書き方) / ドラッグ中は 1 つにまとめる ----------
        var pivot = find("Hierarchy pivot");
        check(!pivot.IsNull, "find 'Hierarchy pivot'");
        var start = World.Get<Transform>(pivot);
        var before = undo.Count;
        _app.WriteTransform(pivot, start.Position + new Vector3(1, 0, 0), start.Rotation, start.Scale);
        _app.WriteTransform(pivot, start.Position + new Vector3(2, 0, 0), start.Rotation, start.Scale);
        _app.WriteTransform(pivot, start.Position + new Vector3(3, 0, 0), start.Rotation, start.Scale);
        undo.EndFrame(editing: false);
        check(undo.Count == before + 1, $"drag merges into one undo ({undo.Count - before})");
        yield return 0;
        yield return 0;
        // 子は親に付いて動く (ワールドの位置が 3 m 動いた)
        var child = find("Hierarchy child");
        var childWorld = World.Get<Transform>(child).WorldPosition;
        _app.WriteTransform(pivot, start.Position + new Vector3(3, 0, 0) + new Vector3(0, 0, 1), start.Rotation, start.Scale);
        undo.EndFrame(editing: false);
        yield return 0;
        yield return 0;
        check(Vector3.Distance(World.Get<Transform>(child).WorldPosition, childWorld + new Vector3(0, 0, 1)) < 1e-3f, "child follows moved parent");
        _app.Undo();
        _app.Undo();
        check(Vector3.Distance(World.Get<Transform>(find("Hierarchy pivot")).Position, start.Position) < 1e-5f, "undo move x2");
        yield return 0;

        // ---------- 消す (子ごと) / 元に戻すと親子も戻る ----------
        _app.Selected = find("Hierarchy pivot");
        _app.DeleteSelected();
        check(World.EntityCount == count - 3 && find("Hierarchy grandchild").IsNull, "delete with children");
        yield return 0;
        _app.Undo();
        pivot = find("Hierarchy pivot");
        child = find("Hierarchy child");
        var grandchild = find("Hierarchy grandchild");
        check(World.EntityCount == count && World.Get<Transform>(child).Parent == pivot && World.Get<Transform>(grandchild).Parent == child, "undo delete restores hierarchy");
        check(_app.Selected == pivot, "selection follows undo (by id)");
        yield return 0;

        // ---------- 複製 (中の参照は複製どうし) ----------
        var copy = _app.Duplicate();
        var copyChildren = Hierarchy.GetChildren(World, copy);
        check(World.EntityCount == count + 3 && copyChildren.Count == 1 && copyChildren[0] != child, "duplicate with children");
        check(World.Get<EntityInfo>(copy).Name == "Hierarchy pivot (copy)" && World.Get<EntityInfo>(copy).Id != World.Get<EntityInfo>(pivot).Id, "duplicate has new id");
        _app.Undo();
        check(World.EntityCount == count, "undo duplicate");
        yield return 0;

        // ---------- 親を変える ----------
        var lone = World.Entities.First(e => World.Has<MeshRenderer>(e) && World.Has<Transform>(e) && World.Get<Transform>(e).Parent.IsNull && e != pivot);
        var loneId = World.Get<EntityInfo>(lone).Id;
        yield return 0;
        var loneWorld = World.Get<Transform>(lone).WorldPosition;
        _app.SetParent(lone, find("Hierarchy grandchild"));
        yield return 0;
        yield return 0;
        lone = SceneSerializer.FindEntity(World, loneId);
        check(World.Get<Transform>(lone).Parent == find("Hierarchy grandchild") && Vector3.Distance(World.Get<Transform>(lone).WorldPosition, loneWorld) < 1e-3f, "set parent keeps world position");
        check(!_app.SetParent(find("Hierarchy pivot"), lone), "cycle is refused");
        _app.Undo();
        check(World.Get<Transform>(lone).Parent.IsNull, "undo set parent");
        yield return 0;

        // ---------- 消した物を参照していた外のコンポーネント (Joint) を繋ぎ直す ----------
        var a = World.CreateEntity("Joint A");
        World.Add(a, new Transform(new Vector3(0, 50, 0)));
        var b = World.CreateEntity("Joint B");
        World.Add(b, new Transform(new Vector3(2, 50, 0)));
        World.Add(a, Joint.Distance(b, Vector3.Zero, Vector3.Zero, 2.0f));
        var bId = World.Get<EntityInfo>(b).Id;
        yield return 0;
        _app.Selected = b;
        _app.DeleteSelected();
        _app.Undo();
        var restoredB = SceneSerializer.FindEntity(World, bId);
        check(!restoredB.IsNull && World.Get<Joint>(a).ConnectedBody == restoredB, "undo delete reconnects joint reference");
        World.DestroyEntity(a);
        World.DestroyEntity(restoredB);
        yield return 0;

        // ---------- コンポーネントを足す / 外す / 値 (インスペクターと同じ ComponentEdit) ----------
        var spinTarget = find("Hierarchy grandchild");
        var targetId = World.Get<EntityInfo>(spinTarget).Id;
        World.Add(spinTarget, new AudioSource());
        undo.Record(new ComponentEdit(targetId, typeof(AudioSource), null, SceneSerializer.SaveComponent(World, spinTarget, typeof(AudioSource))));
        _app.Undo();
        yield return 0;
        check(!World.Has<AudioSource>(find("Hierarchy grandchild")), "undo add component");
        _app.Redo();
        check(World.Has<AudioSource>(find("Hierarchy grandchild")), "redo add component");
        _app.Undo();
        var spinType = gameType("Spin");
        var spinBefore = SceneSerializer.SaveComponent(World, spinTarget, spinType);
        setField(spinTarget, "Spin", "DegreesPerSecond", 999.0f);
        undo.Record(new ComponentEdit(targetId, spinType, spinBefore, SceneSerializer.SaveComponent(World, spinTarget, spinType)));
        _app.Undo();
        var spinValue = getField(find("Hierarchy grandchild"), "Spin", "DegreesPerSecond");
        _app.Redo();
        check(spinValue != 999.0f && getField(find("Hierarchy grandchild"), "Spin", "DegreesPerSecond") == 999.0f, "undo / redo component value");
        _app.Undo();
        yield return 0;

        // ---------- 全部戻すと元のシーンと同じ ----------
        check(sameScene(original, SceneSerializer.Save(World), out var detail), $"after undo everything, scene equals original ({detail})");

        // ---------- 再生して止めると元に戻る ----------
        var spinning = World.Entities.First(e => has(e, "Spin") && World.Has<Transform>(e) && getField(e, "Spin", "DegreesPerSecond") != 0.0f);
        var spinningId = World.Get<EntityInfo>(spinning).Id;
        var rotation = World.Get<Transform>(spinning).Rotation;
        _app.Selected = spinning;
        var snapshot = SceneSerializer.Save(World);
        _app.TogglePlay();
        check(_app.IsPlaying && _app.Engine.IsPlaying, "play");
        for (int i = 0; i < 30; i++) yield return 0;
        var rotated = World.Get<Transform>(SceneSerializer.FindEntity(World, spinningId)).Rotation;
        check(Quaternion.Dot(rotated, rotation) < 0.999999f, "systems run while playing (Spin rotated)");
        _app.TogglePause();
        yield return 0;
        var paused = World.Get<Transform>(SceneSerializer.FindEntity(World, spinningId)).Rotation;
        for (int i = 0; i < 5; i++) yield return 0;
        check(World.Get<Transform>(SceneSerializer.FindEntity(World, spinningId)).Rotation == paused, "pause stops systems");
        _app.Step();
        yield return 0;
        yield return 0;
        yield return 0;
        check(World.Get<Transform>(SceneSerializer.FindEntity(World, spinningId)).Rotation != paused && !_app.Engine.IsPlaying, "step advances one frame");
        _app.CreateEntity(Entity.Null);   // 再生中の変更 (止めると消える)
        _app.Selected = SceneSerializer.FindEntity(World, spinningId);
        var undoCount = undo.Count;
        _app.TogglePlay();
        check(!_app.IsPlaying && !_app.Engine.IsPlaying, "stop");
        check(sameScene(snapshot, SceneSerializer.Save(World), out detail), $"stop restores the scene ({detail})");
        check(_app.Selected == SceneSerializer.FindEntity(World, spinningId) && !_app.Selected.IsNull, "selection kept after stop");
        check(undo.Count == undoCount, "changes while playing are not recorded");
        yield return 0;

        // ---------- 置いて動かして保存 → 開き直すと同じ ----------
        var placed = _app.CreateEntity(Entity.Null);
        World.Get<EntityInfo>(placed).Name = "Placed box";
        var box = World.Entities.First(e => World.Has<MeshRenderer>(e) && World.Get<MeshRenderer>(e).Model.IsValid);
        World.Add(placed, new MeshRenderer(World.Get<MeshRenderer>(box).Model));
        _app.WriteTransform(placed, new Vector3(1.5f, 2.0f, -3.25f), Quaternion.CreateFromYawPitchRoll(0.3f, 0.2f, 0.1f), new Vector3(0.5f));
        yield return 0;
        var path = Path.Combine(Path.GetTempPath(), "ge_editor_test.scene.json");
        _app.SaveSceneTo(path);
        var saved = SceneSerializer.Save(World);
        _app.OpenScene(path);
        yield return 0;
        check(sameScene(saved, SceneSerializer.Save(World), out detail), $"save and reopen gives the same scene ({detail})");
        File.Delete(path);

        // ---------- クリックで選ぶ (モデルの真ん中を通る光線) ----------
        var picker = new Picker(_app.Engine);
        int hits = 0, tried = 0;
        var eye = new Vector3(0, 20, -25);
        foreach (var e in World.Entities.ToList())
        {
            if (!World.Has<MeshRenderer>(e) || !World.Has<Transform>(e) || !picker.TryGetBounds(World.Get<MeshRenderer>(e).Model, out var min, out var max)) continue;
            var center = Vector3.Transform((min + max) * 0.5f, Hierarchy.GetWorldMatrix(World, e));
            var picked = picker.Pick(eye, Vector3.Normalize(center - eye), out _);
            tried++;
            if (picked == e || (World.Has<Transform>(picked) && Hierarchy.IsAncestor(World, picked, e)) || Hierarchy.IsAncestor(World, e, picked)) hits++;
        }
        Console.WriteLine($"[Test] pick : {hits} / {tried} models hit by a ray through their center (others are hidden behind something)");
        check(tried > 0 && hits * 2 >= tried, "pick models");
        check(picker.Pick(eye, Vector3.UnitY, out _).IsNull, "pick sky gives nothing");
        var pickStart = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int i = 0; i < 10; i++) picker.Pick(eye, Vector3.Normalize(new Vector3(0, -0.6f, 1)), out _);
        Console.WriteLine($"[Test] pick time {System.Diagnostics.Stopwatch.GetElapsedTime(pickStart).TotalMilliseconds / 10:F2} ms / click ({World.EntityCount} entities)");
        yield return 0;

        // ---------- ED5 : アセット ----------
        var database = _app.Database;
        var boxAsset = database.Find("Assets/Models/obj/box.obj");
        check(boxAsset != null && boxAsset.Kind == AssetKind.Model, "asset database knows box.obj");
        check(_app.Engine.Assets.LoadModel("../Model/obj/box.obj").Path == "Assets/Models/obj/box.obj", "old sample path is read as the asset path");

        // Assets にファイルを置くと一覧に出る (.meta が付く)
        var testFolder = database.FullPath("Assets/_EditorTest");
        if (Directory.Exists(testFolder)) Directory.Delete(testFolder, true);
        Directory.CreateDirectory(testFolder);
        foreach (var name in new[] { "box.obj", "box.mtl", "grass.jpg", "gravel.jpg" }) File.Copy(database.FullPath($"Assets/Models/obj/{name}"), Path.Combine(testFolder, name));
        AssetEntry? added = null;
        for (int i = 0; i < 300 && (added = database.Find("Assets/_EditorTest/box.obj")) == null; i++) yield return 0;
        check(added != null && File.Exists(Path.Combine(testFolder, "box.obj.meta")), "new file in Assets appears with a .meta (file watcher)");
        if (added == null) yield break;

        // 置く (シーンビューへ落とすのと同じ) → 元に戻す / やり直す
        var countBefore = World.EntityCount;
        var placedModel = _app.PlaceModel(added, new Vector3(0, 0, 3), Entity.Null);
        check(!placedModel.IsNull && World.Get<MeshRenderer>(placedModel).Model.Path == "Assets/_EditorTest/box.obj", "place model asset");
        _app.Undo();
        check(World.EntityCount == countBefore, "undo place");
        _app.Redo();
        placedModel = _app.Selected;
        yield return 0;

        // シーンには Id で書く → ファイルを .meta ごと名前を変えても、開き直すと見つかる
        var scenePath = Path.Combine(Path.GetTempPath(), "ge_editor_test_assets.scene.json");
        _app.SaveSceneTo(scenePath);
        check(File.ReadAllText(scenePath).Contains(added.Guid.ToString()), "scene stores the asset id");
        File.Move(Path.Combine(testFolder, "box.obj"), Path.Combine(testFolder, "renamed.obj"));
        File.Move(Path.Combine(testFolder, "box.obj.meta"), Path.Combine(testFolder, "renamed.obj.meta"));
        database.Refresh();
        check(database.Find(added.Guid)?.Path == "Assets/_EditorTest/renamed.obj", "renamed file keeps its id");
        _app.OpenScene(scenePath);
        var reopened = World.Entities.FirstOrDefault(e => World.Has<MeshRenderer>(e) && World.Get<MeshRenderer>(e).Model.Path == "Assets/_EditorTest/renamed.obj");
        check(!reopened.IsNull, "reopened scene finds the renamed model by id");
        File.Delete(scenePath);
        Directory.Delete(testFolder, true);
        File.Delete(testFolder + ".meta");
        database.Refresh();
        yield return 0;

        // ---------- ED5 後半 : マテリアル / 読み直し / 解放 ----------
        var assets = _app.Engine.Assets;
        var graphics = _app.Engine.Graphics;
        var folder2 = database.FullPath("Assets/_EditorTest2");
        if (Directory.Exists(folder2)) Directory.Delete(folder2, true);
        Directory.CreateDirectory(folder2);
        foreach (var name in new[] { "box.obj", "box.mtl", "grass.jpg", "gravel.jpg" }) File.Copy(database.FullPath($"Assets/Models/obj/{name}"), Path.Combine(folder2, name));
        database.Refresh();
        var boxEntry = database.Find("Assets/_EditorTest2/box.obj")!;
        var placedBox = _app.PlaceModel(boxEntry, new Vector3(2, 0, 2), Entity.Null);
        var placedBoxId = World.Get<EntityInfo>(placedBox).Id;
        yield return 0;

        // マテリアルを作って付ける → 次のフレームで C++ の描画オブジェクトに付く
        var material = assets.CreateMaterialAsset("Assets/_EditorTest2", "Test Material");
        check(File.Exists(database.FullPath(material.Path)) && database.Find(material.Path)?.Kind == AssetKind.Material, "create material asset");
        check(_app.AssignMaterial(placedBox, database.Find(material.Path)!), "assign material");
        yield return 0;
        yield return 0;
        check(World.Get<MeshRenderer>(placedBox).IsMaterialApplied, "material is applied to the render object");
        material.Color = new Vector4(1.0f, 0.2f, 0.1f, 1.0f);
        material.Texture = "Assets/_EditorTest2/gravel.jpg";
        material.Roughness = 0.25f;
        material.Apply();
        material.Save();
        using var reread = new Material();
        reread.FromJson(File.ReadAllText(database.FullPath(material.Path)));
        check(reread.Color == material.Color && reread.Texture == material.Texture && reread.Roughness == 0.25f, "material file round trip");
        check(File.ReadAllText(database.FullPath(material.Path)).Contains(database.Find("Assets/_EditorTest2/gravel.jpg")!.Guid.ToString()), "material stores the texture id");
        _app.Undo();
        check(World.Get<MeshRenderer>(SceneSerializer.FindEntity(World, placedBoxId)).Material == null, "undo assign material");
        _app.Redo();
        placedBox = SceneSerializer.FindEntity(World, placedBoxId);
        var materialScene = Path.Combine(Path.GetTempPath(), "ge_editor_test_material.scene.json");
        _app.SaveSceneTo(materialScene);
        _app.OpenScene(materialScene);
        yield return 0;
        placedBox = SceneSerializer.FindEntity(World, placedBoxId);
        check(World.Get<MeshRenderer>(placedBox).Material == material, "reopened scene uses the same material (by id)");
        yield return 0;
        yield return 0;
        check(World.Get<MeshRenderer>(placedBox).IsMaterialApplied, "material applied after reopen");
        File.Delete(materialScene);

        // モデルの読み直し : 何回読み直しても GPU の頂点 / インデックスの量は増えない (返した所を使い回す)
        var bytesBefore = graphics.MeshBytesUsed;
        var reloads = assets.ReloadCount;
        for (int i = 0; i < 10; i++) assets.ReloadModel("Assets/_EditorTest2/box.obj");
        check(assets.ReloadCount == reloads + 10 && graphics.MeshBytesUsed == bytesBefore, $"reload model x10 keeps mesh memory ({bytesBefore} -> {graphics.MeshBytesUsed} bytes)");
        yield return 0;

        // ファイルを書き換えると読み直す (見張り) : モデル → rock.obj の中身に / テクスチャ → 別の画像に
        reloads = assets.ReloadCount;
        File.Copy(database.FullPath("Assets/Models/obj/rock.obj"), Path.Combine(folder2, "box.obj"), true);
        for (int i = 0; i < 300 && assets.ReloadCount == reloads; i++) yield return 0;
        check(assets.ReloadCount > reloads, "changed model file is reloaded (file watcher)");
        check(_app.Engine.Assets.LoadedModels.First(m => m.Path == "Assets/_EditorTest2/box.obj").IsValid, "reloaded model is valid");
        reloads = assets.ReloadCount;
        File.Copy(database.FullPath("Assets/Models/obj/Rock030_1K_Color.jpg"), Path.Combine(folder2, "gravel.jpg"), true);
        for (int i = 0; i < 300 && assets.ReloadCount == reloads; i++) yield return 0;
        check(assets.ReloadCount > reloads, "changed texture file is reloaded (material uses it)");

        // シェーダーの読み直し (中身は変えない : 全部コンパイルできる)
        check(_app.Shaders != null && _app.Shaders.Reload(), "reload shaders");
        for (int i = 0; i < 3; i++) yield return 0;   // 次のフレームで PSO を作り直して描ける

        // 使っていない物の解放 → 減る。もう一度読むと読める
        World.DestroyEntity(placedBox);
        yield return 0;
        var modelsBefore = graphics.LoadedModelCount;
        bytesBefore = graphics.MeshBytesUsed;
        var (unloadedModels, unloadedMaterials) = _app.UnloadUnused();
        check(unloadedModels >= 1 && unloadedMaterials >= 1 && graphics.LoadedModelCount < modelsBefore && graphics.MeshBytesUsed < bytesBefore,
              $"unload unused ({unloadedModels} models, {unloadedMaterials} materials, {bytesBefore} -> {graphics.MeshBytesUsed} bytes)");
        var bytesAfterUnload = graphics.MeshBytesUsed;
        check(assets.LoadModel("Assets/_EditorTest2/box.obj").IsValid && graphics.MeshBytesUsed <= bytesBefore, "load again after unload reuses the freed memory");
        assets.UnloadUnused(World);
        check(graphics.MeshBytesUsed == bytesAfterUnload, "unload again returns to the same size");

        Directory.Delete(folder2, true);
        File.Delete(folder2 + ".meta");
        database.Refresh();
        yield return 0;

        // ---------- ED6 : RenderSettings / Volume / ProjectSettings ----------
        var countBeforeSettings = World.EntityCount;
        var settingsEntity = _app.SelectRenderSettings();
        check(World.EntityCount == countBeforeSettings + 1 && World.Has<RenderSettings>(settingsEntity), "create render settings");
        check(_app.SelectRenderSettings() == settingsEntity, "render settings is selected, not created twice");
        ref var rs = ref World.Get<RenderSettings>(settingsEntity);
        rs.Exposure = 2.0f;
        rs.Path = RenderPath.Deferred;
        rs.Ssao.Intensity = 0.7f;
        rs.Shadows.CascadeCount = 2;
        rs.BackgroundColor = new Vector3(0.5f, 0.1f, 0.1f);
        yield return 0;
        yield return 0;
        check(graphics.Exposure == 2.0f && graphics.Ssao.Intensity == 0.7f && graphics.Shadows.CascadeCount == 2 && graphics.BackgroundColor == new Vector3(0.5f, 0.1f, 0.1f),
              "render settings are applied to Graphics");
        check(graphics.Pipeline is GameEngine.Rendering.DeferredRenderPipeline, "render path switches to Deferred");
        graphics.Exposure = 3.0f;   // キーやコードで変えた値は、設定を変えるまでそのまま
        yield return 0;
        check(graphics.Exposure == 3.0f, "code changes stay until the settings change");
        World.Get<RenderSettings>(settingsEntity).Exposure = 2.0f;
        yield return 0;

        // 保存して開き直すと同じ (入れ子の値も)
        var settingsScene = Path.Combine(Path.GetTempPath(), "ge_editor_test_settings.scene.json");
        _app.SaveSceneTo(settingsScene);
        graphics.Exposure = 1.0f;
        _app.OpenScene(settingsScene);
        yield return 0;
        yield return 0;
        settingsEntity = World.Entities.First(e => World.Has<RenderSettings>(e));
        var reopenedSettings = World.Get<RenderSettings>(settingsEntity);
        check(reopenedSettings.Exposure == 2.0f && reopenedSettings.Path == RenderPath.Deferred && reopenedSettings.Ssao.Intensity == 0.7f && reopenedSettings.Shadows.CascadeCount == 2,
              "render settings round trip (nested values)");
        check(graphics.Exposure == 2.0f && graphics.Pipeline is GameEngine.Rendering.DeferredRenderPipeline, "reopened scene applies its render settings");
        File.Delete(settingsScene);

        // Volume : 箱 (原点、辺 10 m、外側 4 m で混ざる)。中 → 上書き、混ざる所 → 間、外 → 元の値
        var volumeEntity = _app.CreateVolume(VolumeShape.Box);
        World.Get<Transform>(volumeEntity).Position = new Vector3(0, 50, 0);
        World.Get<Transform>(volumeEntity).MarkDirty();
        ref var vol = ref World.Get<Volume>(volumeEntity);
        vol.Size = new Vector3(10);
        vol.BlendDistance = 4.0f;
        vol.OverrideExposure = true;
        vol.Exposure = 6.0f;
        _app.SetEditorCamera(new Vector3(0, 50, 0));
        yield return 0;
        yield return 0;
        check(MathF.Abs(graphics.Exposure - 6.0f) < 1e-4f, $"inside the volume ({graphics.Exposure})");
        _app.SetEditorCamera(new Vector3(7, 50, 0));   // 箱の面から 2 m 外 = 重み 0.5
        yield return 0;
        yield return 0;
        check(MathF.Abs(graphics.Exposure - 4.0f) < 1e-3f, $"half way in the blend distance ({graphics.Exposure})");
        _app.SetEditorCamera(new Vector3(20, 50, 0));
        yield return 0;
        yield return 0;
        check(MathF.Abs(graphics.Exposure - 2.0f) < 1e-4f, $"outside the volume ({graphics.Exposure})");
        World.Get<Volume>(volumeEntity).Shape = VolumeShape.Global;
        yield return 0;
        check(MathF.Abs(graphics.Exposure - 6.0f) < 1e-4f, "global volume works everywhere");

        // 設定と Volume を消すと既定の値に戻る
        World.DestroyEntity(volumeEntity);
        World.DestroyEntity(settingsEntity);
        yield return 0;
        yield return 0;
        check(graphics.Exposure == 1.0f && graphics.Pipeline is GameEngine.Rendering.ForwardRenderPipeline && graphics.Ssao.Intensity == 1.5f,
              "removing the settings returns to defaults");

        // ProjectSettings : engine へ書く / JSON の読み書き (プロジェクトのファイルには書かない)
        var project = new ProjectSettings();
        project.Physics.Gravity = new Vector3(0, -3.0f, 0);
        project.FixedDeltaTime = 1.0f / 120.0f;
        project.LayerNames[1] = "Player";
        project.LayerNames[2] = "Enemy";
        project.SetLayerCollision(1, 2, false);
        project.Apply(_app.Engine);
        check(_app.Engine.Physics.Solver.Gravity == new Vector3(0, -3.0f, 0) && Math.Abs(Time.FixedDeltaTime - 1.0f / 120.0f) < 1e-7f, "project settings apply physics and time");
        check(!_app.Engine.Physics.Available || (!_app.Engine.Physics.GetLayerCollision(1, 2) && !_app.Engine.Physics.GetLayerCollision(2, 1) && _app.Engine.Physics.GetLayerCollision(1, 1)),
              "project settings apply the layer collision table");
        var json = project.ToJson();
        var parsed = ProjectSettings.FromJson(json);
        check(parsed.ToJson() == json && parsed.LayerName(1) == "Player" && !parsed.GetLayerCollision(2, 1) && parsed.LayerName(5) == "Layer 5", "project settings JSON round trip");
        // 入力のアクション (ED6 後半) : 既定の名前 / JSON にキーの名前で書く / 知らない名前は false と 0
        check(project.InputActions.Any(a => a.Name == "Jump") && project.InputAxes.Any(a => a.Name == "Horizontal"), "default input actions and axes");
        check(json.Contains("\"Space\"") && parsed.InputActions.First(a => a.Name == "Jump").Keys.SequenceEqual(new[] { Keys.Space }), "input actions are written by key names");
        check(InputActions.ActionNames.Contains("Jump", StringComparer.OrdinalIgnoreCase) && !InputActions.Get("Jump") && InputActions.GetAxis("Horizontal") == 0.0f, "input actions are applied");
        check(!InputActions.Get("NoSuchAction") && InputActions.GetAxis("NoSuchAxis") == 0.0f, "unknown action / axis gives false / 0");
        _app.ProjectSettingsWindow.Settings.Apply(_app.Engine);   // 元の設定に戻す
        check(_app.Engine.Physics.Solver.Gravity == new Vector3(0, -9.81f, 0) && _app.Engine.Physics.GetLayerCollision(1, 2) == _app.ProjectSettingsWindow.Settings.GetLayerCollision(1, 2),
              "restore project settings");
        yield return 0;

        // 確認用の表示 (ED6 後半) : 全部入れて数フレーム描いても落ちない
        _app.Engine.Physics.ShowJoints = true;
        _app.Engine.Physics.ShowContacts = true;
        graphics.Shadows.ShowCascades = true;
        for (int i = 0; i < 3; i++) yield return 0;
        _app.Engine.Physics.ShowJoints = false;
        _app.Engine.Physics.ShowContacts = false;
        graphics.Shadows.ShowCascades = false;
        check(true, "debug displays draw");

        // AudioListener / カメラのコンポーネントが一覧に出る (分類付き)
        var known = ComponentRegistry.KnownComponents().ToList();
        check(known.Any(t => t.Type == typeof(AudioListener) && t.Category == "Audio") && known.Any(t => t.Type == typeof(FreeCamera) && t.Category == "Camera"),
              "audio listener / camera components are listed");
        yield return 0;

        // ---------- ED9 : スクリプトリファレンス ----------
        //   ゲームの型を出したまま、この後の ED7 の読み直しを通す (リファレンスがゲームの型を持ち続けると、古い Game.dll が解放されない)
        var reference = _app.Reference;
        var referenceEntries = reference.Entries.ToList();
        check(referenceEntries.Any(e => e.Name == "Collider" && e.Group.StartsWith("Components/Engine")) && referenceEntries.Any(e => e.Name == "Spin" && e.Group == "Components/Game") &&
              referenceEntries.Any(e => e.Name == "SpinSystem" && e.Group == "Systems/Game") && referenceEntries.Any(e => e.Name == "World" && e.Group == "Engine API") &&
              referenceEntries.Any(e => e.Name == "ComponentEditor<T>" && e.Group == "Editor API"),
              $"reference : engine / game components, systems, engine and editor API ({referenceEntries.Count} types)");
        var colliderDoc = reference.DocFor(typeof(Collider));
        var spinDoc = reference.DocFor(gameType("Spin"));
        check(colliderDoc is { } cd && cd.Summary.Contains("コライダー") && !cd.Summary.Contains("====") && cd.Members >= 3 && spinDoc is { Summary.Length: > 0 },
              $"reference : descriptions from the source comments (Collider : {colliderDoc?.Members} members, game : {(spinDoc?.Summary.Length > 0 ? "ok" : "none")})");
        var colliderUsage = reference.usageOf(typeof(Collider));
        check(colliderUsage.Contains("world.Add(entity, new Collider") && colliderUsage.Contains("Collider.Box(") && colliderUsage.Contains("world.ForEach") &&
              reference.usageOf(typeof(ISystem)).Contains("[GameSystem]") && reference.usageOf(typeof(ComponentEditor<>)).Contains("override"),
              "reference : usage examples (component / system / editor extension)");
        check(ScriptReference.kindOf(typeof(Collider)) == "struct (component)" && ScriptReference.nameOf(typeof(ComponentEditor<>)) == "ComponentEditor<T>" &&
              _app.OpenReference(gameType("Spin")) && reference.Selected == "G:GameEngine_console.Spin", "reference : open a game component from the inspector");
        yield return 0;
        yield return 0;

        // ---------- ED7 : ゲームのコード (Game.dll) / 読み直し ----------
        var scripts = _app.Scripts;
        check(scripts.Assembly != null && scripts.SystemCount >= 6 && known.Any(t => t.Type.FullName == "GameEngine_console.Spin"), $"game scripts are loaded ({scripts.SystemCount} systems)");
        var scriptFile = Path.Combine(database.ProjectRoot, "Scripts", "_EditorTestBouncer.cs");
        const string bouncerV1 = """
            using System.Numerics;
            using GameEngine;
            namespace GameEngine_console;
            public struct TestBouncer : IComponent { public float Speed; }
            [GameSystem]
            public sealed class TestBouncerSystem : ISystem
            {
                public void Update(World world) => world.ForEach((Entity _, ref Transform t, ref TestBouncer b) => t.Position += new Vector3(0, b.Speed * Time.DeltaTime, 0));
            }
            """;
        // 1. 新しいコンポーネントを書いて保存 → ビルド → 読み直し (エディターを閉じない)
        // 確認の側が古い DLL の型を持っていると解放されないので、手放しておく
        known = null!;
        spinType = null!;
        var loads = scripts.LoadCount;
        var bouncerHost = find("Hierarchy pivot");
        var hostId = World.Get<EntityInfo>(bouncerHost).Id;
        File.WriteAllText(scriptFile, bouncerV1);
        var waitStart = Time.TotalTime;
        while (scripts.LoadCount == loads && Time.TotalTime - waitStart < 90) yield return 0;
        check(scripts.LoadCount == loads + 1 && ComponentRegistry.Find("GameEngine_console.TestBouncer") != null, $"new component is usable after saving the file ({scripts.LastBuildMs:F0} ms build)");
        // 捨てた AssemblyLoadContext の解放は非同期 (すぐには終わらないことがある) ので、フレームを進めながら 3 秒まで待つ
        var unloaded = false;
        waitStart = Time.TotalTime;
        while (!(unloaded = scripts.PreviousUnloaded() == true) && Time.TotalTime - waitStart < 3.0) yield return 0;
        check(unloaded, $"the previous Game.dll is unloaded (collectible, {Time.TotalTime - waitStart:F2} s)");
        bouncerHost = SceneSerializer.FindEntity(World, hostId);
        var bouncerType = gameType("TestBouncer");
        World.GetPool(bouncerType).SetBoxed(bouncerHost, bouncerType.GetConstructor(Type.EmptyTypes)?.Invoke(null) ?? Activator.CreateInstance(bouncerType)!);
        setField(bouncerHost, "TestBouncer", "Speed", 3.0f);
        var countBeforeReload = World.EntityCount;

        // 2. コードを書き換える (フィールドを足す) → 読み直しても値は引き継ぐ
        loads = scripts.LoadCount;
        File.WriteAllText(scriptFile, bouncerV1.Replace("public float Speed; }", "public float Speed; public float Extra; }"));
        waitStart = Time.TotalTime;
        while (scripts.LoadCount == loads && Time.TotalTime - waitStart < 90) yield return 0;
        bouncerHost = SceneSerializer.FindEntity(World, hostId);
        check(scripts.LoadCount == loads + 1 && World.EntityCount == countBeforeReload && has(bouncerHost, "TestBouncer") && getField(bouncerHost, "TestBouncer", "Speed") == 3.0f,
              "values are kept across a reload");
        check(gameType("TestBouncer").GetField("Extra") != null && gameType("TestBouncer") != bouncerType, "the new type is used after the reload");
        bouncerType = null!;   // 古い DLL の型を手放す (確認の側が持つと解放されない)
        check(_app.Selected == SceneSerializer.FindEntity(World, _app.SelectedId) , "selection is kept across a reload");

        // 3. 再生すると新しい System が動く
        var y0 = World.Get<Transform>(bouncerHost).Position.Y;
        _app.TogglePlay();
        for (int i = 0; i < 20; i++) yield return 0;
        var y1 = World.Get<Transform>(SceneSerializer.FindEntity(World, hostId)).Position.Y;
        _app.TogglePlay();
        check(y1 > y0 + 0.01f, $"the new game system runs while playing ({y0:F3} -> {y1:F3})");
        yield return 0;

        // 4. ビルドのエラー : 前の Game.dll のまま
        loads = scripts.LoadCount;
        File.WriteAllText(scriptFile, bouncerV1 + "\nthis is not C#");
        waitStart = Time.TotalTime;
        while (scripts.LastError == null && Time.TotalTime - waitStart < 90) yield return 0;
        check(scripts.LastError != null && scripts.LoadCount == loads && ComponentRegistry.Find("GameEngine_console.TestBouncer") != null, "build errors keep the previous Game.dll");

        // 5. 片付け : コンポーネントを外してファイルを消す → 読み直し
        bouncerHost = SceneSerializer.FindEntity(World, hostId);
        World.GetPool(gameType("TestBouncer")).Remove(bouncerHost.Id);
        File.Delete(scriptFile);
        waitStart = Time.TotalTime;
        while (scripts.LoadCount == loads && Time.TotalTime - waitStart < 90) yield return 0;
        check(scripts.LoadCount == loads + 1 && scripts.LastError == null && ComponentRegistry.Find("GameEngine_console.TestBouncer") == null, "removing the file removes the type");
        yield return 0;

        // ---------- ED7 後半 : エディターの拡張 (GameEditor.dll) ----------
        check(scripts.EditorAssembly != null && EditorExtensions.EditorFor(gameType("Spin")) != null && EditorExtensions.EditorFor(gameType("OrbitMover")) != null,
              "custom inspectors are loaded (after reloads too)");
        check(EditorExtensions.Windows.Any(w => w.Path == "Game/Game Stats") && EditorExtensions.MenuItems.Any(m => m.Path == "Game/Create Spinning Box") &&
              EditorExtensions.ImporterFor("Assets/x/colors.palette.txt") != null, "custom window / menu item / importer are loaded");

        // ギズモ : 見本の周回する物の道 (全部) + 選んだ回る物の輪
        _app.SetEditorCamera(new Vector3(0, 6, -12), 0.0f, 20.0f);
        _app.Selected = Entity.Null;
        yield return 0;
        yield return 0;
        var orbitGizmos = _app.LastGizmoCount;
        _app.Selected = World.Entities.First(e => has(e, "Spin") && World.Has<Transform>(e));
        yield return 0;
        yield return 0;
        check(orbitGizmos > 0 && _app.LastGizmoCount > orbitGizmos, $"gizmos are drawn (orbits {orbitGizmos}, + selected spin {_app.LastGizmoCount - orbitGizmos})");

        // メニューの項目 : カメラの前に回る箱 → 元に戻す
        var countBeforeMenu = World.EntityCount;
        EditorExtensions.MenuItems.First(m => m.Path == "Game/Create Spinning Box").Method.Invoke(null, null);
        var spinningBox = _app.Selected;
        check(World.EntityCount == countBeforeMenu + 1 && World.Get<EntityInfo>(spinningBox).Name == "Spinning Box" && has(spinningBox, "Spin") &&
              getField(spinningBox, "Spin", "DegreesPerSecond") == 90.0f && World.Has<MeshRenderer>(spinningBox), "menu item creates an entity");
        _app.Undo();
        check(World.EntityCount == countBeforeMenu, "undo the menu item");

        // EditorApi.Modify : 元に戻せる値の変更
        var modified = find("Hierarchy pivot");
        var oldPosition = World.Get<Transform>(modified).Position;
        EditorApi.Modify<Transform>(modified, t => t with { Position = t.Position + new Vector3(0, 5, 0) });
        var movedY = World.Get<Transform>(modified).Position.Y;
        _app.Undo();
        check(movedY == oldPosition.Y + 5 && World.Get<Transform>(find("Hierarchy pivot")).Position == oldPosition, "EditorApi.Modify is undoable");

        // 独自の取り込み : .palette.txt を置くと、同じフォルダにマテリアルができる / 書き換えると直る
        var paletteFolder = database.FullPath("Assets/_EditorTest3");
        if (Directory.Exists(paletteFolder)) Directory.Delete(paletteFolder, true);
        Directory.CreateDirectory(paletteFolder);
        File.WriteAllText(Path.Combine(paletteFolder, "colors.palette.txt"), "Red #FF0000\nSky #3399FF\n");
        waitStart = Time.TotalTime;
        while ((database.Find("Assets/_EditorTest3/Sky.material.json") == null) && Time.TotalTime - waitStart < 20) yield return 0;
        var sky = database.Find("Assets/_EditorTest3/Sky.material.json") != null ? _app.Engine.Assets.LoadMaterial("Assets/_EditorTest3/Sky.material.json") : null;
        check(sky != null && MathF.Abs(sky.Color.Y - 0.6f) < 0.01f && database.Find("Assets/_EditorTest3/Red.material.json") != null, "palette importer creates materials");
        File.WriteAllText(Path.Combine(paletteFolder, "colors.palette.txt"), "Red #FF0000\nSky #00FF00\n");
        waitStart = Time.TotalTime;
        while (sky != null && sky.Color.Y < 0.99f && Time.TotalTime - waitStart < 20) yield return 0;
        check(sky != null && sky.Color.Y > 0.99f && sky.Color.Z < 0.01f, "changing the palette updates the material");
        _app.Engine.Assets.UnloadUnused(World);
        Directory.Delete(paletteFolder, true);
        File.Delete(paletteFolder + ".meta");
        database.Refresh();
        yield return 0;

        // ---------- ED8 : プレハブ ----------
        var prefabFolder = database.FullPath("Assets/Prefabs");
        var hadPrefabFolder = Directory.Exists(prefabFolder);
        var countBeforePrefab = World.EntityCount;
        var rootA = find("Hierarchy pivot");
        var prefab = _app.CreatePrefab(rootA);
        check(prefab != null && File.Exists(database.FullPath(prefab.Path)) && prefab.EntityIds.Count == 3 &&
              EditorWorld.WithDescendants(World, rootA).All(e => World.Has<PrefabLink>(e)), "create a prefab from a subtree (3 entities)");
        if (prefab == null) yield break;
        var prefabAsset = database.Find(prefab.Path)!;
        check(prefabAsset.Kind == AssetKind.Prefab && !prefab.Json.Contains("PrefabLink"), "prefab asset (no links inside)");

        // 2 つ目を置く : 親子も繋がる、根はその場所
        var rootB = _app.InstantiatePrefab(prefabAsset, new Vector3(6, 0, 0), Entity.Null);
        yield return 0;
        yield return 0;
        var childA = Hierarchy.GetChildren(World, rootA)[0];
        var childB = Hierarchy.GetChildren(World, rootB)[0];
        var grandA = Hierarchy.GetChildren(World, childA)[0];
        var grandB = Hierarchy.GetChildren(World, childB)[0];
        check(World.EntityCount == countBeforePrefab + 3 && World.Get<Transform>(rootB).Position == new Vector3(6, 0, 0) &&
              World.Get<EntityInfo>(childB).Name == "Hierarchy child" && World.Get<PrefabLink>(childB).Source == World.Get<PrefabLink>(childA).Source &&
              World.Get<EntityInfo>(rootB).Id != World.Get<EntityInfo>(rootA).Id, "instantiate the prefab (children connected, new ids)");
        check(_app.PrefabRootOf(grandB) == rootB && _app.PrefabRootOf(childA) == rootA, "find the instance root");

        // A で変えた値は印が付く
        var childT = World.Get<Transform>(childA);
        _app.WriteTransform(childA, childT.Position, childT.Rotation, new Vector3(2));
        undo.EndFrame(editing: false);
        check(World.Get<PrefabLink>(childA).IsOverridden("GameEngine.Transform.Scale") && !World.Get<PrefabLink>(childA).IsOverridden("GameEngine.Transform.Position"),
              "an edit is recorded as an override (only the changed field)");

        // B で変えて Apply : プレハブが変わり、A は自分で変えた値 (Scale) を保つ / 変えていない値は受け取る
        var childBT = World.Get<Transform>(childB);
        _app.WriteTransform(childB, childBT.Position, childBT.Rotation, new Vector3(3));
        var grandBT = World.Get<Transform>(grandB);
        _app.WriteTransform(grandB, grandBT.Position + new Vector3(0, 1, 0), grandBT.Rotation, grandBT.Scale);
        undo.EndFrame(editing: false);
        check(_app.ApplyPrefab(childB), "apply");
        yield return 0;
        check(World.Get<Transform>(childA).Scale == new Vector3(2) && World.Get<Transform>(childB).Scale == new Vector3(3),
              "apply : the other instance keeps its override");
        check(MathF.Abs(World.Get<Transform>(grandA).Position.Y - World.Get<Transform>(grandB).Position.Y) < 1e-5f, "apply : the other instance receives non-overridden values");
        check(World.Get<PrefabLink>(childB).Overrides.Length == 0 && World.Get<Transform>(rootB).Position == new Vector3(6, 0, 0) && World.Get<Transform>(rootA).Position != new Vector3(6, 0, 0),
              "apply clears overrides, root positions stay");

        // Revert : A の変えた値を捨てる
        _app.RevertPrefab(childA);
        check(World.Get<Transform>(childA).Scale == new Vector3(3) && World.Get<PrefabLink>(childA).Overrides.Length == 0, "revert");

        // B に子を足して Apply → A にも増える
        var countBeforeAdd = World.EntityCount;
        var addedChild = _app.CreateEntity(childB);
        World.Get<EntityInfo>(addedChild).Name = "Added child";
        _app.ApplyPrefab(rootB);
        yield return 0;
        check(prefab.EntityIds.Count == 4 && World.EntityCount == countBeforeAdd + 2 &&
              Hierarchy.GetChildren(World, childA).Any(e => World.Get<EntityInfo>(e).Name == "Added child"), "a child added and applied appears in the other instance");

        // 保存して開き直す → 置いた物は繋がったまま / 閉じている間にプレハブを書き換えても、開くと効く
        var prefabScene = Path.Combine(Path.GetTempPath(), "ge_editor_test_prefab.scene.json");
        _app.SaveSceneTo(prefabScene);
        var prefabFile = database.FullPath(prefab.Path);
        File.WriteAllText(prefabFile, File.ReadAllText(prefabFile).Replace("\"Hierarchy grandchild\"", "\"Renamed grandchild\""));
        _app.OpenScene(prefabScene);
        yield return 0;
        var renamed = World.Entities.Count(e => World.Get<EntityInfo>(e).Name == "Renamed grandchild");
        check(World.Entities.Count(e => World.Has<PrefabLink>(e)) == 8 && renamed == 2, $"reopen : links kept and prefab changes applied ({renamed} renamed)");
        File.Delete(prefabScene);

        // 置くのを元に戻す / ゲームのコードから置く (Prefab.Instantiate)
        var countBeforePlace = World.EntityCount;
        _app.InstantiatePrefab(prefabAsset, new Vector3(-6, 0, 0), Entity.Null);
        _app.Undo();
        check(World.EntityCount == countBeforePlace, "undo instantiate");
        var runtimeRoot = _app.Engine.Assets.LoadPrefab(prefabAsset.Guid)!.Instantiate(World, new Vector3(0, 0, 8));
        check(!runtimeRoot.IsNull && World.Get<PrefabLink>(runtimeRoot).Source == prefab.RootId && World.EntityCount == countBeforePlace + 4, "Prefab.Instantiate from game code");

        // Unpack : 繋がりを切る
        var unpackRoot = World.Entities.First(e => World.Has<PrefabLink>(e) && World.Get<PrefabLink>(e).Source == prefab.RootId && e != runtimeRoot);
        _app.UnpackPrefab(unpackRoot);
        check(EditorWorld.WithDescendants(World, unpackRoot).All(e => !World.Has<PrefabLink>(e)), "unpack");

        // 片付け
        File.Delete(prefabFile);
        File.Delete(prefabFile + ".meta");
        if (!hadPrefabFolder) { Directory.Delete(prefabFolder, true); File.Delete(prefabFolder + ".meta"); }
        database.Refresh();
        yield return 0;

        // ---------- ED8 後半 : ゲームの書き出し ----------
        //   一時フォルダへ書き出す (プロジェクトの BuildSettings.json は変えない) → 入っている物を確かめる →
        //   書き出した exe を決まった時間刻みで N フレーム動かした様子と、エディターで同じだけ再生した様子を比べる
        // 物理の世界のやり直し (BeginPlay) の確認用のシーン : showcase の Entity 0 を、落ちて回る物に押され続ける剛体にした物
        //   以前は、エディターで前に別のシーンを使った後だと物理の体の番号が変わり、書き出したゲームと 1 cm ずれた
        var determinismFolder = "Assets/_DeterminismTest";   // 名前に "Editor" を入れない (書き出しに「エディターの物」が無いかの確認が名前で見るため)
        Directory.CreateDirectory(database.FullPath(determinismFolder));
        _app.OpenScene(database.FullPath("Assets/Scenes/showcase.scene.json"));
        yield return 0;
        var faller = World.Entities.First(e => World.Get<EntityInfo>(e).Name == "Entity 0");
        if (World.Has<Static>(faller)) World.Remove<Static>(faller);
        World.Set(faller, new Rigidbody());
        World.Get<Transform>(faller).Position = new Vector3(0, 1.74f, 0);
        World.Get<Transform>(faller).MarkDirty();
        var rigidScene = $"{determinismFolder}/showcase_rigid.scene.json";
        _app.SaveSceneTo(database.FullPath(rigidScene));
        database.Refresh();
        yield return 0;

        // ---------- SH1 : プリミティブ ----------
        var shapes = Enum.GetValues<PrimitiveShape>();
        var primitivesOk = true;
        var primitiveReport = new List<string>();
        foreach (var shape in shapes)
        {
            // 面の向き : 三角形の表 (cross(b - a, c - a)) が外を向いている (凸の形は中心から外 / トーラスは管の中心から外 / 平面は上 / クアッドは -Z)
            var mesh = Primitives.Build(shape);
            var outward = 0;
            for (int t = 0; t < mesh.Indices.Count; t += 3)
            {
                var p0 = mesh.Positions[(int)mesh.Indices[t]]; var p1 = mesh.Positions[(int)mesh.Indices[t + 1]]; var p2 = mesh.Positions[(int)mesh.Indices[t + 2]];
                var face = Vector3.Cross(p1 - p0, p2 - p0);
                var center = (p0 + p1 + p2) / 3;
                var expected = shape switch
                {
                    PrimitiveShape.Plane => Vector3.UnitY,
                    PrimitiveShape.Quad  => -Vector3.UnitZ,
                    PrimitiveShape.Torus => center - Vector3.Normalize(new Vector3(center.X, 0, center.Z)) * 0.35f,
                    _ => center,
                };
                if (Vector3.Dot(face, expected) > 0) outward++;
            }
            var triangles = mesh.Indices.Count / 3;
            primitiveReport.Add($"{shape} {mesh.Positions.Count}v/{triangles}t");
            if (outward != triangles || triangles == 0) { primitivesOk = false; Console.WriteLine($"[Test]   {shape} : {triangles - outward} triangles face inward"); }
        }
        check(primitivesOk, $"primitive meshes face outward ({string.Join(", ", primitiveReport)})");

        var primitiveCount = World.EntityCount;
        var createdPrimitives = shapes.Select(shape => _app.CreatePrimitive(shape, Entity.Null)).ToList();
        var renderers = createdPrimitives.Select(e => World.Get<MeshRenderer>(e)).ToList();
        check(World.EntityCount == primitiveCount + shapes.Length && renderers.All(r => r.Model.IsValid && r.Model.Path.StartsWith("builtin:")) &&
              World.Get<Collider>(createdPrimitives[0]).Shape == ColliderShape.Box && World.Get<Collider>(createdPrimitives[1]).Shape == ColliderShape.Sphere &&
              World.Get<Collider>(createdPrimitives[(int)PrimitiveShape.Torus]).Shape == ColliderShape.Mesh,
              "GameObject > 3D Object creates every primitive with a fitting collider");
        check(_app.Engine.Assets.LoadModel("builtin:Cube").Equals(renderers[0].Model) && _app.Engine.Assets.LoadModel("builtin:Sphere:8").Path == "builtin:Sphere:8" &&
              Primitives.Build(PrimitiveShape.Sphere, 8).Positions.Count < Primitives.Build(PrimitiveShape.Sphere).Positions.Count,
              "the same primitive is shared; a segment count makes a different mesh");
        var unknownRejected = false;
        try { _app.Engine.Assets.LoadModel("builtin:Teapot"); } catch (ArgumentException) { unknownRejected = true; }
        check(unknownRejected, "an unknown primitive is refused");
        _app.Undo();
        check(World.EntityCount == primitiveCount + shapes.Length - 1, "undo create primitive");
        _app.Redo();

        // 保存して開き直す : パスだけ (ファイルの Id は無い) / 書き出し (下) でも同じ形を作る
        var primitiveScene = $"{determinismFolder}/primitives.scene.json";
        _app.SaveSceneTo(database.FullPath(primitiveScene));
        var primitiveJson = File.ReadAllText(database.FullPath(primitiveScene));
        _app.OpenScene(database.FullPath(primitiveScene));
        yield return 0;
        check(primitiveJson.Contains("\"path\": \"builtin:Torus\"") && World.Entities.Count(e => World.Has<MeshRenderer>(e) && World.Get<MeshRenderer>(e).Model.Path.StartsWith("builtin:")) == shapes.Length,
              "a scene with primitives saves and reopens");
        database.Refresh();
        yield return 0;

        // ---------- SH2 : 自分のシェーダー ----------
        var opaqueShaderPath = $"{determinismFolder}/Bands.hlsl";
        File.WriteAllText(database.FullPath(opaqueShaderPath), string.Join("\n",
            "// @param color     Tint   (1, 0.5, 0.25, 1)",
            "// @param float     Bands  4 range(1, 16)",
            "// @param texture   Mask   white",
            "// @param texture3d Volume builtin:ValueNoise3D:16",
            "float4 Surface(SurfaceInput IN)",
            "{",
            "    float stripe = step(0.5f, frac(IN.LocalPosition.y * Bands));",
            "    float noise  = Volume.Sample(g_Sampler, IN.LocalPosition + 0.5f).r;",
            "    float3 albedo = ToLinear(Tint.rgb) * Mask.Sample(g_Sampler, IN.UV).rgb * (0.5f + 0.5f * stripe) * (0.5f + noise);",
            "    return float4(Lit(albedo, IN.Normal, IN.WorldPosition, 0.0f, 0.6f), 1.0f);",
            "}"));
        var glassShaderPath = $"{determinismFolder}/Glass.hlsl";
        File.WriteAllText(database.FullPath(glassShaderPath), string.Join("\n",
            "// @queue transparent",
            "// @cull none",
            "// @param color Tint (0.3, 0.7, 1, 0.4)",
            "float4 Surface(SurfaceInput IN) { return float4(ToLinear(Tint.rgb) * (0.6f + 0.4f * abs(IN.Normal.y)), Tint.a); }"));
        database.Refresh();
        var bands = _app.Engine.Assets.LoadShader(opaqueShaderPath);
        var glass = _app.Engine.Assets.LoadShader(glassShaderPath);
        check(bands.IsValid && bands.Error == null && bands.Parameters.Count == 4 && bands.Parameters[1].Range == new Vector2(1, 16) &&
              bands.Parameters[0].DefaultValue == new System.Numerics.Vector4(1, 0.5f, 0.25f, 1) && bands.Parameters[3].Type == ShaderParamType.Texture3D &&
              glass.IsValid && glass.Transparent && glass.Blend == ShaderBlend.Alpha && !glass.DepthWrite && glass.Cull == ShaderCull.None,
              $"shaders compile and their parameters / settings are read ({string.Join(", ", bands.Parameters.Select(p => $"{p.Name}:{p.Type}"))})");

        // マテリアル : シェーダーと値 (ファイルに書いて読み直しても同じ)
        var bandsMaterial = _app.Engine.Assets.CreateMaterialAsset(determinismFolder, "Bands");
        bandsMaterial.Shader = opaqueShaderPath;
        bandsMaterial.ShaderValues["Bands"] = 6.0f;
        bandsMaterial.ShaderValues["Tint"] = new System.Numerics.Vector4(0.2f, 0.8f, 0.4f, 1);
        bandsMaterial.Apply();
        bandsMaterial.Save();
        var glassMaterial = _app.Engine.Assets.CreateMaterialAsset(determinismFolder, "Glass");
        glassMaterial.Shader = glassShaderPath;
        glassMaterial.Apply();
        glassMaterial.Save();
        var rereadMaterial = new Material();
        rereadMaterial.FromJson(File.ReadAllText(database.FullPath(bandsMaterial.Path)));
        check(rereadMaterial.Shader == opaqueShaderPath && rereadMaterial.ShaderValues["Bands"] is float b6 && b6 == 6.0f &&
              rereadMaterial.ShaderValues["Tint"] is System.Numerics.Vector4 tint && tint.Y == 0.8f && bandsMaterial.ShaderAsset == bands,
              "a material keeps its shader and values (save / load)");
        rereadMaterial.Dispose();

        // シーン : 自分のシェーダーの立方体 2 個 + 半透明の球 1 個 → 3 個を自分のシェーダーで描く
        _app.RequestNewScene();
        if (_app.PendingPrompt != null) _app.ResolvePrompt(UnsavedChoice.DontSave);
        var customCubeA = _app.CreatePrimitive(PrimitiveShape.Cube, Entity.Null);
        var customCubeB = _app.CreatePrimitive(PrimitiveShape.Cube, Entity.Null);
        var customSphere = _app.CreatePrimitive(PrimitiveShape.Sphere, Entity.Null);
        World.Get<Transform>(customCubeA).Position = new Vector3(-1.5f, 0.5f, 0);
        World.Get<Transform>(customCubeB).Position = new Vector3(0, 0.5f, 0);
        World.Get<Transform>(customSphere).Position = new Vector3(1.5f, 0.5f, 0);
        foreach (var e in new[] { customCubeA, customCubeB, customSphere }) World.Get<Transform>(e).MarkDirty();
        _app.AssignMaterial(customCubeA, database.Find(bandsMaterial.Path)!);
        _app.AssignMaterial(customCubeB, database.Find(bandsMaterial.Path)!);
        _app.AssignMaterial(customSphere, database.Find(glassMaterial.Path)!);
        for (int i = 0; i < 3; i++) yield return 0;
        var customDraws = _app.Engine.Graphics.CustomDrawCount;
        check(customDraws == 3 && _app.Engine.Assets.LoadTexture3D("builtin:ValueNoise3D:16") != 0 && _app.Engine.Assets.LoadTexture3D("builtin:Nothing3D") == 0,
              $"objects with shader materials are drawn by their shaders (opaque + transparent : {customDraws}), 3D noise texture");

        // コンパイルのエラー : ファイルの行番号が出る / 前の物で描き続ける / 直すと戻る
        var goodSource = File.ReadAllText(database.FullPath(opaqueShaderPath));
        File.WriteAllText(database.FullPath(opaqueShaderPath), goodSource.Replace("float stripe", "float stripe = undefinedThing;\n    float stripe2"));
        var reloadFailed = !_app.Engine.Assets.ReloadShader(opaqueShaderPath);
        for (int i = 0; i < 2; i++) yield return 0;
        var errorText = bands.Error ?? "";
        check(reloadFailed && bands.IsValid && errorText.Contains("Bands.hlsl:7:") && errorText.Contains("undefinedThing") && _app.Engine.Graphics.CustomDrawCount == 3,
              $"a compile error names the file and line, and the previous version keeps drawing ({Inspector.firstErrors(errorText).Split('\n')[0]})");
        var versionBefore = bands.Version;
        File.WriteAllText(database.FullPath(opaqueShaderPath), goodSource.Replace("// @param float     Bands  4 range(1, 16)", "// @param float     Bands  4 range(1, 16)\n// @param float     Glow   0.5"));
        check(_app.Engine.Assets.ReloadShader(opaqueShaderPath) && bands.Error == null && bands.Version == versionBefore + 1 && bands.Parameters.Count == 5,
              "fixing the file recompiles it (a new parameter appears)");

        // シェーダーを外すと共通のシェーダーに戻る
        glassMaterial.Shader = null;
        glassMaterial.Apply();
        for (int i = 0; i < 2; i++) yield return 0;
        check(_app.Engine.Graphics.CustomDrawCount == 2, "removing the shader returns the object to the standard path");
        glassMaterial.Shader = glassShaderPath;
        glassMaterial.Apply();
        glassMaterial.Save();
        var customScene = $"{determinismFolder}/custom.scene.json";
        _app.SaveSceneTo(database.FullPath(customScene));
        var customDependencies = _app.BuildWindow.Builder.CollectAssets(new BuildSettings { Scenes = { customScene } });
        check(customDependencies.Assets.Contains(opaqueShaderPath) && customDependencies.Assets.Contains(glassShaderPath) && customDependencies.Assets.Contains(bandsMaterial.Path),
              "the build includes the shaders that the scene's materials use");
        database.Refresh();
        yield return 0;

        var builder = _app.BuildWindow.Builder;
        var buildFolder = Path.Combine(Path.GetTempPath(), "ge_editor_test_build");
        if (Directory.Exists(buildFolder)) Directory.Delete(buildFolder, true);
        var buildSettings = new BuildSettings
        {
            ProductName = "Test Game", Width = 640, Height = 360, VSync = false, OutputFolder = buildFolder,
            Scenes = { "Assets/Scenes/showcase.scene.json", "Assets/Scenes/physics.scene.json", "Assets/Scenes/particles.scene.json", rigidScene, primitiveScene, customScene },
        };
        var dependencies = builder.CollectAssets(buildSettings);
        var included = dependencies.Assets;
        check(included.Contains("Assets/Models/obj/box.obj") && included.Contains("Assets/Models/obj/box.mtl") && included.Contains("Assets/Models/obj/rock.obj") &&
              included.Any(a => a.EndsWith("Tomimi.pmx")) && included.Any(a => a.EndsWith("/tex/face.png")),
              $"dependencies : models, .mtl and pmx textures ({included.Count} assets)");
        check(!included.Any(a => a.EndsWith("ReadMe.txt") || a.EndsWith(".vmd")), "dependencies : unused assets are not included");
        check(!dependencies.Warnings.Any(w => w.Contains("builtin:")), "dependencies : primitives need no files (no warnings)");   // SH1
        var buildTask = Task.Run(() => builder.Build(buildSettings, dependencies));
        while (!buildTask.IsCompleted) yield return 0;
        var build = buildTask.Result;
        check(build.Ok && File.Exists(build.ExePath), $"build the game ({(build.Ok ? $"{build.FileCount} files, {build.Bytes / 1024 / 1024} MB, {build.TotalMs / 1000:F1} s" : build.Error)})");
        if (!build.Ok) yield break;
        var builtFiles = Directory.EnumerateFiles(buildFolder, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(buildFolder, f).Replace('\\', '/')).ToList();
        check(!builtFiles.Any(f => f.Contains("Editor", StringComparison.OrdinalIgnoreCase) || f.Contains("ImGui", StringComparison.OrdinalIgnoreCase) || f.EndsWith(".pdb")) &&
              builtFiles.Contains("Game.dll") && builtFiles.Contains("RHI.dll") && builtFiles.Contains("Shader/hlslTemplate.shader") && builtFiles.Contains("Data/player.json"),
              "the build has the player, engine, game code and shaders, and no editor files");
        var builtAssets = builtFiles.Where(f => f.StartsWith("Data/Assets/") && !f.EndsWith(".meta") && !f.EndsWith(".dds")).ToList();
        check(builtAssets.Count == included.Count && builtAssets.All(f => builtFiles.Contains(f + ".meta")), $"only used assets, each with its .meta ({builtAssets.Count} of {database.Entries.Count(e => e.Kind != AssetKind.Folder)})");

        // 書き出したゲームと、エディターの再生を比べる (決まった時間刻みで 120 フレーム)
        const int compareFrames = 120;
        var dt = Time.FixedDeltaTime;
        foreach (var compareScene in buildSettings.Scenes)
        {
            var dumpFile = Path.Combine(buildFolder, "dump.txt");
            var info = new System.Diagnostics.ProcessStartInfo(build.ExePath) { UseShellExecute = false };
            foreach (var argument in new[] { "--scene", compareScene, "--capture-dt", dt.ToString("R", System.Globalization.CultureInfo.InvariantCulture),
                                             "--frames", compareFrames.ToString(), "--dump", dumpFile })
                info.ArgumentList.Add(argument);
            var startedAt = System.Diagnostics.Stopwatch.GetTimestamp();
            var player = System.Diagnostics.Process.Start(info)!;
            while (!player.HasExited && System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalSeconds < 60) yield return 0;
            if (!player.HasExited) player.Kill();
            var playerLog = File.Exists(Path.Combine(buildFolder, "Player.log")) ? File.ReadAllText(Path.Combine(buildFolder, "Player.log")) : "";
            var playerDump = File.Exists(dumpFile) ? File.ReadAllText(dumpFile) : "";
            check(player.ExitCode == 0 && playerDump.Length > 0 && playerLog.Contains($"{_app.Scripts.SystemCount} systems"),
                  $"the built game runs {Path.GetFileName(compareScene)} ({System.Diagnostics.Stopwatch.GetElapsedTime(startedAt).TotalSeconds:F1} s, exit {player.ExitCode})");
            if (playerDump.Length == 0) { Console.WriteLine(playerLog); continue; }
            if (compareScene == customScene)   // SH2 : 書き出したゲームも自分のシェーダーをコンパイルできる (DXC も入っている)
                check(!playerLog.Contains("compile failed") && !playerLog.Contains("Failed to load shader"), "the built game compiles the custom shaders");
            // 初めての起動でも、シェーダーは入れたキャッシュから (コンパイルしない)
            if (compareScene == buildSettings.Scenes[0])
                check(build.ShaderCacheFiles > 0 && playerLog.Contains("shader from cache") && !playerLog.Contains("shader compiled"),
                      $"the first start of the built game uses the shipped compiled shaders ({build.ShaderCacheFiles} files)");

            // エディター : 同じシーンを開いて、同じ刻みで同じフレーム数だけ再生する
            _app.OpenScene(database.FullPath(compareScene));
            yield return 0;
            Time.CaptureDeltaTime = dt;
            _app.TogglePlay();
            for (int i = 0; i < compareFrames; i++) yield return 0;
            var editorDump = SceneState.Dump(World);
            _app.TogglePlay();
            Time.CaptureDeltaTime = 0;
            var differences = new List<string>();
            var (differenceCount, maxError) = SceneState.Compare(editorDump, playerDump, 1e-4f, differences);            foreach (var difference in differences) Console.WriteLine($"[Test]   {difference}");
            check(differenceCount == 0, $"the built game moves the same as play in the editor : {Path.GetFileName(compareScene)} ({World.EntityCount} entities, max error {maxError:G3})");
            File.Delete(dumpFile);
        }
        Directory.Delete(buildFolder, true);
        _app.OpenScene(database.FullPath("Assets/Scenes/showcase.scene.json"));
        yield return 0;
        Directory.Delete(database.FullPath(determinismFolder), true);
        File.Delete(database.FullPath(determinismFolder) + ".meta");
        database.Refresh();
        yield return 0;

        // ---------- ED10 : 名前を変える ----------
        var io = ImGui.GetIO();
        // 入力の欄に文字を打って Enter / Esc を押す (ImGui の入力の列に積む。本当の窓の入力と同じ道)
        IEnumerable<int> typeAndPress(string text, ImGuiKey key)
        {
            yield return 0;   // 欄にフォーカスが入る
            yield return 0;
            io.AddInputCharactersUTF8(text);   // 全部選ばれているので置き換わる
            yield return 0;
            io.AddKeyEvent(key, true);
            yield return 0;
            io.AddKeyEvent(key, false);
            yield return 0;
        }
        _app.OpenScene(database.FullPath("Assets/Scenes/showcase.scene.json"));
        yield return 0;
        var renameTarget = _app.CreateEntity(Entity.Null);
        var originalName = World.Get<EntityInfo>(renameTarget).Name;
        _app.RenameEntity(renameTarget, "Renamed Entity");
        var renamedOk = World.Get<EntityInfo>(renameTarget).Name == "Renamed Entity";
        _app.Undo();
        var undone = World.Get<EntityInfo>(renameTarget).Name == originalName;
        _app.Redo();
        check(renamedOk && undone && World.Get<EntityInfo>(renameTarget).Name == "Renamed Entity", "rename an entity (undo / redo)");

        // Hierarchy で F2 と同じ : 欄に打って Enter / Esc
        ImGui.SetWindowFocus("Hierarchy");
        _app.BeginRename(renameTarget);
        foreach (var _ in typeAndPress("Typed Name", ImGuiKey.Enter)) yield return 0;
        check(!_app.IsRenaming && World.Get<EntityInfo>(renameTarget).Name == "Typed Name", "rename in the hierarchy : type and Enter");
        _app.BeginRename(renameTarget);
        foreach (var _ in typeAndPress("Cancelled", ImGuiKey.Escape)) yield return 0;
        check(!_app.IsRenaming && World.Get<EntityInfo>(renameTarget).Name == "Typed Name", "rename in the hierarchy : Esc cancels");

        // アセット : マテリアル → 名前を変える (.meta ごと。Id は同じ。読み込んだ物のパスも変わる)
        var renameFolder = "Assets/_EditorTestRename";
        Directory.CreateDirectory(database.FullPath(renameFolder));
        database.Refresh();
        var renameMaterial = _app.Engine.Assets.CreateMaterialAsset(renameFolder, "Rock Paint");
        var materialGuid = database.Find(renameMaterial.Path)!.Guid;
        var oldMaterialPath = renameMaterial.Path;
        var newMaterialPath = _app.Project.Rename(oldMaterialPath, "Stone Paint");
        check(newMaterialPath == $"{renameFolder}/Stone Paint.material.json" && File.Exists(database.FullPath(newMaterialPath)) &&
              File.Exists(database.FullPath(newMaterialPath) + ".meta") && !File.Exists(database.FullPath(oldMaterialPath)) &&
              database.Find(newMaterialPath)?.Guid == materialGuid && renameMaterial.Path == newMaterialPath,
              "rename an asset (.meta moves, same id, the loaded material follows)");
        check(database.Find(oldMaterialPath)?.Path == newMaterialPath && database.ToAssetPath(oldMaterialPath) == newMaterialPath, "the old path still finds the renamed asset");
        check(_app.Project.Rename(newMaterialPath!, "bad/name") == null && _app.Project.Rename(newMaterialPath!, "  ") == null && File.Exists(database.FullPath(newMaterialPath!)),
              "invalid names are refused");

        // マテリアルを付けたシーンを保存 → フォルダの名前を変える → 開き直しても繋がっている (Id で探す)
        var painted = World.Entities.First(e => World.Has<MeshRenderer>(e));
        var paintedId = World.Get<EntityInfo>(painted).Id;
        _app.AssignMaterial(painted, database.Find(newMaterialPath!)!);
        var renameScene = $"{renameFolder}/Rename Test.scene.json";
        _app.SaveSceneTo(database.FullPath(renameScene));
        database.Refresh();
        var newFolder = _app.Project.Rename(renameFolder, "_EditorTestRenamed");
        var movedScene = $"{newFolder}/Rename Test.scene.json";
        check(newFolder == "Assets/_EditorTestRenamed" && renameMaterial.Path == $"{newFolder}/Stone Paint.material.json" &&
              _app.ScenePath == database.FullPath(movedScene) && !Directory.Exists(database.FullPath(renameFolder)),
              "rename a folder (children, the loaded material and the open scene follow)");
        _app.Project.Rename(movedScene, "Renamed Scene");
        var renamedScene = $"{newFolder}/Renamed Scene.scene.json";
        _app.SaveSceneTo(_app.ScenePath!);
        _app.OpenScene(database.FullPath(renamedScene));
        yield return 0;
        var repainted = SceneSerializer.FindEntity(World, paintedId);
        check(_app.ScenePath == database.FullPath(renamedScene) && World.IsAlive(repainted) &&
              World.Get<MeshRenderer>(repainted).Material?.Path == $"{newFolder}/Stone Paint.material.json",
              "rename the open scene; the saved scene still finds the renamed material");

        // Project で F2 と同じ : タイルの欄に打って Enter
        ImGui.SetWindowFocus("Project");   // 下のタブの中で前に出す
        _app.Project.BeginRename(database.Find(renamedScene)!);
        foreach (var _ in typeAndPress("Typed Scene", ImGuiKey.Enter)) yield return 0;
        check(!_app.Project.IsRenaming && File.Exists(database.FullPath($"{newFolder}/Typed Scene.scene.json")) && _app.ScenePath!.EndsWith("Typed Scene.scene.json"),
              "rename in the project panel : type and Enter (the extension is kept)");

        // ---------- ED10 : 保存していない変更を聞く ----------
        var promptScene = _app.ScenePath!;
        var otherScene = database.FullPath("Assets/Scenes/physics.scene.json");
        check(!_app.IsDirty, "saved scene is not dirty");
        _app.RequestOpenScene(otherScene);   // 変更が無ければ聞かない
        yield return 0;
        check(_app.PendingPrompt == null && _app.ScenePath == otherScene, "no prompt when there are no changes");
        _app.OpenScene(promptScene);
        yield return 0;
        var promptEntity = World.Entities.First(e => World.Get<EntityInfo>(e).Name == "Typed Name");
        _app.RenameEntity(promptEntity, "Unsaved Name");

        // 新しいシーン → 聞く → Cancel : 何もしない
        _app.RequestNewScene();
        yield return 0;
        check(_app.PendingPrompt == "creating a new scene" && ImGui.IsPopupOpen("Unsaved Changes") && _app.ScenePath == promptScene, "changing scenes with changes shows the prompt");
        _app.ResolvePrompt(UnsavedChoice.Cancel);
        yield return 0;
        check(_app.PendingPrompt == null && _app.IsDirty && _app.ScenePath == promptScene && World.IsAlive(promptEntity), "Cancel keeps the scene and the changes");

        // 開く → Don't Save : 保存せずに開く
        _app.RequestOpenScene(otherScene);
        _app.ResolvePrompt(UnsavedChoice.DontSave);
        yield return 0;
        check(_app.ScenePath == otherScene && !_app.IsDirty && !File.ReadAllText(promptScene).Contains("Unsaved Name"), "Don't Save opens the other scene without saving");

        // 新しいシーン → Save : 保存してから
        _app.OpenScene(promptScene);
        yield return 0;
        _app.RenameEntity(World.Entities.First(e => World.Get<EntityInfo>(e).Name == "Typed Name"), "Saved Name");
        _app.RequestNewScene();
        _app.ResolvePrompt(UnsavedChoice.Save);
        yield return 0;
        check(_app.ScenePath == null && File.ReadAllText(promptScene).Contains("Saved Name"), "Save saves, then creates the new scene");

        // 窓の × : 変更があれば閉じずに聞く
        _app.OpenScene(promptScene);
        yield return 0;
        _app.RenameEntity(World.Entities.First(e => World.Get<EntityInfo>(e).Name == "Saved Name"), "Close Test");
        _app.Engine.Window.Close();
        yield return 0;
        check(!_app.Engine.Window.IsDisposed && _app.Engine.Window.Visible && _app.PendingPrompt == "closing the editor", "closing the window with changes shows the prompt");
        _app.ResolvePrompt(UnsavedChoice.Cancel);
        yield return 0;
        check(!_app.Engine.Window.IsDisposed && _app.IsDirty, "Cancel keeps the editor open");

        // 片付け
        _app.OpenScene(database.FullPath("Assets/Scenes/showcase.scene.json"));
        yield return 0;
        Directory.Delete(database.FullPath(newFolder!), true);
        File.Delete(database.FullPath(newFolder!) + ".meta");
        database.Refresh();
        yield return 0;
    }
}
