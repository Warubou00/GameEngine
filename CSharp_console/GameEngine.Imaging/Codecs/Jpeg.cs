namespace GameEngine.Imaging.Codecs;

// ==========================================================================
// JPEG の読み込み (v2.3。ITU-T T.81 / JFIF)
//   対応 : ベースライン (SOF0) / 拡張 (SOF1, 8bit) / プログレッシブ (SOF2) のハフマン符号、
//          色差の間引き (4:4:4 / 4:2:2 / 4:2:0 など)、リスタートマーカー、グレースケール、Adobe の RGB (transform 0)
//   非対応 (ImageFormatException) : 算術符号、ロスレス、階層、12bit、CMYK / YCCK (4 成分)、DNL
//   流れ : 全部のスキャンで係数を集める (プログレッシブは何回にも分けて送られる) → 逆量子化 + 逆 DCT →
//          間引いた色差はバイリニアで広げる → YCbCr を RGB にする
//   EXIF の向き (Orientation) は見ない (ファイルの並びのまま)
// ==========================================================================
internal static class JpegDecoder
{
    internal static readonly int[] ZigZag =
    [
        0, 1, 8, 16, 9, 2, 3, 10, 17, 24, 32, 25, 18, 11, 4, 5, 12, 19, 26, 33, 40, 48, 41, 34, 27, 20, 13, 6, 7, 14, 21, 28,
        35, 42, 49, 56, 57, 50, 43, 36, 29, 22, 15, 23, 30, 37, 44, 51, 58, 59, 52, 45, 38, 31, 39, 46, 53, 60, 61, 54, 47, 55, 62, 63,
        // 壊れたデータで 63 を越えた時の分 (配列の外を読まないように)
        63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63, 63,
    ];

    /// <summary>DCT の基底 : Basis[x * 8 + u] = C(u) / 2 * cos((2x + 1) u π / 16)。C(0) = 1/√2</summary>
    internal static readonly float[] Basis = buildBasis();

    private static float[] buildBasis()
    {
        var table = new float[64];
        for (int x = 0; x < 8; x++)
            for (int u = 0; u < 8; u++)
                table[x * 8 + u] = (float)((u == 0 ? Math.Sqrt(0.5) : 1.0) * 0.5 * Math.Cos((2 * x + 1) * u * Math.PI / 16));
        return table;
    }

    private sealed class Component
    {
        public int Id, H, V, QuantTable;
        public int BlocksPerLine, BlocksPerColumn;   // MCU に合わせて広げた数
        public int Width, Height;                    // この成分の画素の数
        public short[] Coefficients = [];            // ブロックごとに 64 (自然な並び。量子化したまま)
        public int Pred;                             // DC の前の値
        public int DcTable, AcTable;
        public byte[] Plane = [];                    // 逆 DCT した後の画素 (BlocksPerLine * 8 の幅)
    }

    private sealed class Huffman
    {
        public readonly int[] Fast = new int[1 << FastBits];   // 上位 9 bit → (記号 | 長さ << 8)。-1 = 遅い道
        public readonly int[] MaxCode = new int[18];
        public readonly int[] ValPtr = new int[17];
        public readonly int[] MinCode = new int[17];
        public readonly byte[] Values;
        public const int FastBits = 9;

        public Huffman(ReadOnlySpan<byte> counts, ReadOnlySpan<byte> values)
        {
            Values = values.ToArray();
            Array.Fill(Fast, -1);
            int code = 0, k = 0;
            for (int length = 1; length <= 16; length++)
            {
                ValPtr[length] = k;
                MinCode[length] = code;
                int n = counts[length - 1];
                for (int i = 0; i < n; i++, k++, code++)
                {
                    if (length <= FastBits)
                    {
                        int shift = FastBits - length;
                        int first = code << shift;
                        for (int j = 0; j < 1 << shift; j++) Fast[first + j] = Values[k] | length << 8;
                    }
                }
                MaxCode[length] = n > 0 ? code - 1 : -1;
                if (code > 1 << length) throw new ImageFormatException("JPEG : bad Huffman table");
                code <<= 1;
            }
            MaxCode[17] = int.MaxValue;
        }
    }

    // ---------------------------------------------------------------------
    // ビットの読み込み (0xFF 0x00 は 0xFF。マーカーに着いたら後は 0 を読む)
    // ---------------------------------------------------------------------
    private sealed class BitReader(byte[] data)
    {
        public int Pos;
        private uint _bits;
        private int _count;
        private bool _marker;

        public void Reset() { _bits = 0; _count = 0; _marker = false; }

        private void fill()
        {
            while (_count <= 24)
            {
                uint b = 0;
                if (!_marker && Pos < data.Length)
                {
                    b = data[Pos];
                    if (b == 0xFF)
                    {
                        int next = Pos + 1 < data.Length ? data[Pos + 1] : 0xD9;
                        if (next == 0x00) Pos += 2;
                        else { _marker = true; b = 0; }
                    }
                    else Pos++;
                }
                _bits |= b << (24 - _count);
                _count += 8;
            }
        }

        public int Bits(int n)
        {
            if (n == 0) return 0;
            fill();
            int v = (int)(_bits >> (32 - n));
            _bits <<= n;
            _count -= n;
            return v;
        }

