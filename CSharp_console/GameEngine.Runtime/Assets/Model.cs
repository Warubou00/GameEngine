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
