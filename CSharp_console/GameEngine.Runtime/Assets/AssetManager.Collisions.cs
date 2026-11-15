namespace GameEngine;

// ==========================================================================
// コリジョンアセット (.collision.json) の読み込み (v1.9)
//   同じパスは同じ CollisionAsset を返す (使っている CompoundCollider は同じ物を見ている)
//   読み直すと中身を差し替えて Version が上がる → 使っている CompoundCollider は次のフレームで体を作り直す
// ==========================================================================
public sealed partial class AssetManager
{
    private readonly Dictionary<string, CollisionAsset> _collisionAssets = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>コリジョンアセットを読む。同じパスは同じ CollisionAsset を返す</summary>
    public CollisionAsset LoadCollisionAsset(string path)
    {
        (path, var file) = resolve(path);
        if (_collisionAssets.TryGetValue(path, out var cached)) return cached;
        if (!File.Exists(file)) throw new FileNotFoundException($"Failed to load collision asset: {path}", path);
        var asset = new CollisionAsset(path, File.ReadAllText(file, System.Text.Encoding.UTF8));
        _collisionAssets.Add(path, asset);
        return asset;
    }

    /// <summary>ファイルが変わった時 : 読み直す (同じ CollisionAsset の中身を差し替える)</summary>
    public bool ReloadCollisionAsset(string path)
    {
        (path, var file) = resolve(path);
        if (!_collisionAssets.TryGetValue(path, out var asset) || !File.Exists(file)) return false;
        asset.SetJson(File.ReadAllText(file, System.Text.Encoding.UTF8));
        AssetReloaded?.Invoke(path);
        return true;
    }

    /// <summary>読み込み済みなら返す (エディターが保存した物を差し替えるため)</summary>
    public CollisionAsset? FindLoadedCollisionAsset(string path)
    {
        (path, _) = resolve(path);
        return _collisionAssets.TryGetValue(path, out var asset) ? asset : null;
    }

    public IEnumerable<CollisionAsset> LoadedCollisionAssets => _collisionAssets.Values;
}
