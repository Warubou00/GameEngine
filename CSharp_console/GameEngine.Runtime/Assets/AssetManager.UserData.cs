using System.Reflection;
using System.Text;

namespace GameEngine;

// ==========================================================================
// UserData (.userdata.json) の読み込み / 保存 (v2.0 UD1)
//   同じパスは同じ UserData を返す (使う物どうしで共有。Unity の ScriptableObject と同じ)
//   読み直すと同じインスタンスの中身を差し替えて Version が上がり、Changed が呼ばれる
//   ゲームの DLL を読み直す時 (エディター) は ForgetUserData で古い型のインスタンスを捨てる (古い DLL が解放されるように)
// ==========================================================================
public sealed partial class AssetManager
{
    private readonly Dictionary<string, UserData> _userData = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>UserData を読む。同じパスは同じインスタンスを返す (型が見つからない / 読めない時は例外)</summary>
    //   v2.0 UD3 : 書き出したゲームは .userdata.bin (UserDataBinary) を読む。パスは今まで通り .userdata.json で良い (.bin のパスでも同じ物)
    public UserData LoadUserData(string path)
    {
        (path, var file) = resolveUserData(path);
        if (_userData.TryGetValue(path, out var cached)) return cached;
        if (!File.Exists(file)) throw new FileNotFoundException($"Failed to load UserData: {path}", path);
        var json = readUserDataText(file);
        var data = SceneSerializer.CreateUserData(json, path);
        data.Path = path;
        data.AssetId = _database?.Find(path)?.Guid ?? _database?.Find(UserDataBinary.BinaryPathOf(path))?.Guid ?? Guid.Empty;
        // 中身を読む前に覚える (UserData どうしが互いを指していても、もう一度読みに行かないように)
        _userData.Add(path, data);
        var warnings = new List<string>();
        try
        {
            SceneSerializer.PopulateUserData(data, json, this, warnings, resetFirst: false);
        }
        catch
        {
            _userData.Remove(path);
            throw;
        }
        foreach (var warning in warnings) Console.WriteLine($"[UserData] {path} : {warning}");
        data.NotifyLoaded();
        return data;
    }

    /// <summary>UserData を T として読む (型が違えば例外)</summary>
    public T LoadUserData<T>(string path) where T : UserData =>
        LoadUserData(path) is T typed ? typed : throw new InvalidCastException($"UserData '{path}' is {LoadUserData(path).GetType().Name}, not {typeof(T).Name}.");

    /// <summary>Id から UserData を探して読む (無ければ null)</summary>
    public UserData? LoadUserData(Guid assetId) =>
        _database?.Find(assetId) is { Kind: AssetKind.UserData } entry ? LoadUserData(entry.Path) : null;

    /// <summary>ファイルが (外で) 変わった時 : 同じインスタンスの中身を差し替える (読んでいなければ何もしない)</summary>
    public bool ReloadUserData(string path)
    {
        (path, var file) = resolveUserData(path);
        if (!_userData.TryGetValue(path, out var data) || !File.Exists(file)) return false;
        var warnings = new List<string>();
        try
        {
            SceneSerializer.PopulateUserData(data, readUserDataText(file), this, warnings, resetFirst: true);
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException)
        {
            Console.WriteLine($"[UserData] Reload failed {path} : {ex.Message} (kept the previous values)");
            return false;
        }
        foreach (var warning in warnings) Console.WriteLine($"[UserData] {path} : {warning}");
        data.NotifyLoaded();
        data.MarkChanged();
        ReloadCount++;
        AssetReloaded?.Invoke(path);
        return true;
    }

    /// <summary>
    /// JSON (ファイルと同じ形) を同じインスタンスに入れる (エディターの元に戻す。v2.0 UD2)。ファイルには書かない。
    /// JSON に無い値は型の既定の値になる。型が違う / 壊れた JSON は例外 (何も変えない)
    /// </summary>
    public void SetUserDataJson(UserData data, string json)
    {
        var warnings = new List<string>();
        SceneSerializer.PopulateUserData(data, json, this, warnings, resetFirst: true);
        foreach (var warning in warnings) Console.WriteLine($"[UserData] {data.Path} : {warning}");
        data.NotifyLoaded();
        data.MarkChanged();
    }

