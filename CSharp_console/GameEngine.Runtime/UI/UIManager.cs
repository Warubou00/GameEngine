using System.Numerics;

namespace GameEngine;

// ==========================================================================
// UI (v1.7) : ゲームのコードから使う入り口 (UE の CreateWidget / AddToViewport / UWidgetBlueprintLibrary)
//
//   var hud = UI.CreateWidget("Assets/UI/HUD.widget.json");   // アセットから作る (まだ出ない)
//   hud.AddToViewport(10);                                     // 出す (数の大きい物が手前)
//   hud.GetWidget<UTextBlock>("Score")!.SetText("100");        // 名前で部品を探す
//   hud.GetWidget<UButton>("Start")!.OnClicked += () => ...;  // クリック
//   hud.IsVisible = false;                                     // 隠す (on / off)
//   hud.RemoveFromParent();                                    // 外す
//
//   シーンに置く時は UIWidget コンポーネント (Widget のアセット / ZOrder / Visible)。再生すると出て、シーンを替えると消える
//   1 つの Widget アセットを、いくつのシーンからでも使える
// ==========================================================================
public static class UI
{
    /// <summary>今の UI の管理 (Engine が作る)</summary>
    public static UIManager? Manager { get; internal set; }

    private static UIManager manager => Manager ?? throw new InvalidOperationException("UI is not ready (no Engine).");

    /// <summary>Widget のアセットから UserWidget を作る (UE の CreateWidget)。AddToViewport で画面に出る</summary>
    public static UUserWidget CreateWidget(string assetPath) => manager.Assets.LoadWidget(assetPath).CreateInstance();

    public static UUserWidget CreateWidget(WidgetAsset asset) => asset.CreateInstance();

    /// <summary>コードで組んだツリーから作る</summary>
    public static UUserWidget CreateWidget(UWidget root, Vector2 designSize = default) => new(root, designSize);

    /// <summary>エンティティの UIWidget コンポーネントが出している UserWidget (無ければ null)</summary>
    public static UUserWidget? GetWidget(Entity entity) => Manager?.WidgetOf(entity);

    /// <summary>画面に出ている UserWidget (奥 → 手前の順)</summary>
    public static IReadOnlyList<UUserWidget> Widgets => Manager?.Widgets ?? Array.Empty<UUserWidget>();

    /// <summary>マウスが UI の上にあるか (ゲームのクリックを UI に取られた時に、ゲームの方で無視する)</summary>
    public static bool IsPointerOverUI => Manager?.IsPointerOverUI ?? false;

    /// <summary>キーを受け取っている部品 (文字の欄)。null = 無し</summary>
    public static UWidget? FocusedWidget => Manager?.FocusedWidget;

    /// <summary>画面の UserWidget を全部外す</summary>
    public static void RemoveAllWidgets() => Manager?.Clear();
}

// ==========================================================================
// シーンに置く UI (UE の Level Blueprint の CreateWidget + AddToViewport に当たる)
//   再生中、Widget のアセットから UserWidget を作って画面に出す。コンポーネントを外す / エンティティを消すと消える
//   Visible / ZOrder は変えた時に反映される (UserWidget を直接 IsVisible で切り替えてもよい)
//   無効なエンティティの物は出さない
// ==========================================================================
[ComponentInfo(DisplayName = "UI Widget", Category = "UI")]
public struct UIWidget : IComponent
{
    public WidgetAsset? Widget;
    [Tooltip("Larger values are drawn in front")] public int ZOrder;
    public bool Visible;

    // 作った物 (保存しない。UIManager が入れる)
    internal UUserWidget? instance;

    public UIWidget() : this(null) { }

    public UIWidget(WidgetAsset? widget, int zOrder = 0)
    {
        Widget  = widget;
        ZOrder  = zOrder;
        Visible = true;
    }

    /// <summary>出している UserWidget (再生中。まだ作っていなければ null)。コピーでは古いことがあるので UI.GetWidget(entity) が確か</summary>
    public readonly UUserWidget? Instance => instance;
}

