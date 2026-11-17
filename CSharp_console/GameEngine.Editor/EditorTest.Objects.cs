using System.Diagnostics;
using System.Numerics;

namespace GameEngine.Editor;

// 確認用 (v1.18) : GameObject のフィールドを持つコンポーネント (保存 / 読み込みで相手が戻るか)
public struct ObjectRefProbe : IComponent
{
    public GameObject Target;
    public GameObject Missing;
    public Entity     Raw;
}

// 確認用 (v1.20 GO3) : コンポーネントへの参照 / プレハブ / シーンへの参照
public struct AssetRefProbe : IComponent
{
    public ComponentRef<Camera> Cam;
    public Prefab? Bullet;
    public SceneReference Next;
}

// ==========================================================================
// エディターの確認 (v1.18 GO1) : GameObject / 名前・タグ・Id・型で探す / タグの保存
//   --editor-test --editor-test-section objects で、これだけ回せる
//   --editor-test-section objects-bench : 1 万個の物で索引と順に見るのを比べる (数を出すだけ)
// ==========================================================================
internal sealed partial class EditorTest
{
    private static Entity named(World w, string name, Entity parent = default)
    {
        var e = w.CreateEntity(name);
        w.Add(e, new Transform { Rotation = Quaternion.Identity, Scale = Vector3.One, Parent = parent });
        return e;
    }

