using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GameEngine;

// ==========================================================================
// UserData の bin (.userdata.bin。v2.0 UD3)
//   Build Game が .userdata.json をこの形にして書き出す (書き出したゲームの中で、外から開いて書き換えられないように)。
//   ゲームのコードとシーンは今まで通り "Assets/.../名前.userdata.json" で読む (AssetManager が隣の .bin を読む)
//
//   形 :
//     [見出し 68 byte] "TSMGUSD\0" / 版 (2 byte) / 印 (2 byte。1 = Brotli) / 塩 (16 byte。書き出すたびに違う) /
//                      元の大きさ (4) / 中身の大きさ (4) / HMAC-SHA256 (32。見出しの前の 36 byte + 中身)
//     [中身] 値の木を bin にした物 → Brotli で詰め (小さくならなければそのまま) → 鍵の流れで混ぜた物 (Pak と同じ混ぜ方)
//   値の木 (JSON の形はそのまま。型を知らなくても読み書きできるので、フィールドの名前を変えても前の値を失わない) :
//     文字の表 (数 + 長さ + UTF-8。フィールドの名前 / 文字列は 1 回だけ。リストの中の struct の名前が何度出ても 1 回) / 値 :
//       0 null / 1 false / 2 true / 3 整数 (ZigZag の可変長) / 4 float32 / 5 float64 / 6 文字列 (表の番号) /
//       7 配列 (数 + 値) / 8 オブジェクト (数 + (名前の番号 + 値)) / 9 数の文字 (long に入らない整数など。表の番号)
//   読む時は HMAC を確かめ (1 byte でも変えると読まない)、元の JSON に戻して今までの読み方 (PopulateUserData) に渡す
//   ※鍵は Player の中にある (Pak と同じ)。本気で解析すれば読めるが、ファイルを開いて値を書き換えることはできない
// ==========================================================================
public static class UserDataBinary
{
    public const string Extension = ".userdata.bin";
    private static readonly byte[] Magic = "TSMGUSD\0"u8.ToArray();
    private const ushort FormatVersion = 1;
    private const ushort FlagBrotli = 1;
    private const int HeaderSize = 68;   // 8 + 2 + 2 + 16 + 4 + 4 + 32
    private const int MacOffset = 36;

    private const byte TagNull = 0, TagFalse = 1, TagTrue = 2, TagInt = 3, TagFloat = 4, TagDouble = 5, TagString = 6, TagArray = 7, TagObject = 8, TagNumberText = 9;

    /// <summary>bin のファイルか (先頭の印を見る)</summary>
    public static bool IsBinary(ReadOnlySpan<byte> data) => data.Length >= HeaderSize && data[..Magic.Length].SequenceEqual(Magic);

    /// <summary>.userdata.json のパス → 書き出したゲームの .userdata.bin のパス</summary>
    public static string BinaryPathOf(string jsonPath) =>
        jsonPath.EndsWith(UserDataFile.Extension, StringComparison.OrdinalIgnoreCase) ? jsonPath[..^UserDataFile.Extension.Length] + Extension : jsonPath + ".bin";

    /// <summary>.userdata.bin のパス → 元の .userdata.json のパス (ゲームのコード / シーンが使う名前)</summary>
    public static string JsonPathOf(string binaryPath) =>
        binaryPath.EndsWith(Extension, StringComparison.OrdinalIgnoreCase) ? binaryPath[..^Extension.Length] + UserDataFile.Extension : binaryPath;

    // ---------------------------------------------------------------------
    // 書く (JSON → bin)
    // ---------------------------------------------------------------------

