using System.Runtime.CompilerServices;

namespace GameEngine.Imaging;

// ==========================================================================
// 作業用の画像 (v2.3)
//   1 ピクセル = float の R, G, B, A (0〜1。ファイルの値のまま = ガンマ空間。リニアには直さない)
//   並びは左上から右へ、上の行から下へ (Pixels[(y * Width + x) * 4 + c])
//   float にしているのは、処理を何段も重ねた時に 8bit で丸めて段差が出ないように (16bit の PNG もそのまま持てる)
// ==========================================================================
public sealed class ImageBuffer
{
    public int Width { get; }
    public int Height { get; }

    /// <summary>RGBA の並び (長さ = Width * Height * 4)</summary>
    public float[] Pixels { get; }

    public ImageBuffer(int width, int height)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), $"bad image size {width}x{height}");
        if ((long)width * height > 1L << 28) throw new ArgumentOutOfRangeException(nameof(width), $"image too large {width}x{height}");
        Width = width;
        Height = height;
        Pixels = new float[width * height * 4];
    }

    public ImageBuffer(int width, int height, float[] pixels)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width), $"bad image size {width}x{height}");
        if (pixels.Length != width * height * 4) throw new ArgumentException("pixels length must be width * height * 4", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }

    public int PixelCount => Width * Height;

    /// <summary>1 行分 (RGBA * Width)</summary>
    public Span<float> Row(int y) => Pixels.AsSpan(y * Width * 4, Width * 4);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int IndexOf(int x, int y) => (y * Width + x) * 4;

    public (float R, float G, float B, float A) GetPixel(int x, int y)
    {
        var i = IndexOf(x, y);
        return (Pixels[i], Pixels[i + 1], Pixels[i + 2], Pixels[i + 3]);
    }

    public void SetPixel(int x, int y, float r, float g, float b, float a = 1f)
    {
        var i = IndexOf(x, y);
        Pixels[i] = r; Pixels[i + 1] = g; Pixels[i + 2] = b; Pixels[i + 3] = a;
    }

    public ImageBuffer Clone() => new(Width, Height, (float[])Pixels.Clone());

    public void CopyTo(ImageBuffer destination)
    {
        if (destination.Width != Width || destination.Height != Height) throw new ArgumentException("size mismatch", nameof(destination));
        Pixels.AsSpan().CopyTo(destination.Pixels);
    }

    /// <summary>全部のピクセルのアルファが 1 か</summary>
    public bool IsOpaque()
    {
        var p = Pixels;
        for (int i = 3; i < p.Length; i += 4) if (p[i] < 1f) return false;
        return true;
    }

    // ---------------------------------------------------------------------
    // 8bit との変換
    // ---------------------------------------------------------------------

    private static readonly float[] ByteToFloat = Enumerable.Range(0, 256).Select(v => v / 255f).ToArray();

    /// <summary>0〜255 → 0〜1 の表 (デコーダーが使う)</summary>
    internal static ReadOnlySpan<float> ByteTable => ByteToFloat;

    /// <summary>RGBA8 (長さ width * height * 4) から作る</summary>
    public static ImageBuffer FromRgba8(int width, int height, ReadOnlySpan<byte> rgba)
    {
        var image = new ImageBuffer(width, height);
        if (rgba.Length < image.Pixels.Length) throw new ArgumentException("rgba is too small", nameof(rgba));
        var table = ByteToFloat;
        var p = image.Pixels;
        for (int i = 0; i < p.Length; i++) p[i] = table[rgba[i]];
        return image;
    }

    /// <summary>RGBA8 にする (0〜1 の外は切る。四捨五入)</summary>
    public byte[] ToRgba8()
    {
        var result = new byte[Pixels.Length];
        ToRgba8(result);
        return result;
    }

    public void ToRgba8(Span<byte> destination)
    {
        if (destination.Length < Pixels.Length) throw new ArgumentException("destination is too small", nameof(destination));
        var p = Pixels;
        for (int i = 0; i < p.Length; i++) destination[i] = ToByte(p[i]);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte ToByte(float v)
    {
        var x = v * 255f + 0.5f;
        return x <= 0f ? (byte)0 : x >= 255f ? (byte)255 : (byte)x;
    }

    // ---------------------------------------------------------------------
    // 大きさを変える (プレビュー用の縮小など)
    // ---------------------------------------------------------------------

    /// <summary>長い辺が maxSize 以下になるように縮める (面積の平均。もう小さければ自分を返す)</summary>
    public ImageBuffer DownscaleToFit(int maxSize)
    {
        var scale = Math.Min((double)maxSize / Width, (double)maxSize / Height);
        if (scale >= 1.0) return this;
        int width = Math.Max(1, (int)Math.Round(Width * scale)), height = Math.Max(1, (int)Math.Round(Height * scale));
        return Resize(width, height);
    }

    /// <summary>大きさを変える (縮める時は面積の平均、広げる時はバイリニア)</summary>
    public ImageBuffer Resize(int width, int height)
    {
        if (width == Width && height == Height) return Clone();
        var result = new ImageBuffer(width, height);
        var src = Pixels;
        var dst = result.Pixels;
        double sx = (double)Width / width, sy = (double)Height / height;
        bool shrink = sx >= 1.0 && sy >= 1.0;
        int srcWidth = Width, srcHeight = Height;
        Parallel.For(0, height, y =>
        {
            for (int x = 0; x < width; x++)
            {
                var o = (y * width + x) * 4;
                if (shrink)
                {
                    // 元の画像の [x0, x1) x [y0, y1) の重みつき平均
                    double x0 = x * sx, x1 = x0 + sx, y0 = y * sy, y1 = y0 + sy;
                    double r = 0, g = 0, b = 0, a = 0, total = 0;
                    for (int iy = (int)y0; iy < Math.Min(srcHeight, (int)Math.Ceiling(y1)); iy++)
                    {
                        var wy = Math.Min(y1, iy + 1) - Math.Max(y0, iy);
                        for (int ix = (int)x0; ix < Math.Min(srcWidth, (int)Math.Ceiling(x1)); ix++)
                        {
                            var w = wy * (Math.Min(x1, ix + 1) - Math.Max(x0, ix));
                            var i = (iy * srcWidth + ix) * 4;
                            r += src[i] * w; g += src[i + 1] * w; b += src[i + 2] * w; a += src[i + 3] * w;
                            total += w;
                        }
                    }
                    if (total > 0) { dst[o] = (float)(r / total); dst[o + 1] = (float)(g / total); dst[o + 2] = (float)(b / total); dst[o + 3] = (float)(a / total); }
                }
                else
                {
                    var fx = Math.Clamp((x + 0.5) * sx - 0.5, 0, srcWidth - 1);
                    var fy = Math.Clamp((y + 0.5) * sy - 0.5, 0, srcHeight - 1);
                    int ix = (int)fx, iy = (int)fy;
                    int ix1 = Math.Min(ix + 1, srcWidth - 1), iy1 = Math.Min(iy + 1, srcHeight - 1);
                    float tx = (float)(fx - ix), ty = (float)(fy - iy);
                    for (int c = 0; c < 4; c++)
                    {
                        var top = src[(iy * srcWidth + ix) * 4 + c] * (1 - tx) + src[(iy * srcWidth + ix1) * 4 + c] * tx;
                        var bottom = src[(iy1 * srcWidth + ix) * 4 + c] * (1 - tx) + src[(iy1 * srcWidth + ix1) * 4 + c] * tx;
                        dst[o + c] = top * (1 - ty) + bottom * ty;
                    }
                }
            }
        });
        return result;
    }
}
