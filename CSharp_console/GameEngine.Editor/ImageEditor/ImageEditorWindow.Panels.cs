using System.Numerics;
using GameEngine.Imaging;
using GameEngine.Imaging.Processing;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// 画像エディターの左右の欄 (v2.3)
//   左 : 処理の並び。右 : 選んだ処理の設定 (トーンカーブ = ToneCurveWidget、周波数フィルター = スペクトルと黒丸)
//   値はつまみを離した時に元に戻す記録に入る (ImageEditorWindow.Draw の Commit)
// ==========================================================================
public sealed partial class ImageEditorWindow
{
    private readonly ToneCurveWidget _curve = new();
    private int _histogramVersion = -1;
    private float[] _histogram = [];

    private void drawOperationList()
    {
        ImGui.TextDisabled("Operations (top to bottom)");
        if (ImGui.Button("Add...")) ImGui.OpenPopup("##add");
        if (ImGui.BeginPopup("##add"))
        {
            foreach (var group in ImageOperation.Factories.Select(f => f()).GroupBy(o => o.Category))
            {
                if (!ImGui.BeginMenu(group.Key)) continue;
                foreach (var sample in group)
                {
                    var name = sample is ChannelScaleOperation ? "RGB Scale (R / G / B)" : sample.DisplayName;
                    if (ImGui.MenuItem(name)) Add(sample);
                }
                ImGui.EndMenu();
            }
            ImGui.EndPopup();
        }
        ImGui.Separator();
        if (Pipeline.Operations.Count == 0) ImGui.TextWrapped("No operations yet. Use Add... to apply a filter.");
        for (int i = 0; i < Pipeline.Operations.Count; i++)
        {
            var op = Pipeline.Operations[i];
            ImGui.PushID(i);
            bool enabled = op.Enabled;
            if (ImGui.Checkbox("##on", ref enabled)) { op.Enabled = enabled; Commit(); }
            ImGui.SameLine();
            if (!op.Enabled) ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
            if (ImGui.Selectable($"{i + 1}. {op.DisplayName}", SelectedIndex == i)) SelectedIndex = i;
            if (!op.Enabled) ImGui.PopStyleColor();
            ImGui.PopID();
        }
        if (SelectedIndex >= 0 && SelectedIndex < Pipeline.Operations.Count)
        {
            ImGui.Separator();
            ImGui.BeginDisabled(SelectedIndex == 0);
            if (ImGui.Button("Up")) Move(SelectedIndex, -1);
            ImGui.EndDisabled();
            ImGui.SameLine();
            ImGui.BeginDisabled(SelectedIndex == Pipeline.Operations.Count - 1);
            if (ImGui.Button("Down")) Move(SelectedIndex, 1);
            ImGui.EndDisabled();
            ImGui.SameLine();
            if (ImGui.Button("Duplicate")) { var copy = Pipeline.Operations[SelectedIndex].Clone(); Pipeline.Operations.Insert(SelectedIndex + 1, copy); SelectedIndex++; Commit(); }
            ImGui.SameLine();
            if (ImGui.Button("Remove")) Remove(SelectedIndex);
        }
    }

    private void drawSettings()
    {
        if (SelectedIndex >= 0 && SelectedIndex < Pipeline.Operations.Count)
        {
            var op = Pipeline.Operations[SelectedIndex];
            ImGui.Text(op.DisplayName);
            ImGui.Separator();
            float amount = op.Amount;
            if (ImGui.SliderFloat("Amount", ref amount, 0f, 1f)) op.Amount = amount;
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Blend with the input (0 = no effect, 1 = full)");
            drawOperationSettings(op);
        }
        else ImGui.TextDisabled("Select an operation to edit it.");

        if (Display == DisplayMode.Normal)
        {
            ImGui.Separator();
            ImGui.Text("Normal view (display only)");
            drawNormalSettings(NormalView);
        }

        // 結果の輝度のヒストグラム
        ImGui.Separator();
        if (Result != null && _histogramVersion != ProcessedCount)
        {
            _histogramVersion = ProcessedCount;
            var counts = ImageStatistics.Histogram(Result, null, 128);
            float max = Math.Max(1, counts.Max());
            _histogram = counts.Select(c => c / max).ToArray();
        }
        if (_histogram.Length > 0) ImGui.PlotHistogram("##hist", ref _histogram[0], _histogram.Length, 0, "Luminance (after)", 0f, 1f, new Vector2(-1, 70));
    }

