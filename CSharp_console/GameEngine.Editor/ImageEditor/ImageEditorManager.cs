using GameEngine.Imaging.Codecs;

namespace GameEngine.Editor;

// ==========================================================================
// 画像エディターの窓の一覧 (v2.3)。Project で画像 (.png / .jpg / .jpeg / .bmp / .tga) をダブルクリックで開く
//   1 つの画像に 1 つの窓 (同じ画像をもう一度開くと、その窓を前に出す)。いくつでも開ける
//   読み書きと加工は GameEngine.Imaging (エンジンに依存しない)。ここは表示と操作だけ
// ==========================================================================
public sealed class ImageEditorManager
{
    private readonly Engine _engine;
    private readonly AssetDatabase _database;
    private readonly List<ImageEditorWindow> _windows = new();
    private int _nextId = 1;

    public ImageEditorManager(Engine engine, AssetDatabase database)
    {
        _engine = engine;
        _database = database;
    }

    public IReadOnlyList<ImageEditorWindow> Windows => _windows;

    /// <summary>どれかの窓が入力を使っている (エディターのショートカットを止める)</summary>
    public bool HasFocus => _windows.Any(w => w.HasFocus);

    /// <summary>この画像を開けるか (拡張子)</summary>
    public static bool CanOpen(string path) => ImageCodec.IsSupportedExtension(path);

    /// <summary>開く (アセットのパス "Assets/..." でも、ファイルのフルパスでも)。読めなければ null (理由は Console)</summary>
    public ImageEditorWindow? Open(string path)
    {
        var full = File.Exists(path) ? Path.GetFullPath(path) : _database.FullPath(path);
        var existing = _windows.FirstOrDefault(w => string.Equals(w.FullPath, full, StringComparison.OrdinalIgnoreCase));
        if (existing != null)
        {
            existing.Focus();
            return existing;
        }
        if (!CanOpen(full))
        {
            Console.WriteLine($"[ImageEditor] Unsupported image : {path}");
            return null;
        }
        try
        {
            var window = new ImageEditorWindow(_engine, _database, full, _nextId++);
            _windows.Add(window);
            Console.WriteLine($"[ImageEditor] Opened {full} ({window.Info.Description}, {window.Info.Width}x{window.Info.Height})");
            return window;
        }
        catch (Exception ex) when (ex is ImageFormatException or IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[ImageEditor] Failed to open {path} : {ex.Message}");
            return null;
        }
    }

    public void Draw()
    {
        foreach (var window in _windows.ToList())
        {
            window.Draw();
            if (window.IsClosed)
            {
                window.Dispose();
                _windows.Remove(window);
            }
        }
    }

    /// <summary>全部閉じる (保存していない変更は捨てる。確認用 / 終了時)</summary>
    public void CloseAll()
    {
        foreach (var window in _windows) window.Dispose();
        _windows.Clear();
    }
}