    /// <summary>UserData の JSON を bin にする。quality : Brotli の詰め方 (0〜11)。壊れた JSON は例外</summary>
    public static byte[] Encode(string json, int quality = 11)
    {
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
        var strings = new List<string>();
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        var tree = new MemoryStream();
        writeValue(tree, document.RootElement, strings, indices);

        var raw = new MemoryStream();
        writeVarint(raw, (ulong)strings.Count);
        foreach (var s in strings)
        {
            var bytes = Encoding.UTF8.GetBytes(s);
            writeVarint(raw, (ulong)bytes.Length);
            raw.Write(bytes);
        }
        tree.Position = 0;
        tree.CopyTo(raw);
        var rawBytes = raw.ToArray();

        var flags = (ushort)0;
        var payload = rawBytes;
        var packed = new byte[BrotliEncoder.GetMaxCompressedLength(rawBytes.Length)];
        if (BrotliEncoder.TryCompress(rawBytes, packed, out var packedLength, quality, 22) && packedLength < rawBytes.Length)
        {
            payload = packed[..packedLength];
            flags = FlagBrotli;
        }

        var salt = RandomNumberGenerator.GetBytes(16);
        var result = new byte[HeaderSize + payload.Length];
        Magic.CopyTo(result, 0);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(8), FormatVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(result.AsSpan(10), flags);
        salt.CopyTo(result, 12);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(28), rawBytes.Length);
        BinaryPrimitives.WriteInt32LittleEndian(result.AsSpan(32), payload.Length);
        payload.CopyTo(result, HeaderSize);
        Pak.scramble(result.AsSpan(HeaderSize), scrambleKeyOf(salt), 0);
        macOf(result, salt).CopyTo(result, MacOffset);
        return result;
    }

    private static void writeValue(Stream s, JsonElement e, List<string> strings, Dictionary<string, int> indices)
    {
        switch (e.ValueKind)
        {
        case JsonValueKind.Null:
        case JsonValueKind.Undefined:
            s.WriteByte(TagNull);
            break;
        case JsonValueKind.False:
            s.WriteByte(TagFalse);
            break;
        case JsonValueKind.True:
            s.WriteByte(TagTrue);
            break;
        case JsonValueKind.String:
            s.WriteByte(TagString);
            writeVarint(s, (ulong)indexOf(e.GetString()!, strings, indices));
            break;
        case JsonValueKind.Number:
            writeNumber(s, e, strings, indices);
            break;
        case JsonValueKind.Array:
            s.WriteByte(TagArray);
            writeVarint(s, (ulong)e.GetArrayLength());
            foreach (var item in e.EnumerateArray()) writeValue(s, item, strings, indices);
            break;
        case JsonValueKind.Object:
            s.WriteByte(TagObject);
            var properties = e.EnumerateObject().ToList();
            writeVarint(s, (ulong)properties.Count);
            foreach (var property in properties)
            {
                writeVarint(s, (ulong)indexOf(property.Name, strings, indices));
                writeValue(s, property.Value, strings, indices);
            }
            break;
        }
    }

    // 数 : 整数は可変長、小数は float32 で元に戻るなら float32 (UserData の float はこれ)、だめなら float64。どちらでもなければ文字のまま
    private static void writeNumber(Stream s, JsonElement e, List<string> strings, Dictionary<string, int> indices)
    {
        var text = e.GetRawText();
        var isInteger = text.IndexOfAny(['.', 'e', 'E']) < 0;
        if (isInteger && e.TryGetInt64(out var integer))
        {
            s.WriteByte(TagInt);
            writeVarint(s, (ulong)((integer << 1) ^ (integer >> 63)));
            return;
        }
        if (!isInteger && e.TryGetDouble(out var d))
        {
            var f = (float)d;
            // float32 の一番短い書き方が、元の数 (double として) と同じ時だけ float32 (float のフィールドの値は必ずこれ。double のフィールドも値が変わらない)
            if (float.IsFinite(f) && double.Parse(f.ToString("R", System.Globalization.CultureInfo.InvariantCulture), System.Globalization.CultureInfo.InvariantCulture) == d)
            {
                s.WriteByte(TagFloat);
                Span<byte> buffer = stackalloc byte[4];
                BinaryPrimitives.WriteSingleLittleEndian(buffer, f);
                s.Write(buffer);
                return;
            }
            if (d.ToString("R", System.Globalization.CultureInfo.InvariantCulture) is var back && double.Parse(back, System.Globalization.CultureInfo.InvariantCulture) == d)
            {
                s.WriteByte(TagDouble);
                Span<byte> buffer = stackalloc byte[8];
                BinaryPrimitives.WriteDoubleLittleEndian(buffer, d);
                s.Write(buffer);
                return;
            }
        }
        s.WriteByte(TagNumberText);   // long に入らない整数など : 書いてあった通りに残す
        writeVarint(s, (ulong)indexOf(text, strings, indices));
    }

    private static int indexOf(string value, List<string> strings, Dictionary<string, int> indices)
    {
        if (indices.TryGetValue(value, out var index)) return index;
        index = strings.Count;
        strings.Add(value);
        indices.Add(value, index);
        return index;
    }

    private static void writeVarint(Stream s, ulong value)
    {
        while (value >= 0x80)
        {
            s.WriteByte((byte)(value | 0x80));
            value >>= 7;
        }
        s.WriteByte((byte)value);
    }

    // ---------------------------------------------------------------------
    // 読む (bin → JSON)
    // ---------------------------------------------------------------------

    /// <summary>bin を JSON (UserData のファイルと同じ形) に戻す。形が違う / 書き換えられている / 壊れている時は InvalidDataException</summary>
    public static string Decode(byte[] data)
    {
        if (!IsBinary(data)) throw new InvalidDataException("Not a UserData binary file.");
        var version = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(8));
        if (version != FormatVersion) throw new InvalidDataException($"Unsupported UserData binary version {version}.");
        var flags = BinaryPrimitives.ReadUInt16LittleEndian(data.AsSpan(10));
        var salt = data.AsSpan(12, 16).ToArray();
        var rawLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(28));
        var payloadLength = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(32));
        if (payloadLength < 0 || rawLength < 0 || HeaderSize + payloadLength != data.Length || rawLength > 256 * 1024 * 1024)
            throw new InvalidDataException("The UserData binary file is broken (size).");
        if (!CryptographicOperations.FixedTimeEquals(macOf(data, salt), data.AsSpan(MacOffset, 32)))
            throw new InvalidDataException("The UserData binary file was modified or is broken (check failed).");

        var payload = data.AsSpan(HeaderSize, payloadLength).ToArray();
        Pak.scramble(payload, scrambleKeyOf(salt), 0);
        byte[] raw;
        if ((flags & FlagBrotli) != 0)
        {
            raw = new byte[rawLength];
            if (!BrotliDecoder.TryDecompress(payload, raw, out var written) || written != rawLength) throw new InvalidDataException("The UserData binary file is broken (decompress).");
        }
        else raw = payload;

        var reader = new Reader(raw);
        var count = checked((int)reader.Varint());
        var strings = new string[count];
        for (int i = 0; i < count; i++)
        {
            var length = checked((int)reader.Varint());
            strings[i] = Encoding.UTF8.GetString(reader.Bytes(length));
        }
        using var stream = new MemoryStream(raw.Length * 2);
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping, SkipValidation = false }))
        {
            readValue(ref reader, w, strings, 0);
            w.Flush();
        }
        if (reader.Position != raw.Length) throw new InvalidDataException("The UserData binary file is broken (trailing data).");
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static void readValue(ref Reader r, Utf8JsonWriter w, string[] strings, int depth)
    {
        if (depth > 256) throw new InvalidDataException("The UserData binary file is broken (too deep).");
        var tag = r.Byte();
        switch (tag)
        {
        case TagNull:   w.WriteNullValue(); break;
        case TagFalse:  w.WriteBooleanValue(false); break;
        case TagTrue:   w.WriteBooleanValue(true); break;
        case TagInt:
        {
            var zigzag = r.Varint();
            w.WriteNumberValue((long)(zigzag >> 1) ^ -(long)(zigzag & 1));
            break;
        }
        case TagFloat:  w.WriteNumberValue(BinaryPrimitives.ReadSingleLittleEndian(r.Bytes(4))); break;
        case TagDouble: w.WriteNumberValue(BinaryPrimitives.ReadDoubleLittleEndian(r.Bytes(8))); break;
        case TagString: w.WriteStringValue(stringAt(strings, r.Varint())); break;
        case TagNumberText: w.WriteRawValue(stringAt(strings, r.Varint())); break;
        case TagArray:
        {
            var count = r.Varint();
            w.WriteStartArray();
            for (ulong i = 0; i < count; i++) readValue(ref r, w, strings, depth + 1);
            w.WriteEndArray();
            break;
        }
        case TagObject:
        {
            var count = r.Varint();
            w.WriteStartObject();
            for (ulong i = 0; i < count; i++)
            {
                w.WritePropertyName(stringAt(strings, r.Varint()));
                readValue(ref r, w, strings, depth + 1);
            }
            w.WriteEndObject();
            break;
        }
        default:
            throw new InvalidDataException($"The UserData binary file is broken (tag {tag}).");
        }
    }

    private static string stringAt(string[] strings, ulong index) =>
        index < (ulong)strings.Length ? strings[index] : throw new InvalidDataException("The UserData binary file is broken (string index).");

    private ref struct Reader
    {
        private readonly ReadOnlySpan<byte> _data;
        public int Position;
        public Reader(ReadOnlySpan<byte> data) { _data = data; Position = 0; }

        public byte Byte()
        {
            if (Position >= _data.Length) throw new InvalidDataException("The UserData binary file is broken (end of data).");
            return _data[Position++];
        }

        public ReadOnlySpan<byte> Bytes(int length)
        {
            if (length < 0 || Position + length > _data.Length) throw new InvalidDataException("The UserData binary file is broken (end of data).");
            var span = _data.Slice(Position, length);
            Position += length;
            return span;
        }

        public ulong Varint()
        {
            ulong value = 0;
            for (int shift = 0; shift < 64; shift += 7)
            {
                var b = Byte();
                value |= (ulong)(b & 0x7F) << shift;
                if ((b & 0x80) == 0) return value;
            }
            throw new InvalidDataException("The UserData binary file is broken (varint).");
        }
    }

    // ---------------------------------------------------------------------
    // 鍵 (塩ごとに違う)
    // ---------------------------------------------------------------------

    private static ulong scrambleKeyOf(byte[] salt)
    {
        var hash = SHA256.HashData([.. salt, .. "Tsumugi Engine userdata scramble v1"u8]);
        return BinaryPrimitives.ReadUInt64LittleEndian(hash);
    }

    // 見出しの前の 36 byte (印 / 版 / 塩 / 大きさ) と中身の HMAC
    private static byte[] macOf(byte[] file, byte[] salt)
    {
        var key = SHA256.HashData([.. salt, .. "Tsumugi Engine userdata check v1"u8]);
        using var hmac = new HMACSHA256(key);
        hmac.TransformBlock(file, 0, MacOffset, null, 0);
        hmac.TransformFinalBlock(file, HeaderSize, file.Length - HeaderSize);
        return hmac.Hash!;
    }
}
