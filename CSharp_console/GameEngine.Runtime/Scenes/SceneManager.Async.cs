using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;

namespace GameEngine;

// ==========================================================================
// 非同期の読み込み 1 つ (v1.14 S2)。Scenes.LoadSceneAsync / LoadSubSceneAsync が返す (Unity の AsyncOperation)
//
//   var op = Scenes.LoadSceneAsync("Assets/Scenes/stage2.scene.json");
//   op.AllowActivation = false;          // 読み終わっても、true にするまで切り替えない (フェードの間など)
//   ... 毎フレーム : bar.SetPercent(op.Progress);   // 0 → 0.9 (読み終わって待っている) → 1 (切り替えた)
//   if (op.IsReadyToActivate && fadeDone) op.AllowActivation = true;
//   await op;  /  op.Completed += o => ...;          // 続きはメインのスレッド (次のフレームの頭) で呼ばれる
//
//   - 読み込み中のシーンの物は無効 (描画 / 物理 / Behaviour が飛ばす)。有効にした時に全部まとめて有効になる
//   - Single : 前のシーンは有効にする時に外す (それまで前のシーンは動き続ける = ロード画面の裏で前のシーンを残せる)
//   - 取りやめ : 読み込み中のシーンを UnloadScene する / 同期の Single で切り替える / 持ち主を消す → IsCancelled
// ==========================================================================
public sealed class SceneOperation : INotifyCompletion
{
    internal readonly List<SceneJob> Jobs = new();
    internal int Current;   // 物を作っている Job
    private readonly List<Action> _continuations = new();

    internal SceneOperation(Scene scene, LoadSceneMode mode)
    {
        Scene = scene;
        Mode = mode;
    }

    /// <summary>読み込むシーン (サブシーンなら、そのサブシーン)</summary>
    public Scene Scene { get; }
    public LoadSceneMode Mode { get; }
    /// <summary>進み具合 (0 〜 0.9 = 読んでいる / 0.9 = 読み終わって有効にするのを待っている / 1 = 終わった)。Unity と同じ</summary>
    public float Progress { get; internal set; }
    /// <summary>全部読んで、有効にするのを待っている (AllowActivation が false の時)</summary>
    public bool IsReadyToActivate { get; internal set; }
    /// <summary>読み終わったら有効にするか (既定 true)。false の間は IsReadyToActivate のまま待つ</summary>
    public bool AllowActivation { get; set; } = true;
    /// <summary>終わった (有効にした / 取りやめた / 失敗した)</summary>
    public bool IsDone { get; private set; }
    public bool IsCancelled { get; private set; }
    /// <summary>失敗した理由 (ファイルが読めない等)。成功 / 取りやめなら null</summary>
    public string? Error { get; internal set; }

    /// <summary>確認用 : かかったフレームの数 / 1 フレームで読み込みに使った一番長い時間 (ms) / 先に読んだアセットのファイルの大きさ</summary>
    public int Frames { get; internal set; }
    public double MaxStepMilliseconds { get; internal set; }
    public long PrefetchedBytes => Jobs.Sum(j => j.PrefetchedBytes);
    /// <summary>確認用 : 一番時間のかかった物 (名前) とその時間 (ms)。1 つの物は途中で止められないので、フレームが長くなる原因はここを見る</summary>
    public string SlowestEntity { get; internal set; } = "";
    public double SlowestEntityMilliseconds { get; internal set; }

    /// <summary>終わった時 (メインのスレッド。フレームの頭)</summary>
    public event Action<SceneOperation>? Completed;

    // await できるように (続きは Completed と同じ所で呼ぶ = メインのスレッド。別のスレッドで World を触らない)
    public SceneOperation GetAwaiter() => this;
    public bool IsCompleted => IsDone;
    public void GetResult() { }
    public void OnCompleted(Action continuation)
    {
        if (IsDone) continuation();
        else _continuations.Add(continuation);
    }

