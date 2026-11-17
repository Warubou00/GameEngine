using System.Numerics;
using System.Text;
using System.Text.Json;

namespace GameEngine.Editor;

// ==========================================================================
// エディターの確認 (v1.14 S1) : SceneManager (シーンの切り替え / 足す / サブシーン / DontDestroyOnLoad)
//   --editor-test --editor-test-section scenes で、これだけ回せる
// ==========================================================================
internal sealed partial class EditorTest
{
    // ゲームのコードの中から読み込みを頼む System (確認の間だけ engine.AddSystem で足す)
    private sealed class SceneRequestSystem : ISystem
    {
        public string? Path;
        public Scene? Requested;
        public bool SawEntityRightAway;
        public void Update(World world)
        {
            if (Path == null || Requested != null) return;
            Requested = SceneManager.Current!.LoadScene(Path, LoadSceneMode.Additive);
            SawEntityRightAway = world.Entities.Any(e => world.Has<EntityInfo>(e) && world.Get<EntityInfo>(e).Name == "OtherThing");
        }
    }

    private static void writeScene(string file, Action<World> build)
    {
        var world = new World();
        build(world);
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        File.WriteAllText(file, SceneSerializer.Save(world), new UTF8Encoding(false));
    }

    private Entity findByName(string name) =>
        World.Entities.FirstOrDefault(e => World.Has<EntityInfo>(e) && World.Get<EntityInfo>(e).Name == name);

    private int countByName(string name) =>
        World.Entities.Count(e => World.Has<EntityInfo>(e) && World.Get<EntityInfo>(e).Name == name);

