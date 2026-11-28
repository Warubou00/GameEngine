namespace GameEngine.Imaging.Codecs;

// ==========================================================================
// 画像ファイルの読み込み / 書き出しの入口 (v2.3)
//   形式はファイルの先頭のバイトで決める (PNG / JPEG / BMP)。どれでもなく拡張子が .tga なら TGA
//   (TGA には決まった印が無い)。書き出しは拡張子か ImageFormat で決める
//   壊れている / 対応していない形は ImageFormatException
// ==========================================================================

/// <summary>対応している画像の形式</summary>
public enum ImageFormat
{
    Png,
    Jpeg,
    Bmp,
    Tga,
}

/// <summary>読んだ画像の情報</summary>
public sealed record ImageInfo(ImageFormat Format, int Width, int Height, int BitsPerChannel, int Channels, bool HasAlpha, string Description);

/// <summary>読み込んだ画像 (中身 + 情報)</summary>
public sealed record DecodedImage(ImageBuffer Image, ImageInfo Info);

/// <summary>画像のデータが壊れている / 対応していない形</summary>
public sealed class ImageFormatException : Exception
{
    public ImageFormatException(string message) : base(message) { }
    public ImageFormatException(string message, Exception inner) : base(message, inner) { }
}

/// <summary>書き出しの設定</summary>
public sealed class ImageSaveOptions
{
    /// <summary>JPEG の品質 (1〜100)</summary>
    public int JpegQuality { get; set; } = 90;

    /// <summary>JPEG の色差を 4:2:0 に間引く (false = 4:4:4)</summary>
    public bool JpegSubsample { get; set; } = true;

    /// <summary>TGA を RLE で縮める</summary>
    public bool TgaRle { get; set; } = true;

    /// <summary>アルファを書く (false = 捨てる。JPEG はいつも捨てる)。null = 不透明でない時だけ書く</summary>
    public bool? WriteAlpha { get; set; }

    internal bool UseAlpha(ImageBuffer image) => WriteAlpha ?? !image.IsOpaque();
}

public static class ImageCodec
{
    /// <summary>読める拡張子 (小文字、. 付き)</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".png", ".jpg", ".jpeg", ".bmp", ".tga"];

    public static bool IsSupportedExtension(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>拡張子 → 形式 (知らない拡張子は null)</summary>
    public static ImageFormat? FormatFromExtension(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => ImageFormat.Png,
        ".jpg" or ".jpeg" => ImageFormat.Jpeg,
        ".bmp" => ImageFormat.Bmp,
        ".tga" => ImageFormat.Tga,
        _ => null,
    };

    public static string ExtensionOf(ImageFormat format) => format switch
    {
        ImageFormat.Png => ".png",
        ImageFormat.Jpeg => ".jpg",
        ImageFormat.Bmp => ".bmp",
        _ => ".tga",
    };

    /// <summary>先頭のバイトから形式を決める (分からなければ null。TGA は印が無いので null)</summary>
    public static ImageFormat? Detect(ReadOnlySpan<byte> data)
    {
        if (data.Length >= 8 && data[..8].SequenceEqual(PngDecoder.Signature)) return ImageFormat.Png;
        if (data.Length >= 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF) return ImageFormat.Jpeg;
        if (data.Length >= 2 && data[0] == (byte)'B' && data[1] == (byte)'M') return ImageFormat.Bmp;
        return null;
    }

    /// <summary>ファイルを読む</summary>
    public static DecodedImage Load(string path) => Decode(File.ReadAllBytes(path), FormatFromExtension(path));

    /// <summary>メモリの中のファイルを読む。hint : 印で分からない時に使う形式 (TGA)</summary>
    public static DecodedImage Decode(ReadOnlySpan<byte> data, ImageFormat? hint = null)
    {
        var format = Detect(data) ?? hint ?? throw new ImageFormatException("unknown image format");
        try
        {
            return format switch
            {
                ImageFormat.Png => PngDecoder.Decode(data),
                ImageFormat.Jpeg => JpegDecoder.Decode(data),
                ImageFormat.Bmp => BmpDecoder.Decode(data),
                _ => TgaDecoder.Decode(data),
            };
        }
        catch (ImageFormatException) { throw; }
        catch (FbxFormatException ex) { throw new ImageFormatException($"{format} : {ex.Message}", ex); }   // zlib が壊れている
        catch (Exception ex) when (ex is IndexOutOfRangeException or ArgumentOutOfRangeException or ArgumentException or OverflowException)
        {
            throw new ImageFormatException($"{format} : broken data ({ex.Message})", ex);   // 足りないデータを読もうとした
        }
    }

    /// <summary>ファイルに書く (形式は拡張子で決める)</summary>
    public static void Save(string path, ImageBuffer image, ImageSaveOptions? options = null)
    {
        var format = FormatFromExtension(path) ?? throw new ArgumentException($"unsupported extension : {Path.GetExtension(path)}", nameof(path));
        var bytes = Encode(image, format, options);
        // 途中で失敗しても元のファイルが壊れないように、隣に書いてから入れ替える
        // (名前を . で始める : エンジンのアセットの一覧は . で始まるファイルを無視する)
        var temp = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(path))!, "." + Path.GetFileName(path) + ".tmp");
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>メモリに書く</summary>
    public static byte[] Encode(ImageBuffer image, ImageFormat format, ImageSaveOptions? options = null)
    {
        options ??= new ImageSaveOptions();
        return format switch
        {
            ImageFormat.Png => PngEncoder.Encode(image, options),
            ImageFormat.Jpeg => JpegEncoder.Encode(image, options),
            ImageFormat.Bmp => BmpEncoder.Encode(image, options),
            _ => TgaEncoder.Encode(image, options),
        };
    }
}