// ==========================================================================
// UI の管理 : 画面の UserWidget の一覧 / 並べる / マウスとキー / 描く
//   毎フレーム (Engine.Run の中、ゲームの System の後) :
//     UIWidget コンポーネントの反映 → 並べる → マウス / キー (再生中) → 描く (UIDrawList)
//   描いた物は Player では Graphics.SubmitUI で画面の一番上に描かれる。エディターはシーンビューの絵の上に描く (AutoSubmit = false)
// ==========================================================================
public sealed class UIManager
{
    private readonly Engine _engine;
    private readonly List<UUserWidget> _widgets = new();
    private long _order;
    private readonly Dictionary<int, Record> _byEntity = new();
    private readonly HashSet<int> _seen = new();
    private readonly UIPainter _painter = new();
    private bool _submittedSomething;

    private sealed class Record
    {
        public Entity Entity;
        public UUserWidget Widget = null!;
        public WidgetAsset? Asset;
        public int ZOrder;
        public bool Visible;
        public bool Active;
    }

    public UIResources Resources { get; }
    public UIContext Context { get; }
    public UIDrawList DrawList { get; } = new();
    public AssetManager Assets => _engine.Assets;

    /// <summary>描いた物を自分で Graphics.SubmitUI するか (Player = true / エディター = false で、ImGui の中に描く)</summary>
    public bool AutoSubmit { get; set; } = true;
    /// <summary>UI の左上の窓の中の位置 (ピクセル。エディターはシーンビューの絵の左上)</summary>
    public Vector2 ViewportOffset { get; set; }
    /// <summary>UI の大きさ (ピクセル。null = 描く大きさ (Graphics.RenderSize / 窓))</summary>
    public Vector2? ViewportSize { get; set; }
    /// <summary>マウス / キーを UI へ渡すか (エディターは、シーンビューの上にマウスがある時だけ)</summary>
    public bool InputEnabled { get; set; } = true;

    public IReadOnlyList<UUserWidget> Widgets => _widgets;
    public bool IsPointerOverUI { get; private set; }
    public UWidget? HoveredWidget { get; private set; }
    public UWidget? FocusedWidget { get; private set; }
    public UWidget? CapturedWidget { get; private set; }
    /// <summary>最後のフレームの UI の処理の時間 (ms) / 四角の数</summary>
    public double LastMilliseconds { get; private set; }
    public int LastQuadCount => DrawList.Count;

    internal UIManager(Engine engine)
    {
        _engine = engine;
        Resources = new UIResources((w, h, rgba) => engine.Graphics.CreateUITexture(w, h, rgba));
        Context = new UIContext(Resources);
    }

    // ---------------------------------------------------------------------
    // 一覧
    // ---------------------------------------------------------------------

    public bool Contains(UUserWidget widget) => _widgets.Contains(widget);

    internal void Add(UUserWidget widget, int zOrder)
    {
        widget.SetZOrderInternal(zOrder);
        if (!_widgets.Contains(widget))
        {
            widget.Order = ++_order;
            _widgets.Add(widget);
        }
        Resort();
    }

    internal void Remove(UUserWidget widget)
    {
        if (!_widgets.Remove(widget)) return;
        releaseInputOf(widget);
    }

    internal void Resort() => _widgets.Sort((a, b) => a.ZOrder != b.ZOrder ? a.ZOrder.CompareTo(b.ZOrder) : a.Order.CompareTo(b.Order));

    /// <summary>全部外す (シーンを読み直した時 / 再生を止めた時)</summary>
    public void Clear()
    {
        _widgets.Clear();
        _byEntity.Clear();
        HoveredWidget = CapturedWidget = null;
        SetFocus(null, notify: false);
        IsPointerOverUI = false;
    }

    internal UUserWidget? WidgetOf(Entity entity) =>
        _byEntity.TryGetValue(entity.Id, out var record) && record.Entity == entity ? record.Widget : null;

    // ---------------------------------------------------------------------
    // 毎フレーム
    // ---------------------------------------------------------------------

