using System.Numerics;

namespace GameEngine;

// ==========================================================================
// 複数のコリジョンアセットで当たる (v1.9。エンジンのコンポーネント)。Transform と一緒に付ける
//   Assets の全部のアセットの部品 (Box / Capsule / Sphere) が、このエンティティの当たり判定になる
//   - 同じエンティティの部品どうしは当たらない (同じエンティティの Collider とも当たらない)
//   - Rigidbody と一緒に付けると、全部の部品で 1 つの剛体になる (質量を部品の体積で分け、重心と慣性も部品から求める)
//     ※ Collider も付いている時は、剛体は Collider の方 (部品は Transform について動くだけ)
//   - Static も付いた物は動かない物として扱う
//   - OnCollisionEnter / Stay / Exit (OnTrigger* も) の Collision.SelfPart / OtherPart で、どのアセット (AssetIndex) の
//     どのコライダー (Id / Name) が当たったかが分かる。イベントは部品の組ごと
//   位置は持ち主の Transform を 1 回送るだけ (C++ が部品へ広げる)。アセットを直す (Version が上がる) と作り直す
// ==========================================================================
[ComponentInfo(DisplayName = "Compound Collider", Category = "Physics")]
public struct CompoundCollider : IComponent
{
    public CollisionAsset?[] Assets;
    [Tooltip("false にすると当たり判定をしない (体を消す)")]
    public bool Enabled = true;

    public CompoundCollider()
    {
        Assets = Array.Empty<CollisionAsset?>();
    }

    public CompoundCollider(params CollisionAsset[] assets)
    {
        Assets = assets.Cast<CollisionAsset?>().ToArray();
    }
}
