using System.Diagnostics;
using System.Text;

namespace GameEngine;

/// <summary>LoadScene の読み方 (v1.14)</summary>
public enum LoadSceneMode
{
    /// <summary>今のシーンを全部外してから読む (永続シーンの物は残る)。読んだシーンがメインシーンになる (Unity の Single / UE の OpenLevel)</summary>
    Single,
    /// <summary>今のシーンに足す (メインシーンは変わらない)</summary>
    Additive,
}

/// <summary>シーンの状態 (v1.14)</summary>
public enum SceneStatus
{
    /// <summary>外れた (中の物は消えた)</summary>
    Unloaded,
    /// <summary>読み込みの途中 (ゲームのコードの中で頼まれた物は、次のフレームの頭で読む)</summary>
    Loading,
    /// <summary>読み込んだ (有効か待機かは IsActive)</summary>
    Loaded,
}

// ==========================================================================
// 読み込んだシーン 1 つ (v1.14)。SceneManager が作る
//   メインシーン / 足したシーン / サブシーン (Owner = SubScene を持つエンティティ) / 永続シーン (DontDestroyOnLoad の物)
// ==========================================================================
public sealed class Scene
{
    internal readonly List<Scene> _children = new();

    internal Scene(int id, string path, Scene? parent, Entity owner, bool persistent = false)
    {
        Id = id;
        Path = path;
        Parent = parent;
        Owner = owner;
        IsPersistent = persistent;
    }

    /// <summary>EntityInfo の SceneId に入る番号 (1 = 永続シーン。2 から)</summary>
    public int Id { get; }
    /// <summary>シーンのファイル (アセットのパス "Assets/..."。プロジェクトの外ならそのまま)</summary>
    public string Path { get; }
    /// <summary>ファイルの名前 (.scene.json を除く)</summary>
    public string Name => IsPersistent ? "DontDestroyOnLoad" : SceneManager.NameOf(Path);
    /// <summary>親のシーン (サブシーンなら持ち主のエンティティのシーン。メインシーン / 足したシーンは null)</summary>
    public Scene? Parent { get; internal set; }
    /// <summary>サブシーンの持ち主 (SubScene を持つエンティティ)。サブシーンでなければ Entity.Null</summary>
    public Entity Owner { get; }
    public bool IsSubScene => !Owner.IsNull;
    public bool IsPersistent { get; }
    public SceneStatus State { get; internal set; }
    /// <summary>自分の有効 (SetActive で変える)。待機中は false</summary>
    public bool IsActiveSelf { get; internal set; } = true;
    /// <summary>本当に有効か (自分 / 親のシーン / 持ち主のエンティティが全部有効で、読み込んである)</summary>
    public bool IsActive { get; internal set; }
    /// <summary>中のサブシーン</summary>
    public IReadOnlyList<Scene> Children => _children;

    public override string ToString() => $"Scene({Name}, {State}{(State == SceneStatus.Loaded ? IsActive ? ", active" : ", standby" : "")})";
}

// ==========================================================================
// シーンの管理 (v1.14)。engine.Scenes / SceneManager.Current / Behaviour の Scenes
//
//   ・シーンの切り替え (Unity / UE と同じ)
//       Scenes.LoadScene("Assets/Scenes/stage2.scene.json");                       // 今のシーンを外して読む
//       Scenes.LoadScene("Assets/Scenes/hud.scene.json", LoadSceneMode.Additive);  // 足す
//       Scenes.DontDestroyOnLoad(entity);                                          // 切り替えても残す (ロード画面など)
//   ・シーンの中のシーン (RE Engine と同じ。SubScene コンポーネント)
//       Scenes.LoadSubScene(owner);  Scenes.SetSubSceneActive(owner, false);  Scenes.UnloadSubScene(owner);
//
//   - ゲームのコード (Update / FixedUpdate / LateUpdate / 衝突 / UI のイベント) の中で呼んだ読み込み / 外すは、
//     次のフレームの頭 (全部の System の前) で行う (回している物の配列が途中で変わらないように。Unity も次のフレーム)。
//     戻り値の Scene は State = Loading で返り、読み終わると Loaded になる (SceneLoaded のイベントも)
//     有効 / 待機の切り替えは、すぐ効く (印を変えるだけ)
//   - どのシーンの物かは EntityInfo.SceneId で覚える。0 (コードで作った物など) は今のメインシーンの物として扱う
//     (Single で切り替えると消える。残すなら DontDestroyOnLoad)
//   - 待機 / 無効なシーンの物は、自分の Active に関係なく無効 (描画 / 物理 / Behaviour が飛ばす)
//   - アセットは外しても解放しない (同じ物を次のシーンでも使うと読み直さずに済む)。解放するなら UnloadUnusedAssets
// ==========================================================================
public sealed partial class SceneManager
{
    /// <summary>今のエンジンのシーンの管理 (Engine を作ると入る)</summary>
    public static SceneManager? Current { get; internal set; }

