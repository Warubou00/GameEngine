using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Numerics;
using Bitmap = System.Drawing.Bitmap;
using DrawingFont = System.Drawing.Font;
using DrawingGraphics = System.Drawing.Graphics;

namespace GameEngine;

// ==========================================================================
// UI のテクスチャと文字 (v1.7)
//   テクスチャは C++ の UI のテクスチャ (Graphics.CreateUITexture。ImGui と同じ番号) を使う
//   C++ には UI のテクスチャを解放する手段が無いので、作った物は覚えておいて使い回す (同じ画像を 2 回作らない)
// ==========================================================================
public sealed class UIResources
{
    public UITextures Textures { get; }
    public UIFonts    Fonts    { get; }

    internal UIResources(Func<int, int, byte[], uint> createTexture)
    {
        Textures = new UITextures(createTexture);
        Fonts    = new UIFonts(createTexture);
    }
}

// ==========================================================================
// 画像 (テクスチャのアセット : png / jpg / bmp。System.Drawing で読む) + 白 1 点 + 丸 (角の丸い四角に使う)
// ==========================================================================
public sealed class UITextures
{
    public const uint Invalid = 0xFFFFFFFFu;
    private readonly Func<int, int, byte[], uint> _create;
    private readonly Dictionary<string, (uint Id, Vector2 Size)> _images = new(StringComparer.OrdinalIgnoreCase);
    private uint _white = Invalid, _circle = Invalid;

    internal UITextures(Func<int, int, byte[], uint> create) => _create = create;

    /// <summary>白 1 点 (色だけの四角)</summary>
    public uint White
    {
        get
        {
            if (_white == Invalid) _white = _create(2, 2, Enumerable.Repeat((byte)255, 16).ToArray());
            return _white;
        }
    }

    /// <summary>白い丸 (64 x 64。縁はなめらか)。9 分割して角の丸い四角にする</summary>
    public uint Circle
    {
        get
        {
            if (_circle != Invalid) return _circle;
            const int Size = 64;
            var pixels = new byte[Size * Size * 4];
            for (int y = 0; y < Size; y++)
            for (int x = 0; x < Size; x++)
            {
                var dx = x + 0.5f - Size * 0.5f;
                var dy = y + 0.5f - Size * 0.5f;
                var alpha = Math.Clamp(Size * 0.5f - MathF.Sqrt(dx * dx + dy * dy) + 0.5f, 0.0f, 1.0f);
                var o = (y * Size + x) * 4;
                pixels[o] = pixels[o + 1] = pixels[o + 2] = 255;
                pixels[o + 3] = (byte)(alpha * 255.0f + 0.5f);
            }
            _circle = _create(Size, Size, pixels);
            return _circle;
        }
    }

    /// <summary>画像 (アセットのパス)。空なら白。読めない物は白 (Console に 1 回だけ出す)</summary>
    public (uint Id, Vector2 Size) Get(string? path)
    {
        if (string.IsNullOrEmpty(path)) return (White, Vector2.Zero);
        if (_images.TryGetValue(path, out var cached)) return cached.Id == Invalid ? (White, Vector2.Zero) : cached;
        var result = (Id: Invalid, Size: Vector2.Zero);
        try
        {
            var database = AssetDatabase.Current;
            var file = database?.ToAssetPath(path) is { } assetPath ? database.FullPath(assetPath) : Path.GetFullPath(path);
            using var bitmap = new Bitmap(file);
            var width = bitmap.Width;
            var height = bitmap.Height;
            var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var rgba = new byte[width * height * 4];
            unsafe
            {
                for (int y = 0; y < height; y++)
                {
                    var row = (byte*)data.Scan0 + y * data.Stride;
                    for (int x = 0; x < width; x++)
                    {
                        var o = (y * width + x) * 4;
                        rgba[o]     = row[x * 4 + 2];   // BGRA → RGBA
                        rgba[o + 1] = row[x * 4 + 1];
                        rgba[o + 2] = row[x * 4];
                        rgba[o + 3] = row[x * 4 + 3];
                    }
                }
            }
            bitmap.UnlockBits(data);
            result = (_create(width, height, rgba), new Vector2(width, height));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.WriteLine($"[UI] Cannot load image {path} : {ex.Message}");
        }
        _images[path] = result;
        return result.Id == Invalid ? (White, Vector2.Zero) : result;
    }

    /// <summary>画像の大きさ (ピクセル。読めない物は 0)</summary>
    public Vector2 SizeOf(string? path) => string.IsNullOrEmpty(path) ? Vector2.Zero : Get(path).Size;

