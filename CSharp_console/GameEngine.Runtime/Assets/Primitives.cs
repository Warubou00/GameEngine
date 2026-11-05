using System.Numerics;
using System.Runtime.InteropServices;

namespace GameEngine;

// ==========================================================================
// プリミティブ (SH1)。コードで作る形 (立方体 / 球 / カプセル / 円柱 / 円錐 / トーラス / 平面 / クアッド)
//   モデルのパス : "builtin:Sphere" (既定の分割) / "builtin:Sphere:64" (分割数を指定)。ファイルは作らない
//   engine.Assets.LoadModel("builtin:Cube") で読める (同じパスは同じ Model)。シーンにはこのパスで保存され、書き出したゲームでも同じ形を作る
//   大きさ (Unity と同じ) : 立方体 1 / 球 直径 1 / カプセル・円柱 直径 1 × 高さ 2 / 円錐 直径 1 × 高さ 1 / トーラス 外径 1 /
//                         平面 10 × 10 (上向き) / クアッド 1 × 1 (-Z 向き。カメラの方)。どれも原点が中心 (平面とクアッドは面の上)
//   面の向き : このエンジンは画面で時計回りが表 (OBJ / PMX と同じ)。三角形は外向きの法線の側から見て時計回りに並べる
// ==========================================================================
public enum PrimitiveShape { Cube, Sphere, Capsule, Cylinder, Cone, Torus, Plane, Quad }

public static class Primitives
{
    public const string Prefix = "builtin:";

    public static string PathOf(PrimitiveShape shape, int segments = 0) => segments > 0 ? $"{Prefix}{shape}:{segments}" : $"{Prefix}{shape}";

    public static bool IsPrimitive(string? path) => path != null && path.StartsWith(Prefix, StringComparison.Ordinal);

    // "builtin:Sphere:48" → (Sphere, 48)。分割数が無ければ 0 (既定)
    public static bool TryParse(string path, out PrimitiveShape shape, out int segments)
    {
        shape = default;
        segments = 0;
        if (!IsPrimitive(path)) return false;
        var parts = path[Prefix.Length..].Split(':');
        if (!Enum.TryParse(parts[0], ignoreCase: true, out shape) || !Enum.IsDefined(shape)) return false;
        if (parts.Length > 1 && (!int.TryParse(parts[1], out segments) || segments < 3 || segments > 256)) return false;
        return true;
    }

    // ---------------------------------------------------------------------
    // 形を作る (CPU 側。C++ へは AssetManager が送る)
    // ---------------------------------------------------------------------

    public sealed class MeshData
    {
        public readonly List<Vector3> Positions = new();
        public readonly List<Vector3> Normals = new();
        public readonly List<Vector2> UVs = new();
        public readonly List<uint> Indices = new();

        public uint Add(Vector3 position, Vector3 normal, Vector2 uv)
        {
            Positions.Add(position);
            Normals.Add(Vector3.Normalize(normal));
            UVs.Add(uv);
            return (uint)(Positions.Count - 1);
        }

        // 三角形 : 頂点の法線の向き (外) から見て時計回りになるように並べる (逆なら入れ替える)
        //   左手系で画面の時計回り = cross(b - a, c - a) が見ている人の方 (= 外向きの法線の方) を向く
        public void Triangle(uint a, uint b, uint c)
        {
            var pa = Positions[(int)a];
            var face = Vector3.Cross(Positions[(int)b] - pa, Positions[(int)c] - pa);
            var outward = Normals[(int)a] + Normals[(int)b] + Normals[(int)c];
            if (Vector3.Dot(face, outward) < 0) (b, c) = (c, b);
            Indices.Add(a); Indices.Add(b); Indices.Add(c);
        }

        // 四角形 (a b c d が周りの順)
        public void Quad(uint a, uint b, uint c, uint d)
        {
            Triangle(a, b, c);
            Triangle(a, c, d);
        }
    }

