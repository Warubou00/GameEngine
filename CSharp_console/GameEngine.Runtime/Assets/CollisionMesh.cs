namespace GameEngine;

// ==========================================================================
// 衝突判定のメッシュ (動かない物を形どおりに当てる。地面や岩など)
//   engine.Assets.LoadCollisionMesh(model) で作り、Collider.FromMesh(mesh) で使う
//   C++ (Physics.dll) が三角形を持つ。同じモデルから作ったメッシュは共有される
//   Static の付いた物だけ (動く物に付けても動かない物として扱う)
// ==========================================================================
public sealed class CollisionMesh
{
    internal uint Handle { get; }
    public int    VertexCount   { get; }
    public int    TriangleCount { get; }
    public string Path          { get; }

    internal CollisionMesh(uint handle, int vertexCount, int triangleCount, string path)
    {
        Handle        = handle;
        VertexCount   = vertexCount;
        TriangleCount = triangleCount;
        Path          = path;
    }

    public override string ToString() => $"CollisionMesh({Path}, {TriangleCount} triangles)";
}