    internal void Update(World world, bool playing, float deltaTime)
    {
        var start = System.Diagnostics.Stopwatch.GetTimestamp();
        syncComponents(world, playing);
        Context.DeltaTime = deltaTime;
        var size = viewportSize();
        layout(size);
        if (playing && InputEnabled)
        {
            if (processInput(size)) layout(size);   // クリックで変わった物をこのフレームの絵に入れる
            foreach (var widget in _widgets)
                if (widget.IsVisible && widget.Root != null) foreach (var w in widget.Root.Descendants()) w.Tick(deltaTime);
        }
        else
        {
            IsPointerOverUI = false;
        }
        paint();
        if (AutoSubmit) submit();
        LastMilliseconds = System.Diagnostics.Stopwatch.GetElapsedTime(start).TotalMilliseconds;
    }

    private Vector2 viewportSize()
    {
        if (ViewportSize is { } size) return size;
        var (w, h) = _engine.Graphics.RenderSize is { Width: > 0 } render ? render : _engine.ClientSize;
        return new Vector2(w, h);
    }

    /// <summary>基準の解像度 → 画面の倍率 (短い方の辺に合わせる。UE の DPI Scale の既定と同じ考え方)</summary>
    public static float ScaleFor(Vector2 viewport, Vector2 designSize) =>
        Math.Max(1e-3f, Math.Min(viewport.X / Math.Max(1, designSize.X), viewport.Y / Math.Max(1, designSize.Y)));

    private void layout(Vector2 size)
    {
        foreach (var widget in _widgets)
        {
            if (!widget.IsVisible || widget.Root is not { } root) continue;
            var scale = ScaleFor(size, widget.DesignSize);
            LayoutTree(root, size / scale, Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(ViewportOffset),
                       new Vector4(ViewportOffset.X, ViewportOffset.Y, ViewportOffset.X + size.X, ViewportOffset.Y + size.Y));
        }
    }

    /// <summary>ツリーを並べる (size = 自分の単位の大きさ / transform = 自分の単位 → ピクセル / clip = ピクセル)</summary>
    public void LayoutTree(UWidget root, Vector2 size, Matrix3x2 transform, Vector4 clip)
    {
        root.Prepass(Context);
        root.Arrange(Context, transform, size, clip);
    }

    private void paint()
    {
        for (int attempt = 0; attempt < 2; attempt++)
        {
            DrawList.Clear();
            beginPaint(DrawList);
            foreach (var widget in _widgets)
                if (widget.IsVisible && widget.Root is { } root) root.Paint(_painter, 1.0f, true);
            // 初めての文字があった : GPU へ送ってから描き直す (そのフレームから出る)
            if (!_painter.MissingGlyphs || !Resources.Fonts.Flush()) break;
        }
    }

    private void beginPaint(UIDrawList list)
    {
        _painter.List = list;
        _painter.Resources = Resources;
        _painter.MissingGlyphs = false;
    }

    /// <summary>
    /// 1 つのツリーを並べて描く (エディターの Widget Designer / 確認用)。画面の UI とは別
    ///   designSize = 自分の単位の大きさ / offset, scale = ピクセルへ / clip = ピクセル
    /// </summary>
    public void PaintTree(UWidget root, Vector2 designSize, Vector2 offset, float scale, UIDrawList list, Vector4 clip)
    {
        LayoutTree(root, designSize, Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(offset), clip);
        for (int attempt = 0; attempt < 2; attempt++)
        {
            list.Clear();
            beginPaint(list);
            root.Paint(_painter, 1.0f, true);
            if (!_painter.MissingGlyphs || !Resources.Fonts.Flush()) break;
            LayoutTree(root, designSize, Matrix3x2.CreateScale(scale) * Matrix3x2.CreateTranslation(offset), clip);
        }
    }

    private void submit()
    {
        if (DrawList.Count == 0)
        {
            if (!_submittedSomething) return;
            _engine.Graphics.SubmitUI(ReadOnlySpan<Rendering.UIVertex>.Empty, ReadOnlySpan<ushort>.Empty, ReadOnlySpan<Rendering.UIDrawCommand>.Empty);
            _submittedSomething = false;
            return;
        }
        var (vertices, indices, commands) = DrawList.BuildBuffers();
        _engine.Graphics.SubmitUI(vertices.Span, indices.Span, commands.Span);
        _submittedSomething = true;
    }