    public static MeshData Build(PrimitiveShape shape, int segments = 0)
    {
        var mesh = new MeshData();
        switch (shape)
        {
        case PrimitiveShape.Cube:     cube(mesh); break;
        case PrimitiveShape.Sphere:   sphere(mesh, segments > 0 ? segments : 32); break;
        case PrimitiveShape.Capsule:  capsule(mesh, segments > 0 ? segments : 32); break;
        case PrimitiveShape.Cylinder: cylinder(mesh, segments > 0 ? segments : 32, 0.5f, 0.5f, -1.0f, 1.0f); break;
        case PrimitiveShape.Cone:     cylinder(mesh, segments > 0 ? segments : 32, 0.5f, 0.0f, -0.5f, 0.5f); break;
        case PrimitiveShape.Torus:    torus(mesh, segments > 0 ? segments : 32); break;
        case PrimitiveShape.Plane:    plane(mesh, segments > 0 ? segments : 10); break;
        case PrimitiveShape.Quad:     quad(mesh); break;
        }
        return mesh;
    }

    // 立方体 : 面ごとに 4 頂点 (角で法線を分ける)
    private static void cube(MeshData m)
    {
        var faces = new (Vector3 Normal, Vector3 U, Vector3 V)[]
        {
            (Vector3.UnitX,  -Vector3.UnitZ, Vector3.UnitY), (-Vector3.UnitX, Vector3.UnitZ, Vector3.UnitY),
            (Vector3.UnitY,  Vector3.UnitX, -Vector3.UnitZ), (-Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ),
            (Vector3.UnitZ,  Vector3.UnitX, Vector3.UnitY),  (-Vector3.UnitZ, -Vector3.UnitX, Vector3.UnitY),
        };
        foreach (var (n, u, v) in faces)
        {
            var center = n * 0.5f;
            var a = m.Add(center - u * 0.5f - v * 0.5f, n, new Vector2(0, 1));
            var b = m.Add(center + u * 0.5f - v * 0.5f, n, new Vector2(1, 1));
            var c = m.Add(center + u * 0.5f + v * 0.5f, n, new Vector2(1, 0));
            var d = m.Add(center - u * 0.5f + v * 0.5f, n, new Vector2(0, 0));
            m.Quad(a, b, c, d);
        }
    }

    // 球 (緯度・経度)。極は経度ごとに頂点を分ける (UV を合わせる)
    private static void sphere(MeshData m, int segments)
    {
        var rings = Math.Max(2, segments / 2);
        ringGrid(m, segments, rings, (u, v) =>
        {
            var theta = u * MathF.Tau;
            var phi = v * MathF.PI;   // 0 = 上
            var n = new Vector3(MathF.Sin(phi) * MathF.Cos(theta), MathF.Cos(phi), MathF.Sin(phi) * MathF.Sin(theta));
            return (n * 0.5f, n);
        });
    }

    // カプセル : 上の半球 + 円柱 (高さ 1) + 下の半球 (全体の高さ 2)
    private static void capsule(MeshData m, int segments)
    {
        var half = Math.Max(2, segments / 4);   // 半球の緯度の分割
        var rows = half * 2 + 1;                // 赤道で 1 行増やす (円柱の部分)
        ringGrid(m, segments, rows, (u, v) =>
        {
            var row = v * rows;
            var theta = u * MathF.Tau;
            float phi, offset;
            if (row <= half) { phi = row / half * MathF.PI * 0.5f; offset = 0.5f; }                  // 上の半球
            else if (row >= half + 1) { phi = MathF.PI * 0.5f + (row - half - 1) / half * MathF.PI * 0.5f; offset = -0.5f; }   // 下
            else { phi = MathF.PI * 0.5f; offset = 0.5f - (row - half); }                            // 円柱
            var n = new Vector3(MathF.Sin(phi) * MathF.Cos(theta), MathF.Cos(phi), MathF.Sin(phi) * MathF.Sin(theta));
            return (n * 0.5f + new Vector3(0, offset, 0), n);
        });
    }

