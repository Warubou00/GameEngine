using System.Text;
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
// ==========================================================================
public enum AssetKind { Folder, Model, Texture, Sound, Scene, Material, Text, Other }

public sealed record AssetEntry(Guid Guid, string Path, AssetKind Kind)
{
    public string Name => System.IO.Path.GetFileName(Path);
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
        return Path.GetExtension(name) switch
        {
            ".obj" or ".pmx" or ".pmd"                    => AssetKind.Model,
            ".png" or ".jpg" or ".jpeg" or ".bmp" or ".tga" or ".dds" => AssetKind.Texture,
            ".wav"                                        => AssetKind.Sound,
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
            if (_byPath.ContainsKey(path)) continue;
            var isFolder = Directory.Exists(file);
            var guid = readOrCreateMeta(file, isFolder, ref created);
            // 同じ Id の .meta が 2 つある (ファイルを .meta ごと複製した) 時は、後の方に新しい Id を付ける
            if (_byGuid.ContainsKey(guid))
            {
                guid = Guid.NewGuid();
                writeMeta(file + ".meta", guid, isFolder ? AssetKind.Folder : KindOf(file));
            }
            var entry = new AssetEntry(guid, path, isFolder ? AssetKind.Folder : KindOf(file));
            _byPath[path] = entry;
            _byGuid[guid] = entry;
            changed = true;
        }
        if (changed) Version++;
        return created;
    }

    private static Guid readOrCreateMeta(string file, bool isFolder, ref int created)
    {
        var meta = file + ".meta";
        if (File.Exists(meta))
        {
            try
            {
                using var document = JsonDocument.Parse(File.ReadAllText(meta, Encoding.UTF8));
                if (document.RootElement.TryGetProperty("guid", out var g) && Guid.TryParse(g.GetString(), out var guid)) return guid;
            }
            catch (Exception ex) when (ex is JsonException or IOException)
            {
                Console.WriteLine($"[Assets] Broken meta file (a new id is made) : {meta} ({ex.Message})");
            }
        }
        var id = Guid.NewGuid();
        writeMeta(meta, id, isFolder ? AssetKind.Folder : KindOf(file));
        created++;
        return id;
    }

    private static void writeMeta(string meta, Guid guid, AssetKind kind)
    {
        var json = $"{{\r\n  \"guid\": \"{guid}\",\r\n  \"kind\": \"{kind}\"\r\n}}\r\n";
        File.WriteAllText(meta, json, new UTF8Encoding(false));
    }

    // ---------------------------------------------------------------------
    // 引く
    // ---------------------------------------------------------------------

    public AssetEntry? Find(Guid guid) => _byGuid.TryGetValue(guid, out var e) ? e : null;
    public AssetEntry? Find(string assetPath) => _byPath.TryGetValue(assetPath, out var e) ? e : null;

    // パス (アセットのパス / 以前の見本のパス / Assets の中の絶対パス) → アセットのパス "Assets/..." (Assets の外なら null)
    public string? ToAssetPath(string path)
    {
        path = path.Replace('\\', '/');
        if (path.StartsWith(AssetsFolder + "/", StringComparison.OrdinalIgnoreCase)) return path;
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