    private void drawOperationSettings(ImageOperation op)
    {
        switch (op)
        {
            case GrayscaleOperation:
                ImGui.TextWrapped("Y = R * 0.30 + G * 0.59 + B * 0.11");
                break;
            case ChannelScaleOperation c:
            {
                int channel = (int)c.Channel;
                if (ImGui.Combo("Channel", ref channel, "Red\0Green\0Blue\0")) c.Channel = (ColorChannel)channel;
                bool gray = c.AsGray;
                if (ImGui.Checkbox("Show as gray", ref gray)) c.AsGray = gray;
                break;
            }
            case PosterizeOperation p:
            {
                int levels = p.Levels;
                if (ImGui.SliderInt("Levels", ref levels, 2, 64)) p.Levels = levels;
                break;
            }
            case SolarizeOperation s:
            {
                float t = s.Threshold;
                if (ImGui.SliderFloat("Threshold", ref t, 0f, 1f)) s.Threshold = t;
                break;
            }
            case InvertOperation:
                ImGui.TextWrapped("RGB = 1 - RGB (alpha is kept)");
                break;
            case SepiaOperation s:
            {
                float tone = s.Tone;
                if (ImGui.SliderFloat("Tone", ref tone, 0f, 2f)) s.Tone = tone;
                break;
            }
            case GammaOperation g:
            {
                float gamma = g.Gamma;
                if (ImGui.SliderFloat("Gamma", ref gamma, 0.1f, 5f, "%.2f", ImGuiSliderFlags.Logarithmic)) g.Gamma = gamma;
                ImGui.TextDisabled("out = in ^ (1 / gamma)");
                break;
            }
            case GaussianBlurOperation b:
            {
                float sigma = b.Sigma;
                if (ImGui.SliderFloat("Sigma (px)", ref sigma, 0f, 50f, "%.1f")) b.Sigma = sigma;
                break;
            }
            case SharpenOperation s:
            {
                float radius = s.Radius, strength = s.Strength;
                if (ImGui.SliderFloat("Radius (px)", ref radius, 0.3f, 10f, "%.1f")) s.Radius = radius;
                if (ImGui.SliderFloat("Strength", ref strength, 0f, 5f)) s.Strength = strength;
                break;
            }
            case SobelOperation s:
            {
                int direction = (int)s.Direction;
                if (ImGui.Combo("Direction", ref direction, "Both\0Horizontal (X)\0Vertical (Y)\0")) s.Direction = (EdgeDirection)direction;
                float strength = s.Strength;
                if (ImGui.SliderFloat("Strength", ref strength, 0f, 5f)) s.Strength = strength;
                bool gray = s.Grayscale;
                if (ImGui.Checkbox("Grayscale", ref gray)) s.Grayscale = gray;
                break;
            }
            case EmbossOperation e:
            {
                float angle = e.Angle, strength = e.Strength, distance = e.Distance;
                if (ImGui.SliderFloat("Light angle", ref angle, 0f, 360f, "%.0f deg")) e.Angle = angle;
                if (ImGui.SliderFloat("Strength", ref strength, 0f, 5f)) e.Strength = strength;
                if (ImGui.SliderFloat("Distance (px)", ref distance, 0.5f, 10f, "%.1f")) e.Distance = distance;
                bool gray = e.Grayscale;
                if (ImGui.Checkbox("Grayscale", ref gray)) e.Grayscale = gray;
                break;
            }
            case MosaicOperation m:
            {
                int block = m.BlockSize;
                if (ImGui.SliderInt("Block (px)", ref block, 2, 256)) m.BlockSize = block;
                break;
            }
            case ChromaticAberrationOperation c:
            {
                float shift = c.Shift, cx = c.CenterX, cy = c.CenterY;
                if (ImGui.SliderFloat("Shift (px)", ref shift, 0f, 50f, "%.1f")) c.Shift = shift;
                if (ImGui.SliderFloat("Center X", ref cx, 0f, 1f)) c.CenterX = cx;
                if (ImGui.SliderFloat("Center Y", ref cy, 0f, 1f)) c.CenterY = cy;
                break;
            }
            case FrequencyFilterOperation f:
                drawFrequencySettings(f);
                break;
            case ToneCurveOperation t:
                _curve.Draw(t, Result);
                break;
            case NormalMapOperation n:
                drawNormalSettings(n.Settings);
                break;
        }
    }

    private static void drawNormalSettings(NormalMapSettings settings)
    {
        ImGui.PushID("normal");
        float strength = settings.Strength, blur = settings.BlurSigma;
        if (ImGui.SliderFloat("Strength", ref strength, 0.1f, 20f, "%.2f", ImGuiSliderFlags.Logarithmic)) settings.Strength = strength;
        if (ImGui.SliderFloat("Height blur (px)", ref blur, 0f, 20f, "%.1f")) settings.BlurSigma = blur;
        int gradient = (int)settings.Gradient;
        if (ImGui.Combo("Gradient", ref gradient, "Sobel\0Central difference\0")) settings.Gradient = (NormalGradient)gradient;
        int convention = (int)settings.Convention;
        if (ImGui.Combo("Y direction", ref convention, "DirectX (Y-)\0OpenGL (Y+)\0")) settings.Convention = (NormalConvention)convention;
        bool invert = settings.InvertHeight;
        if (ImGui.Checkbox("Invert height (dark = high)", ref invert)) settings.InvertHeight = invert;
        ImGui.TextDisabled("Height = luminance (R*0.30 + G*0.59 + B*0.11)");
        ImGui.PopID();
    }

