using System.Numerics;
using GameEngine.Native;

namespace GameEngine;

// ==========================================================================
// 確認用の線 (コライダーの形など)。そのフレームだけ描く (毎フレーム呼ぶ)
//   物に隠れずに一番上に描く (深度は使わない)。トーンマップ / FXAA の後なので色はそのまま
//   線は配列にためて、フレームの最後に 1 回の P/Invoke で C++ に渡す (毎フレームの確保はしない)
//   v2.2 : 塗りつぶしの三角形 (Triangle / SolidBox / SolidSphere / SolidCapsule)。線と違い、描画パスの中で
//          RenderCommandBuffer.DrawDebugTriangles を積んだ所に描く (深度を見る。エディターの CollisionMap)。
//          積まなければ描かれない (普段のゲームの絵には出ない)
// ==========================================================================
public static class DebugDraw
{
    private static NativeMethods.DebugLineVertex[] _vertices = new NativeMethods.DebugLineVertex[1024];
    private static int  _count;
    private static bool _sentAny;

    public const int MaxLines = 128 * 1024;   // C++ の MAX_DEBUG_LINE_VERTICES / 2

    public static int LineCount => _count / 2;

    // ---------- v2.2 : 塗りつぶしの三角形 ----------
    private static NativeMethods.DebugLineVertex[] _triangles = new NativeMethods.DebugLineVertex[1024];
    private static int  _triangleCount;   // 頂点の数
    private static bool _sentAnyTriangles;
    public const int MaxTriangles = 1024 * 1024;   // C++ の MAX_DEBUG_TRIANGLE_VERTICES / 3
    public static int TriangleCount => _triangleCount / 3;
    public static int LastTriangleCount { get; private set; }   // 前のフレームで C++ へ送った三角形の数 (確認用)

    /// <summary>塗りつぶしの三角形 (両面。色の a で半透明)。描画パスの DrawDebugTriangles の所に描かれる</summary>
    public static void Triangle(Vector3 a, Vector3 b, Vector3 c, uint color)
    {
        if (_triangleCount >= MaxTriangles * 3) return;
        if (_triangleCount + 3 > _triangles.Length) Array.Resize(ref _triangles, Math.Min(_triangles.Length * 2, MaxTriangles * 3));
        _triangles[_triangleCount++] = new NativeMethods.DebugLineVertex { Position = a, Color = color };
        _triangles[_triangleCount++] = new NativeMethods.DebugLineVertex { Position = b, Color = color };
        _triangles[_triangleCount++] = new NativeMethods.DebugLineVertex { Position = c, Color = color };
    }

