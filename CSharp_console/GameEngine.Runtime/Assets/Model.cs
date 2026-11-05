using System.Numerics;
using System.Text.Json;
using GameEngine.Native;

namespace GameEngine;

// ==========================================================================
// 読み込んだモデル (メッシュ + テクスチャ。C++ 側にある実体を指すハンドル)
//   MeshRenderer に入れて使う。同じ Model を何個置いても、頂点やテクスチャは共有される
// ==========================================================================
public readonly struct Model
{
    internal readonly uint Handle;
    public readonly int    SubMeshCount;
    public readonly string Path;

    internal Model(uint handle, int subMeshCount, string path)
    {
        Handle       = handle;
        SubMeshCount = subMeshCount;
        Path         = path;
    }

    public bool IsValid => Path != null && Handle != NativeMethods.InvalidHandle;
    public override string ToString() => $"Model({Path}, {SubMeshCount} sub meshes)";
}

// ==========================================================================
// アセットの読み込み (以前の Test_LoadModel に当たる)
//   同じパスは 1 回だけ読み込み、2 回目からは同じ Model / Sound を返す
// ==========================================================================
public sealed class AssetManager
{
    private readonly Dictionary<string, Model> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Sound> _sounds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, CollisionMesh> _collisionMeshes = new();
    private readonly Dictionary<string, Material> _materials = new(StringComparer.OrdinalIgnoreCase);   // ED5
    private readonly Dictionary<string, Prefab>   _prefabs   = new(StringComparer.OrdinalIgnoreCase);   // ED8
    private readonly Dictionary<string, ShaderAsset> _shaders = new(StringComparer.OrdinalIgnoreCase);   // SH2
    private readonly Dictionary<string, uint>     _textures3D = new(StringComparer.OrdinalIgnoreCase);   // SH2 : builtin: の 3D テクスチャ

    // Physics.dll を読み込めたか (Engine が設定する。無い時は LoadCollisionMesh が例外を出す)
    internal bool PhysicsLoaded { get; set; }

    // Audio.dll を読み込めたか (Engine が設定する。無い時は LoadSound が例外を出す)
    internal bool AudioLoaded { get; set; }

    internal AssetManager()
    {
    }

    // プロジェクトのアセットの一覧 (ED5。エディターが設定する)。あれば "Assets/..." のパスで読め、
    // 以前の見本のパス (../Model/...) も Assets の同じファイルに読み替える (シーンを保存し直すと新しいパスになる)
    public AssetDatabase? Database
    {
        get => _database;
        set
        {
            if (_database != null) _database.Renamed -= onRenamed;
            _database = value;
            AssetDatabase.Current = value;
            if (value != null) value.Renamed += onRenamed;
        }
    }
    private AssetDatabase? _database;