    private IEnumerable<int> gameObjects()
    {
        // ---------- 名前で探す (新しい World) ----------
        var w = new World();
        check(w.Find("Nothing").IsNull, "[objects] Find of a missing name is Null (before any entity)");
        var a = named(w, "Alpha");
        var b1 = named(w, "Beta");
        var b2 = named(w, "Beta");
        check(w.Find("Alpha") == a, "[objects] Find by name");
        check(w.Find("Beta") == b1, "[objects] same name : the first created one");
        var list = new List<Entity>();
        check(w.FindAll("Beta", list) == 2 && list[0] == b1 && list[1] == b2, "[objects] FindAll returns every entity with the name in order");
        var late = named(w, "Late");   // 索引を作った後に作った物 (Added で入る)
        check(w.Find("Late") == late, "[objects] entity created after the index was built is found");

        // 名前を変える
        w.SetName(a, "Alpha2");
        check(w.Find("Alpha").IsNull && w.Find("Alpha2") == a, "[objects] SetName updates the index");
        w.Get<EntityInfo>(b1).Name = "Gamma";   // ref で直接 (索引は古いまま)
        check(w.Find("Beta") == b2, "[objects] raw ref rename : the stale entry is skipped (does not return the renamed entity)");
        w.Get<EntityInfo>(late).Name = "Late2";
        w.Reindex(late);
        check(w.Find("Late2") == late && w.Find("Late").IsNull, "[objects] Reindex after a raw ref rename");
        var info = w.Get<EntityInfo>(b2);
        w.Set(b2, info with { Name = "Delta" });   // 置き換え (Replaced で直る)
        check(w.Find("Delta") == b2 && w.Find("Beta").IsNull, "[objects] World.Set<EntityInfo> replacement updates the index");

        // 消す / 番号の使い回し
        w.DestroyEntity(a);
        check(w.Find("Alpha2").IsNull, "[objects] destroyed entity is not found");
        var reused = named(w, "Reused");
        check(reused.Id == a.Id && w.Find("Reused") == reused && w.Find("Alpha2").IsNull, "[objects] reused id is indexed under its new name only");

        // 無効
        w.SetActive(reused, false);
        check(w.Find("Reused").IsNull && w.Find("Reused", includeInactive: true) == reused, "[objects] inactive entity is skipped unless includeInactive");
        w.SetActive(reused, true);

        // パス
        var root = named(w, "Root");
        var arm = named(w, "Arm", root);
        var hand = named(w, "Hand", arm);
        var otherHand = named(w, "Hand");   // 親の無い同じ名前
        check(w.Find("Root/Arm/Hand") == hand, "[objects] Find by path Root/Arm/Hand");
        check(w.Find("Arm/Hand") == hand, "[objects] Find by partial path Arm/Hand");
        check(w.Find("/Root/Arm/Hand") == hand, "[objects] Find by rooted path /Root/Arm/Hand");
        check(w.Find("/Arm/Hand").IsNull, "[objects] rooted path /Arm/Hand fails (Arm is not a root)");
        check(w.Find("/Hand") == otherHand, "[objects] rooted path /Hand finds the root one only");
        check(w.Find("Root/Nope").IsNull, "[objects] path with a missing part is Null");

        // ---------- タグ ----------
        w.SetTag(hand, "Enemy");
        w.SetTag(otherHand, "Enemy");
        w.SetTag(root, "Player");
        check(w.FindWithTag("Player") == root, "[objects] FindWithTag");
        list.Clear();
        check(w.FindAllWithTag("Enemy", list) == 2 && list.Contains(hand) && list.Contains(otherHand), "[objects] FindAllWithTag");
        check(w.CompareTag(hand, "Enemy") && !w.CompareTag(hand, "Player") && w.CompareTag(arm, EntityTags.Untagged), "[objects] CompareTag (Untagged = no tag)");
        w.SetTag(otherHand, EntityTags.Untagged);
        check(w.GetTag(otherHand) == null && w.FindAllWithTag("Enemy").Count == 1, "[objects] SetTag Untagged clears the tag");
        check(w.FindWithTag("Nobody").IsNull, "[objects] FindWithTag of an unused tag is Null");

        // ---------- Id / 型 ----------
        var id = w.Get<EntityInfo>(hand).Id;
        check(w.FindById(id) == hand && w.FindById(Guid.NewGuid()).IsNull, "[objects] FindById");
        check(SceneSerializer.FindEntity(w, id) == hand, "[objects] SceneSerializer.FindEntity uses the index");
        w.Add(arm, new Camera());
        check(w.FindObjectOfType<Camera>() == arm, "[objects] FindObjectOfType<Camera>");
        list.Clear();
        check(w.FindObjectsOfType<Transform>(list) == w.GetPool<Transform>().Count, "[objects] FindObjectsOfType<Transform> returns every active one");
        yield return 0;

        // ---------- GameObject ----------
        var go = new GameObject(w, hand);
        check(go && go.Name == "Hand" && go.Tag == "Enemy" && go.CompareTag("Enemy"), "[objects] GameObject name / tag");
        check(go.Parent.Entity == arm && go.Parent.Parent.Entity == root && !go.Parent.Parent.Parent, "[objects] GameObject.Parent chain");
        var rootGo = new GameObject(w, root);
        check(rootGo.FindChild("Arm/Hand") == go && !rootGo.FindChild("Hand"), "[objects] GameObject.FindChild (path from the children)");
        check(rootGo.GetChildren().Count == 1, "[objects] GameObject.GetChildren");
        go.Transform.Position = new Vector3(1, 2, 3);
        check(w.Get<Transform>(hand).Position == new Vector3(1, 2, 3), "[objects] GameObject.Transform is a reference");
        go.AddComponent(new ObjectRefProbe { Raw = root }).Target = rootGo;
        check(go.HasComponent<ObjectRefProbe>() && go.GetComponent<ObjectRefProbe>().Target == rootGo, "[objects] GameObject.AddComponent returns a reference");
        go.Name = "Hand2";
        check(w.Find("Hand2") == hand, "[objects] GameObject.Name setter updates the index");
        go.Tag = "Player";
        check(w.FindAllWithTag("Player").Count == 2, "[objects] GameObject.Tag setter updates the index");
        go.SetActive(false);
        check(!go.ActiveSelf && !go.ActiveInHierarchy && w.Find("Hand2").IsNull, "[objects] GameObject.SetActive(false)");
        go.SetActive(true);
        Entity asEntity = go;
        check(asEntity == hand && GameObject.None == default && !GameObject.None, "[objects] GameObject converts to Entity / None is false");

        // ---------- 保存 / 読み込み (タグと GameObject のフィールド) ----------
        var json = SceneSerializer.Save(w);
        var loaded = new World();
        loaded.Find("anything");   // 索引を先に作っておく (読んだ物は Added で入る)
        SceneSerializer.Load(loaded, json, null);
        var loadedHand = loaded.Find("Hand2");
        var loadedRoot = loaded.Find("Root");
        check(!loadedHand.IsNull && loaded.GetTag(loadedHand) == "Player" && loaded.FindAllWithTag("Player").Count == 2, "[objects] tag is saved and loaded (index built before loading)");
        check(loaded.GetTag(loaded.Find("Arm")) == null && !json.Contains("\"tag\": \"Untagged\""), "[objects] untagged entity writes no tag");
        var probe = loaded.Get<ObjectRefProbe>(loadedHand);
        check(probe.Target.Entity == loadedRoot && ReferenceEquals(probe.Target.World, loaded), "[objects] GameObject field is saved by Id and points to the loaded entity");
        check(!probe.Missing && probe.Raw == loadedRoot, "[objects] empty GameObject stays None / Entity field still works");
        check(loaded.FindById(id) == loadedHand, "[objects] FindById after loading (same Id)");

        // ---------- 今のエンジンの World (GameObject.Find の static / エディターの名前の変更) ----------
        var engineWorld = World;
        var probeEntity = engineWorld.CreateEntity("ObjTest_Static");
        engineWorld.Add(probeEntity, new Transform { Rotation = Quaternion.Identity, Scale = Vector3.One });
        check(GameObject.Find("ObjTest_Static").Entity == probeEntity, "[objects] static GameObject.Find searches the engine world");
        _app.RenameEntity(probeEntity, "ObjTest_Renamed");
        check(GameObject.Find("ObjTest_Renamed").Entity == probeEntity && !GameObject.Find("ObjTest_Static"), "[objects] editor rename (Hierarchy) updates the index");
        engineWorld.SetTag(probeEntity, "Respawn");
        check(GameObject.FindWithTag("Respawn").Entity == probeEntity && GameObject.FindGameObjectsWithTag("Respawn").Count == 1, "[objects] static FindWithTag / FindGameObjectsWithTag");
        engineWorld.DestroyEntity(probeEntity);
        check(!GameObject.Find("ObjTest_Renamed"), "[objects] destroyed engine entity is not found");
        yield return 0;
    }

