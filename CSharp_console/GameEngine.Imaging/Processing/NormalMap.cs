namespace GameEngine.Imaging.Processing;

// ==========================================================================
// 法線マップの生成 (v2.3)
//   輝度 (R * 0.30 + G * 0.59 + B * 0.11) を高さとみなし、勾配から法線を作る : n = normalize(-dh/dx * 強さ, ±dh/dy * 強さ, 1)
//   出力 RGB = n * 0.5 + 0.5、A = 1。平らな所 = (0.5, 0.5, 1) (薄い青)
//   Y の向き : DirectX (既定。このエンジンと同じ。緑 = 画像の下向き) / OpenGL (緑 = 上向き)
//   縮めたプレビューでは 1 ピクセルの高さの差が大きくなるので、強さに Scale を掛けて原寸と同じ傾きにする
// ==========================================================================

public enum NormalGradient
{
    Sobel,          // 3x3 (なめらか)
    CentralDifference,   // 左右 / 上下の差 (細かい所が出る)
}

public enum NormalConvention
{
    DirectX,
    OpenGL,
}

public sealed class NormalMapSettings
{
    public float Strength { get; set; } = 2f;

    /// <summary>高さをぼかす (元の画像のピクセル。0 = ぼかさない)</summary>
    public float BlurSigma { get; set; }

    public NormalGradient Gradient { get; set; } = NormalGradient.Sobel;
    public NormalConvention Convention { get; set; } = NormalConvention.DirectX;

    /// <summary>暗い所を高くする</summary>
    public bool InvertHeight { get; set; }
}

public static class NormalMap
{
    /// <summary>src の輝度から法線マップを作って dst に書く</summary>
    public static void Generate(ImageBuffer src, ImageBuffer dst, NormalMapSettings settings, ProcessContext ctx)
    {
        int width = src.Width, height = src.Height;
        var heightMap = SobelOperation.luminancePlane(src, ctx);
        if (settings.InvertHeight) for (int i = 0; i < heightMap.Length; i++) heightMap[i] = 1f - heightMap[i];
        float sigma = settings.BlurSigma * ctx.Scale;
        if (sigma >= 0.2f)
        {
            // 高さだけをぼかす (RGBA のぼかしを使う。R に入れて戻す)
            var temp = new ImageBuffer(width, height);
            for (int i = 0; i < heightMap.Length; i++) temp.Pixels[i * 4] = heightMap[i];
            GaussianBlurOperation.Blur(temp, temp, sigma, ctx);
            for (int i = 0; i < heightMap.Length; i++) heightMap[i] = temp.Pixels[i * 4];
        }

        float strength = settings.Strength * ctx.Scale;   // 縮めた画像は 1 ピクセルの差が 1 / Scale 倍になる
        bool sobel = settings.Gradient == NormalGradient.Sobel;
        float ySign = settings.Convention == NormalConvention.DirectX ? -1f : 1f;
        var d = dst.Pixels;
        Parallel.For(0, height, ctx.Parallel, y =>
        {
            int ym = Math.Max(y - 1, 0), yp = Math.Min(y + 1, height - 1);
            for (int x = 0; x < width; x++)
            {
                int xm = Math.Max(x - 1, 0), xp = Math.Min(x + 1, width - 1);
                float h(int px, int py) => heightMap[py * width + px];
                float dx, dy;   // 1 ピクセルあたりの高さの変化 (dy は画像の下向き)
                if (sobel)
                {
                    dx = (h(xp, ym) + 2 * h(xp, y) + h(xp, yp) - h(xm, ym) - 2 * h(xm, y) - h(xm, yp)) / 8f;
                    dy = (h(xm, yp) + 2 * h(x, yp) + h(xp, yp) - h(xm, ym) - 2 * h(x, ym) - h(xp, ym)) / 8f;
                }
                else
                {
                    dx = (h(xp, y) - h(xm, y)) * 0.5f;
                    dy = (h(x, yp) - h(x, ym)) * 0.5f;
                }
                // OpenGL (v は上向き) : n.y = -dh/dv = +dh/dy。DirectX (下向き) : n.y = -dh/dy
                float nx = -dx * strength, ny = ySign * dy * strength, nz = 1f;
                float len = MathF.Sqrt(nx * nx + ny * ny + nz * nz);
                int o = (y * width + x) * 4;
                d[o] = nx / len * 0.5f + 0.5f;
                d[o + 1] = ny / len * 0.5f + 0.5f;
                d[o + 2] = nz / len * 0.5f + 0.5f;
                d[o + 3] = 1f;
            }
        });
    }
}

/// <summary>法線マップの生成 (処理の並びに入れる = 保存できる)</summary>
public sealed class NormalMapOperation : ImageOperation
{
    public override string DisplayName => "Normal Map";
    public override string Category => "Normal";

    public NormalMapSettings Settings { get; set; } = new();

    public override void Apply(ImageBuffer src, ImageBuffer dst, ProcessContext ctx) => NormalMap.Generate(src, dst, Settings, ctx);
}