        public int Bit() => Bits(1);

        public int Extend(int s)
        {
            if (s == 0) return 0;
            if (s > 16) throw new ImageFormatException("JPEG : bad coefficient size");
            int v = Bits(s);
            return v < 1 << (s - 1) ? v - (1 << s) + 1 : v;
        }

        public int Decode(Huffman h)
        {
            fill();
            int entry = h.Fast[_bits >> (32 - Huffman.FastBits)];
            if (entry >= 0)
            {
                int length = entry >> 8;
                _bits <<= length;
                _count -= length;
                return entry & 0xFF;
            }
            for (int length = Huffman.FastBits + 1; length <= 16; length++)
            {
                int code = (int)(_bits >> (32 - length));
                if (code <= h.MaxCode[length])
                {
                    _bits <<= length;
                    _count -= length;
                    int index = h.ValPtr[length] + code - h.MinCode[length];
                    if ((uint)index >= (uint)h.Values.Length) throw new ImageFormatException("JPEG : bad Huffman code");
                    return h.Values[index];
                }
            }
            throw new ImageFormatException("JPEG : bad Huffman code");
        }

        /// <summary>リスタートマーカー (RST0〜7) を読み飛ばす</summary>
        public void Restart()
        {
            Reset();
            while (Pos + 1 < data.Length)
            {
                if (data[Pos] == 0xFF && data[Pos + 1] >= 0xD0 && data[Pos + 1] <= 0xD7) { Pos += 2; return; }
                if (data[Pos] == 0xFF && data[Pos + 1] != 0x00 && data[Pos + 1] != 0xFF) return;   // 別のマーカー (RST が無い。そのまま続ける)
                Pos++;
            }
        }
    }

    public static DecodedImage Decode(ReadOnlySpan<byte> input)
    {
        var data = input.ToArray();
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8) throw new ImageFormatException("JPEG : no SOI");
        var quant = new ushort[4][];
        var dcTables = new Huffman?[4];
        var acTables = new Huffman?[4];
        Component[]? components = null;
        int width = 0, height = 0, hMax = 1, vMax = 1, mcusX = 0, mcusY = 0, restartInterval = 0;
        bool progressive = false, adobe = false;
        int adobeTransform = -1;
        bool sawScan = false;

