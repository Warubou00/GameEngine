using System.Diagnostics;
using System.Numerics;
using System.Text;

namespace GameEngine.Editor;

// 確認用 (v2.0 UD1) : UserData の型
public enum UDTestKind { Melee, Ranged, Boss }
public struct UDTestWave { public int Count; public float Interval; }
[Serializable]
public sealed class UDTestBox
{
    public string Label = "box";
    public List<float> Values = new();
    public UDTestBox? Inner;
}

[UserData(Menu = "Test/Params", FileName = "New Test Params")]
public sealed class UDTestParams : UserData
{
    public int Hp = 100;
    public float Speed = 3.5f;
    public string Title = "Goblin";
    public Vector3 Offset;
    public UDTestKind Kind;
    public List<int> Drops = new();
    public int[] Arr = Array.Empty<int>();
    public List<UDTestWave> Waves = new();
    public UDTestBox Box = new();
    public List<UDTestBox> Boxes = new();
    public Prefab? Bullet;
    public UDTestParams? Next;
    public UDTestOther? Other;
    public List<UDTestOther> Others = new();
    // シーンの物への参照 (保存しない)
    public GameObject Target;
    public Entity Raw;
    public ComponentRef<Camera> Cam;
    [NonSerialized] public int NotSaved = 5;
    public readonly int ReadOnlyValue = 1;

    public int LoadedCalls { get; private set; }
    public int ValidateCalls { get; private set; }
    protected override void OnLoaded() => LoadedCalls++;
    // UD2 : エディターで変えた後 (負の Hp は 0 に)
    protected override void OnValidate()
    {
        ValidateCalls++;
        if (Hp < 0) Hp = 0;
    }
}

public sealed class UDTestOther : UserData
{
    public int X;
}

// 確認用 (v2.0 UD1) : UserData をフィールドに持つコンポーネント
public struct UserDataProbe : IComponent
{
    public UDTestParams? Params;
    public UDTestOther? Other;
}

