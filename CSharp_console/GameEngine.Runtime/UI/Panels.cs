using System.Numerics;

namespace GameEngine;

// ==========================================================================
// 子を並べる Widget (v1.7。UE の UMG の Panel と同じ並べ方)
// ==========================================================================

/// <summary>自由な位置に置く (アンカー / Offsets / Alignment / ZOrder)。UE の UCanvasPanel</summary>
public sealed class UCanvasPanel : UPanelWidget
{
    public override UIPanelSlot CreateSlot() => new();
    public override string[] SlotFields => new[] { nameof(UIPanelSlot.Anchors), nameof(UIPanelSlot.Offsets), nameof(UIPanelSlot.Alignment), nameof(UIPanelSlot.AutoSize), nameof(UIPanelSlot.ZOrder) };

    /// <summary>子を足して位置と大きさを決める (アンカーは左上)</summary>
    public UIPanelSlot? AddChildToCanvas(UWidget child, Vector2 position, Vector2 size)
    {
        var slot = AddChild(child);
        if (slot != null) { slot.Position = position; slot.SizeValue = size; }
        return slot;
    }

    // 中身の欲しい大きさ (アンカーが左上の点の物の右下)。普通は親いっぱいに置かれるので使われない
    protected override Vector2 ComputeDesiredSize(UIContext context)
    {
        var size = Vector2.Zero;
        foreach (var child in Children)
        {
            var s = child.Slot;
            if (s.Anchors.StretchX || s.Anchors.StretchY || child.Visibility == UIVisibility.Collapsed) continue;
            var childSize = s.AutoSize ? child.DesiredSize : s.SizeValue;
            size = Vector2.Max(size, s.Position - s.Alignment * childSize + childSize);
        }
        return size;
    }

    protected override void ArrangeChildren(UIContext context)
    {
        foreach (var child in Children)
        {
            var (position, size) = Layout(child.Slot, child.DesiredSize, Size);
            ArrangeChild(context, child, position, size);
        }
    }

    /// <summary>CanvasPanel の置き方の計算 (UE の SConstraintCanvas と同じ)</summary>
    public static (Vector2 Position, Vector2 Size) Layout(UIPanelSlot slot, Vector2 desired, Vector2 parentSize)
    {
        var a = slot.Anchors;
        var o = slot.Offsets;
        float px, pw, py, ph;
        if (a.StretchX)
        {
            px = a.Minimum.X * parentSize.X + o.Left;
            pw = a.Maximum.X * parentSize.X - o.Right - px;
        }
        else
        {
            pw = slot.AutoSize ? desired.X : o.Right;
            px = a.Minimum.X * parentSize.X + o.Left;
        }
        if (a.StretchY)
        {
            py = a.Minimum.Y * parentSize.Y + o.Top;
            ph = a.Maximum.Y * parentSize.Y - o.Bottom - py;
        }
        else
        {
            ph = slot.AutoSize ? desired.Y : o.Bottom;
            py = a.Minimum.Y * parentSize.Y + o.Top;
        }
        var size = Vector2.Max(Vector2.Zero, new Vector2(pw, ph));
        return (new Vector2(px, py) - slot.Alignment * size, size);
    }

    // ZOrder の小さい物から描く (同じなら並びの順)
    protected override IEnumerable<UWidget> PaintOrder =>
        Children.Select((c, i) => (c, i)).OrderBy(p => p.c.Slot.ZOrder).ThenBy(p => p.i).Select(p => p.c);
}

/// <summary>子を全部同じ四角に重ねる (後の子が手前)。UE の UOverlay</summary>
public sealed class UOverlay : UPanelWidget
{
    public override UIPanelSlot CreateSlot() => new() { HorizontalAlignment = UIHorizontalAlignment.Left, VerticalAlignment = UIVerticalAlignment.Top };

    protected override Vector2 ComputeDesiredSize(UIContext context) => ContentDesiredSize();

