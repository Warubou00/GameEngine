using System.Numerics;
using GameEngine.Imaging.Codecs;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// スプライトのアニメーションの窓 (v2.4)。Project で .spriteanim.json をダブルクリック
//   左   : コマの一覧 (小さな絵 / 時間 / イベント)。Project の画像をドラッグで足す、Up / Down / Duplicate / Remove
//   中央 : プレビュー (再生 / 一時停止 / 止める / 1 コマ送り / 速さ。市松模様と中心の印)
//   右   : 設定 (FrameRate / Loop / PixelsPerUnit / Pivot)、選んだコマ (画像 / 四角 / 時間 / イベント)、
//          シートを切る (列 / 行 / コマ数 / 余白 / 間隔。切る線を画像の上に出す → コマを置き換える / 後ろに足す)
//   アセットはシーンと同じインスタンスを直に変える (再生中も次のフレームから新しいコマ)。少し後に自動で保存 (Ctrl+S ですぐ)
//   Ctrl+Z / Ctrl+Y : 元に戻す / やり直す (JSON を積む)。Animate in scene : 編集中もシーンのスプライトを動かす
// ==========================================================================
public sealed class SpriteAnimationEditor
{
    private const double SaveDelay = 0.5;
    private readonly Engine _engine;
    private readonly AssetDatabase _database;

    public string? Path { get; private set; }
    public SpriteAnimation? Asset { get; private set; }
    public bool IsOpen => Path != null;
    public bool HasFocus { get; private set; }
    public bool Dirty => _dirty;
    public int SaveCount { get; private set; }
    public int Selected = -1;

    // プレビュー
    public bool Playing = true;
    public float PreviewSpeed = 1f;
    public float PreviewTime;
    public int PreviewFrame { get; private set; }

    // シートを切る
    public string SheetImage = "";
    public int SheetColumns = 4, SheetRows = 1, SheetCount, SheetMarginX, SheetMarginY, SheetSpacingX, SheetSpacingY;

    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private string _committed = "";
    private bool _dirty;
    private double _changedAt;
    private bool _focusNext;
    private readonly Dictionary<string, (int Width, int Height)> _imageSizes = new(StringComparer.OrdinalIgnoreCase);

    public SpriteAnimationEditor(Engine engine, AssetDatabase database)
    {
        _engine = engine;
        _database = database;
    }

    /// <summary>開く (アセットのパス)。読めなければ false (理由は Console)</summary>
    public bool Open(string path)
    {
        Flush();
        try
        {
            var asset = _engine.Assets.LoadSpriteAnimation(path);
            Path = asset.Path;
            Asset = asset;
            _committed = asset.ToJson();
            _undo.Clear(); _redo.Clear();
            _dirty = false;
            Selected = asset.FrameCount > 0 ? 0 : -1;
            PreviewTime = 0;
            Playing = true;
            SheetImage = asset.Frames.FirstOrDefault()?.Image ?? "";
            _focusNext = true;
            return true;
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[SpriteAnimation] Failed to open {path} : {ex.Message}");
            return false;
        }
    }

    public void Close()
    {
        Flush();
        Path = null;
        Asset = null;
    }

    /// <summary>変えた物をすぐ書く</summary>
    public void Flush()
    {
        if (!_dirty || Asset == null) return;
        try
        {
            _engine.Assets.SaveSpriteAnimation(Asset);
            SaveCount++;
            _dirty = false;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[SpriteAnimation] Failed to save {Path} : {ex.Message}");
        }
    }

    /// <summary>変えた (元に戻す記録はつまみを離した時)</summary>
    public void Changed()
    {
        if (Asset == null) return;
        Asset.MarkChanged();
        _dirty = true;
        _changedAt = ImGui.GetTime();
    }

    /// <summary>今の中身を元に戻す記録に入れる (変わっていなければ何もしない)</summary>
    public void Commit()
    {
        if (Asset == null) return;
        var json = Asset.ToJson();
        if (json == _committed) return;
        _undo.Add(_committed);
        if (_undo.Count > 200) _undo.RemoveAt(0);
        _redo.Clear();
        _committed = json;
    }