// ==========================================================================
// エディターの確認 (v2.0 UD1) : UserData (.userdata.json) の型 / 保存 / 読み込み / 読み直し / 参照
//   --editor-test --editor-test-section userdata で、これだけ回せる
//   --editor-test-section userdata-bench : 1000 個の保存と読み込みの時間 (数を出すだけ)
// ==========================================================================
internal sealed partial class EditorTest
{
    private IEnumerable<int> userDataAssets()
    {
        var database = _app.Database;
        var assets = _app.Engine.Assets;
        const string folder = "Assets/_UserDataTest";
        var testAssembly = typeof(UDTestParams).Assembly;
        if (Directory.Exists(database.FullPath(folder))) Directory.Delete(database.FullPath(folder), true);
        Directory.CreateDirectory(database.FullPath(folder));

        // ---------- 型 ----------
        check(UserDataTypes.Find(typeof(UDTestParams).FullName!) == typeof(UDTestParams) && UserDataTypes.Find("Nope.Missing") == null, "[userdata] UserDataTypes.Find by full name");
        check(UserDataTypes.Known().Contains(typeof(UDTestParams)) && UserDataTypes.Known().Contains(typeof(UDTestOther)) && !UserDataTypes.Known().Contains(typeof(UserData)),
              "[userdata] UserDataTypes.Known lists the concrete UserData classes");
        check(UserDataTypes.MenuOf(typeof(UDTestParams)) == "Test/Params" && UserDataTypes.FileNameOf(typeof(UDTestParams)) == "New Test Params" &&
              UserDataTypes.MenuOf(typeof(UDTestOther)) == "UDTestOther" && UserDataTypes.FileNameOf(typeof(UDTestOther)) == "New UDTestOther", "[userdata] [UserData] Menu / FileName (and defaults)");
        check(AssetDatabase.KindOf("Assets/a/Goblin.userdata.json") == AssetKind.UserData && AssetDatabase.KindOf("Assets/a/x.json") == AssetKind.Text, "[userdata] *.userdata.json is a UserData asset");
        check(!ComponentRegistry.KnownComponents().Any(c => c.Type == typeof(UDTestParams)), "[userdata] UserData classes are not components");

        // ---------- 確認用のプレハブ ----------
        var prefabPath = $"{folder}/Arrow.prefab.json";
        writeScene(database.FullPath(prefabPath), x => x.CreateEntity("Arrow"));
        database.Refresh();
        yield return 0;
        var prefab = assets.LoadPrefab(prefabPath);

        // ---------- コードで作って保存する ----------
        var w = new World();
        var cam = w.CreateEntity("Cam");
        w.Add(cam, new Camera());
        var other = new UDTestOther { X = 42 };
        var otherPath = assets.SaveUserData(other, $"{folder}/Other");
        check(otherPath == $"{folder}/Other.userdata.json" && File.Exists(database.FullPath(otherPath)) && other.Path == otherPath, "[userdata] SaveUserData with a path adds the extension and sets Path");
        var p = new UDTestParams
        {
            Hp = 250, Speed = 7.25f, Title = "ゴブリン \"王\"", Offset = new Vector3(1, 2, 3), Kind = UDTestKind.Boss,
            Drops = { 1, 2, 3 }, Arr = new[] { 9, 8 },
            Waves = { new UDTestWave { Count = 5, Interval = 0.5f }, new UDTestWave { Count = 10, Interval = 0.25f } },
            Box = new UDTestBox { Label = "outer", Values = { 1.5f }, Inner = new UDTestBox { Label = "inner" } },
            Boxes = { new UDTestBox { Label = "b0" }, new UDTestBox { Label = "b1", Values = { 2, 3 } } },
            Bullet = prefab, Other = other, Others = { other, other },
            Target = new GameObject(w, cam), Raw = cam, Cam = new ComponentRef<Camera>(w, cam), NotSaved = 99,
        };
        p.Next = p;   // 自分を指す (読む時に止まらないか)
        var path = assets.SaveUserData(p, $"{folder}/Goblin.userdata.json");
        database.Refresh();
        yield return 0;
        var json = File.ReadAllText(database.FullPath(path), Encoding.UTF8);
        check(json.Contains("\"format\": \"GameEngineUserData\"") && json.Contains($"\"type\": \"{typeof(UDTestParams).FullName}\"") && json.Contains("\"values\""), "[userdata] file has format / type / values");
        check(json.Contains("\"Target\": null") && json.Contains("\"Raw\": null") && json.Contains("\"Cam\": null"), "[userdata] scene references (GameObject / Entity / ComponentRef) are not saved");
        check(!json.Contains("NotSaved") && !json.Contains("ReadOnlyValue") && !json.Contains("LoadedCalls") && !json.Contains("UnknownValues"), "[userdata] [NonSerialized] / readonly / properties / base fields are not saved");
        check(json.Contains("ゴブリン"), "[userdata] Japanese text is written as is (readable file)");
        check(database.Find(path) is { Kind: AssetKind.UserData } entry && entry.Guid != Guid.Empty, "[userdata] saved file is a UserData asset with a guid");
        check(ReferenceEquals(assets.LoadUserData(path), p) && ReferenceEquals(assets.FindLoadedUserData(path), p), "[userdata] after saving, LoadUserData returns the same instance");

        // ---------- ファイルから読む (古いインスタンスを捨てて読み直す) ----------
        var forgotten = assets.ForgetUserData(testAssembly);
        check(forgotten == 2 && assets.FindLoadedUserData(path) == null, $"[userdata] ForgetUserData drops instances of the assembly ({forgotten})");
        var loaded = assets.LoadUserData<UDTestParams>(path);
        check(!ReferenceEquals(loaded, p) && loaded.Path == path && loaded.AssetId == database.Find(path)!.Guid && loaded.Name == "Goblin", "[userdata] LoadUserData<T> makes a new instance with Path / AssetId / Name");
        check(loaded.Hp == 250 && loaded.Speed == 7.25f && loaded.Title == "ゴブリン \"王\"" && loaded.Offset == new Vector3(1, 2, 3) && loaded.Kind == UDTestKind.Boss, "[userdata] int / float / string / Vector3 / enum round trip");
        check(loaded.Drops.SequenceEqual(new[] { 1, 2, 3 }) && loaded.Arr.SequenceEqual(new[] { 9, 8 }), "[userdata] List<int> / int[] round trip");
        check(loaded.Waves.Count == 2 && loaded.Waves[1].Count == 10 && loaded.Waves[1].Interval == 0.25f, "[userdata] List of structs round trip");
        check(loaded.Box.Label == "outer" && loaded.Box.Values.SequenceEqual(new[] { 1.5f }) && loaded.Box.Inner?.Label == "inner" && loaded.Box.Inner.Inner == null,
              "[userdata] nested [Serializable] class round trip");
        check(loaded.Boxes.Count == 2 && loaded.Boxes[1].Label == "b1" && loaded.Boxes[1].Values.SequenceEqual(new[] { 2f, 3f }), "[userdata] List of [Serializable] classes round trip");
        check(ReferenceEquals(loaded.Bullet, prefab), "[userdata] Prefab reference loads the same prefab");
        check(ReferenceEquals(loaded.Next, loaded), "[userdata] self reference resolves to the same instance (no endless loading)");
        check(loaded.Other is { X: 42 } o && o.Path == otherPath && loaded.Others.Count == 2 && ReferenceEquals(loaded.Others[0], o) && ReferenceEquals(loaded.Others[1], o),
              "[userdata] UserData references (field and list) load the shared instance");
        check(!loaded.Target && loaded.Raw.IsNull && !loaded.Cam && loaded.NotSaved == 5, "[userdata] scene references load empty / [NonSerialized] keeps the default");
        check(loaded.LoadedCalls == 1, "[userdata] OnLoaded is called once");
        var threw = false;
        try { assets.LoadUserData<UDTestOther>(path); } catch (InvalidCastException) { threw = true; }
        check(threw, "[userdata] LoadUserData<T> with a wrong type throws");
        check(ReferenceEquals(assets.LoadUserData(database.Find(path)!.Guid), loaded), "[userdata] LoadUserData(Guid)");

        // ---------- 読み直し (外でファイルを変えた) ----------
        var version = loaded.Version;
        var changed = 0;
        loaded.Changed += _ => changed++;
        var edited = json.Replace("\"Hp\": 250", "\"Hp\": 7").Replace("\"Speed\": 7.25,", "");
        check(edited.Contains("\"Hp\": 7") && !edited.Contains("7.25"), "[userdata] (test setup) edited JSON");
        File.WriteAllText(database.FullPath(path), edited, new UTF8Encoding(false));
        check(assets.ReloadUserData(path) && ReferenceEquals(assets.LoadUserData(path), loaded), "[userdata] ReloadUserData keeps the same instance");
        check(loaded.Hp == 7 && loaded.Speed == 3.5f && loaded.Title == "ゴブリン \"王\"", "[userdata] reload replaces values (removed value goes back to the default)");
        check(loaded.Version > version && changed >= 1 && loaded.LoadedCalls >= 2, "[userdata] reload raises Version / Changed / OnLoaded");
        File.WriteAllText(database.FullPath(path), "{ broken", new UTF8Encoding(false));
        check(!assets.ReloadUserData(path) && loaded.Hp == 7, "[userdata] broken file : reload fails and keeps the previous values");
        File.WriteAllText(database.FullPath(path), edited, new UTF8Encoding(false));
        assets.ReloadUserData(path);

        // ---------- 知らない値 / 読めない値 / 知らない型 ----------
        var oddPath = $"{folder}/Odd.userdata.json";
        File.WriteAllText(database.FullPath(oddPath),
            $"{{ \"format\": \"GameEngineUserData\", \"version\": 1, \"type\": \"{typeof(UDTestParams).FullName}\", \"values\": {{ \"Hp\": \"abc\", \"Speed\": 2, \"OldName\": {{ \"a\": [1, 2] }} }} }}",
            new UTF8Encoding(false));
        var missingPath = $"{folder}/Missing.userdata.json";
        File.WriteAllText(database.FullPath(missingPath), "{ \"format\": \"GameEngineUserData\", \"version\": 1, \"type\": \"Nope.Missing\", \"values\": {} }", new UTF8Encoding(false));
        database.Refresh();
        yield return 0;
        var odd = assets.LoadUserData<UDTestParams>(oddPath);
        check(odd.Hp == 100 && odd.Speed == 2, "[userdata] unreadable value is skipped (default kept), others are read");
        assets.SaveUserData(odd);
        var oddJson = File.ReadAllText(database.FullPath(oddPath), Encoding.UTF8);
        check(oddJson.Contains("\"OldName\"") && oddJson.Contains("\"Hp\": 100"), "[userdata] unknown value (renamed field) is kept when saving");
        threw = false;
        try { assets.LoadUserData(missingPath); } catch (InvalidDataException) { threw = true; }
        check(threw && assets.FindLoadedUserData(missingPath) == null, "[userdata] unknown type throws and is not cached");

        // ---------- コンポーネントのフィールド ----------
        var sw = new World();
        sw.Add(sw.CreateEntity("Holder"), new UserDataProbe { Params = loaded, Other = loaded.Other });
        sw.Add(sw.CreateEntity("Empty"), new UserDataProbe());
        var sceneJson = SceneSerializer.Save(sw);
        check(sceneJson.Contains(path) && sceneJson.Contains(database.Find(path)!.Guid.ToString()), "[userdata] component field is saved as {guid, path}");
        var sl = new World();
        SceneSerializer.Load(sl, sceneJson, assets);
        var probe = sl.Get<UserDataProbe>(sl.Find("Holder"));
        check(ReferenceEquals(probe.Params, loaded) && ReferenceEquals(probe.Other, loaded.Other), "[userdata] component field loads the shared instance");
        check(sl.Get<UserDataProbe>(sl.Find("Empty")) is { Params: null, Other: null }, "[userdata] empty component field stays null");
        var wrong = (UserDataProbe)SceneSerializer.LoadComponent(sl, typeof(UserDataProbe), $"{{ \"Other\": {{ \"path\": \"{path}\" }} }}", null, assets);
        check(wrong.Other == null, "[userdata] component field with a file of another UserData type stays null");

        // ---------- 名前を変える ----------
        var renamed = database.Rename(path, "Goblin2");
        check(renamed == $"{folder}/Goblin2.userdata.json" && loaded.Path == renamed && ReferenceEquals(assets.FindLoadedUserData(renamed), loaded) &&
              database.Find(renamed)?.Kind == AssetKind.UserData, $"[userdata] rename keeps the .userdata.json extension and the loaded instance follows ({renamed})");
        var afterRename = new World();
        SceneSerializer.Load(afterRename, sceneJson, assets);   // 前のパスの JSON でも guid で見つかる
        check(ReferenceEquals(afterRename.Get<UserDataProbe>(afterRename.Find("Holder")).Params, loaded), "[userdata] old scene JSON finds the renamed UserData by guid");

        // ---------- ゲームの DLL を読み直した後のように捨てる ----------
        var weak = new WeakReference(assets.FindLoadedUserData(otherPath));
        loaded = null!; odd = null!; o = null!; wrong = default; p = null!; other = null!; probe = default; sl = null!; sw = null!; afterRename = null!; w = null!;
        // World は型ごとに最後に使った World の入れ物を static に覚えている (World.PoolCache)。空の World で上書きしておく
        var dummy = new World();
        dummy.Add(dummy.CreateEntity(), new UserDataProbe());
        dummy = null!;
        assets.ForgetUserData(testAssembly);
        yield return 0;
        for (int i = 0; i < 3; i++) { GC.Collect(); GC.WaitForPendingFinalizers(); }
        check(!weak.IsAlive, "[userdata] forgotten UserData is not kept alive by the asset manager");

        // 片付け
        Directory.Delete(database.FullPath(folder), true);
        if (File.Exists(database.FullPath(folder) + ".meta")) File.Delete(database.FullPath(folder) + ".meta");
        database.Refresh();
        yield return 0;
    }