    internal void Finish(bool cancelled)
    {
        if (IsDone) return;
        IsDone = true;
        IsCancelled = cancelled;
        if (!cancelled) Progress = 1.0f;
        IsReadyToActivate = false;
        foreach (var job in Jobs) job.Loader?.Dispose();
        Completed?.Invoke(this);
        var continuations = _continuations.ToArray();
        _continuations.Clear();
        foreach (var continuation in continuations) continuation();
    }

    public override string ToString() => $"SceneOperation({Scene.Name}, {(IsDone ? IsCancelled ? "cancelled" : "done" : IsReadyToActivate ? "ready" : $"{Progress:P0}")})";
}

// 読み込む 1 つのシーン (Operation の中。読んだシーンの中の LoadOnStart のサブシーンも Job になる)
internal sealed class SceneJob
{
    public required Scene Scene;
    public Task<SceneSerializer.SceneLoader> Prepare = null!;   // 別のスレッド : ファイル → JSON の解析 → アセットのファイルを先に読む
    public SceneSerializer.SceneLoader? Loader;
    public bool Registered;
    public long PrefetchedBytes;
    public string[] AssetPaths = [];                      // v1.17 (S4) : 使うアセットのパス (別のスレッドで集めた物)
    public readonly List<string> ModelLoads = new();      // v1.17 (S4) : 別のスレッドで読み込み中のモデル (終わるまで物を作らない)
    public int ModelLoadCount;                            // 始めた数 (進み具合)
}

public sealed partial class SceneManager
{
    private readonly List<SceneOperation> _operations = new();

    /// <summary>
    /// 非同期の読み込みが 1 フレームに使ってよい時間 (ms。既定 4)。フレームの頭で、この時間の分だけ物を作る
    /// 1 つの物 (大きなモデルを読む物など) はそれ以上かかることがある (途中では止められない)
    /// </summary>
    public double AsyncBudgetMs { get; set; } = 4.0;

    /// <summary>進んでいる非同期の読み込み</summary>
    public IReadOnlyList<SceneOperation> Operations => _operations;

    /// <summary>
    /// シーンを非同期で読む (v1.14 S2)。ファイルの読み込みと解析は別のスレッド、物を作るのはフレームの頭で AsyncBudgetMs ずつ
    /// 読み終わると AllowActivation (既定 true) で有効にする。Single は、その時に前のシーンを外す
    /// ゲームのコードの中から呼んでよい (World を触るのは次のフレームの頭から)
    /// </summary>
    public SceneOperation LoadSceneAsync(SceneReference scene, LoadSceneMode mode = LoadSceneMode.Single) =>
        LoadSceneAsync(scene.Path ?? throw new ArgumentException("SceneReference is empty (None).", nameof(scene)), mode);   // v1.20 GO3

    public SceneOperation LoadSceneAsync(string path, LoadSceneMode mode = LoadSceneMode.Single)
    {
        var (assetPath, file) = resolve(path);
        if (!File.Exists(file)) throw new FileNotFoundException($"Scene not found : {path}", file);
        var scene = new Scene(_nextId++, assetPath, null, Entity.Null) { State = SceneStatus.Loading };
        var op = new SceneOperation(scene, mode);
        op.Jobs.Add(newJob(scene, file));
        _operations.Add(op);
        CrashReporter.SetInfo("Scene (loading)", assetPath);
        return op;
    }

