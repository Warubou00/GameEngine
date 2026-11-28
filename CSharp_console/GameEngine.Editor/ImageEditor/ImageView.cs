using System.Numerics;
using GameEngine.Imaging;
using GameEngine.Imaging.Processing;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// 画像の表示 (v2.3。画像エディターの真ん中)
//   After / Before : 1 枚。Side by side : 左 = Before / 右 = After (拡大と位置は同じ)。Split : 縦の線の左 = Before / 右 = After (線をドラッグ)
//   ホイール : マウスの所を中心に拡大縮小 / ドラッグ (左・中・右) : 移動 / ダブルクリック : 全体
//   透明な所は市松模様。マウスの下のピクセルの値 (元の画像の座標、Before / After の RGBA と輝度) を下に出す
//   座標は元の画像のピクセルで持つ (プレビューを縮めていても、拡大の 1:1 は元の画像の 1 ピクセル = 画面の 1 ピクセル)
// ==========================================================================
internal sealed class ImageView
{
    private float _zoom = 1f;              // 画面のピクセル / 元の画像のピクセル
    private Vector2 _center;               // 窓の真ん中に来る、元の画像の座標
    private bool _fit = true;
    private float _split = 0.5f;           // Split の線の位置 (窓の幅の割合)
    private bool _draggingSplit;

    public float Zoom => _zoom;

    public void RequestFit() => _fit = true;

    public void RequestActualSize(ImageEditorWindow window)
    {
        _fit = false;
        _zoom = 1f;
        _center = new Vector2(window.Original.Width, window.Original.Height) * 0.5f;
    }

    public void Draw(ImageEditorWindow window)
    {
        var origin = ImGui.GetCursorScreenPos();
        var size = Vector2.Max(ImGui.GetContentRegionAvail(), new Vector2(32));
        ImGui.InvisibleButton("##canvas", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight | ImGuiButtonFlags.MouseButtonMiddle);
        bool hovered = ImGui.IsItemHovered();
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + size, 0xFF202020u);

        var imageSize = new Vector2(window.Original.Width, window.Original.Height);
        bool sideBySide = window.Compare == ImageEditorWindow.CompareMode.SideBySide;
        var paneSize = sideBySide ? new Vector2((size.X - 4) * 0.5f, size.Y) : size;
        if (_fit)
        {
            _zoom = Math.Min(paneSize.X / imageSize.X, paneSize.Y / imageSize.Y) * 0.95f;
            _center = imageSize * 0.5f;
        }

