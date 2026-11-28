using System.Buffers.Binary;

namespace GameEngine.Imaging.Codecs;

// ==========================================================================
// PNG の読み込み (v2.3。ISO/IEC 15948 / W3C PNG)
//   カラー型 : 0 グレー / 2 RGB / 3 パレット / 4 グレー + アルファ / 6 RGBA。bit 深度 1/2/4/8/16
//   フィルター 5 種 (None / Sub / Up / Average / Paeth)、Adam7 のインターレース、tRNS (透明色 / パレットのアルファ)
//   チャンクの CRC を確かめる。知らない補助チャンク (gAMA / iCCP など) は読み飛ばす (色の補正はしない)
// ==========================================================================
internal static class PngDecoder
{
    public static ReadOnlySpan<byte> Signature => [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];

    // Adam7 : (開始 x, 開始 y, x の間隔, y の間隔)
    private static readonly (int X, int Y, int DX, int DY)[] Passes =
        [(0, 0, 8, 8), (4, 0, 8, 8), (0, 4, 4, 8), (2, 0, 4, 4), (0, 2, 2, 4), (1, 0, 2, 2), (0, 1, 1, 2)];

    public static DecodedImage Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 8 || !data[..8].SequenceEqual(Signature)) throw new ImageFormatException("PNG : bad signature");
        int width = 0, height = 0, bitDepth = 0, colorType = -1, interlace = 0;
        byte[]? palette = null;           // RGB の並び
        byte[]? paletteAlpha = null;      // tRNS (カラー型 3)
        int[]? transparent = null;        // tRNS (カラー型 0 / 2 : 透明にする値)
        using var idat = new MemoryStream();
        bool seenHeader = false, seenEnd = false;

        int pos = 8;
        while (pos + 12 <= data.Length)
        {
            int length = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[pos..]));
            if (pos + 12 + (long)length > data.Length) throw new ImageFormatException("PNG : truncated chunk");
            var type = data.Slice(pos + 4, 4);
            var body = data.Slice(pos + 8, length);
            var storedCrc = BinaryPrimitives.ReadUInt32BigEndian(data[(pos + 8 + length)..]);
            if (Crc32.Compute(data.Slice(pos + 4, length + 4)) != storedCrc)
                throw new ImageFormatException($"PNG : CRC mismatch in {System.Text.Encoding.ASCII.GetString(type)}");
            pos += 12 + length;

            var name = System.Text.Encoding.ASCII.GetString(type);
            if (!seenHeader && name != "IHDR") throw new ImageFormatException("PNG : IHDR must come first");
            switch (name)
            {
                case "IHDR":
                    if (length != 13) throw new ImageFormatException("PNG : bad IHDR");
                    width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(body));
                    height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(body[4..]));
                    bitDepth = body[8];
                    colorType = body[9];
                    if (body[10] != 0 || body[11] != 0) throw new ImageFormatException("PNG : unknown compression / filter method");
                    interlace = body[12];
                    if (interlace > 1) throw new ImageFormatException("PNG : unknown interlace method");
                    validate(colorType, bitDepth);
                    if (width <= 0 || height <= 0) throw new ImageFormatException("PNG : bad size");
                    seenHeader = true;
                    break;
                case "PLTE":
                    if (length % 3 != 0 || length == 0 || length > 768) throw new ImageFormatException("PNG : bad PLTE");
                    palette = body.ToArray();
                    break;
                case "tRNS":
                    if (colorType == 3) paletteAlpha = body.ToArray();
                    else if (colorType == 0 && length >= 2) transparent = [BinaryPrimitives.ReadUInt16BigEndian(body)];
                    else if (colorType == 2 && length >= 6)
                        transparent = [BinaryPrimitives.ReadUInt16BigEndian(body), BinaryPrimitives.ReadUInt16BigEndian(body[2..]), BinaryPrimitives.ReadUInt16BigEndian(body[4..])];
                    break;
                case "IDAT":
                    idat.Write(body);
                    break;
                case "IEND":
                    seenEnd = true;
                    break;
                default:
                    if ((type[0] & 0x20) == 0) throw new ImageFormatException($"PNG : unknown critical chunk {name}");
                    break;   // 補助チャンクは読み飛ばす
            }
            if (seenEnd) break;
        }
        if (!seenHeader) throw new ImageFormatException("PNG : no IHDR");
        if (idat.Length == 0) throw new ImageFormatException("PNG : no IDAT");
        if (colorType == 3 && palette == null) throw new ImageFormatException("PNG : palette image without PLTE");

        int channels = colorType switch { 0 => 1, 2 => 3, 3 => 1, 4 => 2, _ => 4 };
        int bitsPerPixel = channels * bitDepth;
        int filterBytes = Math.Max(1, bitsPerPixel / 8);

        long expected = 0;
        if (interlace == 0) expected = (long)height * (1 + rowBytes(width, bitsPerPixel));
        else
            foreach (var p in Passes)
            {
                int pw = passSize(width, p.X, p.DX), ph = passSize(height, p.Y, p.DY);
                if (pw > 0 && ph > 0) expected += (long)ph * (1 + rowBytes(pw, bitsPerPixel));
            }
        if (expected > int.MaxValue) throw new ImageFormatException("PNG : image too large");

        var raw = Inflate.Zlib(idat.GetBuffer().AsSpan(0, (int)idat.Length));
        if (raw.Length < expected) throw new ImageFormatException($"PNG : image data too short ({raw.Length} / {expected})");

        var image = new ImageBuffer(width, height);
        var context = new RowContext(colorType, bitDepth, palette, paletteAlpha, transparent);
        if (interlace == 0)
        {
            unfilter(raw, 0, width, height, bitsPerPixel, filterBytes);
            int stride = 1 + rowBytes(width, bitsPerPixel);
            Parallel.For(0, height, y => context.Convert(raw.AsSpan(y * stride + 1, stride - 1), width, image.Pixels, y * width * 4, 4));
        }
        else
        {
            int offset = 0;
            foreach (var p in Passes)
            {
                int pw = passSize(width, p.X, p.DX), ph = passSize(height, p.Y, p.DY);
                if (pw == 0 || ph == 0) continue;
                int stride = 1 + rowBytes(pw, bitsPerPixel);
                unfilter(raw, offset, pw, ph, bitsPerPixel, filterBytes);
                for (int y = 0; y < ph; y++)
                {
                    int iy = p.Y + y * p.DY;
                    context.Convert(raw.AsSpan(offset + y * stride + 1, stride - 1), pw, image.Pixels, (iy * width + p.X) * 4, p.DX * 4);
                }
                offset += ph * stride;
            }
        }

        string colorName = colorType switch { 0 => "Gray", 2 => "RGB", 3 => "Palette", 4 => "GrayAlpha", _ => "RGBA" };
        bool hasAlpha = colorType is 4 or 6 || paletteAlpha != null || transparent != null;
        var info = new ImageInfo(ImageFormat.Png, width, height, bitDepth, colorType == 3 ? 3 : channels, hasAlpha,
                                 $"PNG {colorName} {bitDepth}bit{(interlace == 1 ? " interlaced" : "")}");
        return new DecodedImage(image, info);
    }

    private static void validate(int colorType, int bitDepth)
    {
        bool ok = colorType switch
        {
            0 => bitDepth is 1 or 2 or 4 or 8 or 16,
            3 => bitDepth is 1 or 2 or 4 or 8,
            2 or 4 or 6 => bitDepth is 8 or 16,
            _ => false,
        };
        if (!ok) throw new ImageFormatException($"PNG : bad color type {colorType} / bit depth {bitDepth}");
    }

    private static int rowBytes(int width, int bitsPerPixel) => (int)(((long)width * bitsPerPixel + 7) / 8);
    private static int passSize(int size, int start, int step) => size <= start ? 0 : (size - start + step - 1) / step;

    // フィルターを戻す (行の頭の 1 byte がフィルターの種類)
    private static void unfilter(byte[] raw, int offset, int width, int height, int bitsPerPixel, int bpp)
    {
        int length = rowBytes(width, bitsPerPixel), stride = length + 1;
        for (int y = 0; y < height; y++)
        {
            int start = offset + y * stride;
            int filter = raw[start];
            var row = raw.AsSpan(start + 1, length);
            var prior = y > 0 ? raw.AsSpan(start - stride + 1, length) : Span<byte>.Empty;
            switch (filter)
            {
                case 0: break;
                case 1:
                    for (int i = bpp; i < length; i++) row[i] += row[i - bpp];
                    break;
                case 2:
                    if (y > 0) for (int i = 0; i < length; i++) row[i] += prior[i];
                    break;
                case 3:
                    for (int i = 0; i < length; i++)
                    {
                        int left = i >= bpp ? row[i - bpp] : 0, up = y > 0 ? prior[i] : 0;
                        row[i] += (byte)((left + up) >> 1);
                    }
                    break;
                case 4:
                    for (int i = 0; i < length; i++)
                    {
                        int a = i >= bpp ? row[i - bpp] : 0, b = y > 0 ? prior[i] : 0, c = i >= bpp && y > 0 ? prior[i - bpp] : 0;
                        row[i] += (byte)Paeth(a, b, c);
                    }
                    break;
                default: throw new ImageFormatException($"PNG : bad filter type {filter}");
            }
        }
    }

    internal static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    // 1 行 (フィルターを戻した物) → float RGBA
    private sealed class RowContext(int colorType, int bitDepth, byte[]? palette, byte[]? paletteAlpha, int[]? transparent)
    {
        public void Convert(ReadOnlySpan<byte> row, int width, float[] dst, int dstIndex, int dstStep)
        {
            var table = ImageBuffer.ByteTable;
            int maxValue = (1 << bitDepth) - 1;
            for (int x = 0; x < width; x++, dstIndex += dstStep)
            {
                float r, g, b, a = 1f;
                switch (colorType)
                {
                    case 0:
                    {
                        int v = sample(row, x, 0, 1);
                        r = g = b = (float)v / maxValue;
                        if (transparent != null && v == transparent[0]) a = 0f;
                        break;
                    }
                    case 3:
                    {
                        int index = sample(row, x, 0, 1);
                        if (index * 3 + 2 >= palette!.Length) { r = g = b = 0f; }   // パレットの外 (壊れたファイル。黒にする)
                        else { r = table[palette[index * 3]]; g = table[palette[index * 3 + 1]]; b = table[palette[index * 3 + 2]]; }
                        if (paletteAlpha != null && index < paletteAlpha.Length) a = table[paletteAlpha[index]];
                        break;
                    }
                    case 2:
                    {
                        int vr = sample(row, x, 0, 3), vg = sample(row, x, 1, 3), vb = sample(row, x, 2, 3);
                        r = (float)vr / maxValue; g = (float)vg / maxValue; b = (float)vb / maxValue;
                        if (transparent != null && vr == transparent[0] && vg == transparent[1] && vb == transparent[2]) a = 0f;
                        break;
                    }
                    case 4:
                        r = g = b = (float)sample(row, x, 0, 2) / maxValue;
                        a = (float)sample(row, x, 1, 2) / maxValue;
                        break;
                    default:
                        r = (float)sample(row, x, 0, 4) / maxValue;
                        g = (float)sample(row, x, 1, 4) / maxValue;
                        b = (float)sample(row, x, 2, 4) / maxValue;
                        a = (float)sample(row, x, 3, 4) / maxValue;
                        break;
                }
                dst[dstIndex] = r; dst[dstIndex + 1] = g; dst[dstIndex + 2] = b; dst[dstIndex + 3] = a;
            }
        }

        private int sample(ReadOnlySpan<byte> row, int x, int channel, int channels)
        {
            switch (bitDepth)
            {
                case 8: return row[x * channels + channel];
                case 16: { int i = (x * channels + channel) * 2; return row[i] << 8 | row[i + 1]; }
                default:
                {
                    // 1 / 2 / 4 bit は 1 チャンネルだけ (グレー / パレット)。上の bit から並ぶ
                    int bit = x * bitDepth;
                    int shift = 8 - bitDepth - (bit & 7);
                    return (row[bit >> 3] >> shift) & ((1 << bitDepth) - 1);
                }
            }
        }
    }
}

