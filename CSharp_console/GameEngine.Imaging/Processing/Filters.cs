namespace GameEngine.Imaging.Processing;

// ==========================================================================
// 画像処理 (v2.3) : 周りのピクセルを見る処理。端は端のピクセルを延ばす
// ==========================================================================

/// <summary>ガウシアンブラー (横 → 縦の 2 回に分ける)。Sigma は元の画像のピクセル</summary>
public sealed class GaussianBlurOperation : ImageOperation
{
    public override string DisplayName => "Gaussian Blur";
    public override string Category => "Filter";

    public float Sigma { get; set; } = 3f;

    public override void Apply(ImageBuffer src, ImageBuffer dst, ProcessContext ctx) => Blur(src, dst, Sigma * ctx.Scale, ctx);

    /// <summary>ぼかす (sigma : 今の画像のピクセル)。src と dst は同じでもよい</summary>
    public static void Blur(ImageBuffer src, ImageBuffer dst, float sigma, ProcessContext ctx)
    {
        if (sigma < 0.2f) { if (!ReferenceEquals(src, dst)) src.CopyTo(dst); return; }
        int radius = Math.Max(1, (int)MathF.Ceiling(sigma * 3f));
        var kernel = new float[radius * 2 + 1];
        float total = 0;
        for (int i = -radius; i <= radius; i++) total += kernel[i + radius] = MathF.Exp(-i * i / (2f * sigma * sigma));
        for (int i = 0; i < kernel.Length; i++) kernel[i] /= total;

        int width = src.Width, height = src.Height;
        var temp = new float[src.Pixels.Length];
        var s = src.Pixels;
        Parallel.For(0, height, ctx.Parallel, y =>
        {
            int row = y * width;
            for (int x = 0; x < width; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int sx = Math.Clamp(x + k, 0, width - 1);
                    int i = (row + sx) * 4;
                    float w = kernel[k + radius];
                    r += s[i] * w; g += s[i + 1] * w; b += s[i + 2] * w; a += s[i + 3] * w;
                }
                int o = (row + x) * 4;
                temp[o] = r; temp[o + 1] = g; temp[o + 2] = b; temp[o + 3] = a;
            }
        });
        var d = dst.Pixels;
        Parallel.For(0, height, ctx.Parallel, y =>
        {
            for (int x = 0; x < width; x++)
            {
                float r = 0, g = 0, b = 0, a = 0;
                for (int k = -radius; k <= radius; k++)
                {
                    int sy = Math.Clamp(y + k, 0, height - 1);
                    int i = (sy * width + x) * 4;
                    float w = kernel[k + radius];
                    r += temp[i] * w; g += temp[i + 1] * w; b += temp[i + 2] * w; a += temp[i + 3] * w;
                }
                int o = (y * width + x) * 4;
                d[o] = r; d[o + 1] = g; d[o + 2] = b; d[o + 3] = a;
            }
        });
    }
}

/// <summary>鮮鋭化 (アンシャープマスク : out = in + 量 * (in - ぼかした物))</summary>
public sealed class SharpenOperation : ImageOperation
{
    public override string DisplayName => "Sharpen";
    public override string Category => "Filter";

    public float Radius { get; set; } = 1.5f;
    public float Strength { get; set; } = 1f;

    public override void Apply(ImageBuffer src, ImageBuffer dst, ProcessContext ctx)
    {
        var blurred = new ImageBuffer(src.Width, src.Height);
        GaussianBlurOperation.Blur(src, blurred, Math.Max(0.3f, Radius * ctx.Scale), ctx);
        float k = Strength;
        var s = src.Pixels;
        var b = blurred.Pixels;
        var d = dst.Pixels;
        int width = src.Width;
        Parallel.For(0, src.Height, ctx.Parallel, y =>
        {
            for (int i = y * width * 4, end = i + width * 4; i < end; i += 4)
            {
                d[i] = s[i] + k * (s[i] - b[i]);
                d[i + 1] = s[i + 1] + k * (s[i + 1] - b[i + 1]);
                d[i + 2] = s[i + 2] + k * (s[i + 2] - b[i + 2]);
                d[i + 3] = s[i + 3];
            }
        });
    }
}

