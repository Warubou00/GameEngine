using System.Buffers.Binary;

namespace GameEngine.Imaging.Codecs;

// ==========================================================================
// TGA の読み込み (v2.3。Truevision TGA 2.0)
//   種類 : 1 / 9 パレット、2 / 10 フルカラー、3 / 11 グレー (9〜11 は RLE。パケットが行をまたいでもよい)
//   画素 : 8 (グレー / パレットの番号)、15 / 16 (5:5:5 + 1bit のアルファ)、24、32。グレー + アルファの 16bit
//   原点 : 記述子の bit 5 = 上から、bit 4 = 右から
//   32bit でアルファの bit 数が 0 と書いてあっても、アルファの値が全部 0 でなければ使う (そう書く物が多い)
// ==========================================================================
internal static class TgaDecoder
{
    public static DecodedImage Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 18) throw new ImageFormatException("TGA : too short");
        int idLength = data[0], colorMapType = data[1], imageType = data[2];
        int mapFirst = BinaryPrimitives.ReadUInt16LittleEndian(data[3..]);
        int mapLength = BinaryPrimitives.ReadUInt16LittleEndian(data[5..]);
        int mapEntryBits = data[7];
        int width = BinaryPrimitives.ReadUInt16LittleEndian(data[12..]);
        int height = BinaryPrimitives.ReadUInt16LittleEndian(data[14..]);
        int depth = data[16], descriptor = data[17];
        int alphaBits = descriptor & 15;
        bool rle = imageType is 9 or 10 or 11;
        int baseType = rle ? imageType - 8 : imageType;
        if (baseType is not (1 or 2 or 3)) throw new ImageFormatException($"TGA : image type {imageType} is not supported");
        if (colorMapType > 1) throw new ImageFormatException("TGA : bad color map type");
        if (width == 0 || height == 0) throw new ImageFormatException("TGA : bad size");
        bool okDepth = baseType switch
        {
            1 => depth is 8 or 16,
            2 => depth is 15 or 16 or 24 or 32,
            _ => depth is 8 or 16,
        };
        if (!okDepth) throw new ImageFormatException($"TGA : {depth}-bit is not supported for type {imageType}");

        int pos = 18 + idLength;
        float[]? palette = null;   // RGBA
        if (colorMapType == 1)
        {
            int entryBytes = (mapEntryBits + 7) / 8;
            if (entryBytes is < 2 or > 4) throw new ImageFormatException($"TGA : {mapEntryBits}-bit color map is not supported");
            palette = new float[(mapFirst + mapLength) * 4];
            for (int i = 0; i < mapLength; i++)
            {
                var (r, g, b, a) = trueColor(data.Slice(pos + i * entryBytes, entryBytes), mapEntryBits, mapEntryBits == 32 || (mapEntryBits == 16 && alphaBits > 0));
                int o = (mapFirst + i) * 4;
                palette[o] = r; palette[o + 1] = g; palette[o + 2] = b; palette[o + 3] = a;
            }
            pos += mapLength * entryBytes;
        }
        if (baseType == 1 && palette == null) throw new ImageFormatException("TGA : color-mapped image without color map");

        // 画素を並んでいる順に読む (RLE を戻す)
        int pixelBytes = (depth + 7) / 8;
        int count = width * height;
        var raw = new byte[count * pixelBytes];
        if (rle)
        {
            int written = 0;
            while (written < raw.Length)
            {
                if (pos >= data.Length) throw new ImageFormatException("TGA : truncated RLE data");
                int header = data[pos++];
                int n = (header & 0x7F) + 1;
                if ((header & 0x80) != 0)
                {
                    var pixel = data.Slice(pos, pixelBytes);
                    pos += pixelBytes;
                    for (int k = 0; k < n && written < raw.Length; k++, written += pixelBytes) pixel.CopyTo(raw.AsSpan(written));
                }
                else
                {
                    int bytes = Math.Min(n * pixelBytes, raw.Length - written);
                    data.Slice(pos, bytes).CopyTo(raw.AsSpan(written));
                    pos += n * pixelBytes;
                    written += bytes;
                }
            }
        }
        else
        {
            if (pos + raw.Length > data.Length) throw new ImageFormatException("TGA : pixel data too short");
            data.Slice(pos, raw.Length).CopyTo(raw);
        }

        bool topOrigin = (descriptor & 0x20) != 0, rightOrigin = (descriptor & 0x10) != 0;
        var image = new ImageBuffer(width, height);
        var dst = image.Pixels;
        var table = ImageBuffer.ByteTable;
        bool useAlpha16 = depth == 16 && alphaBits > 0;
        for (int i = 0; i < count; i++)
        {
            int sx = i % width, sy = i / width;
            int x = rightOrigin ? width - 1 - sx : sx;
            int y = topOrigin ? sy : height - 1 - sy;
            int o = (y * width + x) * 4;
            var p = raw.AsSpan(i * pixelBytes, pixelBytes);
            float r, g, b, a = 1f;
            switch (baseType)
            {
                case 1:
                {
                    int index = depth == 8 ? p[0] : p[0] | p[1] << 8;
                    if (index * 4 + 3 >= palette!.Length) { r = g = b = 0f; }
                    else { r = palette[index * 4]; g = palette[index * 4 + 1]; b = palette[index * 4 + 2]; a = palette[index * 4 + 3]; }
                    break;
                }
                case 2:
                    (r, g, b, a) = trueColor(p, depth, depth == 32 || useAlpha16);
                    break;
                default:
                    r = g = b = table[p[0]];
                    if (depth == 16) a = table[p[1]];
                    break;
            }
            dst[o] = r; dst[o + 1] = g; dst[o + 2] = b; dst[o + 3] = a;
        }

        bool hasAlpha = depth == 32 || useAlpha16 || (baseType == 3 && depth == 16) || (baseType == 1 && (mapEntryBits == 32 || (mapEntryBits == 16 && alphaBits > 0)));
        // アルファの bit 数を 0 と書いた 32bit で、アルファが全部 0 = アルファを使っていない (不透明にする)
        if (hasAlpha && alphaBits == 0)
        {
            bool allZero = true;
            for (int i = 3; i < dst.Length && allZero; i += 4) allZero = dst[i] == 0f;
            if (allZero)
            {
                for (int i = 3; i < dst.Length; i += 4) dst[i] = 1f;
                hasAlpha = false;
            }
        }

        string kind = baseType switch { 1 => "color-mapped", 2 => "true-color", _ => "gray" };
        var info = new ImageInfo(ImageFormat.Tga, width, height, depth is 15 or 16 && baseType == 2 ? 5 : 8, baseType == 3 ? (depth == 16 ? 2 : 1) : hasAlpha ? 4 : 3, hasAlpha,
                                 $"TGA {kind} {depth}bit{(rle ? " RLE" : "")}");
        return new DecodedImage(image, info);
    }

    private static (float R, float G, float B, float A) trueColor(ReadOnlySpan<byte> p, int bits, bool alpha)
    {
        var table = ImageBuffer.ByteTable;
        switch (bits)
        {
            case 15 or 16:
            {
                int v = p[0] | p[1] << 8;
                const float k = 1f / 31f;
                return (((v >> 10) & 31) * k, ((v >> 5) & 31) * k, (v & 31) * k, alpha ? (v >> 15) & 1 : 1f);
            }
            case 24: return (table[p[2]], table[p[1]], table[p[0]], 1f);
            default: return (table[p[2]], table[p[1]], table[p[0]], alpha ? table[p[3]] : 1f);
        }
    }
}

