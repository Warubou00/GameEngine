using System.Buffers.Binary;
using System.Numerics;

namespace GameEngine.Imaging.Codecs;

// ==========================================================================
// BMP の読み込み (v2.3)
//   ヘッダー : OS/2 (12 byte) / BITMAPINFOHEADER (40) / V2 (52) / V3 (56) / V4 (108) / V5 (124)
//   1/4/8bit (パレット)、16/24/32bit、RLE8 / RLE4、BI_BITFIELDS / BI_ALPHABITFIELDS、上下逆 (高さが負 = 上から)
//   32bit の BI_RGB はアルファを使わない (Windows と同じ)。アルファのマスクがある時だけ使う
//   非対応 : 中身が JPEG / PNG の BMP (圧縮 4 / 5)
// ==========================================================================
internal static class BmpDecoder
{
    public static DecodedImage Decode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 26 || data[0] != 'B' || data[1] != 'M') throw new ImageFormatException("BMP : bad signature");
        int pixelOffset = BinaryPrimitives.ReadInt32LittleEndian(data[10..]);
        int headerSize = BinaryPrimitives.ReadInt32LittleEndian(data[14..]);
        var h = data[14..];
        int width, height, bpp, compression = 0, colorsUsed = 0;
        bool core = headerSize == 12;
        if (core)
        {
            width = BinaryPrimitives.ReadUInt16LittleEndian(h[4..]);
            height = BinaryPrimitives.ReadInt16LittleEndian(h[6..]);
            bpp = BinaryPrimitives.ReadUInt16LittleEndian(h[10..]);
        }
        else if (headerSize >= 40)
        {
            width = BinaryPrimitives.ReadInt32LittleEndian(h[4..]);
            height = BinaryPrimitives.ReadInt32LittleEndian(h[8..]);
            bpp = BinaryPrimitives.ReadUInt16LittleEndian(h[14..]);
            compression = BinaryPrimitives.ReadInt32LittleEndian(h[16..]);
            colorsUsed = BinaryPrimitives.ReadInt32LittleEndian(h[32..]);
        }
        else throw new ImageFormatException($"BMP : unknown header size {headerSize}");

        bool topDown = height < 0;
        height = Math.Abs(height);
        if (width <= 0 || height <= 0) throw new ImageFormatException("BMP : bad size");
        if (compression is 4 or 5) throw new ImageFormatException("BMP : embedded JPEG / PNG is not supported");
        if (compression is not (0 or 1 or 2 or 3 or 6)) throw new ImageFormatException($"BMP : compression {compression} is not supported");
        if (bpp is not (1 or 4 or 8 or 16 or 24 or 32)) throw new ImageFormatException($"BMP : {bpp}-bit is not supported");

        // マスク (16 / 32bit)
        uint rMask = 0, gMask = 0, bMask = 0, aMask = 0;
        int afterHeader = 14 + headerSize;
        if (compression is 3 or 6)
        {
            if (headerSize >= 52)
            {
                rMask = BinaryPrimitives.ReadUInt32LittleEndian(h[40..]);
                gMask = BinaryPrimitives.ReadUInt32LittleEndian(h[44..]);
                bMask = BinaryPrimitives.ReadUInt32LittleEndian(h[48..]);
                if (headerSize >= 56) aMask = BinaryPrimitives.ReadUInt32LittleEndian(h[52..]);
            }
            else
            {
                // 40 byte のヘッダーの後ろに 3 つ (BI_ALPHABITFIELDS は 4 つ)
                var m = data[afterHeader..];
                rMask = BinaryPrimitives.ReadUInt32LittleEndian(m);
                gMask = BinaryPrimitives.ReadUInt32LittleEndian(m[4..]);
                bMask = BinaryPrimitives.ReadUInt32LittleEndian(m[8..]);
                afterHeader += 12;
                if (compression == 6) { aMask = BinaryPrimitives.ReadUInt32LittleEndian(m[12..]); afterHeader += 4; }
            }
        }
        else if (bpp == 16) { rMask = 0x7C00; gMask = 0x03E0; bMask = 0x001F; }
        else if (bpp == 32) { rMask = 0x00FF0000; gMask = 0x0000FF00; bMask = 0x000000FF; }
        else if (bpp == 24) { rMask = 0x00FF0000; gMask = 0x0000FF00; bMask = 0x000000FF; }

        // パレット
        float[]? palette = null;
        if (bpp <= 8)
        {
            int entrySize = core ? 3 : 4;
            int count = colorsUsed > 0 ? Math.Min(colorsUsed, 256) : 1 << bpp;
            count = Math.Min(count, Math.Max(0, (pixelOffset - afterHeader) / entrySize));
            palette = new float[256 * 3];
            var table = ImageBuffer.ByteTable;
            for (int i = 0; i < count; i++)
            {
                int o = afterHeader + i * entrySize;
                palette[i * 3] = table[data[o + 2]];
                palette[i * 3 + 1] = table[data[o + 1]];
                palette[i * 3 + 2] = table[data[o]];
            }
        }

        var image = new ImageBuffer(width, height);
        var dst = image.Pixels;
        bool hasAlpha = aMask != 0;
        if (pixelOffset <= 0 || pixelOffset >= data.Length) throw new ImageFormatException("BMP : bad pixel data offset");
        var pixels = data[pixelOffset..];

        if (compression is 1 or 2)
        {
            if (palette == null) throw new ImageFormatException("BMP : RLE without palette");
            var indices = decodeRle(pixels, width, height, compression == 2);
            for (int y = 0; y < height; y++)
            {
                int dy = topDown ? y : height - 1 - y;
                for (int x = 0; x < width; x++)
                {
                    int index = indices[y * width + x], o = (dy * width + x) * 4;
                    dst[o] = palette[index * 3]; dst[o + 1] = palette[index * 3 + 1]; dst[o + 2] = palette[index * 3 + 2]; dst[o + 3] = 1f;
                }
            }
        }
        else
        {
            int stride = (int)(((long)width * bpp + 31) / 32 * 4);
            if ((long)stride * height > pixels.Length) throw new ImageFormatException("BMP : pixel data too short");
            var channel = new MaskChannel[] { new(rMask), new(gMask), new(bMask), new(aMask) };
            var table = ImageBuffer.ByteTable;
            for (int y = 0; y < height; y++)
            {
                var row = pixels.Slice(y * stride, stride);
                int dy = topDown ? y : height - 1 - y;
                int o = dy * width * 4;
                for (int x = 0; x < width; x++, o += 4)
                {
                    switch (bpp)
                    {
                        case 1 or 4 or 8:
                        {
                            int bit = x * bpp;
                            int index = (row[bit >> 3] >> (8 - bpp - (bit & 7))) & ((1 << bpp) - 1);
                            dst[o] = palette![index * 3]; dst[o + 1] = palette[index * 3 + 1]; dst[o + 2] = palette[index * 3 + 2]; dst[o + 3] = 1f;
                            break;
                        }
                        case 24 when compression == 0:
                            dst[o] = table[row[x * 3 + 2]]; dst[o + 1] = table[row[x * 3 + 1]]; dst[o + 2] = table[row[x * 3]]; dst[o + 3] = 1f;
                            break;
                        default:
                        {
                            uint v = bpp == 16 ? BinaryPrimitives.ReadUInt16LittleEndian(row[(x * 2)..])
                                   : bpp == 24 ? (uint)(row[x * 3] | row[x * 3 + 1] << 8 | row[x * 3 + 2] << 16)
                                   : BinaryPrimitives.ReadUInt32LittleEndian(row[(x * 4)..]);
                            dst[o] = channel[0].Get(v); dst[o + 1] = channel[1].Get(v); dst[o + 2] = channel[2].Get(v);
                            dst[o + 3] = hasAlpha ? channel[3].Get(v) : 1f;
                            break;
                        }
                    }
                }
            }
        }

        string kind = compression switch { 1 => "RLE8", 2 => "RLE4", 3 or 6 => "bitfields", _ => "RGB" };
        var info = new ImageInfo(ImageFormat.Bmp, width, height, bpp <= 8 ? 8 : bpp == 16 ? 5 : 8, bpp <= 8 ? 3 : hasAlpha ? 4 : 3, hasAlpha,
                                 $"BMP {bpp}bit {kind}{(topDown ? " top-down" : "")}");
        return new DecodedImage(image, info);
    }

    private readonly struct MaskChannel
    {
        private readonly uint _mask;
        private readonly int _shift;
        private readonly float _scale;

        public MaskChannel(uint mask)
        {
            _mask = mask;
            _shift = mask == 0 ? 0 : BitOperations.TrailingZeroCount(mask);
            int bits = BitOperations.PopCount(mask);
            _scale = bits == 0 ? 0f : 1f / (float)((1UL << bits) - 1);
        }

        public float Get(uint value) => _mask == 0 ? 0f : ((value & _mask) >> _shift) * _scale;
    }

    // RLE8 / RLE4 → パレットの番号 (下の行から。書いていない所は 0)
    private static byte[] decodeRle(ReadOnlySpan<byte> data, int width, int height, bool four)
    {
        var result = new byte[width * height];
        int x = 0, y = 0, i = 0;
        void put(int index)
        {
            if (x < width && y < height) result[y * width + x] = (byte)index;
            x++;
        }
        while (i + 1 < data.Length && y < height)
        {
            int count = data[i], value = data[i + 1];
            i += 2;
            if (count > 0)
            {
                for (int k = 0; k < count; k++) put(four ? (k & 1) == 0 ? value >> 4 : value & 15 : value);
                continue;
            }
            switch (value)
            {
                case 0: x = 0; y++; break;           // 行の終わり
                case 1: return result;               // 画像の終わり
                case 2:                              // 動かす
                    if (i + 1 >= data.Length) return result;
                    x += data[i]; y += data[i + 1]; i += 2;
                    break;
                default:                             // そのまま value 個
                {
                    int bytes = four ? (value + 1) / 2 : value;
                    for (int k = 0; k < value && i + (four ? k / 2 : k) < data.Length; k++)
                        put(four ? (k & 1) == 0 ? data[i + k / 2] >> 4 : data[i + k / 2] & 15 : data[i + k]);
                    i += bytes + (bytes & 1);   // 2 byte の区切りへ
                    break;
                }
            }
        }
        return result;
    }
}

