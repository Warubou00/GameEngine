namespace GameEngine.Imaging.Processing;

// ==========================================================================
// 周波数領域の処理 (v2.3)
//   Fft2D : 2 の累乗の大きさの 2 次元 FFT (Radix-2。行 → 列。行と列はそれぞれ並列)
//   FrequencyFilterOperation : RGB それぞれを FFT → 中央 (= 低い周波数) に黒丸のマスク → 逆 FFT
//     画像は 2 の累乗まで鏡のように折り返して広げる (端の段差で十字の筋が出ないように)。戻した後に元の大きさに切る
//     丸の半径は元の画像のナイキスト周波数 (0.5 周期 / ピクセル) に対する割合。縮めたプレビュー (Scale < 1) では
//     同じ模様 (画像の幅あたりの周期) が 1 / Scale 倍の周波数になるので、半径を 1 / Scale 倍して原寸と同じ所を消す
// ==========================================================================
public static class Fft2D
{
    private const int ColumnBlock = 16;

    public static int NextPowerOfTwo(int n)
    {
        int p = 1;
        while (p < n) p <<= 1;
        return p;
    }

    /// <summary>2 次元 FFT (その場で)。inverse = true は逆 (1 / (w * h) も掛ける)</summary>
    public static void Transform(float[] re, float[] im, int width, int height, bool inverse, CancellationToken cancellation = default)
    {
        if ((width & (width - 1)) != 0 || (height & (height - 1)) != 0) throw new ArgumentException("size must be a power of two");
        var options = new ParallelOptions { CancellationToken = cancellation };
        var rowTables = Tables.Of(width);
        Parallel.For(0, height, options, y => transform1D(re.AsSpan(y * width, width), im.AsSpan(y * width, width), rowTables, inverse));
        // 列 : ColumnBlock 本ずつまとめて写す (1 本ずつだと飛び飛びに読むので遅い)
        var columnTables = Tables.Of(height);
        int blocks = (width + ColumnBlock - 1) / ColumnBlock;
        Parallel.For(0, blocks, options, () => (new float[ColumnBlock * height], new float[ColumnBlock * height]), (block, _, buffers) =>
        {
            var (cr, ci) = buffers;
            int x0 = block * ColumnBlock, count = Math.Min(ColumnBlock, width - x0);
            for (int y = 0; y < height; y++)
                for (int k = 0; k < count; k++) { cr[k * height + y] = re[y * width + x0 + k]; ci[k * height + y] = im[y * width + x0 + k]; }
            for (int k = 0; k < count; k++) transform1D(cr.AsSpan(k * height, height), ci.AsSpan(k * height, height), columnTables, inverse);
            for (int y = 0; y < height; y++)
                for (int k = 0; k < count; k++) { re[y * width + x0 + k] = cr[k * height + y]; im[y * width + x0 + k] = ci[k * height + y]; }
            return buffers;
        }, _ => { });
        if (inverse)
        {
            float scale = 1f / ((float)width * height);
            for (int i = 0; i < re.Length; i++) { re[i] *= scale; im[i] *= scale; }
        }
    }

    private sealed class Tables
    {
        public readonly int[] Reverse;
        public readonly float[] Cos, Sin;

        private Tables(int n)
        {
            Reverse = new int[n];
            int bits = 0;
            while (1 << bits < n) bits++;
            for (int i = 0; i < n; i++)
            {
                int r = 0;
                for (int b = 0; b < bits; b++) if ((i & (1 << b)) != 0) r |= 1 << (bits - 1 - b);
                Reverse[i] = r;
            }
            Cos = new float[n / 2 + 1];
            Sin = new float[n / 2 + 1];
            for (int i = 0; i <= n / 2; i++) { Cos[i] = (float)Math.Cos(2 * Math.PI * i / n); Sin[i] = (float)Math.Sin(2 * Math.PI * i / n); }
        }

        private static readonly Dictionary<int, Tables> Cache = new();