    // ---------------------------------------------------------------------
    // v2.0 UD2 : エディター (作る / Inspector で変える / 元に戻す / 再生中 / 選ぶ窓 / 読めない物)
    // ---------------------------------------------------------------------
    private IEnumerable<int> userDataEditor()
    {
        var database = _app.Database;
        var assets = _app.Engine.Assets;
        var project = _app.Project;
        var inspector = _app.Inspector;
        const string folder = "Assets/_UserDataEditorTest";
        if (Directory.Exists(database.FullPath(folder))) Directory.Delete(database.FullPath(folder), true);
        Directory.CreateDirectory(database.FullPath(folder));
        database.Refresh();
        yield return 0;

        // ---------- 作る (Create > UserData) ----------
        project.CreateFolderForTest(folder);
        var path = project.CreateUserData(typeof(UDTestParams));
        var path2 = project.CreateUserData(typeof(UDTestParams));
        var otherPath = project.CreateUserData(typeof(UDTestOther), "Other");
        check(path == $"{folder}/New Test Params.userdata.json" && path2 == $"{folder}/New Test Params 1.userdata.json" && otherPath == $"{folder}/Other.userdata.json",
              $"[userdata-editor] Create uses [UserData] FileName and a unique name ({path}, {path2})");
        check(path != null && database.Find(path) is { Kind: AssetKind.UserData } && assets.LoadUserData<UDTestParams>(path) is { Hp: 100 } created && created.AssetId == database.Find(path)!.Guid,
              "[userdata-editor] created file has the type defaults and an asset id");
        check(project.Selected?.Path == otherPath, "[userdata-editor] the created UserData is selected");

        // ---------- Inspector で出す ----------
        project.Select(database.Find(path!));
        for (int i = 0; i < 3; i++) yield return 0;
        check(inspector.EditingUserDataPath == path, "[userdata-editor] selecting a UserData shows it in the Inspector");
        var data = assets.LoadUserData<UDTestParams>(path!);
        var sceneDirty = _app.IsDirty;
        var undoCount = _app.UndoHistory.Count;

        // ---------- 変える → 触り終わったら書く / OnValidate / 元に戻す ----------
        check(inspector.EditUserDataForTest(d => ((UDTestParams)d).Hp = 250), "[userdata-editor] (test) edit through the Inspector");
        yield return 0;
        yield return 0;
        check(!inspector.UserDataDirty && File.ReadAllText(database.FullPath(path!)).Contains("\"Hp\": 250"), "[userdata-editor] edited value is saved to the file when no field is active");
        check(_app.UndoHistory.Count == undoCount + 1 && _app.IsDirty == sceneDirty, "[userdata-editor] an undo step is recorded but the scene is not marked unsaved");
        var validateCalls = data.ValidateCalls;
        inspector.EditUserDataForTest(d => ((UDTestParams)d).Hp = -5);
        yield return 0;
        yield return 0;
        check(data.Hp == 0 && data.ValidateCalls > validateCalls && File.ReadAllText(database.FullPath(path!)).Contains("\"Hp\": 0"), "[userdata-editor] OnValidate runs after an edit (Hp clamped to 0)");
        _app.Undo();
        yield return 0;
        check(data.Hp == 250 && File.ReadAllText(database.FullPath(path!)).Contains("\"Hp\": 250"), "[userdata-editor] Undo restores the value in the instance and the file");
        _app.Undo();
        yield return 0;
        check(data.Hp == 100 && File.ReadAllText(database.FullPath(path!)).Contains("\"Hp\": 100") && _app.IsDirty == sceneDirty, "[userdata-editor] Undo again goes back to the default (scene still not unsaved)");
        _app.Redo();
        yield return 0;
        check(data.Hp == 250 && File.ReadAllText(database.FullPath(path!)).Contains("\"Hp\": 250"), "[userdata-editor] Redo");
        for (int i = 0; i < 2; i++) yield return 0;
        check(inspector.EditingUserDataPath == path && !inspector.UserDataDirty, "[userdata-editor] Inspector follows the undo (no pending change)");

        // ---------- 別の UserData を指す欄 / 選ぶ窓は同じ型の物だけ ----------
        inspector.EditUserDataForTest(d => ((UDTestParams)d).Other = assets.LoadUserData<UDTestOther>(otherPath!));
        for (int i = 0; i < 2; i++) yield return 0;
        check(File.ReadAllText(database.FullPath(path!)).Contains(otherPath!), "[userdata-editor] UserData field saved as {guid, path}");
        inspector.BuildAssetPickerItems(new[] { AssetKind.UserData }, "", typeof(UDTestOther));
        check(inspector.AssetPickerItems.Count == 1 && inspector.AssetPickerItems[0].Path == otherPath, "[userdata-editor] picker for a UDTestOther field lists only UDTestOther files");
        inspector.BuildAssetPickerItems(new[] { AssetKind.UserData }, "_UserDataEditorTest", typeof(UDTestParams));
        check(inspector.AssetPickerItems.Count == 2 && inspector.AssetPickerItems.All(e => e.Path.Contains("New Test Params")), "[userdata-editor] picker for a UDTestParams field lists only UDTestParams files");
        inspector.BuildAssetPickerItems(new[] { AssetKind.UserData }, "_UserDataEditorTest", typeof(UserData));
        check(inspector.AssetPickerItems.Count == 3, "[userdata-editor] picker for a UserData field lists every UserData file");

        // ---------- コンポーネントの欄 (描いても落ちない / 保存とシーンの読み直し) ----------
        var holder = World.CreateEntity("UserData_Holder");
        World.Add(holder, new UserDataProbe { Params = data, Other = data.Other });
        _app.Selected = holder;
        for (int i = 0; i < 3; i++) yield return 0;
        check(inspector.EditingUserDataPath == path, "[userdata-editor] selecting an entity keeps the UserData state (nothing pending)");
        check(ReferenceEquals(World.Get<UserDataProbe>(holder).Params, data), "[userdata-editor] component field with a UserData is drawn");

        // ---------- 再生中 : ゲームのコードの変更 / Inspector の変更は Stop で戻る ----------
        _app.TogglePlay();
        for (int i = 0; i < 3; i++) yield return 0;
        check(_app.IsPlaying, "[userdata-editor] (test) playing");
        data.Hp = 999;   // ゲームのコードが変えた
        data.Title = "changed while playing";
        project.Select(database.Find(path!));
        for (int i = 0; i < 2; i++) yield return 0;
        inspector.EditUserDataForTest(d => ((UDTestParams)d).Speed = 42);
        for (int i = 0; i < 3; i++) yield return 0;
        var fileWhilePlaying = File.ReadAllText(database.FullPath(path!));
        // 以前は "42" が無いかで見ていたが、guid の中の "42" に当たることがあった。値の所で見る
        check(!fileWhilePlaying.Contains("\"Hp\": 999") && !fileWhilePlaying.Contains("\"Speed\": 42") && fileWhilePlaying.Contains("\"Hp\": 250"), "[userdata-editor] changes while playing are not written to the file");
        _app.TogglePlay();
        for (int i = 0; i < 3; i++) yield return 0;
        check(!_app.IsPlaying && data.Hp == 250 && data.Speed == 3.5f && data.Title == "Goblin", "[userdata-editor] Stop reverts the UserData to the file values");
        var holderAfter = findByName("UserData_Holder");
        check(World.IsAlive(holderAfter) && ReferenceEquals(World.Get<UserDataProbe>(holderAfter).Params, data), "[userdata-editor] after Stop the restored component points to the same UserData");
        check(!File.ReadAllText(database.FullPath(path!)).Contains("\"Hp\": 999"), "[userdata-editor] file unchanged after Stop");

        // ---------- 読めない UserData (知らない型) : 見るだけ ----------
        var missingPath = $"{folder}/Missing.userdata.json";
        const string missingJson = "{ \"format\": \"GameEngineUserData\", \"version\": 1, \"type\": \"Nope.Missing\", \"values\": { \"A\": 1 } }";
        File.WriteAllText(database.FullPath(missingPath), missingJson, new UTF8Encoding(false));
        database.Refresh();
        yield return 0;
        project.Select(database.Find(missingPath));
        for (int i = 0; i < 3; i++) yield return 0;
        check(inspector.EditingUserDataPath == null && File.ReadAllText(database.FullPath(missingPath)) == missingJson, "[userdata-editor] unknown type is shown read-only and the file is not changed");

        // 片付け
        _app.Selected = Entity.Null;
        project.Select(null);
        World.DestroyEntity(holderAfter);
        yield return 0;
        assets.ForgetUserData(typeof(UDTestParams).Assembly);
        Directory.Delete(database.FullPath(folder), true);
        if (File.Exists(database.FullPath(folder) + ".meta")) File.Delete(database.FullPath(folder) + ".meta");
        database.Refresh();
        yield return 0;
    }