    private readonly World _world;
    private readonly AssetManager _assets;
    private readonly List<Scene> _scenes = new();           // 読み込んだ (読み込み中の) シーン。永続シーンは入れない
    private readonly Dictionary<int, Scene> _byId = new();
    private readonly Dictionary<Entity, Scene> _byOwner = new();   // サブシーンの持ち主 → シーン
    private readonly List<Action> _pending = new();         // ゲームのコードの中で頼まれた読み込み / 外す (次のフレームの頭で)
    private int _nextId = 2;
    private bool _flagsDirty;

    internal SceneManager(World world, AssetManager assets)
    {
        _world = world;
        _assets = assets;
        Persistent = new Scene(1, "", null, Entity.Null, persistent: true) { State = SceneStatus.Loaded, IsActive = true };
        _byId[1] = Persistent;
        world.Cleared += onWorldCleared;
    }

    /// <summary>永続シーン (DontDestroyOnLoad の物。Single で切り替えても残る)</summary>
    public Scene Persistent { get; }
    /// <summary>今のメインシーン (Single で読んだシーン。エディターで開いて作り直した世界などは null)</summary>
    public Scene? ActiveScene { get; private set; }
    /// <summary>読み込んだシーン (メイン / 足した物 / サブシーン。永続シーンは入らない)</summary>
    public IReadOnlyList<Scene> Scenes => _scenes;
    /// <summary>次のフレームの頭で行う、頼まれた読み込み / 外すの数</summary>
    public int PendingCount => _pending.Count;

    /// <summary>シーンを読み終わった後 (サブシーンも)</summary>
    public event Action<Scene>? SceneLoaded;
    /// <summary>シーンを外す前 (中の物はまだある)</summary>
    public event Action<Scene>? SceneUnloading;
    /// <summary>メインシーンが変わった (前, 今)</summary>
    public event Action<Scene?, Scene?>? ActiveSceneChanged;

    // ゲームのコードを呼んでいる間 (Engine が立てる)。この間の読み込み / 外すは次のフレームの頭で行う
    internal bool InGameCode { get; set; }

    /// <summary>シーンのファイルのパス → 名前 (.scene.json を除いたファイル名)</summary>
    public static string NameOf(string path)
    {
        var name = System.IO.Path.GetFileName(path);
        return name.EndsWith(".scene.json", StringComparison.OrdinalIgnoreCase) ? name[..^".scene.json".Length] : System.IO.Path.GetFileNameWithoutExtension(name);
    }

    // ---------------------------------------------------------------------
    // シーンの切り替え / 足す
    // ---------------------------------------------------------------------

    /// <summary>
    /// シーンを読む。Single = 今のシーンを外してから読み、メインシーンにする (永続シーンの物は残る) / Additive = 足す
    /// ゲームのコードの中で呼んだ時は次のフレームの頭で読む (戻り値は State = Loading)
    /// </summary>
    public Scene LoadScene(string path, LoadSceneMode mode = LoadSceneMode.Single)
    {
        var (assetPath, file) = resolve(path);
        if (!File.Exists(file)) throw new FileNotFoundException($"Scene not found : {path}", file);
        var scene = new Scene(_nextId++, assetPath, null, Entity.Null) { State = SceneStatus.Loading };
        if (InGameCode) _pending.Add(() => loadTopLevel(scene, file, mode));
        else loadTopLevel(scene, file, mode);
        return scene;
    }

    // Engine.LoadScene 用 : すぐ読んで、作ったエンティティを返す (以前の Engine.LoadScene と同じ動き)
    internal List<Entity> LoadSceneNow(string path, LoadSceneMode mode)
    {
        var (assetPath, file) = resolve(path);
        var scene = new Scene(_nextId++, assetPath, null, Entity.Null) { State = SceneStatus.Loading };
        return loadTopLevel(scene, file, mode);
    }