    protected override void ArrangeChildren(UIContext context)
    {
        foreach (var child in Children)
        {
            var (position, size) = AlignInside(child, Vector2.Zero, Size);
            ArrangeChild(context, child, position, size);
        }
    }
}

/// <summary>横 / 縦に並べる (Auto = 中身の大きさ / Fill = 余りを分ける)。UE の UHorizontalBox / UVerticalBox</summary>
public abstract class UBoxPanel : UPanelWidget
{
    protected abstract bool Horizontal { get; }

    public override UIPanelSlot CreateSlot() => new();
    public override string[] SlotFields => new[] { nameof(UIPanelSlot.Padding), nameof(UIPanelSlot.Size), nameof(UIPanelSlot.FillWeight), nameof(UIPanelSlot.HorizontalAlignment), nameof(UIPanelSlot.VerticalAlignment) };

    protected override Vector2 ComputeDesiredSize(UIContext context)
    {
        float main = 0, cross = 0;
        foreach (var child in Children)
        {
            if (child.Visibility == UIVisibility.Collapsed) continue;
            var size = child.DesiredSize + child.Slot.Padding.Total;
            main += Horizontal ? size.X : size.Y;
            cross = Math.Max(cross, Horizontal ? size.Y : size.X);
        }
        return Horizontal ? new Vector2(main, cross) : new Vector2(cross, main);
    }

    protected override void ArrangeChildren(UIContext context)
    {
        var total = Horizontal ? Size.X : Size.Y;
        float autoSize = 0, weights = 0;
        foreach (var child in Children)
        {
            if (child.Visibility == UIVisibility.Collapsed) continue;
            var padding = Horizontal ? child.Slot.Padding.Horizontal : child.Slot.Padding.Vertical;
            if (child.Slot.Size == UISizeRule.Fill) { weights += Math.Max(0, child.Slot.FillWeight); autoSize += padding; }
            else autoSize += padding + (Horizontal ? child.DesiredSize.X : child.DesiredSize.Y);
        }
        var remaining = Math.Max(0, total - autoSize);
        var cursor = 0.0f;
        foreach (var child in Children)
        {
            if (child.Visibility == UIVisibility.Collapsed) continue;
            var slot = child.Slot;
            var padding = slot.Padding;
            var desired = child.DesiredSize;
            // 主の向き : 割り当て
            var allotted = slot.Size == UISizeRule.Fill ? (weights > 0 ? remaining * Math.Max(0, slot.FillWeight) / weights : 0) : (Horizontal ? desired.X : desired.Y);
            Vector2 position, size;
            if (Horizontal)
            {
                var (x, w) = align1D((int)slot.HorizontalAlignment, cursor + padding.Left, allotted, desired.X);
                var (y, h) = align1D((int)slot.VerticalAlignment, padding.Top, Size.Y - padding.Vertical, desired.Y);
                position = new Vector2(x, y); size = new Vector2(w, h);
                cursor += padding.Horizontal + allotted;
            }
            else
            {
                var (x, w) = align1D((int)slot.HorizontalAlignment, padding.Left, Size.X - padding.Horizontal, desired.X);
                var (y, h) = align1D((int)slot.VerticalAlignment, cursor + padding.Top, allotted, desired.Y);
                position = new Vector2(x, y); size = new Vector2(w, h);
                cursor += padding.Vertical + allotted;
            }
            ArrangeChild(context, child, position, size);
        }
    }

    // 1 つの向きの揃え (0 = Fill / 1 = 始め / 2 = 真ん中 / 3 = 終わり)
    internal static (float Position, float Size) align1D(int alignment, float start, float space, float desired)
    {
        space = Math.Max(0, space);
        if (alignment == 0) return (start, space);
        var size = Math.Min(desired, space);
        return alignment switch
        {
            1 => (start, size),
            2 => (start + (space - size) * 0.5f, size),
            _ => (start + space - size, size),
        };
    }
}