    // v1.18 GO2 : Inspector の物の欄 (選ぶ窓の一覧 / 別のシーンの物を断る / Ping / 欄の描画 / Missing)
    private IEnumerable<int> objectFields()
    {
        var inspector = _app.Inspector;
        var database = _app.Database;
        var scenes = _app.Engine.Scenes;
        var folder = "Assets/_ObjectFieldTest";
        var otherPath = $"{folder}/OtherObjects.scene.json";

        // ---------- 物 (今のメインシーン) ----------
        var holder = World.CreateEntity("ObjField_Holder");
        World.Add(holder, new Transform { Rotation = Quaternion.Identity, Scale = Vector3.One });
        var parent = World.CreateEntity("ObjField_Parent");
        World.Add(parent, new Transform { Rotation = Quaternion.Identity, Scale = Vector3.One });
        var child = World.CreateEntity("ObjField_Child");
        World.Add(child, new Transform { Rotation = Quaternion.Identity, Scale = Vector3.One, Parent = parent });
        World.Add(child, new Camera());
        World.SetTag(child, "Enemy");
        World.Add(holder, new ObjectRefProbe { Target = new GameObject(World, child), Raw = parent });

        // ---------- 選ぶ窓の一覧 ----------
        inspector.BuildPickerItems("ObjField_");
        check(inspector.PickerItems.Count == 3 && inspector.PickerItems.Contains(child), "[objects-ui] picker search by name part");
        inspector.BuildPickerItems("objfield_child");
        check(inspector.PickerItems.Count == 1 && inspector.PickerItems[0] == child, "[objects-ui] picker search ignores case");
        inspector.BuildPickerItems("t:Camera");
        check(inspector.PickerItems.Contains(child) && inspector.PickerItems.All(e => World.Has<Camera>(e)), "[objects-ui] picker t:Camera lists only objects with a Camera");
        inspector.BuildPickerItems("tag:Enemy");
        check(inspector.PickerItems.Contains(child) && inspector.PickerItems.All(e => World.CompareTag(e, "Enemy")), "[objects-ui] picker tag:Enemy");
        inspector.BuildPickerItems("t:NoSuchComponent");
        check(inspector.PickerItems.Count == 0, "[objects-ui] picker t: of an unknown type lists nothing");

        // ---------- 値の型 / 入れられるか ----------
        check(inspector.MakeReference(typeof(GameObject), child) is GameObject go && go.Entity == child && ReferenceEquals(go.World, World), "[objects-ui] GameObject field gets a GameObject of this world");
        check(inspector.MakeReference(typeof(Entity), child) is Entity e && e == child, "[objects-ui] Entity field gets the Entity");
        check(inspector.MakeReference(typeof(GameObject), Entity.Null) is GameObject none && !none, "[objects-ui] clearing a GameObject field gives None");
        check(inspector.RefuseReference(holder, child) == null, "[objects-ui] same scene reference is accepted");

        // 別のシーン (足したシーン) の物は断る
        writeScene(database.FullPath(otherPath), w => { w.CreateEntity("ObjField_Other"); w.CreateEntity("ObjField_Other2"); });
        // メインシーンが無い時 (前の確認が外した後など) は、足したシーンがメインになる (SceneManager の決まり) ので、先にメイン役のシーンを足す
        Scene? filler = null;
        if (scenes.ActiveScene == null)
        {
            writeScene(database.FullPath($"{folder}/MainFiller.scene.json"), w => w.CreateEntity("ObjField_Filler"));
            database.Refresh();
            filler = scenes.LoadScene($"{folder}/MainFiller.scene.json", LoadSceneMode.Additive);
        }
        database.Refresh();
        var other = scenes.LoadScene(otherPath, LoadSceneMode.Additive);
        yield return 0;
        var otherThing = findByName("ObjField_Other");
        var otherThing2 = findByName("ObjField_Other2");
        var refusal = inspector.RefuseReference(holder, otherThing);
        check(refusal != null && refusal.Contains("OtherObjects"), $"[objects-ui] cross-scene reference is refused while editing ({refusal?.Split('\n')[0]})");
        check(inspector.RefuseReference(otherThing, otherThing2) == null, "[objects-ui] reference inside the same additive scene is accepted");
        check(inspector.RefuseReference(holder, Entity.Null) != null, "[objects-ui] dead / null object is refused");

        // ---------- Ping (親を開いて光らせる) ----------
        var before = _app.PingScrollCount;
        _app.Ping(child);
        for (int i = 0; i < 3; i++) yield return 0;
        check(_app.PingedEntity == child && _app.PingScrollCount == before + 1, "[objects-ui] Ping highlights the object and scrolls the Hierarchy once");

        // ---------- 欄を描く (選んだ物の Inspector)。消えた物は Missing ----------
        _app.Selected = holder;
        for (int i = 0; i < 3; i++) yield return 0;
        inspector.PickerOpenRequest = "Target";
        for (int i = 0; i < 3; i++) yield return 0;
        check(inspector.PickerOpen && inspector.PickerItems.Count >= 3, $"[objects-ui] picker opens from the field ({inspector.PickerItems.Count} items)");
        ImGuiNET.ImGui.CloseCurrentPopup();
        World.DestroyEntity(child);
        for (int i = 0; i < 3; i++) yield return 0;
        var probe = World.Get<ObjectRefProbe>(holder);
        check(!probe.Target && probe.Target.Entity == child, "[objects-ui] deleted target shows as Missing (the field keeps the old reference, the editor does not crash)");

        // 片付け
        scenes.UnloadScene(other);
        if (filler != null) scenes.UnloadScene(filler);
        World.DestroyEntity(holder);
        World.DestroyEntity(parent);
        _app.Selected = Entity.Null;
        yield return 0;
        Directory.Delete(database.FullPath(folder), true);
        if (File.Exists(database.FullPath(folder) + ".meta")) File.Delete(database.FullPath(folder) + ".meta");
        database.Refresh();
        yield return 0;
    }