        int pos = 2;
        while (true)
        {
            // 次のマーカーを探す (スキャンの後ろのごみ / 余分な 0xFF は飛ばす)
            while (pos < data.Length && data[pos] != 0xFF) pos++;
            while (pos < data.Length && data[pos] == 0xFF) pos++;
            if (pos >= data.Length)
            {
                if (sawScan) break;   // EOI が無い (途中で切れたファイル。読めた所まで出す)
                throw new ImageFormatException("JPEG : unexpected end of file");
            }
            int marker = data[pos++];
            if (marker == 0xD9) break;                                   // EOI
            if (marker == 0x00 || (marker >= 0xD0 && marker <= 0xD7) || marker == 0x01) continue;   // 詰め物 / RST / TEM
            if (pos + 2 > data.Length) throw new ImageFormatException("JPEG : truncated segment");
            int length = data[pos] << 8 | data[pos + 1];
            if (length < 2 || pos + length > data.Length) throw new ImageFormatException("JPEG : bad segment length");
            var body = data.AsSpan(pos + 2, length - 2);
            int next = pos + length;

            switch (marker)
            {
                case 0xDB:   // DQT
                    for (int i = 0; i < body.Length;)
                    {
                        int pq = body[i] >> 4, tq = body[i] & 15;
                        if (tq > 3) throw new ImageFormatException("JPEG : bad DQT");
                        i++;
                        var table = new ushort[64];
                        for (int k = 0; k < 64; k++)
                        {
                            table[ZigZag[k]] = pq == 0 ? body[i] : (ushort)(body[i] << 8 | body[i + 1]);
                            i += pq == 0 ? 1 : 2;
                        }
                        quant[tq] = table;
                    }
                    break;
                case 0xC4:   // DHT
                    for (int i = 0; i < body.Length;)
                    {
                        int tc = body[i] >> 4, th = body[i] & 15;
                        if (tc > 1 || th > 3) throw new ImageFormatException("JPEG : bad DHT");
                        var counts = body.Slice(i + 1, 16);
                        int total = 0;
                        foreach (var c in counts) total += c;
                        if (total > 256) throw new ImageFormatException("JPEG : bad DHT");
                        var table = new Huffman(counts, body.Slice(i + 17, total));
                        if (tc == 0) dcTables[th] = table; else acTables[th] = table;
                        i += 17 + total;
                    }
                    break;
                case 0xC0 or 0xC1 or 0xC2:   // SOF0 / SOF1 / SOF2
                {
                    if (components != null) throw new ImageFormatException("JPEG : more than one frame");
                    progressive = marker == 0xC2;
                    if (body[0] != 8) throw new ImageFormatException($"JPEG : {body[0]}-bit precision is not supported");
                    height = body[1] << 8 | body[2];
                    width = body[3] << 8 | body[4];
                    if (height == 0) throw new ImageFormatException("JPEG : DNL (height 0) is not supported");
                    if (width == 0) throw new ImageFormatException("JPEG : bad width");
                    int count = body[5];
                    if (count != 1 && count != 3) throw new ImageFormatException(count == 4 ? "JPEG : CMYK / YCCK is not supported" : $"JPEG : {count} components are not supported");
                    components = new Component[count];
                    for (int c = 0; c < count; c++)
                    {
                        var b = body.Slice(6 + c * 3, 3);
                        components[c] = new Component { Id = b[0], H = Math.Max(1, b[1] >> 4), V = Math.Max(1, b[1] & 15), QuantTable = b[2] & 3 };
                        if (components[c].H > 4 || components[c].V > 4) throw new ImageFormatException("JPEG : bad sampling factor");
                    }
                    hMax = components.Max(c => c.H);
                    vMax = components.Max(c => c.V);
                    mcusX = (width + 8 * hMax - 1) / (8 * hMax);
                    mcusY = (height + 8 * vMax - 1) / (8 * vMax);
                    foreach (var c in components)
                    {
                        c.BlocksPerLine = mcusX * c.H;
                        c.BlocksPerColumn = mcusY * c.V;
                        c.Width = (width * c.H + hMax - 1) / hMax;
                        c.Height = (height * c.V + vMax - 1) / vMax;
                        c.Coefficients = new short[c.BlocksPerLine * c.BlocksPerColumn * 64];
                    }
                    break;
                }
                case >= 0xC3 and <= 0xCF when marker != 0xC4 && marker != 0xC8 && marker != 0xCC:
                    throw new ImageFormatException($"JPEG : SOF{marker - 0xC0} (lossless / hierarchical / arithmetic) is not supported");
                case 0xDD:   // DRI
                    restartInterval = body[0] << 8 | body[1];
                    break;
                case 0xEE:   // APP14 (Adobe)
                    if (body.Length >= 12 && body[0] == 'A' && body[1] == 'd' && body[2] == 'o' && body[3] == 'b' && body[4] == 'e')
                    {
                        adobe = true;
                        adobeTransform = body[11];
                    }
                    break;
                case 0xDA:   // SOS
                {
                    if (components == null) throw new ImageFormatException("JPEG : SOS before SOF");
                    int count = body[0];
                    var scan = new Component[count];
                    for (int i = 0; i < count; i++)
                    {
                        int id = body[1 + i * 2], tables = body[2 + i * 2];
                        scan[i] = components.FirstOrDefault(c => c.Id == id) ?? throw new ImageFormatException("JPEG : unknown component in scan");
                        scan[i].DcTable = tables >> 4;
                        scan[i].AcTable = tables & 15;
                    }
                    int p = 1 + count * 2;
                    int ss = body[p], se = body[p + 1], ah = body[p + 2] >> 4, al = body[p + 2] & 15;
                    var reader = new BitReader(data) { Pos = next };
                    decodeScan(reader, scan, dcTables, acTables, progressive, ss, se, ah, al, restartInterval, mcusX, mcusY, hMax, vMax);
                    next = reader.Pos;
                    sawScan = true;
                    break;
                }
                default:
                    break;   // APPn / COM など
            }
            pos = next;
        }

        if (components == null || !sawScan) throw new ImageFormatException("JPEG : no image data");
        foreach (var c in components)
            if (quant[c.QuantTable] == null) throw new ImageFormatException("JPEG : missing quantization table");

        // 逆量子化 + 逆 DCT
        foreach (var c in components) idctComponent(c, quant[c.QuantTable]);

        var image = new ImageBuffer(width, height);
        bool rgb = components.Length == 3 && ((adobe && adobeTransform == 0) || (!adobe && components[0].Id == 'R' && components[1].Id == 'G' && components[2].Id == 'B'));
        Parallel.For(0, height, y =>
        {
            var dst = image.Pixels;
            int o = y * width * 4;
            for (int x = 0; x < width; x++, o += 4)
            {
                if (components.Length == 1)
                {
                    float g = components[0].Plane[y * components[0].BlocksPerLine * 8 + x] / 255f;
                    dst[o] = dst[o + 1] = dst[o + 2] = g;
                }
                else
                {
                    float c0 = sample(components[0], x, y, hMax, vMax), c1 = sample(components[1], x, y, hMax, vMax), c2 = sample(components[2], x, y, hMax, vMax);
                    float r, g, b;
                    if (rgb) { r = c0; g = c1; b = c2; }
                    else
                    {
                        float cb = c1 - 128f, cr = c2 - 128f;
                        r = c0 + 1.402f * cr;
                        g = c0 - 0.344136f * cb - 0.714136f * cr;
                        b = c0 + 1.772f * cb;
                    }
                    dst[o] = Math.Clamp(r, 0f, 255f) / 255f;
                    dst[o + 1] = Math.Clamp(g, 0f, 255f) / 255f;
                    dst[o + 2] = Math.Clamp(b, 0f, 255f) / 255f;
                }
                dst[o + 3] = 1f;
            }
        });

