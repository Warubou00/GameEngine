namespace GameEngine;

// ==========================================================================
// エンジンが作る 3D テクスチャ (SH2)。シェーダーの texture3d のパラメーターに "builtin:ValueNoise3D:64" のように書く
//   ファイルは作らない (起動するたびに同じ値を作る。乱数の種は固定)。端は繋がる (タイルになる : 繰り返しのサンプラーで継ぎ目が出ない)
//   ValueNoise3D : 格子の点に乱数を置いて滑らかに補間した物を 3 段重ねた (fBm)。R8
//   (SH3 で雲の Perlin-Worley などを足す)
// ==========================================================================
public static class Noise3D
{
    public const string Prefix = "builtin:";
    public static readonly string[] Kinds = { "ValueNoise3D" };

    public static bool IsNoise(string? path) => path != null && path.StartsWith(Prefix, StringComparison.Ordinal);

    // "builtin:ValueNoise3D:64" → (ValueNoise3D, 64)。大きさが無ければ 64
    public static bool TryParse(string path, out string kind, out int size)
    {
        kind = "";
        size = 64;
        if (!IsNoise(path)) return false;
        var parts = path[Prefix.Length..].Split(':');
        kind = Kinds.FirstOrDefault(k => k.Equals(parts[0], StringComparison.OrdinalIgnoreCase)) ?? "";
        if (kind.Length == 0) return false;
        if (parts.Length > 1 && (!int.TryParse(parts[1], out size) || size < 4 || size > 256)) return false;
        return true;
    }

    /// <summary>1 ボクセル 1 バイト (x → y → z の順)</summary>
    public static byte[] Generate(string kind, int size)
    {
        var data = new byte[size * size * size];
        switch (kind)
        {
        case "ValueNoise3D":
            Parallel.For(0, size, z =>
            {
                for (int y = 0; y < size; y++)
                    for (int x = 0; x < size; x++)
                    {
                        float u = (float)x / size, v = (float)y / size, w = (float)z / size;
                        var f = 0.5f * valueNoise(u, v, w, 4) + 0.25f * valueNoise(u, v, w, 8) + 0.125f * valueNoise(u, v, w, 16);
                        data[(z * size + y) * size + x] = (byte)Math.Clamp(f / 0.875f * 255.0f + 0.5f, 0, 255);
                    }
            });
            break;
        }
        return data;
    }

    // 周期 period の格子の値のノイズ (0..1)。Hermite で補間 (資料の値のノイズと同じ形)
    private static float valueNoise(float u, float v, float w, int period)
    {
        float x = u * period, y = v * period, z = w * period;
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y), zi = (int)MathF.Floor(z);
        float fx = x - xi, fy = y - yi, fz = z - zi;
        fx = fx * fx * (3 - 2 * fx); fy = fy * fy * (3 - 2 * fy); fz = fz * fz * (3 - 2 * fz);
        float c(int dx, int dy, int dz) => hash(mod(xi + dx, period), mod(yi + dy, period), mod(zi + dz, period), period);
        float lerp(float a, float b, float t) => a + (b - a) * t;
        var x00 = lerp(c(0, 0, 0), c(1, 0, 0), fx);
        var x10 = lerp(c(0, 1, 0), c(1, 1, 0), fx);
        var x01 = lerp(c(0, 0, 1), c(1, 0, 1), fx);
        var x11 = lerp(c(0, 1, 1), c(1, 1, 1), fx);
        return lerp(lerp(x00, x10, fy), lerp(x01, x11, fy), fz);
    }

    private static int mod(int a, int m) => ((a % m) + m) % m;

    // 格子の点の乱数 (0..1。種は固定)
    internal static float hash(int x, int y, int z, int salt)
    {
        unchecked
        {
            uint h = (uint)(x * 73856093) ^ (uint)(y * 19349663) ^ (uint)(z * 83492791) ^ (uint)(salt * 2654435761u);
            h ^= h >> 13; h *= 0x5bd1e995; h ^= h >> 15;
            return (h & 0xFFFFFF) / (float)0xFFFFFF;
        }
    }
}