/// <summary>ソーベルの向き</summary>
public enum EdgeDirection
{
    Both,
    Horizontal,   // X 方向の変化 (縦の線が出る)
    Vertical,     // Y 方向の変化 (横の線が出る)
}

/// <summary>ソーベルフィルタ (3x3)。輪郭の強さ (0〜1。段差 1 の縁 = 1) * Strength</summary>
public sealed class SobelOperation : ImageOperation
{
    public override string DisplayName => "Sobel";
    public override string Category => "Filter";

    public EdgeDirection Direction { get; set; } = EdgeDirection.Both;
    public float Strength { get; set; } = 1f;

    /// <summary>true = 輝度で調べてグレーで出す / false = RGB それぞれ</summary>
    public bool Grayscale { get; set; } = true;

    public override void Apply(ImageBuffer src, ImageBuffer dst, ProcessContext ctx)
    {
        int width = src.Width, height = src.Height;
        var direction = Direction;
        float k = Strength * 0.25f;
        bool gray = Grayscale;
        float[]? lum = gray ? luminancePlane(src, ctx) : null;
        var s = src.Pixels;
        var d = dst.Pixels;
        Parallel.For(0, height, ctx.Parallel, y =>
        {
            int ym = Math.Max(y - 1, 0), yp = Math.Min(y + 1, height - 1);
            for (int x = 0; x < width; x++)
            {
                int xm = Math.Max(x - 1, 0), xp = Math.Min(x + 1, width - 1);
                int o = (y * width + x) * 4;
                for (int c = 0; c < (gray ? 1 : 3); c++)
                {
                    float at(int px, int py) => gray ? lum![py * width + px] : s[(py * width + px) * 4 + c];
                    float gx = -at(xm, ym) - 2 * at(xm, y) - at(xm, yp) + at(xp, ym) + 2 * at(xp, y) + at(xp, yp);
                    float gy = -at(xm, ym) - 2 * at(x, ym) - at(xp, ym) + at(xm, yp) + 2 * at(x, yp) + at(xp, yp);
                    float v = direction switch
                    {
                        EdgeDirection.Horizontal => MathF.Abs(gx),
                        EdgeDirection.Vertical => MathF.Abs(gy),
                        _ => MathF.Sqrt(gx * gx + gy * gy),
                    } * k;
                    if (gray) { d[o] = d[o + 1] = d[o + 2] = v; }
                    else d[o + c] = v;
                }
                d[o + 3] = s[o + 3];
            }
        });
    }

    internal static float[] luminancePlane(ImageBuffer src, ProcessContext ctx)
    {
        var lum = new float[src.PixelCount];
        var s = src.Pixels;
        Parallel.For(0, src.Height, ctx.Parallel, y =>
        {
            for (int i = y * src.Width, end = i + src.Width; i < end; i++) lum[i] = ImageMath.Luminance(s[i * 4], s[i * 4 + 1], s[i * 4 + 2]);
        });
        return lum;
    }
}

/// <summary>エンボス : 光の向き (角度。0 = 右、90 = 上) の差 + 0.5 の灰色</summary>
public sealed class EmbossOperation : ImageOperation
{
    public override string DisplayName => "Emboss";
    public override string Category => "Filter";

    public float Angle { get; set; } = 135f;
    public float Strength { get; set; } = 1f;

    /// <summary>距離 (元の画像のピクセル)</summary>
    public float Distance { get; set; } = 1f;

    public bool Grayscale { get; set; } = true;

