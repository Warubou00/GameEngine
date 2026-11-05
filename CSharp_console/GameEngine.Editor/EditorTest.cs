using System.Numerics;
using System.Text.Json;

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
        Console.WriteLine($"[Test] editor : pass {_pass} / fail {_fail}");
        _app.FrameHook = null;
        _app.Engine.Quit();
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
        check(scripts.PreviousUnloaded() == true, "the previous Game.dll is unloaded (collectible)");
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
    }
}
