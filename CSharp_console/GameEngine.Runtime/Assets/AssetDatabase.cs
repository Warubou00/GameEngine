using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace GameEngine;

// ==========================================================================
// アセットの一覧 (ED5)
//   プロジェクトの Assets フォルダの中のファイルに、変わらない Id (GUID) を付けて覚える
//   - Id は隣の「ファイル名.meta」(JSON) に書く。Id の無いファイルを見つけたら .meta を作る
//     (ファイルを動かす / 名前を変える時は .meta も一緒に動かすと Id が変わらない)
//   - アセットのパスは "Assets/..." (区切りは /。プロジェクトのフォルダからの相対)。シーンには Id とパスを両方書き、
//     読む時は Id を優先する (動かしたファイルも見つかる)
//   - 以前のパス (見本の "../Model/...") は Assets/Models/... に同じファイルがあればそちらに読み替える
//   - 作られる物 (テクスチャの DDS キャッシュ *.v1.dds) と .meta 自身は一覧に入れない
//   - 表示名 : .meta の "displayName" (エディターの Project に出す名前だけ。ファイル / フォルダの名前とパスはそのまま)
// ==========================================================================
public enum AssetKind { Folder, Model, Texture, Sound, Scene, Material, Text, Other, Prefab, Shader, ShaderGraph, FbxModel, FbxSkeleton, FbxAnimation }

public sealed record AssetEntry(Guid Guid, string Path, AssetKind Kind)
{
    public string Name => System.IO.Path.GetFileName(Path);
    /// <summary>表示名 (.meta の displayName。無ければ null)。エディターの表示だけで、ファイルの名前とパスは変わらない</summary>
    public string? DisplayName { get; init; }
    /// <summary>画面に出す名前 (表示名があればそれ、無ければファイルの名前)</summary>
    public string Label => DisplayName ?? Name;
}

public sealed class AssetDatabase
{
    public const string AssetsFolder = "Assets";
    private const string LegacyModelPrefix = "../Model/";
    private const string LegacyModelTarget = "Assets/Models/";

    // シーンの読み書きが使う今の一覧 (Engine が 1 つだけ持つ前提。無ければパスだけで読み書きする)
    public static AssetDatabase? Current { get; set; }

    public string ProjectRoot { get; }   // Assets フォルダの親 (絶対パス)
    public string AssetsRoot  { get; }
    private readonly Dictionary<Guid, AssetEntry>   _byGuid = new();
    private readonly Dictionary<string, AssetEntry> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<Guid, DateTime>     _metaTimes = new();   // .meta を読んだ時の更新時刻 (外で書き換えられたら表示名を読み直す)

    // 一覧が変わった回数 (画面の作り直しに使う)
    public int Version { get; private set; }
    public IEnumerable<AssetEntry> Entries => _byPath.Values;

    public AssetDatabase(string projectRoot)
    {
        ProjectRoot = Path.GetFullPath(projectRoot);
        AssetsRoot  = Path.Combine(ProjectRoot, AssetsFolder);
        Directory.CreateDirectory(AssetsRoot);
    }

    public static AssetKind KindOf(string path)
    {
        var name = path.ToLowerInvariant();
        if (name.EndsWith(".scene.json"))    return AssetKind.Scene;
        if (name.EndsWith(".material.json")) return AssetKind.Material;
        if (name.EndsWith(".prefab.json"))   return AssetKind.Prefab;   // ED8
        if (name.EndsWith(".shadergraph.json")) return AssetKind.ShaderGraph;   // SH4
        if (name.EndsWith(".mod.fbx")) return AssetKind.FbxModel;       // AN1 : FBX は名前で種類が決まる (ただの .fbx は Other)
        if (name.EndsWith(".skl.fbx")) return AssetKind.FbxSkeleton;
        if (name.EndsWith(".anm.fbx")) return AssetKind.FbxAnimation;
        return Path.GetExtension(name) switch
        {
            ".obj" or ".pmx" or ".pmd"                    => AssetKind.Model,
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".dds" => AssetKind.Texture,
            ".wav"                                        => AssetKind.Sound,
            ".hlsl"                                       => AssetKind.Shader,   // 自分のシェーダー (SH2)
            ".txt" or ".mtl" or ".json" or ".md"          => AssetKind.Text,
            _                                             => AssetKind.Other,
        };
    }