    public override void Apply(ImageBuffer src, ImageBuffer dst, ProcessContext ctx)
    {
        float rad = Angle * MathF.PI / 180f;
        float distance = Math.Max(0.5f, Distance * ctx.Scale);
        float dx = MathF.Cos(rad) * distance, dy = -MathF.Sin(rad) * distance;   // 画像の y は下向き
        float k = Strength;
        bool gray = Grayscale;
        int width = src.Width;
        var s = src.Pixels;
        var d = dst.Pixels;
        Parallel.For(0, src.Height, ctx.Parallel, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int o = (y * width + x) * 4;
                if (gray)
                {
                    float toward = lumAt(src, x + dx, y + dy), away = lumAt(src, x - dx, y - dy);
                    d[o] = d[o + 1] = d[o + 2] = 0.5f + k * (toward - away);
                }
                else
                    for (int c = 0; c < 3; c++) d[o + c] = 0.5f + k * (ImageMath.Sample(src, x + dx, y + dy, c) - ImageMath.Sample(src, x - dx, y - dy, c));
                d[o + 3] = s[o + 3];
            }
        });
    }

    private static float lumAt(ImageBuffer image, float x, float y) =>
        ImageMath.Luminance(ImageMath.Sample(image, x, y, 0), ImageMath.Sample(image, x, y, 1), ImageMath.Sample(image, x, y, 2));
}

/// <summary>モザイク : BlockSize の四角の平均の色 (元の画像のピクセル)</summary>
public sealed class MosaicOperation : ImageOperation
{
    public override string DisplayName => "Mosaic";
    public override string Category => "Filter";

    public int BlockSize { get; set; } = 16;

    public override void Apply(ImageBuffer src, ImageBuffer dst, ProcessContext ctx)
    {
        int block = Math.Max(1, (int)MathF.Round(BlockSize * ctx.Scale));
        int width = src.Width, height = src.Height;
        int blocksX = (width + block - 1) / block, blocksY = (height + block - 1) / block;
        var s = src.Pixels;
        var d = dst.Pixels;
        Parallel.For(0, blocksY, ctx.Parallel, by =>
        {
            int y0 = by * block, y1 = Math.Min(y0 + block, height);
            for (int bx = 0; bx < blocksX; bx++)
            {
                int x0 = bx * block, x1 = Math.Min(x0 + block, width);
                float r = 0, g = 0, b = 0, a = 0;
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        int i = (y * width + x) * 4;
                        r += s[i]; g += s[i + 1]; b += s[i + 2]; a += s[i + 3];
                    }
                float n = 1f / ((x1 - x0) * (y1 - y0));
                r *= n; g *= n; b *= n; a *= n;
                for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        int i = (y * width + x) * 4;
                        d[i] = r; d[i + 1] = g; d[i + 2] = b; d[i + 3] = a;
                    }
            }
        });
    }
}

/// <summary>色収差 : 中心から外へ、R を広げて B を縮める (Shift = 一番遠い角でのずれ。元の画像のピクセル)</summary>
public sealed class ChromaticAberrationOperation : ImageOperation
{
    public override string DisplayName => "Chromatic Aberration";
    public override string Category => "Filter";

    public float Shift { get; set; } = 6f;

    /// <summary>中心 (0〜1。0.5 = 画像の真ん中)</summary>
    public float CenterX { get; set; } = 0.5f;
    public float CenterY { get; set; } = 0.5f;

    public override void Apply(ImageBuffer src, ImageBuffer dst, ProcessContext ctx)
    {
        int width = src.Width, height = src.Height;
        float cx = CenterX * (width - 1), cy = CenterY * (height - 1);
        float far = MathF.Sqrt(MathF.Max(cx, width - 1 - cx) * MathF.Max(cx, width - 1 - cx) + MathF.Max(cy, height - 1 - cy) * MathF.Max(cy, height - 1 - cy));
        float k = far > 0 ? Shift * ctx.Scale / far : 0f;
        var s = src.Pixels;
        var d = dst.Pixels;
        Parallel.For(0, height, ctx.Parallel, y =>
        {
            for (int x = 0; x < width; x++)
            {
                int o = (y * width + x) * 4;
                float ox = x - cx, oy = y - cy;
                // R は中心寄りを読む = 外へ広がって見える。B は外寄りを読む = 内へ縮んで見える
                d[o] = ImageMath.Sample(src, cx + ox * (1 - k), cy + oy * (1 - k), 0);
                d[o + 1] = s[o + 1];
                d[o + 2] = ImageMath.Sample(src, cx + ox * (1 + k), cy + oy * (1 + k), 2);
                d[o + 3] = s[o + 3];
            }
        });
    }
}