    /// <summary>画像を読み直す (ファイルが変わった時。前の GPU のテクスチャは C++ に残る)</summary>
    public void Forget(string path) => _images.Remove(path);

    public int ImageCount => _images.Count;
}

// ==========================================================================
// 文字 : Windows のフォントを System.Drawing で文字の表 (アトラス) に描いて GPU へ
//   大きさは決まった段 (12〜128 ピクセル) の 1 つで描き、間の大きさは縮めて使う (拡大縮小の度に作り直さない)
//   新しい文字はそのフレームの分をまとめて 1 枚の小さなテクスチャにする (前のテクスチャは作り直さない)
//   → 使った文字の分しか GPU のメモリを使わない。日本語も出る (フォントにある文字)
// ==========================================================================
public sealed class UIFonts
{
    private static readonly int[] PixelSizes = { 12, 16, 20, 24, 32, 40, 48, 64, 80, 96, 128 };
    private readonly Func<int, int, byte[], uint> _create;
    private readonly Dictionary<(string Family, bool Bold, bool Italic, int Size), UIFontAtlas> _atlases = new();

    internal UIFonts(Func<int, int, byte[], uint> create) => _create = create;

    /// <summary>font を pixelSize (画面のピクセル) で描く時の文字の表</summary>
    public UIFontAtlas Get(in UIFont font, float pixelSize)
    {
        var size = PixelSizes[^1];
        foreach (var s in PixelSizes) if (s >= pixelSize * 0.95f) { size = s; break; }
        var key = (string.IsNullOrEmpty(font.Family) ? UIFont.DefaultFamily : font.Family, font.Bold, font.Italic, size);
        if (!_atlases.TryGetValue(key, out var atlas)) _atlases[key] = atlas = new UIFontAtlas(key.Item1, font.Bold, font.Italic, size);
        return atlas;
    }

    /// <summary>まだ GPU に無い文字を送る。戻り値 = 送ったか</summary>
    public bool Flush()
    {
        var any = false;
        foreach (var atlas in _atlases.Values) any |= atlas.Flush(_create);
        return any;
    }

    /// <summary>1 行の幅 (デザインの単位)</summary>
    public float Measure(ReadOnlySpan<char> text, in UIFont font)
    {
        var atlas = Get(font, font.Size);
        var scale = font.Size / atlas.PixelSize;
        var width = 0.0f;
        foreach (var c in text) width += atlas.Get(c).Advance;
        return width * scale;
    }

    /// <summary>行の高さ (デザインの単位)</summary>
    public float LineHeight(in UIFont font)
    {
        var atlas = Get(font, font.Size);
        return atlas.LineHeight * font.Size / atlas.PixelSize;
    }

    public int AtlasCount => _atlases.Count;
    public int TextureCount => _atlases.Values.Sum(a => a.TextureCount);
}

public struct UIGlyph
{
    public float Advance;            // 次の文字までの幅 (文字の表のピクセル)
    public float OffsetX, OffsetY;   // 描く四角の左上 (文字の位置から)
    public float Width, Height;      // 描く四角の大きさ
    public Vector2 Uv0, Uv1;
    public uint Texture;             // Invalid = 描く物が無い (空白) / まだ送っていない (Pending)
    public bool Pending;
}

public sealed class UIFontAtlas : IDisposable
{
    private const int Pad = 2;
    private readonly DrawingFont _font;
    private readonly Bitmap _measureBitmap = new(1, 1);
    private readonly DrawingGraphics _measure;
    private readonly StringFormat _format;
    private readonly Dictionary<char, UIGlyph> _glyphs = new();
    private readonly List<char> _pending = new();
    private readonly float _cellHeight;

    public int   PixelSize  { get; }
    public float LineHeight { get; }
    public int   TextureCount { get; private set; }