    public int UndoCount => _undo.Count;
    public void Undo() => restore(_undo, _redo);
    public void Redo() => restore(_redo, _undo);

    private void restore(List<string> from, List<string> to)
    {
        Commit();
        if (Asset == null || from.Count == 0) return;
        to.Add(_committed);
        _committed = from[^1];
        from.RemoveAt(from.Count - 1);
        Asset.SetJson(_committed);
        Selected = Math.Min(Selected, Asset.FrameCount - 1);
        _dirty = true;
        _changedAt = ImGui.GetTime();
    }

    // ---------------------------------------------------------------------
    // コマの操作 (UI と確認用)
    // ---------------------------------------------------------------------

    public void AddFrame(string image)
    {
        if (Asset == null) return;
        int at = Selected >= 0 && Selected < Asset.FrameCount ? Selected + 1 : Asset.FrameCount;
        Asset.Frames.Insert(at, new SpriteFrame { Image = image });
        Selected = at;
        Changed();
        Commit();
    }

    public void RemoveFrame(int index)
    {
        if (Asset == null || index < 0 || index >= Asset.FrameCount) return;
        Asset.Frames.RemoveAt(index);
        Selected = Math.Min(Selected, Asset.FrameCount - 1);
        Changed();
        Commit();
    }

    public void MoveFrame(int index, int delta)
    {
        if (Asset == null) return;
        int to = index + delta;
        if (index < 0 || to < 0 || index >= Asset.FrameCount || to >= Asset.FrameCount) return;
        (Asset.Frames[index], Asset.Frames[to]) = (Asset.Frames[to], Asset.Frames[index]);
        if (Selected == index) Selected = to;
        Changed();
        Commit();
    }

    public void DuplicateFrame(int index)
    {
        if (Asset == null || index < 0 || index >= Asset.FrameCount) return;
        Asset.Frames.Insert(index + 1, Asset.Frames[index].Clone());
        Selected = index + 1;
        Changed();
        Commit();
    }

    /// <summary>画像の大きさ (ピクセル。読めなければ (0, 0))</summary>
    public (int Width, int Height) ImageSizeOf(string image)
    {
        if (string.IsNullOrEmpty(image)) return (0, 0);
        if (_imageSizes.TryGetValue(image, out var size)) return size;
        size = (0, 0);
        try
        {
            var file = _database.ToAssetPath(image) is { } assetPath ? _database.FullPath(assetPath) : image;
            var decoded = ImageCodec.Load(file);
            size = (decoded.Info.Width, decoded.Info.Height);
        }
        catch (Exception ex) when (ex is IOException or ImageFormatException or UnauthorizedAccessException) { }
        _imageSizes[image] = size;
        return size;
    }

    /// <summary>シートを切ってコマにする (append = false は全部置き換える)。戻り値 = 作ったコマの数</summary>
    public int SliceSheet(bool append)
    {
        if (Asset == null) return 0;
        var (w, h) = ImageSizeOf(SheetImage);
        if (w == 0) return 0;
        var frames = SpriteAnimation.SliceGrid(SheetImage, w, h, SheetColumns, SheetRows, SheetCount, SheetMarginX, SheetMarginY, SheetSpacingX, SheetSpacingY);
        if (frames.Count == 0) return 0;
        if (!append) Asset.Frames.Clear();
        Asset.Frames.AddRange(frames);
        Selected = append ? Asset.FrameCount - frames.Count : 0;
        Changed();
        Commit();
        return frames.Count;
    }

    // ---------------------------------------------------------------------
    // 窓
    // ---------------------------------------------------------------------