    // 撮影用 : 物の欄を持つ物を選び、選ぶ窓を開いたまま待つ (--editor-test-section objects-view)
    private IEnumerable<int> objectFieldsView()
    {
        var parent = World.CreateEntity("Enemy Group");
        World.Add(parent, new Transform { Rotation = Quaternion.Identity, Scale = Vector3.One });
        var enemy = World.CreateEntity("Enemy A");
        World.Add(enemy, new Transform { Rotation = Quaternion.Identity, Scale = Vector3.One, Parent = parent });
        var holder = World.CreateEntity("Turret");
        World.Add(holder, new Transform { Rotation = Quaternion.Identity, Scale = Vector3.One });
        World.Add(holder, new ObjectRefProbe { Target = new GameObject(World, enemy), Raw = parent });
        var camera = World.CreateEntity("Turret Camera");
        World.Add(camera, new Camera());
        World.Add(holder, new AssetRefProbe { Cam = new ComponentRef<Camera>(World, camera), Next = new SceneReference("Assets/Scenes/scenes_demo.scene.json") });
        _app.Selected = holder;
        for (int i = 0; i < 30; i++) yield return 0;
        _app.Ping(enemy);
        for (int i = 0; i < 20; i++) yield return 0;
        Console.WriteLine("[Test] objects-view : ping");
        var start = DateTime.Now;
        while ((DateTime.Now - start).TotalSeconds < 1.2) yield return 0;
        _app.Inspector.PickerOpenRequest = "Target";
        for (int i = 0; i < 10; i++) yield return 0;
        Console.WriteLine("[Test] objects-view : picker");
        start = DateTime.Now;
        while ((DateTime.Now - start).TotalSeconds < 3) yield return 0;
        _app.Inspector.AssetPickerOpenRequest = "Next";   // v1.20 GO3 : アセットの選ぶ窓 (シーン)
        for (int i = 0; i < 10; i++) yield return 0;
        Console.WriteLine("[Test] objects-view : asset-picker");
        start = DateTime.Now;
        while ((DateTime.Now - start).TotalSeconds < 3) yield return 0;
    }