    // ---------------------------------------------------------------------
    // v2.0 UD3 : 書き出したゲームの UserData の bin (.userdata.bin)
    // ---------------------------------------------------------------------
    private IEnumerable<int> userDataBinary()
    {
        var database = _app.Database;
        var assets = _app.Engine.Assets;
        const string folder = "Assets/_UserDataBinaryTest";
        if (Directory.Exists(database.FullPath(folder))) Directory.Delete(database.FullPath(folder), true);
        Directory.CreateDirectory(database.FullPath(folder));

        // ---------- JSON → bin → JSON ----------
        var p = new UDTestParams
        {
            Hp = -250, Speed = 0.1f, Title = "ゴブリン \"王\" \\ /", Offset = new Vector3(1e-7f, -3.5f, 123456.79f), Kind = UDTestKind.Ranged,
            Drops = { 0, 1, -1, int.MaxValue, int.MinValue }, Arr = new[] { 9, 8 },
            Waves = Enumerable.Range(0, 50).Select(i => new UDTestWave { Count = i, Interval = i * 0.25f }).ToList(),
            Box = new UDTestBox { Label = "outer", Values = { float.MaxValue, float.Epsilon, -0.0f }, Inner = new UDTestBox() },
        };
        var json = SceneSerializer.SaveUserData(p);
        // 型に無い値 (double / long に入らない整数 / 空の配列とオブジェクト / NaN の文字) も JSON の通りに戻るか
        json = json.Replace("\"values\": {", "\"values\": { \"ExtraDouble\": 0.1234567890123, \"ExtraBig\": 18446744073709551615, \"ExtraLong\": -9223372036854775808, \"ExtraEmpty\": [], \"ExtraObj\": {}, \"ExtraNaN\": \"NaN\", \"ExtraNull\": null, \"ExtraExp\": 1.5e300,");
        // 同じ値か (数は書き方 (1.5e300 / 1.5E+300) でなく値で比べる)
        static bool same(System.Text.Json.JsonElement a, System.Text.Json.JsonElement b)
        {
            if (a.ValueKind != b.ValueKind) return false;
            switch (a.ValueKind)
            {
            case System.Text.Json.JsonValueKind.Object:
                var pa = a.EnumerateObject().ToList();
                var pb = b.EnumerateObject().ToList();
                return pa.Count == pb.Count && pa.Zip(pb).All(x => x.First.Name == x.Second.Name && same(x.First.Value, x.Second.Value));
            case System.Text.Json.JsonValueKind.Array:
                return a.GetArrayLength() == b.GetArrayLength() && a.EnumerateArray().Zip(b.EnumerateArray()).All(x => same(x.First, x.Second));
            case System.Text.Json.JsonValueKind.Number:
                if (a.GetRawText() == b.GetRawText()) return true;
                if (a.TryGetInt64(out var ia) && b.TryGetInt64(out var ib)) return ia == ib;
                return a.GetDouble() == b.GetDouble();
            case System.Text.Json.JsonValueKind.String:
                return a.GetString() == b.GetString();
            default:
                return true;
            }
        }
        bool sameJson(string x, string y)
        {
            using var dx = System.Text.Json.JsonDocument.Parse(x);
            using var dy = System.Text.Json.JsonDocument.Parse(y);
            return same(dx.RootElement, dy.RootElement);
        }
        var bin = UserDataBinary.Encode(json);
        var back = UserDataBinary.Decode(bin);
        check(UserDataBinary.IsBinary(bin) && sameJson(back, json), "[userdata-bin] JSON -> binary -> JSON gives the same values (ints, floats, doubles, big numbers, text, nested, empty)");
        var text = Encoding.UTF8.GetString(bin);
        check(!text.Contains("GameEngineUserData") && !text.Contains("Waves") && !text.Contains("Interval") && !text.Contains("ゴブリン") && !text.Contains("UDTestParams"),
              "[userdata-bin] the binary file does not show the type, field names or text when opened directly");
        check(bin.Length < Encoding.UTF8.GetByteCount(json) / 3, $"[userdata-bin] the binary file is compressed ({Encoding.UTF8.GetByteCount(json)} -> {bin.Length} bytes)");
        var bin2 = UserDataBinary.Encode(json);
        check(!bin.AsSpan().SequenceEqual(bin2) && sameJson(UserDataBinary.Decode(bin2), json), "[userdata-bin] each build uses a new salt (different bytes, same values)");

        // ---------- 書き換えた / 壊れた bin は読まない ----------
        bool rejects(Func<byte[], byte[]> change)
        {
            try { UserDataBinary.Decode(change((byte[])bin.Clone())); return false; }
            catch (InvalidDataException) { return true; }
        }
        check(rejects(b => { b[^1] ^= 1; return b; }) && rejects(b => { b[80] ^= 0x10; return b; }), "[userdata-bin] a changed byte in the data is detected");
        check(rejects(b => { b[13] ^= 1; return b; }) && rejects(b => { b[40] ^= 1; return b; }) && rejects(b => { b[28] ^= 1; return b; }), "[userdata-bin] a changed salt / check / size is detected");
        check(rejects(b => b[..^5]) && rejects(b => b[..30]) && rejects(b => b.Concat(new byte[] { 0 }).ToArray()), "[userdata-bin] a cut / extended file is detected");

        // ---------- 書き出したゲームと同じ : .userdata.bin だけがある ----------
        var jsonPath = $"{folder}/Boss.userdata.json";
        var binPath = $"{folder}/Boss.userdata.bin";
        File.WriteAllBytes(database.FullPath(binPath), bin);
        var otherBin = $"{folder}/Ally.userdata.bin";
        File.WriteAllBytes(database.FullPath(otherBin), UserDataBinary.Encode(SceneSerializer.SaveUserData(new UDTestOther { X = 5 })));
        database.Refresh();
        yield return 0;
        check(database.Find(binPath)?.Kind == AssetKind.UserData && UserDataBinary.BinaryPathOf(jsonPath) == binPath && UserDataBinary.JsonPathOf(binPath) == jsonPath,
              "[userdata-bin] *.userdata.bin is a UserData asset / path mapping");
        var loaded = assets.LoadUserData<UDTestParams>(jsonPath);
        check(loaded.Path == jsonPath && loaded.Hp == -250 && loaded.Speed == 0.1f && loaded.Title == p.Title && loaded.Offset == p.Offset && loaded.Waves.Count == 50 && loaded.Waves[49].Interval == 12.25f &&
              loaded.Box.Values.SequenceEqual(p.Box.Values) && loaded.Box.Inner != null, "[userdata-bin] LoadUserData(.userdata.json) reads the .userdata.bin next to it with the same values");
        check(ReferenceEquals(assets.LoadUserData(binPath), loaded) && loaded.AssetId == database.Find(binPath)!.Guid && ReferenceEquals(assets.LoadUserData(loaded.AssetId), loaded),
              "[userdata-bin] the .bin path / asset id give the same instance");
        check(loaded.UnknownValueNames.Contains("ExtraBig"), "[userdata-bin] values not in the type are kept from the binary file too");
        var tampered = (byte[])bin.Clone();
        tampered[^3] ^= 0x40;
        File.WriteAllBytes(database.FullPath(otherBin), tampered);
        var threw = false;
        try { assets.LoadUserData($"{folder}/Ally.userdata.json"); } catch (InvalidDataException) { threw = true; }
        check(threw && assets.FindLoadedUserData($"{folder}/Ally.userdata.json") == null, "[userdata-bin] a modified .userdata.bin is refused (not loaded)");

        // 速さと大きさ (数を出すだけ)
        var big = SceneSerializer.SaveUserData(new UDTestParams { Waves = Enumerable.Range(0, 2000).Select(i => new UDTestWave { Count = i, Interval = i * 0.1f }).ToList() });
        var sw = Stopwatch.StartNew();
        var bigBin = UserDataBinary.Encode(big);
        var encodeMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        for (int i = 0; i < 10; i++) UserDataBinary.Decode(bigBin);
        var decodeMs = sw.Elapsed.TotalMilliseconds / 10;
        Console.WriteLine($"[Bench] UserData binary : 2000 waves {Encoding.UTF8.GetByteCount(big)} -> {bigBin.Length} bytes ({100.0 * bigBin.Length / Encoding.UTF8.GetByteCount(big):F1} %), " +
                          $"encode {encodeMs:F2} ms, decode {decodeMs:F2} ms / small {Encoding.UTF8.GetByteCount(json)} -> {bin.Length} bytes");

        assets.ForgetUserData(typeof(UDTestParams).Assembly);
        loaded = null!;
        Directory.Delete(database.FullPath(folder), true);
        if (File.Exists(database.FullPath(folder) + ".meta")) File.Delete(database.FullPath(folder) + ".meta");
        database.Refresh();
        yield return 0;
    }