    // アセットの名前を変えた時 (ED10) : 読み込んだ物の覚え方を新しいパスに付け替える (同じ物をもう一度読まないように)
    //   Material / Prefab のパスも変える (保存する時に前のパスへ書かないように)。Model / Sound は struct なので、
    //   コンポーネントの中は前のパスのまま (AssetDatabase が前のパスを今のパスに読み替える)
    private void onRenamed(string oldPath, string newPath)
    {
        string? map(string key) =>
            key.Equals(oldPath, StringComparison.OrdinalIgnoreCase) ? newPath :
            key.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase) ? newPath + key[oldPath.Length..] : null;
        void rekey<T>(Dictionary<string, T> cache, Action<T, string>? update = null)
        {
            foreach (var key in cache.Keys.ToList())
            {
                if (map(key) is not { } moved) continue;
                var value = cache[key];
                cache.Remove(key);
                cache[moved] = value;
                update?.Invoke(value, moved);
            }
        }
        rekey(_models);
        rekey(_sounds);
        rekey(_materials, (material, path) => material.Path = path);
        rekey(_prefabs, (prefab, path) => prefab.Path = path);
        rekey(_shaders, (shader, path) => shader.Path = path);   // SH2 (#line のファイル名は次のコンパイルから)
    }

    // パス → (覚える名前, 読むファイル)
    private (string Key, string File) resolve(string path)
    {
        if (_database?.ToAssetPath(path) is { } assetPath) return (assetPath, _database.FullPath(assetPath));
        return (path, path);
    }

    /// <summary>
    /// サウンド (wav) を読み込む。パスは実行ファイルのフォルダからの相対パスか絶対パス
    /// 音の出る機器が無い環境でも読み込みはできる (鳴らないだけ)
    /// </summary>
    public Sound LoadSound(string path)
    {
        (path, var file) = resolve(path);
        if (_sounds.TryGetValue(path, out var cached)) return cached;
        if (!AudioLoaded) throw new InvalidOperationException("Audio.dll is not loaded. Build the Audio project (x64) first.");

        var handle = AudioNativeMethods.Audio_LoadSound(file, out var length);
        if (handle == AudioNativeMethods.InvalidHandle)
        {
            throw new FileNotFoundException($"Failed to load sound: {path}", path);
        }

        var sound = new Sound(handle, length, path);
        _sounds.Add(path, sound);
        return sound;
    }

    /// <summary>
    /// モデルの形 (位置 xyz の並びと三角形のインデックス) を写す (エディターのクリックで選ぶ時の当たり。ED4)
    /// 毎回 C++ から写すので、使う側で覚えておくこと。形が無ければ false
    /// </summary>
    public unsafe bool GetModelGeometry(Model model, out float[] positions, out uint[] indices)
    {
        positions = Array.Empty<float>();
        indices   = Array.Empty<uint>();
        if (!model.IsValid || !NativeMethods.Renderer_GetModelGeometry(model.Handle, null, 0, null, 0, out var vertexCount, out var indexCount) || indexCount < 3) return false;
        positions = new float[vertexCount * 3];
        indices   = new uint[indexCount];
        fixed (float* p = positions)
        fixed (uint* i = indices)
        {
            NativeMethods.Renderer_GetModelGeometry(model.Handle, p, vertexCount, i, indexCount, out _, out _);
        }
        return true;
    }

    /// <summary>
    /// モデルの形から衝突判定のメッシュを作る (地面や岩を形どおりに当てる。動かない物だけ)
    /// 同じモデルは 1 回だけ作る。三角形が多いモデル (キャラクターなど) は重いので、カプセルなどを使うこと
    /// </summary>
    public unsafe CollisionMesh LoadCollisionMesh(Model model)
    {
        if (_collisionMeshes.TryGetValue(model.Handle, out var cached)) return cached;
        if (!PhysicsLoaded) throw new InvalidOperationException("Physics.dll is not loaded. Build the Physics project (x64) first.");

        if (!NativeMethods.Renderer_GetModelGeometry(model.Handle, null, 0, null, 0, out var vertexCount, out var indexCount) || indexCount < 3)
        {
            throw new InvalidOperationException($"The model has no geometry: {model.Path}");
        }
        var positions = new float[vertexCount * 3];
        var indices   = new uint[indexCount];
        uint mesh;
        fixed (float* p = positions)
        fixed (uint* i = indices)
        {
            NativeMethods.Renderer_GetModelGeometry(model.Handle, p, vertexCount, i, indexCount, out _, out _);
            mesh = PhysicsNativeMethods.Physics_CreateMesh(p, vertexCount, i, indexCount);
        }
        if (mesh == PhysicsNativeMethods.InvalidHandle) throw new InvalidOperationException($"Failed to create a collision mesh: {model.Path}");

        var result = new CollisionMesh(mesh, (int)vertexCount, (int)(indexCount / 3), model.Path);
        _collisionMeshes.Add(model.Handle, result);
        return result;
    }

    // =====================================================================
    // ED5 : マテリアル / 読み直し / 解放
    // =====================================================================

    // アセットを読み直した時 (アセットのパス)。エディターがクリックの当たりの形などを捨てるのに使う
    public event Action<string>? AssetReloaded;
    public int ReloadCount { get; private set; }

    public IEnumerable<Model>    LoadedModels    => _models.Values;
    public IEnumerable<Material> LoadedMaterials => _materials.Values;

    /// <summary>マテリアルのアセット (.material.json) を読み込む。同じパスは同じ Material を返す</summary>
    public Material LoadMaterial(string path)
    {
        (path, var file) = resolve(path);
        if (_materials.TryGetValue(path, out var cached)) return cached;
        var material = new Material(path, this);
        try
        {
            material.FromJson(File.ReadAllText(file, System.Text.Encoding.UTF8));
        }
        catch (Exception ex) when (ex is IOException or JsonException or InvalidDataException or UnauthorizedAccessException)
        {
            material.Dispose();
            throw new FileNotFoundException($"Failed to load material: {path} ({ex.Message})", path, ex);
        }
        material.Apply();
        _materials.Add(path, material);
        return material;
    }

    /// <summary>プレハブ (.prefab.json) を読み込む (ED8)。同じパスは同じ Prefab を返す</summary>
    public Prefab LoadPrefab(string path)
    {
        (path, var file) = resolve(path);
        if (_prefabs.TryGetValue(path, out var cached)) return cached;
        if (!File.Exists(file)) throw new FileNotFoundException($"Failed to load prefab: {path}", path);
        var prefab = new Prefab(path, _database?.Find(path)?.Guid ?? Guid.Empty, File.ReadAllText(file, System.Text.Encoding.UTF8), this);
        _prefabs.Add(path, prefab);
        return prefab;
    }

    /// <summary>Id からプレハブを探して読む (無ければ null)</summary>
    public Prefab? LoadPrefab(Guid assetId) =>
        _database?.Find(assetId) is { Kind: AssetKind.Prefab } entry ? LoadPrefab(entry.Path) : null;

    /// <summary>プレハブのファイルが (外で) 変わった時 : 読み直す (置いた物を作り直すのはエディター)</summary>
    public Prefab? ReloadPrefab(string path)
    {
        (path, var file) = resolve(path);
        if (!_prefabs.TryGetValue(path, out var prefab)) return null;
        prefab.SetJson(File.ReadAllText(file, System.Text.Encoding.UTF8));
        ReloadCount++;
        AssetReloaded?.Invoke(path);
        return prefab;
    }

    /// <summary>新しいマテリアルのファイルを作る (folder はアセットのパス)。戻り値 = 読み込んだ Material</summary>
    public Material CreateMaterialAsset(string folder, string name = "New Material")
    {
        if (_database == null) throw new InvalidOperationException("No asset database.");
        var path = $"{folder}/{name}.material.json";
        for (int i = 1; File.Exists(_database.FullPath(path)); i++) path = $"{folder}/{name} {i}.material.json";
        var material = new Material(path, this);
        NoteOwnWrite(_database.FullPath(path));
        File.WriteAllText(_database.FullPath(path), material.ToJson(), new System.Text.UTF8Encoding(false));
        material.Dispose();   // 書くためだけに作った物 (読み込みは LoadMaterial で)
        _database.Refresh();
        return LoadMaterial(path);
    }

    /// <summary>モデルのファイルが変わった時 : 同じ番号のまま読み直す (置いてある物も新しい形で描かれる)。読めなければ前のまま</summary>
    public bool ReloadModel(string path)
    {
        (path, _) = resolve(path);
        if (!_models.TryGetValue(path, out var model)) return false;
        if (!NativeMethods.Renderer_ReloadModel(model.Handle, out var subMeshCount)) return false;
        _models[path] = new Model(model.Handle, (int)subMeshCount, path);
        ReloadCount++;
        AssetReloaded?.Invoke(path);
        return true;
    }

    /// <summary>テクスチャのファイルが変わった時 : 使っているモデルとマテリアルを読み直す。戻り値 = 読み直した数</summary>
    public int ReloadTexture(string path)
    {
        (path, var file) = resolve(path);
        var count = (int)NativeMethods.Renderer_ReloadModelsUsingTexture(Path.GetFullPath(file));
        foreach (var material in _materials.Values)
        {
            if (material.Texture == null || !string.Equals(resolve(material.Texture).Key, path, StringComparison.OrdinalIgnoreCase)) continue;
            material.ReloadTexture();
            count++;
        }
        if (count > 0) { ReloadCount++; AssetReloaded?.Invoke(path); }
        return count;
    }

    /// <summary>マテリアルのファイルが (外で) 変わった時 : 値を読み直して送る</summary>
    public bool ReloadMaterial(string path)
    {
        (path, var file) = resolve(path);
        if (!_materials.TryGetValue(path, out var material)) return false;
        material.FromJson(File.ReadAllText(file, System.Text.Encoding.UTF8));
        material.Apply();
        ReloadCount++;
        AssetReloaded?.Invoke(path);
        return true;
    }

    /// <summary>
    /// どの MeshRenderer も使っていないモデル / マテリアルを解放する (GPU の頂点 / テクスチャ / マテリアルを返す)
    /// 次に LoadModel / LoadMaterial した時はファイルから読み直す。戻り値 = (モデルの数, マテリアルの数)
    /// </summary>
    public (int Models, int Materials) UnloadUnused(World world)
    {
        var usedModels = new HashSet<uint>();
        var usedMaterials = new HashSet<Material>();
        foreach (ref var renderer in world.GetPool<MeshRenderer>().Components)
        {
            if (renderer.Model.IsValid) usedModels.Add(renderer.Model.Handle);
            if (renderer.Material != null) usedMaterials.Add(renderer.Material);
        }
        var models = 0;
        foreach (var (path, model) in _models.ToList())
        {
            if (usedModels.Contains(model.Handle) || !NativeMethods.Renderer_UnloadModel(model.Handle)) continue;
            _models.Remove(path);
            models++;
        }
        var materials = 0;
        foreach (var (path, material) in _materials.ToList())
        {
            if (usedMaterials.Contains(material)) continue;
            material.Dispose();
            _materials.Remove(path);
            materials++;
        }
        return (models, materials);
    }

    // 自分で書いたファイル (エディターの見張りが「外で変わった」と思って読み直さないように)
    private readonly Dictionary<string, DateTime> _ownWrites = new(StringComparer.OrdinalIgnoreCase);
    public void NoteOwnWrite(string file) => _ownWrites[Path.GetFullPath(file)] = DateTime.UtcNow;
    public bool IsOwnWrite(string file) =>
        _ownWrites.TryGetValue(Path.GetFullPath(file), out var time) && (DateTime.UtcNow - time).TotalSeconds < 2.0;

    /// <summary>
    /// モデル (今は OBJ) を読み込む。パスは実行ファイルのフォルダからの相対パスか絶対パス
    /// GPU への転送は次のフレームの描画の前にまとめて行われる (ここでは待たない)
    /// </summary>
    public Model LoadModel(string path)
    {
        // プリミティブ (SH1) : "builtin:Cube" など。ファイルを読まずにコードで作る
        if (Primitives.IsPrimitive(path)) return loadPrimitive(path);
        (path, var file) = resolve(path);
        if (_models.TryGetValue(path, out var cached)) return cached;

        var handle = NativeMethods.Renderer_LoadModel(file, out var subMeshCount);
        if (handle == NativeMethods.InvalidHandle)
        {
            throw new FileNotFoundException($"Failed to load model: {path}", path);
        }

        var model = new Model(handle, (int)subMeshCount, path);
        _models.Add(path, model);
        return model;
    }

    // ---------------------------------------------------------------------
    // 自分のシェーダー / 3D テクスチャ (SH2)
    // ---------------------------------------------------------------------

    /// <summary>
    /// シェーダー (.hlsl) を読んでコンパイルする。同じパスは同じ ShaderAsset を返す
    /// コンパイルできなくても ShaderAsset は返す (Error に理由。IsValid = false。マテリアルは共通のシェーダーで描く)
    /// </summary>
    public ShaderAsset LoadShader(string path)
    {
        (path, var file) = resolve(path);
        if (_shaders.TryGetValue(path, out var cached)) return cached;
        if (!File.Exists(file)) throw new FileNotFoundException($"Failed to load shader: {path}", path);
        var shader = new ShaderAsset(path);
        shader.Compile(File.ReadAllText(file, System.Text.Encoding.UTF8));
        _shaders.Add(path, shader);
        return shader;
    }

    /// <summary>シェーダーのファイルが変わった時 : コンパイルし直し、使っているマテリアルが値を送り直す。失敗したら前の物のまま</summary>
    public bool ReloadShader(string path)
    {
        (path, var file) = resolve(path);
        if (!_shaders.TryGetValue(path, out var shader)) return false;
        var ok = shader.Compile(File.ReadAllText(file, System.Text.Encoding.UTF8));
        foreach (var material in _materials.Values.Where(m => m.ShaderAsset == shader)) material.Apply();
        ReloadCount++;
        AssetReloaded?.Invoke(path);
        if (ok) Console.WriteLine($"[Shader] Reloaded {path}");
        return ok;
    }

    /// <summary>3D テクスチャ (今は "builtin:ValueNoise3D[:大きさ]" だけ)。戻り値 = C++ のテクスチャの番号 (読めなければ 0 = 白)</summary>
    public unsafe uint LoadTexture3D(string? path)
    {
        if (string.IsNullOrEmpty(path)) return 0;
        if (_textures3D.TryGetValue(path, out var cached)) return cached;
        uint handle = 0;
        if (Noise3D.TryParse(path, out var kind, out var size))
        {
            var start = System.Diagnostics.Stopwatch.GetTimestamp();
            var data = Noise3D.Generate(kind, size);
            fixed (byte* d = data) handle = NativeMethods.Renderer_CreateTexture3D((uint)size, (uint)size, (uint)size, 1, d);
            Console.WriteLine($"[Texture3D] {path} : {size}^3 ({System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds:F0} ms)");
        }
        else Console.WriteLine($"[Texture3D] Unknown 3D texture : {path} (builtin:{string.Join(" / builtin:", Noise3D.Kinds)})");
        _textures3D[path] = handle;
        return handle;
    }

    /// <summary>プリミティブのモデル (SH1)。同じ形と分割数は同じ Model を返す</summary>
    public Model LoadPrimitive(PrimitiveShape shape, int segments = 0) => loadPrimitive(Primitives.PathOf(shape, segments));

    private unsafe Model loadPrimitive(string path)
    {
        if (_models.TryGetValue(path, out var cached)) return cached;
        if (!Primitives.TryParse(path, out var shape, out var segments)) throw new ArgumentException($"Unknown primitive : {path} (builtin:Cube / Sphere / Capsule / Cylinder / Cone / Torus / Plane / Quad[:segments])");
        var mesh = Primitives.Build(shape, segments);
        var vertices = new Primitives.NativeVertex[mesh.Positions.Count];
        for (int i = 0; i < vertices.Length; i++)
            vertices[i] = new Primitives.NativeVertex { Position = mesh.Positions[i], Normal = mesh.Normals[i], Color = Vector4.One, UV = mesh.UVs[i] };
        var indices = mesh.Indices.ToArray();
        uint handle;
        fixed (Primitives.NativeVertex* v = vertices)
        fixed (uint* i = indices)
            handle = NativeMethods.Renderer_CreateMesh(v, (uint)vertices.Length, i, (uint)indices.Length, path);
        if (handle == NativeMethods.InvalidHandle) throw new InvalidOperationException($"Failed to create the primitive : {path}");
        var model = new Model(handle, 1, path);
        _models.Add(path, model);
        return model;
    }
}