    // v1.20 GO3 : ComponentRef<T> / Prefab / SceneReference (保存と読み込み / 名前の変更 / Inspector の表と選ぶ窓)
    private IEnumerable<int> assetRefs()
    {
        var database = _app.Database;
        var assets = _app.Engine.Assets;
        var inspector = _app.Inspector;
        var folder = "Assets/_AssetRefTest";
        var prefabPath = $"{folder}/Bullet.prefab.json";
        var scenePath = $"{folder}/NextStage.scene.json";

        // ---------- ComponentRef<T> ----------
        var w = new World();
        var cam = w.CreateEntity("Cam");
        w.Add(cam, new Camera { FieldOfView = 60 });
        var plain = w.CreateEntity("Plain");
        var r = new ComponentRef<Camera>(w, cam);
        check(r && r.GameObject.Entity == cam && r.Value.FieldOfView == 60, "[objects-ref] ComponentRef<Camera> points to the camera");
        r.Value.FieldOfView = 75;
        check(w.Get<Camera>(cam).FieldOfView == 75, "[objects-ref] ComponentRef.Value is a reference");
        check(!new ComponentRef<Camera>(w, plain) && !default(ComponentRef<Camera>), "[objects-ref] ComponentRef to an object without T / default is not valid");
        check(new GameObject(w, cam).GetComponentRef<Camera>() == r && !new GameObject(w, plain).GetComponentRef<Camera>(), "[objects-ref] GameObject.GetComponentRef");

        // ---------- 確認用のアセット (プレハブ / シーン) ----------
        writeScene(database.FullPath(prefabPath), x => { var b = x.CreateEntity("Bullet"); x.Add(b, new Transform { Rotation = Quaternion.Identity, Scale = Vector3.One }); });
        writeScene(database.FullPath(scenePath), x => x.CreateEntity("NextStageThing"));
        database.Refresh();
        yield return 0;
        var prefab = assets.LoadPrefab(prefabPath);
        var next = new SceneReference(scenePath);
        check(next && next.AssetId != Guid.Empty && next.Name == "NextStage", "[objects-ref] SceneReference from a path gets the asset id");

        // ---------- 保存 / 読み込み ----------
        w.Add(plain, new AssetRefProbe { Cam = r, Bullet = prefab, Next = next });
        var json = SceneSerializer.Save(w);
        var loaded = new World();
        SceneSerializer.Load(loaded, json, assets);
        var loadedProbe = loaded.Get<AssetRefProbe>(loaded.Find("Plain"));
        check(loadedProbe.Cam && loadedProbe.Cam.Entity == loaded.Find("Cam") && ReferenceEquals(loadedProbe.Cam.World, loaded) && loadedProbe.Cam.Value.FieldOfView == 75,
              "[objects-ref] ComponentRef field is saved by Id and points to the loaded camera");
        check(ReferenceEquals(loadedProbe.Bullet, prefab), "[objects-ref] Prefab field is saved by asset and loads the same prefab");
        check(loadedProbe.Next == next && loadedProbe.Next.Path == scenePath, "[objects-ref] SceneReference field is saved and loaded");
        var empty = new World();
        empty.Add(empty.CreateEntity("E"), new AssetRefProbe());
        var emptyLoaded = new World();
        SceneSerializer.Load(emptyLoaded, SceneSerializer.Save(empty), assets);
        var emptyProbe = emptyLoaded.Get<AssetRefProbe>(emptyLoaded.Find("E"));
        check(!emptyProbe.Cam && emptyProbe.Bullet == null && !emptyProbe.Next, "[objects-ref] empty ComponentRef / Prefab / SceneReference stay empty");
        var spawned = prefab.Instantiate(loaded, Vector3.Zero);
        check(loaded.GetName(spawned) == "Bullet", "[objects-ref] the referenced prefab can be instantiated");

        // ---------- シーンの名前を変えても辿れる / LoadScene に渡せる ----------
        var renamed = database.Rename(scenePath, "NextStage2");
        check(next.Path == renamed && loadedProbe.Next.Path == renamed, $"[objects-ref] SceneReference follows a renamed scene ({renamed})");
        var scene = _app.Engine.Scenes.LoadScene(next, LoadSceneMode.Additive);
        yield return 0;
        check(scene.State == SceneStatus.Loaded && findByName("NextStageThing") is var thing && World.IsAlive(thing), "[objects-ref] Scenes.LoadScene(SceneReference)");
        _app.Engine.Scenes.UnloadScene(scene);
        yield return 0;

        // ---------- Inspector の表 / 選ぶ窓 ----------
        check(inspector.FindAssetField(typeof(Prefab))?.Kinds.SequenceEqual(new[] { AssetKind.Prefab }) == true &&
              inspector.FindAssetField(typeof(SceneReference))?.Kinds.SequenceEqual(new[] { AssetKind.Scene }) == true &&
              inspector.FindAssetField(typeof(Model)) != null && inspector.FindAssetField(typeof(Material)) != null, "[objects-ref] asset field table has Prefab / SceneReference / the old asset types");
        inspector.BuildAssetPickerItems(new[] { AssetKind.Prefab }, "bullet");
        check(inspector.AssetPickerItems.Any(e => e.Path == prefabPath) && inspector.AssetPickerItems.All(e => e.Kind == AssetKind.Prefab), "[objects-ref] asset picker lists prefabs (search ignores case)");
        inspector.BuildAssetPickerItems(new[] { AssetKind.Scene }, "_AssetRefTest");
        check(inspector.AssetPickerItems.Count == 1 && inspector.AssetPickerItems[0].Path == renamed, "[objects-ref] asset picker lists scenes by folder");
        var sceneField = inspector.FindAssetField(typeof(SceneReference))!;
        check(sceneField.Load(renamed) is SceneReference loadedRef && loadedRef == next && sceneField.PathOf(next) == renamed, "[objects-ref] SceneReference field value from a path");

        // ComponentRef の欄 : Camera を持つ物だけ
        var holder = World.CreateEntity("AssetRef_Holder");
        var camEntity = World.CreateEntity("AssetRef_Cam");
        World.Add(camEntity, new Camera());
        var noCam = World.CreateEntity("AssetRef_NoCam");
        check(inspector.RefuseReference(holder, noCam, typeof(Camera)) is { } why && why.Contains("Camera") && inspector.RefuseReference(holder, camEntity, typeof(Camera)) == null,
              "[objects-ref] ComponentRef<Camera> field refuses objects without a Camera");
        inspector.BuildPickerItems("AssetRef_", typeof(Camera));
        check(inspector.PickerItems.Count == 1 && inspector.PickerItems[0] == camEntity, "[objects-ref] ComponentRef picker lists only objects with the component");
        check(inspector.MakeReference(typeof(ComponentRef<Camera>), camEntity) is ComponentRef<Camera> made && made && made.Entity == camEntity, "[objects-ref] ComponentRef field value from an object");
        World.Add(holder, new AssetRefProbe { Cam = new ComponentRef<Camera>(World, camEntity), Bullet = prefab, Next = next });
        _app.Selected = holder;
        for (int i = 0; i < 3; i++) yield return 0;
        World.Remove<Camera>(camEntity);   // 相手が Camera を外した : 赤で出る (落ちない)
        for (int i = 0; i < 3; i++) yield return 0;
        check(!World.Get<AssetRefProbe>(holder).Cam, "[objects-ref] ComponentRef becomes invalid when the target loses the component (Inspector keeps drawing)");

        // 片付け
        _app.Selected = Entity.Null;
        foreach (var e in new[] { holder, camEntity, noCam }) World.DestroyEntity(e);
        yield return 0;
        Directory.Delete(database.FullPath(folder), true);
        if (File.Exists(database.FullPath(folder) + ".meta")) File.Delete(database.FullPath(folder) + ".meta");
        database.Refresh();
        yield return 0;
    }