    /// <summary>
    /// 読み込み済みの UserData のうち、中身がファイルと違う物をファイルの値に戻す (エディターの再生を止めた時。v2.0 UD2)。
    /// ゲームのコードが再生中に書き換えた値はアセットに残さない。戻り値 = 戻した数
    /// </summary>
    public int RevertUserData()
    {
        var reverted = 0;
        foreach (var (path, data) in _userData.ToList())
        {
            var (_, file) = resolveUserData(path);
            if (!File.Exists(file)) continue;
            string json;
            try { json = readUserDataText(file); }
            catch (Exception ex) when (ex is IOException or InvalidDataException) { Console.WriteLine($"[UserData] Revert failed {path} : {ex.Message}"); continue; }
            if (normalizeNewlines(SceneSerializer.SaveUserData(data)) == normalizeNewlines(json)) continue;
            try
            {
                SetUserDataJson(data, json);
                reverted++;
            }
            catch (Exception ex) when (ex is InvalidDataException or System.Text.Json.JsonException)
            {
                Console.WriteLine($"[UserData] Revert failed {path} : {ex.Message}");
            }
        }
        return reverted;
    }

    // パス → (覚える名前 = .userdata.json のパス, 読むファイル) (v2.0 UD3)
    //   .userdata.bin のパスは .json の名前で覚える。.json が無ければ隣の .bin を読む (書き出したゲーム)
    private (string Key, string File) resolveUserData(string path)
    {
        (path, var file) = resolve(path);
        if (path.EndsWith(UserDataBinary.Extension, StringComparison.OrdinalIgnoreCase)) return (UserDataBinary.JsonPathOf(path), file);
        if (!File.Exists(file) && UserDataBinary.BinaryPathOf(file) is var binary && File.Exists(binary)) return (path, binary);
        return (path, file);
    }

    // ファイルの中身を JSON で (bin なら確かめてから戻す。書き換えられた bin は InvalidDataException)
    private static string readUserDataText(string file)
    {
        var bytes = File.ReadAllBytes(file);
        if (UserDataBinary.IsBinary(bytes)) return UserDataBinary.Decode(bytes);
        var start = bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF ? 3 : 0;   // BOM
        return Encoding.UTF8.GetString(bytes, start, bytes.Length - start);
    }

    private static string normalizeNewlines(string text) => text.Replace("\r\n", "\n").Trim();

    /// <summary>
    /// UserData をファイルに書く。path を渡すとそこに書き (コードで new した物を初めて保存する時)、以後はそのパスの UserData になる
    /// 戻り値 = 書いたアセットのパス
    /// </summary>
    public string SaveUserData(UserData data, string? path = null)
    {
        path ??= data.Path ?? throw new InvalidOperationException("UserData has no path. Pass the path to save it to.");
        if (!path.EndsWith(UserDataFile.Extension, StringComparison.OrdinalIgnoreCase)) path += UserDataFile.Extension;
        (path, var file) = resolve(path);
        if (_userData.TryGetValue(path, out var other) && !ReferenceEquals(other, data))
            throw new InvalidOperationException($"Another UserData is already loaded from {path}.");
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(file));
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        // パスを先に入れてから JSON にする (自分を指すフィールドが、今のパスで書かれるように)
        var previous = data.Path;
        data.Path = path;
        string json;
        try
        {
            json = SceneSerializer.SaveUserData(data);
        }
        catch
        {
            data.Path = previous;
            throw;
        }
        NoteOwnWrite(file);   // エディターのファイルの見張りが「外で変わった」と読み直さないように
        File.WriteAllText(file, json, new UTF8Encoding(false));
        if (previous != null && !previous.Equals(path, StringComparison.OrdinalIgnoreCase)) _userData.Remove(previous);
        _userData[path] = data;
        if (data.AssetId == Guid.Empty) data.AssetId = _database?.Find(path)?.Guid ?? Guid.Empty;
        return path;
    }

    /// <summary>読み込み済みなら返す</summary>
    public UserData? FindLoadedUserData(string path)
    {
        (path, _) = resolve(path);
        return _userData.TryGetValue(path, out var data) ? data : null;
    }

    public IEnumerable<UserData> LoadedUserData => _userData.Values;

    /// <summary>
    /// そのアセンブリの型の UserData を捨てる (エディターがゲームの DLL を読み直す前。持っていると古い DLL が解放されない)。
    /// 次に読む時は新しい型で作り直す。戻り値 = 捨てた数
    /// </summary>
    public int ForgetUserData(Assembly assembly)
    {
        var removed = 0;
        foreach (var (path, data) in _userData.ToList())
        {
            if (data.GetType().Assembly != assembly) continue;
            _userData.Remove(path);
            removed++;
        }
        return removed;
    }
}