    /// <summary>シーンを外す (中の物とサブシーンを消す)。メインシーンを外すと、メインシーンは残った最初のシーンになる</summary>
    public void UnloadScene(Scene scene)
    {
        if (scene.IsPersistent) throw new InvalidOperationException("The persistent scene cannot be unloaded.");
        if (InGameCode) _pending.Add(() => unload(scene));
        else unload(scene);
    }

    /// <summary>
    /// 切り替え (Single) でも消さない (永続シーンへ移す)。子 (Transform の子) も一緒。親があれば外す (見た目の位置はそのまま)
    /// 持っているサブシーンも一緒に残る
    /// </summary>
    public void DontDestroyOnLoad(Entity entity)
    {
        if (!_world.IsAlive(entity)) return;
        if (_world.Has<Transform>(entity) && !_world.Get<Transform>(entity).Parent.IsNull) Hierarchy.SetParent(_world, entity, Entity.Null, keepWorld: true);
        var moved = new List<Entity>();
        collectWithChildren(entity, moved);
        foreach (var e in moved)
        {
            if (!_world.Has<EntityInfo>(e)) _world.Add(e, new EntityInfo($"Entity {e.Id}"));
            _world.Get<EntityInfo>(e).SceneId = Persistent.Id;
            if (_byOwner.TryGetValue(e, out var sub)) reparent(sub, Persistent);
        }
        _flagsDirty = true;
        refreshFlags();
    }

    /// <summary>この物のシーン (SceneId が 0 の物は今のメインシーン)</summary>
    public Scene? GetScene(Entity entity)
    {
        if (!_world.IsAlive(entity) || !_world.Has<EntityInfo>(entity)) return ActiveScene;
        var id = _world.Get<EntityInfo>(entity).SceneId;
        return id == 0 ? ActiveScene : _byId.GetValueOrDefault(id);
    }

    /// <summary>シーンの物 (サブシーンの中の物は入らない)</summary>
    public List<Entity> GetEntities(Scene scene)
    {
        var result = new List<Entity>();
        var infos = _world.GetPool<EntityInfo>();
        var entities = infos.Entities;
        var components = infos.Components;
        var includeUnassigned = scene == ActiveScene;
        for (int i = 0; i < components.Length; i++)
        {
            var id = components[i].SceneId;
            if (id == scene.Id || (includeUnassigned && id == 0)) result.Add(entities[i]);
        }
        return result;
    }

    /// <summary>どのシーンの物も使っていないモデル / マテリアルを解放する (AssetManager.UnloadUnused)</summary>
    public (int Models, int Materials) UnloadUnusedAssets() => _assets.UnloadUnused(_world);

    // ---------------------------------------------------------------------
    // サブシーン (SubScene コンポーネント)
    // ---------------------------------------------------------------------

    /// <summary>
    /// 持ち主 (SubScene を持つエンティティ) のシーンを読む。active : 読んだ後に有効にするか (null = SubScene.ActiveOnLoad)
    /// もう読んであれば、そのシーンを返す (active を渡せば有効 / 待機を変える)。読めない時 (SubScene が無い / 輪になる) は null
    /// </summary>
    public Scene? LoadSubScene(Entity owner, bool? active = null)
    {
        if (_byOwner.TryGetValue(owner, out var loaded) && loaded.State != SceneStatus.Unloaded)
        {
            if (active is { } a) SetActive(loaded, a);
            return loaded;
        }
        if (!_world.IsAlive(owner) || !_world.Has<SubScene>(owner)) return null;
        var sub = _world.Get<SubScene>(owner);
        if (string.IsNullOrWhiteSpace(sub.Scene)) return null;
        var (assetPath, file) = resolve(sub.Scene);
        var parent = GetScene(owner);
        if (isInChain(parent, assetPath))
        {
            Console.WriteLine($"[Scene] Sub scene '{assetPath}' on {nameOf(owner)} is not loaded : it would load itself (cycle)");
            return null;
        }
        if (!File.Exists(file))
        {
            Console.WriteLine($"[Scene] Sub scene not found : {sub.Scene} (on {nameOf(owner)})");
            return null;
        }
        var scene = new Scene(_nextId++, assetPath, parent, owner) { State = SceneStatus.Loading, IsActiveSelf = active ?? sub.ActiveOnLoad };
        _byOwner[owner] = scene;
        if (InGameCode) _pending.Add(() => loadSub(scene, file));
        else loadSub(scene, file);
        return scene;
    }

