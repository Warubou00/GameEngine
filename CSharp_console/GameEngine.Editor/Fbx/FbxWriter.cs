using System.IO.Compression;
using System.Text;
using GameEngine.Fbx;

namespace GameEngine.Editor;

// ==========================================================================
// FBX のバイナリを書く (AN1。自作のサンプルとテスト用)
//   読む側 (Runtime の FbxDocument) と同じ形 : 頭 → ノードの記録 (版 7500 から 64bit) → 空の記録 → 後ろの飾り
//   配列は大きい物を zlib で縮められる (compress)。縮めるのは .NET の ZLibStream (書く道具の中だけ。
//   読む側は自分で書いた inflate で戻す = 本物の DCC ツールが縮めた物と同じ形のデータで確かめられる)
//   プロパティの型は .NET の型で決まる : short Y / bool C / int I / float F / double D / long L / string S / byte[] R /
//   float[] f / double[] d / int[] i / long[] l / bool[] b
// ==========================================================================
public static class FbxWriter
{
    // 後ろの飾り (Blender の書き出しと同じ値。読む側は使わない)
    private static readonly byte[] FooterId = { 0xfa, 0xbc, 0xab, 0x09, 0xd0, 0xc8, 0xd4, 0x66, 0xb1, 0x76, 0xfb, 0x83, 0x1c, 0xf7, 0x26, 0x7e };
    private static readonly byte[] FooterMagic = { 0xf8, 0x5a, 0x8c, 0x6a, 0xde, 0xf5, 0xd9, 0x7e, 0xec, 0xe9, 0x0c, 0xe3, 0x75, 0x8f, 0x29, 0x0b };
    private const int CompressAbove = 128;   // この byte 数より大きい配列だけ縮める (Blender と同じくらい)

    public static byte[] Write(IEnumerable<FbxNode> topNodes, int version = 7400, bool compress = true)
    {
        using var stream = new MemoryStream();
        var w = new BinaryWriter(stream);
        w.Write(Encoding.ASCII.GetBytes(FbxDocument.Magic));
        w.Write((byte)0x1A);
        w.Write((byte)0x00);
        w.Write(version);
        var wide = version >= 7500;
        foreach (var node in topNodes) writeNode(w, node, wide, compress);
        writeNull(w, wide);
        // 後ろの飾り
        w.Write(FooterId);
        w.Write(0);
        var pad = (int)(((stream.Position + 15) & ~15L) - stream.Position);
        if (pad == 0) pad = 16;
        w.Write(new byte[pad]);
        w.Write(version);
        w.Write(new byte[120]);
        w.Write(FooterMagic);
        w.Flush();
        return stream.ToArray();
    }

    private static void writeNull(BinaryWriter w, bool wide) => w.Write(new byte[wide ? 25 : 13]);

    private static void writeHeader(BinaryWriter w, bool wide, ulong value)
    {
        if (wide) w.Write(value);
        else w.Write(checked((uint)value));
    }

    private static void writeNode(BinaryWriter w, FbxNode node, bool wide, bool compress)
    {
        var stream = w.BaseStream;
        var start = stream.Position;
        writeHeader(w, wide, 0);   // 終わりの位置 (後で書く)
        writeHeader(w, wide, (ulong)node.Properties.Count);
        writeHeader(w, wide, 0);   // プロパティの大きさ (後で書く)
        var name = Encoding.ASCII.GetBytes(node.Name);
        w.Write((byte)name.Length);
        w.Write(name);
        var propertiesStart = stream.Position;
        foreach (var p in node.Properties) writeProperty(w, p, compress);
        var propertiesEnd = stream.Position;
        if (node.Children.Count > 0 || node.Properties.Count == 0)
        {
            foreach (var child in node.Children) writeNode(w, child, wide, compress);
            writeNull(w, wide);
        }
        var end = stream.Position;
        stream.Position = start;
        writeHeader(w, wide, (ulong)end);
        writeHeader(w, wide, (ulong)node.Properties.Count);
        writeHeader(w, wide, (ulong)(propertiesEnd - propertiesStart));
        stream.Position = end;
    }