// ==========================================================================
// TGA の書き出し (v2.3) : 24bit (不透明) / 32bit (アルファ 8bit)、上から並べる。RLE は行ごと (行をまたがない)。TGA 2.0 の後書きを付ける
// ==========================================================================
internal static class TgaEncoder
{
    public static byte[] Encode(ImageBuffer image, ImageSaveOptions options)
    {
        bool alpha = options.UseAlpha(image);
        int width = image.Width, height = image.Height;
        if (width > 65535 || height > 65535) throw new ArgumentException("TGA : image is larger than 65535");
        int bytes = alpha ? 4 : 3;
        using var output = new MemoryStream(width * height * bytes + 64);
        Span<byte> header = stackalloc byte[18];
        header.Clear();
        header[2] = (byte)(options.TgaRle ? 10 : 2);
        BinaryPrimitives.WriteUInt16LittleEndian(header[12..], (ushort)width);
        BinaryPrimitives.WriteUInt16LittleEndian(header[14..], (ushort)height);
        header[16] = (byte)(bytes * 8);
        header[17] = (byte)(0x20 | (alpha ? 8 : 0));
        output.Write(header);

        var px = image.Pixels;
        var row = new byte[width * bytes];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0, s = y * width * 4, d = 0; x < width; x++, s += 4)
            {
                row[d++] = ImageBuffer.ToByte(px[s + 2]);
                row[d++] = ImageBuffer.ToByte(px[s + 1]);
                row[d++] = ImageBuffer.ToByte(px[s]);
                if (alpha) row[d++] = ImageBuffer.ToByte(px[s + 3]);
            }
            if (!options.TgaRle) { output.Write(row); continue; }
            writeRleRow(output, row, width, bytes);
        }
        // TGA 2.0 の後書き (拡張 / 開発者の領域は無し)
        output.Write(new byte[8]);
        output.Write("TRUEVISION-XFILE.\0"u8);
        return output.ToArray();
    }

    private static void writeRleRow(Stream output, byte[] row, int width, int bytes)
    {
        bool same(int a, int b) => row.AsSpan(a * bytes, bytes).SequenceEqual(row.AsSpan(b * bytes, bytes));
        int x = 0;
        while (x < width)
        {
            // 同じ画素が 2 つ以上続く → 繰り返しのパケット
            int run = 1;
            while (x + run < width && run < 128 && same(x, x + run)) run++;
            if (run >= 2)
            {
                output.WriteByte((byte)(0x80 | (run - 1)));
                output.Write(row, x * bytes, bytes);
                x += run;
                continue;
            }
            // 続かない所 → そのままのパケット (次に同じ物が 2 つ続く所の手前まで)
            int raw = 1;
            while (x + raw < width && raw < 128 && !(x + raw + 1 < width && same(x + raw, x + raw + 1))) raw++;
            output.WriteByte((byte)(raw - 1));
            output.Write(row, x * bytes, raw * bytes);
            x += raw;
        }
    }
}