        string sampling = components.Length == 1 ? "Gray" : $"{(rgb ? "RGB" : "YCbCr")} {string.Join(",", components.Select(c => $"{c.H}x{c.V}"))}";
        var info = new ImageInfo(ImageFormat.Jpeg, width, height, 8, components.Length, false, $"JPEG {(progressive ? "progressive" : "baseline")} {sampling}");
        return new DecodedImage(image, info);
    }

    // 成分の値 (0〜255)。間引いてある成分はバイリニアで (画素の中心を合わせる)
    private static float sample(Component c, int x, int y, int hMax, int vMax)
    {
        int stride = c.BlocksPerLine * 8;
        if (c.H == hMax && c.V == vMax) return c.Plane[y * stride + x];
        float fx = (x + 0.5f) * c.H / hMax - 0.5f, fy = (y + 0.5f) * c.V / vMax - 0.5f;
        fx = Math.Clamp(fx, 0f, c.Width - 1);
        fy = Math.Clamp(fy, 0f, c.Height - 1);
        int x0 = (int)fx, y0 = (int)fy;
        int x1 = Math.Min(x0 + 1, c.Width - 1), y1 = Math.Min(y0 + 1, c.Height - 1);
        float tx = fx - x0, ty = fy - y0;
        var p = c.Plane;
        float top = p[y0 * stride + x0] * (1 - tx) + p[y0 * stride + x1] * tx;
        float bottom = p[y1 * stride + x0] * (1 - tx) + p[y1 * stride + x1] * tx;
        return top * (1 - ty) + bottom * ty;
    }

    private static void idctComponent(Component c, ushort[] quant)
    {
        int stride = c.BlocksPerLine * 8;
        c.Plane = new byte[stride * c.BlocksPerColumn * 8];
        var basis = Basis;
        Parallel.For(0, c.BlocksPerColumn, row =>
        {
            Span<float> block = stackalloc float[64];
            Span<float> temp = stackalloc float[64];
            for (int col = 0; col < c.BlocksPerLine; col++)
            {
                int offset = (row * c.BlocksPerLine + col) * 64;
                for (int i = 0; i < 64; i++) block[i] = c.Coefficients[offset + i] * quant[i];
                // 行 (u → x)
                for (int v = 0; v < 8; v++)
                    for (int x = 0; x < 8; x++)
                    {
                        float sum = 0;
                        for (int u = 0; u < 8; u++) sum += basis[x * 8 + u] * block[v * 8 + u];
                        temp[v * 8 + x] = sum;
                    }
                // 列 (v → y)
                for (int y = 0; y < 8; y++)
                {
                    int dst = (row * 8 + y) * stride + col * 8;
                    for (int x = 0; x < 8; x++)
                    {
                        float sum = 0;
                        for (int v = 0; v < 8; v++) sum += basis[y * 8 + v] * temp[v * 8 + x];
                        int value = (int)MathF.Round(sum + 128f);
                        c.Plane[dst + x] = (byte)Math.Clamp(value, 0, 255);
                    }
                }
            }
        });
    }

    // ---------------------------------------------------------------------
    // スキャン
    // ---------------------------------------------------------------------

    private static void decodeScan(BitReader reader, Component[] scan, Huffman?[] dcTables, Huffman?[] acTables, bool progressive,
                                   int ss, int se, int ah, int al, int restartInterval, int mcusX, int mcusY, int hMax, int vMax)
    {
        if (progressive)
        {
            if (ss == 0 && se != 0) throw new ImageFormatException("JPEG : bad progressive scan (DC and AC mixed)");
            if (ss > se || se > 63) throw new ImageFormatException("JPEG : bad spectral selection");
            if (ss > 0 && scan.Length != 1) throw new ImageFormatException("JPEG : AC scan with more than one component");
        }
        else { ss = 0; se = 63; ah = 0; al = 0; }

        foreach (var c in scan)
        {
            c.Pred = 0;
            bool needDc = ss == 0 && ah == 0;
            bool needAc = se > 0 && !(progressive && ss == 0);
            if (needDc && dcTables[c.DcTable] == null) throw new ImageFormatException("JPEG : missing DC Huffman table");
            if ((needAc || (!progressive)) && acTables[c.AcTable] == null) throw new ImageFormatException("JPEG : missing AC Huffman table");
        }

        int eobRun = 0;
        void decodeBlock(Component c, int blockRow, int blockCol)
        {
            int offset = (blockRow * c.BlocksPerLine + blockCol) * 64;
            var coef = c.Coefficients.AsSpan(offset, 64);
            if (!progressive)
            {
                int t = reader.Decode(dcTables[c.DcTable]!);
                c.Pred += t == 0 ? 0 : reader.Extend(t);
                coef[0] = (short)c.Pred;
                var ac = acTables[c.AcTable]!;
                for (int k = 1; k < 64;)
                {
                    int rs = reader.Decode(ac);
                    int s = rs & 15, r = rs >> 4;
                    if (s == 0)
                    {
                        if (r < 15) break;
                        k += 16;
                        continue;
                    }
                    k += r;
                    if (k > 63) throw new ImageFormatException("JPEG : bad AC coefficients");
                    coef[ZigZag[k]] = (short)reader.Extend(s);
                    k++;
                }
                return;
            }

            if (ss == 0)
            {
                if (ah == 0)
                {
                    int t = reader.Decode(dcTables[c.DcTable]!);
                    c.Pred += t == 0 ? 0 : reader.Extend(t) * (1 << al);
                    coef[0] = (short)c.Pred;
                }
                else if (reader.Bit() != 0) coef[0] |= (short)(1 << al);
                return;
            }

            var table = acTables[c.AcTable]!;
            if (ah == 0)
            {
                // AC の最初
                if (eobRun > 0) { eobRun--; return; }
                for (int k = ss; k <= se;)
                {
                    int rs = reader.Decode(table);
                    int s = rs & 15, r = rs >> 4;
                    if (s == 0)
                    {
                        if (r < 15)
                        {
                            eobRun = (1 << r) - 1;
                            if (r > 0) eobRun += reader.Bits(r);
                            break;
                        }
                        k += 16;
                        continue;
                    }
                    k += r;
                    if (k > 63) throw new ImageFormatException("JPEG : bad AC coefficients");
                    coef[ZigZag[k]] = (short)(reader.Extend(s) * (1 << al));
                    k++;
                }
                return;
            }

            // AC の付け足し (stb_image の jpeg_decode_block_prog_ac と同じ手順)
            short bit = (short)(1 << al);
            if (eobRun > 0)
            {
                eobRun--;
                for (int k = ss; k <= se; k++)
                {
                    ref short p = ref coef[ZigZag[k]];
                    if (p != 0 && reader.Bit() != 0 && (p & bit) == 0) p += p > 0 ? bit : (short)-bit;
                }
                return;
            }
            int kk = ss;
            do
            {
                int rs = reader.Decode(table);
                int s = rs & 15, r = rs >> 4;
                if (s == 0)
                {
                    if (r < 15)
                    {
                        eobRun = (1 << r) - 1;
                        if (r > 0) eobRun += reader.Bits(r);
                        r = 64;   // ブロックの残りは付け足しの bit だけ
                    }
                    // r == 15 : 0 を 16 個 (下で 15 個進めて、s = 0 を書く)
                }
                else s = reader.Bit() != 0 ? bit : -bit;
                while (kk <= se)
                {
                    ref short p = ref coef[ZigZag[kk++]];
                    if (p != 0)
                    {
                        if (reader.Bit() != 0 && (p & bit) == 0) p += p > 0 ? bit : (short)-bit;
                    }
                    else
                    {
                        if (r == 0) { p = (short)s; break; }
                        r--;
                    }
                }
            } while (kk <= se);
        }

        int mcu = 0;
        void restartIfNeeded()
        {
            if (restartInterval > 0 && mcu > 0 && mcu % restartInterval == 0)
            {
                reader.Restart();
                foreach (var c in scan) c.Pred = 0;
                eobRun = 0;
            }
            mcu++;
        }

        if (scan.Length == 1)
        {
            // 1 成分のスキャン : MCU = 1 ブロック。並びは成分の本当の大きさ (MCU に合わせて広げた所は含まない)
            var c = scan[0];
            int blocksX = (c.Width + 7) / 8, blocksY = (c.Height + 7) / 8;
            for (int by = 0; by < blocksY; by++)
                for (int bx = 0; bx < blocksX; bx++)
                {
                    restartIfNeeded();
                    decodeBlock(c, by, bx);
                }
        }
        else
        {
            for (int my = 0; my < mcusY; my++)
                for (int mx = 0; mx < mcusX; mx++)
                {
                    restartIfNeeded();
                    foreach (var c in scan)
                        for (int v = 0; v < c.V; v++)
                            for (int h = 0; h < c.H; h++)
                                decodeBlock(c, my * c.V + v, mx * c.H + h);
                }
        }
    }
}