    private IEnumerable<int> sceneManager()
    {
        var database = _app.Database;
        var engine = _app.Engine;
        var scenes = engine.Scenes;
        var folder = "Assets/_SceneTest";
        var mainPath = $"{folder}/Main.scene.json";
        var subPath = $"{folder}/Sub.scene.json";
        var innerPath = $"{folder}/Inner.scene.json";
        var otherPath = $"{folder}/Other.scene.json";

        // ---------- 確認用のシーン ----------
        //   Inner : InnerA (Transform (1,0,0)) / InnerNoTransform (EntityInfo だけ)
        //   Sub   : SubRoot (Transform (0,0,5)) とその子 SubChild / Nested (SubScene = Inner)
        //   Main  : Holder (Transform (10,0,0), SubScene = Sub) / Standby (SubScene = Inner、待機で読む) / Loop (SubScene = Main : 輪なので読まない)
        //   Other : OtherThing
        writeScene(database.FullPath(innerPath), w =>
        {
            var a = w.CreateEntity("InnerA");
            w.Add(a, new Transform { Position = new Vector3(1, 0, 0), Rotation = Quaternion.Identity, Scale = Vector3.One });
            w.CreateEntity("InnerNoTransform");
        });
        writeScene(database.FullPath(subPath), w =>
        {
            var root = w.CreateEntity("SubRoot");
            w.Add(root, new Transform { Position = new Vector3(0, 0, 5), Rotation = Quaternion.Identity, Scale = Vector3.One });
            var child = w.CreateEntity("SubChild");
            w.Add(child, new Transform { Position = new Vector3(0, 1, 0), Rotation = Quaternion.Identity, Scale = Vector3.One, Parent = root });
            var nested = w.CreateEntity("Nested");
            w.Add(nested, new Transform { Rotation = Quaternion.Identity, Scale = Vector3.One });
            w.Add(nested, new SubScene { Scene = innerPath });
        });
        writeScene(database.FullPath(mainPath), w =>
        {
            var holder = w.CreateEntity("Holder");
            w.Add(holder, new Transform { Position = new Vector3(10, 0, 0), Rotation = Quaternion.Identity, Scale = Vector3.One });
            w.Add(holder, new SubScene { Scene = subPath });
            var standby = w.CreateEntity("Standby");
            w.Add(standby, new Transform { Position = new Vector3(0, 0, -20), Rotation = Quaternion.Identity, Scale = Vector3.One });
            w.Add(standby, new SubScene { Scene = innerPath, ActiveOnLoad = false });
            var loop = w.CreateEntity("Loop");
            w.Add(loop, new SubScene { Scene = mainPath });
        });
        writeScene(database.FullPath(otherPath), w => w.CreateEntity("OtherThing"));
        // S2 : 非同期の確認用。Big = 40 個の物 + 一緒に読むサブシーン (BigNested = Inner) + 後で読むサブシーン (BigRoom = Inner、待機)
        var bigPath = $"{folder}/Big.scene.json";
        writeScene(database.FullPath(bigPath), w =>
        {
            for (int i = 0; i < 40; i++)
            {
                var e = w.CreateEntity($"Big{i}");
                w.Add(e, new Transform { Position = new Vector3(i, 0, 0) });
            }
            var nested = w.CreateEntity("BigNested");
            w.Add(nested, new Transform());
            w.Add(nested, new SubScene { Scene = innerPath });
            var room = w.CreateEntity("BigRoom");
            w.Add(room, new Transform());
            w.Add(room, new SubScene { Scene = innerPath, LoadOnStart = false, ActiveOnLoad = false });
        });
        database.Refresh();

        // ---------- 読む : サブシーンも一緒に (入れ子 / 待機 / 輪) ----------
        _app.OpenScene(database.FullPath(mainPath));   // エディターで開く (停止の後に、このパスをメインシーンとして登録し直す)
        var created = scenes.GetEntities(scenes.ActiveScene!);
        yield return 0;
        yield return 0;
        var holder = findByName("Holder");
        var standby = findByName("Standby");
        var subRoot = findByName("SubRoot");
        check(created.Count == 3 && scenes.ActiveScene?.Name == "Main" && scenes.ActiveScene.Path == mainPath,
              $"LoadScene : the main scene ({created.Count} entities, active {scenes.ActiveScene?.Path})");
        check(scenes.Scenes.Count == 4 && scenes.GetSubScene(holder)?.Children.Count == 1 && scenes.GetSubScene(findByName("Loop")) == null &&
              countByName("InnerA") == 2,
              $"sub scenes load with the scene (nested Inner in Sub, standby Inner, the cycle is skipped : {scenes.Scenes.Count} scenes)");
        check(!subRoot.IsNull && World.Get<Transform>(subRoot).Parent == holder &&
              Vector3.Distance(World.Get<Transform>(findByName("SubChild")).WorldPosition, new Vector3(10, 1, 5)) < 1e-4f,
              "sub scene roots become children of the owner (moved with it)");
        var standbyScene = scenes.GetSubScene(standby)!;
        var standbyMembers = scenes.GetEntities(standbyScene);
        check(standbyScene.State == SceneStatus.Loaded && !standbyScene.IsActive && standbyMembers.Count == 2 && standbyMembers.All(e => !World.IsActive(e)) &&
              World.Get<EntityInfo>(standbyMembers[0]).Active,
              "ActiveOnLoad = false : loaded in standby (members inactive, their own Active untouched)");
        check(World.IsActive(subRoot) && scenes.GetEntities(scenes.GetSubScene(holder)!.Children[0]).All(e => World.IsActive(e)),
              "active sub scenes : members are active (nested too)");

        // ---------- 有効 / 待機の切り替え ----------
        scenes.SetSubSceneActive(standby, true);
        var noTransform = standbyMembers.First(e => !World.Has<Transform>(e));
        check(standbyScene.IsActive && World.IsActive(noTransform), "SetSubSceneActive(true) : takes effect right away");
        yield return 0;
        check(standbyMembers.All(e => World.IsActive(e)), "SetSubSceneActive(true) : all members active after a frame");
        scenes.SetSubSceneActive(standby, false);
        check(standbyMembers.All(e => !World.IsActive(e)) && standbyMembers.All(e => World.IsAlive(e)), "SetSubSceneActive(false) : back to standby (entities kept)");

        // 持ち主を無効にすると、中の物 (Transform の無い物 / 入れ子のサブシーンも) が無効
        var nestedScene = scenes.GetSubScene(holder)!.Children[0];
        World.SetActive(holder, false);
        yield return 0;
        yield return 0;
        check(!World.IsActive(subRoot) && !nestedScene.IsActive && scenes.GetEntities(nestedScene).All(e => !World.IsActive(e)),
              "disabling the owner disables its sub scene (and nested ones)");
        World.SetActive(holder, true);
        yield return 0;
        yield return 0;
        check(World.IsActive(subRoot) && nestedScene.IsActive && scenes.GetEntities(nestedScene).All(e => World.IsActive(e)), "enabling the owner again");

        // ---------- 保存 : サブシーンの中の物は書かない ----------
        using (var saved = JsonDocument.Parse(SceneSerializer.Save(World)))
        {
            var names = saved.RootElement.GetProperty("entities").EnumerateArray().Select(e => e.GetProperty("name").GetString()).ToList();
            check(names.Count == 3 && names.Contains("Holder") && !names.Contains("SubRoot") && !names.Contains("InnerA"),
                  $"saving the main scene skips sub scene members ({string.Join(", ", names)})");
        }

        // ---------- 外す ----------
        var subScene = scenes.GetSubScene(holder)!;
        scenes.UnloadSubScene(holder);
        check(subScene.State == SceneStatus.Unloaded && nestedScene.State == SceneStatus.Unloaded && findByName("SubRoot").IsNull && countByName("InnerA") == 1 &&
              scenes.Scenes.Count == 2, $"UnloadSubScene removes members and nested scenes ({scenes.Scenes.Count} scenes left)");
        scenes.LoadSubScene(holder);
        check(!findByName("SubRoot").IsNull && countByName("InnerA") == 2, "LoadSubScene loads it again");
        World.DestroyEntity(standby);
        yield return 0;
        check(standbyScene.State == SceneStatus.Unloaded && countByName("InnerA") == 1, "destroying the owner unloads its sub scene");

        // ---------- ゲームのコードの中からの読み込みは、次のフレームの頭で ----------
        _app.TogglePlay();
        var requester = new SceneRequestSystem { Path = otherPath };
        engine.AddSystem(requester);
        yield return 0;
        check(requester.Requested?.State == SceneStatus.Loading && !requester.SawEntityRightAway && countByName("OtherThing") == 0,
              $"LoadScene from game code is deferred (state {requester.Requested?.State})");
        yield return 0;
        check(requester.Requested?.State == SceneStatus.Loaded && countByName("OtherThing") == 1 && scenes.ActiveScene?.Name == "Main",
              "... and loaded at the start of the next frame (Additive keeps the main scene)");
        engine.RemoveSystems(new object[] { requester });
        _app.TogglePlay();
        yield return 0;
        check(countByName("OtherThing") == 0 && !findByName("SubRoot").IsNull && countByName("InnerA") == 1,
              "stop restores the scene and reloads its sub scenes");

        // ---------- 切り替え (Single) と DontDestroyOnLoad ----------
        engine.LoadScene(database.FullPath(mainPath));
        var keeper = World.CreateEntity("Keeper");
        World.Add(keeper, new Transform { Rotation = Quaternion.Identity, Scale = Vector3.One });
        var keeperChild = World.CreateEntity("KeeperChild");
        World.Add(keeperChild, new Transform { Rotation = Quaternion.Identity, Scale = Vector3.One, Parent = keeper });
        var spawned = World.CreateEntity("Spawned");   // コードで作った物 (SceneId 0) : 切り替えで消える
        scenes.DontDestroyOnLoad(keeper);
        string? changed = null;
        Action<Scene?, Scene?> onChanged = (_, now) => changed = now?.Name;
        scenes.ActiveSceneChanged += onChanged;
        scenes.LoadScene(otherPath);
        scenes.ActiveSceneChanged -= onChanged;
        check(World.IsAlive(keeper) && World.IsAlive(keeperChild) && !World.IsAlive(spawned) && findByName("Holder").IsNull && findByName("SubRoot").IsNull &&
              countByName("OtherThing") == 1 && scenes.ActiveScene?.Name == "Other" && changed == "Other" && scenes.Scenes.Count == 1,
              "LoadScene (Single) : old scenes go, DontDestroyOnLoad entities (with children) stay");
        check(scenes.GetScene(keeper) == scenes.Persistent && scenes.GetScene(keeperChild) == scenes.Persistent && World.IsActive(keeper),
              "DontDestroyOnLoad entities are in the persistent scene");
        using (var saved = JsonDocument.Parse(SceneSerializer.Save(World)))
            check(saved.RootElement.GetProperty("entities").GetArrayLength() == 1, "persistent entities are not saved into the main scene");

        // ---------- S2 : 非同期 (LoadSceneAsync) ----------
        // 今 : Other (メイン) + Keeper / KeeperChild (永続)。予算 0 = 1 フレームに 1 手 (何フレームもかかるようにする)
        var budget = scenes.AsyncBudgetMs;
        scenes.AsyncBudgetMs = 0;
        var op = scenes.LoadSceneAsync(bigPath);
        op.AllowActivation = false;
        var completedEvents = 0;
        var awaited = false;
        op.Completed += _ => completedEvents++;
        async void awaitIt(SceneOperation o) { await o; awaited = true; }
        awaitIt(op);
        check(!op.IsDone && op.Progress == 0 && countByName("Big0") == 0 && countByName("OtherThing") == 1,
              "LoadSceneAsync returns at once (nothing created yet)");
        for (int i = 0; i < 6; i++) yield return 0;
        var big0 = findByName("Big0");
        check(!big0.IsNull && !World.IsActive(big0) && op.Progress > 0 && op.Progress < 0.9f && !op.IsReadyToActivate &&
              World.IsActive(findByName("OtherThing")) && scenes.ActiveScene?.Name == "Other",
              $"while loading : new entities exist but are inactive, the old scene keeps running (progress {op.Progress:P0})");
        var waited = 0;
        while (!op.IsReadyToActivate && waited++ < 200) yield return 0;
        check(op.IsReadyToActivate && op.Progress == 0.9f && op.Frames > 20 && countByName("Big39") == 1 && countByName("InnerA") == 1,
              $"loaded in {op.Frames} frames (40 entities + the LoadOnStart sub scene in the same operation), progress 0.9 while waiting");
        for (int i = 0; i < 3; i++) yield return 0;
        check(!op.IsDone && countByName("OtherThing") == 1 && !World.IsActive(findByName("Big39")), "AllowActivation = false : waits (nothing switched)");
        op.AllowActivation = true;
        yield return 0;
        var bigNested = findByName("BigNested");
        check(op.IsDone && !op.IsCancelled && op.Progress == 1 && completedEvents == 1 && awaited && scenes.ActiveScene == op.Scene &&
              countByName("OtherThing") == 0 && World.IsAlive(keeper) && World.IsActive(findByName("Big39")),
              "activation : the old scene goes, new entities become active, Completed / await run");
        yield return 0;
        var nestedInner = scenes.GetSubScene(bigNested);
        check(nestedInner is { State: SceneStatus.Loaded, IsActive: true } && scenes.GetEntities(nestedInner).All(e => World.IsActive(e)) &&
              World.Get<Transform>(findByName("InnerA")).Parent == bigNested,
              "the sub scene loaded with it is active and under its owner");

        // 非同期と同期で、同じ物ができる (Id と値)
        var asyncJson = SceneSerializer.Save(World);
        scenes.LoadScene(bigPath);
        var syncJson = SceneSerializer.Save(World);
        check(sameScene(asyncJson, syncJson, out var sameDetail), $"async and sync loading make the same scene ({sameDetail})");

        // サブシーンを非同期で (待機の BigRoom を有効で)
        var room = findByName("BigRoom");
        var roomOp = scenes.LoadSubSceneAsync(room, active: true)!;
        check(!roomOp.IsDone && scenes.GetSubScene(room)?.State == SceneStatus.Loading && scenes.LoadSubSceneAsync(room) == roomOp,
              "LoadSubSceneAsync : loading (asking again returns the same operation)");
        waited = 0;
        while (!roomOp.IsDone && waited++ < 50) yield return 0;
        var roomScene = scenes.GetSubScene(room);
        check(roomOp.IsDone && roomScene is { IsActive: true } && scenes.GetEntities(roomScene).All(e => World.IsActive(e)) && countByName("InnerA") == 2,
              $"LoadSubSceneAsync : loaded and active ({roomOp.Frames} frames)");
        check(scenes.LoadSubSceneAsync(room) is { IsDone: true } again && again.Scene == roomScene, "LoadSubSceneAsync on a loaded sub scene returns a finished operation");

        // 取りやめ : 読み込み中のシーンを UnloadScene / World.Clear
        var additive = scenes.LoadSceneAsync(bigPath, LoadSceneMode.Additive);
        for (int i = 0; i < 4; i++) yield return 0;
        var midCount = countByName("Big0");
        scenes.UnloadScene(additive.Scene);
        yield return 0;
        check(midCount == 2 && additive.IsDone && additive.IsCancelled && countByName("Big0") == 1 && scenes.ActiveScene?.Name == "Big",
              "UnloadScene while loading cancels it and removes the half-built entities");
        var cleared = scenes.LoadSceneAsync(otherPath, LoadSceneMode.Additive);
        World.Clear();
        yield return 0;
        check(cleared.IsDone && cleared.IsCancelled && countByName("OtherThing") == 0 && scenes.Operations.Count == 0, "World.Clear cancels loading");

        // 1 フレームの予算 (既定 4 ms) : 物が少ないので 1 フレームで終わる
        scenes.AsyncBudgetMs = budget;
        var quick = scenes.LoadSceneAsync(bigPath);
        waited = 0;
        while (!quick.IsDone && waited++ < 50) yield return 0;
        check(quick.IsDone && !quick.IsCancelled && quick.Frames <= 4 && countByName("Big0") == 1,
              $"default budget : {quick.Frames} frames, max {quick.MaxStepMilliseconds:F2} ms per frame");

        // UI : KeepOnSceneChange の Widget だけ、切り替えで残る
        var kept = UI.CreateWidget(new UTextBlock { Text = "kept" });
        kept.KeepOnSceneChange = true;
        kept.AddToViewport(5);
        var dropped = UI.CreateWidget(new UTextBlock { Text = "dropped" });
        dropped.AddToViewport(5);
        scenes.LoadScene(otherPath);
        check(kept.IsInViewport && !dropped.IsInViewport, "KeepOnSceneChange widgets stay across LoadScene (Single)");
        kept.RemoveFromParent();

        // 後片付け
        World.Clear();
        check(scenes.Scenes.Count == 0 && scenes.ActiveScene == null, "World.Clear resets the scene list");
        Directory.Delete(database.FullPath(folder), true);
        if (File.Exists(database.FullPath(folder) + ".meta")) File.Delete(database.FullPath(folder) + ".meta");
        database.Refresh();
        yield return 0;
    }
    // 見本 (v1.14 S2) : Assets/Scenes/scenes_demo.scene.json + SceneLoadingDemo.cs (ロード画面つきの切り替え / サブシーンのキー)
    private IEnumerable<int> sceneDemo()
    {
        var scenes = _app.Engine.Scenes;
        var database = _app.Database;
        var demoPath = "Assets/Scenes/scenes_demo.scene.json";
        if (!File.Exists(database.FullPath(demoPath)) || ComponentRegistry.Find("GameEngine_console.SceneSwitcher") == null)
        {
            check(false, "scene demo : scenes_demo.scene.json / SceneSwitcher (Game.dll) not found");
            yield break;
        }
        var exceptions = _app.GameCodeExceptionCount;
        _app.OpenScene(database.FullPath(demoPath));
        yield return 0;
        var red = findByName("Room Cube Red");
        check(!red.IsNull && World.IsActive(red) && scenes.GetSubScene(findByName("Room")) is { IsActive: true }, "scene demo : the Room sub scene loads with the scene");
        _app.TogglePlay();
        for (int i = 0; i < 3; i++) yield return 0;
        var switcher = findByName("Scene Switcher");
        check(scenes.GetScene(switcher) == scenes.Persistent, "scene demo : the switcher moves itself to DontDestroyOnLoad");

        Input.SimulateKeyPress(System.Windows.Forms.Keys.D1);
        for (int i = 0; i < 2; i++) yield return 0;
        check(!World.IsActive(findByName("Room Cube Red")) && World.IsAlive(findByName("Room Cube Red")), "scene demo : key 1 puts the room on standby");
        Input.SimulateKeyPress(System.Windows.Forms.Keys.D1);
        for (int i = 0; i < 2; i++) yield return 0;
        check(World.IsActive(findByName("Room Cube Red")), "scene demo : key 1 again activates it");
        Input.SimulateKeyPress(System.Windows.Forms.Keys.D2);
        for (int i = 0; i < 3; i++) yield return 0;
        check(countByName("Room Cube Red") == 0, "scene demo : key 2 unloads the room");
        Input.SimulateKeyPress(System.Windows.Forms.Keys.D2);
        var waited = 0;
        while (countByName("Room Cube Red") == 0 && waited++ < 60) yield return 0;
        yield return 0;
        check(countByName("Room Cube Red") == 1 && World.IsActive(findByName("Room Cube Red")), $"scene demo : key 2 again loads it asynchronously ({waited} frames)");

        // N : ロード画面つきで showcase へ
        Input.SimulateKeyPress(System.Windows.Forms.Keys.N);
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        for (int i = 0; i < 2; i++) yield return 0;
        bool screenShown() => UI.Widgets.Any(w => w.Root?.Name == "LoadingRoot");
        check(screenShown() && scenes.Operations.Count == 1, "scene demo : N shows the loading screen and starts LoadSceneAsync");
        var sawScreenAfterSwitch = false;
        waited = 0;
        while (scenes.ActiveScene?.Path != "Assets/Scenes/showcase.scene.json" && waited++ < 1200) yield return 0;
        sawScreenAfterSwitch = screenShown();
        var seconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalSeconds;
        waited = 0;
        while (screenShown() && waited++ < 2000) yield return 0;   // 切り替えた後、少し (0.25 秒) 残してから外す
        check(scenes.ActiveScene?.Path == "Assets/Scenes/showcase.scene.json" && seconds >= 0.5 && World.IsAlive(switcher) && countByName("Room Cube Red") == 0,
              $"scene demo : switched to showcase after {seconds:F2} s (loading screen kept at least 0.5 s), the switcher survives");
        check(sawScreenAfterSwitch && !screenShown(), $"scene demo : the loading screen stays across the switch (KeepOnSceneChange) and is removed {waited} frames later");

        // B : 同期で physics へ
        Input.SimulateKeyPress(System.Windows.Forms.Keys.B);
        for (int i = 0; i < 4; i++) yield return 0;
        check(scenes.ActiveScene?.Path == "Assets/Scenes/physics.scene.json" && World.IsAlive(switcher), "scene demo : B switches synchronously (next frame)");
        check(_app.GameCodeExceptionCount == exceptions, "scene demo : no exceptions in the game code");

        _app.TogglePlay();
        yield return 0;
        check(scenes.ActiveScene?.Path == demoPath && countByName("Room Cube Red") == 1 && countByName("Scene Switcher") == 1 &&
              scenes.GetScene(findByName("Scene Switcher")) == scenes.ActiveScene,
              "scene demo : stop restores the demo scene (room reloaded, switcher back in the scene)");
    }