        public static Tables Of(int n)
        {
            lock (Cache)
            {
                if (!Cache.TryGetValue(n, out var t)) Cache[n] = t = new Tables(n);
                return t;
            }
        }
    }

    private static void transform1D(Span<float> re, Span<float> im, Tables tables, bool inverse)
    {
        int n = re.Length;
        if (n <= 1) return;
        var reverse = tables.Reverse;
        for (int i = 0; i < n; i++)
        {
            int j = reverse[i];
            if (j > i) { (re[i], re[j]) = (re[j], re[i]); (im[i], im[j]) = (im[j], im[i]); }
        }
        float sign = inverse ? 1f : -1f;
        for (int size = 2; size <= n; size <<= 1)
        {
            int half = size >> 1, step = n / size;
            for (int start = 0; start < n; start += size)
            {
                for (int k = 0; k < half; k++)
                {
                    float wr = tables.Cos[k * step], wi = sign * tables.Sin[k * step];
                    int a = start + k, b = a + half;
                    float tr = re[b] * wr - im[b] * wi, ti = re[b] * wi + im[b] * wr;
                    re[b] = re[a] - tr; im[b] = im[a] - ti;
                    re[a] += tr; im[a] += ti;
                }
            }
        }
    }

    /// <summary>1 チャンネルを 2 の累乗の大きさへ鏡で広げる</summary>
    internal static void FillPadded(ImageBuffer src, int channel, float[] re, float[] im, int n, int m) => FillPadded(src, channel, -2, re, im, n, m);

    /// <summary>re に channel、im に imagChannel (-2 = 0) を入れる。-1 = 輝度</summary>
    internal static void FillPadded(ImageBuffer src, int channel, int imagChannel, float[] re, float[] im, int n, int m)
    {
        int width = src.Width, height = src.Height;
        var s = src.Pixels;
        Parallel.For(0, m, y =>
        {
            int sy = mirror(y, height);
            for (int x = 0; x < n; x++)
            {
                int sx = mirror(x, width);
                int i = (sy * width + sx) * 4;
                re[y * n + x] = channel < 0 ? ImageMath.Luminance(s[i], s[i + 1], s[i + 2]) : s[i + channel];
                im[y * n + x] = imagChannel >= 0 ? s[i + imagChannel] : 0f;
            }
        });
    }

    private static int mirror(int i, int size)
    {
        if (i < size) return i;
        int m = 2 * size - 1 - i;
        return Math.Clamp(m, 0, size - 1);
    }

    /// <summary>周波数 (中心からの距離。1 = ナイキスト) : 並び (u, v) の (FFT の並び。0 が直流)</summary>
    internal static float RadiusOf(int u, int v, int n, int m)
    {
        float fu = (u < n / 2 ? u : u - n) / (float)n, fv = (v < m / 2 ? v : v - m) / (float)m;
        return MathF.Sqrt(fu * fu + fv * fv) / 0.5f;
    }

    /// <summary>
    /// 振幅のスペクトルの画像 (輝度。log(1 + |F|) を最大で割る。直流を真ん中に)。
    /// 縦横を同じ大きさ (size) にして出す = 丸は丸に見える
    /// </summary>
    public static ImageBuffer Spectrum(ImageBuffer src, int size, CancellationToken cancellation = default)
    {
        int n = NextPowerOfTwo(src.Width), m = NextPowerOfTwo(src.Height);
        var re = new float[n * m];
        var im = new float[n * m];
        FillPadded(src, -1, re, im, n, m);
        Transform(re, im, n, m, false, cancellation);
        var magnitude = new float[n * m];
        float max = 0;
        for (int i = 0; i < magnitude.Length; i++)
        {
            magnitude[i] = MathF.Log(1f + MathF.Sqrt(re[i] * re[i] + im[i] * im[i]));
            if (magnitude[i] > max) max = magnitude[i];
        }
        var result = new ImageBuffer(size, size);
        float inv = max > 0 ? 1f / max : 0f;
        Parallel.For(0, size, y =>
        {
            for (int x = 0; x < size; x++)
            {
                // 表示の (x, y) → 周波数 (-0.5〜0.5) → FFT の並び
                int u = ((int)((x + 0.5f) / size * n) + n / 2) % n;
                int v = ((int)((y + 0.5f) / size * m) + m / 2) % m;
                float value = magnitude[v * n + u] * inv;
                result.SetPixel(x, y, value, value, value, 1f);
            }
        });
        return result;
    }
}

