using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Text;

namespace GameEngine.Fbx;

// ==========================================================================
// FBX のバイナリを読む (AN1。外部のライブラリを使わない)
//   形 : 頭 27 byte ("Kaydara FBX Binary  \0" + 0x1A 0x00 + 版 uint32) → ノードの記録の木 → 空の記録 → 後ろの飾り
//   ノードの記録 : 終わりの位置 / プロパティの数 / プロパティの大きさ (版 7500 から uint64、それより前は uint32) /
//                  名前の長さ (uint8) / 名前 / プロパティ / 子のノード (空の記録で終わる)
//   プロパティ : Y int16 / C bool / I int32 / F float / D double / L int64 / S 文字 / R バイト列 /
//                f d l i b = 配列 (数 / encoding (0 そのまま / 1 zlib) / 中身の大きさ)
//   ASCII の FBX と、版 7100 より前は読まない (エラー)
//   壊れたファイル (途中で切れた / 大きさが合わない) は FbxFormatException (どこで分かったかの位置も出す)
// ==========================================================================
public sealed class FbxNode
{
    public string Name { get; }
    public List<object> Properties { get; } = new();
    public List<FbxNode> Children { get; } = new();
    /// <summary>ファイルの中の位置 (エラーや確認用)</summary>
    public long Offset { get; }

    public FbxNode(string name, long offset = 0)
    {
        Name = name;
        Offset = offset;
    }

    public FbxNode? Child(string name)
    {
        foreach (var c in Children) if (c.Name == name) return c;
        return null;
    }

    public IEnumerable<FbxNode> ChildrenNamed(string name) => Children.Where(c => c.Name == name);

    public object? this[int index] => index < Properties.Count ? Properties[index] : null;

    /// <summary>プロパティを数として読む (int / long / float / double / short / bool のどれでも)</summary>
    public double Number(int index) => this[index] switch
    {
        int i => i, long l => l, float f => f, double d => d, short s => s, bool b => b ? 1 : 0,
        _ => throw new FbxFormatException($"{Name} : property {index} is not a number"),
    };

    public long Long(int index) => this[index] switch
    {
        int i => i, long l => l, short s => s,
        _ => throw new FbxFormatException($"{Name} : property {index} is not an integer"),
    };

    public string String(int index) => this[index] as string ?? throw new FbxFormatException($"{Name} : property {index} is not a string");

    /// <summary>子のノードの最初のプロパティの配列 (Vertices など)。無ければ null</summary>
    public T[]? ChildArray<T>(string name) => Child(name)?[0] as T[];

    public override string ToString() => $"{Name} ({Properties.Count} props, {Children.Count} children)";
}

public sealed class FbxDocument
{
    public const string Magic = "Kaydara FBX Binary  \0";
    public const int MinVersion = 7100;
    public const int MaxVersion = 7700;

    public int Version { get; private set; }
    /// <summary>ノードの記録の長さが 64bit か (版 7500 から)</summary>
    public bool Is64Bit => Version >= 7500;
    /// <summary>一番上のノード (FBXHeaderExtension / GlobalSettings / Objects / Connections ...)。名前の無い入れ物</summary>
    public FbxNode Root { get; } = new("");
    /// <summary>縮めた配列の数と、戻した大きさ (確認 / 時間)</summary>
    public int CompressedArrays { get; private set; }
    public long InflatedBytes { get; private set; }

    public FbxNode? this[string name] => Root.Child(name);

    public static FbxDocument Load(string file) => Read(File.ReadAllBytes(file));

