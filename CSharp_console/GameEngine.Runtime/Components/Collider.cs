using System.Numerics;

namespace GameEngine;

// コライダーの形 (C++ の Physics::ShapeType と同じ番号)
public enum ColliderShape : uint
{
    Sphere  = 0,   // Size.X = 半径
    Box     = 1,   // Size = 半分の大きさ (中心から面まで)
    Capsule = 2,   // Size.X = 半径 / Size.Y = 円柱の半分の高さ (ローカルの Y 方向。両端の半球は含まない)
}

// ==========================================================================
// コライダー (Transform と一緒に付ける。衝突判定の形)
//   位置 / 回転 / 大きさは Transform から取る (Center と Size にも Transform の大きさが掛かる)
//   Static も付いた物は「動かない物」として扱う (動かない物どうしは調べない。動かす時は Transform.MarkDirty)
//   重なり始め / 離れた は engine.Physics.Events で受け取る (次のフレームの System から読める)
//   Layer (0〜31) と CollideWith (調べる相手のレイヤーのビット) で、調べる組を絞れる
// ==========================================================================
public struct Collider
{
    public ColliderShape Shape;
    public Vector3       Size;
    public Vector3       Center;                     // 形の中心 (Transform の位置からのずれ。ローカル)
    public int           Layer       = 0;
    public uint          CollideWith = 0xFFFFFFFFu;  // 全部のレイヤー

    public Collider()
    {
        Shape = ColliderShape.Sphere;
        Size  = new Vector3(0.5f, 0.0f, 0.0f);
    }

    public static Collider Sphere(float radius, Vector3 center = default) =>
        new() { Shape = ColliderShape.Sphere, Size = new Vector3(radius, 0, 0), Center = center };

    public static Collider Box(Vector3 halfExtents, Vector3 center = default) =>
        new() { Shape = ColliderShape.Box, Size = halfExtents, Center = center };

    /// <param name="halfHeight">円柱の部分の半分の高さ (全体の高さ = 2 × (halfHeight + radius))</param>
    public static Collider Capsule(float radius, float halfHeight, Vector3 center = default) =>
        new() { Shape = ColliderShape.Capsule, Size = new Vector3(radius, halfHeight, 0), Center = center };
}