// ==========================================================================
// JPEG の書き出し (v2.3) : ベースライン、YCbCr 4:2:0 (または 4:4:4)、標準の量子化表を品質で拡大縮小 (IJG と同じ式)、
//   標準のハフマン表 (T.81 Annex K.3)。アルファは捨てる
// ==========================================================================
internal static class JpegEncoder
{
    private static readonly byte[] LumQuant =
    [
        16, 11, 10, 16, 24, 40, 51, 61, 12, 12, 14, 19, 26, 58, 60, 55, 14, 13, 16, 24, 40, 57, 69, 56, 14, 17, 22, 29, 51, 87, 80, 62,
        18, 22, 37, 56, 68, 109, 103, 77, 24, 35, 55, 64, 81, 104, 113, 92, 49, 64, 78, 87, 103, 121, 120, 101, 72, 92, 95, 98, 112, 100, 103, 99,
    ];
    private static readonly byte[] ChromQuant =
    [
        17, 18, 24, 47, 99, 99, 99, 99, 18, 21, 26, 66, 99, 99, 99, 99, 24, 26, 56, 99, 99, 99, 99, 99, 47, 66, 99, 99, 99, 99, 99, 99,
        99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99, 99,
    ];

    internal static readonly byte[] DcLumCounts = [0, 1, 5, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0, 0, 0];
    internal static readonly byte[] DcChromCounts = [0, 3, 1, 1, 1, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0, 0];
    internal static readonly byte[] DcValues = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9, 10, 11];
    internal static readonly byte[] AcLumCounts = [0, 2, 1, 3, 3, 2, 4, 3, 5, 5, 4, 4, 0, 0, 1, 0x7d];
    internal static readonly byte[] AcLumValues =
    [
        0x01, 0x02, 0x03, 0x00, 0x04, 0x11, 0x05, 0x12, 0x21, 0x31, 0x41, 0x06, 0x13, 0x51, 0x61, 0x07, 0x22, 0x71, 0x14, 0x32, 0x81, 0x91, 0xa1, 0x08,
        0x23, 0x42, 0xb1, 0xc1, 0x15, 0x52, 0xd1, 0xf0, 0x24, 0x33, 0x62, 0x72, 0x82, 0x09, 0x0a, 0x16, 0x17, 0x18, 0x19, 0x1a, 0x25, 0x26, 0x27, 0x28,
        0x29, 0x2a, 0x34, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58, 0x59,
        0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7a, 0x83, 0x84, 0x85, 0x86, 0x87, 0x88, 0x89,
        0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7, 0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4, 0xb5, 0xb6,
        0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3, 0xc4, 0xc5, 0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda, 0xe1, 0xe2,
        0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea, 0xf1, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8, 0xf9, 0xfa,
    ];
    internal static readonly byte[] AcChromCounts = [0, 2, 1, 2, 4, 4, 3, 4, 7, 5, 4, 4, 0, 1, 2, 0x77];
    internal static readonly byte[] AcChromValues =
    [
        0x00, 0x01, 0x02, 0x03, 0x11, 0x04, 0x05, 0x21, 0x31, 0x06, 0x12, 0x41, 0x51, 0x07, 0x61, 0x71, 0x13, 0x22, 0x32, 0x81, 0x08, 0x14, 0x42, 0x91,
        0xa1, 0xb1, 0xc1, 0x09, 0x23, 0x33, 0x52, 0xf0, 0x15, 0x62, 0x72, 0xd1, 0x0a, 0x16, 0x24, 0x34, 0xe1, 0x25, 0xf1, 0x17, 0x18, 0x19, 0x1a, 0x26,
        0x27, 0x28, 0x29, 0x2a, 0x35, 0x36, 0x37, 0x38, 0x39, 0x3a, 0x43, 0x44, 0x45, 0x46, 0x47, 0x48, 0x49, 0x4a, 0x53, 0x54, 0x55, 0x56, 0x57, 0x58,
        0x59, 0x5a, 0x63, 0x64, 0x65, 0x66, 0x67, 0x68, 0x69, 0x6a, 0x73, 0x74, 0x75, 0x76, 0x77, 0x78, 0x79, 0x7a, 0x82, 0x83, 0x84, 0x85, 0x86, 0x87,
        0x88, 0x89, 0x8a, 0x92, 0x93, 0x94, 0x95, 0x96, 0x97, 0x98, 0x99, 0x9a, 0xa2, 0xa3, 0xa4, 0xa5, 0xa6, 0xa7, 0xa8, 0xa9, 0xaa, 0xb2, 0xb3, 0xb4,
        0xb5, 0xb6, 0xb7, 0xb8, 0xb9, 0xba, 0xc2, 0xc3, 0xc4, 0xc5, 0xc6, 0xc7, 0xc8, 0xc9, 0xca, 0xd2, 0xd3, 0xd4, 0xd5, 0xd6, 0xd7, 0xd8, 0xd9, 0xda,
        0xe2, 0xe3, 0xe4, 0xe5, 0xe6, 0xe7, 0xe8, 0xe9, 0xea, 0xf2, 0xf3, 0xf4, 0xf5, 0xf6, 0xf7, 0xf8, 0xf9, 0xfa,
    ];

    // 記号 → (符号, 長さ)
    private sealed class Table
    {
        public readonly ushort[] Code = new ushort[256];
        public readonly byte[] Length = new byte[256];

        public Table(byte[] counts, byte[] values)
        {
            int code = 0, k = 0;
            for (int length = 1; length <= 16; length++)
            {
                for (int i = 0; i < counts[length - 1]; i++, k++, code++)
                {
                    Code[values[k]] = (ushort)code;
                    Length[values[k]] = (byte)length;
                }
                code <<= 1;
            }
        }
    }

    private static readonly Table DcLum = new(DcLumCounts, DcValues);
    private static readonly Table DcChrom = new(DcChromCounts, DcValues);
    private static readonly Table AcLum = new(AcLumCounts, AcLumValues);
    private static readonly Table AcChrom = new(AcChromCounts, AcChromValues);

    private static byte[] scaledQuant(byte[] base64, int quality)
    {
        quality = Math.Clamp(quality, 1, 100);
        int scale = quality < 50 ? 5000 / quality : 200 - quality * 2;
        var table = new byte[64];
        for (int i = 0; i < 64; i++) table[i] = (byte)Math.Clamp((base64[i] * scale + 50) / 100, 1, 255);
        return table;
    }

    public static byte[] Encode(ImageBuffer image, ImageSaveOptions options)
    {
        int width = image.Width, height = image.Height;
        if (width > 65535 || height > 65535) throw new ArgumentException("JPEG : image is larger than 65535");
        bool subsample = options.JpegSubsample;
        int mcuSize = subsample ? 16 : 8;
        int mcusX = (width + mcuSize - 1) / mcuSize, mcusY = (height + mcuSize - 1) / mcuSize;
        var lumQ = scaledQuant(LumQuant, options.JpegQuality);
        var chromQ = scaledQuant(ChromQuant, options.JpegQuality);
        int blocksPerMcu = subsample ? 6 : 3;
        var blocks = new short[mcusX * mcusY * blocksPerMcu * 64];   // 量子化した係数 (ジグザグの順)

        // 色の変換・間引き・DCT・量子化 (MCU の行ごとに並列)
        Parallel.For(0, mcusY, my =>
        {
            Span<float> y = stackalloc float[64 * 4];
            Span<float> cb = stackalloc float[64];
            Span<float> cr = stackalloc float[64];
            Span<float> temp = stackalloc float[64];
            Span<float> cbFull = stackalloc float[256];   // 4:2:0 の間引く前 (16x16)
            Span<float> crFull = stackalloc float[256];
            var px = image.Pixels;
            for (int mx = 0; mx < mcusX; mx++)
            {
                int baseIndex = (my * mcusX + mx) * blocksPerMcu * 64;
                if (subsample)
                {
                    for (int yy = 0; yy < 16; yy++)
                        for (int xx = 0; xx < 16; xx++)
                        {
                            var (Y, Cb, Cr) = ycc(px, width, height, mx * 16 + xx, my * 16 + yy);
                            int block = (yy >> 3) * 2 + (xx >> 3);
                            y[block * 64 + (yy & 7) * 8 + (xx & 7)] = Y;
                            cbFull[yy * 16 + xx] = Cb;
                            crFull[yy * 16 + xx] = Cr;
                        }
                    for (int yy = 0; yy < 8; yy++)
                        for (int xx = 0; xx < 8; xx++)
                        {
                            int i = yy * 2 * 16 + xx * 2;
                            cb[yy * 8 + xx] = (cbFull[i] + cbFull[i + 1] + cbFull[i + 16] + cbFull[i + 17]) * 0.25f;
                            cr[yy * 8 + xx] = (crFull[i] + crFull[i + 1] + crFull[i + 16] + crFull[i + 17]) * 0.25f;
                        }
                    for (int b = 0; b < 4; b++) forward(y.Slice(b * 64, 64), temp, lumQ, blocks.AsSpan(baseIndex + b * 64, 64));
                    forward(cb, temp, chromQ, blocks.AsSpan(baseIndex + 4 * 64, 64));
                    forward(cr, temp, chromQ, blocks.AsSpan(baseIndex + 5 * 64, 64));
                }
                else
                {
                    for (int yy = 0; yy < 8; yy++)
                        for (int xx = 0; xx < 8; xx++)
                        {
                            var (Y, Cb, Cr) = ycc(px, width, height, mx * 8 + xx, my * 8 + yy);
                            y[yy * 8 + xx] = Y; cb[yy * 8 + xx] = Cb; cr[yy * 8 + xx] = Cr;
                        }
                    forward(y[..64], temp, lumQ, blocks.AsSpan(baseIndex, 64));
                    forward(cb, temp, chromQ, blocks.AsSpan(baseIndex + 64, 64));
                    forward(cr, temp, chromQ, blocks.AsSpan(baseIndex + 128, 64));
                }
            }
        });

        using var output = new MemoryStream(width * height / 2 + 1024);
        void marker(int m) { output.WriteByte(0xFF); output.WriteByte((byte)m); }
        void u16(int v) { output.WriteByte((byte)(v >> 8)); output.WriteByte((byte)v); }

        marker(0xD8);   // SOI
        marker(0xE0); u16(16);   // APP0 JFIF
        output.Write("JFIF\0"u8);
        output.WriteByte(1); output.WriteByte(1); output.WriteByte(0); u16(1); u16(1); output.WriteByte(0); output.WriteByte(0);
        marker(0xDB); u16(2 + 65 * 2);   // DQT
        output.WriteByte(0);
        for (int k = 0; k < 64; k++) output.WriteByte(lumQ[JpegDecoder.ZigZag[k]]);
        output.WriteByte(1);
        for (int k = 0; k < 64; k++) output.WriteByte(chromQ[JpegDecoder.ZigZag[k]]);
        marker(0xC0); u16(17); output.WriteByte(8); u16(height); u16(width); output.WriteByte(3);   // SOF0
        output.WriteByte(1); output.WriteByte((byte)(subsample ? 0x22 : 0x11)); output.WriteByte(0);
        output.WriteByte(2); output.WriteByte(0x11); output.WriteByte(1);
        output.WriteByte(3); output.WriteByte(0x11); output.WriteByte(1);
        marker(0xC4);   // DHT
        u16(2 + (17 + 12) * 2 + (17 + 162) * 2);
        void dht(int classAndId, byte[] counts, byte[] values) { output.WriteByte((byte)classAndId); output.Write(counts); output.Write(values); }
        dht(0x00, DcLumCounts, DcValues);
        dht(0x10, AcLumCounts, AcLumValues);
        dht(0x01, DcChromCounts, DcValues);
        dht(0x11, AcChromCounts, AcChromValues);
        marker(0xDA); u16(12); output.WriteByte(3);   // SOS
        output.WriteByte(1); output.WriteByte(0x00);
        output.WriteByte(2); output.WriteByte(0x11);
        output.WriteByte(3); output.WriteByte(0x11);
        output.WriteByte(0); output.WriteByte(63); output.WriteByte(0);

        var writer = new JpegBitWriter(output);
        int predY = 0, predCb = 0, predCr = 0;
        for (int m = 0; m < mcusX * mcusY; m++)
        {
            int baseIndex = m * blocksPerMcu * 64;
            int lumBlocks = subsample ? 4 : 1;
            for (int b = 0; b < lumBlocks; b++) encodeBlock(writer, blocks.AsSpan(baseIndex + b * 64, 64), ref predY, DcLum, AcLum);
            encodeBlock(writer, blocks.AsSpan(baseIndex + lumBlocks * 64, 64), ref predCb, DcChrom, AcChrom);
            encodeBlock(writer, blocks.AsSpan(baseIndex + (lumBlocks + 1) * 64, 64), ref predCr, DcChrom, AcChrom);
        }
        writer.Flush();
        marker(0xD9);   // EOI
        return output.ToArray();
    }

    // 端の外は端の画素を延ばす
    private static (float Y, float Cb, float Cr) ycc(float[] px, int width, int height, int x, int y)
    {
        int i = (Math.Min(y, height - 1) * width + Math.Min(x, width - 1)) * 4;
        float r = Math.Clamp(px[i], 0f, 1f) * 255f, g = Math.Clamp(px[i + 1], 0f, 1f) * 255f, b = Math.Clamp(px[i + 2], 0f, 1f) * 255f;
        return (0.299f * r + 0.587f * g + 0.114f * b - 128f,
                -0.168736f * r - 0.331264f * g + 0.5f * b,
                0.5f * r - 0.418688f * g - 0.081312f * b);
    }

    // DCT (Basis の転置) → 量子化 → ジグザグに並べる
    private static void forward(Span<float> block, Span<float> temp, byte[] quant, Span<short> output)
    {
        var basis = JpegDecoder.Basis;
        for (int y = 0; y < 8; y++)
            for (int u = 0; u < 8; u++)
            {
                float sum = 0;
                for (int x = 0; x < 8; x++) sum += block[y * 8 + x] * basis[x * 8 + u];
                temp[y * 8 + u] = sum;
            }
        for (int v = 0; v < 8; v++)
            for (int u = 0; u < 8; u++)
            {
                float sum = 0;
                for (int y = 0; y < 8; y++) sum += temp[y * 8 + u] * basis[y * 8 + v];
                block[v * 8 + u] = sum;
            }
        for (int k = 0; k < 64; k++)
        {
            int natural = JpegDecoder.ZigZag[k];
            // ベースラインのハフマン表で書ける大きさ (AC は 10 bit、DC の差は 11 bit) に収める
            output[k] = (short)Math.Clamp((int)MathF.Round(block[natural] / quant[natural]), -1023, 1023);
        }
    }

    private static int category(int value)
    {
        value = Math.Abs(value);
        int bits = 0;
        while (value > 0) { bits++; value >>= 1; }
        return bits;
    }

    private static void writeValue(JpegBitWriter writer, int value, int size)
    {
        if (size == 0) return;
        writer.Write((uint)(value < 0 ? value + (1 << size) - 1 : value), size);
    }

    private static void encodeBlock(JpegBitWriter writer, ReadOnlySpan<short> zigzag, ref int pred, Table dc, Table ac)
    {
        int diff = zigzag[0] - pred;
        pred = zigzag[0];
        int s = category(diff);
        writer.Write(dc.Code[s], dc.Length[s]);
        writeValue(writer, diff, s);
        int run = 0;
        for (int k = 1; k < 64; k++)
        {
            int v = zigzag[k];
            if (v == 0) { run++; continue; }
            while (run > 15) { writer.Write(ac.Code[0xF0], ac.Length[0xF0]); run -= 16; }
            int size = category(v);
            int symbol = run << 4 | size;
            writer.Write(ac.Code[symbol], ac.Length[symbol]);
            writeValue(writer, v, size);
            run = 0;
        }
        if (run > 0) writer.Write(ac.Code[0x00], ac.Length[0x00]);
    }

    // 上の bit から書く。0xFF の後には 0x00 を入れる
    private sealed class JpegBitWriter(Stream output)
    {
        private uint _bits;
        private int _count;

        public void Write(uint value, int count)
        {
            _bits = _bits << count | (value & ((1u << count) - 1));
            _count += count;
            while (_count >= 8)
            {
                var b = (byte)(_bits >> (_count - 8));
                output.WriteByte(b);
                if (b == 0xFF) output.WriteByte(0);
                _count -= 8;
            }
            _bits &= (1u << _count) - 1;
        }

        public void Flush()
        {
            if (_count > 0) Write((1u << (8 - _count)) - 1, 8 - _count);   // 残りは 1 で埋める
        }
    }
}