    // v1.21 GO4 : 見本 (Assets/Scenes/objects_demo.scene.json + Scripts/ObjectsDemo.cs) を開いて再生する。
    //   ゲームの型の ComponentRef<Enemy> を使った後に、ゲームのコードを読み直しても古い Game.dll が残らないかも見る
    private IEnumerable<int> objectsDemo()
    {
        const string demoPath = "Assets/Scenes/objects_demo.scene.json";
        _app.OpenScene(_app.Database.FullPath(demoPath));
        yield return 0;
        var turret = findByName("Turret");
        var enemyB = findByName("Enemy B");
        {
            // ゲームの型はこの括弧の中だけで持つ (反復子のフィールドに残すと、古い Game.dll が解放されない)
            var turretType = gameType("Turret");
            var value = World.GetPool(turretType).GetBoxed(turret.Id);
            var target = turretType.GetField("Target")!.GetValue(value) as IEntityRef;
            var muzzle = (GameObject)turretType.GetField("Muzzle")!.GetValue(value)!;
            var bullet = turretType.GetField("Bullet")!.GetValue(value) as Prefab;
            var next = (SceneReference)turretType.GetField("Next")!.GetValue(value)!;
            check(target != null && target.Entity == enemyB && target.ComponentType == gameType("Enemy") && muzzle.Entity == findByName("Muzzle") &&
                  bullet?.Path == "Assets/Samples/Objects/Bullet.prefab.json" && next.Name == "scenes_demo",
                  "[objects-demo] the sample scene loads ComponentRef<Enemy> / GameObject / Prefab / SceneReference fields");
        }
        Time.CaptureDeltaTime = 1.0f / 60.0f;
        _app.TogglePlay();
        var maxBullets = 0;
        for (int i = 0; i < 240; i++)   // 4 秒 : 3 発で Enemy B が倒れ、次の敵を狙う
        {
            if (i % 10 == 0) maxBullets = Math.Max(maxBullets, countByName("Bullet"));
            yield return 0;
        }
        var enemyBAlive = countByName("Enemy B") > 0;
        var enemies = World.GetPool(gameType("Enemy")).Count;
        _app.TogglePlay();
        Time.CaptureDeltaTime = 0;
        yield return 0;
        check(!enemyBAlive && enemies is 1 or 2 && maxBullets > 0,
              $"[objects-demo] playing : the turret shoots prefab bullets, Enemy B (the Inspector target) is destroyed and it retargets ({3 - enemies} destroyed, up to {maxBullets} bullets in flight)");
        check(countByName("Enemy B") == 1 && countByName("Bullet") == 0, "[objects-demo] stopping play restores the scene");

        // 読み直し : 古い Game.dll が解放される (ComponentRef<Enemy> の型が残っていない)
        _app.ReloadScripts();
        var unloaded = false;
        var waitStart = Time.TotalTime;
        while (!(unloaded = _app.Scripts.PreviousUnloaded() == true) && Time.TotalTime - waitStart < 3.0) yield return 0;
        check(unloaded, $"[objects-demo] after using ComponentRef<Enemy>, reloading the scripts frees the previous Game.dll ({Time.TotalTime - waitStart:F2} s)");
        _app.OpenScene(_app.Database.FullPath("Assets/Scenes/showcase.scene.json"));
        yield return 0;
    }