    // v2.0 UD3 : 見本 (Assets/Scenes/userdata_demo.scene.json + Scripts/UserDataDemo.cs)。ゲームの型の UserData を使った後に、
    //   ゲームのコードを読み直しても古い Game.dll が残らないかも見る
    private IEnumerable<int> userDataDemo()
    {
        const string demoPath = "Assets/Scenes/userdata_demo.scene.json";
        _app.OpenScene(_app.Database.FullPath(demoPath));
        yield return 0;
        var spawner = findByName("Spawner");
        {
            // ゲームの型はこの括弧の中だけで持つ (反復子のフィールドに残すと、古い Game.dll が解放されない)
            var spawnerType = gameType("WaveSpawner");
            var table = spawnerType.GetField("Table")!.GetValue(World.GetPool(spawnerType).GetBoxed(spawner.Id)) as UserData;
            var waves = table?.GetType().GetField("Waves")!.GetValue(table) as System.Collections.IList;
            var first = waves?[0];
            var enemy = first?.GetType().GetField("Enemy")!.GetValue(first) as UserData;
            var body = enemy?.GetType().GetField("Body")!.GetValue(enemy) as Prefab;
            check(table?.Path == "Assets/Samples/UserData/Stage1.userdata.json" && table.GetType().Name == "WaveTable" && waves?.Count == 2 &&
                  enemy?.Path == "Assets/Samples/UserData/Slime.userdata.json" && body?.Path == "Assets/Samples/UserData/Slime.prefab.json",
                  "[userdata-demo] the sample scene loads WaveTable -> Wave ([Serializable]) -> EnemyType -> Prefab");
            check(UserDataTypes.Known().Any(t => t.Name == "EnemyType") && UserDataTypes.MenuOf(gameType2("EnemyType")) == "Samples/Enemy Type", "[userdata-demo] game UserData types are listed for Create > UserData");
        }
        Time.CaptureDeltaTime = 1.0f / 60.0f;
        _app.TogglePlay();
        var maxWalkers = 0;
        for (int i = 0; i < 180; i++)   // 3 秒
        {
            if (i % 10 == 0) maxWalkers = Math.Max(maxWalkers, World.GetPool(gameType("Walker")).Count);
            yield return 0;
        }
        var slimes = countByName("Slime");
        _app.TogglePlay();
        Time.CaptureDeltaTime = 0;
        yield return 0;
        check(maxWalkers >= 2, $"[userdata-demo] playing : enemies are spawned from the WaveTable / EnemyType (up to {maxWalkers} at once, {slimes} slimes at the end)");
        check(World.GetPool(gameType("Walker")).Count == 0, "[userdata-demo] stopping play restores the scene");

        // 読み直し : 古い Game.dll が解放される (UserData の型が残っていない)
        _app.ReloadScripts();
        var unloaded = false;
        var waitStart = Time.TotalTime;
        while (!(unloaded = _app.Scripts.PreviousUnloaded() == true) && Time.TotalTime - waitStart < 3.0) yield return 0;
        check(unloaded, $"[userdata-demo] after using game UserData types, reloading the scripts frees the previous Game.dll ({Time.TotalTime - waitStart:F2} s)");
        {
            var spawnerType = gameType("WaveSpawner");
            var table = spawnerType.GetField("Table")!.GetValue(World.GetPool(spawnerType).GetBoxed(findByName("Spawner").Id)) as UserData;
            check(table != null && table.GetType().Assembly == spawnerType.Assembly, "[userdata-demo] after reloading, the WaveTable is recreated with the new game types");
        }
        _app.OpenScene(_app.Database.FullPath("Assets/Scenes/showcase.scene.json"));
        yield return 0;
    }

