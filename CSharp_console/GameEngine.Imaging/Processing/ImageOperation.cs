using System.Text.Json.Serialization;

namespace GameEngine.Imaging.Processing;

// ==========================================================================
// 画像の処理の 1 つ (v2.3)
//   Apply(src, dst) : src を読んで dst (同じ大きさ) に書く。src は書き換えない
//   パラメータは public なプロパティ (JSON に書ける。Undo / Redo と処理の並びの保存に使う)
//   Enabled = false の処理は飛ばす。Amount = 元との混ぜ具合 (0 = 元のまま / 1 = 全部この処理)
//   ピクセルの単位のパラメータ (ぼかしの半径 / モザイクの大きさ など) は、元の画像のピクセルで持ち、
//   縮めたプレビューでは ProcessContext.Scale を掛けて使う (縮めても見た目が同じになるように)
// ==========================================================================
[JsonPolymorphic(TypeDiscriminatorPropertyName = "type")]
[JsonDerivedType(typeof(GrayscaleOperation), "Grayscale")]
[JsonDerivedType(typeof(ChannelScaleOperation), "ChannelScale")]
[JsonDerivedType(typeof(PosterizeOperation), "Posterize")]
[JsonDerivedType(typeof(SolarizeOperation), "Solarize")]
[JsonDerivedType(typeof(InvertOperation), "Invert")]
[JsonDerivedType(typeof(SepiaOperation), "Sepia")]
[JsonDerivedType(typeof(GammaOperation), "Gamma")]
[JsonDerivedType(typeof(GaussianBlurOperation), "GaussianBlur")]
[JsonDerivedType(typeof(SharpenOperation), "Sharpen")]
[JsonDerivedType(typeof(SobelOperation), "Sobel")]
[JsonDerivedType(typeof(EmbossOperation), "Emboss")]
[JsonDerivedType(typeof(MosaicOperation), "Mosaic")]
[JsonDerivedType(typeof(ChromaticAberrationOperation), "ChromaticAberration")]
[JsonDerivedType(typeof(FrequencyFilterOperation), "FrequencyFilter")]
[JsonDerivedType(typeof(ToneCurveOperation), "ToneCurve")]
[JsonDerivedType(typeof(NormalMapOperation), "NormalMap")]
public abstract class ImageOperation
{
    /// <summary>表示する名前</summary>
    [JsonIgnore]
    public abstract string DisplayName { get; }

    /// <summary>分類 (エディターのメニュー : Color / Filter / Frequency / Tone / Normal)</summary>
    [JsonIgnore]
    public abstract string Category { get; }

    public bool Enabled { get; set; } = true;

    /// <summary>元の画像と混ぜる量 (0〜1)</summary>
    public float Amount { get; set; } = 1f;

    /// <summary>src → dst (同じ大きさ)。ctx.Cancellation が来たら OperationCanceledException</summary>
    public abstract void Apply(ImageBuffer src, ImageBuffer dst, ProcessContext ctx);

    /// <summary>同じパラメータの複製 (JSON を通す)</summary>
    public ImageOperation Clone() => ImagePipeline.CloneOperation(this);

    /// <summary>種類の一覧 (メニュー用。表示の順)</summary>
    public static IReadOnlyList<Func<ImageOperation>> Factories { get; } =
    [
        () => new GrayscaleOperation(), () => new ChannelScaleOperation(), () => new PosterizeOperation(), () => new SolarizeOperation(),
        () => new InvertOperation(), () => new SepiaOperation(), () => new GammaOperation(),
        () => new GaussianBlurOperation(), () => new SharpenOperation(), () => new SobelOperation(), () => new EmbossOperation(),
        () => new MosaicOperation(), () => new ChromaticAberrationOperation(),
        () => new FrequencyFilterOperation(), () => new ToneCurveOperation(), () => new NormalMapOperation(),
    ];
}

/// <summary>処理の実行の情報</summary>
public sealed class ProcessContext
{
    /// <summary>元の画像に対する今の画像の大きさの比 (縮めたプレビュー = 1 より小さい)</summary>
    public float Scale { get; init; } = 1f;

    public CancellationToken Cancellation { get; init; }

    public static ProcessContext Default { get; } = new();

    public void ThrowIfCanceled() => Cancellation.ThrowIfCancellationRequested();

    /// <summary>並列の設定 (キャンセルを効かせる)</summary>
    internal ParallelOptions Parallel => new() { CancellationToken = Cancellation };
}

/// <summary>RGB の 1 つ</summary>
public enum ColorChannel
{
    Red,
    Green,
    Blue,
}

/// <summary>処理で共通に使う物</summary>
public static class ImageMath
{
    /// <summary>輝度 (グレースケールの式。R * 0.30 + G * 0.59 + B * 0.11)</summary>
    public static float Luminance(float r, float g, float b) => r * 0.30f + g * 0.59f + b * 0.11f;

    public static float Saturate(float v) => v < 0f ? 0f : v > 1f ? 1f : v;

    /// <summary>端を延ばして読む</summary>
    public static float Read(ImageBuffer image, int x, int y, int channel)
    {
        x = Math.Clamp(x, 0, image.Width - 1);
        y = Math.Clamp(y, 0, image.Height - 1);
        return image.Pixels[(y * image.Width + x) * 4 + channel];
    }

    /// <summary>バイリニアで読む (端を延ばす。座標はピクセルの中心が整数)</summary>
    public static float Sample(ImageBuffer image, float x, float y, int channel)
    {
        x = Math.Clamp(x, 0f, image.Width - 1);
        y = Math.Clamp(y, 0f, image.Height - 1);
        int x0 = (int)x, y0 = (int)y;
        int x1 = Math.Min(x0 + 1, image.Width - 1), y1 = Math.Min(y0 + 1, image.Height - 1);
        float tx = x - x0, ty = y - y0;
        var p = image.Pixels;
        int w = image.Width;
        float top = p[(y0 * w + x0) * 4 + channel] * (1 - tx) + p[(y0 * w + x1) * 4 + channel] * tx;
        float bottom = p[(y1 * w + x0) * 4 + channel] * (1 - tx) + p[(y1 * w + x1) * 4 + channel] * tx;
        return top * (1 - ty) + bottom * ty;
    }

    /// <summary>1 ピクセルずつの処理 (RGBA → RGBA) を行ごとに並列で</summary>
    public static void ForEachPixel(ImageBuffer src, ImageBuffer dst, ProcessContext ctx, PixelFunc func)
    {
        int width = src.Width;
        System.Threading.Tasks.Parallel.For(0, src.Height, ctx.Parallel, y =>
        {
            var s = src.Pixels;
            var d = dst.Pixels;
            for (int i = y * width * 4, end = i + width * 4; i < end; i += 4)
            {
                float r = s[i], g = s[i + 1], b = s[i + 2], a = s[i + 3];
                func(ref r, ref g, ref b, ref a);
                d[i] = r; d[i + 1] = g; d[i + 2] = b; d[i + 3] = a;
            }
        });
    }

    public delegate void PixelFunc(ref float r, ref float g, ref float b, ref float a);
}
