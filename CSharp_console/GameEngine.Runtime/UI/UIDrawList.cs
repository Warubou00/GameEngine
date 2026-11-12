using System.Numerics;
using GameEngine.Rendering;

namespace GameEngine;

// ==========================================================================
// UI の描画の中身 (v1.7)。四角 (4 つの角 + UV + 色 + テクスチャ + 切り取りの四角) を積む
//   ゲーム (Player) : BuildBuffers で頂点 / インデックス / 命令にして Graphics.SubmitUI へ (C++ は ImGui と同じ UI のパスで描く)
//   エディター      : 同じ四角を ImGui の描画のリストへ足す (シーンビューの絵の上 / Widget Designer の中)
//   同じテクスチャ + 同じ切り取りが続く間は 1 つの命令にまとめる
// ==========================================================================
public sealed class UIDrawList
{
    public struct Quad
    {
        public Vector2 P0, P1, P2, P3;   // 左上 / 右上 / 右下 / 左下 (ピクセル)
        public Vector2 Uv0, Uv1;         // 左上 / 右下
        public uint    Color;
        public uint    Texture;
        public Vector4 Clip;             // 左 / 上 / 右 / 下 (ピクセル)
    }

    private Quad[] _quads = new Quad[1024];
    private int _count;

    public int Count => _count;
    public ReadOnlySpan<Quad> Quads => new(_quads, 0, _count);

    public void Clear() => _count = 0;

    public void Add(in Quad quad)
    {
        if (_count == _quads.Length) Array.Resize(ref _quads, _count * 2);
        _quads[_count++] = quad;
    }

    /// <summary>四角 (自分の座標の min〜max を transform でピクセルへ) を足す</summary>
    public void AddRect(in Matrix3x2 transform, Vector2 min, Vector2 max, Vector2 uv0, Vector2 uv1, uint color, uint texture, Vector4 clip)
    {
        if ((color >> 24) == 0 || texture == UITextures.Invalid) return;   // 透明 / テクスチャが無い物は描かない
        if (_count == _quads.Length) Array.Resize(ref _quads, _count * 2);
        ref var q = ref _quads[_count++];
        q.P0 = Vector2.Transform(min, transform);
        q.P1 = Vector2.Transform(new Vector2(max.X, min.Y), transform);
        q.P2 = Vector2.Transform(max, transform);
        q.P3 = Vector2.Transform(new Vector2(min.X, max.Y), transform);
        q.Uv0 = uv0;
        q.Uv1 = uv1;
        q.Color = color;
        q.Texture = texture;
        q.Clip = clip;
    }

    // ---------------------------------------------------------------------
    // C++ へ渡す形 (UIVertex / ushort / UIDrawCommand) にする
    // ---------------------------------------------------------------------
    private UIVertex[] _vertices = Array.Empty<UIVertex>();
    private ushort[] _indices = Array.Empty<ushort>();
    private UIDrawCommand[] _commands = new UIDrawCommand[64];

    public (ReadOnlyMemory<UIVertex> Vertices, ReadOnlyMemory<ushort> Indices, ReadOnlyMemory<UIDrawCommand> Commands) BuildBuffers()
    {
        if (_vertices.Length < _count * 4) _vertices = new UIVertex[Math.Max(1024, _count * 8)];
        if (_indices.Length < _count * 6) _indices = new ushort[Math.Max(1536, _count * 12)];
        var commandCount = 0;
        const int QuadsPerBlock = 16384;   // 16bit のインデックスで指せる頂点 (65536 = 16384 個の四角) ごとに VertexOffset を変える
        for (int i = 0; i < _count; i++)
        {
            ref readonly var q = ref _quads[i];
            var v = i * 4;
            _vertices[v]     = new UIVertex { Position = q.P0, UV = q.Uv0, Color = q.Color };
            _vertices[v + 1] = new UIVertex { Position = q.P1, UV = new Vector2(q.Uv1.X, q.Uv0.Y), Color = q.Color };
            _vertices[v + 2] = new UIVertex { Position = q.P2, UV = q.Uv1, Color = q.Color };
            _vertices[v + 3] = new UIVertex { Position = q.P3, UV = new Vector2(q.Uv0.X, q.Uv1.Y), Color = q.Color };
            var local = (ushort)((i % QuadsPerBlock) * 4);
            var n = i * 6;
            _indices[n] = local; _indices[n + 1] = (ushort)(local + 1); _indices[n + 2] = (ushort)(local + 2);
            _indices[n + 3] = local; _indices[n + 4] = (ushort)(local + 2); _indices[n + 5] = (ushort)(local + 3);

            var block = i / QuadsPerBlock;
            var sameAsLast = commandCount > 0 && i % QuadsPerBlock != 0 &&
                             _commands[commandCount - 1].Texture == q.Texture && _commands[commandCount - 1].Clip == q.Clip;
            if (sameAsLast)
            {
                _commands[commandCount - 1].IndexCount += 6;
                continue;
            }
            if (commandCount == _commands.Length) Array.Resize(ref _commands, commandCount * 2);
            _commands[commandCount++] = new UIDrawCommand
            {
                IndexCount   = 6,
                IndexOffset  = (uint)n,
                VertexOffset = (uint)(block * QuadsPerBlock * 4),
                Texture      = q.Texture,
                Clip         = q.Clip,
            };
        }
        return (_vertices.AsMemory(0, _count * 4), _indices.AsMemory(0, _count * 6), _commands.AsMemory(0, commandCount));
    }
}