// ==========================================================================
// PNG の書き出し (v2.3) : 8bit の RGB / RGBA。行ごとに 5 種のフィルターを試し、差の絶対値の和が一番小さい物を使う
// ==========================================================================
internal static class PngEncoder
{
    public static byte[] Encode(ImageBuffer image, ImageSaveOptions options)
    {
        bool alpha = options.UseAlpha(image);
        int channels = alpha ? 4 : 3;
        int width = image.Width, height = image.Height;
        int length = width * channels, stride = length + 1;

        // 8bit にする
        var pixels = new byte[height * length];
        Parallel.For(0, height, y =>
        {
            var src = image.Pixels;
            int s = y * width * 4, d = y * length;
            for (int x = 0; x < width; x++, s += 4)
            {
                pixels[d++] = ImageBuffer.ToByte(src[s]);
                pixels[d++] = ImageBuffer.ToByte(src[s + 1]);
                pixels[d++] = ImageBuffer.ToByte(src[s + 2]);
                if (alpha) pixels[d++] = ImageBuffer.ToByte(src[s + 3]);
            }
        });

        // フィルター (前の行はフィルター前の値を使うので、行ごとに並列にできる)
        var raw = new byte[height * stride];
        Parallel.For(0, height, y =>
        {
            var row = pixels.AsSpan(y * length, length);
            var prior = y > 0 ? pixels.AsSpan((y - 1) * length, length) : Span<byte>.Empty;
            var candidate = new byte[length];
            var bestRow = new byte[length];
            long bestScore = long.MaxValue;
            int bestFilter = 0;
            for (int filter = 0; filter < 5; filter++)
            {
                long score = 0;
                for (int i = 0; i < length; i++)
                {
                    int a = i >= channels ? row[i - channels] : 0, b = y > 0 ? prior[i] : 0, c = i >= channels && y > 0 ? prior[i - channels] : 0;
                    int predicted = filter switch { 0 => 0, 1 => a, 2 => b, 3 => (a + b) >> 1, _ => PngDecoder.Paeth(a, b, c) };
                    var v = (byte)(row[i] - predicted);
                    candidate[i] = v;
                    score += v < 128 ? v : 256 - v;
                }
                if (score < bestScore)
                {
                    bestScore = score;
                    bestFilter = filter;
                    candidate.CopyTo(bestRow, 0);
                }
            }
            raw[y * stride] = (byte)bestFilter;
            bestRow.CopyTo(raw, y * stride + 1);
        });

        var compressed = Deflater.Zlib(raw);
        using var output = new MemoryStream(compressed.Length + 128);
        output.Write(PngDecoder.Signature);
        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(header, (uint)width);
        BinaryPrimitives.WriteUInt32BigEndian(header[4..], (uint)height);
        header[8] = 8;
        header[9] = (byte)(alpha ? 6 : 2);
        header[10] = header[11] = header[12] = 0;
        writeChunk(output, "IHDR", header);
        const int MaxChunk = 1 << 20;
        for (int offset = 0; offset < compressed.Length; offset += MaxChunk)
            writeChunk(output, "IDAT", compressed.AsSpan(offset, Math.Min(MaxChunk, compressed.Length - offset)));
        writeChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    private static void writeChunk(Stream output, string type, ReadOnlySpan<byte> body)
    {
        Span<byte> head = stackalloc byte[8];
        BinaryPrimitives.WriteUInt32BigEndian(head, (uint)body.Length);
        for (int i = 0; i < 4; i++) head[4 + i] = (byte)type[i];
        output.Write(head);
        output.Write(body);
        var crc = Crc32.Update(0xFFFFFFFFu, head[4..]);
        crc = Crc32.Update(crc, body) ^ 0xFFFFFFFFu;
        Span<byte> tail = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(tail, crc);
        output.Write(tail);
    }
}
