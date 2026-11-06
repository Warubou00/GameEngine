using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameEngine.Editor;

// ==========================================================================
// 使っているアセットを探す (ED8 後半。ゲームの書き出しで、使っている物だけを入れるため)
//   シーン / プレハブ / マテリアル (JSON) : 中の {guid, path} と "Assets/..." の文字を全部辿る (プレハブ / マテリアルの中も辿る)
//   モデル : .obj → mtllib の .mtl → map_Kd のテクスチャ / .pmx → テクスチャの表
//            (テクスチャが無い時は、C++ と同じく同じ名前で拡張子の違う物 (.png / .jpg ...) を探す)
//   テクスチャ : 隣の DDS のキャッシュ (名前.v1.dds) も入れる (初めの起動で作り直さなくてよい)
//   ゲームのコード : Scripts の .cs の "Assets/..." / "../Model/..." の文字 (engine.Assets.LoadModel("...") で読む物)
//   Resources という名前のフォルダの中は、いつも全部入れる (コードで名前を組み立てて読む物の置き場)
// ==========================================================================
internal sealed class AssetDependencies
{
    private readonly AssetDatabase _database;
    private readonly HashSet<string> _assets = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, List<string>> _reasons = new(StringComparer.OrdinalIgnoreCase);   // 何から使われているか (窓に出す)
    public List<string> Warnings { get; } = new();

    public AssetDependencies(AssetDatabase database) => _database = database;

    public IReadOnlyCollection<string> Assets => _assets;
    public IReadOnlyList<string> ReasonsFor(string asset) => _reasons.TryGetValue(asset, out var list) ? list : Array.Empty<string>();