    // エディター (v1.14 S3) : サブシーンの中の物は見るだけ / 持ち主の削除・複製・元に戻す / シーンをドラッグして置く / 書き出し
    private IEnumerable<int> sceneEditor()
    {
        var scenes = _app.Engine.Scenes;
        var database = _app.Database;
        var demoPath = "Assets/Scenes/scenes_demo.scene.json";
        var roomPath = "Assets/Scenes/scenes_demo_room.scene.json";
        if (!File.Exists(database.FullPath(demoPath)))
        {
            check(false, "scene editor : scenes_demo.scene.json not found");
            yield break;
        }
        var folder = "Assets/_SceneEditTest";
        var altPath = $"{folder}/Alt.scene.json";
        writeScene(database.FullPath(altPath), w => { var a = w.CreateEntity("AltThing"); w.Add(a, new Transform()); });
        database.Refresh();

        _app.OpenScene(database.FullPath(demoPath));
        yield return 0;
        var room = findByName("Room");
        var red = findByName("Room Cube Red");
        check(!_app.IsReadOnly(room) && _app.IsReadOnly(red), "sub scene members are read-only while editing (the owner is not)");

        // 見るだけの物は消せない / 複製できない / 親を変えられない / 名前を変えられない / 値を変えられない
        var undoCount = _app.UndoHistory.Count;
        _app.Selected = red;
        _app.DeleteSelected();
        var duplicated = _app.Duplicate();
        var moved = _app.SetParent(red, Entity.Null);
        _app.RenameEntity(red, "Renamed");
        _app.ModifyComponent<Transform>(red, t => t with { Position = new Vector3(100, 0, 0) });
        check(World.IsAlive(red) && duplicated.IsNull && !moved && World.Get<EntityInfo>(red).Name == "Room Cube Red" &&
              World.Get<Transform>(red).Position.X < 50 && _app.UndoHistory.Count == undoCount,
              "read-only members : delete / duplicate / reparent / rename / edit are refused (nothing recorded)");

        // 持ち主を消す → 中の物も外れる。元に戻すと持ち主が戻り、サブシーンも読み直す (中の物はメインシーンの物にならない)
        _app.Selected = room;
        _app.DeleteSelected();
        yield return 0;
        check(countByName("Room") == 0 && countByName("Room Cube Red") == 0, "deleting the owner removes its sub scene");
        _app.Undo();
        yield return 0;
        room = findByName("Room");
        red = findByName("Room Cube Red");
        check(!room.IsNull && countByName("Room Cube Red") == 1 && _app.IsReadOnly(red) && scenes.GetSubScene(room) is { IsActive: true },
              "undo brings the owner back and reloads its sub scene (members stay in the sub scene)");
        using (var saved = JsonDocument.Parse(SceneSerializer.Save(World)))
            check(!saved.RootElement.GetProperty("entities").EnumerateArray().Any(e => e.GetProperty("name").GetString()!.StartsWith("Room Cube")),
                  "after undo, the members are still not saved into the main scene");

        // 持ち主を複製 → 複製にもサブシーンが読まれる (中の物は JSON に入らない)
        _app.Selected = room;
        var copy = _app.Duplicate();
        yield return 0;
        check(!copy.IsNull && countByName("Room Cube Red") == 2 && scenes.GetSubScene(copy) != null && World.Get<Transform>(findByName("Room Cube Red")).Parent != copy == true,
              "duplicating the owner duplicates only the owner; its sub scene loads on its own");
        _app.Undo();
        yield return 0;
        check(countByName("Room Cube Red") == 1 && countByName("Room (copy)") == 0, "undo duplicate removes the copy and its sub scene");

        // シーンをドラッグして置く (CreateSubScene)。開いているシーン自身は置けない
        var altEntry = database.Find(altPath)!;
        var placed = _app.CreateSubScene(altEntry, Entity.Null);
        yield return 0;
        check(!placed.IsNull && World.Get<SubScene>(placed).Scene == altPath && countByName("AltThing") == 1 && World.Get<Transform>(findByName("AltThing")).Parent == placed,
              "dropping a scene asset creates an owner with SubScene and loads it");
        check(_app.CreateSubScene(database.Find(demoPath)!, Entity.Null).IsNull, "the open scene cannot be placed inside itself");
        // SubScene の Scene を変える → 読み直す
        _app.ModifyComponent<SubScene>(placed, s => s with { Scene = roomPath });
        yield return 0;
        check(countByName("AltThing") == 0 && countByName("Room Cube Red") == 2 && scenes.GetSubScene(placed)?.Path == roomPath,
              "changing SubScene.Scene unloads the old scene and loads the new one");
        _app.Undo();
        yield return 0;
        check(countByName("AltThing") == 1 && countByName("Room Cube Red") == 1, "undo the change loads the old scene again");
        _app.Undo();
        yield return 0;
        check(countByName("AltThing") == 0 && findByName("Alt").IsNull, "undo the placement removes the owner and its sub scene");

        // 名前を変えたシーンのパスを付け替える
        var renamedTo = database.Rename(altPath, "Alt2");
        placed = _app.CreateSubScene(database.Find(renamedTo)!, Entity.Null);
        var renamedBack = database.Rename(renamedTo, "Alt");
        yield return 0;
        check(renamedTo == $"{folder}/Alt2.scene.json" && renamedBack == altPath && World.Get<SubScene>(placed).Scene == altPath, $"renaming the scene asset updates SubScene.Scene ({World.Get<SubScene>(placed).Scene})");
        _app.Undo();
        _app.Undo();
        yield return 0;

        // Inspector / Hierarchy を描く (選んだ物ごとに数フレーム。例外が出ないこと)
        _app.Selected = red;
        for (int i = 0; i < 3; i++) yield return 0;
        _app.Selected = room;
        for (int i = 0; i < 3; i++) yield return 0;

        // 再生中は触れる
        _app.TogglePlay();
        for (int i = 0; i < 3; i++) yield return 0;
        red = findByName("Room Cube Red");
        check(!_app.IsReadOnly(red) && !_app.IsReadOnly(findByName("Scene Switcher")), "while playing, sub scene members and persistent entities can be edited");
        _app.TogglePlay();
        yield return 0;

        // 書き出し : サブシーンのファイルも入る
        var dependencies = new AssetDependencies(database);
        dependencies.AddScene(demoPath);
        check(dependencies.Assets.Contains(roomPath) && dependencies.ReasonsFor(roomPath).Contains(demoPath),
              "Build Game : the sub scene file is included (found from the scene that holds it)");

        Directory.Delete(database.FullPath(folder), true);
        if (File.Exists(database.FullPath(folder) + ".meta")) File.Delete(database.FullPath(folder) + ".meta");
        database.Refresh();
        yield return 0;
    }