public sealed class UHorizontalBox : UBoxPanel { protected override bool Horizontal => true; }
public sealed class UVerticalBox : UBoxPanel { protected override bool Horizontal => false; }

/// <summary>同じ大きさのマスに並べる (Row / Column)。UE の UUniformGridPanel</summary>
public sealed class UUniformGridPanel : UPanelWidget
{
    public UIMargin SlotPadding;
    public float MinDesiredSlotWidth;
    public float MinDesiredSlotHeight;

    public override UIPanelSlot CreateSlot() => new();
    public override string[] SlotFields => new[] { nameof(UIPanelSlot.Row), nameof(UIPanelSlot.Column), nameof(UIPanelSlot.HorizontalAlignment), nameof(UIPanelSlot.VerticalAlignment) };

    /// <summary>子を足してマスを決める</summary>
    public UIPanelSlot? AddChildToUniformGrid(UWidget child, int row, int column)
    {
        var slot = AddChild(child);
        if (slot != null) { slot.Row = row; slot.Column = column; }
        return slot;
    }

    private (int Rows, int Columns, Vector2 Cell) measure()
    {
        int rows = 0, columns = 0;
        var cell = new Vector2(MinDesiredSlotWidth, MinDesiredSlotHeight);
        foreach (var child in Children)
        {
            if (child.Visibility == UIVisibility.Collapsed) continue;
            rows = Math.Max(rows, child.Slot.Row + 1);
            columns = Math.Max(columns, child.Slot.Column + 1);
            cell = Vector2.Max(cell, child.DesiredSize + SlotPadding.Total);
        }
        return (rows, columns, cell);
    }

    protected override Vector2 ComputeDesiredSize(UIContext context)
    {
        var (rows, columns, cell) = measure();
        return new Vector2(columns * cell.X, rows * cell.Y);
    }

    protected override void ArrangeChildren(UIContext context)
    {
        var (rows, columns, _) = measure();
        if (rows == 0 || columns == 0) return;
        var cell = new Vector2(Size.X / columns, Size.Y / rows);
        foreach (var child in Children)
        {
            var slot = child.Slot;
            var origin = new Vector2(Math.Max(0, slot.Column) * cell.X, Math.Max(0, slot.Row) * cell.Y) + SlotPadding.TopLeft;
            var inner = cell - SlotPadding.Total;
            var (x, w) = UBoxPanel.align1D((int)slot.HorizontalAlignment, origin.X, inner.X, child.DesiredSize.X);
            var (y, h) = UBoxPanel.align1D((int)slot.VerticalAlignment, origin.Y, inner.Y, child.DesiredSize.Y);
            ArrangeChild(context, child, new Vector2(x, y), new Vector2(w, h));
        }
    }
}

/// <summary>中身を 1 つ持つ物の元 (UE の UContentWidget)</summary>
public abstract class UContentWidget : UPanelWidget
{
    public override int MaxChildren => 1;
    public UWidget? Content => GetChildAt(0);

    /// <summary>中身を差し替える</summary>
    public UIPanelSlot? SetContent(UWidget? content)
    {
        ClearChildren();
        return content != null ? AddChild(content) : null;
    }

    protected override Vector2 ComputeDesiredSize(UIContext context) => ContentDesiredSize();

    protected override void ArrangeChildren(UIContext context)
    {
        if (Content is not { } child) return;
        var (position, size) = AlignInside(child, Vector2.Zero, Size);
        ArrangeChild(context, child, position, size);
    }
}

/// <summary>背景 (ブラシ) と中身 1 つ。UE の UBorder</summary>
public sealed class UBorder : UContentWidget
{
    public UIBrush Background = new();
    public Vector4 BrushColor = Vector4.One;
    public Vector2 DesiredSizeScale = Vector2.One;

    public override UIPanelSlot CreateSlot() => new() { Padding = new UIMargin(4, 2) };
    protected internal override bool BlocksPointer => Background.DrawAs != UIBrushDrawType.None && (Background.Tint * BrushColor).W > 0.0f;