/// <summary>周波数フィルター : 中央 (低い周波数) に黒丸を置く = ハイパス。Invert = 丸の外を消す = ローパス</summary>
public sealed class FrequencyFilterOperation : ImageOperation
{
    public override string DisplayName => "Frequency Filter";
    public override string Category => "Frequency";

    /// <summary>黒丸の半径 (0〜1。1 = 元の画像のナイキスト周波数)</summary>
    public float Radius { get; set; } = 0.05f;

    /// <summary>丸の縁をぼかす幅 (半径と同じ単位。0 = くっきり。リンギングを減らす)</summary>
    public float Feather { get; set; } = 0.02f;

    /// <summary>true = 丸の外を消す (ローパス)</summary>
    public bool Invert { get; set; }

    /// <summary>直流 (平均の明るさ) は残す (ハイパスで真っ黒にならないように)</summary>
    public bool KeepMean { get; set; }

    /// <summary>マスクの値 (1 = 通す / 0 = 消す)。r : 今の画像の周波数 (1 = ナイキスト)。scale : 元の画像に対する今の画像の大きさ</summary>
    public float MaskAt(float r, float scale = 1f)
    {
        float radius = Radius / Math.Max(scale, 1e-6f), feather = Feather / Math.Max(scale, 1e-6f);
        float inner = radius - feather * 0.5f, outer = radius + feather * 0.5f;
        float pass = feather <= 0f ? (r >= radius ? 1f : 0f) : smoothstep(inner, outer, r);
        if (Radius <= 0f) pass = 1f;   // 半径 0 = 何も消さない
        return Invert ? 1f - pass : pass;
    }

    private static float smoothstep(float a, float b, float x)
    {
        float t = ImageMath.Saturate((x - a) / (b - a));
        return t * t * (3f - 2f * t);
    }

    public override void Apply(ImageBuffer src, ImageBuffer dst, ProcessContext ctx)
    {
        int width = src.Width, height = src.Height;
        int n = Fft2D.NextPowerOfTwo(width), m = Fft2D.NextPowerOfTwo(height);
        var mask = new float[n * m];
        for (int v = 0; v < m; v++)
            for (int u = 0; u < n; u++) mask[v * n + u] = MaskAt(Fft2D.RadiusOf(u, v, n, m), ctx.Scale);
        if (KeepMean) mask[0] = 1f;

        var re = new float[n * m];
        var im = new float[n * m];
        var d = dst.Pixels;
        // マスクは実数で原点に対して対称なので、2 つのチャンネルを 実部 / 虚部 に入れて 1 回で済ませる
        // (R + iG → 戻した実部 = R、虚部 = G。B は 1 回で)
        foreach (var (c0, c1) in new[] { (0, 1), (2, -2) })
        {
            ctx.ThrowIfCanceled();
            Fft2D.FillPadded(src, c0, c1, re, im, n, m);
            Fft2D.Transform(re, im, n, m, false, ctx.Cancellation);
            for (int i = 0; i < re.Length; i++) { re[i] *= mask[i]; im[i] *= mask[i]; }
            Fft2D.Transform(re, im, n, m, true, ctx.Cancellation);
            Parallel.For(0, height, ctx.Parallel, y =>
            {
                for (int x = 0; x < width; x++)
                {
                    d[(y * width + x) * 4 + c0] = re[y * n + x];
                    if (c1 >= 0) d[(y * width + x) * 4 + c1] = im[y * n + x];
                }
            });
        }
        var s = src.Pixels;
        for (int i = 3; i < d.Length; i += 4) d[i] = s[i];
    }
}