    // ---------------------------------------------------------------------
    // シーンの UIWidget コンポーネント
    // ---------------------------------------------------------------------

    private void syncComponents(World world, bool playing)
    {
        var pool = world.GetPool<UIWidget>();
        if (pool.Count == 0 && _byEntity.Count == 0) return;
        _seen.Clear();
        var entities = pool.Entities;
        var components = pool.Components;
        for (int i = 0; i < entities.Length; i++)
        {
            var entity = entities[i];
            ref var component = ref components[i];
            _seen.Add(entity.Id);
            var active = world.IsActive(entity);
            if (_byEntity.TryGetValue(entity.Id, out var record) && (record.Entity != entity || record.Asset != component.Widget))
            {
                Remove(record.Widget);   // 別のエンティティ (番号の使い回し) / アセットを差し替えた : 作り直す
                _byEntity.Remove(entity.Id);
                record = null;
            }
            if (record == null)
            {
                if (!playing || component.Widget == null) { component.instance = null; continue; }
                UUserWidget created;
                try { created = component.Widget.CreateInstance(); }
                catch (Exception ex) when (ex is System.Text.Json.JsonException or InvalidDataException)
                {
                    Console.WriteLine($"[UI] Cannot create {component.Widget.Path} : {ex.Message}");
                    component.Widget = null;
                    continue;
                }
                created.Entity = entity;
                created.IsVisible = component.Visible;
                record = new Record { Entity = entity, Widget = created, Asset = component.Widget, ZOrder = component.ZOrder, Visible = component.Visible, Active = active };
                _byEntity[entity.Id] = record;
                if (active) Add(created, component.ZOrder);
            }
            else
            {
                if (component.ZOrder != record.ZOrder) { record.Widget.SetZOrder(component.ZOrder); record.ZOrder = component.ZOrder; }
                if (component.Visible != record.Visible) { record.Widget.IsVisible = component.Visible; record.Visible = component.Visible; }
                if (active != record.Active)
                {
                    if (active) Add(record.Widget, record.ZOrder); else Remove(record.Widget);
                    record.Active = active;
                }
            }
            component.instance = record.Widget;
        }
        // コンポーネントを外した / エンティティを消した物
        if (_byEntity.Count > _seen.Count)
        {
            foreach (var id in _byEntity.Keys.Where(id => !_seen.Contains(id)).ToList())
            {
                Remove(_byEntity[id].Widget);
                _byEntity.Remove(id);
            }
        }
    }

    // ---------------------------------------------------------------------
    // マウス / キー
    // ---------------------------------------------------------------------

    /// <summary>キーを受け取る部品を決める (null = 外す。notify = 外れた部品の OnFocusLost を呼ぶ)</summary>
    public void SetFocus(UWidget? widget, bool notify = true)
    {
        if (FocusedWidget == widget) return;
        var old = FocusedWidget;
        FocusedWidget = widget;
        if (old != null)
        {
            old.HasKeyboardFocus = false;
            if (notify) old.OnFocusLost();
        }
        if (widget != null) widget.HasKeyboardFocus = true;
    }

