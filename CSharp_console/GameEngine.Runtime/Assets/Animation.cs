using System.Numerics;
using GameEngine.Fbx;

namespace GameEngine;

// ==========================================================================
// スケルトンとアニメーションのアセット (AN3)。xxx.skl.fbx / xxx.anm.fbx を AssetManager が読んで作る
//   読み直すと同じ物の中身が入れ替わる (コンポーネントが持っている物もそのまま新しくなる。Version が増える)
//   空間はエンジン (左手系 / +Y 上 / m)。骨の値は親から見た値 (ローカル)
// ==========================================================================
public sealed record SkeletonBone(string Name, int Parent, bool IsBone, Vector3 Position, Quaternion Rotation, Vector3 Scale, Matrix4x4 BindGlobal);

public sealed class Skeleton
{
    public string Path { get; internal set; }
    public IReadOnlyList<SkeletonBone> Bones { get; private set; } = Array.Empty<SkeletonBone>();
    /// <summary>読み直した回数 (Animator が骨を探し直す印)</summary>
    public int Version { get; private set; }
    private Dictionary<string, int> _index = new(StringComparer.Ordinal);

    internal Skeleton(string path) => Path = path;

    internal void Set(List<FbxSkeletonBone> bones)
    {
        Bones = bones.Select(b => new SkeletonBone(b.Name, b.Parent, b.IsBone, b.Position, b.Rotation, b.Scale, b.BindGlobal)).ToList();
        _index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < Bones.Count; i++) _index.TryAdd(Bones[i].Name, i);
        Version++;
    }

    /// <summary>名前の骨の番号 (無ければ -1)</summary>
    public int IndexOf(string name) => _index.TryGetValue(name, out var i) ? i : -1;

    public override string ToString() => $"Skeleton({Path}, {Bones.Count} bones)";
}

/// <summary>
/// 曲げるモデルの骨 (AN4。xxx.mod.fbx#ノード にスキンがある時)。骨の行列 k = InverseBind[k] × 骨の世界の行列 × メッシュの世界の逆行列
/// 骨の番号 BoneNames.Count は「動かない骨」(単位行列)
/// </summary>
public sealed class ModelSkin
{
    public IReadOnlyList<string> BoneNames { get; internal set; } = Array.Empty<string>();
    public IReadOnlyList<Matrix4x4> InverseBind { get; internal set; } = Array.Empty<Matrix4x4>();
    public IReadOnlyList<float> BoneRadius { get; internal set; } = Array.Empty<float>();
    public int Version { get; internal set; }
    public int MatrixCount => BoneNames.Count + 1;
    /// <summary>休みの姿勢の頂点 (メッシュの空間) と、頂点ごとの骨と重み (確認 / CPU で同じ計算をする用)</summary>
    public IReadOnlyList<Vector3> BindPositions { get; internal set; } = Array.Empty<Vector3>();
    public IReadOnlyList<(ushort, ushort, ushort, ushort)> BoneIndices { get; internal set; } = Array.Empty<(ushort, ushort, ushort, ushort)>();
    public IReadOnlyList<Vector4> BoneWeights { get; internal set; } = Array.Empty<Vector4>();
}

public sealed class AnimationTrack
{
    public string Name { get; internal set; } = "";
    public bool HasTranslation { get; internal set; }
    public bool HasRotation { get; internal set; }
    public bool HasScale { get; internal set; }
    internal Vector3[] Positions = Array.Empty<Vector3>();
    internal Quaternion[] Rotations = Array.Empty<Quaternion>();
    internal Vector3[] Scales = Array.Empty<Vector3>();
}

public sealed class AnimationClip
{
    public string Path { get; internal set; }
    public string Name { get; private set; } = "";
    /// <summary>長さ (秒)</summary>
    public float Duration { get; private set; }
    public float FrameRate { get; private set; } = 30;
    public int FrameCount { get; private set; }
    public IReadOnlyList<AnimationTrack> Tracks { get; private set; } = Array.Empty<AnimationTrack>();
    public int Version { get; private set; }
    private Dictionary<string, int> _index = new(StringComparer.Ordinal);

    internal AnimationClip(string path) => Path = path;

    internal void Set(FbxAnimationData data)
    {
        Name = data.Name;
        Duration = data.Duration;
        FrameRate = data.FrameRate;
        FrameCount = data.FrameCount;
        Tracks = data.Tracks.Select(t => new AnimationTrack
        {
            Name = t.Name, HasTranslation = t.HasTranslation, HasRotation = t.HasRotation, HasScale = t.HasScale,
            Positions = t.Positions, Rotations = t.Rotations, Scales = t.Scales,
        }).ToList();
        _index = new Dictionary<string, int>(StringComparer.Ordinal);
        for (int i = 0; i < Tracks.Count; i++) _index.TryAdd(Tracks[i].Name, i);
        Version++;
    }

    /// <summary>名前 (骨の名前) の列の番号 (無ければ -1)</summary>
    public int TrackIndex(string name) => _index.TryGetValue(name, out var i) ? i : -1;

    /// <summary>
    /// その時間 (秒。0 〜 Duration に収める) の値。刻みの間は 位置 / 大きさ は直線、回転は球面線形で補う
    /// </summary>
    public void Sample(int track, float time, out Vector3 position, out Quaternion rotation, out Vector3 scale)
    {
        var t = Tracks[track];
        var frame = Math.Clamp(time, 0, Duration) * FrameRate;
        var i = Math.Clamp((int)MathF.Floor(frame), 0, FrameCount - 1);
        var j = Math.Min(i + 1, FrameCount - 1);
        var a = Math.Clamp(frame - i, 0, 1);
        position = Vector3.Lerp(t.Positions[i], t.Positions[j], a);
        rotation = Quaternion.Slerp(t.Rotations[i], t.Rotations[j], a);
        scale = Vector3.Lerp(t.Scales[i], t.Scales[j], a);
    }

    public override string ToString() => $"AnimationClip({Path}, {Name}, {Duration:F2} s, {Tracks.Count} tracks)";
}