    // 四角形 (a b c d の順に回る 4 点。三角形 2 枚)
    public static void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, uint color)
    {
        Triangle(a, b, c, color);
        Triangle(a, c, d, color);
    }

    // 回した箱 (面 6 枚)
    public static void SolidBox(Vector3 center, Quaternion rotation, Vector3 halfExtents, uint color)
    {
        var x = Vector3.Transform(Vector3.UnitX, rotation) * halfExtents.X;
        var y = Vector3.Transform(Vector3.UnitY, rotation) * halfExtents.Y;
        var z = Vector3.Transform(Vector3.UnitZ, rotation) * halfExtents.Z;
        Span<Vector3> c = stackalloc Vector3[8];
        for (int i = 0; i < 8; i++) c[i] = center + ((i & 1) != 0 ? x : -x) + ((i & 2) != 0 ? y : -y) + ((i & 4) != 0 ? z : -z);
        Quad(c[0], c[1], c[3], c[2], color);   // -Z
        Quad(c[4], c[6], c[7], c[5], color);   // +Z
        Quad(c[0], c[2], c[6], c[4], color);   // -X
        Quad(c[1], c[5], c[7], c[3], color);   // +X
        Quad(c[0], c[4], c[5], c[1], color);   // -Y
        Quad(c[2], c[3], c[7], c[6], color);   // +Y
    }

    // 球 (緯度 × 経度に分ける)
    public static void SolidSphere(Vector3 center, float radius, uint color, int segments = 16)
        => SolidCapsule(center, Quaternion.Identity, radius, 0.0f, color, segments);

    // カプセル (ローカルの Y が軸。halfHeight = 0 なら球)。上の半球 (+halfHeight へずらす) + 円柱 + 下の半球
    public static void SolidCapsule(Vector3 center, Quaternion rotation, float radius, float halfHeight, uint color, int segments = 16)
    {
        segments = Math.Max(segments, 4);
        var half = Math.Max(segments / 4, 2);   // 半球の緯度の段
        var x = Vector3.Transform(Vector3.UnitX, rotation);
        var y = Vector3.Transform(Vector3.UnitY, rotation);
        var z = Vector3.Transform(Vector3.UnitZ, rotation);
        // theta : 上の極からの角度 (0〜π) / phi : 経度
        Vector3 point(float theta, int j, float offset)
        {
            var phi = MathF.Tau * j / segments;
            var ring = MathF.Sin(theta);
            return center + (x * (MathF.Cos(phi) * ring) + z * (MathF.Sin(phi) * ring)) * radius + y * (MathF.Cos(theta) * radius + offset);
        }
        for (int i = 0; i < half; i++)
        {
            var t0 = MathF.PI * 0.5f * i / half;
            var t1 = MathF.PI * 0.5f * (i + 1) / half;
            for (int j = 0; j < segments; j++)
            {
                Quad(point(t0, j, halfHeight), point(t0, j + 1, halfHeight), point(t1, j + 1, halfHeight), point(t1, j, halfHeight), color);   // 上
                Quad(point(MathF.PI - t1, j, -halfHeight), point(MathF.PI - t1, j + 1, -halfHeight),
                     point(MathF.PI - t0, j + 1, -halfHeight), point(MathF.PI - t0, j, -halfHeight), color);                                   // 下
            }
        }
        if (halfHeight <= 0.0f) return;
        for (int j = 0; j < segments; j++)   // 円柱
        {
            var e = MathF.PI * 0.5f;
            Quad(point(e, j, halfHeight), point(e, j + 1, halfHeight), point(e, j + 1, -halfHeight), point(e, j, -halfHeight), color);
        }
    }

    // 色 (0〜1) → R8G8B8A8
    public static uint Color(float r, float g, float b, float a = 1.0f) =>
        (uint)(Math.Clamp(r, 0, 1) * 255.0f + 0.5f) | ((uint)(Math.Clamp(g, 0, 1) * 255.0f + 0.5f) << 8) |
        ((uint)(Math.Clamp(b, 0, 1) * 255.0f + 0.5f) << 16) | ((uint)(Math.Clamp(a, 0, 1) * 255.0f + 0.5f) << 24);

    public static void Line(Vector3 a, Vector3 b, uint color)
    {
        if (_count >= MaxLines * 2) return;
        if (_count + 2 > _vertices.Length) Array.Resize(ref _vertices, _vertices.Length * 2);
        _vertices[_count++] = new NativeMethods.DebugLineVertex { Position = a, Color = color };
        _vertices[_count++] = new NativeMethods.DebugLineVertex { Position = b, Color = color };
    }

    // 回した箱 (12 本)
    public static void WireBox(Vector3 center, Quaternion rotation, Vector3 halfExtents, uint color)
    {
        var x = Vector3.Transform(Vector3.UnitX, rotation) * halfExtents.X;
        var y = Vector3.Transform(Vector3.UnitY, rotation) * halfExtents.Y;
        var z = Vector3.Transform(Vector3.UnitZ, rotation) * halfExtents.Z;
        Span<Vector3> c = stackalloc Vector3[8];
        for (int i = 0; i < 8; i++)
        {
            c[i] = center + ((i & 1) != 0 ? x : -x) + ((i & 2) != 0 ? y : -y) + ((i & 4) != 0 ? z : -z);
        }
        for (int i = 0; i < 8; i++)
        {
            if ((i & 1) == 0) Line(c[i], c[i | 1], color);
            if ((i & 2) == 0) Line(c[i], c[i | 2], color);
            if ((i & 4) == 0) Line(c[i], c[i | 4], color);
        }
    }

    // 円 (axisA / axisB の平面。segments 本)
    public static void Circle(Vector3 center, Vector3 axisA, Vector3 axisB, float radius, uint color, int segments = 24,
                              float startAngle = 0.0f, float endAngle = MathF.Tau)
    {
        var step = (endAngle - startAngle) / segments;
        var prev = center + (axisA * MathF.Cos(startAngle) + axisB * MathF.Sin(startAngle)) * radius;
        for (int i = 1; i <= segments; i++)
        {
            var angle = startAngle + step * i;
            var next = center + (axisA * MathF.Cos(angle) + axisB * MathF.Sin(angle)) * radius;
            Line(prev, next, color);
            prev = next;
        }
    }

    // 球 (3 つの円)
    public static void WireSphere(Vector3 center, float radius, uint color)
    {
        Circle(center, Vector3.UnitX, Vector3.UnitY, radius, color);
        Circle(center, Vector3.UnitY, Vector3.UnitZ, radius, color);
        Circle(center, Vector3.UnitZ, Vector3.UnitX, radius, color);
    }

    // カプセル (ローカルの Y が軸。両端の円 + 4 本の線 + 両端の半円)
    public static void WireCapsule(Vector3 center, Quaternion rotation, float radius, float halfHeight, uint color)
    {
        var x  = Vector3.Transform(Vector3.UnitX, rotation);
        var y  = Vector3.Transform(Vector3.UnitY, rotation);
        var z  = Vector3.Transform(Vector3.UnitZ, rotation);
        var top    = center + y * halfHeight;
        var bottom = center - y * halfHeight;
        Circle(top, x, z, radius, color);
        Circle(bottom, x, z, radius, color);
        Line(top + x * radius, bottom + x * radius, color);
        Line(top - x * radius, bottom - x * radius, color);
        Line(top + z * radius, bottom + z * radius, color);
        Line(top - z * radius, bottom - z * radius, color);
        Circle(top, x, y, radius, color, 12, 0.0f, MathF.PI);
        Circle(top, z, y, radius, color, 12, 0.0f, MathF.PI);
        Circle(bottom, x, -y, radius, color, 12, 0.0f, MathF.PI);
        Circle(bottom, z, -y, radius, color, 12, 0.0f, MathF.PI);
    }

    // フレームの最後に C++ へ渡して空にする (線が無いフレームは、前のフレームに線があった時だけ 0 本を送る)
    internal static unsafe void Flush()
    {
        // v2.2 : 塗りつぶしの三角形も (線と同じく、無いフレームは前に送った時だけ 0 を送る)
        if (_triangleCount == 0 && !_sentAnyTriangles) LastTriangleCount = 0;
        if (_triangleCount > 0 || _sentAnyTriangles)
        {
            fixed (NativeMethods.DebugLineVertex* data = _triangles)
            {
                NativeMethods.Renderer_SetDebugTriangles(data, (uint)_triangleCount);
            }
            _sentAnyTriangles = _triangleCount > 0;
            LastTriangleCount = _triangleCount / 3;
            _triangleCount = 0;
        }
        if (_count == 0 && !_sentAny) return;
        fixed (NativeMethods.DebugLineVertex* data = _vertices)
        {
            NativeMethods.Renderer_SetDebugLines(data, (uint)_count);
        }
        _sentAny = _count > 0;
        _count = 0;
    }
}