    public static FbxDocument Read(byte[] data)
    {
        var document = new FbxDocument();
        if (data.Length < 27 || Encoding.ASCII.GetString(data, 0, 21) != Magic)
        {
            var text = Encoding.ASCII.GetString(data, 0, Math.Min(data.Length, 256));
            if (text.Contains("FBXHeaderExtension") || text.TrimStart().StartsWith(';'))
                throw new FbxFormatException("ASCII FBX is not supported : export as binary FBX (7.1 - 7.7)");
            throw new FbxFormatException("not an FBX file (no \"Kaydara FBX Binary\" header)");
        }
        document.Version = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(23));
        if (document.Version < MinVersion || document.Version > MaxVersion)
            throw new FbxFormatException($"FBX version {document.Version / 1000.0:F1} is not supported (7.1 - 7.7)");
        var reader = new Reader(data, document);
        var pos = 27L;
        while (true)
        {
            var node = reader.ReadNode(ref pos, 0);
            if (node == null) break;   // 一番上の空の記録 (この後は後ろの飾り)
            document.Root.Children.Add(node);
            if (pos >= data.Length - reader.NullRecordSize) break;   // 空の記録の無い古い書き出し
        }
        if (document.Root.Children.Count == 0) throw new FbxFormatException("the FBX file has no nodes");
        return document;
    }

    private sealed class Reader
    {
        private readonly byte[] _data;
        private readonly FbxDocument _document;
        private readonly bool _wide;
        public int NullRecordSize => _wide ? 25 : 13;
        private const int MaxDepth = 64;
        private const int MaxArrayLength = 1 << 28;

        public Reader(byte[] data, FbxDocument document)
        {
            _data = data;
            _document = document;
            _wide = document.Is64Bit;
        }

        private void need(long pos, long count, string what)
        {
            if (pos < 0 || count < 0 || pos + count > _data.Length) throw new FbxFormatException($"unexpected end of file at {pos} ({what})");
        }

        private ulong readHeader(ref long pos)
        {
            if (_wide)
            {
                need(pos, 8, "record");
                var v = BinaryPrimitives.ReadUInt64LittleEndian(_data.AsSpan((int)pos));
                pos += 8;
                return v;
            }
            need(pos, 4, "record");
            var u = BinaryPrimitives.ReadUInt32LittleEndian(_data.AsSpan((int)pos));
            pos += 4;
            return u;
        }

        /// <summary>ノードを 1 つ読む。空の記録なら null</summary>
        public FbxNode? ReadNode(ref long pos, int depth)
        {
            if (depth > MaxDepth) throw new FbxFormatException($"nodes are nested too deeply at {pos}");
            var start = pos;
            var endOffset = readHeader(ref pos);
            var propertyCount = readHeader(ref pos);
            var propertyBytes = readHeader(ref pos);
            need(pos, 1, "name length");
            var nameLength = _data[pos++];
            if (endOffset == 0 && propertyCount == 0 && propertyBytes == 0 && nameLength == 0) return null;
            if (endOffset > (ulong)_data.Length || (long)endOffset <= start) throw new FbxFormatException($"bad node end offset {endOffset} at {start}");
            need(pos, nameLength, "name");
            var node = new FbxNode(Encoding.ASCII.GetString(_data, (int)pos, nameLength), start);
            pos += nameLength;
            var propertiesStart = pos;
            if (propertyCount > (ulong)_data.Length) throw new FbxFormatException($"{node.Name} : bad property count at {start}");
            for (ulong i = 0; i < propertyCount; i++) node.Properties.Add(readProperty(ref pos, node.Name));
            if ((ulong)(pos - propertiesStart) != propertyBytes)
                throw new FbxFormatException($"{node.Name} : property size {pos - propertiesStart} does not match the record ({propertyBytes}) at {start}");
            // 子 : 終わりの位置まで (最後に空の記録がある)
            while (pos < (long)endOffset)
            {
                var child = ReadNode(ref pos, depth + 1);
                if (child == null) break;
                node.Children.Add(child);
            }
            if (pos != (long)endOffset) throw new FbxFormatException($"{node.Name} : children end at {pos}, the record says {endOffset}");
            return node;
        }

        private object readProperty(ref long pos, string owner)
        {
            need(pos, 1, "property type");
            var type = (char)_data[pos++];
            var span = _data.AsSpan();
            switch (type)
            {
            case 'Y': need(pos, 2, "int16"); pos += 2; return BinaryPrimitives.ReadInt16LittleEndian(span[(int)(pos - 2)..]);
            case 'C': need(pos, 1, "bool"); return _data[pos++] != 0;
            case 'I': need(pos, 4, "int32"); pos += 4; return BinaryPrimitives.ReadInt32LittleEndian(span[(int)(pos - 4)..]);
            case 'F': need(pos, 4, "float"); pos += 4; return BinaryPrimitives.ReadSingleLittleEndian(span[(int)(pos - 4)..]);
            case 'D': need(pos, 8, "double"); pos += 8; return BinaryPrimitives.ReadDoubleLittleEndian(span[(int)(pos - 8)..]);
            case 'L': need(pos, 8, "int64"); pos += 8; return BinaryPrimitives.ReadInt64LittleEndian(span[(int)(pos - 8)..]);
            case 'S':
            case 'R':
            {
                need(pos, 4, "length");
                var length = BinaryPrimitives.ReadUInt32LittleEndian(span[(int)pos..]);
                pos += 4;
                need(pos, length, type == 'S' ? "string" : "raw");
                var start = (int)pos;
                pos += length;
                return type == 'S' ? Encoding.UTF8.GetString(_data, start, (int)length) : _data.AsSpan(start, (int)length).ToArray();
            }
            case 'f': return readArray<float>(ref pos, 4, owner);
            case 'd': return readArray<double>(ref pos, 8, owner);
            case 'i': return readArray<int>(ref pos, 4, owner);
            case 'l': return readArray<long>(ref pos, 8, owner);
            case 'b': return readArray<bool>(ref pos, 1, owner);
            default: throw new FbxFormatException($"{owner} : unknown property type '{type}' (0x{(int)type:X2}) at {pos - 1}");
            }
        }

        private T[] readArray<T>(ref long pos, int elementSize, string owner) where T : struct
        {
            need(pos, 12, "array header");
            var span = _data.AsSpan((int)pos);
            var length = BinaryPrimitives.ReadUInt32LittleEndian(span);
            var encoding = BinaryPrimitives.ReadUInt32LittleEndian(span[4..]);
            var stored = BinaryPrimitives.ReadUInt32LittleEndian(span[8..]);
            pos += 12;
            if (length > MaxArrayLength) throw new FbxFormatException($"{owner} : array too long ({length})");
            need(pos, stored, "array data");
            var bytes = (int)length * elementSize;
            var source = _data.AsSpan((int)pos, (int)stored);
            pos += stored;
            T[] result;
            switch (encoding)
            {
            case 0:
                if (stored != bytes) throw new FbxFormatException($"{owner} : array size {stored} does not match {length} x {elementSize}");
                result = new T[length];
                source.CopyTo(MemoryMarshal.AsBytes(result.AsSpan()));
                break;
            case 1:
                var raw = Inflate.Zlib(source, bytes);
                result = new T[length];
                raw.AsSpan().CopyTo(MemoryMarshal.AsBytes(result.AsSpan()));
                _document.CompressedArrays++;
                _document.InflatedBytes += bytes;
                break;
            default:
                throw new FbxFormatException($"{owner} : unknown array encoding {encoding}");
            }
            if (typeof(T) == typeof(bool))   // bool は 0 / 1 以外 (例 : 'Y' / 'T') もあるので 0 か どうかにそろえる
            {
                var b = MemoryMarshal.AsBytes(result.AsSpan());
                for (int i = 0; i < b.Length; i++) b[i] = b[i] != 0 ? (byte)1 : (byte)0;
            }
            return result;
        }
    }
}
