namespace GameEngine.Editor;

// ==========================================================================
// シェーダーの読み直し (ED5 後半)
//   シェーダーの元のファイル (リポジトリの RHI/Source/Shader) と、実行中に読むフォルダ (作業フォルダの ../Shader) を見張る
//   - 元のファイルが変わったら、実行中に読むフォルダへ写す (ビルドし直さなくても効くように)
//   - どちらかが変わったら、最後の変化から 0.3 秒後に Graphics.ReloadShaders (GPU を待ってからコンパイルし直す)
//   コンパイルに失敗したシェーダーは前の物を使い続ける (エラーは Console に出る)
// ==========================================================================
internal sealed class ShaderHotReload : IDisposable
{
    private readonly Engine _engine;
    private readonly string _runtimeFolder;
    private readonly string? _sourceFolder;
    private readonly List<FileSystemWatcher> _watchers = new();
    private volatile bool _changed;
    private double _changedAt;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, byte> _sourceChanges = new(StringComparer.OrdinalIgnoreCase);

    public int ReloadCount { get; private set; }

    public ShaderHotReload(Engine engine, string runtimeFolder, string? sourceFolder)
    {
        _engine = engine;
        _runtimeFolder = Path.GetFullPath(runtimeFolder);
        _sourceFolder  = sourceFolder != null && Directory.Exists(sourceFolder) ? Path.GetFullPath(sourceFolder) : null;
        watch(_runtimeFolder, source: false);
        if (_sourceFolder != null) watch(_sourceFolder, source: true);
    }

    private void watch(string folder, bool source)
    {
        if (!Directory.Exists(folder)) return;
        try
        {
            var watcher = new FileSystemWatcher(folder, "*.shader") { NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName, EnableRaisingEvents = true };
            FileSystemEventHandler handler = (_, e) =>
            {
                if (source) _sourceChanges[e.FullPath] = 0;
                _changed = true;
            };
            watcher.Changed += handler;
            watcher.Created += handler;
            watcher.Renamed += (_, e) => handler(null!, e);
            _watchers.Add(watcher);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Shader] Cannot watch {folder} : {ex.Message}");
        }
    }

    // 毎フレーム (メインのスレッド)
    public void Update()
    {
        if (_changed) { _changed = false; _changedAt = Time.TotalTime; }
        if (_changedAt <= 0 || Time.TotalTime - _changedAt < 0.3) return;
        _changedAt = 0;
        // 元のファイルを実行中のフォルダへ写す (中身が同じなら写さない = 写したことでもう一度読み直さない)
        foreach (var file in _sourceChanges.Keys.ToList())
        {
            _sourceChanges.TryRemove(file, out _);
            try
            {
                var target = Path.Combine(_runtimeFolder, Path.GetFileName(file));
                var text = File.ReadAllBytes(file);
                if (File.Exists(target) && File.ReadAllBytes(target).AsSpan().SequenceEqual(text)) continue;
                File.WriteAllBytes(target, text);
                Console.WriteLine($"[Shader] Copied {Path.GetFileName(file)} -> {_runtimeFolder}");
            }
            catch (IOException ex)
            {
                Console.WriteLine($"[Shader] Failed to copy {file} : {ex.Message}");
                _changed = true;   // 書き込み中だった。もう一度
            }
        }
        Reload();
    }

    public bool Reload()
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var ok = _engine.Graphics.ReloadShaders();
        ReloadCount++;
        Console.WriteLine($"[Shader] Reloaded shaders{(ok ? "" : " (errors : the previous shaders are kept. See the log above)")}, {System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds:F1} ms");
        return ok;
    }

    public void Dispose()
    {
        foreach (var watcher in _watchers) watcher.Dispose();
    }
}