    /// <summary>持ち主のサブシーンを外す (中の物を消す)</summary>
    public void UnloadSubScene(Entity owner)
    {
        if (_byOwner.TryGetValue(owner, out var scene)) UnloadScene(scene);
    }

    /// <summary>持ち主のサブシーンの有効 / 待機を変える (読んでいなければ読む)。戻り値 = そのシーン (読めなければ null)</summary>
    public Scene? SetSubSceneActive(Entity owner, bool active) => LoadSubScene(owner, active);

    /// <summary>持ち主のサブシーン (読んでいなければ null)</summary>
    public Scene? GetSubScene(Entity owner) => _byOwner.TryGetValue(owner, out var scene) && scene.State != SceneStatus.Unloaded ? scene : null;

    /// <summary>名前で持ち主 (SubScene を持つエンティティ) を探す (無ければ Entity.Null)</summary>
    public Entity FindSubSceneOwner(string name)
    {
        var subs = _world.GetPool<SubScene>();
        var entities = subs.Entities;
        for (int i = 0; i < entities.Length; i++)
        {
            if (_world.Has<EntityInfo>(entities[i]) && _world.Get<EntityInfo>(entities[i]).Name == name) return entities[i];
        }
        return Entity.Null;
    }

    /// <summary>
    /// LoadOnStart のサブシーンで、まだ読んでいない物を読む (エディターが、JSON から世界を作り直した後に呼ぶ)
    /// mainScenePath : 今の世界のシーンのファイル。メインシーンが無ければ、どのシーンでもない物 (SceneId 0) をそのシーンとして登録する
    ///   (自分を読むサブシーンの輪を見つけるため。以前 (S1 の最初) は登録しなかったので、停止の後にメインシーンが自分の中にもう 1 つ読まれた)
    /// 戻り値 = 読んだ数
    /// </summary>
    public int LoadStartSubScenes(string? mainScenePath = null)
    {
        if (ActiveScene == null && !string.IsNullOrEmpty(mainScenePath))
        {
            var scene = new Scene(_nextId++, resolve(mainScenePath).AssetPath, null, Entity.Null) { State = SceneStatus.Loaded };
            register(scene);
            ActiveScene = scene;   // 中の物は SceneId 0 のまま (0 は今のメインシーンの物として扱う)
            _flagsDirty = true;
            refreshFlags();
        }
        var count = 0;
        var subs = _world.GetPool<SubScene>();
        var owners = subs.Entities.ToArray();
        foreach (var owner in owners)
        {
            if (!_world.IsAlive(owner) || !_world.Get<SubScene>(owner).LoadOnStart || GetSubScene(owner) != null) continue;
            if (LoadSubScene(owner) != null) count++;
        }
        return count;
    }

    /// <summary>シーンの有効 / 待機を変える (すぐ効く。中の物とアセットは残る)</summary>
    public void SetActive(Scene scene, bool active)
    {
        if (scene.IsPersistent || scene.IsActiveSelf == active) return;
        scene.IsActiveSelf = active;
        _flagsDirty = true;
        refreshFlags();
    }

    // ---------------------------------------------------------------------
    // 毎フレーム (Engine が、フレームの頭の全部の System の前に呼ぶ)
    // ---------------------------------------------------------------------

    internal void Update()
    {
        // 1. ゲームのコードの中で頼まれた読み込み / 外す
        if (_pending.Count > 0)
        {
            var pending = _pending.ToArray();
            _pending.Clear();
            foreach (var action in pending) action();
        }
        // v1.14 S2 : 非同期の読み込みを、1 フレームの予算 (AsyncBudgetMs) の分だけ進める
        if (_operations.Count > 0 || _assets.PendingModelLoads > 0) updateOperations();   // v1.17 (S4) : 別のスレッドのモデルも
        if (_byOwner.Count == 0 && !_flagsDirty) return;   // サブシーンが無ければここまで (毎フレームの手間は無い)

        // 2. 持ち主が消えた / SubScene が外されたサブシーンは外す
        List<Scene>? orphans = null;
        foreach (var (owner, scene) in _byOwner)
        {
            if (scene.State == SceneStatus.Loaded && (!_world.IsAlive(owner) || !_world.Has<SubScene>(owner))) (orphans ??= new()).Add(scene);
        }
        if (orphans != null) foreach (var scene in orphans) unload(scene);

        // 3. 持ち主の有効が変わった (持ち主を無効にした等) サブシーンの印を直す
        refreshFlags();
    }

