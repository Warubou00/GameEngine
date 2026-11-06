namespace GameEngine;

// ==========================================================================
// アニメーション (AN3)。Skeleton (xxx.skl.fbx) の骨の名前と同じ名前の子 (孫も) のエンティティを、Clip (xxx.anm.fbx) で動かす
//   骨のエンティティは AnimationRig.Build (エディターでは .skl.fbx をシーンに落とす) が作る。
//   その骨の子に部品のメッシュ (xxx.mod.fbx#ノード) を付けると、部品が骨と一緒に動く (階層型のアニメーション)
//   時間 (Time) は再生中だけ進む (Speed 倍。Loop なら最初に戻る)。編集中は Time の姿勢を見せる (Inspector で動かせる)
//   動かすのは Clip に曲線がある骨だけ (他の骨は置いた時の値のまま)
// ==========================================================================
[ComponentInfo(DisplayName = "Animator", Category = "Animation")]
public struct Animator : IComponent
{
    public Skeleton? Skeleton;
    public AnimationClip? Clip;
    [Range(0, 4), Tooltip("Playback speed (1 = normal)")]
    public float Speed;
    [Tooltip("Go back to the start at the end")]
    public bool Loop;
    [Tooltip("Advance the time while the game is playing")]
    public bool Playing;
    [Tooltip("Current time in seconds")]
    public float Time;
    [Tooltip("Draw the bones as lines")]
    public bool ShowBones;

    // AnimationSystem が覚える物 (保存しない)
    internal Entity[]? Bones;            // 骨の番号 → エンティティ (無ければ Entity.Null)
    internal int[]? Tracks;              // 骨の番号 → Clip の列の番号 (無ければ -1)
    internal int BoundSkeletonVersion;
    internal int BoundClipVersion;
    internal AnimationClip? BoundClip;
    internal Skeleton? BoundSkeleton;

    public Animator()
    {
        Speed = 1.0f;
        Loop = true;
        Playing = true;
    }

    public Animator(Skeleton? skeleton, AnimationClip? clip) : this()
    {
        Skeleton = skeleton;
        Clip = clip;
    }

    /// <summary>見つけた骨の数 (AnimationSystem が探した後)</summary>
    public readonly int BoundBoneCount => Bones?.Count(b => b != Entity.Null) ?? 0;

    /// <summary>Clip の列がある骨の数 (動かす骨の数)</summary>
    public readonly int AnimatedBoneCount => Tracks?.Count(t => t >= 0) ?? 0;
}