    // 見本を書き出したゲームで動かした時の Player.log の行 (Stage1 / Slime / Golem の値)
    private const string UserDataDemoReady = "[UserDataDemo] ready : table=Stage1 waves=2 スライムx3(hp 2, speed 2.5) ゴーレムx1(hp 10, speed 1)";

    // v2.0 UD3 : 見本だけを書き出す (Data.pak にまとめない形)。.userdata.bin を書き換えると、書き出したゲームは読まない
    //   (--editor-test-section userdata-build。全部の確認の Build Game は pak にまとめた形で同じ見本を動かす)
    private IEnumerable<int> userDataBuild()
    {
        var builder = _app.BuildWindow.Builder;
        var buildFolder = Path.Combine(Path.GetTempPath(), "ge_editor_test_build_userdata");
        if (Directory.Exists(buildFolder)) Directory.Delete(buildFolder, true);
        var settings = new BuildSettings
        {
            ProductName = "UserData Test", Width = 640, Height = 360, VSync = false, OutputFolder = buildFolder,
            IncludeDotnet = false, PackAssets = false, Scenes = { "Assets/Scenes/userdata_demo.scene.json" },
        };
        var dependencies = builder.CollectAssets(settings);
        check(dependencies.Assets.Contains("Assets/Samples/UserData/Stage1.userdata.json") && dependencies.Assets.Contains("Assets/Samples/UserData/Golem.userdata.json") &&
              dependencies.Assets.Contains("Assets/Samples/UserData/Golem.prefab.json") && dependencies.Assets.Contains("Assets/Materials/MC_Red.material.json"),
              "[userdata-build] dependencies follow scene -> WaveTable -> EnemyType -> Prefab -> Material");
        var task = Task.Run(() => builder.Build(settings, dependencies));
        while (!task.IsCompleted) yield return 0;
        var build = task.Result;
        check(build.Ok, $"[userdata-build] build ({(build.Ok ? $"{build.TotalMs / 1000:F1} s" : build.Error)})");
        if (!build.Ok) yield break;
        var folder = Path.Combine(buildFolder, "Data", "Assets", "Samples", "UserData");
        var files = Directory.EnumerateFiles(folder).Select(Path.GetFileName).ToList();
        check(files.Contains("Stage1.userdata.bin") && files.Contains("Stage1.userdata.bin.meta") && !files.Any(f => f!.EndsWith(".userdata.json")) &&
              UserDataBinary.IsBinary(File.ReadAllBytes(Path.Combine(folder, "Stage1.userdata.bin"))),
              $"[userdata-build] UserData is written as .userdata.bin with its .meta (no .userdata.json) : {string.Join(", ", files.Where(f => f!.Contains("userdata")))}");
        check(build.UserDataCount == 3 && build.UserDataBinaryBytes < build.UserDataJsonBytes, $"[userdata-build] UserData is smaller ({build.UserDataJsonBytes} -> {build.UserDataBinaryBytes} bytes)");

        string run()
        {
            var info = new System.Diagnostics.ProcessStartInfo(build.ExePath) { UseShellExecute = false };
            foreach (var argument in new[] { "--capture-dt", (1.0f / 60).ToString("R", System.Globalization.CultureInfo.InvariantCulture), "--frames", "30" }) info.ArgumentList.Add(argument);
            using var player = System.Diagnostics.Process.Start(info)!;
            if (!player.WaitForExit(60000)) player.Kill();
            var log = Path.Combine(buildFolder, "Player.log");
            return File.Exists(log) ? File.ReadAllText(log) : "";
        }
        var playerTask = Task.Run(run);
        while (!playerTask.IsCompleted) yield return 0;
        var playerLog = playerTask.Result;
        check(playerLog.Contains(UserDataDemoReady), "[userdata-build] the built game reads the .userdata.bin files");
        if (!playerLog.Contains(UserDataDemoReady)) Console.WriteLine(playerLog);

        // 書き換える (1 byte) : 読まない
        var stage = Path.Combine(folder, "Stage1.userdata.bin");
        var bytes = File.ReadAllBytes(stage);
        bytes[^2] ^= 0x01;
        File.WriteAllBytes(stage, bytes);
        playerTask = Task.Run(run);
        while (!playerTask.IsCompleted) yield return 0;
        playerLog = playerTask.Result;
        check(!playerLog.Contains("[UserDataDemo] ready") && playerLog.Contains("[UserDataDemo] no WaveTable") && playerLog.Contains("modified"),
              "[userdata-build] the built game refuses a modified .userdata.bin");
        if (!playerLog.Contains("modified")) Console.WriteLine(playerLog);
        Directory.Delete(buildFolder, true);
    }