    // ---------------------------------------------------------------------
    // 中身
    // ---------------------------------------------------------------------

    private List<Entity> loadTopLevel(Scene scene, string file, LoadSceneMode mode)
    {
        var start = Stopwatch.GetTimestamp();
        CrashReporter.SetInfo(mode == LoadSceneMode.Additive ? "Scene (additive)" : "Scene", scene.Path);   // v1.8 : どのシーンで落ちたか
        var json = File.ReadAllText(file, Encoding.UTF8);
        var previous = ActiveScene;
        if (mode == LoadSceneMode.Single)
        {
            unloadAllButPersistent();
            GameEngine.UI.Manager?.ClearForSceneChange();   // v1.7 : シーンを替えると画面の UI は外れる (UE のレベルの切り替えと同じ)。v1.14 : KeepOnSceneChange の物は残す
            ActiveScene = scene;
        }
        else if (ActiveScene == null && _scenes.Count == 0)
        {
            ActiveScene = scene;   // 何も無い所に足した時は、それがメインシーン
        }
        register(scene);
        var created = loadEntities(scene, json);
        if (mode == LoadSceneMode.Single || ActiveScene == scene) markMainChanged();
        Console.WriteLine($"[Scene] Loaded {scene.Path} ({mode}) : {created.Count} entities, {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms");
        SceneLoaded?.Invoke(scene);
        if (ActiveScene != previous) ActiveSceneChanged?.Invoke(previous, ActiveScene);
        return created;
    }

    private void loadSub(Scene scene, string file)
    {
        if (scene.State != SceneStatus.Loading) return;   // 読む前に外された
        if (!_world.IsAlive(scene.Owner))
        {
            scene.State = SceneStatus.Unloaded;
            _byOwner.Remove(scene.Owner);
            return;
        }
        var start = Stopwatch.GetTimestamp();
        var json = File.ReadAllText(file, Encoding.UTF8);
        if (scene.Parent is { } parent) parent._children.Add(scene);
        register(scene);
        var created = loadEntities(scene, json);
        parentRootsToOwner(scene, created);
        Console.WriteLine($"[Scene] Loaded sub scene {scene.Path} on {nameOf(scene.Owner)} : {created.Count} entities " +
                          $"({(scene.IsActive ? "active" : "standby")}), {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms");
        SceneLoaded?.Invoke(scene);
    }

    // シーンの物を作る (待機なら作る前から無効)。読んだ後に、中の LoadOnStart のサブシーンも読む
    private List<Entity> loadEntities(Scene scene, string json)
    {
        _flagsDirty = true;
        refreshFlags();   // 待機中のシーンの物は作る時から無効にするため、先に印を書く
        var warnings = new List<string>();
        var created = SceneSerializer.Load(_world, json, _assets, warnings, freshIds: false, sceneId: scene.Id);
        foreach (var warning in warnings) Console.WriteLine($"[Scene] {warning}");
        scene.State = SceneStatus.Loaded;
        _flagsDirty = true;
        refreshFlags();
        foreach (var entity in created)
        {
            if (!_world.Has<SubScene>(entity) || !_world.Get<SubScene>(entity).LoadOnStart) continue;
            LoadSubScene(entity);   // InGameCode でなければすぐ読む (次のフレームの頭で読んだ物も、ここは外の扱い)
        }
        return created;
    }

    // 中の一番上の物 (親の無い Transform) を持ち主の子にする (持ち主を動かすと全部動く / 持ち主を無効にすると全部無効)
    private void parentRootsToOwner(Scene scene, List<Entity> created)
    {
        if (!scene.IsSubScene || !_world.IsAlive(scene.Owner) || !_world.Has<Transform>(scene.Owner)) return;
        var transforms = _world.GetPool<Transform>();
        foreach (var entity in created)
        {
            if (!transforms.Has(entity.Id)) continue;
            ref var t = ref transforms.Get(entity.Id);
            if (!t.Parent.IsNull) continue;
            t.Parent = scene.Owner;
            t.HasMatrix = false;
        }
    }