    /// <summary>
    /// 持ち主 (SubScene を持つエンティティ) のシーンを非同期で読む。active : 読んだ後に有効にするか (null = SubScene.ActiveOnLoad)
    /// もう読んであれば、終わった Operation を返す (active を渡せば有効 / 待機を変える)。読み込み中ならその Operation。読めなければ null
    /// </summary>
    public SceneOperation? LoadSubSceneAsync(Entity owner, bool? active = null)
    {
        if (_byOwner.TryGetValue(owner, out var existing) && existing.State != SceneStatus.Unloaded)
        {
            if (existing.State == SceneStatus.Loading && _operations.FirstOrDefault(o => o.Jobs.Any(j => j.Scene == existing)) is { } running)
            {
                if (active is { } a) existing.IsActiveSelf = a;
                return running;
            }
            if (active is { } b) SetActive(existing, b);
            var done = new SceneOperation(existing, LoadSceneMode.Additive);
            done.Finish(cancelled: false);
            return done;
        }
        if (!_world.IsAlive(owner) || !_world.Has<SubScene>(owner)) return null;
        var sub = _world.Get<SubScene>(owner);
        if (prepareSubScene(owner, sub, GetScene(owner), active) is not { } job) return null;
        var op = new SceneOperation(job.Scene, LoadSceneMode.Additive);
        op.Jobs.Add(job);
        _operations.Add(op);
        return op;
    }

    // サブシーンの Job を作る (輪 / 無いファイルは null)。持ち主の表にも入れる (同じ持ち主を 2 回読まない)
    private SceneJob? prepareSubScene(Entity owner, SubScene sub, Scene? parent, bool? active)
    {
        if (string.IsNullOrWhiteSpace(sub.Scene)) return null;
        var (assetPath, file) = resolve(sub.Scene);
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
        return newJob(scene, file);
    }