    protected override Vector2 ComputeDesiredSize(UIContext context) => ContentDesiredSize() * DesiredSizeScale;

    protected override void OnPaint(UIPainter painter) => painter.Brush(Background, Vector2.Zero, Size, BrushColor);
}

/// <summary>大きさを決める / 制限する。UE の USizeBox (0 = 決めない)</summary>
public sealed class USizeBox : UContentWidget
{
    public float WidthOverride;
    public float HeightOverride;
    public float MinDesiredWidth;
    public float MinDesiredHeight;
    public float MaxDesiredWidth;
    public float MaxDesiredHeight;

    protected override Vector2 ComputeDesiredSize(UIContext context)
    {
        var size = ContentDesiredSize();
        if (MinDesiredWidth > 0) size.X = Math.Max(size.X, MinDesiredWidth);
        if (MinDesiredHeight > 0) size.Y = Math.Max(size.Y, MinDesiredHeight);
        if (MaxDesiredWidth > 0) size.X = Math.Min(size.X, MaxDesiredWidth);
        if (MaxDesiredHeight > 0) size.Y = Math.Min(size.Y, MaxDesiredHeight);
        if (WidthOverride > 0) size.X = WidthOverride;
        if (HeightOverride > 0) size.Y = HeightOverride;
        return size;
    }
}

/// <summary>中身を拡大 / 縮小して入れる。UE の UScaleBox</summary>
public sealed class UScaleBox : UContentWidget
{
    public UIStretch Stretch = UIStretch.ScaleToFit;
    public float UserSpecifiedScale = 1.0f;

    public override UIPanelSlot CreateSlot() => new() { HorizontalAlignment = UIHorizontalAlignment.Center, VerticalAlignment = UIVerticalAlignment.Center };

    /// <summary>今の拡大率 (並べた後)</summary>
    public float CurrentScale { get; private set; } = 1.0f;

    protected override Vector2 ComputeDesiredSize(UIContext context) =>
        Stretch == UIStretch.UserSpecified ? ContentDesiredSize() * UserSpecifiedScale : ContentDesiredSize();

    protected override void ArrangeChildren(UIContext context)
    {
        if (Content is not { } child) return;
        var desired = Vector2.Max(child.DesiredSize, new Vector2(1e-3f));
        var fit = Size / desired;
        CurrentScale = Stretch switch
        {
            UIStretch.ScaleToFit    => Math.Min(fit.X, fit.Y),
            UIStretch.ScaleToFill   => Math.Max(fit.X, fit.Y),
            UIStretch.ScaleToFitX   => fit.X,
            UIStretch.ScaleToFitY   => fit.Y,
            UIStretch.UserSpecified => UserSpecifiedScale,
            _                       => 1.0f,
        };
        if (Stretch == UIStretch.Fill)
        {
            // 縦横を別に伸ばす : 中身は自分の大きさで並べる
            ArrangeChild(context, child, Vector2.Zero, Size);
            return;
        }
        var scaled = desired * CurrentScale;
        var (x, _) = UBoxPanel.align1D(Math.Max(1, (int)child.Slot.HorizontalAlignment), 0, Size.X, scaled.X);
        var (y, _) = UBoxPanel.align1D(Math.Max(1, (int)child.Slot.VerticalAlignment), 0, Size.Y, scaled.Y);
        // 揃えの計算は「入りきらない時は切る」ので、はみ出す時 (ScaleToFill) は真ん中に置き直す
        if (scaled.X > Size.X) x = (Size.X - scaled.X) * 0.5f;
        if (scaled.Y > Size.Y) y = (Size.Y - scaled.Y) * 0.5f;
        ArrangeChild(context, child, new Vector2(x, y), desired, CurrentScale);
    }
}

