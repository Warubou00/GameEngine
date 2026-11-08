using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace GameEngine.Editor;

// ==========================================================================
// 書き出した exe のアイコンを入れ替える (v1.3。Project Settings の Icon。Unity の Player Settings > Icon と同じ)
//   画像 (png / jpg / bmp / tga 以外) は 256 / 128 / 64 / 48 / 32 / 24 / 16 の大きさを作る (正方形でなければ透明の余白を足す)
//   .ico はそのまま使う。exe の今のアイコン (Engine.props の Tsumugi Engine のアイコン) を消して、同じ名前で入れる
//   Windows の UpdateResource で書く (exe の他の所は変えない)。戻り値 = できなければ理由
//   ※エクスプローラーはアイコンを覚えているので、同じ場所に書き出すと前のアイコンのまま見えることがある
// ==========================================================================
internal static class ExeIcon
{
    public static readonly int[] Sizes = { 256, 128, 64, 48, 32, 24, 16 };

    public static string? Apply(string exePath, string imageFile)
    {
        try
        {
            var images = imageFile.EndsWith(".ico", StringComparison.OrdinalIgnoreCase) ? readIco(File.ReadAllBytes(imageFile)) : fromImage(imageFile);
            if (images.Count == 0) return $"No images in {Path.GetFileName(imageFile)}";
            return write(exePath, images);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidDataException or ExternalException)
        {
            return ex.Message;
        }
    }

    // アイコンの 1 枚 (幅 / 高さ / 色の数 / ビット / 中身 (PNG か BMP の DIB))
    private sealed record IconImage(int Width, int Height, byte ColorCount, ushort Planes, ushort BitCount, byte[] Data);

    private static List<IconImage> readIco(byte[] ico)
    {
        var images = new List<IconImage>();
        if (ico.Length < 6 || BitConverter.ToUInt16(ico, 2) != 1) throw new InvalidDataException("Not an .ico file");
        var count = BitConverter.ToUInt16(ico, 4);
        for (int i = 0; i < count; i++)
        {
            var e = 6 + i * 16;
            var size = BitConverter.ToInt32(ico, e + 8);
            var offset = BitConverter.ToInt32(ico, e + 12);
            images.Add(new IconImage(ico[e] == 0 ? 256 : ico[e], ico[e + 1] == 0 ? 256 : ico[e + 1], ico[e + 2],
                                     BitConverter.ToUInt16(ico, e + 4), BitConverter.ToUInt16(ico, e + 6), ico.AsSpan(offset, size).ToArray()));
        }
        return images;
    }