    private void register(Scene scene)
    {
        _scenes.Add(scene);
        _byId[scene.Id] = scene;
    }

    private void unload(Scene scene)
    {
        if (scene.State == SceneStatus.Unloaded) return;
        if (scene.State == SceneStatus.Loading && !_scenes.Contains(scene))
        {
            // 読む前 (頼まれただけ) : 読まない
            scene.State = SceneStatus.Unloaded;
            if (scene.IsSubScene) _byOwner.Remove(scene.Owner);
            return;
        }
        var subtree = new List<Scene>();
        collectSubtree(scene, subtree);
        foreach (var s in subtree) if (s.State == SceneStatus.Loaded) SceneUnloading?.Invoke(s);   // 読み込み中 (非同期) の物は、読み終わっていないので呼ばない
        destroyMembers(subtree.Select(s => s.Id).ToHashSet(), includeUnassigned: false);
        foreach (var s in subtree) forget(s);
        scene.Parent?._children.Remove(scene);
        Console.WriteLine($"[Scene] Unloaded {scene.Path}{(subtree.Count > 1 ? $" (+{subtree.Count - 1} sub scenes)" : "")}");
        if (ActiveScene == scene)
        {
            var previous = ActiveScene;
            ActiveScene = _scenes.FirstOrDefault(s => s.Parent == null);
            markMainChanged();
            ActiveSceneChanged?.Invoke(previous, ActiveScene);
        }
        _flagsDirty = true;
        refreshFlags();
    }

    // Single : 永続シーン (とその中のサブシーン) 以外を全部外す
    // alsoKeep : 非同期で読み終わったシーン (と中のサブシーン)。有効にする時に、これは残して前のシーンだけを外す (S2)
    private void unloadAllButPersistent(Scene? alsoKeep = null)
    {
        var keep = new List<Scene>();
        collectSubtree(Persistent, keep);
        if (alsoKeep != null) collectSubtree(alsoKeep, keep);
        foreach (var scene in _scenes.ToArray())
        {
            if (scene.State == SceneStatus.Loaded && !keep.Contains(scene)) SceneUnloading?.Invoke(scene);
        }
        var keepIds = keep.Select(s => s.Id).ToHashSet();
        if (alsoKeep == null && !anyMember(keepIds))
        {
            _world.Clear();   // 残す物が無い : 全部消す (以前の Engine.LoadScene と同じ。番号も 0 から順に使い直す)。onWorldCleared で一覧も空になる
            return;
        }
        destroyMembers(keepIds, includeUnassigned: false, keep: true);
        foreach (var scene in _scenes.ToArray()) if (!keep.Contains(scene)) forget(scene);
        ActiveScene = null;
    }

    // ids のシーンの物を消す (keep = true なら ids 以外の物を消す)。後ろから消す (World.Clear と同じく、次に作る物が小さい番号から使う)
    private void destroyMembers(HashSet<int> ids, bool includeUnassigned, bool keep = false)
    {
        var infos = _world.GetPool<EntityInfo>();
        var doomed = new List<Entity>();
        foreach (var entity in _world.Entities)
        {
            var sceneId = infos.Has(entity.Id) ? infos.Get(entity.Id).SceneId : 0;
            var inSet = ids.Contains(sceneId) || (includeUnassigned && sceneId == 0);
            if (inSet != keep) doomed.Add(entity);
        }
        for (int i = doomed.Count - 1; i >= 0; i--) _world.DestroyEntity(doomed[i]);
    }

    private bool anyMember(HashSet<int> ids)
    {
        foreach (ref var info in _world.GetPool<EntityInfo>().Components) if (ids.Contains(info.SceneId)) return true;
        return false;
    }

    private void forget(Scene scene)
    {
        scene.State = SceneStatus.Unloaded;
        scene.IsActive = false;
        _scenes.Remove(scene);
        _byId.Remove(scene.Id);
        if (scene.IsSubScene && _byOwner.TryGetValue(scene.Owner, out var s) && s == scene) _byOwner.Remove(scene.Owner);
        _world.SetSceneFlags(scene.Id, 0);
    }

