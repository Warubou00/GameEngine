namespace GameEngine.Imaging.Codecs;

// ==========================================================================
// zlib / deflate に縮める (v2.3。PNG の書き出し用。RFC 1950 / RFC 1951)
//   LZ77 : 3 byte のハッシュの鎖で、32 KB の窓の中から一番長い一致を探す (欲張り。鎖をたどる数は level で決める)
//   ハフマン : ブロックごとに頻度から作る (動的ハフマン)。符号の長さは 15 bit (符号の長さの符号は 7 bit) までに抑える
//     (抑え方は miniz の tdefl_huffman_enforce_max_code_size と同じ : 長さごとの数を直してから、頻度の高い順に短い長さを配る)
//   戻すのは Inflate (Runtime の Inflate.cs をリンク)。テストで往復を確かめる
// ==========================================================================
internal static class Deflater
{
    private const int WindowSize = 32768;
    private const int WindowMask = WindowSize - 1;
    private const int HashBits = 15;
    private const int MinMatch = 3;
    private const int MaxMatch = 258;
    private const int BlockTokens = 1 << 15;   // 1 ブロックに入れる記号の数

    internal static readonly int[] LengthBase = [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258];
    internal static readonly int[] LengthExtra = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];
    internal static readonly int[] DistBase = [1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577];
    internal static readonly int[] DistExtra = [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13];
    private static readonly int[] CodeLengthOrder = [16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15];

    private static readonly byte[] LengthCodeOf = buildLengthCodes();   // 長さ (3〜258) → 0〜28
    private static readonly byte[] DistCodeOf = buildDistCodes();       // 距離 (1〜32768) → 0〜29

    private static byte[] buildLengthCodes()
    {
        var table = new byte[MaxMatch + 1];
        for (int code = 0; code < LengthBase.Length; code++)
        {
            int end = code + 1 < LengthBase.Length ? LengthBase[code + 1] : MaxMatch + 1;
            if (code == LengthBase.Length - 1) end = MaxMatch + 1;
            for (int length = LengthBase[code]; length < end && length <= MaxMatch; length++) table[length] = (byte)code;
        }
        table[MaxMatch] = (byte)(LengthBase.Length - 1);   // 258 は専用の符号 285
        return table;
    }

    private static byte[] buildDistCodes()
    {
        var table = new byte[WindowSize + 1];
        for (int code = 0; code < DistBase.Length; code++)
        {
            int end = code + 1 < DistBase.Length ? DistBase[code + 1] : WindowSize + 1;
            for (int d = DistBase[code]; d < end; d++) table[d] = (byte)code;
        }
        return table;
    }

    /// <summary>zlib に縮める。level : 1 (速い) 〜 9 (よく縮む)</summary>
    public static byte[] Zlib(ReadOnlySpan<byte> data, int level = 6)
    {
        var writer = new BitWriter(data.Length / 2 + 64);
        writer.WriteByte(0x78);
        writer.WriteByte(0x9C);   // 既定の圧縮 (FCHECK : 0x789C % 31 == 0)
        Deflate(data, writer, level);
        writer.Flush();
        var adler = Inflate.Adler32(data);
        writer.WriteByte((byte)(adler >> 24));
        writer.WriteByte((byte)(adler >> 16));
        writer.WriteByte((byte)(adler >> 8));
        writer.WriteByte((byte)adler);
        return writer.ToArray();
    }

    internal static void Deflate(ReadOnlySpan<byte> data, BitWriter writer, int level)
    {
        int maxChain = Math.Clamp(level, 1, 9) switch { 1 => 4, 2 => 8, 3 => 16, 4 => 32, 5 => 64, 6 => 128, 7 => 256, 8 => 1024, _ => 4096 };
        int niceLength = level >= 8 ? MaxMatch : level >= 5 ? 128 : 32;   // これだけ一致すれば探すのをやめる
        var head = new int[1 << HashBits];
        Array.Fill(head, -1);
        var prev = new int[WindowSize];
        var tokens = new List<int>(BlockTokens);   // 文字 = 0〜255 / 一致 = (長さ << 16) | 距離 (長さ >= 3 なので文字と重ならない)
        int n = data.Length, pos = 0;

        while (pos < n)
        {
            int bestLength = 0, bestDist = 0;
            if (pos + MinMatch <= n)
            {
                int hash = hashOf(data, pos);
                int candidate = head[hash];
                int chain = maxChain;
                int limit = Math.Min(MaxMatch, n - pos);
                while (candidate >= 0 && pos - candidate <= WindowSize && chain-- > 0)
                {
                    // 今の一番長い所の次の byte が違えば、それより長くならない (先に見て速くする)
                    if (data[candidate + bestLength] == data[pos + bestLength] && data[candidate] == data[pos])
                    {
                        int length = 0;
                        while (length < limit && data[candidate + length] == data[pos + length]) length++;
                        if (length > bestLength)
                        {
                            bestLength = length;
                            bestDist = pos - candidate;
                            if (length >= niceLength || length == limit) break;
                        }
                    }
                    int next = prev[candidate & WindowMask];
                    if (next >= candidate) break;   // 窓を一回りした古い所 (鎖が切れている)
                    candidate = next;
                }
                insert(data, pos, head, prev);
            }

            if (bestLength >= MinMatch)
            {
                tokens.Add(bestLength << 16 | bestDist);
                // 一致の中の位置もハッシュに入れる (次の一致を見つけやすく)
                int end = pos + bestLength;
                for (int p = pos + 1; p < end && p + MinMatch <= n; p++) insert(data, p, head, prev);
                pos = end;
            }
            else
            {
                tokens.Add(data[pos]);
                pos++;
            }

            if (tokens.Count >= BlockTokens)
            {
                writeBlock(writer, tokens, final: pos >= n);
                tokens.Clear();
            }
        }
        if (tokens.Count > 0 || n == 0) writeBlock(writer, tokens, final: true);
    }

    private static int hashOf(ReadOnlySpan<byte> data, int pos) =>
        (int)(((uint)(data[pos] << 16 | data[pos + 1] << 8 | data[pos + 2]) * 2654435761u) >> (32 - HashBits));

    private static void insert(ReadOnlySpan<byte> data, int pos, int[] head, int[] prev)
    {
        int hash = hashOf(data, pos);
        prev[pos & WindowMask] = head[hash];
        head[hash] = pos;
    }

    // ---------------------------------------------------------------------
    // 1 ブロック (動的ハフマン)
    // ---------------------------------------------------------------------

    private static void writeBlock(BitWriter writer, List<int> tokens, bool final)
    {
        var litFreq = new int[286];
        var distFreq = new int[30];
        foreach (var token in tokens)
        {
            if (token < 256) litFreq[token]++;
            else
            {
                litFreq[257 + LengthCodeOf[token >> 16]]++;
                distFreq[DistCodeOf[token & 0xFFFF]]++;
            }
        }
        litFreq[256] = 1;   // ブロックの終わり

        var litLengths = BuildLengths(litFreq, 15);
        var distLengths = BuildLengths(distFreq, 15);
        int numLit = 286;
        while (numLit > 257 && litLengths[numLit - 1] == 0) numLit--;
        int numDist = 30;
        while (numDist > 1 && distLengths[numDist - 1] == 0) numDist--;

        // 符号の長さの並びを 16 / 17 / 18 で縮める
        var all = new byte[numLit + numDist];
        litLengths.AsSpan(0, numLit).CopyTo(all);
        distLengths.AsSpan(0, numDist).CopyTo(all.AsSpan(numLit));
        var clSymbols = new List<int>();   // (記号) | (追加の値 << 8)
        var clFreq = new int[19];
        for (int i = 0; i < all.Length;)
        {
            int value = all[i], run = 1;
            while (i + run < all.Length && all[i + run] == value) run++;
            int left = run;
            if (value == 0)
            {
                while (left >= 11) { int r = Math.Min(left, 138); clSymbols.Add(18 | (r - 11) << 8); clFreq[18]++; left -= r; }
                if (left >= 3) { clSymbols.Add(17 | (left - 3) << 8); clFreq[17]++; left = 0; }
            }
            else
            {
                clSymbols.Add(value); clFreq[value]++; left--;
                while (left >= 3) { int r = Math.Min(left, 6); clSymbols.Add(16 | (r - 3) << 8); clFreq[16]++; left -= r; }
            }
            for (; left > 0; left--) { clSymbols.Add(value); clFreq[value]++; }
            i += run;
        }
        var clLengths = BuildLengths(clFreq, 7);
        int numCl = 19;
        while (numCl > 4 && clLengths[CodeLengthOrder[numCl - 1]] == 0) numCl--;

        var litCodes = canonicalCodes(litLengths);
        var distCodes = canonicalCodes(distLengths);
        var clCodes = canonicalCodes(clLengths);

        writer.WriteBits(final ? 1u : 0u, 1);
        writer.WriteBits(2, 2);   // 動的ハフマン
        writer.WriteBits((uint)(numLit - 257), 5);
        writer.WriteBits((uint)(numDist - 1), 5);
        writer.WriteBits((uint)(numCl - 4), 4);
        for (int i = 0; i < numCl; i++) writer.WriteBits(clLengths[CodeLengthOrder[i]], 3);
        foreach (var item in clSymbols)
        {
            int symbol = item & 0xFF, extra = item >> 8;
            writer.WriteBits(clCodes[symbol], clLengths[symbol]);
            if (symbol == 16) writer.WriteBits((uint)extra, 2);
            else if (symbol == 17) writer.WriteBits((uint)extra, 3);
            else if (symbol == 18) writer.WriteBits((uint)extra, 7);
        }

        foreach (var token in tokens)
        {
            if (token < 256)
            {
                writer.WriteBits(litCodes[token], litLengths[token]);
                continue;
            }
            int length = token >> 16, dist = token & 0xFFFF;
            int lc = LengthCodeOf[length];
            writer.WriteBits(litCodes[257 + lc], litLengths[257 + lc]);
            if (LengthExtra[lc] > 0) writer.WriteBits((uint)(length - LengthBase[lc]), LengthExtra[lc]);
            int dc = DistCodeOf[dist];
            writer.WriteBits(distCodes[dc], distLengths[dc]);
            if (DistExtra[dc] > 0) writer.WriteBits((uint)(dist - DistBase[dc]), DistExtra[dc]);
        }
        writer.WriteBits(litCodes[256], litLengths[256]);
    }

    // ---------------------------------------------------------------------
    // ハフマンの符号の長さ (使う記号が 1 つでも、符号が欠けないように 2 つにする)
    // ---------------------------------------------------------------------

    internal static byte[] BuildLengths(int[] freq, int maxBits)
    {
        var lengths = new byte[freq.Length];
        var used = new List<int>();
        for (int i = 0; i < freq.Length; i++) if (freq[i] > 0) used.Add(i);
        if (used.Count == 0) { used.Add(0); used.Add(1); }
        else if (used.Count == 1) used.Add(used[0] == 0 ? 1 : 0);
        long freqOf(int symbol) => Math.Max(1, freq[symbol]);

        // ハフマンの木 (葉 = 0..count-1、節 = count..)。深さ = 符号の長さ
        int count = used.Count;
        var parent = new int[count * 2];
        var queue = new PriorityQueue<int, (long, int)>();
        for (int i = 0; i < count; i++) queue.Enqueue(i, (freqOf(used[i]), i));
        int nextNode = count;
        while (queue.Count > 1)
        {
            queue.TryDequeue(out var a, out var pa);
            queue.TryDequeue(out var b, out var pb);
            parent[a] = nextNode;
            parent[b] = nextNode;
            queue.Enqueue(nextNode, (pa.Item1 + pb.Item1, nextNode));
            nextNode++;
        }
        int root = nextNode - 1;
        var depth = new int[count];
        int maxDepth = 0;
        for (int i = 0; i < count; i++)
        {
            int d = 0;
            for (int node = i; node != root; node = parent[node]) d++;
            depth[i] = d;
            maxDepth = Math.Max(maxDepth, d);
        }

        // 長さごとの数を maxBits に収める
        var countOf = new int[Math.Max(maxDepth, maxBits) + 1];
        foreach (var d in depth) countOf[d]++;
        if (maxDepth > maxBits)
        {
            for (int i = maxBits + 1; i <= maxDepth; i++) { countOf[maxBits] += countOf[i]; countOf[i] = 0; }
            long total = 0;
            for (int i = 1; i <= maxBits; i++) total += (long)countOf[i] << (maxBits - i);
            while (total != 1L << maxBits)
            {
                countOf[maxBits]--;
                for (int i = maxBits - 1; i > 0; i--)
                {
                    if (countOf[i] == 0) continue;
                    countOf[i]--;
                    countOf[i + 1] += 2;
                    break;
                }
                total--;
            }
        }

        // 頻度の高い順に短い長さを配る
        var order = Enumerable.Range(0, count).OrderByDescending(i => freqOf(used[i])).ThenBy(i => used[i]).ToArray();
        int k = 0;
        for (int length = 1; length < countOf.Length; length++)
            for (int c = 0; c < countOf[length]; c++) lengths[used[order[k++]]] = (byte)length;
        return lengths;
    }

    // 長さ → 正準ハフマンの符号 (書く順に bit を逆にしてある = BitWriter は下の bit から書く)
    private static uint[] canonicalCodes(byte[] lengths)
    {
        int maxLength = 0;
        foreach (var l in lengths) maxLength = Math.Max(maxLength, l);
        var countOf = new int[maxLength + 1];
        foreach (var l in lengths) if (l > 0) countOf[l]++;
        var next = new uint[maxLength + 2];
        uint code = 0;
        for (int bits = 1; bits <= maxLength; bits++)
        {
            code = (code + (uint)countOf[bits - 1]) << 1;
            next[bits] = code;
        }
        var codes = new uint[lengths.Length];
        for (int i = 0; i < lengths.Length; i++)
        {
            int len = lengths[i];
            if (len == 0) continue;
            uint c = next[len]++;
            uint reversed = 0;
            for (int b = 0; b < len; b++) { reversed = reversed << 1 | (c & 1); c >>= 1; }
            codes[i] = reversed;
        }
        return codes;
    }
}