    // マウスで落とす確認 (Input.Simulate で ImGui の本当のドラッグ & ドロップを通す)
    //   Hierarchy の "ObjDrag_Enemy" の行を、Inspector の欄 (GameObject の Missing / Entity の Raw) へドラッグする。Undo で戻るかも見る
    //   (外から PostMessage でマウスを送る方法は、エディターの窓の WinForms の MouseMove に届かなかったので使わない)
    private IEnumerable<int> objectFieldsDrag()
    {
        var enemy = World.CreateEntity("ObjDrag_Enemy");
        World.Add(enemy, new Transform { Rotation = Quaternion.Identity, Scale = Vector3.One });
        var holder = World.CreateEntity("ObjDrag_Turret");
        World.Add(holder, new Transform { Rotation = Quaternion.Identity, Scale = Vector3.One });
        World.Add(holder, new ObjectRefProbe());
        World.Add(holder, new AssetRefProbe());   // v1.20 GO3 : ComponentRef<Camera> の欄 (Cam)
        _app.Selected = holder;
        _app.TrackedRow = enemy;
        _app.Ping(enemy);   // 行が見える所までスクロールさせる
        for (int i = 0; i < 10; i++) yield return 0;

        IEnumerable<int> drag(string field)
        {
            var (rowMin, rowMax) = _app.TrackedRowRect;
            var from = new Vector2(rowMin.X + 30, (rowMin.Y + rowMax.Y) * 0.5f);
            var (fieldMin, fieldMax) = _app.Inspector.FieldRects.TryGetValue(field, out var r) ? r : default;
            var to = (fieldMin + fieldMax) * 0.5f;
            Input.Simulate(from, MouseButtons.None);
            yield return 0;
            Input.Simulate(from, MouseButtons.Left);
            yield return 0;
            for (int i = 1; i <= 10; i++)
            {
                Input.Simulate(Vector2.Lerp(from, to, i / 10.0f), MouseButtons.Left);
                yield return 0;
            }
            Input.Simulate(to, MouseButtons.Left);
            yield return 0;
            Input.Simulate(to, MouseButtons.None);
            yield return 0;
            yield return 0;
        }

        foreach (var step in drag("Missing")) yield return step;
        var dropped = World.Get<ObjectRefProbe>(holder).Missing;
        check(dropped.Entity == enemy && ReferenceEquals(dropped.World, World), "[objects-drag] dragging a Hierarchy row onto a GameObject field sets it");
        foreach (var step in drag("Raw")) yield return step;
        check(World.Get<ObjectRefProbe>(holder).Raw == enemy, "[objects-drag] dragging onto an Entity field sets it");
        check(_app.Selected == holder, "[objects-drag] starting a drag in Hierarchy keeps the previous selection (the Inspector stays on the field owner)");
        check(Hierarchy.GetChildren(World, enemy).Count == 0 && World.Get<Transform>(enemy).Parent.IsNull, "[objects-drag] the drop does not reparent anything");
        _app.Undo();
        yield return 0;
        holder = findByName("ObjDrag_Turret");
        check(World.IsAlive(holder) && World.Get<ObjectRefProbe>(holder).Raw.IsNull && World.Get<ObjectRefProbe>(holder).Missing, "[objects-drag] undo reverts the last drop only");
        _app.Undo();
        yield return 0;
        holder = findByName("ObjDrag_Turret");
        check(World.IsAlive(holder) && !World.Get<ObjectRefProbe>(holder).Missing, "[objects-drag] undo again clears the first drop");

        // v1.20 GO3 : ComponentRef<Camera> の欄。Camera の無い物は落としても入らない / 付けたら入る
        foreach (var step in drag("Cam")) yield return step;
        check(!World.Get<AssetRefProbe>(holder).Cam && World.Get<AssetRefProbe>(holder).Cam.Entity.IsNull, "[objects-drag] dropping an object without a Camera on a ComponentRef<Camera> field is refused");
        World.Add(enemy, new Camera());
        foreach (var step in drag("Cam")) yield return step;
        check(World.Get<AssetRefProbe>(holder).Cam.Entity == enemy && World.Get<AssetRefProbe>(holder).Cam, "[objects-drag] dropping an object with a Camera on a ComponentRef<Camera> field sets it");

        _app.TrackedRow = Entity.Null;
        World.DestroyEntity(findByName("ObjDrag_Enemy"));
        World.DestroyEntity(holder);
        _app.Selected = Entity.Null;
        yield return 0;
    }