/// <summary>子の 1 つだけを出す (タブ / ページの切り替え)。UE の UWidgetSwitcher</summary>
public sealed class UWidgetSwitcher : UPanelWidget
{
    public int ActiveWidgetIndex;

    public override string[] SlotFields => new[] { nameof(UIPanelSlot.Padding), nameof(UIPanelSlot.HorizontalAlignment), nameof(UIPanelSlot.VerticalAlignment) };

    public UWidget? ActiveWidget => GetChildAt(Math.Clamp(ActiveWidgetIndex, 0, Math.Max(0, ChildrenCount - 1)));

    public void SetActiveWidgetIndex(int index) => ActiveWidgetIndex = index;

    public void SetActiveWidget(UWidget widget)
    {
        var index = GetChildIndex(widget);
        if (index >= 0) ActiveWidgetIndex = index;
    }

    protected override Vector2 ComputeDesiredSize(UIContext context) => ContentDesiredSize();

    protected override void ArrangeChildren(UIContext context)
    {
        if (ActiveWidget is not { } child) return;
        var (position, size) = AlignInside(child, Vector2.Zero, Size);
        ArrangeChild(context, child, position, size);
    }

    protected override IEnumerable<UWidget> PaintOrder => ActiveWidget is { } active ? new[] { active } : Array.Empty<UWidget>();
    internal override IEnumerable<UWidget> HitOrder => PaintOrder;
}

/// <summary>中身を縦 (横) に並べて、ホイール / つまみのドラッグで動かす。UE の UScrollBox</summary>
public sealed class UScrollBox : UPanelWidget
{
    public UIOrientation Orientation = UIOrientation.Vertical;
    public float ScrollOffset;
    public bool  AlwaysShowScrollbar;
    public float ScrollbarThickness = 8.0f;
    public float WheelScrollMultiplier = 60.0f;
    public Vector4 ScrollbarColor = new(1, 1, 1, 0.35f);
    public Vector4 ThumbColor = new(1, 1, 1, 0.8f);

    /// <summary>ユーザーが動かした時 (新しい ScrollOffset)</summary>
    public event Action<float>? OnUserScrolled;

    private float _contentLength;
    private bool _draggingThumb;
    private float _dragStart, _dragOffset;

    public override string[] SlotFields => new[] { nameof(UIPanelSlot.Padding), nameof(UIPanelSlot.HorizontalAlignment), nameof(UIPanelSlot.VerticalAlignment) };
    protected override bool ClipsChildren => true;
    protected internal override bool HandlesPointer => ScrollRange > 0;   // つまみのドラッグ
    protected internal override bool BlocksPointer => true;

    private bool vertical => Orientation == UIOrientation.Vertical;
    private float viewLength => vertical ? Size.Y : Size.X;
    /// <summary>動かせる量 (中身 - 見えている大きさ)</summary>
    public float ScrollRange => Math.Max(0, _contentLength - viewLength);

    public void ScrollToStart() => ScrollOffset = 0;
    public void ScrollToEnd() => ScrollOffset = ScrollRange;

    protected override Vector2 ComputeDesiredSize(UIContext context)
    {
        float main = 0, cross = 0;
        foreach (var child in Children)
        {
            if (child.Visibility == UIVisibility.Collapsed) continue;
            var size = child.DesiredSize + child.Slot.Padding.Total;
            main += vertical ? size.Y : size.X;
            cross = Math.Max(cross, vertical ? size.X : size.Y);
        }
        _contentLength = main;
        return vertical ? new Vector2(cross + ScrollbarThickness, main) : new Vector2(main, cross + ScrollbarThickness);
    }