// ==========================================================================
// 描く道具 (Widget の OnPaint が使う)。今の Widget の変換 / 切り取り / 不透明度を持つ
// ==========================================================================
public sealed class UIPainter
{
    internal UIDrawList List = null!;
    internal UIResources Resources = null!;
    internal Matrix3x2 Transform;
    internal Vector4 Clip;
    internal float Opacity = 1.0f;
    internal float PixelScale = 1.0f;   // デザインの 1 単位が何ピクセルか (文字の細かさを選ぶ)
    internal bool MissingGlyphs;         // まだ GPU に無い文字があった (送ってから描き直す)

    /// <summary>色の四角</summary>
    public void Rect(Vector2 min, Vector2 max, Vector4 color)
    {
        List.AddRect(Transform, min, max, Vector2.Zero, Vector2.One, UIColors.Pack(color, Opacity), Resources.Textures.White, Clip);
    }

    /// <summary>ブラシで四角を描く (tint は掛ける色)</summary>
    public void Brush(in UIBrush brush, Vector2 min, Vector2 max, Vector4 tint)
    {
        if (brush.DrawAs == UIBrushDrawType.None) return;
        var color = brush.Tint * tint;
        if (color.W <= 0.0f || max.X <= min.X || max.Y <= min.Y) return;
        var packed = UIColors.Pack(color, Opacity);
        switch (brush.DrawAs)
        {
        case UIBrushDrawType.RoundedBox:
        {
            var radius = Math.Min(brush.CornerRadius, Math.Min(max.X - min.X, max.Y - min.Y) * 0.5f);
            if (radius <= 0.0f) { List.AddRect(Transform, min, max, Vector2.Zero, Vector2.One, packed, Resources.Textures.White, Clip); return; }
            nineSlice(min, max, new UIMargin(radius), new UIMargin(0.5f), packed, Resources.Textures.Circle);
            return;
        }
        case UIBrushDrawType.Box:
        {
            var image = Resources.Textures.Get(brush.Image);
            var size = image.Size == Vector2.Zero ? Vector2.One : image.Size;
            var m = brush.Margin;
            nineSlice(min, max, new UIMargin(m.Left * size.X, m.Top * size.Y, m.Right * size.X, m.Bottom * size.Y), m, packed, image.Id);
            return;
        }
        default:
        {
            var image = Resources.Textures.Get(brush.Image);
            List.AddRect(Transform, min, max, Vector2.Zero, Vector2.One, packed, image.Id, Clip);
            return;
        }
        }
    }

    // 9 分割 : edge = 角の大きさ (自分の単位) / uv = 角の UV の幅 (0〜0.5)
    private void nineSlice(Vector2 min, Vector2 max, UIMargin edge, UIMargin uv, uint color, uint texture)
    {
        // 角が四角より大きい時は縮める
        var sx = Math.Min(1.0f, (max.X - min.X) / Math.Max(edge.Horizontal, 1e-4f));
        var sy = Math.Min(1.0f, (max.Y - min.Y) / Math.Max(edge.Vertical, 1e-4f));
        ReadOnlySpan<float> xs = stackalloc float[] { min.X, min.X + edge.Left * sx, max.X - edge.Right * sx, max.X };
        ReadOnlySpan<float> ys = stackalloc float[] { min.Y, min.Y + edge.Top * sy, max.Y - edge.Bottom * sy, max.Y };
        ReadOnlySpan<float> us = stackalloc float[] { 0, uv.Left, 1 - uv.Right, 1 };
        ReadOnlySpan<float> vs = stackalloc float[] { 0, uv.Top, 1 - uv.Bottom, 1 };
        for (int y = 0; y < 3; y++)
        for (int x = 0; x < 3; x++)
        {
            if (xs[x + 1] <= xs[x] || ys[y + 1] <= ys[y]) continue;
            List.AddRect(Transform, new Vector2(xs[x], ys[y]), new Vector2(xs[x + 1], ys[y + 1]),
                         new Vector2(us[x], vs[y]), new Vector2(us[x + 1], vs[y + 1]), color, texture, Clip);
        }
    }

    /// <summary>文字 (1 行。position = 左上)。戻り値 = 幅</summary>
    public float Text(ReadOnlySpan<char> text, Vector2 position, in UIFont font, Vector4 color)
    {
        var atlas = Resources.Fonts.Get(font, font.Size * PixelScale);
        var scale = font.Size / atlas.PixelSize;   // 文字の表のピクセル → デザインの単位
        var packed = UIColors.Pack(color, Opacity);
        var x = position.X;
        foreach (var c in text)
        {
            var glyph = atlas.Get(c);
            if (glyph.Pending) MissingGlyphs = true;
            else if (glyph.Texture != UITextures.Invalid)
            {
                var min = new Vector2(x + glyph.OffsetX * scale, position.Y + glyph.OffsetY * scale);
                List.AddRect(Transform, min, min + new Vector2(glyph.Width, glyph.Height) * scale, glyph.Uv0, glyph.Uv1, packed, glyph.Texture, Clip);
            }
            x += glyph.Advance * scale;
        }
        return x - position.X;
    }
}
