namespace GameEngine.Fbx;

// ==========================================================================
// zlib / deflate を戻す (AN1。外部のライブラリを使わずに書いた物。RFC 1950 / RFC 1951)
//   FBX のバイナリは大きな配列 (頂点 / 番号 / 法線 など) を zlib で縮めて入れる (配列の encoding = 1)
//   ブロック : 0 = そのまま / 1 = 決まったハフマン / 2 = 中で表を送るハフマン
//   ハフマンの読み方 : 10 bit までの符号は表を 1 回引くだけ (速い道)。それより長い符号は 1 bit ずつ (zlib の puff.c と同じ考え方)
//   最後に Adler-32 (zlib の確かめの数) を比べる。壊れたデータは FbxFormatException (落ちない)
// ==========================================================================
public static class Inflate
{
    /// <summary>zlib (2 byte の頭 + deflate + Adler-32) を戻す。expectedLength : 戻した大きさ (分かっていれば。違えばエラー)</summary>
    public static byte[] Zlib(ReadOnlySpan<byte> data, int expectedLength = -1)
    {
        if (data.Length == 0 && expectedLength <= 0) return Array.Empty<byte>();   // 空の配列 (.NET の ZLibStream は空を 0 byte で書く)
        if (data.Length < 6) throw new FbxFormatException("zlib : too short");
        int cmf = data[0], flg = data[1];
        if ((cmf & 0x0F) != 8 || (cmf >> 4) > 7) throw new FbxFormatException($"zlib : not deflate (CMF 0x{cmf:X2})");
        if ((cmf * 256 + flg) % 31 != 0) throw new FbxFormatException("zlib : bad header check");
        if ((flg & 0x20) != 0) throw new FbxFormatException("zlib : preset dictionary is not supported");
        var output = Deflate(data[2..], expectedLength, out var used);
        var end = 2 + used;
        if (end + 4 > data.Length) throw new FbxFormatException("zlib : missing Adler-32");
        var stored = (uint)(data[end] << 24 | data[end + 1] << 16 | data[end + 2] << 8 | data[end + 3]);
        var actual = Adler32(output);
        if (stored != actual) throw new FbxFormatException($"zlib : Adler-32 mismatch (0x{stored:X8} / 0x{actual:X8})");
        return output;
    }

    /// <summary>deflate だけを戻す。used = 読んだ byte の数</summary>
    public static byte[] Deflate(ReadOnlySpan<byte> data, int expectedLength, out int used)
    {
        var state = new State(data, expectedLength);
        bool last;
        do
        {
            last = state.Bits(1) == 1;
            switch (state.Bits(2))
            {
            case 0: state.Stored(); break;
            case 1: state.Codes(FixedLength, FixedDistance); break;
            case 2: state.Dynamic(); break;
            default: throw new FbxFormatException("deflate : bad block type 3");
            }
        } while (!last);
        used = state.BytesUsed;
        if (expectedLength >= 0 && state.OutLength != expectedLength) throw new FbxFormatException($"deflate : {state.OutLength} bytes, expected {expectedLength}");
        return state.Result();
    }

    public static uint Adler32(ReadOnlySpan<byte> data)
    {
        uint a = 1, b = 0;
        while (data.Length > 0)
        {
            var n = Math.Min(data.Length, 5552);   // この数までは足しても uint を越えない
            for (int i = 0; i < n; i++) { a += data[i]; b += a; }
            a %= 65521; b %= 65521;
            data = data[n..];
        }
        return b << 16 | a;
    }

    // ---------------------------------------------------------------------
    // ハフマンの表
    // ---------------------------------------------------------------------
    private const int FastBits = 10;

    private sealed class Huffman
    {
        public readonly short[] Count = new short[16];   // 長さごとの符号の数
        public readonly short[] Symbol;                  // 長さ → 符号の順に並べた記号
        public readonly int[] Fast = new int[1 << FastBits];   // 下位 10 bit → (記号 << 4) | 長さ。0 = 速い道に無い

