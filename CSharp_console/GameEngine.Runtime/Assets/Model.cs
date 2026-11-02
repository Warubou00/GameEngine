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

    // Physics.dll を読み込めたか (Engine が設定する。無い時は LoadCollisionMesh が例外を出す)
    internal bool PhysicsLoaded { get; set; }

    // Audio.dll を読み込めたか (Engine が設定する。無い時は LoadSound が例外を出す)
    internal bool AudioLoaded { get; set; }

    internal AssetManager()
    {
    }

    /// <summary>
    /// サウンド (wav) を読み込む。パスは実行ファイルのフォルダからの相対パスか絶対パス
    /// 音の出る機器が無い環境でも読み込みはできる (鳴らないだけ)
    /// </summary>
    public Sound LoadSound(string path)
    {
        if (_sounds.TryGetValue(path, out var cached)) return cached;
        if (!AudioLoaded) throw new InvalidOperationException("Audio.dll is not loaded. Build the Audio project (x64) first.");

        var handle = AudioNativeMethods.Audio_LoadSound(path, out var length);
        if (handle == AudioNativeMethods.InvalidHandle)
        {
            throw new FileNotFoundException($"Failed to load sound: {path}", path);
        }

        var sound = new Sound(handle, length, path);
        _sounds.Add(path, sound);
        return sound;
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

    /// <summary>
    /// モデル (今は OBJ) を読み込む。パスは実行ファイルのフォルダからの相対パスか絶対パス
    /// GPU への転送は次のフレームの描画の前にまとめて行われる (ここでは待たない)
    /// </summary>
    public Model LoadModel(string path)
    {
        if (_models.TryGetValue(path, out var cached)) return cached;

        var handle = NativeMethods.Renderer_LoadModel(path, out var subMeshCount);
        if (handle == NativeMethods.InvalidHandle)
        {
            throw new FileNotFoundException($"Failed to load model: {path}", path);
        }

        var model = new Model(handle, (int)subMeshCount, path);
        _models.Add(path, model);
        return model;
    }
}
