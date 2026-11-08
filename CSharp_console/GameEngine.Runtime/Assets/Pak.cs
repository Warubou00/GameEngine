using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GameEngine;

// ==========================================================================
// .pak : 書き出したゲームのアセットをまとめる独自の形 (v1.3)
//   エディターの File > Build Game が、Data (シーン / アセット / 設定 / キャッシュ) と Shader と Game.dll を 1 つの Data.pak にまとめる。
//   Player は起動の時に一時フォルダ (隠し) へ展開して使い、終わる時に消す (Game.dll はファイルにせず、メモリから読む)
//
//   形 :
//     [見出し 48 byte] "TSMGPAK\0" / 版 (1) / 予備 / 塩 (16 byte。書き出すたびに違う) / 一覧の位置 / 一覧の大きさ (詰めた後 / 元)
//     [中身] ファイルごとに Brotli で詰めて (詰めても小さくならない物 (jpg / png など) はそのまま)、鍵の流れで混ぜた物
//     [一覧] 名前 / 位置 / 大きさ / 詰め方 / CRC32。一覧も Brotli + 混ぜる (中を見ても名前が読めない)
//   同じ中身のファイルは 1 回だけ入れる (一覧は同じ位置を指す)
//   ※混ぜるのは「開けばすぐ読める」のを防ぐため (暗号ではない。鍵は Player の中にあるので、本気で解析すれば読める)
// ==========================================================================
public static class Pak
{
    public const string FileName = "Data.pak";
    private static readonly byte[] Magic = "TSMGPAK\0"u8.ToArray();
    private const int Version = 1;
    private const int HeaderSize = 48;
    private const byte Stored = 0, Brotli = 1;

    public sealed record Entry(string Name, long Offset, int StoredLength, int Length, byte Method, uint Crc);

    // ---------------------------------------------------------------------
    // 書く
    // ---------------------------------------------------------------------

    /// <summary>書いた結果 (確認 / ログ用)</summary>
    public sealed record WriteResult(int Files, int UniqueFiles, long OriginalBytes, long PakBytes, double Milliseconds);

    /// <summary>
    /// files (pak の中の名前 → ディスクのファイル) を pakPath に書く。名前は "/" 区切り。
    /// quality : Brotli の詰め方 (0〜11。大きいほど小さく遅い)
    /// </summary>
    public static WriteResult Write(string pakPath, IReadOnlyList<(string Name, string File)> files, int quality = 9)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = keyOf(salt);
        // 読んで、同じ中身をまとめ、詰める (詰めるのは並列で)
        var contents = files.Select(f => File.ReadAllBytes(f.File)).ToArray();
        var hashes = contents.Select(c => Convert.ToHexString(SHA256.HashData(c))).ToArray();
        var unique = new Dictionary<string, int>();   // 中身のハッシュ → 最初のファイルの番号
        for (int i = 0; i < contents.Length; i++) unique.TryAdd(hashes[i], i);
        var blobs = new (byte[] Data, byte Method)[contents.Length];
        Parallel.ForEach(unique.Values, i =>
        {
            var packed = compress(contents[i], quality);
            blobs[i] = packed != null && packed.Length < contents[i].Length * 0.97 ? (packed, Brotli) : (contents[i], Stored);
        });