    // モデルを別のスレッドで読む (v1.17 S4) : 同期と同じ物ができる / LoadModel が待つ / シーンはモデルを待ってから物を作る / 取りやめても残らない
    private IEnumerable<int> sceneAsyncModels()
    {
        var engine = _app.Engine;
        var assets = engine.Assets;
        var scenes = engine.Scenes;
        var database = _app.Database;
        var showcase = "Assets/Scenes/showcase.scene.json";
        string? pmx = null;
        if (File.Exists(database.FullPath(showcase)))
        {
            using var document = JsonDocument.Parse(File.ReadAllText(database.FullPath(showcase)));
            foreach (var entity in document.RootElement.GetProperty("entities").EnumerateArray())
                foreach (var component in entity.GetProperty("components").EnumerateArray())
                    if (component.TryGetProperty("Model", out var model) && model.ValueKind == JsonValueKind.Object &&
                        model.TryGetProperty("path", out var path) && path.GetString() is { } text && text.EndsWith(".pmx", StringComparison.OrdinalIgnoreCase))
                        pmx = text;
        }
        var obj = "Assets/Models/obj/rock.obj";
        if (pmx == null || !File.Exists(database.FullPath(pmx)) || !File.Exists(database.FullPath(obj)))
        {
            Console.WriteLine("[Test] SKIP async models : the sample models (Assets/Models) are not in this project");
            yield break;
        }
        _app.ProfilerWindows.ShowOverview = false;

        // 同期で読んだ物を覚える
        World.Clear();
        assets.UnloadUnused(World);
        var syncModel = assets.LoadModel(pmx);
        var syncMaterials = assets.GetModelMaterials(syncModel).ToList();
        World.Clear();
        var unloaded = assets.UnloadUnused(World);
        yield return 0;

        // 別のスレッドで読む
        var started = assets.BeginLoadModel(pmx);
        var again = assets.BeginLoadModel(pmx);
        check(unloaded.Models >= 1 && started && again && assets.IsModelLoading(pmx) && assets.PendingModelLoads == 1,
              "BeginLoadModel starts one background load (asking again does not start another)");
        check(!assets.BeginLoadModel("builtin:Cube") && !assets.BeginLoadModel("Assets/Nothing/none.pmx"), "BeginLoadModel refuses primitives / missing files (LoadModel reads them as before)");
        var longest = 0.0;
        var last = System.Diagnostics.Stopwatch.GetTimestamp();
        var frames = 0;
        while (assets.IsModelLoading(pmx) && frames++ < 1200)
        {
            yield return 0;
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            longest = Math.Max(longest, System.Diagnostics.Stopwatch.GetElapsedTime(last, now).TotalMilliseconds);
            last = now;
        }
        var asyncModel = assets.LoadModel(pmx);
        var asyncMaterials = assets.GetModelMaterials(asyncModel).ToList();
        check(asyncModel.IsValid && asyncModel.SubMeshCount == syncModel.SubMeshCount && asyncMaterials.SequenceEqual(syncMaterials),
              $"the background load gives the same model ({asyncModel.SubMeshCount} sub meshes, same materials / textures)");
        check(frames > 1 && longest < 150, $"frames keep going while the model loads in the background ({frames} frames, longest {longest:F1} ms)");

        // 読み込み中に LoadModel → 終わるのを待って同じ物を使う
        World.Clear();
        assets.UnloadUnused(World);
        assets.BeginLoadModel(obj);
        var objModel = assets.LoadModel(obj);
        check(objModel.IsValid && !assets.IsModelLoading(obj) && assets.PendingModelLoads == 0 && assets.LoadModel(obj).Equals(objModel),
              "LoadModel waits for a pending background load and uses it (not loaded twice)");

        // 非同期のシーン : モデルを待ってから物を作る。物を作る手は短い
        World.Clear();
        assets.UnloadUnused(World);
        var op = scenes.LoadSceneAsync(showcase);
        var sawWaiting = false;
        longest = 0.0;
        last = System.Diagnostics.Stopwatch.GetTimestamp();
        frames = 0;
        while (!op.IsDone && frames++ < 1200)
        {
            yield return 0;
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            longest = Math.Max(longest, System.Diagnostics.Stopwatch.GetElapsedTime(last, now).TotalMilliseconds);
            last = now;
            if (assets.PendingModelLoads > 0 && World.EntityCount == 0) sawWaiting = true;
        }
        check(op.IsDone && !op.IsCancelled && sawWaiting && scenes.ActiveScene?.Path == showcase,
              "LoadSceneAsync loads the scene's models in the background before it builds the entities");
        check(op.SlowestEntityMilliseconds < 50 && longest < 150,
              $"no long frame : slowest entity {op.SlowestEntityMilliseconds:F1} ms ('{op.SlowestEntity}'), longest frame {longest:F1} ms, {frames} frames");

        // 取りやめ : 読み込み中のモデルは終わり次第登録され、残らない
        World.Clear();
        assets.UnloadUnused(World);
        var cancelled = scenes.LoadSceneAsync(showcase);
        frames = 0;
        while (assets.PendingModelLoads == 0 && !cancelled.IsDone && frames++ < 300) yield return 0;
        var hadPending = assets.PendingModelLoads > 0;
        World.Clear();
        frames = 0;
        while (assets.PendingModelLoads > 0 && frames++ < 1200) yield return 0;
        yield return 0;
        check(hadPending && cancelled.IsCancelled && assets.PendingModelLoads == 0 && World.EntityCount == 0,
              "cancelling while models load : the operation is cancelled and no background load is left");
        assets.UnloadUnused(World);
        // 最後 : 読み込み中のまま終わる (C++ の Shutdown が待ってから壊す。落ちないこと = 終わりのコードとクラッシュレポートで見る)
        check(assets.BeginLoadModel(pmx), "a background load is still running when the editor quits");
    }

