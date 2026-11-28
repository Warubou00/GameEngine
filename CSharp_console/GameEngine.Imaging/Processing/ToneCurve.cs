namespace GameEngine.Imaging.Processing;

// ==========================================================================
// トーンカーブ (v2.3)
//   制御点 (入力 X → 出力 Y、どちらも 0〜1) を単調 3 次補間 (Fritsch-Carlson) でつなぐ。
//   点の間で上下に飛び出さない (点が単調なら曲線も単調)。点が 1 つだけ / 無い時は恒等
//   RGB 共通の曲線 → R / G / B それぞれの曲線 の順に掛ける (1024 段の表)
// ==========================================================================
public sealed class CurvePoint
{
    public float X { get; set; }
    public float Y { get; set; }

    public CurvePoint() { }
    public CurvePoint(float x, float y) { X = x; Y = y; }
}

public static class ToneCurve
{
    /// <summary>恒等 ((0,0) と (1,1))</summary>
    public static List<CurvePoint> Identity() => [new(0, 0), new(1, 1)];

    /// <summary>点を X の順に並べ、同じ X は後の方を残し、0〜1 に収める</summary>
    public static List<CurvePoint> Normalize(IEnumerable<CurvePoint> points)
    {
        var list = points.Select(p => new CurvePoint(ImageMath.Saturate(p.X), ImageMath.Saturate(p.Y))).OrderBy(p => p.X).ToList();
        for (int i = list.Count - 1; i > 0; i--)
            if (list[i].X - list[i - 1].X < 1e-5f) list.RemoveAt(i - 1);
        return list;
    }

    /// <summary>曲線の値 (点の外は端の値のまま)</summary>
    public static float Evaluate(IReadOnlyList<CurvePoint> points, float x)
    {
        var tangents = Tangents(points);
        return evaluate(points, tangents, x);
    }

    /// <summary>表 (Lut.Size + 1 段)</summary>
    public static float[] BuildLut(IReadOnlyList<CurvePoint> rawPoints)
    {
        var points = Normalize(rawPoints);
        if (points.Count < 2) return Lut.Build(v => v);
        var tangents = Tangents(points);
        return Lut.Build(v => ImageMath.Saturate(evaluate(points, tangents, v)));
    }

    /// <summary>恒等か (表を掛けなくてよい)</summary>
    public static bool IsIdentity(IReadOnlyList<CurvePoint> points) =>
        points.Count < 2 || points.All(p => MathF.Abs(p.X - p.Y) < 1e-5f) && points.Min(p => p.X) <= 1e-5f && points.Max(p => p.X) >= 1f - 1e-5f;

    /// <summary>Fritsch-Carlson の接線</summary>
    public static float[] Tangents(IReadOnlyList<CurvePoint> p)
    {
        int n = p.Count;
        var m = new float[n];
        if (n < 2) return m;
        var delta = new float[n - 1];
        for (int i = 0; i < n - 1; i++) delta[i] = (p[i + 1].Y - p[i].Y) / Math.Max(p[i + 1].X - p[i].X, 1e-6f);
        m[0] = delta[0];
        m[n - 1] = delta[n - 2];
        for (int i = 1; i < n - 1; i++) m[i] = delta[i - 1] * delta[i] <= 0 ? 0 : (delta[i - 1] + delta[i]) * 0.5f;
        for (int i = 0; i < n - 1; i++)
        {
            if (delta[i] == 0) { m[i] = 0; m[i + 1] = 0; continue; }
            float a = m[i] / delta[i], b = m[i + 1] / delta[i];
            float s = a * a + b * b;
            if (s > 9f)
            {
                float t = 3f / MathF.Sqrt(s);
                m[i] = t * a * delta[i];
                m[i + 1] = t * b * delta[i];
            }
        }
        return m;
    }

    private static float evaluate(IReadOnlyList<CurvePoint> p, float[] m, float x)
    {
        int n = p.Count;
        if (n == 0) return x;
        if (n == 1) return p[0].Y;
        if (x <= p[0].X) return p[0].Y;
        if (x >= p[n - 1].X) return p[n - 1].Y;
        int i = 0;
        while (i < n - 2 && x > p[i + 1].X) i++;
        float h = p[i + 1].X - p[i].X;
        float t = (x - p[i].X) / h;
        float t2 = t * t, t3 = t2 * t;
        return (2 * t3 - 3 * t2 + 1) * p[i].Y + (t3 - 2 * t2 + t) * h * m[i] + (-2 * t3 + 3 * t2) * p[i + 1].Y + (t3 - t2) * h * m[i + 1];
    }
}

/// <summary>トーンカーブ (RGB 共通 + R / G / B)</summary>
public sealed class ToneCurveOperation : ImageOperation
{
    public override string DisplayName => "Tone Curve";
    public override string Category => "Tone";

    public List<CurvePoint> Rgb { get; set; } = ToneCurve.Identity();
    public List<CurvePoint> Red { get; set; } = ToneCurve.Identity();
    public List<CurvePoint> Green { get; set; } = ToneCurve.Identity();
    public List<CurvePoint> Blue { get; set; } = ToneCurve.Identity();

    /// <summary>チャンネルの曲線 (null = RGB 共通)</summary>
    public List<CurvePoint> CurveOf(ColorChannel? channel) => channel switch
    {
        null => Rgb,
        ColorChannel.Red => Red,
        ColorChannel.Green => Green,
        _ => Blue,
    };

    public override void Apply(ImageBuffer src, ImageBuffer dst, ProcessContext ctx)
    {
        // RGB 共通 → チャンネルの順をまとめて 1 つの表に
        var all = ToneCurve.BuildLut(Rgb);
        var r = ToneCurve.BuildLut(Red);
        var g = ToneCurve.BuildLut(Green);
        var b = ToneCurve.BuildLut(Blue);
        var lr = Lut.Build(v => Lut.Apply(r, Lut.Apply(all, v)));
        var lg = Lut.Build(v => Lut.Apply(g, Lut.Apply(all, v)));
        var lb = Lut.Build(v => Lut.Apply(b, Lut.Apply(all, v)));
        ImageMath.ForEachPixel(src, dst, ctx, (ref float rr, ref float gg, ref float bb, ref float a) =>
        {
            rr = Lut.Apply(lr, rr);
            gg = Lut.Apply(lg, gg);
            bb = Lut.Apply(lb, bb);
        });
    }
}

/// <summary>画像の統計 (ヒストグラム)</summary>
public static class ImageStatistics
{
    /// <summary>ヒストグラム (bins 個。channel = null は輝度)。最大の値で割らない (数そのもの)</summary>
    public static int[] Histogram(ImageBuffer image, ColorChannel? channel, int bins = 256)
    {
        var result = new int[bins];
        var p = image.Pixels;
        for (int i = 0; i < p.Length; i += 4)
        {
            float v = channel switch
            {
                null => ImageMath.Luminance(p[i], p[i + 1], p[i + 2]),
                ColorChannel.Red => p[i],
                ColorChannel.Green => p[i + 1],
                _ => p[i + 2],
            };
            result[Math.Clamp((int)(ImageMath.Saturate(v) * bins), 0, bins - 1)]++;
        }
        return result;
    }
}
