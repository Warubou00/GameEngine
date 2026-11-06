namespace GameEngine;

// ==========================================================================
// 骨で曲げるメッシュ (AN4。GPU スキニング)。MeshRenderer と一緒に付ける
//   MeshRenderer のモデルが スキンのある部品 (xxx.mod.fbx#ノード) なら、Root の子孫の骨のエンティティ (名前で探す) の動きで曲げる
//   Root が無ければ、親をたどって最初の Animator の物
//   曲げるのは GPU (コンピュートシェーダー)。1 フレームに 1 回、姿勢が変わった時だけ。影 / 深度 / 自分のシェーダーもその形を使う
//   AnimationRig.Build (.skl.fbx をシーンに落とす) が、スキンのある部品に付ける
// ==========================================================================
[ComponentInfo(DisplayName = "Skinned Mesh", Category = "Animation")]
public struct SkinnedMesh : IComponent
{
    [Tooltip("The object whose children are the bones (empty = the nearest parent with an Animator)")]
    public Entity Root;

    // SkinningSystem が覚える物 (保存しない)
    internal Entity[]? Bones;
    internal ModelSkin? BoundSkin;
    internal int BoundVersion;
    internal Entity BoundRoot;
    internal uint SkinnedHandle;   // C++ に「曲げる」と伝えた描画オブジェクト + 1 (0 = まだ)

    /// <summary>見つけた骨の数 (SkinningSystem が探した後)</summary>
    public readonly int BoundBoneCount => Bones?.Count(b => b != Entity.Null) ?? 0;
}