    // 周波数フィルター : スペクトル (フィルターに入る画像。直流が真ん中) と黒丸。丸の上をドラッグで半径を変える
    private void drawFrequencySettings(FrequencyFilterOperation f)
    {
        float radius = f.Radius, feather = f.Feather;
        if (ImGui.SliderFloat("Radius", ref radius, 0f, 1f, "%.3f", ImGuiSliderFlags.Logarithmic)) f.Radius = radius;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("1 = Nyquist frequency of the original image");
        if (ImGui.SliderFloat("Feather", ref feather, 0f, 0.5f, "%.3f")) f.Feather = feather;
        bool invert = f.Invert, keepMean = f.KeepMean;
        if (ImGui.Checkbox("Invert (low-pass)", ref invert)) f.Invert = invert;
        ImGui.SameLine();
        if (ImGui.Checkbox("Keep mean", ref keepMean)) f.KeepMean = keepMean;
        ImGui.TextDisabled(f.Invert ? "Keeps inside the circle (blur)" : "Removes inside the circle (edges)");

        float width = Math.Min(ImGui.GetContentRegionAvail().X, 300f);
        var size = new Vector2(width, width);
        var origin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##spectrum", size);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + size, 0xFF000000u);
        if (SpectrumTexture != NoTexture) draw.AddImage((IntPtr)SpectrumTexture, origin, origin + size);
        else draw.AddText(origin + new Vector2(8, 8), 0xFFC0C0C0u, "Computing spectrum...");
        // スペクトルはプレビューの画像の物 (端 = プレビューのナイキスト)。半径は元の画像のナイキストに対する割合なので、縮小率で割る
        var center = origin + size * 0.5f;
        float scale = PreviewScale;
        float toPixels = size.X * 0.5f / scale;
        draw.PushClipRect(origin, origin + size, true);
        uint fill = 0x90000000u;
        float r = f.Radius * toPixels;
        if (!f.Invert) draw.AddCircleFilled(center, r, fill, 64);
        else
        {
            // 丸の外を暗く (外側の輪を太い線で塗る)
            float outer = size.X;
            draw.AddCircle(center, (r + outer) * 0.5f, fill, 64, outer - r);
        }
        draw.AddCircle(center, r, 0xFF40C0FFu, 64, 2f);
        if (f.Feather > 0)
        {
            draw.AddCircle(center, Math.Max(0, (f.Radius - f.Feather * 0.5f) * toPixels), 0x8040C0FFu, 64, 1f);
            draw.AddCircle(center, (f.Radius + f.Feather * 0.5f) * toPixels, 0x8040C0FFu, 64, 1f);
        }
        draw.PopClipRect();
        if (ImGui.IsItemActive() && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var d = Vector2.Distance(ImGui.GetIO().MousePos, center);
            f.Radius = Math.Clamp(d / toPixels, 0f, 1f);
        }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Spectrum of the filter input (log magnitude, DC at the center). Drag to change the radius.");
    }
}

// ==========================================================================
// トーンカーブのグラフ (v2.3)
//   横 = 入力、縦 = 出力。背景はヒストグラム (結果)。クリック : 点を足す / ドラッグ : 動かす / 右クリック : 消す
//   両端の点は横に動かせない (縦だけ)。RGB (共通) / R / G / B を切り替える
// ==========================================================================
internal sealed class ToneCurveWidget
{
    private ColorChannel? _channel;
    private int _dragging = -1;
    private ImageBuffer? _histogramSource;
    private ColorChannel? _histogramChannel;
    private int[] _counts = [];

    public ColorChannel? Channel => _channel;