/// <summary>下の bit から詰めて書く (deflate の並び)</summary>
internal sealed class BitWriter
{
    private byte[] _buffer;
    private int _length;
    private ulong _bits;
    private int _count;

    public BitWriter(int capacity) => _buffer = new byte[Math.Max(capacity, 64)];

    public void WriteBits(uint value, int count)
    {
        if (count == 0) return;
        _bits |= (ulong)(value & ((1u << count) - 1)) << _count;
        _count += count;
        while (_count >= 8)
        {
            put((byte)_bits);
            _bits >>= 8;
            _count -= 8;
        }
    }

    /// <summary>残りの bit を byte の区切りまで書く</summary>
    public void Flush()
    {
        if (_count > 0) put((byte)_bits);
        _bits = 0;
        _count = 0;
    }

    public void WriteByte(byte value)
    {
        Flush();
        put(value);
    }

    private void put(byte value)
    {
        if (_length == _buffer.Length) Array.Resize(ref _buffer, _buffer.Length * 2);
        _buffer[_length++] = value;
    }

    public byte[] ToArray() => _buffer.AsSpan(0, _length).ToArray();
}

/// <summary>CRC-32 (PNG のチャンクの確かめ)</summary>
internal static class Crc32
{
    private static readonly uint[] Table = Enumerable.Range(0, 256).Select(n =>
    {
        uint c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    public static uint Update(uint crc, ReadOnlySpan<byte> data)
    {
        var table = Table;
        foreach (var b in data) crc = table[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return crc;
    }

    public static uint Compute(ReadOnlySpan<byte> data) => Update(0xFFFFFFFFu, data) ^ 0xFFFFFFFFu;
}

/// <summary>zlib の圧縮 / 展開 (自前。PNG の中身と同じ形式。外部のライブラリは使わない)</summary>
public static class Zlib
{
    /// <summary>縮める。level : 1 (速い) 〜 9 (よく縮む)</summary>
    public static byte[] Compress(ReadOnlySpan<byte> data, int level = 6) => Deflater.Zlib(data, level);

    /// <summary>戻す。壊れている時は ImageFormatException</summary>
    public static byte[] Decompress(ReadOnlySpan<byte> data)
    {
        try { return Inflate.Zlib(data); }
        catch (FbxFormatException ex) { throw new ImageFormatException(ex.Message, ex); }
    }
}
