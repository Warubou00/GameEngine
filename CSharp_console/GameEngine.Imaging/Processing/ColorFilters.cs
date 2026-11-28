namespace GameEngine.Imaging.Processing;

// ==========================================================================
// 色のフィルター (v2.3)。1 ピクセルずつ (周りを見ない)。アルファはそのまま
// ==========================================================================

/// <summary>グレースケール : Y = R * 0.30 + G * 0.59 + B * 0.11</summary>
public sealed class GrayscaleOperation : ImageOperation
{
    public override string DisplayName => "Grayscale";
    public override string Category => "Color";

    public override void Apply(ImageBuffer src, ImageBuffer dst, ProcessContext ctx) =>
        ImageMath.ForEachPixel(src, dst, ctx, (ref float r, ref float g, ref float b, ref float a) => r = g = b = ImageMath.Luminance(r, g, b));
}

/// <summary>RGB の 1 つだけを残す (色のまま = 他を 0 / グレー = その値を RGB に)</summary>
public sealed class ChannelScaleOperation : ImageOperation
{
    public override string DisplayName => $"{Channel} Scale";
    public override string Category => "Color";

    public ColorChannel Channel { get; set; } = ColorChannel.Red;

    /// <summary>true = その値をグレーで出す</summary>
    public bool AsGray { get; set; }

    public override void Apply(ImageBuffer src, ImageBuffer dst, ProcessContext ctx)
    {
        var channel = Channel;
        bool gray = AsGray;
        ImageMath.ForEachPixel(src, dst, ctx, (ref float r, ref float g, ref float b, ref float a) =>
        {
            float v = channel switch { ColorChannel.Red => r, ColorChannel.Green => g, _ => b };
            if (gray) { r = g = b = v; return; }
            r = channel == ColorChannel.Red ? v : 0f;
            g = channel == ColorChannel.Green ? v : 0f;
            b = channel == ColorChannel.Blue ? v : 0f;
        });
    }
}

/// <summary>ポスタライズ : 各チャンネルを Levels 段に</summary>
public sealed class PosterizeOperation : ImageOperation
{
    public override string DisplayName => "Posterize";
    public override string Category => "Color";

    /// <summary>段の数 (2〜256)</summary>
    public int Levels { get; set; } = 4;

    public override void Apply(ImageBuffer src, ImageBuffer dst, ProcessContext ctx)
    {
        float steps = Math.Clamp(Levels, 2, 256) - 1;
        static float q(float v, float steps) => MathF.Min(MathF.Floor(ImageMath.Saturate(v) * (steps + 1)), steps) / steps;
        ImageMath.ForEachPixel(src, dst, ctx, (ref float r, ref float g, ref float b, ref float a) => { r = q(r, steps); g = q(g, steps); b = q(b, steps); });
    }
}

/// <summary>ソラリゼーション : しきい値より明るい所を反転</summary>
public sealed class SolarizeOperation : ImageOperation
{
    public override string DisplayName => "Solarize";
    public override string Category => "Color";

    public float Threshold { get; set; } = 0.5f;

    public override void Apply(ImageBuffer src, ImageBuffer dst, ProcessContext ctx)
    {
        float t = Threshold;
        ImageMath.ForEachPixel(src, dst, ctx, (ref float r, ref float g, ref float b, ref float a) =>
        {
            if (r > t) r = 1f - r;
            if (g > t) g = 1f - g;
            if (b > t) b = 1f - b;
        });
    }
}

/// <summary>ネガポジ反転 : RGB を 1 - x</summary>
public sealed class InvertOperation : ImageOperation
{
    public override string DisplayName => "Invert";
    public override string Category => "Color";

    public override void Apply(ImageBuffer src, ImageBuffer dst, ProcessContext ctx) =>
        ImageMath.ForEachPixel(src, dst, ctx, (ref float r, ref float g, ref float b, ref float a) => { r = 1f - r; g = 1f - g; b = 1f - b; });
}

/// <summary>セピア : グレースケールにしてから色を付ける (R + 40 / G + 20 / B - 20 を 0〜255 で)</summary>
public sealed class SepiaOperation : ImageOperation
{
    public override string DisplayName => "Sepia";
    public override string Category => "Color";

    /// <summary>色の付き方 (0 = グレーのまま / 1 = 既定 / 2 = 濃い)</summary>
    public float Tone { get; set; } = 1f;

    public override void Apply(ImageBuffer src, ImageBuffer dst, ProcessContext ctx)
    {
        float k = Tone;
        ImageMath.ForEachPixel(src, dst, ctx, (ref float r, ref float g, ref float b, ref float a) =>
        {
            float y = ImageMath.Luminance(r, g, b);
            r = ImageMath.Saturate(y + k * 40f / 255f);
            g = ImageMath.Saturate(y + k * 20f / 255f);
            b = ImageMath.Saturate(y - k * 20f / 255f);
        });
    }
}

/// <summary>ガンマ補正 : out = in ^ (1 / γ)  (γ > 1 で明るく)</summary>
public sealed class GammaOperation : ImageOperation
{
    public override string DisplayName => "Gamma";
    public override string Category => "Color";

    public float Gamma { get; set; } = 2.2f;

    public override void Apply(ImageBuffer src, ImageBuffer dst, ProcessContext ctx)
    {
        // 1024 段の表を引く (pow を毎回呼ばない)
        var lut = Lut.Build(v => MathF.Pow(v, 1f / Math.Max(Gamma, 0.01f)));
        ImageMath.ForEachPixel(src, dst, ctx, (ref float r, ref float g, ref float b, ref float a) => { r = Lut.Apply(lut, r); g = Lut.Apply(lut, g); b = Lut.Apply(lut, b); });
    }
}

/// <summary>0〜1 → 0〜1 の表 (間は直線で補う)</summary>
public static class Lut
{
    public const int Size = 1024;

    public static float[] Build(Func<float, float> f)
    {
        var table = new float[Size + 1];
        for (int i = 0; i <= Size; i++) table[i] = f((float)i / Size);
        return table;
    }

    public static float Apply(float[] table, float v)
    {
        v = ImageMath.Saturate(v) * Size;
        int i = (int)v;
        if (i >= Size) return table[Size];
        float t = v - i;
        return table[i] + (table[i + 1] - table[i]) * t;
    }
}