        using var stream = new FileStream(pakPath, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 20);
        stream.Write(new byte[HeaderSize]);
        var entries = new List<Entry>(files.Count);
        var written = new Dictionary<int, (long Offset, int Length, byte Method)>();
        for (int i = 0; i < files.Count; i++)
        {
            var first = unique[hashes[i]];
            if (!written.TryGetValue(first, out var at))
            {
                var data = (byte[])blobs[first].Data.Clone();
                at = (stream.Position, data.Length, blobs[first].Method);
                scramble(data, key, (ulong)at.Offset);
                stream.Write(data);
                written[first] = at;
            }
            entries.Add(new Entry(files[i].Name.Replace('\\', '/'), at.Offset, at.Length, contents[i].Length, at.Method, crc32(contents[i])));
        }
        // 一覧
        var table = new MemoryStream();
        using (var w = new BinaryWriter(table, Encoding.UTF8, leaveOpen: true))
        {
            w.Write(entries.Count);
            foreach (var e in entries)
            {
                w.Write(e.Name);
                w.Write(e.Offset);
                w.Write(e.StoredLength);
                w.Write(e.Length);
                w.Write(e.Method);
                w.Write(e.Crc);
            }
        }
        var tableBytes = table.ToArray();
        var packedTable = compress(tableBytes, quality) ?? throw new InvalidOperationException("Cannot compress the pak table");
        var tableOffset = stream.Position;
        scramble(packedTable, key ^ 0x5441424C45ul, (ulong)tableOffset);
        stream.Write(packedTable);
        // 見出し
        var header = new byte[HeaderSize];
        Magic.CopyTo(header, 0);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(8), Version);
        salt.CopyTo(header, 16);
        BinaryPrimitives.WriteInt64LittleEndian(header.AsSpan(32), tableOffset);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(40), packedTable.Length);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(44), tableBytes.Length);
        stream.Position = 0;
        stream.Write(header);
        var pakBytes = stream.Length;
        return new WriteResult(files.Count, unique.Count, contents.Sum(c => (long)c.Length), pakBytes,
                               System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }

    private static byte[]? compress(byte[] data, int quality)
    {
        // 空のファイルなどで詰められない時は null (そのまま入れる。以前は例外で書き出しが止まった)
        if (data.Length == 0) return null;
        var buffer = new byte[Math.Max(64, BrotliEncoder.GetMaxCompressedLength(data.Length))];
        return BrotliEncoder.TryCompress(data, buffer, out var size, quality, 22) ? buffer.AsSpan(0, size).ToArray() : null;
    }

    // ---------------------------------------------------------------------
    // 読む
    // ---------------------------------------------------------------------

    public sealed class Reader : IDisposable
    {
        private readonly SafeFileHandle _file;
        private readonly ulong _key;
        public IReadOnlyList<Entry> Entries { get; }

        public Reader(string pakPath)
        {
            _file = File.OpenHandle(pakPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            var header = new byte[HeaderSize];
            RandomAccess.Read(_file, header, 0);
            if (!header.AsSpan(0, 8).SequenceEqual(Magic)) throw new InvalidDataException($"Not a pak file : {pakPath}");
            if (BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(8)) != Version) throw new InvalidDataException($"Unknown pak version : {pakPath}");
            _key = keyOf(header.AsSpan(16, 16).ToArray());
            var tableOffset = BinaryPrimitives.ReadInt64LittleEndian(header.AsSpan(32));
            var packed = new byte[BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(40))];
            var tableLength = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(44));
            RandomAccess.Read(_file, packed, tableOffset);
            scramble(packed, _key ^ 0x5441424C45ul, (ulong)tableOffset);
            var table = decompress(packed, tableLength);
            using var r = new BinaryReader(new MemoryStream(table), Encoding.UTF8);
            var count = r.ReadInt32();
            var entries = new List<Entry>(count);
            for (int i = 0; i < count; i++) entries.Add(new Entry(r.ReadString(), r.ReadInt64(), r.ReadInt32(), r.ReadInt32(), r.ReadByte(), r.ReadUInt32()));
            Entries = entries;
        }

        public bool Contains(string name) => Entries.Any(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>1 つのファイルの中身 (無ければ null)。CRC が合わなければ例外</summary>
        public byte[]? ReadAllBytes(string name) =>
            Entries.FirstOrDefault(e => e.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) is { } entry ? read(entry) : null;

        private byte[] read(Entry entry)
        {
            var data = new byte[entry.StoredLength];
            RandomAccess.Read(_file, data, entry.Offset);
            scramble(data, _key, (ulong)entry.Offset);
            var result = entry.Method == Brotli ? decompress(data, entry.Length) : data;
            if (crc32(result) != entry.Crc) throw new InvalidDataException($"The pak is broken : {entry.Name}");
            return result;
        }

        /// <summary>全部 (skip が true の物を除く) を folder に書き出す (並列)。戻り値 = 書いた数 / 大きさ</summary>
        public (int Files, long Bytes) ExtractAll(string folder, Func<string, bool>? skip = null)
        {
            var targets = Entries.Where(e => skip == null || !skip(e.Name)).ToList();
            long bytes = 0;
            Parallel.ForEach(targets, entry =>
            {
                var path = Path.Combine(folder, entry.Name.Replace('/', Path.DirectorySeparatorChar));
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                var data = read(entry);
                File.WriteAllBytes(path, data);
                Interlocked.Add(ref bytes, data.Length);
            });
            return (targets.Count, bytes);
        }

        public void Dispose() => _file.Dispose();
    }

    private static byte[] decompress(byte[] data, int length)
    {
        var result = new byte[length];
        if (!BrotliDecoder.TryDecompress(data, result, out var written) || written != length) throw new InvalidDataException("The pak is broken (decompress)");
        return result;
    }

    // ---------------------------------------------------------------------
    // 混ぜる (同じ物をもう一度かけると戻る)。鍵の流れは SplitMix64 (位置ごとに違う)
    // ---------------------------------------------------------------------

    private static ulong keyOf(byte[] salt)
    {
        var hash = SHA256.HashData([.. salt, .. "Tsumugi Engine pak v1"u8]);
        return BinaryPrimitives.ReadUInt64LittleEndian(hash);
    }

    private static void scramble(Span<byte> data, ulong key, ulong position)
    {
        var state = key ^ (position * 0x9E3779B97F4A7C15ul);
        int i = 0;
        for (; i + 8 <= data.Length; i += 8)
        {
            var value = BinaryPrimitives.ReadUInt64LittleEndian(data[i..]) ^ next(ref state);
            BinaryPrimitives.WriteUInt64LittleEndian(data[i..], value);
        }
        if (i < data.Length)
        {
            var last = next(ref state);
            for (; i < data.Length; i++, last >>= 8) data[i] ^= (byte)last;
        }
    }

    private static ulong next(ref ulong state)
    {
        var z = state += 0x9E3779B97F4A7C15ul;
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9ul;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBul;
        return z ^ (z >> 31);
    }

    // CRC32 (壊れていないかの確かめ)
    private static readonly uint[] CrcTable = Enumerable.Range(0, 256).Select(n =>
    {
        var c = (uint)n;
        for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
        return c;
    }).ToArray();

    private static uint crc32(ReadOnlySpan<byte> data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data) crc = CrcTable[(crc ^ b) & 0xFF] ^ (crc >> 8);
        return ~crc;
    }
}
