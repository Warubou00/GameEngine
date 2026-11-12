using System.Numerics;

namespace GameEngine;

// ==========================================================================
// Widget の元 (v1.7。UE の UWidget)
//   名前は UE の C++ と同じく U を付ける (UButton / UImage ...。WinForms の Button などとぶつからないように)
//   アセット (.widget.json) に保存されるのは public なフィールド ([NonSerialized] は除く)。プロパティは保存しない
//
//   並べ方 (UE の Slate と同じ 2 回) :
//     1. Prepass   : 子から順に「欲しい大きさ」(DesiredSize) を求める
//     2. Arrange   : 親が子の位置と大きさを決める (Transform = 自分の座標 → 画面のピクセル)
//   描く : Paint (親 → 子の順。後に描いた物が手前)
//   マウス : 手前から当たりを調べる (HitTest)。Button などは Handles... で受け取る
// ==========================================================================
public abstract class UWidget
{
    public string Name = "";
    public UIVisibility Visibility = UIVisibility.Visible;
    public bool IsEnabled = true;
    [Range(0, 1)] public float RenderOpacity = 1.0f;
    public UIRenderTransform RenderTransform = new();
    public UIClipping Clipping;

    /// <summary>親の Panel (一番上の物は null)</summary>
    public UPanelWidget? Parent { get; internal set; }
    /// <summary>親の中での置き方 (親の種類で使う項目が違う)</summary>
    public UIPanelSlot Slot { get; internal set; } = new();
    /// <summary>この Widget を持っている UserWidget (アセットから作った時)</summary>
    public UUserWidget? Owner { get; internal set; }

    /// <summary>種類の名前 (アセットに書く名前。U を除いた物)</summary>
    public string TypeName => WidgetTypes.NameOf(GetType());

    // ---- 並べた結果 ----
    public Vector2   DesiredSize { get; private set; }
    /// <summary>並べた大きさ (自分の座標 = デザインの単位)</summary>
    public Vector2   Size { get; private set; }
    /// <summary>自分の座標 → 画面のピクセル</summary>
    public Matrix3x2 Transform { get; private set; } = Matrix3x2.Identity;
    /// <summary>切り取りの四角 (画面のピクセル。左 / 上 / 右 / 下)</summary>
    public Vector4   ClipRect { get; private set; }

    // ---- マウス / キー ----
    public bool IsHovered { get; internal set; }
    public bool HasKeyboardFocus { get; internal set; }

    public virtual IReadOnlyList<UWidget> Children => Array.Empty<UWidget>();

    /// <summary>見えるか (Collapsed / Hidden でない)</summary>
    public bool IsVisible => Visibility is not (UIVisibility.Collapsed or UIVisibility.Hidden);

    /// <summary>親も含めて有効か</summary>
    public bool IsEnabledInHierarchy => IsEnabled && (Parent?.IsEnabledInHierarchy ?? true);

    public void SetVisibility(UIVisibility visibility) => Visibility = visibility;

    /// <summary>親から外す (UE の RemoveFromParent)</summary>
    public void RemoveFromParent() => Parent?.RemoveChild(this);

    // ---------------------------------------------------------------------
    // 並べる
    // ---------------------------------------------------------------------

    internal void Prepass(UIContext context)
    {
        if (Visibility == UIVisibility.Collapsed) { DesiredSize = Vector2.Zero; return; }
        foreach (var child in Children) child.Prepass(context);
        DesiredSize = Vector2.Max(Vector2.Zero, ComputeDesiredSize(context));
    }

    /// <summary>欲しい大きさ (子の DesiredSize はもう決まっている)</summary>
    protected abstract Vector2 ComputeDesiredSize(UIContext context);