    private static readonly JsonDocumentOptions JsonOptions = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };
    private static readonly Regex ScriptPath = new("\"((?:Assets|\\.\\./Model)/[^\"\\r\\n]+)\"", RegexOptions.Compiled);

    // ---------------------------------------------------------------------
    // 入り口
    // ---------------------------------------------------------------------

    public void AddScene(string scenePath) => add(scenePath, "(scene list)");

    // ゲームのコードの中の "Assets/..." (Editor / bin / obj の中は見ない)
    public void AddScripts(string scriptsFolder)
    {
        if (!Directory.Exists(scriptsFolder)) return;
        foreach (var file in Directory.EnumerateFiles(scriptsFolder, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(scriptsFolder, file).Replace('\\', '/');
            if (relative.StartsWith("Editor/") || relative.StartsWith("bin/") || relative.StartsWith("obj/") || relative.Contains("/bin/") || relative.Contains("/obj/")) continue;
            foreach (Match match in ScriptPath.Matches(File.ReadAllText(file, Encoding.UTF8)))
            {
                var assetPath = _database.ToAssetPath(match.Groups[1].Value);
                if (assetPath != null && File.Exists(_database.FullPath(assetPath))) add(assetPath, $"Scripts/{relative}");
                else Warnings.Add($"Scripts/{relative} : \"{match.Groups[1].Value}\" is not an asset (not included)");
            }
        }
    }

    // Resources フォルダの中を全部
    public void AddResourcesFolders()
    {
        foreach (var entry in _database.Entries.Where(e => e.Kind == AssetKind.Folder && Path.GetFileName(e.Path).Equals("Resources", StringComparison.OrdinalIgnoreCase)).ToList())
        {
            foreach (var file in Directory.EnumerateFiles(_database.FullPath(entry.Path), "*", SearchOption.AllDirectories))
            {
                // アセットの一覧にある物だけ (.meta / DDS のキャッシュは別に扱う)
                if (_database.ToAssetPath(file) is { } assetPath && _database.Find(assetPath) is { Kind: not AssetKind.Folder }) add(assetPath, entry.Path);
            }
        }
    }

    // ---------------------------------------------------------------------
    // 辿る
    // ---------------------------------------------------------------------

    private void add(string assetPath, string from)
    {
        assetPath = assetPath.Replace('\\', '/');
        if (!_reasons.TryGetValue(assetPath, out var reasons)) _reasons[assetPath] = reasons = new List<string>();
        if (!reasons.Contains(from)) reasons.Add(from);
        if (!_assets.Add(assetPath)) return;   // 一度辿った物は辿らない (プレハブが自分を入れていても止まる)

        var file = _database.FullPath(assetPath);
        if (!File.Exists(file)) { Warnings.Add($"{from} : missing asset {assetPath}"); _assets.Remove(assetPath); return; }
        var name = assetPath.ToLowerInvariant();
        try
        {
            if (name.EndsWith(".json")) walkJson(assetPath, file);   // シーン / プレハブ / マテリアル
            else if (name.EndsWith(".obj")) walkObj(assetPath, file);
            else if (name.EndsWith(".pmx")) walkPmx(assetPath, file);
            else if (name.EndsWith(".mod.fbx")) walkFbx(assetPath, file);   // AN2
            if (AssetDatabase.KindOf(file) == AssetKind.Texture) addTextureCache(file);
        }
        catch (Exception ex) when (ex is IOException or JsonException or EndOfStreamException or ArgumentException)
        {
            Warnings.Add($"{assetPath} : cannot read dependencies ({ex.Message})");
        }
    }

    // JSON の中の { "guid": ... } と "Assets/..." の文字
    private void walkJson(string assetPath, string file)
    {
        using var document = JsonDocument.Parse(File.ReadAllText(file, Encoding.UTF8), JsonOptions);
        visit(document.RootElement);

        void visit(JsonElement element)
        {
            switch (element.ValueKind)
            {
            case JsonValueKind.Object:
                if (element.TryGetProperty("guid", out var guid) && guid.ValueKind == JsonValueKind.String && Guid.TryParse(guid.GetString(), out var id) && _database.Find(id) is { } entry)
                {
                    if (entry.Kind != AssetKind.Folder) add(entry.Path, assetPath);
                }
                foreach (var property in element.EnumerateObject()) visit(property.Value);
                break;
            case JsonValueKind.Array:
                foreach (var item in element.EnumerateArray()) visit(item);
                break;
            case JsonValueKind.String:
                // guid で見つからない古い形式 / 文字で持っているパス
                var text = element.GetString();
                if (text != null && (text.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || text.StartsWith("../Model/", StringComparison.OrdinalIgnoreCase)) &&
                    _database.ToAssetPath(text) is { } path && File.Exists(_database.FullPath(path)))
                    add(path, assetPath);
                break;
            }
        }
    }

    // FBX のモデル → テクスチャ (ファイルの物だけ。FBX に入っている物は FBX と一緒に入る。AN2)
    private void walkFbx(string assetPath, string file)
    {
        GameEngine.Fbx.FbxScene scene;
        try { scene = new GameEngine.Fbx.FbxScene(GameEngine.Fbx.FbxDocument.Load(file)); }
        catch (GameEngine.Fbx.FbxFormatException ex) { Warnings.Add($"{assetPath} : {ex.Message}"); return; }
        foreach (var (name, texture, embedded) in GameEngine.Fbx.FbxModelImporter.TextureReferences(scene, file,
                     n => _database.Entries.FirstOrDefault(e => e.Kind == AssetKind.Texture && e.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) is { } entry ? _database.FullPath(entry.Path) : null))
        {
            if (embedded) continue;
            if (texture != null && _database.ToAssetPath(texture) is { } texturePath) add(texturePath, assetPath);
            else Warnings.Add($"{assetPath} : texture {name} not found");
        }
    }

    // .obj → .mtl → テクスチャ (パスはモデルのフォルダから)
    private void walkObj(string assetPath, string file)
    {
        var folder = Path.GetDirectoryName(file)!;
        foreach (var line in File.ReadLines(file))
        {
            if (!line.StartsWith("mtllib ")) continue;
            var mtl = Path.Combine(folder, line["mtllib ".Length..].Trim());
            if (!File.Exists(mtl)) { Warnings.Add($"{assetPath} : missing {Path.GetFileName(mtl)}"); continue; }
            addFile(mtl, assetPath);
            foreach (var mtlLine in File.ReadLines(mtl))
            {
                if (!mtlLine.StartsWith("map_Kd ")) continue;
                addTexture(Path.Combine(folder, mtlLine["map_Kd ".Length..].Trim()), assetPath);
            }
        }
    }

    // .pmx : ヘッダー → 名前 4 つ → 頂点 → 面 → テクスチャの表 (C++ の PmxLoader と同じ順に読み飛ばす)
    private void walkPmx(string assetPath, string file)
    {
        using var reader = new BinaryReader(File.OpenRead(file));
        if (new string(reader.ReadChars(4)) != "PMX ") return;
        reader.ReadSingle();   // 版
        var globals = reader.ReadBytes(reader.ReadByte());
        var utf8 = globals[0] == 1;
        int additionalUv = globals[1], vertexIndex = globals[2], boneIndex = globals[5];
        string text() { var length = reader.ReadInt32(); var bytes = reader.ReadBytes(length); return utf8 ? Encoding.UTF8.GetString(bytes) : Encoding.Unicode.GetString(bytes); }
        void skip(long bytes) => reader.BaseStream.Seek(bytes, SeekOrigin.Current);

        for (int i = 0; i < 4; i++) text();
        var vertexCount = reader.ReadInt32();
        for (int i = 0; i < vertexCount; i++)
        {
            skip(12 + 12 + 8 + additionalUv * 16);   // 位置 / 法線 / UV / 追加 UV
            var deform = reader.ReadByte();
            skip(deform switch
            {
                0 => boneIndex,
                1 => boneIndex * 2 + 4,
                2 or 4 => boneIndex * 4 + 16,
                3 => boneIndex * 2 + 4 + 36,
                _ => throw new IOException($"unknown weight type {deform}"),
            });
            skip(4);   // エッジの倍率
        }
        skip((long)reader.ReadInt32() * vertexIndex);   // 面
        var folder = Path.GetDirectoryName(file)!;
        var textureCount = reader.ReadInt32();
        for (int i = 0; i < textureCount; i++) addTexture(Path.Combine(folder, text().Replace('\\', Path.DirectorySeparatorChar)), assetPath);
    }

    // テクスチャ : 無ければ同じ名前で拡張子の違う物 (C++ の resolveTexturePath と同じ順)
    private static readonly string[] TextureExtensions = { ".png", ".jpg", ".jpeg", ".tga", ".bmp", ".dds" };
    private void addTexture(string file, string from)
    {
        if (!File.Exists(file))
        {
            var found = TextureExtensions.Select(e => Path.ChangeExtension(file, e)).FirstOrDefault(File.Exists);
            if (found == null) { Warnings.Add($"{from} : missing texture {Path.GetFileName(file)}"); return; }
            file = found;
        }
        addFile(file, from);
    }

    private void addFile(string file, string from)
    {
        if (_database.ToAssetPath(Path.GetFullPath(file)) is { } path) add(path, from);
        else Warnings.Add($"{from} : {file} is outside Assets (not included)");
    }

    // テクスチャの DDS のキャッシュ (C++ が作る 名前.拡張子.v1.dds)。アセットではないので、ここで覚えておくだけ
    private readonly HashSet<string> _caches = new(StringComparer.OrdinalIgnoreCase);
    public IReadOnlyCollection<string> CacheFiles => _caches;
    private void addTextureCache(string file)
    {
        var folder = Path.GetDirectoryName(file)!;
        foreach (var cache in Directory.EnumerateFiles(folder, Path.GetFileName(file) + ".v*.dds")) _caches.Add(cache);
    }
}