        public Huffman(ReadOnlySpan<byte> lengths)
        {
            Symbol = new short[lengths.Length];
            foreach (var l in lengths) Count[l]++;
            Count[0] = 0;
            var left = 1;
            for (int len = 1; len < 16; len++)
            {
                left <<= 1;
                left -= Count[len];
                if (left < 0) throw new FbxFormatException("deflate : over-subscribed Huffman code");
            }
            Span<short> offs = stackalloc short[16];
            for (int len = 1; len < 15; len++) offs[len + 1] = (short)(offs[len] + Count[len]);
            for (int s = 0; s < lengths.Length; s++) if (lengths[s] != 0) Symbol[offs[lengths[s]]++] = (short)s;
            // 速い道の表 : 符号は上の bit から送られるが、読むのは下の bit から → 反転して入れる
            var code = 0;
            var index = 0;
            for (int len = 1; len <= FastBits; len++)
            {
                for (int k = 0; k < Count[len]; k++, index++, code++)
                {
                    var reversed = 0;
                    for (int b = 0; b < len; b++) reversed |= ((code >> b) & 1) << (len - 1 - b);
                    for (int i = reversed; i < Fast.Length; i += 1 << len) Fast[i] = Symbol[index] << 4 | len;
                }
                code <<= 1;
            }
        }
    }

    private static readonly Huffman FixedLength = makeFixedLength();
    private static readonly Huffman FixedDistance = makeFixedDistance();

    private static Huffman makeFixedLength()
    {
        var lengths = new byte[288];
        for (int i = 0; i < 144; i++) lengths[i] = 8;
        for (int i = 144; i < 256; i++) lengths[i] = 9;
        for (int i = 256; i < 280; i++) lengths[i] = 7;
        for (int i = 280; i < 288; i++) lengths[i] = 8;
        return new Huffman(lengths);
    }

    private static Huffman makeFixedDistance()
    {
        var lengths = new byte[30];
        lengths.AsSpan().Fill(5);
        return new Huffman(lengths);
    }

    private static readonly short[] LengthBase  = { 3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258 };
    private static readonly short[] LengthExtra = { 0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0 };
    private static readonly short[] DistBase    = { 1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577 };
    private static readonly short[] DistExtra   = { 0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13 };
    private static readonly byte[] CodeLengthOrder = { 16, 17, 18, 0, 8, 7, 9, 6, 10, 5, 11, 4, 12, 3, 13, 2, 14, 1, 15 };