    // 一覧に入れないファイル (.meta / DDS キャッシュ / 隠しファイル)
    private static bool ignored(string file)
    {
        var name = Path.GetFileName(file);
        if (name.StartsWith('.') || name.EndsWith(".meta", StringComparison.OrdinalIgnoreCase)) return true;
        // テクスチャの DDS キャッシュ (例 : grass.jpg.v1.dds)
        var parts = name.Split('.');
        return parts.Length >= 3 && parts[^1].Equals("dds", StringComparison.OrdinalIgnoreCase) && parts[^2].Length >= 2 && parts[^2][0] == 'v' && parts[^2][1..].All(char.IsDigit);
    }

    // ---------------------------------------------------------------------
    // 見回り : Assets の中を全部見て、.meta の無い物に Id を付ける。消えたファイルは一覧から外す
    //   戻り値 = 新しく Id を付けた数
    // ---------------------------------------------------------------------
    public int Refresh()
    {
        var created = 0;
        var changed = false;
        var files = Directory.EnumerateFileSystemEntries(AssetsRoot, "*", SearchOption.AllDirectories).Where(f => !ignored(f)).ToList();
        var seen = new HashSet<string>(files.Select(f => ToAssetPath(f)!), StringComparer.OrdinalIgnoreCase);
        // 消えたファイルを先に外す (以前は後で外していたので、名前を変えたファイルが「同じ Id が 2 つ」と見なされ、新しい Id になった)
        foreach (var gone in _byPath.Keys.Where(p => !seen.Contains(p)).ToList())
        {
            _byGuid.Remove(_byPath[gone].Guid);
            _byPath.Remove(gone);
            changed = true;
        }
        foreach (var file in files)
        {
            var path = ToAssetPath(file)!;
            if (_byPath.TryGetValue(path, out var known))
            {
                // 表示名 : .meta が外で書き換えられた (git で取ってきた / 手で直した) 時だけ読み直す
                if (File.Exists(file + ".meta") && File.GetLastWriteTimeUtc(file + ".meta") is var time && (!_metaTimes.TryGetValue(known.Guid, out var seenTime) || seenTime != time))
                {
                    _metaTimes[known.Guid] = time;
                    var display = readMeta(file + ".meta").DisplayName;
                    if (display != known.DisplayName) { replace(known with { DisplayName = display }); changed = true; }
                }
                continue;
            }
            var isFolder = Directory.Exists(file);
            var (guid, displayName) = readOrCreateMeta(file, isFolder, ref created);
            // 同じ Id の .meta が 2 つある (ファイルを .meta ごと複製した) 時は、後の方に新しい Id を付ける
            if (_byGuid.ContainsKey(guid))
            {
                guid = Guid.NewGuid();
                writeMeta(file + ".meta", guid, isFolder ? AssetKind.Folder : KindOf(file), displayName);
            }
            var entry = new AssetEntry(guid, path, isFolder ? AssetKind.Folder : KindOf(file)) { DisplayName = displayName };
            _byPath[path] = entry;
            _byGuid[guid] = entry;
            if (File.Exists(file + ".meta")) _metaTimes[guid] = File.GetLastWriteTimeUtc(file + ".meta");
            changed = true;
        }
        if (changed) Version++;
        return created;
    }

    private static (Guid Guid, string? DisplayName) readOrCreateMeta(string file, bool isFolder, ref int created)
    {
        var meta = file + ".meta";
        if (File.Exists(meta))
        {
            var (guid, displayName, broken) = readMeta(meta);
            if (guid is { } id) return (id, displayName);
            if (broken != null) Console.WriteLine($"[Assets] Broken meta file (a new id is made) : {meta} ({broken})");
        }
        var newId = Guid.NewGuid();
        writeMeta(meta, newId, isFolder ? AssetKind.Folder : KindOf(file), null);
        created++;
        return (newId, null);
    }