    // 経度 segments × 緯度 rows の格子 (球 / カプセル)。f(u, v) → (位置, 法線)。u, v は 0..1
    private static void ringGrid(MeshData m, int segments, int rows, Func<float, float, (Vector3 Position, Vector3 Normal)> f)
    {
        var start = (uint)m.Positions.Count;
        for (int y = 0; y <= rows; y++)
        {
            for (int x = 0; x <= segments; x++)
            {
                float u = (float)x / segments, v = (float)y / rows;
                var (p, n) = f(u, v);
                m.Add(p, n, new Vector2(u, v));
            }
        }
        var stride = (uint)(segments + 1);
        for (int y = 0; y < rows; y++)
        {
            for (int x = 0; x < segments; x++)
            {
                var a = start + (uint)y * stride + (uint)x;
                var b = a + 1;
                var c = a + stride + 1;
                var d = a + stride;
                if (y > 0) m.Triangle(a, b, c);               // 上の極の三角形は潰れているので作らない
                if (y < rows - 1) m.Triangle(a, c, d);        // 下の極も
            }
        }
    }

    // 円柱 / 円錐 (上の半径 0 で円錐)。側面 + ふた
    private static void cylinder(MeshData m, int segments, float bottomRadius, float topRadius, float bottom, float top)
    {
        var height = top - bottom;
        var slope = (bottomRadius - topRadius) / height;   // 側面の法線の Y
        var side = (uint)m.Positions.Count;
        for (int x = 0; x <= segments; x++)
        {
            var u = (float)x / segments;
            var theta = u * MathF.Tau;
            var dir = new Vector3(MathF.Cos(theta), 0, MathF.Sin(theta));
            var normal = Vector3.Normalize(dir + new Vector3(0, slope, 0));
            // 円錐の先は、経度ごとの頂点の真ん中の向きにする (先が尖った所の陰影が割れないように)
            var topNormal = topRadius > 0 ? normal : Vector3.Normalize(new Vector3(MathF.Cos(theta + MathF.PI / segments), 0, MathF.Sin(theta + MathF.PI / segments)) + new Vector3(0, slope, 0));
            m.Add(dir * bottomRadius + new Vector3(0, bottom, 0), normal, new Vector2(u, 1));
            m.Add(dir * topRadius + new Vector3(0, top, 0), topNormal, new Vector2(u, 0));
        }
        for (int x = 0; x < segments; x++)
        {
            var b0 = side + (uint)x * 2;
            var t0 = b0 + 1;
            var b1 = b0 + 2;
            var t1 = b0 + 3;
            if (topRadius > 0) m.Quad(b0, b1, t1, t0);
            else m.Triangle(b0, b1, t0);
        }
        cap(m, segments, bottomRadius, bottom, -Vector3.UnitY);
        if (topRadius > 0) cap(m, segments, topRadius, top, Vector3.UnitY);
    }

    private static void cap(MeshData m, int segments, float radius, float y, Vector3 normal)
    {
        var center = m.Add(new Vector3(0, y, 0), normal, new Vector2(0.5f, 0.5f));
        var first = (uint)m.Positions.Count;
        for (int x = 0; x <= segments; x++)
        {
            var theta = (float)x / segments * MathF.Tau;
            var c = MathF.Cos(theta);
            var s = MathF.Sin(theta);
            m.Add(new Vector3(c * radius, y, s * radius), normal, new Vector2(0.5f + c * 0.5f, 0.5f - s * 0.5f));
        }
        for (int x = 0; x < segments; x++) m.Triangle(center, first + (uint)x, first + (uint)x + 1);
    }

