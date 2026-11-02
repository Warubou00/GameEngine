using System.Numerics;
using GameEngine.Native;

namespace GameEngine;

// ==========================================================================
// 確認用の線 (コライダーの形など)。そのフレームだけ描く (毎フレーム呼ぶ)
//   物に隠れずに一番上に描く (深度は使わない)。トーンマップ / FXAA の後なので色はそのまま
//   線は配列にためて、フレームの最後に 1 回の P/Invoke で C++ に渡す (毎フレームの確保はしない)
// ==========================================================================
public static class DebugDraw
{
    private static NativeMethods.DebugLineVertex[] _vertices = new NativeMethods.DebugLineVertex[1024];
    private static int  _count;
    private static bool _sentAny;

    public const int MaxLines = 128 * 1024;   // C++ の MAX_DEBUG_LINE_VERTICES / 2

    public static int LineCount => _count / 2;

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
        if (_count == 0 && !_sentAny) return;
        fixed (NativeMethods.DebugLineVertex* data = _vertices)
        {
            NativeMethods.Renderer_SetDebugLines(data, (uint)_count);
        }
        _sentAny = _count > 0;
        _count = 0;
    }
}