    public void Draw()
    {
        HasFocus = false;
        if (!IsOpen || Asset == null) return;
        var asset = Asset;
        var viewport = ImGui.GetMainViewport();
        ImGui.SetNextWindowSize(new Vector2(Math.Min(1200, viewport.WorkSize.X * 0.75f), Math.Min(720, viewport.WorkSize.Y * 0.75f)), ImGuiCond.FirstUseEver);
        ImGui.SetNextWindowPos(viewport.WorkPos + viewport.WorkSize * 0.5f, ImGuiCond.FirstUseEver, new Vector2(0.5f, 0.5f));
        if (_focusNext) { ImGui.SetNextWindowFocus(); _focusNext = false; }
        ImGui.SetNextWindowBgAlpha(1f);
        bool open = true;
        var title = $"Sprite Animation - {System.IO.Path.GetFileName(Path)}{(_dirty ? " *" : "")}###SpriteAnimationEditor";
        var visible = ImGui.Begin(title, ref open, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
        if (!open) { ImGui.End(); Close(); return; }
        if (visible)
        {
            HasFocus = ImGui.IsWindowFocused(ImGuiFocusedFlags.RootAndChildWindows);
            drawToolbar(asset);
            var avail = ImGui.GetContentRegionAvail();
            const float LeftWidth = 240, RightWidth = 320;
            ImGui.BeginChild("##frames", new Vector2(LeftWidth, avail.Y), ImGuiChildFlags.Borders);
            drawFrameList(asset);
            ImGui.EndChild();
            ImGui.SameLine();
            ImGui.BeginChild("##preview", new Vector2(Math.Max(100, avail.X - LeftWidth - RightWidth - 16), avail.Y), ImGuiChildFlags.Borders, ImGuiWindowFlags.NoScrollbar);
            drawPreview(asset);
            ImGui.EndChild();
            ImGui.SameLine();
            ImGui.BeginChild("##settings", new Vector2(RightWidth, avail.Y), ImGuiChildFlags.Borders);
            drawSettings(asset);
            ImGui.EndChild();
            if (HasFocus)
            {
                var io = ImGui.GetIO();
                if (!io.WantTextInput && io.KeyCtrl)
                {
                    if (ImGui.IsKeyPressed(ImGuiKey.Z)) { if (io.KeyShift) Redo(); else Undo(); }
                    if (ImGui.IsKeyPressed(ImGuiKey.Y)) Redo();
                    if (ImGui.IsKeyPressed(ImGuiKey.S)) Flush();
                }
                if (!io.WantTextInput && ImGui.IsKeyPressed(ImGuiKey.Delete) && Selected >= 0) RemoveFrame(Selected);
            }
        }
        ImGui.End();
        if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) Commit();
        if (_dirty && ImGui.GetTime() - _changedAt > SaveDelay && !ImGui.IsAnyItemActive()) Flush();
    }

    private void drawToolbar(SpriteAnimation asset)
    {
        if (ImGui.Button("Save")) Flush();
        ImGui.SameLine();
        ImGui.BeginDisabled(_undo.Count == 0);
        if (ImGui.Button("Undo")) Undo();
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(_redo.Count == 0);
        if (ImGui.Button("Redo")) Redo();
        ImGui.EndDisabled();
        ImGui.SameLine();
        bool preview = SpriteAnimator.PreviewInEditor;
        if (ImGui.Checkbox("Animate in scene", ref preview)) SpriteAnimator.PreviewInEditor = preview;
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Play sprite animations in the scene view while editing (events are not sent)");
        ImGui.SameLine();
        ImGui.TextDisabled($"{asset.FrameCount} frames / {asset.Duration:F2} s");
    }