    // 別のスレッドで : ファイルを読む → JSON を解析する → 使うアセットのファイルを先に読んでおく (World には触らない)
    private SceneJob newJob(Scene scene, string file)
    {
        var database = _assets.Database;
        var job = new SceneJob { Scene = scene };
        var world = _world;
        var assets = _assets;
        var sceneId = scene.Id;
        job.Prepare = Task.Run(() =>
        {
            var json = File.ReadAllText(file, Encoding.UTF8);
            var loader = new SceneSerializer.SceneLoader(world, json, assets, freshIds: false, sceneId: sceneId);
            loader.Parse();
            long bytes = 0;
            var buffer = new byte[1 << 20];
            var assetPaths = loader.CollectAssetPaths();
            job.AssetPaths = assetPaths.ToArray();
            foreach (var path in assetPaths)
            {
                try
                {
                    var full = Path.IsPathRooted(path) ? path : database != null && path.StartsWith(AssetDatabase.AssetsFolder + "/", StringComparison.OrdinalIgnoreCase) ? database.FullPath(path) : path;
                    if (!File.Exists(full)) continue;
                    using var stream = new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, 1 << 16, FileOptions.SequentialScan);
                    int read;
                    while ((read = stream.Read(buffer, 0, buffer.Length)) > 0) bytes += read;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }   // 先に読めなくても、後で本当に読む時に分かる
            }
            job.PrefetchedBytes = bytes;
            return loader;
        });
        return job;
    }

    // フレームの頭 (Update) : 予算の分だけ進める
    private void updateOperations()
    {
        var start = Stopwatch.GetTimestamp();
        var deadline = start + (long)(Math.Max(0.0, AsyncBudgetMs) * Stopwatch.Frequency / 1000.0);
        // v1.17 (S4) : 別のスレッドで読み終わったモデルを GPU に登録する (取りやめた読み込みの物も、ここで片付く)
        if (_assets.PendingModelLoads > 0) _assets.FinishModelLoads(Math.Max(0.0, AsyncBudgetMs));
        foreach (var op in _operations.ToArray())
        {
            if (op.IsDone) continue;
            var opStart = Stopwatch.GetTimestamp();
            op.Frames++;   // このフレームも数える (以前は後で数えたので、1 フレームで終わった物が 0 になった)
            stepOperation(op, deadline);
            op.MaxStepMilliseconds = Math.Max(op.MaxStepMilliseconds, Stopwatch.GetElapsedTime(opStart).TotalMilliseconds);
        }
        _operations.RemoveAll(op => op.IsDone);
    }

    private void stepOperation(SceneOperation op, long deadline)
    {
        // 外から取りやめられた (UnloadScene / 同期の Single / World.Clear / 持ち主を消した)
        if (op.Scene.State == SceneStatus.Unloaded || (op.Scene.IsSubScene && !_world.IsAlive(op.Scene.Owner)))
        {
            cancel(op, "cancelled");
            return;
        }

        while (!op.IsReadyToActivate)
        {
            var job = op.Jobs[op.Current];
            if (!job.Prepare.IsCompleted) break;   // 別のスレッドがまだ読んでいる
            if (job.Prepare.IsFaulted || job.Prepare.IsCanceled)
            {
                op.Error = job.Prepare.Exception?.GetBaseException().Message ?? "cancelled";
                cancel(op, $"failed : {op.Error}");
                return;
            }
            if (job.Scene.State == SceneStatus.Unloaded)
            {
                // 中のサブシーンだけ外された (持ち主を消した等) : 飛ばす
                if (!job.Registered) job.Prepare.Result.Dispose();
                if (!next(op)) break;
                continue;
            }
            if (!job.Registered) startJob(job);

            // v1.17 (S4) : このシーンが使うモデルを別のスレッドで読んでいる間は、物を作らない (作ると LoadModel がメインスレッドで待つため)
            if (job.ModelLoads.Count > 0)
            {
                job.ModelLoads.RemoveAll(path => !_assets.IsModelLoading(path));
                if (job.ModelLoads.Count > 0) break;
            }

            // 物を作る (少なくとも 1 手は進める)
            var loader = job.Loader!;
            while (true)
            {
                var stepStart = Stopwatch.GetTimestamp();
                var more = loader.Step();
                var ms = Stopwatch.GetElapsedTime(stepStart).TotalMilliseconds;
                if (ms > op.SlowestEntityMilliseconds && !loader.LastEntity.IsNull)
                {
                    op.SlowestEntityMilliseconds = ms;
                    op.SlowestEntity = nameOf(loader.LastEntity);
                }
                if (!more || Stopwatch.GetTimestamp() >= deadline) break;
            }
            if (!loader.IsFinished) break;   // 予算を使い切った : 次のフレームに続ける
            finishJob(op, job);
            if (!next(op) || Stopwatch.GetTimestamp() >= deadline) break;
        }

        // 進み具合 (物を作る手数で 0 〜 0.9)
        int done = 0, total = 0;
        foreach (var job in op.Jobs)
        {
            if (job.Loader is { } l) { done += l.Done; total += l.Total; }
            else total += 1;
            // v1.17 (S4) : 別のスレッドのモデル 1 つを、物 10 個分と数える (重さが違うので)
            done += (job.ModelLoadCount - job.ModelLoads.Count) * 10;
            total += job.ModelLoadCount * 10;
        }
        op.Progress = op.IsReadyToActivate ? 0.9f : 0.9f * done / Math.Max(1, total);

        if (op.IsReadyToActivate && op.AllowActivation) activate(op);
    }

    // 次の Job へ。全部終わったら有効にするのを待つ。戻り値 = まだ Job があるか
    private static bool next(SceneOperation op)
    {
        op.Current++;
        if (op.Current < op.Jobs.Count) return true;
        op.IsReadyToActivate = true;
        return false;
    }

    private void startJob(SceneJob job)
    {
        job.Loader = job.Prepare.Result;
        // v1.17 (S4) : 使うモデルを別のスレッドで読み始める (解析 + テクスチャのデコード。GPU への登録は Update で)
        foreach (var path in job.AssetPaths)
        {
            if (_assets.BeginLoadModel(path)) job.ModelLoads.Add(path);
        }
        job.ModelLoadCount = job.ModelLoads.Count;
        if (job.Scene.Parent is { } parent && job.Scene.IsSubScene) parent._children.Add(job.Scene);
        register(job.Scene);
        job.Registered = true;
        _flagsDirty = true;
        refreshFlags();   // 読み込み中 (State = Loading) のシーンは無効 : 作る物は最初から無効
    }

    // 1 つのシーンの物を全部作った : サブシーンなら持ち主の子に、中の LoadOnStart のサブシーンも同じ Operation で読む
    private void finishJob(SceneOperation op, SceneJob job)
    {
        var loader = job.Loader!;
        foreach (var warning in loader.Warnings) Console.WriteLine($"[Scene] {warning}");
        parentRootsToOwner(job.Scene, loader.Created);
        foreach (var entity in loader.Created)
        {
            if (!_world.Has<SubScene>(entity)) continue;
            var sub = _world.Get<SubScene>(entity);
            if (!sub.LoadOnStart) continue;
            if (prepareSubScene(entity, sub, job.Scene, null) is { } child) op.Jobs.Add(child);
        }
    }

    // 読み終わった : 有効にする (Single なら前のシーンを外す)
    private void activate(SceneOperation op)
    {
        var previous = ActiveScene;
        var created = op.Jobs.Where(j => j.Registered).Sum(j => j.Loader!.Created.Count);
        if (!op.Scene.IsSubScene)
        {
            if (op.Mode == LoadSceneMode.Single)
            {
                unloadAllButPersistent(alsoKeep: op.Scene);   // 前のシーンを外す (読んだシーンと永続シーンは残す)
                GameEngine.UI.Manager?.ClearForSceneChange();
                ActiveScene = op.Scene;
            }
            else if (ActiveScene == null && !_scenes.Any(s => s.Parent == null && s.State == SceneStatus.Loaded))
            {
                ActiveScene = op.Scene;
            }
            CrashReporter.SetInfo(op.Mode == LoadSceneMode.Additive ? "Scene (additive)" : "Scene", op.Scene.Path);
        }
        foreach (var job in op.Jobs)
        {
            if (job.Registered && job.Scene.State == SceneStatus.Loading) job.Scene.State = SceneStatus.Loaded;
        }
        markMainChanged();
        refreshFlags();   // ここで中の物がまとめて有効になる
        Console.WriteLine($"[Scene] Loaded {op.Scene.Path} (async {(op.Scene.IsSubScene ? $"sub scene on {nameOf(op.Scene.Owner)}" : op.Mode.ToString())}) : " +
                          $"{created} entities, {op.Jobs.Count(j => j.Registered)} scenes, {op.Frames} frames, max {op.MaxStepMilliseconds:F1} ms / frame, " +
                          $"prefetched {op.PrefetchedBytes / (1024.0 * 1024.0):F1} MB" +
                          (op.SlowestEntityMilliseconds > 16 ? $", slowest '{op.SlowestEntity}' {op.SlowestEntityMilliseconds:F0} ms" : ""));
        for (int i = op.Jobs.Count - 1; i >= 0; i--)   // 同期の読み込みと同じく、中のサブシーンから
        {
            if (op.Jobs[i].Registered && op.Jobs[i].Scene.State == SceneStatus.Loaded) SceneLoaded?.Invoke(op.Jobs[i].Scene);
        }
        if (ActiveScene != previous) ActiveSceneChanged?.Invoke(previous, ActiveScene);
        op.Finish(cancelled: false);
    }

    private void cancel(SceneOperation op, string reason)
    {
        foreach (var job in op.Jobs)
        {
            if (job.Registered) unload(job.Scene);   // 作りかけの物を消す (外されていれば何もしない)
            else
            {
                job.Scene.State = SceneStatus.Unloaded;
                if (job.Scene.IsSubScene && _byOwner.TryGetValue(job.Scene.Owner, out var s) && s == job.Scene) _byOwner.Remove(job.Scene.Owner);
                if (job.Prepare.IsCompletedSuccessfully) job.Prepare.Result.Dispose();
            }
        }
        Console.WriteLine($"[Scene] Loading {op.Scene.Path} {reason}");
        op.Finish(cancelled: true);
    }

    private void onWorldClearedOperations()
    {
        foreach (var op in _operations)
        {
            foreach (var job in op.Jobs) job.Scene.State = SceneStatus.Unloaded;
        }
    }
}