    // .meta を読む (Id / 表示名。読めなければ Id は null と理由)
    private static (Guid? Guid, string? DisplayName, string? Error) readMeta(string meta)
    {
        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(meta, Encoding.UTF8));
            var root = document.RootElement;
            Guid? guid = root.TryGetProperty("guid", out var g) && Guid.TryParse(g.GetString(), out var parsed) ? parsed : null;
            var display = root.TryGetProperty("displayName", out var d) && d.ValueKind == JsonValueKind.String ? CleanDisplayName(d.GetString()) : null;
            return (guid, display, guid == null ? "no guid" : null);
        }
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            return (null, null, ex.Message);
        }
    }

    // .meta を書く。表示名は日本語をそのまま書く (\u30E2 のようにしない。手で見ても読めるように)
    private static readonly JsonSerializerOptions MetaJson = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static void writeMeta(string meta, Guid guid, AssetKind kind, string? displayName)
    {
        var json = $"{{\r\n  \"guid\": \"{guid}\",\r\n  \"kind\": \"{kind}\"";
        if (displayName != null) json += $",\r\n  \"displayName\": {JsonSerializer.Serialize(displayName, MetaJson)}";
        json += "\r\n}\r\n";
        File.WriteAllText(meta, json, new UTF8Encoding(false));
    }

    /// <summary>表示名を整える (前後の空白を取り、改行は空白に。空なら null = 表示名なし)</summary>
    public static string? CleanDisplayName(string? name)
    {
        if (name == null) return null;
        name = name.Replace("\r", " ").Replace("\n", " ").Replace("\t", " ").Trim();
        return name.Length == 0 ? null : name;
    }

    // ---------------------------------------------------------------------
    // 表示名 : エディターの Project に出す名前だけを変える (ファイル / フォルダの名前とパスはそのまま。.meta の displayName)
    //   null か空 = 表示名を消す (ファイルの名前を出す)。戻り値 = 新しい表示名
    // ---------------------------------------------------------------------
    public string? SetDisplayName(string assetPath, string? displayName)
    {
        var entry = Find(assetPath) ?? throw new FileNotFoundException($"Not an asset : {assetPath}");
        if (entry.Path == AssetsFolder) throw new InvalidOperationException("The Assets folder has no display name");
        displayName = CleanDisplayName(displayName);
        if (displayName == entry.Name) displayName = null;   // ファイルの名前と同じなら付けない
        if (displayName == entry.DisplayName) return displayName;
        var meta = FullPath(entry.Path) + ".meta";
        writeMeta(meta, entry.Guid, entry.Kind, displayName);
        _metaTimes[entry.Guid] = File.GetLastWriteTimeUtc(meta);
        replace(entry with { DisplayName = displayName });
        Version++;
        return displayName;
    }

    private void replace(AssetEntry entry)
    {
        _byPath[entry.Path] = entry;
        _byGuid[entry.Guid] = entry;
    }

    /// <summary>アセットのパスを、表示名でつないだ文字にする (例 : "Assets/モデル/box.obj"。表示名の無い所はファイルの名前)</summary>
    public string LabelPath(string assetPath)
    {
        var parts = assetPath.Split('/');
        var labels = (string[])parts.Clone();   // 引くのは元のパスで (書き換えた表示名で次を引かない)
        for (int i = 1; i < parts.Length; i++)
            if (Find(string.Join('/', parts.Take(i + 1))) is { DisplayName: { } label }) labels[i] = label;
        return string.Join('/', labels);
    }

    // ---------------------------------------------------------------------
    // 引く
    // ---------------------------------------------------------------------

    public AssetEntry? Find(Guid guid) => _byGuid.TryGetValue(guid, out var e) ? e : null;
    public AssetEntry? Find(string assetPath) =>
        _byPath.TryGetValue(assetPath, out var e) ? e :
        _moved.TryGetValue(assetPath, out var guid) && _byGuid.TryGetValue(guid, out var moved) ? moved : null;   // 名前を変えた前のパス (ED10)

    // パス (アセットのパス / 以前の見本のパス / Assets の中の絶対パス) → アセットのパス "Assets/..." (Assets の外なら null)
    public string? ToAssetPath(string path)
    {
        path = path.Replace('\\', '/');
        if (path.StartsWith(AssetsFolder + "/", StringComparison.OrdinalIgnoreCase)) return movedPath(path) ?? path;   // 名前を変えた前のパスは今のパスに (ED10)
        if (path.StartsWith(LegacyModelPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var mapped = LegacyModelTarget + path[LegacyModelPrefix.Length..];
            return File.Exists(FullPath(mapped)) ? mapped : null;
        }
        if (Path.IsPathRooted(path))
        {
            var full = Path.GetFullPath(path).Replace('\\', '/');
            var root = ProjectRoot.Replace('\\', '/').TrimEnd('/') + "/";
            if (full.StartsWith(root, StringComparison.OrdinalIgnoreCase)) return full[root.Length..];
        }
        return null;
    }

    public string FullPath(string assetPath) => Path.Combine(ProjectRoot, assetPath.Replace('/', Path.DirectorySeparatorChar));

    // ---------------------------------------------------------------------
    // 名前を変える (ED10。エディターの Project の F2 / 右クリック > Rename)
    //   .meta も一緒に動かすので Id は変わらない (シーン / プレハブ / マテリアルは Id で探すので、そのまま繋がる)
    //   フォルダなら中の全部のパスが変わる。テクスチャの DDS のキャッシュ (名前.拡張子.v1.dds) も一緒に動かす
    //   前のパスは覚えておき、Find / ToAssetPath で今のパスに読み替える
    //     (読み込んだ Model などは前のパスを持ったまま。シーンを保存するとその Id で書かれ、次に開くと新しいパスで読む)
    //   newName は拡張子を除いた名前 (拡張子は前のまま。".scene.json" のような 2 つの拡張子も)。戻り値 = 新しいアセットのパス
    // ---------------------------------------------------------------------
    public event Action<string, string>? Renamed;   // (前のパス, 新しいパス)。フォルダは中の物ごとには呼ばない

    private readonly Dictionary<string, Guid> _moved = new(StringComparer.OrdinalIgnoreCase);

    // 名前を 拡張子を除いた名前 と 拡張子 に分ける ("a.scene.json" → ("a", ".scene.json") / フォルダは拡張子無し)
    public static (string Stem, string Extension) SplitName(string fileName, bool isFolder)
    {
        if (isFolder) return (fileName, "");
        foreach (var compound in new[] { ".scene.json", ".material.json", ".prefab.json", ".shadergraph.json", ".mod.fbx", ".skl.fbx", ".anm.fbx" })
            if (fileName.EndsWith(compound, StringComparison.OrdinalIgnoreCase)) return (fileName[..^compound.Length], fileName[^compound.Length..]);
        var extension = Path.GetExtension(fileName);
        return (fileName[..^extension.Length], extension);
    }

    public string Rename(string assetPath, string newName)
    {
        var entry = Find(assetPath) ?? throw new FileNotFoundException($"Not an asset : {assetPath}");
        assetPath = entry.Path;
        if (assetPath == AssetsFolder) throw new InvalidOperationException("The Assets folder cannot be renamed");
        newName = newName.Trim();
        if (newName.Length == 0) throw new ArgumentException("The name is empty");
        if (newName.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 || newName is "." or ".." || newName.EndsWith('.'))
            throw new ArgumentException($"The name has characters that cannot be used in a file name : {newName}");
        var isFolder = entry.Kind == AssetKind.Folder;
        var (_, extension) = SplitName(entry.Name, isFolder);
        var parent = assetPath[..assetPath.LastIndexOf('/')];
        var newPath = $"{parent}/{newName}{extension}";
        if (newPath == assetPath) return assetPath;
        var source = FullPath(assetPath);
        var target = FullPath(newPath);
        // 大文字 / 小文字だけ違う時は、Windows では同じファイルなので「もうある」にしない
        var caseOnly = string.Equals(newPath, assetPath, StringComparison.OrdinalIgnoreCase);
        if (!caseOnly && (File.Exists(target) || Directory.Exists(target))) throw new IOException($"{newName}{extension} already exists");

        if (isFolder)
        {
            moveDirectory(source, target, caseOnly);
        }
        else
        {
            var caches = Directory.EnumerateFiles(Path.GetDirectoryName(source)!, Path.GetFileName(source) + ".v*.dds").ToList();
            moveFile(source, target, caseOnly);
            foreach (var cache in caches) moveFile(cache, target + Path.GetFileName(cache)[Path.GetFileName(source).Length..], false);
        }
        if (File.Exists(source + ".meta")) moveFile(source + ".meta", target + ".meta", caseOnly);

        // 一覧を書き換える (中の物も。前のパスは覚えておく)
        foreach (var old in _byPath.Values.Where(e => e.Path.Equals(assetPath, StringComparison.OrdinalIgnoreCase) ||
                                                      e.Path.StartsWith(assetPath + "/", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            var moved = old with { Path = newPath + old.Path[assetPath.Length..] };
            _byPath.Remove(old.Path);
            _byPath[moved.Path] = moved;
            _byGuid[moved.Guid] = moved;
            _moved[old.Path] = moved.Guid;
        }
        foreach (var key in _moved.Where(p => _byGuid.TryGetValue(p.Value, out var e) && e.Path.Equals(p.Key, StringComparison.OrdinalIgnoreCase)).Select(p => p.Key).ToList())
            _moved.Remove(key);   // 元の名前に戻した物
        Version++;
        Renamed?.Invoke(assetPath, newPath);
        return newPath;
    }

    private static void moveFile(string source, string target, bool caseOnly)
    {
        if (!caseOnly) { File.Move(source, target); return; }
        var temporary = target + ".renaming";   // 大文字 / 小文字だけ変える時は、一度別の名前を通す
        File.Move(source, temporary);
        File.Move(temporary, target);
    }

    private static void moveDirectory(string source, string target, bool caseOnly)
    {
        if (!caseOnly) { Directory.Move(source, target); return; }
        var temporary = target + ".renaming";
        Directory.Move(source, temporary);
        Directory.Move(temporary, target);
    }

    // 名前を変えた前のパス → 今のパス (変えていなければ null)
    private string? movedPath(string assetPath) =>
        _moved.TryGetValue(assetPath, out var guid) && _byGuid.TryGetValue(guid, out var entry) ? entry.Path : null;

    // ---------------------------------------------------------------------
    // ファイルを持ってくる (Assets の外のファイルを写す。同じ名前があれば "名前 1.拡張子")。戻り値 = アセットのパス
    // ---------------------------------------------------------------------
    public string Import(string sourceFile, string targetFolder)
    {
        var folder = FullPath(targetFolder);
        Directory.CreateDirectory(folder);
        var name = Path.GetFileNameWithoutExtension(sourceFile);
        var extension = Path.GetExtension(sourceFile);
        var target = Path.Combine(folder, name + extension);
        for (int i = 1; File.Exists(target); i++) target = Path.Combine(folder, $"{name} {i}{extension}");
        File.Copy(sourceFile, target);
        // OBJ はマテリアル (.mtl) とテクスチャも一緒に要るので、同じフォルダの物を写す (同じ名前があれば写さない)
        if (extension.Equals(".obj", StringComparison.OrdinalIgnoreCase))
        {
            var sourceFolder = Path.GetDirectoryName(sourceFile)!;
            foreach (var file in Directory.EnumerateFiles(sourceFolder))
            {
                var kind = KindOf(file);
                if ((kind == AssetKind.Texture || file.EndsWith(".mtl", StringComparison.OrdinalIgnoreCase)) && !ignored(file))
                {
                    var copy = Path.Combine(folder, Path.GetFileName(file));
                    if (!File.Exists(copy)) File.Copy(file, copy);
                }
            }
        }
        Refresh();
        return ToAssetPath(target)!;
    }
}