    internal void Arrange(UIContext context, in Matrix3x2 layoutTransform, Vector2 size, Vector4 parentClip)
    {
        Size = Vector2.Max(Vector2.Zero, size);
        var transform = layoutTransform;
        if (!RenderTransform.IsIdentity)
        {
            var rt = RenderTransform;
            var pivot = rt.Pivot * Size;
            transform = Matrix3x2.CreateTranslation(-pivot) * Matrix3x2.CreateScale(rt.Scale) * Matrix3x2.CreateRotation(rt.Angle * MathF.PI / 180.0f) *
                        Matrix3x2.CreateTranslation(pivot + rt.Translation) * layoutTransform;
        }
        Transform = transform;
        ClipRect = Clipping == UIClipping.ClipToBounds || ClipsChildren ? Intersect(parentClip, Bounds) : parentClip;
        ArrangeChildren(context);
    }

    /// <summary>子を並べる (ArrangeChild を呼ぶ)</summary>
    protected virtual void ArrangeChildren(UIContext context) { }

    /// <summary>子を自分の座標の position に size の大きさで置く (scale = 子の拡大。ScaleBox)</summary>
    protected void ArrangeChild(UIContext context, UWidget child, Vector2 position, Vector2 size, float scale = 1.0f)
    {
        if (child.Visibility == UIVisibility.Collapsed) return;
        var layout = scale == 1.0f ? Matrix3x2.CreateTranslation(position) * Transform
                                   : Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(position) * Transform;
        child.Arrange(context, layout, size, ClipRect);
    }

    /// <summary>いつも子を切り取る (ScrollBox)</summary>
    protected virtual bool ClipsChildren => false;

    /// <summary>画面での外側の四角 (回っている時は囲む四角)</summary>
    public Vector4 Bounds
    {
        get
        {
            var a = Vector2.Transform(Vector2.Zero, Transform);
            var b = Vector2.Transform(new Vector2(Size.X, 0), Transform);
            var c = Vector2.Transform(Size, Transform);
            var d = Vector2.Transform(new Vector2(0, Size.Y), Transform);
            var min = Vector2.Min(Vector2.Min(a, b), Vector2.Min(c, d));
            var max = Vector2.Max(Vector2.Max(a, b), Vector2.Max(c, d));
            return new Vector4(min.X, min.Y, max.X, max.Y);
        }
    }

