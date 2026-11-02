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
//   同じパスは 1 回だけ読み込み、2 回目からは同じ Model を返す
// ==========================================================================
public sealed class AssetManager
{
    private readonly Dictionary<string, Model> _models = new(StringComparer.OrdinalIgnoreCase);

    internal AssetManager()
    {
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