    internal UIFontAtlas(string family, bool bold, bool italic, int pixelSize)
    {
        PixelSize = pixelSize;
        var style = (bold ? System.Drawing.FontStyle.Bold : 0) | (italic ? System.Drawing.FontStyle.Italic : 0);
        _font = new DrawingFont(family, pixelSize, style, System.Drawing.GraphicsUnit.Pixel);
        if (!_font.Name.Equals(family, StringComparison.OrdinalIgnoreCase))
        {
            // 無いフォント : 日本語の出る物から順に
            _font.Dispose();
            var fallback = new[] { "Meiryo", "Yu Gothic UI", "MS Gothic", "Segoe UI" }
                .FirstOrDefault(f => { using var test = new DrawingFont(f, 12); return test.Name.Equals(f, StringComparison.OrdinalIgnoreCase); }) ?? "Arial";
            _font = new DrawingFont(fallback, pixelSize, style, System.Drawing.GraphicsUnit.Pixel);
        }
        var em = _font.FontFamily.GetEmHeight(_font.Style);
        var ascent = _font.FontFamily.GetCellAscent(_font.Style) * pixelSize / (float)em;
        var descent = _font.FontFamily.GetCellDescent(_font.Style) * pixelSize / (float)em;
        _cellHeight = ascent + descent;
        LineHeight = _cellHeight;
        _measure = DrawingGraphics.FromImage(_measureBitmap);
        _measure.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
        _format = (StringFormat)StringFormat.GenericTypographic.Clone();
        _format.FormatFlags |= StringFormatFlags.MeasureTrailingSpaces | StringFormatFlags.NoClip;
    }

    /// <summary>文字の形 (初めての文字は幅だけ測って、次の Flush で GPU へ送る)</summary>
    public UIGlyph Get(char c)
    {
        if (_glyphs.TryGetValue(c, out var glyph)) return glyph;
        if (c == '\t') c = ' ';
        var advance = c == '\n' || c == '\r' ? 0.0f : _measure.MeasureString(c.ToString(), _font, System.Drawing.PointF.Empty, _format).Width;
        glyph = new UIGlyph
        {
            Advance = advance,
            OffsetX = -Pad,
            OffsetY = -Pad,
            Width   = MathF.Ceiling(advance) + Pad * 2,
            Height  = MathF.Ceiling(_cellHeight) + Pad * 2,
            Texture = UITextures.Invalid,
            Pending = !char.IsWhiteSpace(c) && !char.IsControl(c),
        };
        if (glyph.Pending) _pending.Add(c);
        _glyphs[c] = glyph;
        return glyph;
    }

    internal bool Flush(Func<int, int, byte[], uint> create)
    {
        if (_pending.Count == 0) return false;
        // 並べる (幅 1024 で折り返す)
        const int MaxWidth = 1024;
        var cellHeight = (int)MathF.Ceiling(_cellHeight) + Pad * 2;
        var positions = new List<(char C, int X, int Y)>(_pending.Count);
        int x = 0, y = 0, width = 0;
        foreach (var c in _pending)
        {
            var w = (int)_glyphs[c].Width;
            if (x + w > MaxWidth && x > 0) { x = 0; y += cellHeight; }
            positions.Add((c, x, y));
            x += w;
            width = Math.Max(width, x);
        }
        var height = y + cellHeight;
        using (var bitmap = new Bitmap(width, height, PixelFormat.Format32bppArgb))
        {
            using (var g = DrawingGraphics.FromImage(bitmap))
            {
                g.Clear(System.Drawing.Color.Black);
                g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
                g.SmoothingMode = SmoothingMode.HighQuality;
                foreach (var (c, px, py) in positions) g.DrawString(c.ToString(), _font, System.Drawing.Brushes.White, px + Pad, py + Pad, _format);
            }
            // 白黒の明るさ → 白の不透明度
            var data = bitmap.LockBits(new System.Drawing.Rectangle(0, 0, width, height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
            var rgba = new byte[width * height * 4];
            unsafe
            {
                for (int row = 0; row < height; row++)
                {
                    var src = (byte*)data.Scan0 + row * data.Stride;
                    for (int col = 0; col < width; col++)
                    {
                        var o = (row * width + col) * 4;
                        rgba[o] = rgba[o + 1] = rgba[o + 2] = 255;
                        rgba[o + 3] = src[col * 4 + 1];   // G (白黒なので R / G / B は同じ)
                    }
                }
            }
            bitmap.UnlockBits(data);
            var texture = create(width, height, rgba);
            TextureCount++;
            foreach (var (c, px, py) in positions)
            {
                var glyph = _glyphs[c];
                glyph.Texture = texture;
                glyph.Pending = false;
                glyph.Uv0 = new Vector2(px / (float)width, py / (float)height);
                glyph.Uv1 = new Vector2((px + glyph.Width) / width, (py + glyph.Height) / height);
                _glyphs[c] = glyph;
            }
        }
        _pending.Clear();
        return true;
    }

    public void Dispose()
    {
        _measure.Dispose();
        _measureBitmap.Dispose();
        _font.Dispose();
        _format.Dispose();
    }
}