    // v1.21 GO4 : 1 万個の物で、物の欄の選ぶ窓の重さを測る (--editor-test-section objects-picker-bench。数を出すだけ)
    //   閉じている時 / 開いている時 (見えている行だけ描く) のフレームの時間と、一覧を作る時間 (開いた時と検索を変えた時だけ)
    private IEnumerable<int> objectPickerBench()
    {
        const int count = 10000;
        var made = new List<Entity>(count);
        for (int i = 0; i < count; i++) made.Add(World.CreateEntity($"Bench {i}"));
        var holder = World.CreateEntity("Bench Holder");
        World.Add(holder, new ObjectRefProbe());
        _app.Selected = holder;
        for (int i = 0; i < 60; i++) yield return 0;

        IEnumerable<int> measure(List<double> result)
        {
            var last = Stopwatch.GetTimestamp();
            for (int i = 0; i < 300; i++)
            {
                yield return 0;
                var now = Stopwatch.GetTimestamp();
                result.Add(Stopwatch.GetElapsedTime(last, now).TotalMilliseconds);
                last = now;
            }
        }
        static double median(List<double> v) { v.Sort(); return v[v.Count / 2]; }

        var closed = new List<double>();
        foreach (var step in measure(closed)) yield return step;
        _app.Inspector.PickerOpenRequest = "Target";
        for (int i = 0; i < 5; i++) yield return 0;
        var open = new List<double>();
        foreach (var step in measure(open)) yield return step;
        var opened = _app.Inspector.PickerOpen;
        var items = _app.Inspector.PickerItems.Count;
        ImGuiNET.ImGui.CloseCurrentPopup();
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < 10; i++) _app.Inspector.BuildPickerItems(i % 2 == 0 ? "Bench 1" : "");
        var buildMs = sw.Elapsed.TotalMilliseconds / 10;

        Console.WriteLine($"[Bench] object picker : {World.EntityCount} entities, frame median closed {median(closed):F2} ms / open {median(open):F2} ms ({items} items), list build {buildMs:F2} ms (on open / search change only)");
        check(opened && items >= count, "[objects-picker-bench] the picker opened with every object");
        foreach (var e in made) World.DestroyEntity(e);
        World.DestroyEntity(holder);
        _app.Selected = Entity.Null;
        yield return 0;
    }

    // 1 万個の物 : 名前で探す (索引) と、今までの順に見るやり方を比べる
    private IEnumerable<int> gameObjectsBench()
    {
        const int count = 10000, lookups = 10000;
        var w = new World();
        for (int i = 0; i < count; i++) w.CreateEntity($"Obj{i}");
        var rng = new Random(1);
        var names = Enumerable.Range(0, lookups).Select(_ => $"Obj{rng.Next(count)}").ToArray();

        var sw = Stopwatch.StartNew();
        w.Find("Obj0");
        var buildMs = sw.Elapsed.TotalMilliseconds;

        var found = 0;
        sw.Restart();
        foreach (var name in names) if (!w.Find(name).IsNull) found++;
        var indexMs = sw.Elapsed.TotalMilliseconds;

        var linearFound = 0;
        var infos = w.GetPool<EntityInfo>();
        sw.Restart();
        foreach (var name in names)
        {
            var comps = infos.Components;
            for (int i = 0; i < comps.Length; i++) if (comps[i].Name == name) { linearFound++; break; }
        }
        var linearMs = sw.Elapsed.TotalMilliseconds;

        // 作る / 消すの手間 (索引を作った後 / 作っていない World)。1 回目は JIT が入るので 3 回の一番速い物
        var w2 = new World();
        for (int i = 0; i < count; i++) w2.CreateEntity($"Obj{i}");
        var bulletNames = Enumerable.Range(0, 1000).Select(i => $"Bullet{i}").ToArray();
        double churn(World world)
        {
            var best = double.MaxValue;
            var made = new List<Entity>(1000);
            for (int round = 0; round < 3; round++)
            {
                made.Clear();
                var watch = Stopwatch.StartNew();
                foreach (var name in bulletNames) made.Add(world.CreateEntity(name));
                foreach (var e in made) world.DestroyEntity(e);
                best = Math.Min(best, watch.Elapsed.TotalMilliseconds);
            }
            return best;
        }
        var churnMs = churn(w);
        var churnNoIndexMs = churn(w2);

        Console.WriteLine($"[Bench] objects : {count} entities, {lookups} Find : index {indexMs:F2} ms ({indexMs * 1000 / lookups:F3} us each, build {buildMs:F2} ms) / linear {linearMs:F1} ms ({linearMs * 1000 / lookups:F1} us each)");
        Console.WriteLine($"[Bench] objects : create+destroy 1000 named entities : with index {churnMs:F2} ms / without index {churnNoIndexMs:F2} ms");
        check(found == lookups && linearFound == lookups, "[objects-bench] every name is found");
        check(indexMs < linearMs, "[objects-bench] index is faster than the linear scan");
        yield return 0;
    }
}