    private static List<IconImage> fromImage(string file)
    {
        using var source = new Bitmap(file);
        var images = new List<IconImage>();
        foreach (var size in Sizes)
        {
            using var bitmap = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bitmap))
            {
                g.Clear(Color.Transparent);
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.CompositingQuality = CompositingQuality.HighQuality;
                var scale = Math.Min((float)size / source.Width, (float)size / source.Height);   // 正方形に収める (余白は透明)
                var w = source.Width * scale;
                var h = source.Height * scale;
                g.DrawImage(source, (size - w) / 2, (size - h) / 2, w, h);
            }
            images.Add(new IconImage(size, size, 0, 1, 32, size >= 256 ? png(bitmap) : dib(bitmap)));
        }
        return images;
    }

    private static byte[] png(Bitmap bitmap)
    {
        using var stream = new MemoryStream();
        bitmap.Save(stream, ImageFormat.Png);
        return stream.ToArray();
    }

    // アイコンの中の BMP : 見出し (高さは 2 倍) / 下の行から BGRA / 1 bit のマスク (透明 = 1)
    private static byte[] dib(Bitmap bitmap)
    {
        int size = bitmap.Width;
        using var stream = new MemoryStream();
        using var w = new BinaryWriter(stream);
        w.Write(40); w.Write(size); w.Write(size * 2); w.Write((ushort)1); w.Write((ushort)32);
        w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0); w.Write(0);
        var data = bitmap.LockBits(new Rectangle(0, 0, size, size), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        var pixels = new byte[size * size * 4];
        for (int y = 0; y < size; y++) Marshal.Copy(data.Scan0 + y * data.Stride, pixels, y * size * 4, size * 4);
        bitmap.UnlockBits(data);
        for (int y = size - 1; y >= 0; y--) w.Write(pixels, y * size * 4, size * 4);   // 下の行から (BGRA のまま)
        var stride = (size + 31) / 32 * 4;
        for (int y = size - 1; y >= 0; y--)
        {
            var row = new byte[stride];
            for (int x = 0; x < size; x++) if (pixels[(y * size + x) * 4 + 3] == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
            w.Write(row);
        }
        return stream.ToArray();
    }

    // ---------------------------------------------------------------------
    // exe へ書く
    // ---------------------------------------------------------------------
    private const int RT_ICON = 3, RT_GROUP_ICON = 14;

    private static string? write(string exePath, List<IconImage> images)
    {
        // 今のアイコンのまとまり (名前 / 言語 / 中の絵の番号) を読む
        IntPtr groupName = (IntPtr)1;
        string? groupString = null;
        ushort groupLanguage = 0;
        var oldIcons = new List<(ushort Id, ushort Language)>();
        var module = LoadLibraryEx(exePath, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE);
        if (module != IntPtr.Zero)
        {
            try
            {
                IntPtr found = IntPtr.Zero;
                EnumResourceNames(module, (IntPtr)RT_GROUP_ICON, (m, t, name, l) =>
                {
                    if (((long)name >> 16) == 0) found = name;   // 番号
                    else { groupString = Marshal.PtrToStringUni(name); found = (IntPtr)(-1); }
                    return false;   // 最初の 1 つ (exe のアイコン)
                }, IntPtr.Zero);
                if (found != IntPtr.Zero)
                {
                    groupName = found;
                    var nameArg = groupString != null ? Marshal.StringToHGlobalUni(groupString) : found;
                    try
                    {
                        EnumResourceLanguages(module, (IntPtr)RT_GROUP_ICON, nameArg, (m, t, n, language, l) => { groupLanguage = language; return false; }, IntPtr.Zero);
                        var resource = FindResourceEx(module, (IntPtr)RT_GROUP_ICON, nameArg, groupLanguage);
                        if (resource != IntPtr.Zero)
                        {
                            var size = SizeofResource(module, resource);
                            var pointer = LockResource(LoadResource(module, resource));
                            var bytes = new byte[size];
                            Marshal.Copy(pointer, bytes, 0, (int)size);
                            for (int i = 0; i < BitConverter.ToUInt16(bytes, 4); i++)
                            {
                                var id = BitConverter.ToUInt16(bytes, 6 + i * 14 + 12);
                                ushort iconLanguage = groupLanguage;
                                EnumResourceLanguages(module, (IntPtr)RT_ICON, (IntPtr)id, (m, t, n, language, l) => { iconLanguage = language; return false; }, IntPtr.Zero);
                                oldIcons.Add((id, iconLanguage));
                            }
                        }
                    }
                    finally { if (groupString != null) Marshal.FreeHGlobal(nameArg); }
                }
            }
            finally { FreeLibrary(module); }
        }

        var update = BeginUpdateResource(exePath, false);
        if (update == IntPtr.Zero) return $"Cannot open {Path.GetFileName(exePath)} for writing (error {Marshal.GetLastWin32Error()})";
        var ok = true;
        var groupArg = groupString != null ? Marshal.StringToHGlobalUni(groupString) : groupName;
        try
        {
            // 前の絵とまとまりを消す
            foreach (var (id, language) in oldIcons) ok &= UpdateResource(update, (IntPtr)RT_ICON, (IntPtr)id, language, IntPtr.Zero, 0);
            if (oldIcons.Count > 0 || groupString != null || groupName != (IntPtr)1) ok &= UpdateResource(update, (IntPtr)RT_GROUP_ICON, groupArg, groupLanguage, IntPtr.Zero, 0);
            // 新しい絵 (番号は 1 から) とまとまり
            var group = new MemoryStream();
            using (var g = new BinaryWriter(group))
            {
                g.Write((ushort)0); g.Write((ushort)1); g.Write((ushort)images.Count);
                for (int i = 0; i < images.Count; i++)
                {
                    var image = images[i];
                    g.Write((byte)(image.Width >= 256 ? 0 : image.Width)); g.Write((byte)(image.Height >= 256 ? 0 : image.Height));
                    g.Write(image.ColorCount); g.Write((byte)0); g.Write(image.Planes); g.Write(image.BitCount);
                    g.Write(image.Data.Length); g.Write((ushort)(i + 1));
                }
            }
            for (int i = 0; i < images.Count; i++) ok &= updateBytes(update, RT_ICON, (IntPtr)(i + 1), groupLanguage, images[i].Data);
            ok &= updateBytes(update, RT_GROUP_ICON, groupArg, groupLanguage, group.ToArray());
        }
        finally
        {
            var error = ok ? 0 : Marshal.GetLastWin32Error();
            if (!EndUpdateResource(update, !ok) && ok) { ok = false; error = Marshal.GetLastWin32Error(); }
            if (groupString != null) Marshal.FreeHGlobal(groupArg);
            if (!ok) throw new IOException($"Cannot write the icon into {Path.GetFileName(exePath)} (error {error})");
        }
        return null;
    }

    private static bool updateBytes(IntPtr update, int type, IntPtr name, ushort language, byte[] data)
    {
        var handle = GCHandle.Alloc(data, GCHandleType.Pinned);
        try { return UpdateResource(update, (IntPtr)type, name, language, handle.AddrOfPinnedObject(), (uint)data.Length); }
        finally { handle.Free(); }
    }

    /// <summary>確認用 : exe のアイコンのまとまりにある絵の大きさ (無ければ空)</summary>
    public static List<int> ReadSizes(string exePath)
    {
        var sizes = new List<int>();
        var module = LoadLibraryEx(exePath, IntPtr.Zero, LOAD_LIBRARY_AS_DATAFILE | LOAD_LIBRARY_AS_IMAGE_RESOURCE);
        if (module == IntPtr.Zero) return sizes;
        try
        {
            EnumResourceNames(module, (IntPtr)RT_GROUP_ICON, (m, t, name, l) =>
            {
                var resource = FindResource(m, name, (IntPtr)RT_GROUP_ICON);
                if (resource == IntPtr.Zero) return false;
                var size = SizeofResource(m, resource);
                var bytes = new byte[size];
                Marshal.Copy(LockResource(LoadResource(m, resource)), bytes, 0, (int)size);
                for (int i = 0; i < BitConverter.ToUInt16(bytes, 4); i++) sizes.Add(bytes[6 + i * 14] == 0 ? 256 : bytes[6 + i * 14]);
                return false;
            }, IntPtr.Zero);
        }
        finally { FreeLibrary(module); }
        return sizes;
    }

    private const uint LOAD_LIBRARY_AS_DATAFILE = 0x2, LOAD_LIBRARY_AS_IMAGE_RESOURCE = 0x20;
    private delegate bool EnumNameProc(IntPtr module, IntPtr type, IntPtr name, IntPtr param);
    private delegate bool EnumLanguageProc(IntPtr module, IntPtr type, IntPtr name, ushort language, IntPtr param);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr LoadLibraryEx(string file, IntPtr reserved, uint flags);
    [DllImport("kernel32.dll")] private static extern bool FreeLibrary(IntPtr module);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumResourceNames(IntPtr module, IntPtr type, EnumNameProc proc, IntPtr param);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool EnumResourceLanguages(IntPtr module, IntPtr type, IntPtr name, EnumLanguageProc proc, IntPtr param);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindResource(IntPtr module, IntPtr name, IntPtr type);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindResourceEx(IntPtr module, IntPtr type, IntPtr name, ushort language);
    [DllImport("kernel32.dll")] private static extern IntPtr LoadResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll")] private static extern IntPtr LockResource(IntPtr data);
    [DllImport("kernel32.dll")] private static extern uint SizeofResource(IntPtr module, IntPtr resource);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern IntPtr BeginUpdateResource(string file, bool deleteExisting);
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)] private static extern bool UpdateResource(IntPtr update, IntPtr type, IntPtr name, ushort language, IntPtr data, uint size);
    [DllImport("kernel32.dll", SetLastError = true)] private static extern bool EndUpdateResource(IntPtr update, bool discard);
}