    // トーラス : 外径 1 (輪の半径 0.35 + 管の半径 0.15)。管の分割は segments / 2
    private static void torus(MeshData m, int segments)
    {
        const float major = 0.35f, minor = 0.15f;
        var tube = Math.Max(3, segments / 2);
        var start = (uint)m.Positions.Count;
        for (int i = 0; i <= segments; i++)
        {
            var u = (float)i / segments;
            var theta = u * MathF.Tau;
            var ring = new Vector3(MathF.Cos(theta), 0, MathF.Sin(theta));
            for (int j = 0; j <= tube; j++)
            {
                var v = (float)j / tube;
                var phi = v * MathF.Tau;
                var n = ring * MathF.Cos(phi) + Vector3.UnitY * MathF.Sin(phi);
                m.Add(ring * major + n * minor, n, new Vector2(u, v));
            }
        }
        var stride = (uint)(tube + 1);
        for (int i = 0; i < segments; i++)
            for (int j = 0; j < tube; j++)
            {
                var a = start + (uint)i * stride + (uint)j;
                m.Quad(a, a + stride, a + stride + 1, a + 1);
            }
    }

    // 平面 : 10 × 10、上向き。分割 segments × segments
    private static void plane(MeshData m, int segments)
    {
        var start = (uint)m.Positions.Count;
        for (int z = 0; z <= segments; z++)
            for (int x = 0; x <= segments; x++)
            {
                float u = (float)x / segments, v = (float)z / segments;
                m.Add(new Vector3((u - 0.5f) * 10.0f, 0, (0.5f - v) * 10.0f), Vector3.UnitY, new Vector2(u, v));
            }
        var stride = (uint)(segments + 1);
        for (int z = 0; z < segments; z++)
            for (int x = 0; x < segments; x++)
            {
                var a = start + (uint)z * stride + (uint)x;
                m.Quad(a, a + 1, a + stride + 1, a + stride);
            }
    }

    // クアッド : 1 × 1、-Z 向き
    private static void quad(MeshData m)
    {
        var n = -Vector3.UnitZ;
        var a = m.Add(new Vector3(-0.5f, -0.5f, 0), n, new Vector2(0, 1));
        var b = m.Add(new Vector3(0.5f, -0.5f, 0), n, new Vector2(1, 1));
        var c = m.Add(new Vector3(0.5f, 0.5f, 0), n, new Vector2(1, 0));
        var d = m.Add(new Vector3(-0.5f, 0.5f, 0), n, new Vector2(0, 0));
        m.Quad(a, b, c, d);
    }

    // ---------------------------------------------------------------------
    // 形に合うコライダー (エディターの GameObject > 3D Object が付ける)
    //   円柱はカプセル (Unity と同じ)。円錐 / トーラスはメッシュ (動かない物だけ)。平面 / クアッドは薄い箱
    // ---------------------------------------------------------------------
    public static Collider? ColliderFor(PrimitiveShape shape, AssetManager assets, Model model) => shape switch
    {
        PrimitiveShape.Cube     => Collider.Box(new Vector3(0.5f)),
        PrimitiveShape.Sphere   => Collider.Sphere(0.5f),
        PrimitiveShape.Capsule  => Collider.Capsule(0.5f, 0.5f),
        PrimitiveShape.Cylinder => Collider.Capsule(0.5f, 0.5f),
        PrimitiveShape.Plane    => Collider.Box(new Vector3(5.0f, 0.005f, 5.0f), new Vector3(0, -0.005f, 0)),
        PrimitiveShape.Quad     => Collider.Box(new Vector3(0.5f, 0.5f, 0.005f)),
        _ => assets.PhysicsLoaded ? Collider.FromMesh(assets.LoadCollisionMesh(model)) : null,
    };

    // C++ の RHI::Vertex と同じ並び (56 byte)
    [StructLayout(LayoutKind.Sequential)]
    internal struct NativeVertex
    {
        public Vector3 Position;
        public float   Padding1;
        public Vector3 Normal;
        public float   Padding2;
        public Vector4 Color;
        public Vector2 UV;
    }
}