    private static void writeProperty(BinaryWriter w, object value, bool compress)
    {
        switch (value)
        {
        case short v: w.Write((byte)'Y'); w.Write(v); break;
        case bool v: w.Write((byte)'C'); w.Write((byte)(v ? 1 : 0)); break;
        case int v: w.Write((byte)'I'); w.Write(v); break;
        case float v: w.Write((byte)'F'); w.Write(v); break;
        case double v: w.Write((byte)'D'); w.Write(v); break;
        case long v: w.Write((byte)'L'); w.Write(v); break;
        case string v: { var b = Encoding.UTF8.GetBytes(v); w.Write((byte)'S'); w.Write(b.Length); w.Write(b); break; }
        case byte[] v: w.Write((byte)'R'); w.Write(v.Length); w.Write(v); break;
        case float[] v: writeArray(w, 'f', v.Length, System.Runtime.InteropServices.MemoryMarshal.AsBytes(v.AsSpan()), compress); break;
        case double[] v: writeArray(w, 'd', v.Length, System.Runtime.InteropServices.MemoryMarshal.AsBytes(v.AsSpan()), compress); break;
        case int[] v: writeArray(w, 'i', v.Length, System.Runtime.InteropServices.MemoryMarshal.AsBytes(v.AsSpan()), compress); break;
        case long[] v: writeArray(w, 'l', v.Length, System.Runtime.InteropServices.MemoryMarshal.AsBytes(v.AsSpan()), compress); break;
        case bool[] v: writeArray(w, 'b', v.Length, v.Select(x => x ? (byte)1 : (byte)0).ToArray(), compress); break;
        default: throw new ArgumentException($"FBX property type {value?.GetType().Name ?? "null"} is not supported");
        }
    }

    private static void writeArray(BinaryWriter w, char type, int length, ReadOnlySpan<byte> bytes, bool compress)
    {
        w.Write((byte)type);
        w.Write(length);
        if (compress && bytes.Length > CompressAbove)
        {
            var packed = Zlib(bytes);
            w.Write(1);
            w.Write(packed.Length);
            w.Write(packed);
        }
        else
        {
            w.Write(0);
            w.Write(bytes.Length);
            w.Write(bytes);
        }
    }

    /// <summary>zlib で縮める (.NET の ZLibStream。書く側とテストの比べ相手だけで使う)</summary>
    public static byte[] Zlib(ReadOnlySpan<byte> data, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var output = new MemoryStream();
        using (var z = new ZLibStream(output, level, leaveOpen: true)) z.Write(data);
        return output.ToArray();
    }

    // ---------------------------------------------------------------------
    // ノードを作る道具
    // ---------------------------------------------------------------------

    public static FbxNode Node(string name, params object[] properties)
    {
        var node = new FbxNode(name);
        node.Properties.AddRange(properties);
        return node;
    }

    public static FbxNode With(this FbxNode node, params FbxNode?[] children)
    {
        foreach (var c in children) if (c != null) node.Children.Add(c);
        return node;
    }

    /// <summary>Properties70 の 1 行 : P "名前" "型" "型2" "印" 値...</summary>
    public static FbxNode P(string name, string type, string type2, string flags, params object[] values) =>
        Node("P", new object[] { name, type, type2, flags }.Concat(values).ToArray());

    public static FbxNode P3(string name, string type, System.Numerics.Vector3 v, string flags = "A") =>
        P(name, type, "", flags, (double)v.X, (double)v.Y, (double)v.Z);

    /// <summary>Objects の中の物 : 種類 Id "名前\0\x01種類" "細かい種類"</summary>
    public static FbxNode Object(string kind, long id, string name, string subKind, params FbxNode?[] children) =>
        Node(kind, id, $"{name}\0\x01{kind}", subKind).With(children);

    public static FbxNode ConnectOO(long child, long parent) => Node("C", "OO", child, parent);
    public static FbxNode ConnectOP(long child, long parent, string property) => Node("C", "OP", child, parent, property);
}