    protected override void ArrangeChildren(UIContext context)
    {
        ScrollOffset = Math.Clamp(ScrollOffset, 0, ScrollRange);
        var cursor = -ScrollOffset;
        var crossSpace = (vertical ? Size.X : Size.Y) - ScrollbarThickness;
        foreach (var child in Children)
        {
            if (child.Visibility == UIVisibility.Collapsed) continue;
            var slot = child.Slot;
            var p = slot.Padding;
            if (vertical)
            {
                var (x, w) = UBoxPanel.align1D((int)slot.HorizontalAlignment, p.Left, crossSpace - p.Horizontal, child.DesiredSize.X);
                ArrangeChild(context, child, new Vector2(x, cursor + p.Top), new Vector2(w, child.DesiredSize.Y));
                cursor += child.DesiredSize.Y + p.Vertical;
            }
            else
            {
                var (y, h) = UBoxPanel.align1D((int)slot.VerticalAlignment, p.Top, crossSpace - p.Vertical, child.DesiredSize.Y);
                ArrangeChild(context, child, new Vector2(cursor + p.Left, y), new Vector2(child.DesiredSize.X, h));
                cursor += child.DesiredSize.X + p.Horizontal;
            }
        }
    }

    // つまみの位置と長さ (主の向き)
    private (float Start, float Length) thumb()
    {
        var view = viewLength;
        var length = _contentLength > 0 ? Math.Max(16.0f, view * Math.Min(1, view / _contentLength)) : view;
        var start = ScrollRange > 0 ? (view - length) * ScrollOffset / ScrollRange : 0;
        return (start, length);
    }

    protected override bool HasOverlay => true;

    // 中身の後 (手前) につまみを描く
    protected override void OnPaintOverlay(UIPainter painter)
    {
        if (ScrollRange <= 0 && !AlwaysShowScrollbar) return;
        var (start, length) = thumb();
        var t = ScrollbarThickness;
        if (vertical)
        {
            painter.Brush(UIBrush.Rounded(ScrollbarColor, t * 0.5f), new Vector2(Size.X - t, 0), Size, Vector4.One);
            painter.Brush(UIBrush.Rounded(ThumbColor, t * 0.5f), new Vector2(Size.X - t, start), new Vector2(Size.X, start + length), Vector4.One);
        }
        else
        {
            painter.Brush(UIBrush.Rounded(ScrollbarColor, t * 0.5f), new Vector2(0, Size.Y - t), Size, Vector4.One);
            painter.Brush(UIBrush.Rounded(ThumbColor, t * 0.5f), new Vector2(start, Size.Y - t), new Vector2(start + length, Size.Y), Vector4.One);
        }
    }

    protected internal override bool OnMouseWheel(float delta)
    {
        if (ScrollRange <= 0) return false;
        var before = ScrollOffset;
        ScrollOffset = Math.Clamp(ScrollOffset - delta * WheelScrollMultiplier, 0, ScrollRange);
        if (ScrollOffset != before) OnUserScrolled?.Invoke(ScrollOffset);
        return true;
    }

    protected internal override void OnMouseDown(Vector2 local)
    {
        var along = vertical ? local.Y : local.X;
        var across = vertical ? local.X : local.Y;
        var crossSize = vertical ? Size.X : Size.Y;
        _draggingThumb = across >= crossSize - ScrollbarThickness * 1.5f;   // つまみの列を押した時だけ
        if (!_draggingThumb) return;
        var (start, length) = thumb();
        if (along < start || along > start + length) ScrollOffset = Math.Clamp((along - length * 0.5f) / Math.Max(1, viewLength - length) * ScrollRange, 0, ScrollRange);
        _dragStart = along;
        _dragOffset = ScrollOffset;
    }

    protected internal override void OnMouseDrag(Vector2 local)
    {
        if (!_draggingThumb) return;
        var along = vertical ? local.Y : local.X;
        var (_, length) = thumb();
        var before = ScrollOffset;
        ScrollOffset = Math.Clamp(_dragOffset + (along - _dragStart) / Math.Max(1, viewLength - length) * ScrollRange, 0, ScrollRange);
        if (ScrollOffset != before) OnUserScrolled?.Invoke(ScrollOffset);
    }

    protected internal override void OnMouseUp(Vector2 local, bool inside) => _draggingThumb = false;
}