    private void drawFrameList(SpriteAnimation asset)
    {
        ImGui.TextDisabled("Frames (drag images here)");
        // Project の画像を落とすと後ろに足す
        var listStart = ImGui.GetCursorScreenPos();
        for (int i = 0; i < asset.FrameCount; i++)
        {
            var frame = asset.Frames[i];
            ImGui.PushID(i);
            var texture = asset.TextureOf(i);
            var rowStart = ImGui.GetCursorPos();
            if (ImGui.Selectable("##row", Selected == i, ImGuiSelectableFlags.None, new Vector2(0, 40))) Selected = i;
            var after = ImGui.GetCursorPos();
            ImGui.SetCursorPos(rowStart + new Vector2(2, 2));
            if (texture.IsValid)
            {
                float s = 36f / Math.Max(texture.Width, texture.Height);
                ImGui.Image((IntPtr)texture.Texture, new Vector2(texture.Width, texture.Height) * s);
            }
            else ImGui.Dummy(new Vector2(36, 36));
            ImGui.SameLine();
            ImGui.BeginGroup();
            ImGui.Text($"{i + 1}. {System.IO.Path.GetFileName(frame.Image)}");
            ImGui.TextDisabled($"{asset.FrameDuration(i) * 1000:F0} ms{(string.IsNullOrEmpty(frame.Event) ? "" : $"  [{frame.Event}]")}");
            ImGui.EndGroup();
            ImGui.SetCursorPos(after);
            ImGui.PopID();
        }
        var remaining = Vector2.Max(ImGui.GetContentRegionAvail(), new Vector2(1, 40));
        ImGui.InvisibleButton("##drop", new Vector2(remaining.X, Math.Max(40, remaining.Y - 70)));
        if (ProjectPanel.Dragged is { Kind: AssetKind.Texture } dragged && ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                if (ImGui.AcceptDragDropPayload(ProjectPanel.Payload).NativePtr != null) AddFrame(dragged.Path);
            }
            ImGui.EndDragDropTarget();
        }
        if (asset.FrameCount == 0)
        {
            var p = listStart + new Vector2(8, 20);
            ImGui.GetWindowDrawList().AddText(p, ImGui.GetColorU32(ImGuiCol.TextDisabled), "Drop images from Project,\nor slice a sheet (right).");
        }
        ImGui.Separator();
        bool has = Selected >= 0 && Selected < asset.FrameCount;
        ImGui.BeginDisabled(!has || Selected == 0);
        if (ImGui.Button("Up")) MoveFrame(Selected, -1);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!has || Selected >= asset.FrameCount - 1);
        if (ImGui.Button("Down")) MoveFrame(Selected, 1);
        ImGui.EndDisabled();
        ImGui.SameLine();
        ImGui.BeginDisabled(!has);
        if (ImGui.Button("Duplicate")) DuplicateFrame(Selected);
        ImGui.SameLine();
        if (ImGui.Button("Remove")) RemoveFrame(Selected);
        ImGui.EndDisabled();
    }

    private void drawPreview(SpriteAnimation asset)
    {
        // 再生の操作
        if (ImGui.Button(Playing ? "Pause" : "Play")) Playing = !Playing;
        ImGui.SameLine();
        if (ImGui.Button("Stop")) { Playing = false; PreviewTime = 0; }
        ImGui.SameLine();
        if (ImGui.Button("Next")) { Playing = false; PreviewTime = timeOfFrame(asset, (PreviewFrame + 1) % Math.Max(1, asset.FrameCount)); }
        ImGui.SameLine();
        ImGui.SetNextItemWidth(120);
        ImGui.SliderFloat("Speed", ref PreviewSpeed, 0.1f, 4f, "%.2fx");
        if (Playing) PreviewTime += ImGui.GetIO().DeltaTime * PreviewSpeed;
        var sample = asset.Sample(PreviewTime, asset.Loop);
        if (sample.Finished && asset.Loop == SpriteLoopMode.Once && Playing) PreviewTime = 0;   // プレビューは繰り返して見せる
        PreviewFrame = Math.Max(0, sample.Frame);
        ImGui.SameLine();
        ImGui.TextDisabled($"frame {PreviewFrame + 1} / {asset.FrameCount}   {PreviewTime:F2} s");

        var origin = ImGui.GetCursorScreenPos();
        var size = Vector2.Max(ImGui.GetContentRegionAvail(), new Vector2(32));
        ImGui.InvisibleButton("##canvas", size);
        var draw = ImGui.GetWindowDrawList();
        draw.AddRectFilled(origin, origin + size, 0xFF202020u);
        if (asset.FrameCount == 0) return;
        var texture = asset.TextureOf(PreviewFrame);
        if (!texture.IsValid) { draw.AddText(origin + new Vector2(10, 10), 0xFF8080FFu, "Image not loaded"); return; }
        // 全部のコマが入る大きさで (コマで大きさが違っても動かない)
        float maxW = 1, maxH = 1;
        for (int i = 0; i < asset.FrameCount; i++) { var t = asset.TextureOf(i); maxW = Math.Max(maxW, t.Width); maxH = Math.Max(maxH, t.Height); }
        float scale = Math.Min(size.X * 0.8f / maxW, size.Y * 0.8f / maxH);
        if (scale > 1) scale = MathF.Floor(scale);   // ドット絵がにじまないように整数倍
        var pivotPos = origin + size * 0.5f + new Vector2((asset.Pivot.X - 0.5f) * maxW, (0.5f - asset.Pivot.Y) * maxH) * scale;
        var imageSize = new Vector2(texture.Width, texture.Height) * scale;
        var topLeft = pivotPos - new Vector2(asset.Pivot.X * imageSize.X, (1 - asset.Pivot.Y) * imageSize.Y);
        checker(draw, topLeft, topLeft + imageSize);
        draw.AddImage((IntPtr)texture.Texture, topLeft, topLeft + imageSize);
        draw.AddRect(topLeft, topLeft + imageSize, 0x60FFFFFFu);
        draw.AddLine(pivotPos - new Vector2(8, 0), pivotPos + new Vector2(8, 0), 0xFF40C0FFu, 2f);
        draw.AddLine(pivotPos - new Vector2(0, 8), pivotPos + new Vector2(0, 8), 0xFF40C0FFu, 2f);
    }

    private static float timeOfFrame(SpriteAnimation asset, int frame)
    {
        float t = 0;
        for (int i = 0; i < frame; i++) t += asset.FrameDuration(i);
        return t + 0.0001f;
    }

    private static void checker(ImDrawListPtr draw, Vector2 min, Vector2 max)
    {
        draw.AddRectFilled(min, max, 0xFF9A9A9Au);
        const float Cell = 10f;
        for (float y = min.Y; y < max.Y; y += Cell)
            for (float x = min.X + (((int)((y - min.Y) / Cell) & 1) == 0 ? Cell : 0); x < max.X; x += Cell * 2)
                draw.AddRectFilled(new Vector2(x, y), Vector2.Min(new Vector2(x + Cell, y + Cell), max), 0xFF6A6A6Au);
    }

    private void drawSettings(SpriteAnimation asset)
    {
        ImGui.SeparatorText("Animation");
        float rate = asset.FrameRate;
        if (ImGui.DragFloat("Frame rate", ref rate, 0.2f, 0.1f, 120f, "%.1f fps")) { asset.FrameRate = Math.Max(0.1f, rate); Changed(); }
        int loop = (int)asset.Loop;
        if (ImGui.Combo("Loop", ref loop, "Loop\0Once\0PingPong\0")) { asset.Loop = (SpriteLoopMode)loop; Changed(); }
        float ppu = asset.PixelsPerUnit;
        if (ImGui.DragFloat("Pixels per unit", ref ppu, 1f, 1f, 4096f, "%.0f")) { asset.PixelsPerUnit = Math.Max(1f, ppu); Changed(); }
        if (ImGui.IsItemHovered()) ImGui.SetTooltip("GameObject size : this many pixels = 1 m");
        var pivot = asset.Pivot;
        if (ImGui.SliderFloat2("Pivot", ref pivot, 0f, 1f)) { asset.Pivot = pivot; Changed(); }

        if (Selected >= 0 && Selected < asset.FrameCount)
        {
            var frame = asset.Frames[Selected];
            ImGui.SeparatorText($"Frame {Selected + 1}");
            ImGui.TextWrapped(frame.Image);
            if (ProjectPanel.Dragged is { Kind: AssetKind.Texture } dragged && ImGui.BeginDragDropTarget())
            {
                unsafe
                {
                    if (ImGui.AcceptDragDropPayload(ProjectPanel.Payload).NativePtr != null) { frame.Image = dragged.Path; Changed(); }
                }
                ImGui.EndDragDropTarget();
            }
            var rect = new[] { frame.X, frame.Y, frame.Width, frame.Height };
            if (ImGui.DragInt4("Rect (x y w h)", ref rect[0], 0.5f, 0, 16384)) { frame.X = rect[0]; frame.Y = rect[1]; frame.Width = rect[2]; frame.Height = rect[3]; Changed(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Width or height 0 = whole image");
            float duration = frame.Duration * 1000f;
            if (ImGui.DragFloat("Duration (ms)", ref duration, 1f, 0f, 10000f, duration <= 0 ? "default" : "%.0f")) { frame.Duration = Math.Max(0, duration / 1000f); Changed(); }
            var ev = frame.Event;
            if (ImGui.InputText("Event", ref ev, 64)) { frame.Event = ev.Trim(); Changed(); }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Sent to OnSpriteEvent (Behaviour) / OnFrameEvent (SpriteImage) when this frame starts");
            if (ImGui.Button("Use as sheet")) SheetImage = frame.Image;
        }

        ImGui.SeparatorText("Slice sheet");
        ImGui.TextWrapped(string.IsNullOrEmpty(SheetImage) ? "(drop an image here)" : SheetImage);
        if (ProjectPanel.Dragged is { Kind: AssetKind.Texture } sheetDragged && ImGui.BeginDragDropTarget())
        {
            unsafe
            {
                if (ImGui.AcceptDragDropPayload(ProjectPanel.Payload).NativePtr != null) SheetImage = sheetDragged.Path;
            }
            ImGui.EndDragDropTarget();
        }
        var (w, h) = ImageSizeOf(SheetImage);
        if (w > 0) ImGui.TextDisabled($"{w} x {h} px");
        ImGui.DragInt("Columns", ref SheetColumns, 0.1f, 1, 256); SheetColumns = Math.Max(1, SheetColumns);
        ImGui.DragInt("Rows", ref SheetRows, 0.1f, 1, 256); SheetRows = Math.Max(1, SheetRows);
        ImGui.DragInt("Count (0 = all)", ref SheetCount, 0.1f, 0, 65536);
        var margin = new[] { SheetMarginX, SheetMarginY };
        if (ImGui.DragInt2("Margin", ref margin[0], 0.1f, 0, 4096)) { SheetMarginX = margin[0]; SheetMarginY = margin[1]; }
        var spacing = new[] { SheetSpacingX, SheetSpacingY };
        if (ImGui.DragInt2("Spacing", ref spacing[0], 0.1f, 0, 4096)) { SheetSpacingX = spacing[0]; SheetSpacingY = spacing[1]; }
        // シートと切る線
        var sheetTexture = _engine.Assets.LoadSpriteTexture(SheetImage);
        if (sheetTexture.IsValid && w > 0)
        {
            float width = ImGui.GetContentRegionAvail().X;
            float s = Math.Min(width / w, 240f / h);
            var origin = ImGui.GetCursorScreenPos();
            ImGui.Image((IntPtr)sheetTexture.Texture, new Vector2(w, h) * s);
            var draw = ImGui.GetWindowDrawList();
            foreach (var f in SpriteAnimation.SliceGrid(SheetImage, w, h, SheetColumns, SheetRows, SheetCount, SheetMarginX, SheetMarginY, SheetSpacingX, SheetSpacingY))
                draw.AddRect(origin + new Vector2(f.X, f.Y) * s, origin + new Vector2(f.X + f.Width, f.Y + f.Height) * s, 0xFF40C0FFu);
        }
        ImGui.BeginDisabled(w == 0);
        if (ImGui.Button("Replace frames")) SliceSheet(false);
        ImGui.SameLine();
        if (ImGui.Button("Append frames")) SliceSheet(true);
        ImGui.EndDisabled();
    }
}