    // 目で見る用 (v1.14 S2。--editor-test-section scenes-demo-hold) : ロード画面を 4 秒出したままにする (窓を写して確かめる)
    private IEnumerable<int> sceneDemoHold()
    {
        _app.OpenScene(_app.Database.FullPath("Assets/Scenes/scenes_demo.scene.json"));
        yield return 0;
        // S3 : 編集中。サブシーンの中の物を選ぶ (Inspector は見るだけ)
        _app.ProfilerWindows.ShowOverview = false;
        _app.Selected = findByName("Room Cube Red");
        Console.WriteLine("[Hold] edit mode");
        var editStart = System.Diagnostics.Stopwatch.GetTimestamp();
        while (System.Diagnostics.Stopwatch.GetElapsedTime(editStart).TotalSeconds < 2.5) yield return 0;
        _app.Selected = Entity.Null;
        _app.TogglePlay();
        for (int i = 0; i < 30; i++) yield return 0;
        setField(findByName("Scene Switcher"), "SceneSwitcher", "MinLoadingSeconds", 4.0f);
        _app.Engine.Graphics.ProfilerVisible = false;   // 真ん中を隠さないように
        _app.ProfilerWindows.ShowOverview = false;
        Console.WriteLine("[Hold] demo scene playing");
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        while (System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalSeconds < 2.5) yield return 0;
        Input.SimulateKeyPress(System.Windows.Forms.Keys.N);
        Console.WriteLine("[Hold] loading screen");
        start = System.Diagnostics.Stopwatch.GetTimestamp();
        while (System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalSeconds < 6.5) yield return 0;
        Console.WriteLine($"[Hold] now {_app.Engine.Scenes.ActiveScene?.Name}");
        check(_app.Engine.Scenes.ActiveScene?.Name == "showcase", "hold : switched");
    }