    private void onWorldCleared()
    {
        // World.Clear (エディターの新しいシーン / 停止 / 切り替え) : 物は全部消えたので、シーンの一覧も空にする
        foreach (var scene in _scenes) { scene.State = SceneStatus.Unloaded; scene.IsActive = false; }
        _scenes.Clear();
        _byOwner.Clear();
        _byId.Clear();
        _byId[Persistent.Id] = Persistent;
        Persistent._children.Clear();
        ActiveScene = null;
        _flagsDirty = false;
        onWorldClearedOperations();   // v1.14 S2 : 読み込み中の物も消えたので、非同期の読み込みは取りやめ (次の Update で Completed)
    }

    private void reparent(Scene scene, Scene parent)
    {
        scene.Parent?._children.Remove(scene);
        scene.Parent = parent;
        parent._children.Add(scene);
    }

    private static void collectSubtree(Scene scene, List<Scene> result)
    {
        result.Add(scene);
        foreach (var child in scene._children) collectSubtree(child, result);
    }

    private void collectWithChildren(Entity entity, List<Entity> result)
    {
        result.Add(entity);
        foreach (var child in Hierarchy.GetChildren(_world, entity)) collectWithChildren(child, result);
    }

    private bool isInChain(Scene? scene, string assetPath)
    {
        for (var s = scene; s != null; s = s.Parent)
        {
            if (string.Equals(s.Path, assetPath, StringComparison.OrdinalIgnoreCase)) return true;
        }
        return false;
    }

    private void markMainChanged() => _flagsDirty = true;

    // 全部のシーンの印 (無効 / メインでない) を World に書き、変わったシーンの物の有効をすぐ直す
    private void refreshFlags()
    {
        var changed = _flagsDirty;
        _flagsDirty = false;
        List<Scene>? toggled = null;
        foreach (var scene in _scenes)
        {
            var active = effectiveActive(scene);
            if (scene.IsActive != active) { scene.IsActive = active; (toggled ??= new()).Add(scene); changed = true; }
        }
        if (!changed) return;
        foreach (var scene in _scenes)
        {
            byte flags = 0;
            if (!scene.IsActive) flags |= World.SceneInactive;
            if (scene != ActiveScene) flags |= World.SceneNotMain;
            _world.SetSceneFlags(scene.Id, flags);
        }
        _world.SetSceneFlags(Persistent.Id, World.SceneNotMain);
        if (toggled == null) return;

        // 有効が変わったシーンの物 : すぐ反映する (親からの伝わりは、このフレームの TransformSystem が直す)
        var ids = toggled.Select(s => s.Id).ToHashSet();
        var infos = _world.GetPool<EntityInfo>();
        var entities = infos.Entities;
        var components = infos.Components;
        for (int i = 0; i < components.Length; i++)
        {
            if (ids.Contains(components[i].SceneId)) _world.SetActiveInHierarchy(entities[i].Id, _world.IsSelfActive(components[i]));
        }
    }

    // 読み込んであり、自分 / 親のシーン / 持ち主が全部有効か (持ち主の有効は前のフレームの値)
    private bool effectiveActive(Scene scene)
    {
        if (scene.State != SceneStatus.Loaded || !scene.IsActiveSelf) return false;
        if (scene.Parent is { } parent && !parent.IsPersistent && !effectiveActive(parent)) return false;
        if (scene.IsSubScene)
        {
            if (!_world.IsActive(scene.Owner)) return false;
            // 持ち主の自分の Active (このフレームで SetActive(false) した / 読んだばかりで TransformSystem がまだ回っていない時)
            if (_world.Has<EntityInfo>(scene.Owner) && !_world.IsSelfActive(_world.Get<EntityInfo>(scene.Owner))) return false;
        }
        return true;
    }

    private (string AssetPath, string File) resolve(string path)
    {
        if (_assets.Database?.ToAssetPath(path) is { } assetPath) return (assetPath, _assets.Database.FullPath(assetPath));
        return (path.Replace('\\', '/'), path);
    }

    private string nameOf(Entity entity) => _world.IsAlive(entity) && _world.Has<EntityInfo>(entity) ? _world.Get<EntityInfo>(entity).Name : entity.ToString();
}
