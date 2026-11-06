namespace GameEngine;

// ==========================================================================
// エンジンが作る 3D テクスチャ (SH2 / SH3)。シェーダーの texture3d のパラメーターに "builtin:PerlinWorley3D:128" のように書く
//   ファイルのアセットではない (乱数の種は固定で、いつも同じ値)。端は繋がる (タイルになる : 繰り返しのサンプラーで継ぎ目が出ない)
//   作った物はプロジェクトの Library/Noise3D にキャッシュする (2 回目からは読むだけ。書き出したゲームも Data/Library に入れる)
//
//   ValueNoise3D   : 格子の値を滑らかに補間した物の 3 段 (fBm)。R8
//   Perlin3D       : 勾配のノイズ (Perlin) の 3 段。R8
//   Worley3D       : 点からの距離のノイズ (Worley) を反転した物の 3 段 (丸い塊)。R8
//   PerlinWorley3D : 雲用 (CEDEC 2019 の資料の Perlin-Worley。Schneider 2015 の作り方)。RGBA8
//                    R = Perlin の fBm / G = Worley の fBm / B = Perlin-Worley (Perlin を Worley で削った物) / A = 細かい Worley
// ==========================================================================
public static class Noise3D
{
    public const string Prefix = "builtin:";
    public static readonly string[] Kinds = { "ValueNoise3D", "Perlin3D", "Worley3D", "PerlinWorley3D" };
    private const int CacheVersion = 1;   // 作り方を変えたら上げる (古いキャッシュを使わない)

    public static bool IsNoise(string? path) => path != null && path.StartsWith(Prefix, StringComparison.Ordinal);

    // "builtin:PerlinWorley3D:128" → (PerlinWorley3D, 128)。大きさが無ければ 64
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

    public static int ChannelsOf(string kind) => kind == "PerlinWorley3D" ? 4 : 1;

    /// <summary>キャッシュがあれば読み、無ければ作って書く (cacheFolder が null なら作るだけ)。戻り値 = ボクセル (x → y → z の順) と作ったか</summary>
    public static (byte[] Data, bool Generated) Load(string kind, int size, string? cacheFolder)
    {
        var channels = ChannelsOf(kind);
        var bytes = size * size * size * channels;
        var file = cacheFolder != null ? System.IO.Path.Combine(cacheFolder, $"{kind}_{size}_v{CacheVersion}.bin") : null;
        if (file != null && File.Exists(file))
        {
            try
            {
                var cached = File.ReadAllBytes(file);
                if (cached.Length == bytes) return (cached, false);
            }
            catch (IOException) { }
        }
        var data = Generate(kind, size);
        if (file != null)
        {
            try
            {
                Directory.CreateDirectory(cacheFolder!);
                File.WriteAllBytes(file, data);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Console.WriteLine($"[Texture3D] Cannot write the cache {file} : {ex.Message}");
            }
        }
        return (data, true);
    }

    /// <summary>作る (x → y → z の順。1 ボクセル ChannelsOf(kind) バイト)</summary>
    public static byte[] Generate(string kind, int size)
    {
        var channels = ChannelsOf(kind);
        var data = new byte[size * size * size * channels];
        Parallel.For(0, size, z =>
        {
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                {
                    float u = (x + 0.5f) / size, v = (y + 0.5f) / size, w = (z + 0.5f) / size;
                    var index = ((z * size + y) * size + x) * channels;
                    switch (kind)
                    {
                    case "ValueNoise3D":
                        data[index] = toByte((0.5f * valueNoise(u, v, w, 4) + 0.25f * valueNoise(u, v, w, 8) + 0.125f * valueNoise(u, v, w, 16)) / 0.875f);
                        break;
                    case "Perlin3D":
                        data[index] = toByte(perlinFbm(u, v, w, 4));
                        break;
                    case "Worley3D":
                        data[index] = toByte(worleyFbm(u, v, w, 4));
                        break;
                    default:   // PerlinWorley3D
                        var perlin = perlinFbm(u, v, w, 4);
                        var worley = worleyFbm(u, v, w, 4);
                        data[index]     = toByte(perlin);
                        data[index + 1] = toByte(worley);
                        data[index + 2] = toByte(remap(perlin, worley - 1.0f, 1.0f, 0.0f, 1.0f));   // Perlin を Worley の形で削る (雲の盛り上がり)
                        data[index + 3] = toByte(worleyFbm(u, v, w, 16));
                        break;
                    }
                }
        });
        return data;
    }

    private static byte toByte(float value) => (byte)Math.Clamp(value * 255.0f + 0.5f, 0, 255);
    private static float remap(float v, float lo, float hi, float newLo, float newHi) => newLo + (v - lo) / MathF.Max(hi - lo, 1e-5f) * (newHi - newLo);
    private static int mod(int a, int m) => ((a % m) + m) % m;

