namespace GameEngine;

// ==========================================================================
// SceneReference (v1.20 GO3)。シーンのアセット (.scene.json) への参照。Inspector で Project からシーンを落として選ぶ
//
//   public struct Door : IComponent { public SceneReference Next; }
//   if (door.Next) Scenes.LoadScene(door.Next);   // LoadScene / LoadSceneAsync にそのまま渡せる
//
//   - アセットの Id (.meta の guid) も持つので、シーンのファイルの名前を変えても / 動かしても辿れる (Path は今のパスを引き直す)
//   - 保存は { "guid": ..., "path": ... } (他のアセットと同じ)。書き出したゲームでも、Build Game がこのシーンのファイルを入れる
// ==========================================================================
public readonly struct SceneReference : IEquatable<SceneReference>
{
    private readonly string? _path;
    private readonly Guid _assetId;

    /// <summary>パス ("Assets/Scenes/stage2.scene.json") から作る (アセットの Id は今の AssetDatabase から引く)</summary>
    public SceneReference(string path) : this(path, AssetDatabase.Current?.Find(path)?.Guid ?? Guid.Empty) { }

    public SceneReference(string? path, Guid assetId)
    {
        _path = string.IsNullOrEmpty(path) ? null : path;
        _assetId = assetId;
    }

    /// <summary>今のパス (Id で引けたら名前を変えた後のパス。無ければ null)</summary>
    public string? Path => _assetId != Guid.Empty && AssetDatabase.Current?.Find(_assetId) is { } entry ? entry.Path : _path;

    /// <summary>アセットの Id (無ければ Guid.Empty)</summary>
    public Guid AssetId => _assetId;

    /// <summary>シーンの名前 (.scene.json を除いたファイル名)</summary>
    public string Name => Path is { } path ? SceneManager.NameOf(path) : "";

    public bool IsValid => !string.IsNullOrEmpty(Path);
    public static implicit operator bool(SceneReference scene) => scene.IsValid;

    public bool Equals(SceneReference other) =>
        _assetId != Guid.Empty || other._assetId != Guid.Empty ? _assetId == other._assetId : string.Equals(_path, other._path, StringComparison.OrdinalIgnoreCase);
    public override bool Equals(object? obj) => obj is SceneReference other && Equals(other);
    public override int GetHashCode() => _assetId != Guid.Empty ? _assetId.GetHashCode() : StringComparer.OrdinalIgnoreCase.GetHashCode(_path ?? "");
    public static bool operator ==(SceneReference a, SceneReference b) => a.Equals(b);
    public static bool operator !=(SceneReference a, SceneReference b) => !a.Equals(b);
    public override string ToString() => IsValid ? $"SceneReference({Path})" : "SceneReference(None)";
}
