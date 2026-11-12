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
public sealed partial class AssetManager
{
    private readonly Dictionary<string, Model> _models = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, Sound> _sounds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<uint, CollisionMesh> _collisionMeshes = new();
    private readonly Dictionary<string, Material> _materials = new(StringComparer.OrdinalIgnoreCase);   // ED5
    private readonly Dictionary<string, Prefab>   _prefabs   = new(StringComparer.OrdinalIgnoreCase);   // ED8
    private readonly Dictionary<string, ShaderAsset> _shaders = new(StringComparer.OrdinalIgnoreCase);   // SH2
    private readonly Dictionary<string, uint>     _textures3D = new(StringComparer.OrdinalIgnoreCase);   // SH2 : builtin: の 3D テクスチャ
    private readonly Dictionary<string, Fbx.FbxAsset> _fbx = new(StringComparer.OrdinalIgnoreCase);   // AN1
    private readonly Dictionary<string, Skeleton> _skeletons = new(StringComparer.OrdinalIgnoreCase);    // AN3
    private readonly Dictionary<string, ModelSkin> _modelSkins = new(StringComparer.OrdinalIgnoreCase);  // AN4 : 曲げるモデル (部品) の骨
    private readonly Dictionary<string, AnimationClip> _clips = new(StringComparer.OrdinalIgnoreCase);   // AN3

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
            key.StartsWith(oldPath + "/", StringComparison.OrdinalIgnoreCase) || key.StartsWith(oldPath + "#", StringComparison.Ordinal) ? newPath + key[oldPath.Length..] : null;   // AN3 : 部品 (xxx.mod.fbx#ノード) も
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
        rekey(_fbx, (fbx, path) => fbx.Path = path);   // AN1
        rekey(_skeletons, (skeleton, path) => skeleton.Path = path);   // AN3
        rekey(_modelSkins);   // AN4
        rekey(_clips, (clip, path) => clip.Path = path);
        rekey(_widgets, (widget, path) => widget.Path = path);   // v1.7
    }

    // パス → (覚える名前, 読むファイル)
    private (string Key, string File) resolve(string path)
    {
        if (_database?.ToAssetPath(path) is { } assetPath) return (assetPath, _database.FullPath(assetPath));
        return (path, path);
    }

    // ---------------------------------------------------------------------
    // FBX (AN1) : xxx.mod.fbx / xxx.skl.fbx / xxx.anm.fbx。読めなくても FbxAsset は返す (Error に理由)
    // ---------------------------------------------------------------------

    public Fbx.FbxAsset LoadFbx(string path)
    {
        (path, var file) = resolve(path);
        if (_fbx.TryGetValue(path, out var cached)) return cached;
        if (!File.Exists(file)) throw new FileNotFoundException($"Failed to load FBX: {path}", path);
        var asset = Fbx.FbxAsset.Load(path, file);
        _fbx.Add(path, asset);
        return asset;
    }

    /// <summary>FBX のファイルが変わった時 : 読み直す (読んだ事が無ければ何もしない)。戻り値 = 読めたか</summary>
    public bool ReloadFbx(string path)
    {
        (path, var file) = resolve(path);
        if (!_fbx.TryGetValue(path, out var asset)) return false;
        asset.Reload(file);
        // AN2 : モデルとして置いてあれば、同じ番号のまま作り直す (置いた物も新しい形で描かれる)。読めなければ前の形のまま
        //   AN3 : 部品 (xxx.mod.fbx#ノード) も
        if (asset.IsValid && asset.Type == Fbx.FbxAssetType.Model)
        {
            foreach (var key in _models.Keys.Where(k => k.Equals(path, StringComparison.OrdinalIgnoreCase) || k.StartsWith(path + "#", StringComparison.OrdinalIgnoreCase)).ToList())
            {
                try { _models[key] = createFbxModel(key, file, asset, _models[key].Handle); }
                catch (InvalidDataException ex) { Console.WriteLine($"[FBX] {key} : {ex.Message} (the previous model is kept)"); }
            }
        }
        // AN3 : スケルトン / アニメーションは中身を入れ替える (Animator が持っている物もそのまま新しくなる)
        if (asset.IsValid && asset.Scene != null && asset.Type == Fbx.FbxAssetType.Skeleton && _skeletons.TryGetValue(path, out var skeleton))
            skeleton.Set(Fbx.FbxAnimationImporter.ImportSkeleton(asset.Scene));
        if (asset.IsValid && asset.Scene != null && asset.Type == Fbx.FbxAssetType.Animation && _clips.TryGetValue(path, out var clip))
            clip.Set(Fbx.FbxAnimationImporter.ImportAnimation(asset.Scene));
        ReloadCount++;
        AssetReloaded?.Invoke(path);
        return asset.IsValid;
    }

    /// <summary>曲げるモデル (部品) の骨 (AN4。スキンが無ければ null)</summary>
    public ModelSkin? GetSkin(Model model) => model.IsValid && _modelSkins.TryGetValue(model.Path, out var skin) ? skin : null;

    // AN3 : スケルトン (xxx.skl.fbx)。読めない / 中身が足りない時は理由付きの例外
    public Skeleton LoadSkeleton(string path)
    {
        (path, var file) = resolve(path);
        if (_skeletons.TryGetValue(path, out var cached)) return cached;
        if (Fbx.FbxAsset.TypeOf(path) != Fbx.FbxAssetType.Skeleton) throw new InvalidDataException($"{path} : not a skeleton (name it xxx.skl.fbx)");
        if (!File.Exists(file)) throw new FileNotFoundException($"Failed to load skeleton: {path}", path);
        var asset = LoadFbx(path);
        if (!asset.IsValid || asset.Scene == null) throw new InvalidDataException($"{path} : {asset.Error}");
        var skeleton = new Skeleton(path);
        skeleton.Set(Fbx.FbxAnimationImporter.ImportSkeleton(asset.Scene));
        _skeletons.Add(path, skeleton);
        return skeleton;
    }

    // AN3 : アニメーション (xxx.anm.fbx)
    public AnimationClip LoadAnimationClip(string path)
    {
        (path, var file) = resolve(path);
        if (_clips.TryGetValue(path, out var cached)) return cached;
        if (Fbx.FbxAsset.TypeOf(path) != Fbx.FbxAssetType.Animation) throw new InvalidDataException($"{path} : not an animation (name it xxx.anm.fbx)");
        if (!File.Exists(file)) throw new FileNotFoundException($"Failed to load animation: {path}", path);
        var asset = LoadFbx(path);
        if (!asset.IsValid || asset.Scene == null) throw new InvalidDataException($"{path} : {asset.Error}");
        var data = Fbx.FbxAnimationImporter.ImportAnimation(asset.Scene);
        foreach (var warning in data.Warnings) Console.WriteLine($"[FBX] {path} : warning : {warning}");
        var clip = new AnimationClip(path);
        clip.Set(data);
        _clips.Add(path, clip);
        return clip;
    }

    // AN2 : .mod.fbx → 頂点 / インデックス / マテリアル → C++ のモデル (replace が有効なら同じ番号で入れ替える)
    //   AN3 : path が xxx.mod.fbx#ノード なら、そのノードのメッシュだけをノードの空間で (部品)
    private unsafe Model createFbxModel(string path, string file, Fbx.FbxAsset asset, uint replace)
    {
        if (!asset.IsValid || asset.Scene == null) throw new InvalidDataException($"{path} : {asset.Error}");
        if (asset.Type != Fbx.FbxAssetType.Model) throw new InvalidDataException($"{path} : not a model (name it xxx.mod.fbx)");
        var embedded = _database != null ? Path.Combine(_database.ProjectRoot, "Library", "FbxTextures") : null;
        var hash = path.IndexOf('#');
        var node = hash >= 0 ? path[(hash + 1)..] : null;
        var data = Fbx.FbxModelImporter.Import(asset.Scene, file, findTextureByName, embedded, node);
        foreach (var warning in data.Warnings) Console.WriteLine($"[FBX] {path} : warning : {warning}");
        if (data.SubMeshes.Count == 0) throw new InvalidDataException(node != null ? $"{path} : no mesh in the node '{node}'" : $"{path} : no triangles");
        var vertices = new Primitives.NativeVertex[data.Positions.Count];
        for (int i = 0; i < vertices.Length; i++)
            vertices[i] = new Primitives.NativeVertex { Position = data.Positions[i], Normal = data.Normals[i], Color = data.Colors[i], UV = data.UVs[i] };
        var indices = data.Indices.ToArray();
        var subMeshes = new NativeMethods.ModelSubMeshDesc[data.SubMeshes.Count];
        var textures = new string?[data.SubMeshes.Count];
        for (int s = 0; s < subMeshes.Length; s++)
        {
            var sub = data.SubMeshes[s];
            var material = data.Materials[sub.Material];
            subMeshes[s] = new NativeMethods.ModelSubMeshDesc
            {
                FirstIndex = (uint)sub.FirstIndex, IndexCount = (uint)sub.IndexCount, Color = material.Color,
                Metallic = material.Metallic, Roughness = material.Roughness, Flags = material.Transparent ? 1u : 0u,
            };
            textures[s] = material.TexturePath;
        }
        uint handle, subMeshCount;
        fixed (Primitives.NativeVertex* v = vertices)
        fixed (uint* i = indices)
        fixed (NativeMethods.ModelSubMeshDesc* s = subMeshes)
            handle = NativeMethods.Renderer_CreateModel(v, (uint)vertices.Length, i, (uint)indices.Length, s, (uint)subMeshes.Length, textures, path, replace, out subMeshCount);
        if (handle == NativeMethods.InvalidHandle) throw new InvalidDataException($"{path} : the renderer could not create the model");
        if (node == null) asset.ModelData = data;   // Inspector に出すのはモデル全体の形
        _subMeshNames[path] = data.SubMeshes.Select(sub => data.Materials[sub.Material].Name).ToArray();   // v1.5 : マテリアルのアセットの名前
        // AN4 : スキン (部品の時だけ) → C++ に骨の番号と重み、こちらに骨の名前と休みの姿勢の逆行列
        if (data.HasSkin)
        {
            var skinVertices = new NativeMethods.SkinVertex[data.Positions.Count];
            for (int v = 0; v < skinVertices.Length; v++)
            {
                var (b0, b1, b2, b3) = data.SkinIndices[v];
                var w = data.SkinWeights[v];
                uint q(float x) => (uint)Math.Clamp(MathF.Round(x * 65535.0f), 0, 65535);
                skinVertices[v] = new NativeMethods.SkinVertex
                {
                    Bones01 = b0 | (uint)b1 << 16, Bones23 = b2 | (uint)b3 << 16,
                    Weights01 = q(w.X) | q(w.Y) << 16, Weights23 = q(w.Z) | q(w.W) << 16,
                };
            }
            bool ok;
            fixed (NativeMethods.SkinVertex* s = skinVertices)
                ok = NativeMethods.Renderer_SetModelSkin(handle, s, (uint)skinVertices.Length, (uint)data.SkinBones.Count + 1);
            if (!ok) Console.WriteLine($"[FBX] {path} : could not set the skin (drawn in the bind pose)");
            if (!_modelSkins.TryGetValue(path, out var skin)) _modelSkins[path] = skin = new ModelSkin();
            skin.BoneNames = data.SkinBones.ToList();
            skin.InverseBind = data.SkinInverseBind.ToList();
            skin.BoneRadius = data.SkinBoneRadius.ToList();
            skin.BindPositions = data.Positions.ToList();
            skin.BoneIndices = data.SkinIndices.ToList();
            skin.BoneWeights = data.SkinWeights.ToList();
            skin.Version++;
        }
        else _modelSkins.Remove(path);
        return new Model(handle, (int)subMeshCount, path);
    }

    // FBX に書いてあるテクスチャの名前で Assets の中を探す (FBX の隣に無い時)
    private string? findTextureByName(string name)
    {
        if (_database == null || string.IsNullOrEmpty(name)) return null;
        var entry = _database.Entries.FirstOrDefault(e => e.Kind == AssetKind.Texture && e.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                 ?? _database.Entries.FirstOrDefault(e => e.Kind == AssetKind.Texture && Path.GetFileNameWithoutExtension(e.Name).Equals(Path.GetFileNameWithoutExtension(name), StringComparison.OrdinalIgnoreCase));
        return entry != null ? _database.FullPath(entry.Path) : null;
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

    // =====================================================================
    // v1.5 : 1 Mesh / 1 Material (モデルを読み込んだ時に、サブメッシュごとのマテリアルのアセットを作る)
    // =====================================================================

    private readonly Dictionary<string, string[]> _subMeshNames = new(StringComparer.OrdinalIgnoreCase);   // FBX のサブメッシュのマテリアルの名前

    /// <summary>読み込んだモデルのサブメッシュ s のマテリアル (ファイルに書いてあった色 / テクスチャなど)</summary>
    public readonly record struct ModelMaterial(string Name, Vector4 Color, float Metallic, float Roughness, bool Transparent, bool DoubleSided, string? Texture);

    /// <summary>モデルのサブメッシュごとのマテリアル (読み込んだ時の値)。テクスチャはアセットのパス (Assets の外ならフルパス)</summary>
    public unsafe IReadOnlyList<ModelMaterial> GetModelMaterials(Model model)
    {
        var result = new List<ModelMaterial>();
        if (!model.IsValid) return result;
        _subMeshNames.TryGetValue(model.Path ?? "", out var names);
        var texture = stackalloc char[1024];
        var name    = stackalloc char[256];
        for (int s = 0; s < model.SubMeshCount; s++)
        {
            if (!NativeMethods.Renderer_GetModelMaterialInfo(model.Handle, (uint)s, out var info, texture, 1024, name, 256)) break;
            var texturePath = new string(texture);
            var materialName = names != null && s < names.Length && !string.IsNullOrWhiteSpace(names[s]) ? names[s] : new string(name);
            result.Add(new ModelMaterial(materialName, info.Color, info.Metallic, info.Roughness, (info.Flags & 1) != 0, (info.Flags & 2) != 0,
                                         texturePath.Length == 0 ? null : _database?.ToAssetPath(texturePath) ?? texturePath));
        }
        return result;
    }

    /// <summary>モデルのマテリアルのアセットのフォルダ ("Assets/Models/Robot.obj" → "Assets/Models/Robot_Materials")</summary>
    public static string ModelMaterialFolder(string modelPath)
    {
        var hash = modelPath.IndexOf('#');
        if (hash >= 0) modelPath = modelPath[..hash];
        var slash = modelPath.LastIndexOf('/');
        var folder = slash >= 0 ? modelPath[..slash] : "Assets";
        var stem = AssetDatabase.SplitName(slash >= 0 ? modelPath[(slash + 1)..] : modelPath, false).Stem;
        return $"{folder}/{stem}_Materials";
    }

    /// <summary>
    /// v1.5 : モデルのサブメッシュごとのマテリアルのアセットを作る (Unity のモデルの Materials の取り出しと同じ)
    ///   &lt;モデルのフォルダ&gt;/&lt;モデル名&gt;_Materials/&lt;マテリアル名&gt;.material.json に、読み込んだ色 / テクスチャ / 金属 / 粗さ / 半透明 / 両面を書く
    ///   もうあるファイルは書き換えない (エディターで変えた値を残す)。同じ名前の材質は 1 つのアセットを使う
    ///   戻り値 = サブメッシュごとの Material (MeshRenderer.Materials に入れる)。アセットのデータベースが無い / プリミティブなら空
    /// </summary>
    public Material?[] CreateModelMaterialAssets(Model model)
    {
        if (_database == null || !model.IsValid || string.IsNullOrEmpty(model.Path) || Primitives.IsPrimitive(model.Path) ||
            !model.Path.StartsWith(AssetDatabase.AssetsFolder + "/", StringComparison.OrdinalIgnoreCase)) return [];
        var sources = GetModelMaterials(model);
        if (sources.Count == 0) return [];
        var folder = ModelMaterialFolder(model.Path);
        var paths = new string[sources.Count];
        var wrote = false;
        for (int s = 0; s < sources.Count; s++)
        {
            var source = sources[s];
            var name = safeFileName(source.Name);
            if (name.Length == 0) name = $"Material {s}";
            paths[s] = $"{folder}/{name}.material.json";
            var file = _database.FullPath(paths[s]);
            if (File.Exists(file)) continue;
            Directory.CreateDirectory(Path.GetDirectoryName(file)!);
            // Assets の外のテクスチャ (FBX に埋め込まれていて Library に出した物など) は、マテリアルの隣に写して使う
            // (Library は書き出したゲームに入らないので、そのままだとゲームでテクスチャが無くなる)
            var texture = source.Texture;
            if (texture != null && !texture.StartsWith(AssetDatabase.AssetsFolder + "/", StringComparison.OrdinalIgnoreCase))
            {
                var from = Path.IsPathRooted(texture) ? texture : _database.FullPath(texture);
                if (File.Exists(from))
                {
                    var copied = $"{folder}/{Path.GetFileName(from)}";
                    if (!File.Exists(_database.FullPath(copied))) { NoteOwnWrite(_database.FullPath(copied)); File.Copy(from, _database.FullPath(copied)); }
                    texture = copied;
                }
            }
            using var material = new Material(paths[s], null)
            {
                Color = source.Color, Texture = texture, Metallic = source.Metallic, Roughness = source.Roughness,
                Transparent = source.Transparent, DoubleSided = source.DoubleSided,
            };
            NoteOwnWrite(file);
            File.WriteAllText(file, material.ToJson(), new System.Text.UTF8Encoding(false));
            wrote = true;
        }
        if (wrote)
        {
            _database.Refresh();
            Console.WriteLine($"[Assets] Created materials for {model.Path} in {folder}");
        }
        var result = new Material?[paths.Length];
        for (int s = 0; s < paths.Length; s++)
        {
            try { result[s] = LoadMaterial(paths[s]); }
            catch (FileNotFoundException ex) { Console.WriteLine($"[Assets] {ex.Message}"); }
        }
        return result;
    }

    private static string safeFileName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var chars = name.Trim().Select(c => invalid.Contains(c) || c == '#' ? '_' : c).ToArray();
        return new string(chars).Trim().TrimEnd('.');
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
        // AN2 : FBX のモデルは C++ がファイルから読み直せないので、テクスチャを使っている物を作り直す
        var full = Path.GetFullPath(file);
        foreach (var (fbxPath, fbx) in _fbx.ToList())
        {
            if (fbx.ModelData == null || !fbx.ModelData.Materials.Any(m => m.TexturePath != null && string.Equals(Path.GetFullPath(m.TexturePath), full, StringComparison.OrdinalIgnoreCase))) continue;
            if (ReloadFbx(fbxPath)) count++;
        }
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
            if (renderer.Materials != null) foreach (var m in renderer.Materials) if (m != null) usedMaterials.Add(m);   // v1.5 : サブメッシュごと
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
        // AN3 : 部品 (xxx.mod.fbx#ノード)。# の前をアセットのパスとして探す
        var fragment = "";
        var hashIndex = path.IndexOf('#');
        if (hashIndex >= 0) { fragment = path[hashIndex..]; path = path[..hashIndex]; }
        (path, var file) = resolve(path);
        var key = path + fragment;
        if (_models.TryGetValue(key, out var cached)) return cached;
        // AN2 : FBX のモデル (C# で読んで形を渡す)。読めない / 中身が足りない時は理由付きの例外
        if (Fbx.FbxAsset.TypeOf(path) == Fbx.FbxAssetType.Model)
        {
            if (!File.Exists(file)) throw new FileNotFoundException($"Failed to load model: {path}", path);
            var fbxModel = createFbxModel(key, file, LoadFbx(path), NativeMethods.InvalidHandle);
            _models.Add(key, fbxModel);
            return fbxModel;
        }
        if (fragment.Length > 0) throw new FileNotFoundException($"Failed to load model: {key} (# is only for FBX models)", key);

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
        shader.CompileFile(File.ReadAllText(file, System.Text.Encoding.UTF8));   // .hlsl / .shadergraph.json (SH4)
        _shaders.Add(path, shader);
        return shader;
    }

    /// <summary>シェーダーのファイルが変わった時 : コンパイルし直し、使っているマテリアルが値を送り直す。失敗したら前の物のまま</summary>
    public bool ReloadShader(string path)
    {
        (path, var file) = resolve(path);
        if (!_shaders.TryGetValue(path, out var shader)) return false;
        var text = File.ReadAllText(file, System.Text.Encoding.UTF8);
        if (text == shader.SourceText) return shader.Error == null;   // 中身が同じ (グラフの窓が保存してコンパイル済み : 2 回しない。SH4)
        var ok = shader.CompileFile(text);
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
            // キャッシュ : プロジェクトの Library/Noise3D (SH3。書き出したゲームは Data/Library/Noise3D)
            var cacheFolder = _database != null ? System.IO.Path.Combine(_database.ProjectRoot, "Library", "Noise3D") : null;
            var (data, generated) = Noise3D.Load(kind, size, cacheFolder);
            var channels = (uint)Noise3D.ChannelsOf(kind);
            fixed (byte* d = data) handle = NativeMethods.Renderer_CreateTexture3D((uint)size, (uint)size, (uint)size, channels, d);
            Console.WriteLine($"[Texture3D] {path} : {size}^3 x{channels} {(generated ? "generated" : "from cache")} ({System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds:F0} ms)");
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