    public void Draw(ToneCurveOperation op, ImageBuffer? histogramSource)
    {
        if (ImGui.RadioButton("RGB", _channel == null)) _channel = null;
        ImGui.SameLine();
        if (ImGui.RadioButton("R", _channel == ColorChannel.Red)) _channel = ColorChannel.Red;
        ImGui.SameLine();
        if (ImGui.RadioButton("G", _channel == ColorChannel.Green)) _channel = ColorChannel.Green;
        ImGui.SameLine();
        if (ImGui.RadioButton("B", _channel == ColorChannel.Blue)) _channel = ColorChannel.Blue;
        ImGui.SameLine();
        var points = op.CurveOf(_channel);
        if (ImGui.SmallButton("Reset curve")) { points.Clear(); points.AddRange(ToneCurve.Identity()); }

        float width = Math.Min(ImGui.GetContentRegionAvail().X, 300f);
        var size = new Vector2(width, width);
        var origin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##curve", size, ImGuiButtonFlags.MouseButtonLeft | ImGuiButtonFlags.MouseButtonRight);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + size, 0xFF181818u);
        Vector2 toScreen(float x, float y) => origin + new Vector2(x * size.X, (1 - y) * size.Y);
        (float X, float Y) fromScreen(Vector2 p) => (Math.Clamp((p.X - origin.X) / size.X, 0f, 1f), Math.Clamp(1 - (p.Y - origin.Y) / size.Y, 0f, 1f));

        // ヒストグラム
        if (histogramSource != null)
        {
            // 結果かチャンネルが変わった時だけ数える (毎フレーム 400 万ピクセルを数えない)
            if (!ReferenceEquals(histogramSource, _histogramSource) || _histogramChannel != _channel)
            {
                _histogramSource = histogramSource;
                _histogramChannel = _channel;
                _counts = ImageStatistics.Histogram(histogramSource, _channel, 64);
            }
            var counts = _counts;
            float max = Math.Max(1, counts.Max());
            for (int i = 0; i < counts.Length; i++)
            {
                float h = counts[i] / max;
                draw.AddRectFilled(toScreen(i / 64f, h), toScreen((i + 1) / 64f, 0), 0x40FFFFFFu);
            }
        }
        for (int i = 1; i < 4; i++)
        {
            draw.AddLine(toScreen(i / 4f, 0), toScreen(i / 4f, 1), 0x30FFFFFFu);
            draw.AddLine(toScreen(0, i / 4f), toScreen(1, i / 4f), 0x30FFFFFFu);
        }
        draw.AddLine(toScreen(0, 0), toScreen(1, 1), 0x40FFFFFFu);

        uint color = _channel switch { ColorChannel.Red => 0xFF5050FFu, ColorChannel.Green => 0xFF50FF50u, ColorChannel.Blue => 0xFFFF7050u, _ => 0xFFFFFFFFu };
        var sorted = ToneCurve.Normalize(points);
        var prev = toScreen(0, ToneCurve.Evaluate(sorted, 0));
        for (int i = 1; i <= 128; i++)
        {
            float x = i / 128f;
            var next = toScreen(x, Math.Clamp(ToneCurve.Evaluate(sorted, x), 0f, 1f));
            draw.AddLine(prev, next, color, 2f);
            prev = next;
        }

        // 操作
        var mouse = ImGui.GetIO().MousePos;
        int hoveredPoint = -1;
        for (int i = 0; i < points.Count; i++)
            if (Vector2.Distance(toScreen(points[i].X, points[i].Y), mouse) < 7) hoveredPoint = i;
        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Left))
        {
            if (hoveredPoint >= 0) _dragging = hoveredPoint;
            else
            {
                var (x, y) = fromScreen(mouse);
                points.Add(new CurvePoint(x, y));
                _dragging = points.Count - 1;
            }
        }
        if (ImGui.IsItemHovered() && ImGui.IsMouseClicked(ImGuiMouseButton.Right) && hoveredPoint >= 0 && points.Count > 2) points.RemoveAt(hoveredPoint);
        if (_dragging >= 0 && _dragging < points.Count && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var (x, y) = fromScreen(mouse);
            var p = points[_dragging];
            bool isEnd = p.X <= 0f || p.X >= 1f;
            if (!isEnd)
            {
                // 隣の点を越えない
                float left = points.Where((q, i) => i != _dragging && q.X < p.X).Select(q => q.X).DefaultIfEmpty(0f).Max();
                float right = points.Where((q, i) => i != _dragging && q.X > p.X).Select(q => q.X).DefaultIfEmpty(1f).Min();
                p.X = Math.Clamp(x, left + 0.005f, right - 0.005f);
            }
            p.Y = y;
        }
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) _dragging = -1;
        foreach (var (p, i) in points.Select((p, i) => (p, i)))
            draw.AddCircleFilled(toScreen(p.X, p.Y), i == hoveredPoint || i == _dragging ? 6f : 4f, color);
        if (ImGui.IsItemHovered())
        {
            var (x, y) = fromScreen(mouse);
            ImGui.SetTooltip($"in {x * 255:F0} -> out {ToneCurve.Evaluate(sorted, x) * 255:F0}\nClick: add / drag: move / right click: remove");
        }
        ImGui.TextDisabled($"{points.Count} points");
    }
}