    // 速さ (v1.14 S2。--editor-test-section scenes-bench-sync / scenes-bench-async、--scene に軽いシーンを渡して起動する)
    //   showcase を初めて読む間の、一番長いフレーム (ms) とかかった時間。アセットがまだ読まれていない状態で比べる
    private IEnumerable<int> sceneBench(bool async)
    {
        var scenes = _app.Engine.Scenes;
        var path = "Assets/Scenes/showcase.scene.json";
        var frameTimes = new List<double>();
        var last = System.Diagnostics.Stopwatch.GetTimestamp();
        var start = last;
        SceneOperation? op = null;
        if (async) op = scenes.LoadSceneAsync(path);
        else scenes.LoadScene(path);
        var frames = 0;
        while ((op != null && !op.IsDone) || frames < 3)
        {
            yield return 0;
            var now = System.Diagnostics.Stopwatch.GetTimestamp();
            frameTimes.Add(System.Diagnostics.Stopwatch.GetElapsedTime(last, now).TotalMilliseconds);
            last = now;
            if (++frames > 2000) break;
        }
        var total = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
        var sorted = frameTimes.OrderBy(t => t).ToList();
        Console.WriteLine($"[Bench] {(async ? "async" : "sync")} showcase : total {total:F0} ms, {frameTimes.Count} frames, longest frame {sorted[^1]:F1} ms, " +
                          $"2nd {(sorted.Count > 1 ? sorted[^2] : 0):F1} ms, median {sorted[sorted.Count / 2]:F1} ms" +
                          (op != null ? $", op frames {op.Frames}, max step {op.MaxStepMilliseconds:F1} ms, prefetched {op.PrefetchedBytes / 1048576.0:F1} MB" : ""));
        check(scenes.ActiveScene?.Path == path, "bench : showcase loaded");
    }
}