    // 戻り値 = イベントを呼んだか (並べ直す)
    private bool processInput(Vector2 size)
    {
        validate();
        var mouse = Input.MousePosition;
        var local = mouse - ViewportOffset;
        var inside = local.X >= 0 && local.Y >= 0 && local.X < size.X && local.Y < size.Y;
        var hit = inside ? HitTest(mouse) : null;
        IsPointerOverUI = hit != null;
        var handler = hit;
        while (handler != null && !handler.HandlesPointer) handler = handler.Parent;
        if (handler != null && !handler.IsEnabledInHierarchy) handler = null;

        var fired = false;
        // 乗った / 離れた (掴んでいる間は、掴んだ物の上にある時だけ乗っている)
        var hovered = CapturedWidget != null && handler != CapturedWidget ? null : handler;
        if (hovered != HoveredWidget)
        {
            if (HoveredWidget != null) { HoveredWidget.IsHovered = false; HoveredWidget.OnMouseLeave(); }
            HoveredWidget = hovered;
            if (hovered != null) { hovered.IsHovered = true; hovered.OnMouseEnter(); }
            fired = true;
        }
        if (Input.GetMouseButtonDown(MouseButtons.Left))
        {
            if (handler != null)
            {
                CapturedWidget = handler;
                SetFocus(handler.IsFocusable ? handler : null);
                handler.OnMouseDown(handler.ToLocal(mouse));
            }
            else SetFocus(null);
            fired = true;
        }
        if (CapturedWidget != null)
        {
            var captured = CapturedWidget;
            if (Input.MouseDelta != Vector2.Zero && Input.GetMouseButton(MouseButtons.Left)) { captured.OnMouseDrag(captured.ToLocal(mouse)); fired = true; }
            if (Input.GetMouseButtonUp(MouseButtons.Left) || !Input.GetMouseButton(MouseButtons.Left))
            {
                CapturedWidget = null;
                captured.OnMouseUp(captured.ToLocal(mouse), handler == captured);
                fired = true;
            }
        }
        // ホイール : マウスの下から親へ、使う物を探す
        if (Input.ScrollDelta != 0 && hit != null)
        {
            for (var w = hit; w != null; w = w.Parent)
                if (w.IsEnabledInHierarchy && w.OnMouseWheel(Input.ScrollDelta)) { fired = true; break; }
        }
        // キー
        if (FocusedWidget is { } focused)
        {
            foreach (var (key, down) in Input.KeyEvents)
            {
                if (!down) continue;
                focused.OnKeyDown(key);
                fired = true;
                if (FocusedWidget != focused) break;
            }
            if (FocusedWidget == focused)
                foreach (var c in Input.TypedCharacters) { focused.OnCharacter(c); fired = true; }
        }
        return fired;
    }

    /// <summary>画面のピクセルの位置で一番手前の当たる部品 (無ければ null)</summary>
    public UWidget? HitTest(Vector2 screen)
    {
        for (int i = _widgets.Count - 1; i >= 0; i--)
        {
            var widget = _widgets[i];
            if (!widget.IsVisible || widget.Visibility == UIVisibility.HitTestInvisible || widget.Root == null) continue;
            if (hitTest(widget.Root, screen) is { } hit) return hit;
        }
        return null;
    }

    private static UWidget? hitTest(UWidget widget, Vector2 screen)
    {
        if (!widget.IsVisible || widget.Visibility == UIVisibility.HitTestInvisible) return null;
        foreach (var child in widget.HitOrder)
            if (hitTest(child, screen) is { } hit) return hit;
        if (widget.Visibility != UIVisibility.SelfHitTestInvisible && (widget.BlocksPointer || widget.HandlesPointer) && widget.Contains(screen)) return widget;
        return null;
    }

    // 画面から外れた / 隠れた物を掴んだまま・乗ったままにしない
    private void validate()
    {
        bool live(UWidget? w)
        {
            if (w == null) return true;
            for (var p = w; p != null; p = p.Parent) if (!p.IsVisible) return false;
            var root = w;
            while (root.Parent != null) root = root.Parent;
            foreach (var widget in _widgets) if (widget.Root == root) return widget.IsVisible;
            return false;
        }
        if (!live(HoveredWidget)) { HoveredWidget!.IsHovered = false; HoveredWidget = null; }
        if (!live(CapturedWidget)) CapturedWidget = null;
        if (!live(FocusedWidget)) SetFocus(null);
    }

    private void releaseInputOf(UUserWidget widget)
    {
        bool belongs(UWidget? w)
        {
            if (w == null) return false;
            while (w.Parent != null) w = w.Parent;
            return w == widget.Root;
        }
        if (belongs(HoveredWidget)) { HoveredWidget!.IsHovered = false; HoveredWidget = null; }
        if (belongs(CapturedWidget)) CapturedWidget = null;
        if (belongs(FocusedWidget)) SetFocus(null, notify: false);
    }
}