    internal static Vector4 Intersect(Vector4 a, Vector4 b) =>
        new(Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), Math.Min(a.Z, b.Z), Math.Min(a.W, b.W));

    /// <summary>画面のピクセル → 自分の座標</summary>
    public Vector2 ToLocal(Vector2 screen) => Matrix3x2.Invert(Transform, out var inverse) ? Vector2.Transform(screen, inverse) : new Vector2(float.NaN);

    /// <summary>画面のピクセルが自分の四角 (と切り取りの中) に入っているか</summary>
    public bool Contains(Vector2 screen)
    {
        if (screen.X < ClipRect.X || screen.Y < ClipRect.Y || screen.X >= ClipRect.Z || screen.Y >= ClipRect.W) return false;
        var local = ToLocal(screen);
        return local.X >= 0 && local.Y >= 0 && local.X < Size.X && local.Y < Size.Y;
    }

    // ---------------------------------------------------------------------
    // 描く
    // ---------------------------------------------------------------------

    internal void Paint(UIPainter painter, float parentOpacity, bool parentEnabled)
    {
        if (!IsVisible) return;
        if (ClipRect.Z <= ClipRect.X || ClipRect.W <= ClipRect.Y) return;   // 切り取られて見えない
        var opacity = parentOpacity * RenderOpacity;
        if (opacity <= 0.0f) return;
        var enabled = parentEnabled && IsEnabled;
        painter.Transform = Transform;
        painter.Clip = ClipRect;
        painter.Opacity = opacity * (enabled ? 1.0f : 0.5f);   // 無効な物は薄く
        painter.PixelScale = MathF.Sqrt(MathF.Abs(Transform.GetDeterminant()));
        OnPaint(painter);
        var children = Children.Count > 0;
        if (children) foreach (var child in PaintOrder) child.Paint(painter, opacity, enabled);
        if (HasOverlay)
        {
            painter.Transform = Transform;
            painter.Clip = ClipRect;
            painter.Opacity = opacity * (enabled ? 1.0f : 0.5f);
            painter.PixelScale = MathF.Sqrt(MathF.Abs(Transform.GetDeterminant()));
            OnPaintOverlay(painter);
        }
    }

    /// <summary>子の後 (手前) に描く物があるか (ScrollBox のつまみ)</summary>
    protected virtual bool HasOverlay => false;
    /// <summary>子の後 (手前) に描く</summary>
    protected virtual void OnPaintOverlay(UIPainter painter) { }

    /// <summary>当たりを調べる順 (手前の物から)</summary>
    internal virtual IEnumerable<UWidget> HitOrder => PaintOrder.Reverse();

    /// <summary>子を手前から順に (マウスの当たりを調べる順。エディターが使う)</summary>
    public IEnumerable<UWidget> ChildrenFrontToBack => HitOrder;

    /// <summary>自分を描く (子は後で描かれる)</summary>
    protected virtual void OnPaint(UIPainter painter) { }

    /// <summary>描く順 (後の物が手前)</summary>
    protected virtual IEnumerable<UWidget> PaintOrder => Children;

    // ---------------------------------------------------------------------
    // マウス / キー
    // ---------------------------------------------------------------------

    /// <summary>マウスのボタンを受け取る (Button / CheckBox / Slider / 文字の欄)</summary>
    protected internal virtual bool HandlesPointer => false;
    /// <summary>当たるとマウスが UI の上にあることになる (後ろのゲームへ通さない)</summary>
    protected internal virtual bool BlocksPointer => HandlesPointer;
    /// <summary>キーを受け取る (文字の欄)</summary>
    protected internal virtual bool IsFocusable => false;

    protected internal virtual void OnMouseEnter() { }
    protected internal virtual void OnMouseLeave() { }
    protected internal virtual void OnMouseDown(Vector2 local) { }
    /// <summary>inside = 離した時にマウスが自分の上にあったか</summary>
    protected internal virtual void OnMouseUp(Vector2 local, bool inside) { }
    /// <summary>押している間 (自分が掴んでいる間) のマウスの動き</summary>
    protected internal virtual void OnMouseDrag(Vector2 local) { }
    /// <summary>ホイール。戻り値 = 使ったか (使わなければ親へ)</summary>
    protected internal virtual bool OnMouseWheel(float delta) => false;
    protected internal virtual void OnKeyDown(Keys key) { }
    protected internal virtual void OnCharacter(char c) { }
    protected internal virtual void OnFocusLost() { }
    /// <summary>毎フレーム (再生中。文字の欄のカーソルの点滅など)</summary>
    protected internal virtual void Tick(float deltaTime) { }

    // ---------------------------------------------------------------------
    // 探す
    // ---------------------------------------------------------------------

    /// <summary>自分と子孫から名前で探す</summary>
    public UWidget? FindWidget(string name)
    {
        if (Name == name) return this;
        foreach (var child in Children) if (child.FindWidget(name) is { } found) return found;
        return null;
    }

    public T? FindWidget<T>(string name) where T : UWidget => FindWidget(name) as T;

    /// <summary>自分と子孫を全部 (親 → 子の順)</summary>
    public IEnumerable<UWidget> Descendants()
    {
        yield return this;
        foreach (var child in Children)
            foreach (var d in child.Descendants()) yield return d;
    }

    public override string ToString() => $"{TypeName} '{Name}'";
}

// ==========================================================================
// 子を持つ物 (UE の UPanelWidget)。中身を 1 つだけ持つ物 (Border / Button ...) は MaxChildren = 1
// ==========================================================================
public abstract class UPanelWidget : UWidget
{
    private readonly List<UWidget> _children = new();
    public override IReadOnlyList<UWidget> Children => _children;

    /// <summary>持てる子の数 (int.MaxValue = いくつでも)</summary>
    public virtual int MaxChildren => int.MaxValue;
    public bool CanAddMoreChildren => _children.Count < MaxChildren;

    /// <summary>子の置き方の既定 (Panel の種類ごと)</summary>
    public virtual UIPanelSlot CreateSlot() => new();