    // 撮影用 : 見本を再生して、敵が出ている所で待つ (--editor-test-section userdata-demo-view)
    private IEnumerable<int> userDataDemoView()
    {
        _app.OpenScene(_app.Database.FullPath("Assets/Scenes/userdata_demo.scene.json"));
        _app.ProfilerWindows.ShowOverview = false;   // 画面の真ん中を隠すので閉じる
        yield return 0;
        Time.CaptureDeltaTime = 1.0f / 60.0f;
        _app.TogglePlay();
        for (int i = 0; i < 150; i++) yield return 0;
        Time.CaptureDeltaTime = 0;
        _app.TogglePause();   // 止めて撮る (敵が出ている所)
        Console.WriteLine("[Test] userdata-demo-view : playing");
        var start = DateTime.Now;
        while ((DateTime.Now - start).TotalSeconds < 3) yield return 0;
        _app.TogglePlay();
        yield return 0;
    }

    private static Type gameType2(string name) => UserDataTypes.Find($"GameEngine_console.{name}") ?? throw new InvalidOperationException($"No game UserData type {name}");

    // 撮影用 : UserData を Inspector に出したまま待つ / 別の UserData の欄の選ぶ窓を開く (--editor-test-section userdata-view)
    private IEnumerable<int> userDataView()
    {
        var database = _app.Database;
        var assets = _app.Engine.Assets;
        const string folder = "Assets/_UserDataView";
        if (Directory.Exists(database.FullPath(folder))) Directory.Delete(database.FullPath(folder), true);
        Directory.CreateDirectory(database.FullPath(folder));
        var other = new UDTestOther { X = 7 };
        assets.SaveUserData(other, $"{folder}/Archer.userdata.json");
        assets.SaveUserData(new UDTestOther { X = 9 }, $"{folder}/Knight.userdata.json");
        var p = new UDTestParams
        {
            Hp = 250, Speed = 7.25f, Title = "ゴブリン", Kind = UDTestKind.Boss, Offset = new Vector3(0, 1, 0),
            Drops = { 1, 2, 3 }, Waves = { new UDTestWave { Count = 5, Interval = 0.5f } },
            Boxes = { new UDTestBox { Label = "b0" } }, Other = other,
        };
        var path = assets.SaveUserData(p, $"{folder}/Goblin.userdata.json");
        database.Refresh();
        yield return 0;
        _app.Project.CreateFolderForTest(folder);
        _app.Project.Select(database.Find(path));
        for (int i = 0; i < 30; i++) yield return 0;
        Console.WriteLine("[Test] userdata-view : inspector");
        var start = DateTime.Now;
        while ((DateTime.Now - start).TotalSeconds < 3) yield return 0;
        _app.Inspector.AssetPickerOpenRequest = "Other";
        for (int i = 0; i < 10; i++) yield return 0;
        Console.WriteLine("[Test] userdata-view : picker");
        start = DateTime.Now;
        while ((DateTime.Now - start).TotalSeconds < 3) yield return 0;
        _app.Project.Select(null);
        yield return 0;
        assets.ForgetUserData(typeof(UDTestParams).Assembly);
        Directory.Delete(database.FullPath(folder), true);
        if (File.Exists(database.FullPath(folder) + ".meta")) File.Delete(database.FullPath(folder) + ".meta");
        database.Refresh();
        yield return 0;
    }