    // ---------------------------------------------------------------------
    // 読む物 (ref struct : Span を持つ)
    // ---------------------------------------------------------------------
    private ref struct State
    {
        private readonly ReadOnlySpan<byte> _in;
        private int _pos;
        private ulong _bitBuffer;
        private int _bitCount;
        private int _overrun;          // 終わりの後に 0 として読んだ byte の数 (使ってしまったらエラー)
        private byte[] _out;
        private int _outPos;
        private readonly bool _fixedSize;

        public State(ReadOnlySpan<byte> input, int expectedLength)
        {
            _in = input;
            _pos = 0; _bitBuffer = 0; _bitCount = 0; _overrun = 0; _outPos = 0;
            _fixedSize = expectedLength >= 0;
            _out = new byte[Math.Max(expectedLength, _fixedSize ? 0 : Math.Max(64, input.Length * 4))];
        }

        public int OutLength => _outPos;
        public int BytesUsed => _pos - (_bitCount >> 3);   // まだ使っていない byte は返す
        public byte[] Result() => _out.Length == _outPos ? _out : _out.AsSpan(0, _outPos).ToArray();

        private void fill(int need)
        {
            while (_bitCount < need)
            {
                ulong b;
                if (_pos < _in.Length) b = _in[_pos];
                else { b = 0; _overrun++; }
                _pos++;
                _bitBuffer |= b << _bitCount;
                _bitCount += 8;
            }
        }

        public int Bits(int n)
        {
            if (n == 0) return 0;
            fill(n);
            var v = (int)(_bitBuffer & ((1UL << n) - 1));
            _bitBuffer >>= n;
            _bitCount -= n;
            if (_pos - (_bitCount >> 3) > _in.Length) throw new FbxFormatException("deflate : unexpected end of data");
            return v;
        }

        private int decode(Huffman h)
        {
            fill(FastBits);
            var entry = h.Fast[(int)(_bitBuffer & ((1 << FastBits) - 1))];
            if (entry != 0)
            {
                var len = entry & 15;
                _bitBuffer >>= len;
                _bitCount -= len;
                if (_pos - (_bitCount >> 3) > _in.Length) throw new FbxFormatException("deflate : unexpected end of data");
                return entry >> 4;
            }
            // 長い符号 : 1 bit ずつ (puff.c と同じ)
            int code = 0, first = 0, index = 0;
            for (int len = 1; len < 16; len++)
            {
                code |= Bits(1);
                int count = h.Count[len];
                if (code - count < first) return h.Symbol[index + (code - first)];
                index += count;
                first += count;
                first <<= 1;
                code <<= 1;
            }
            throw new FbxFormatException("deflate : invalid Huffman code");
        }

        private void ensure(int more)
        {
            if (_outPos + more <= _out.Length) return;
            if (_fixedSize) throw new FbxFormatException("deflate : output is longer than expected");
            Array.Resize(ref _out, Math.Max(_out.Length * 2, _outPos + more));
        }

        public void Stored()
        {
            // byte の区切りまで捨てる
            var drop = _bitCount & 7;
            _bitBuffer >>= drop;
            _bitCount -= drop;
            var len = Bits(16);
            var nlen = Bits(16);
            if ((len ^ 0xFFFF) != nlen) throw new FbxFormatException("deflate : stored block length check failed");
            ensure(len);
            while (len > 0 && _bitCount > 0) { _out[_outPos++] = (byte)Bits(8); len--; }
            if (_pos + len > _in.Length) throw new FbxFormatException("deflate : stored block runs past the end");
            _in.Slice(_pos, len).CopyTo(_out.AsSpan(_outPos));
            _pos += len;
            _outPos += len;
        }

        public void Codes(Huffman lengthCode, Huffman distanceCode)
        {
            while (true)
            {
                var symbol = decode(lengthCode);
                if (symbol < 256)
                {
                    ensure(1);
                    _out[_outPos++] = (byte)symbol;
                    continue;
                }
                if (symbol == 256) return;
                symbol -= 257;
                if (symbol >= 29) throw new FbxFormatException("deflate : bad length symbol");
                var length = LengthBase[symbol] + Bits(LengthExtra[symbol]);
                var d = decode(distanceCode);
                if (d >= 30) throw new FbxFormatException("deflate : bad distance symbol");
                var distance = DistBase[d] + Bits(DistExtra[d]);
                if (distance > _outPos) throw new FbxFormatException("deflate : distance too far back");
                ensure(length);
                var from = _outPos - distance;
                if (distance >= length)
                {
                    _out.AsSpan(from, length).CopyTo(_out.AsSpan(_outPos));
                    _outPos += length;
                }
                else
                {
                    for (int i = 0; i < length; i++) _out[_outPos++] = _out[from + i];   // 重なる時は 1 byte ずつ (繰り返しになる)
                }
            }
        }

        public void Dynamic()
        {
            var nlen = Bits(5) + 257;
            var ndist = Bits(5) + 1;
            var ncode = Bits(4) + 4;
            if (nlen > 286 || ndist > 30) throw new FbxFormatException("deflate : bad code counts");
            Span<byte> lengths = stackalloc byte[320];
            lengths.Clear();
            for (int i = 0; i < ncode; i++) lengths[CodeLengthOrder[i]] = (byte)Bits(3);
            var lengthCode = new Huffman(lengths[..19]);
            var index = 0;
            while (index < nlen + ndist)
            {
                var symbol = decode(lengthCode);
                if (symbol < 16) { lengths[index++] = (byte)symbol; continue; }
                byte value = 0;
                int repeat;
                if (symbol == 16)
                {
                    if (index == 0) throw new FbxFormatException("deflate : repeat with no first length");
                    value = lengths[index - 1];
                    repeat = 3 + Bits(2);
                }
                else if (symbol == 17) repeat = 3 + Bits(3);
                else repeat = 11 + Bits(7);
                if (index + repeat > nlen + ndist) throw new FbxFormatException("deflate : too many lengths");
                while (repeat-- > 0) lengths[index++] = value;
            }
            if (lengths[256] == 0) throw new FbxFormatException("deflate : no end-of-block code");
            Codes(new Huffman(lengths[..nlen]), new Huffman(lengths.Slice(nlen, ndist)));
        }
    }
}

/// <summary>FBX / zlib のデータが読めない (壊れている / 対応していない形)</summary>
public sealed class FbxFormatException : Exception
{
    public FbxFormatException(string message) : base(message) { }
}