        // 入力 (どちらの窓の上でも同じ拡大・位置)
        var io = ImGui.GetIO();
        var mouse = io.MousePos;
        var pane0 = origin;
        var pane1 = origin + new Vector2(paneSize.X + 4, 0);
        var mousePane = sideBySide && mouse.X >= pane1.X ? pane1 : pane0;
        var paneCenter = mousePane + paneSize * 0.5f;
        float splitX = origin.X + _split * size.X;
        bool nearSplit = window.Compare == ImageEditorWindow.CompareMode.Split && Math.Abs(mouse.X - splitX) < 6 && hovered;
        if (hovered && ImGui.IsMouseClicked(ImGuiMouseButton.Left) && nearSplit) _draggingSplit = true;
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) _draggingSplit = false;
        if (_draggingSplit) _split = Math.Clamp((mouse.X - origin.X) / size.X, 0f, 1f);
        else if (ImGui.IsItemActive() && (ImGui.IsMouseDragging(ImGuiMouseButton.Left, 1) || ImGui.IsMouseDragging(ImGuiMouseButton.Right, 1) || ImGui.IsMouseDragging(ImGuiMouseButton.Middle, 1)))
        {
            _center -= io.MouseDelta / _zoom;
            _fit = false;
        }
        if (hovered && io.MouseWheel != 0)
        {
            var before = _center + (mouse - paneCenter) / _zoom;
            _zoom = Math.Clamp(_zoom * MathF.Pow(1.15f, io.MouseWheel), 0.02f, 64f);
            _center = before - (mouse - paneCenter) / _zoom;
            _fit = false;
        }
        if (hovered && ImGui.IsMouseDoubleClicked(ImGuiMouseButton.Left) && !nearSplit) _fit = true;
        if (nearSplit || _draggingSplit) ImGui.SetMouseCursor(ImGuiMouseCursor.ResizeEW);

        // 描く
        if (window.AfterTexture == ImageEditorWindow.NoTexture || window.BeforeTexture == ImageEditorWindow.NoTexture)
        {
            draw.AddText(origin + new Vector2(12, 12), 0xFFC0C0C0u, "Processing...");
            return;
        }
        switch (window.Compare)
        {
            case ImageEditorWindow.CompareMode.After:
                drawImage(draw, window.AfterTexture, pane0, paneSize, imageSize, null);
                break;
            case ImageEditorWindow.CompareMode.Before:
                drawImage(draw, window.BeforeTexture, pane0, paneSize, imageSize, null);
                break;
            case ImageEditorWindow.CompareMode.SideBySide:
                drawImage(draw, window.BeforeTexture, pane0, paneSize, imageSize, null);
                drawImage(draw, window.AfterTexture, pane1, paneSize, imageSize, null);
                label(draw, pane0 + new Vector2(8, 6), "Before");
                label(draw, pane1 + new Vector2(8, 6), "After");
                break;
            default:
                drawImage(draw, window.BeforeTexture, pane0, paneSize, imageSize, (origin.X, splitX));
                drawImage(draw, window.AfterTexture, pane0, paneSize, imageSize, (splitX, origin.X + size.X));
                draw.AddLine(new Vector2(splitX, origin.Y), new Vector2(splitX, origin.Y + size.Y), _draggingSplit || nearSplit ? 0xFF40C0FFu : 0xFFFFFFFFu, 2f);
                label(draw, origin + new Vector2(8, 6), "Before");
                label(draw, new Vector2(origin.X + size.X - 52, origin.Y + 6), "After");
                break;
        }

        // マウスの下のピクセル
        var footer = $"{_zoom * 100:F0}%";
        if (hovered)
        {
            var p = _center + (mouse - paneCenter) / _zoom;
            int ox = (int)MathF.Floor(p.X), oy = (int)MathF.Floor(p.Y);
            if (ox >= 0 && oy >= 0 && ox < window.Original.Width && oy < window.Original.Height)
            {
                float scale = window.PreviewScale;
                int px = Math.Min((int)(ox * scale), window.PreviewSource.Width - 1), py = Math.Min((int)(oy * scale), window.PreviewSource.Height - 1);
                footer += $"   ({ox}, {oy})   Before {rgba(window.PreviewSource, px, py)}";
                if (window.Result is { } result && result.Width == window.PreviewSource.Width) footer += $"   After {rgba(result, px, py)}";
            }
        }
        var footerPos = new Vector2(origin.X + 6, origin.Y + size.Y - ImGui.GetTextLineHeight() - 6);
        draw.AddRectFilled(footerPos - new Vector2(4, 2), footerPos + ImGui.CalcTextSize(footer) + new Vector2(4, 2), 0xB0000000u);
        draw.AddText(footerPos, 0xFFE0E0E0u, footer);
    }

    private static string rgba(ImageBuffer image, int x, int y)
    {
        var (r, g, b, a) = image.GetPixel(x, y);
        return $"{ImageBuffer.ToByte(r)},{ImageBuffer.ToByte(g)},{ImageBuffer.ToByte(b)},{ImageBuffer.ToByte(a)} Y{ImageBuffer.ToByte(ImageMath.Luminance(r, g, b))}";
    }

    private static void label(ImDrawListPtr draw, Vector2 pos, string text)
    {
        draw.AddRectFilled(pos - new Vector2(4, 2), pos + ImGui.CalcTextSize(text) + new Vector2(4, 2), 0xB0000000u);
        draw.AddText(pos, 0xFFFFFFFFu, text);
    }

    // clipX : 横の切り取り (Split の左 / 右)
    private void drawImage(ImDrawListPtr draw, uint texture, Vector2 pane, Vector2 paneSize, Vector2 imageSize, (float Min, float Max)? clipX)
    {
        var clipMin = pane;
        var clipMax = pane + paneSize;
        if (clipX is { } c) { clipMin.X = Math.Max(clipMin.X, c.Min); clipMax.X = Math.Min(clipMax.X, c.Max); }
        if (clipMax.X <= clipMin.X) return;
        draw.PushClipRect(clipMin, clipMax, true);
        var topLeft = pane + paneSize * 0.5f - _center * _zoom;
        var bottomRight = topLeft + imageSize * _zoom;
        // 透明な所が分かるように市松模様 (見えている所だけ)
        var from = Vector2.Max(topLeft, clipMin);
        var to = Vector2.Min(bottomRight, clipMax);
        const float Cell = 12f;
        if (to.X > from.X && to.Y > from.Y)
        {
            draw.AddRectFilled(from, to, 0xFF9A9A9Au);
            for (float y = MathF.Floor((from.Y - topLeft.Y) / Cell) * Cell + topLeft.Y; y < to.Y; y += Cell)
                for (float x = MathF.Floor((from.X - topLeft.X) / Cell) * Cell + topLeft.X; x < to.X; x += Cell)
                {
                    int ix = (int)MathF.Round((x - topLeft.X) / Cell), iy = (int)MathF.Round((y - topLeft.Y) / Cell);
                    if (((ix + iy) & 1) == 0) continue;
                    draw.AddRectFilled(Vector2.Max(new Vector2(x, y), from), Vector2.Min(new Vector2(x + Cell, y + Cell), to), 0xFF6A6A6Au);
                }
        }
        draw.AddImage((IntPtr)texture, topLeft, bottomRight);
        draw.PopClipRect();
    }
}