    // 1000 個の UserData の保存 / 読み込みの時間 (数を出すだけ)
    private IEnumerable<int> userDataBench()
    {
        var database = _app.Database;
        var assets = _app.Engine.Assets;
        const string folder = "Assets/_UserDataBench";
        const int count = 1000;
        Directory.CreateDirectory(database.FullPath(folder));
        var sw = Stopwatch.StartNew();
        for (int i = 0; i < count; i++)
            assets.SaveUserData(new UDTestParams { Hp = i, Drops = { i, i + 1 }, Waves = { new UDTestWave { Count = i } }, Boxes = { new UDTestBox() } }, $"{folder}/P{i}");
        var saveMs = sw.Elapsed.TotalMilliseconds;
        database.Refresh();
        yield return 0;
        assets.ForgetUserData(typeof(UDTestParams).Assembly);
        sw.Restart();
        var sum = 0;
        for (int i = 0; i < count; i++) sum += assets.LoadUserData<UDTestParams>($"{folder}/P{i}.userdata.json").Hp;
        var loadMs = sw.Elapsed.TotalMilliseconds;
        sw.Restart();
        for (int i = 0; i < count; i++) sum += assets.LoadUserData<UDTestParams>($"{folder}/P{i}.userdata.json").Hp;
        var cachedMs = sw.Elapsed.TotalMilliseconds;
        Console.WriteLine($"[Bench] UserData x{count} : save {saveMs:F1} ms, load {loadMs:F1} ms ({loadMs * 1000 / count:F1} us each), cached {cachedMs:F2} ms (sum {sum})");
        check(sum == 2 * (count * (count - 1) / 2), "[userdata-bench] all loaded");
        assets.ForgetUserData(typeof(UDTestParams).Assembly);
        Directory.Delete(database.FullPath(folder), true);
        if (File.Exists(database.FullPath(folder) + ".meta")) File.Delete(database.FullPath(folder) + ".meta");
        database.Refresh();
        yield return 0;
    }
}