    // ---------- 値のノイズ ----------
    private static float valueNoise(float u, float v, float w, int period)
    {
        float x = u * period, y = v * period, z = w * period;
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y), zi = (int)MathF.Floor(z);
        float fx = fade3(x - xi), fy = fade3(y - yi), fz = fade3(z - zi);
        float c(int dx, int dy, int dz) => hash(mod(xi + dx, period), mod(yi + dy, period), mod(zi + dz, period), period);
        return trilinear(c(0, 0, 0), c(1, 0, 0), c(0, 1, 0), c(1, 1, 0), c(0, 0, 1), c(1, 0, 1), c(0, 1, 1), c(1, 1, 1), fx, fy, fz);
    }

    private static float fade3(float t) => t * t * (3 - 2 * t);   // Hermite (資料の値のノイズと同じ)
    private static float fade5(float t) => t * t * t * (t * (t * 6 - 15) + 10);   // Perlin の改良版

    private static float trilinear(float c000, float c100, float c010, float c110, float c001, float c101, float c011, float c111, float fx, float fy, float fz)
    {
        float lerp(float a, float b, float t) => a + (b - a) * t;
        return lerp(lerp(lerp(c000, c100, fx), lerp(c010, c110, fx), fy), lerp(lerp(c001, c101, fx), lerp(c011, c111, fx), fy), fz);
    }

    // ---------- Perlin (勾配のノイズ。周期 period で繋がる) ----------
    private static readonly (float X, float Y, float Z)[] Gradients =
    {
        (1, 1, 0), (-1, 1, 0), (1, -1, 0), (-1, -1, 0), (1, 0, 1), (-1, 0, 1), (1, 0, -1), (-1, 0, -1),
        (0, 1, 1), (0, -1, 1), (0, 1, -1), (0, -1, -1),
    };

    private static float perlin(float u, float v, float w, int period)
    {
        float x = u * period, y = v * period, z = w * period;
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y), zi = (int)MathF.Floor(z);
        float fx = x - xi, fy = y - yi, fz = z - zi;
        float g(int dx, int dy, int dz)
        {
            var h = (int)(hash(mod(xi + dx, period), mod(yi + dy, period), mod(zi + dz, period), period * 7 + 1) * 12.0f) % 12;
            var (gx, gy, gz) = Gradients[h];
            return gx * (fx - dx) + gy * (fy - dy) + gz * (fz - dz);
        }
        var r = trilinear(g(0, 0, 0), g(1, 0, 0), g(0, 1, 0), g(1, 1, 0), g(0, 0, 1), g(1, 0, 1), g(0, 1, 1), g(1, 1, 1), fade5(fx), fade5(fy), fade5(fz));
        return r * 0.5f + 0.5f;   // おおよそ 0..1
    }

    // 3 段 (周期 base, base × 2, base × 4)。0..1 に広げる
    private static float perlinFbm(float u, float v, float w, int basePeriod)
    {
        var f = 0.5f * perlin(u, v, w, basePeriod) + 0.25f * perlin(u, v, w, basePeriod * 2) + 0.125f * perlin(u, v, w, basePeriod * 4);
        return Math.Clamp((f / 0.875f - 0.5f) * 1.8f + 0.5f, 0.0f, 1.0f);
    }

    // ---------- Worley (セルごとの点からの一番近い距離。反転して 1 = 点の所。周期 period で繋がる) ----------
    private static float worley(float u, float v, float w, int period)
    {
        float x = u * period, y = v * period, z = w * period;
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y), zi = (int)MathF.Floor(z);
        var best = float.MaxValue;
        for (int dz = -1; dz <= 1; dz++)
            for (int dy = -1; dy <= 1; dy++)
                for (int dx = -1; dx <= 1; dx++)
                {
                    int cx = xi + dx, cy = yi + dy, cz = zi + dz;
                    int wx = mod(cx, period), wy = mod(cy, period), wz = mod(cz, period);
                    float px = cx + hash(wx, wy, wz, period * 3 + 11);
                    float py = cy + hash(wx, wy, wz, period * 5 + 23);
                    float pz = cz + hash(wx, wy, wz, period * 13 + 37);
                    float ddx = px - x, ddy = py - y, ddz = pz - z;
                    best = MathF.Min(best, ddx * ddx + ddy * ddy + ddz * ddz);
                }
        return 1.0f - Math.Clamp(MathF.Sqrt(best), 0.0f, 1.0f);
    }

    private static float worleyFbm(float u, float v, float w, int basePeriod) =>
        0.625f * worley(u, v, w, basePeriod) + 0.25f * worley(u, v, w, basePeriod * 2) + 0.125f * worley(u, v, w, basePeriod * 4);

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