    /// <summary>エディターに出す置き方の項目 (Panel の種類ごと)</summary>
    public virtual string[] SlotFields => new[] { nameof(UIPanelSlot.Padding), nameof(UIPanelSlot.HorizontalAlignment), nameof(UIPanelSlot.VerticalAlignment) };

    public int ChildrenCount => _children.Count;
    public UWidget? GetChildAt(int index) => index >= 0 && index < _children.Count ? _children[index] : null;
    public int GetChildIndex(UWidget child) => _children.IndexOf(child);

    /// <summary>子を足す (戻り値 = 置き方。持てる数を超えると null)</summary>
    public UIPanelSlot? AddChild(UWidget child) => InsertChildAt(_children.Count, child);

    public UIPanelSlot? InsertChildAt(int index, UWidget child, UIPanelSlot? slot = null)
    {
        if (child == this || IsDescendantOf(child)) return null;
        if (child.Parent == this) { _children.Remove(child); index = Math.Min(index, _children.Count); }
        else
        {
            if (!CanAddMoreChildren) return null;
            child.Parent?.RemoveChild(child);
        }
        _children.Insert(Math.Clamp(index, 0, _children.Count), child);
        if (child.Parent != this || slot != null) child.Slot = slot ?? CreateSlot();
        child.Parent = this;
        child.Owner ??= Owner;
        return child.Slot;
    }

    public bool RemoveChild(UWidget child)
    {
        if (!_children.Remove(child)) return false;
        child.Parent = null;
        return true;
    }

    public void RemoveChildAt(int index) { if (GetChildAt(index) is { } child) RemoveChild(child); }

    public void ClearChildren()
    {
        foreach (var child in _children) child.Parent = null;
        _children.Clear();
    }

    private bool IsDescendantOf(UWidget widget)
    {
        for (var p = Parent; p != null; p = p.Parent) if (p == widget) return true;
        return false;
    }

    // ---- 子を 1 つの四角の中に置く (Padding と揃え。中身を 1 つ持つ物 / Overlay が使う) ----
    protected static (Vector2 Position, Vector2 Size) AlignInside(UWidget child, Vector2 areaPosition, Vector2 areaSize)
    {
        var slot = child.Slot;
        var inner = areaSize - slot.Padding.Total;
        var position = areaPosition + slot.Padding.TopLeft;
        var desired = child.DesiredSize;
        float x = 0, w = inner.X, y = 0, h = inner.Y;
        switch (slot.HorizontalAlignment)
        {
        case UIHorizontalAlignment.Left:   w = Math.Min(desired.X, inner.X); break;
        case UIHorizontalAlignment.Center: w = Math.Min(desired.X, inner.X); x = (inner.X - w) * 0.5f; break;
        case UIHorizontalAlignment.Right:  w = Math.Min(desired.X, inner.X); x = inner.X - w; break;
        }
        switch (slot.VerticalAlignment)
        {
        case UIVerticalAlignment.Top:    h = Math.Min(desired.Y, inner.Y); break;
        case UIVerticalAlignment.Center: h = Math.Min(desired.Y, inner.Y); y = (inner.Y - h) * 0.5f; break;
        case UIVerticalAlignment.Bottom: h = Math.Min(desired.Y, inner.Y); y = inner.Y - h; break;
        }
        return (position + new Vector2(x, y), Vector2.Max(Vector2.Zero, new Vector2(w, h)));
    }

    /// <summary>中身 1 つの欲しい大きさ (子 + Padding)</summary>
    protected Vector2 ContentDesiredSize()
    {
        var size = Vector2.Zero;
        foreach (var child in Children)
            if (child.Visibility != UIVisibility.Collapsed) size = Vector2.Max(size, child.DesiredSize + child.Slot.Padding.Total);
        return size;
    }
}

// ==========================================================================
// 並べる時の情報 (文字の大きさを測る / 時間)
// ==========================================================================
public sealed class UIContext
{
    public UIResources Resources { get; }
    public float DeltaTime { get; internal set; }

    internal UIContext(UIResources resources) => Resources = resources;
}