// ==========================================================================
// BMP の書き出し (v2.3) : 不透明なら 24bit (BITMAPINFOHEADER)、アルファがあれば 32bit (BITMAPV4HEADER + BI_BITFIELDS)。下の行から
// ==========================================================================
internal static class BmpEncoder
{
    public static byte[] Encode(ImageBuffer image, ImageSaveOptions options)
    {
        bool alpha = options.UseAlpha(image);
        int width = image.Width, height = image.Height;
        int bpp = alpha ? 32 : 24;
        int headerSize = alpha ? 108 : 40;
        int stride = (width * bpp + 31) / 32 * 4;
        int offset = 14 + headerSize;
        var data = new byte[offset + stride * height];
        var span = data.AsSpan();
        span[0] = (byte)'B'; span[1] = (byte)'M';
        BinaryPrimitives.WriteInt32LittleEndian(span[2..], data.Length);
        BinaryPrimitives.WriteInt32LittleEndian(span[10..], offset);
        var h = span[14..];
        BinaryPrimitives.WriteInt32LittleEndian(h, headerSize);
        BinaryPrimitives.WriteInt32LittleEndian(h[4..], width);
        BinaryPrimitives.WriteInt32LittleEndian(h[8..], height);
        BinaryPrimitives.WriteUInt16LittleEndian(h[12..], 1);
        BinaryPrimitives.WriteUInt16LittleEndian(h[14..], (ushort)bpp);
        BinaryPrimitives.WriteInt32LittleEndian(h[16..], alpha ? 3 : 0);
        BinaryPrimitives.WriteInt32LittleEndian(h[20..], stride * height);
        BinaryPrimitives.WriteInt32LittleEndian(h[24..], 2835);   // 72 dpi
        BinaryPrimitives.WriteInt32LittleEndian(h[28..], 2835);
        if (alpha)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(h[40..], 0x00FF0000);
            BinaryPrimitives.WriteUInt32LittleEndian(h[44..], 0x0000FF00);
            BinaryPrimitives.WriteUInt32LittleEndian(h[48..], 0x000000FF);
            BinaryPrimitives.WriteUInt32LittleEndian(h[52..], 0xFF000000);
            BinaryPrimitives.WriteUInt32LittleEndian(h[56..], 0x73524742);   // 'sRGB'
        }
        var px = image.Pixels;
        Parallel.For(0, height, y =>
        {
            int d = offset + (height - 1 - y) * stride, s = y * width * 4;
            for (int x = 0; x < width; x++, s += 4)
            {
                data[d++] = ImageBuffer.ToByte(px[s + 2]);
                data[d++] = ImageBuffer.ToByte(px[s + 1]);
                data[d++] = ImageBuffer.ToByte(px[s]);
                if (alpha) data[d++] = ImageBuffer.ToByte(px[s + 3]);
            }
        });
        return data;
    }
}
