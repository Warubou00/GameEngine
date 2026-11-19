using System.Text;
using System.Text.Json;

namespace GameEngine.Editor;

// ==========================================================================
// 窓の並びのプリセット (v2.1。View > Layout)
//   - 既定の並び (Default。消せない) と、自分で登録した並び 5 つまで
//   - 名前は自由 (日本語も可)。前後の空白は取り、空 / 同じ名前 (大文字小文字を区別しない) / "Default" は断る
//   - 中身 = ImGui の ini (窓の位置 / 大きさ / docking) + ini に入らない「窓を出すか」(Hierarchy などの View の印)
//   - 置き場は %LOCALAPPDATA%\<エンジン名>\Editor\layouts.json (プロジェクトにもエディターの版にも依らない。
//     Package のエディターのフォルダは版を上げると消えるので、exe の隣には置かない)
// ==========================================================================
internal sealed class LayoutPresets
{
    public const int MaxPresets    = 5;
    public const int MaxNameLength = 32;   // 文字の数 (日本語も 1 文字)
    public const string DefaultName = "Default";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    public sealed class Preset
    {
        public string Name { get; set; } = "";
        public string Ini  { get; set; } = "";
        public Dictionary<string, bool> Windows { get; set; } = new();   // 窓を出すか (名前 → 出す)
    }

    private sealed class FileData
    {
        public int Version { get; set; } = 1;
        public List<Preset> Presets { get; set; } = new();
    }

    private readonly List<Preset> _presets = new();
    public IReadOnlyList<Preset> Presets => _presets;
    public string FilePath { get; }

    public LayoutPresets(string file)
    {
        FilePath = file;
        try
        {
            if (File.Exists(file) && JsonSerializer.Deserialize<FileData>(File.ReadAllText(file, Encoding.UTF8), JsonOptions) is { } data)
            {
                // 手で書き換えられた時のため、ここでも数と名前を確かめる
                foreach (var p in data.Presets)
                    if (_presets.Count < MaxPresets && CheckName(p.Name, -1) == null) { p.Name = p.Name.Trim(); _presets.Add(p); }
            }
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[Layout] Cannot read {file} : {ex.Message}");
        }
    }

    /// <summary>既定の置き場 (%LOCALAPPDATA%\&lt;エンジン名&gt;\Editor\layouts.json)</summary>
    public static string DefaultFile() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), EngineInfo.Name, "Editor", "layouts.json");   // クラッシュレポートと同じ所 (Tsumugi Engine)

    public bool IsFull => _presets.Count >= MaxPresets;

    /// <summary>名前を確かめる。使えれば null、使えなければ理由 (except = 名前を変える物の番号。自分とは同じでよい)</summary>
    public string? CheckName(string? name, int except)
    {
        var n = name?.Trim() ?? "";
        if (n.Length == 0) return "Enter a name";
        if (n.EnumerateRunes().Count() > MaxNameLength) return $"Names can be up to {MaxNameLength} characters";
        if (n.Equals(DefaultName, StringComparison.OrdinalIgnoreCase)) return $"\"{DefaultName}\" is reserved for the default layout";
        for (int i = 0; i < _presets.Count; i++)
            if (i != except && _presets[i].Name.Equals(n, StringComparison.OrdinalIgnoreCase)) return "A layout with this name already exists";
        return null;
    }

    /// <summary>新しく登録する。戻り値 : 断った理由 (登録できれば null)</summary>
    public string? Add(string name, string ini, Dictionary<string, bool> windows)
    {
        if (IsFull) return $"Up to {MaxPresets} layouts (delete or overwrite one)";
        if (CheckName(name, -1) is { } error) return error;
        _presets.Add(new Preset { Name = name.Trim(), Ini = ini, Windows = new(windows) });
        Save();
        return null;
    }

    /// <summary>今の並びで上書きする (名前はそのまま)</summary>
    public void Overwrite(int index, string ini, Dictionary<string, bool> windows)
    {
        if ((uint)index >= (uint)_presets.Count) return;
        _presets[index].Ini = ini;
        _presets[index].Windows = new(windows);
        Save();
    }

    public string? Rename(int index, string name)
    {
        if ((uint)index >= (uint)_presets.Count) return "No such layout";
        if (CheckName(name, index) is { } error) return error;
        _presets[index].Name = name.Trim();
        Save();
        return null;
    }

    public void Remove(int index)
    {
        if ((uint)index >= (uint)_presets.Count) return;
        _presets.RemoveAt(index);
        Save();
    }

    public void Move(int index, int to)
    {
        if ((uint)index >= (uint)_presets.Count || (uint)to >= (uint)_presets.Count || index == to) return;
        var p = _presets[index];
        _presets.RemoveAt(index);
        _presets.Insert(to, p);
        Save();
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            var temp = FilePath + ".tmp";   // 書いている途中で落ちても前のファイルが残るように
            File.WriteAllText(temp, JsonSerializer.Serialize(new FileData { Presets = _presets }, JsonOptions), new UTF8Encoding(false));
            File.Move(temp, FilePath, true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[Layout] Cannot save {FilePath} : {ex.Message}");
        }
    }
}
