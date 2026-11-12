using System.Numerics;

namespace GameEngine;

// ==========================================================================
// 部品 (v1.7。UE の UMG の Common / Input の部品)
//   TextBlock / Image / Button / CheckBox / ProgressBar / Slider / EditableTextBox / Spacer
//   イベントは C# の event (UE の OnClicked などに当たる。ゲームのコードで += する)
// ==========================================================================

/// <summary>文字。UE の UTextBlock</summary>
public sealed class UTextBlock : UWidget
{
    public string  Text = "Text Block";
    public UIFont  Font = new();
    public Vector4 ColorAndOpacity = Vector4.One;
    public UITextJustify Justification;
    public bool    AutoWrapText;
    public float   WrapTextAt;            // 0 = 折り返さない (AutoWrapText は並べた幅で)
    public float   LineHeightPercentage = 1.0f;
    public Vector2 ShadowOffset;          // 0,0 = 影なし
    public Vector4 ShadowColorAndOpacity = new(0, 0, 0, 0.6f);
    public float   MinDesiredWidth;

    public void SetText(string text) => Text = text ?? "";
    public void SetColorAndOpacity(Vector4 color) => ColorAndOpacity = color;

    private float _lastWidth;

    protected override Vector2 ComputeDesiredSize(UIContext context)
    {
        var wrap = WrapTextAt > 0 ? WrapTextAt : AutoWrapText && _lastWidth > 0 ? _lastWidth : float.PositiveInfinity;
        var lines = UIText.Wrap(Text, Font, wrap, context.Resources.Fonts);
        var lineHeight = context.Resources.Fonts.LineHeight(Font) * LineHeightPercentage;
        var width = 0.0f;
        foreach (var (start, length) in lines) width = Math.Max(width, context.Resources.Fonts.Measure(Text.AsSpan(start, length), Font));
        return new Vector2(Math.Max(width, MinDesiredWidth), lines.Count * lineHeight) + Vector2.Abs(ShadowOffset);
    }

    protected override void ArrangeChildren(UIContext context) => _lastWidth = Size.X;

    protected override void OnPaint(UIPainter painter)
    {
        var fonts = painter.Resources.Fonts;
        var wrap = WrapTextAt > 0 ? WrapTextAt : AutoWrapText ? Size.X : float.PositiveInfinity;
        var lines = UIText.Wrap(Text, Font, wrap, fonts);
        var lineHeight = fonts.LineHeight(Font) * LineHeightPercentage;
        var y = 0.0f;
        foreach (var (start, length) in lines)
        {
            var line = Text.AsSpan(start, length);
            var x = Justification switch
            {
                UITextJustify.Center => (Size.X - fonts.Measure(line, Font)) * 0.5f,
                UITextJustify.Right  => Size.X - fonts.Measure(line, Font),
                _                    => 0.0f,
            };
            if (ShadowOffset != Vector2.Zero) painter.Text(line, new Vector2(x, y) + ShadowOffset, Font, ShadowColorAndOpacity);
            painter.Text(line, new Vector2(x, y), Font, ColorAndOpacity);
            y += lineHeight;
        }
    }
}

/// <summary>画像 (テクスチャのアセット) / 色の四角。UE の UImage</summary>
public sealed class UImage : UWidget
{
    public UIBrush Brush = new() { ImageSize = new Vector2(32, 32) };
    public Vector4 ColorAndOpacity = Vector4.One;

    public void SetBrushFromTexture(string imagePath, bool matchSize = false)
    {
        Brush.Image = imagePath ?? "";
        if (matchSize) Brush.ImageSize = Vector2.Zero;
    }

    public void SetColorAndOpacity(Vector4 color) => ColorAndOpacity = color;

    protected internal override bool BlocksPointer => ColorAndOpacity.W > 0.0f && Brush.DrawAs != UIBrushDrawType.None;

    protected override Vector2 ComputeDesiredSize(UIContext context) =>
        Brush.ImageSize != Vector2.Zero ? Brush.ImageSize : context.Resources.Textures.SizeOf(Brush.Image);

    protected override void OnPaint(UIPainter painter) => painter.Brush(Brush, Vector2.Zero, Size, ColorAndOpacity);
}

/// <summary>何も描かない隙間。UE の USpacer</summary>
public sealed class USpacer : UWidget
{
    public Vector2 Size2D = Vector2.One;
    protected override Vector2 ComputeDesiredSize(UIContext context) => Size2D;
}

/// <summary>押せる (中身 1 つ : TextBlock など)。UE の UButton</summary>
public sealed class UButton : UContentWidget
{
    public UIBrush Normal   = UIBrush.Rounded(new Vector4(0.22f, 0.22f, 0.24f, 1.0f), 4.0f);
    public UIBrush Hovered  = UIBrush.Rounded(new Vector4(0.32f, 0.32f, 0.36f, 1.0f), 4.0f);
    public UIBrush Pressed  = UIBrush.Rounded(new Vector4(0.12f, 0.12f, 0.14f, 1.0f), 4.0f);
    public UIBrush Disabled = UIBrush.Rounded(new Vector4(0.22f, 0.22f, 0.24f, 0.5f), 4.0f);
    public Vector4 BackgroundColor = Vector4.One;   // ブラシに掛ける色
    public Vector4 ColorAndOpacity = Vector4.One;   // (中身の色。今は使わない : UE と同じ項目を残す)
    public Vector2 MinimumSize = new(32, 32);

    /// <summary>押して、上で離した (クリック)。UE の OnClicked</summary>
    public event Action? OnClicked;
    /// <summary>押した / 離した</summary>
    public event Action? OnPressed, OnReleased;
    /// <summary>マウスが乗った / 離れた</summary>
    public event Action? OnHovered, OnUnhovered;

    /// <summary>押している (掴んでいる) 間</summary>
    public bool IsPressed { get; private set; }
    /// <summary>今までに押された回数 (確認用)</summary>
    public int ClickCount { get; private set; }

    public override UIPanelSlot CreateSlot() => new() { Padding = new UIMargin(12, 4), HorizontalAlignment = UIHorizontalAlignment.Center, VerticalAlignment = UIVerticalAlignment.Center };

    protected internal override bool HandlesPointer => true;

    protected override Vector2 ComputeDesiredSize(UIContext context) => Vector2.Max(ContentDesiredSize(), MinimumSize);

    protected override void OnPaint(UIPainter painter)
    {
        var brush = !IsEnabledInHierarchy ? Disabled : IsPressed && IsHovered ? Pressed : IsHovered || IsPressed ? Hovered : Normal;
        painter.Brush(brush, Vector2.Zero, Size, BackgroundColor);
    }

    protected internal override void OnMouseEnter() => OnHovered?.Invoke();
    protected internal override void OnMouseLeave() => OnUnhovered?.Invoke();
    protected internal override void OnMouseDown(Vector2 local) { IsPressed = true; OnPressed?.Invoke(); }

    protected internal override void OnMouseUp(Vector2 local, bool inside)
    {
        IsPressed = false;
        OnReleased?.Invoke();
        if (inside) Click();
    }

    /// <summary>コードから押す (OnClicked を呼ぶ)</summary>
    public void Click()
    {
        ClickCount++;
        OnClicked?.Invoke();
    }
}

/// <summary>チェック (中身 1 つ = 横の文字)。UE の UCheckBox</summary>
public sealed class UCheckBox : UContentWidget
{
    public bool IsChecked;
    public float BoxSize = 24.0f;
    public Vector4 BoxColor = new(0.18f, 0.18f, 0.2f, 1.0f);
    public Vector4 HoveredBoxColor = new(0.28f, 0.28f, 0.32f, 1.0f);
    public Vector4 CheckColor = new(0.35f, 0.75f, 1.0f, 1.0f);

    /// <summary>チェックが変わった (新しい値)</summary>
    public event Action<bool>? OnCheckStateChanged;

    public override UIPanelSlot CreateSlot() => new() { Padding = new UIMargin(6, 0, 0, 0), HorizontalAlignment = UIHorizontalAlignment.Left, VerticalAlignment = UIVerticalAlignment.Center };
    protected internal override bool HandlesPointer => true;

    public void SetIsChecked(bool value)
    {
        if (IsChecked == value) return;
        IsChecked = value;
        OnCheckStateChanged?.Invoke(value);
    }

    protected override Vector2 ComputeDesiredSize(UIContext context)
    {
        var content = ContentDesiredSize();
        return new Vector2(BoxSize + content.X, Math.Max(BoxSize, content.Y));
    }

    protected override void ArrangeChildren(UIContext context)
    {
        if (Content is not { } child) return;
        var (position, size) = AlignInside(child, new Vector2(BoxSize, 0), new Vector2(Math.Max(0, Size.X - BoxSize), Size.Y));
        ArrangeChild(context, child, position, size);
    }

    protected override void OnPaint(UIPainter painter)
    {
        var top = (Size.Y - BoxSize) * 0.5f;
        var min = new Vector2(0, top);
        var max = new Vector2(BoxSize, top + BoxSize);
        painter.Brush(UIBrush.Rounded(IsHovered ? HoveredBoxColor : BoxColor, 4), min, max, Vector4.One);
        if (IsChecked)
        {
            var inset = BoxSize * 0.22f;
            painter.Brush(UIBrush.Rounded(CheckColor, 3), min + new Vector2(inset), max - new Vector2(inset), Vector4.One);
        }
    }

    protected internal override void OnMouseUp(Vector2 local, bool inside)
    {
        if (inside) SetIsChecked(!IsChecked);
    }
}

/// <summary>割合の棒 (Percent 0〜1)。UE の UProgressBar</summary>
public sealed class UProgressBar : UWidget
{
    [Range(0, 1)] public float Percent = 0.5f;
    public UIProgressFill BarFillType;
    public UIBrush BackgroundImage = UIBrush.Rounded(new Vector4(0.1f, 0.1f, 0.12f, 0.9f), 4.0f);
    public UIBrush FillImage = UIBrush.Rounded(Vector4.One, 4.0f);
    public Vector4 FillColorAndOpacity = new(0.2f, 0.65f, 1.0f, 1.0f);
    public Vector2 DesiredBarSize = new(200, 20);

    public void SetPercent(float percent) => Percent = Math.Clamp(percent, 0, 1);
    public void SetFillColorAndOpacity(Vector4 color) => FillColorAndOpacity = color;

    protected override Vector2 ComputeDesiredSize(UIContext context) => DesiredBarSize;
    protected internal override bool BlocksPointer => true;

    protected override void OnPaint(UIPainter painter)
    {
        painter.Brush(BackgroundImage, Vector2.Zero, Size, Vector4.One);
        var p = Math.Clamp(Percent, 0, 1);
        if (p <= 0) return;
        Vector2 min = Vector2.Zero, max = Size;
        switch (BarFillType)
        {
        case UIProgressFill.LeftToRight:    max.X = Size.X * p; break;
        case UIProgressFill.RightToLeft:    min.X = Size.X * (1 - p); break;
        case UIProgressFill.FillFromCenter: min.X = Size.X * (1 - p) * 0.5f; max.X = Size.X - min.X; break;
        case UIProgressFill.TopToBottom:    max.Y = Size.Y * p; break;
        case UIProgressFill.BottomToTop:    min.Y = Size.Y * (1 - p); break;
        }
        painter.Brush(FillImage, min, max, FillColorAndOpacity);
    }
}

/// <summary>つまみを動かして値を選ぶ。UE の USlider</summary>
public sealed class USlider : UWidget
{
    public float Value;
    public float MinValue;
    public float MaxValue = 1.0f;
    public float StepSize = 0.01f;   // 0 = 段なし
    public UIOrientation Orientation;
    public float BarThickness = 6.0f;
    public float HandleSize = 20.0f;
    public Vector4 SliderBarColor = new(0.15f, 0.15f, 0.17f, 1.0f);
    public Vector4 SliderProgressColor = new(0.2f, 0.65f, 1.0f, 1.0f);
    public Vector4 SliderHandleColor = new(0.9f, 0.9f, 0.92f, 1.0f);
    public Vector2 DesiredBarSize = new(200, 24);

    /// <summary>ユーザーが動かした時 (新しい値)</summary>
    public event Action<float>? OnValueChanged;

    protected internal override bool HandlesPointer => true;

    public void SetValue(float value) => Value = Math.Clamp(value, Math.Min(MinValue, MaxValue), Math.Max(MinValue, MaxValue));

    /// <summary>0〜1 の位置</summary>
    public float NormalizedValue => MaxValue != MinValue ? Math.Clamp((Value - MinValue) / (MaxValue - MinValue), 0, 1) : 0;

    private bool vertical => Orientation == UIOrientation.Vertical;

    protected override Vector2 ComputeDesiredSize(UIContext context) => vertical ? new Vector2(DesiredBarSize.Y, DesiredBarSize.X) : DesiredBarSize;

    protected override void OnPaint(UIPainter painter)
    {
        var t = NormalizedValue;
        var h = HandleSize * 0.5f;
        if (!vertical)
        {
            var cy = Size.Y * 0.5f;
            var length = Math.Max(0, Size.X - HandleSize);
            painter.Brush(UIBrush.Rounded(SliderBarColor, BarThickness * 0.5f), new Vector2(h, cy - BarThickness * 0.5f), new Vector2(Size.X - h, cy + BarThickness * 0.5f), Vector4.One);
            painter.Brush(UIBrush.Rounded(SliderProgressColor, BarThickness * 0.5f), new Vector2(h, cy - BarThickness * 0.5f), new Vector2(h + length * t, cy + BarThickness * 0.5f), Vector4.One);
            var cx = h + length * t;
            painter.Brush(UIBrush.Rounded(IsHovered ? Vector4.One : SliderHandleColor, h), new Vector2(cx - h, cy - h), new Vector2(cx + h, cy + h), Vector4.One);
        }
        else
        {
            var cx = Size.X * 0.5f;
            var length = Math.Max(0, Size.Y - HandleSize);
            var cy = Size.Y - h - length * t;   // 下が最小
            painter.Brush(UIBrush.Rounded(SliderBarColor, BarThickness * 0.5f), new Vector2(cx - BarThickness * 0.5f, h), new Vector2(cx + BarThickness * 0.5f, Size.Y - h), Vector4.One);
            painter.Brush(UIBrush.Rounded(SliderProgressColor, BarThickness * 0.5f), new Vector2(cx - BarThickness * 0.5f, cy), new Vector2(cx + BarThickness * 0.5f, Size.Y - h), Vector4.One);
            painter.Brush(UIBrush.Rounded(IsHovered ? Vector4.One : SliderHandleColor, h), new Vector2(cx - h, cy - h), new Vector2(cx + h, cy + h), Vector4.One);
        }
    }

    protected internal override void OnMouseDown(Vector2 local) => setFromLocal(local);
    protected internal override void OnMouseDrag(Vector2 local) => setFromLocal(local);

    private void setFromLocal(Vector2 local)
    {
        var h = HandleSize * 0.5f;
        var t = !vertical ? (local.X - h) / Math.Max(1, Size.X - HandleSize) : 1 - (local.Y - h) / Math.Max(1, Size.Y - HandleSize);
        var value = MinValue + Math.Clamp(t, 0, 1) * (MaxValue - MinValue);
        if (StepSize > 0) value = MinValue + MathF.Round((value - MinValue) / StepSize) * StepSize;
        value = Math.Clamp(value, Math.Min(MinValue, MaxValue), Math.Max(MinValue, MaxValue));
        if (value == Value) return;
        Value = value;
        OnValueChanged?.Invoke(value);
    }
}

/// <summary>1 行の文字の欄 (クリックで入力。Enter で確定)。UE の UEditableTextBox</summary>
public sealed class UEditableTextBox : UWidget
{
    public string  Text = "";
    public string  HintText = "";
    public UIFont  Font = new(20);
    public Vector4 ForegroundColor = Vector4.One;
    public Vector4 HintColor = new(1, 1, 1, 0.4f);
    public UIBrush Background = UIBrush.Rounded(new Vector4(0.08f, 0.08f, 0.1f, 0.95f), 4.0f);
    public Vector4 FocusedBorderColor = new(0.3f, 0.65f, 1.0f, 1.0f);
    public UIMargin Padding = new(8, 4);
    public bool    IsReadOnly;
    public bool    IsPassword;
    public float   MinDesiredWidth = 200.0f;
    public int     MaxLength;   // 0 = 決めない

    /// <summary>文字が変わった (打つたび)</summary>
    public event Action<string>? OnTextChanged;
    /// <summary>確定した (Enter / フォーカスが外れた)</summary>
    public event Action<string, UITextCommit>? OnTextCommitted;

    public int CaretIndex { get; private set; }
    private float _blink;
    private float _scroll;

    protected internal override bool HandlesPointer => true;
    protected internal override bool IsFocusable => !IsReadOnly;

    public void SetText(string text) { Text = text ?? ""; CaretIndex = Math.Min(CaretIndex, Text.Length); }

    private string shown => IsPassword ? new string('*', Text.Length) : Text;

    protected override Vector2 ComputeDesiredSize(UIContext context) =>
        new Vector2(MinDesiredWidth, context.Resources.Fonts.LineHeight(Font)) + Padding.Total;

    protected override void OnPaint(UIPainter painter)
    {
        painter.Brush(Background, Vector2.Zero, Size, Vector4.One);
        if (HasKeyboardFocus)
        {
            // 枠 (4 辺の細い四角)
            const float b = 2;
            painter.Rect(Vector2.Zero, new Vector2(Size.X, b), FocusedBorderColor);
            painter.Rect(new Vector2(0, Size.Y - b), Size, FocusedBorderColor);
            painter.Rect(Vector2.Zero, new Vector2(b, Size.Y), FocusedBorderColor);
            painter.Rect(new Vector2(Size.X - b, 0), Size, FocusedBorderColor);
        }
        var fonts = painter.Resources.Fonts;
        var inner = Size.X - Padding.Horizontal;
        var lineHeight = fonts.LineHeight(Font);
        var y = (Size.Y - lineHeight) * 0.5f;
        // カーソルが見えるように横にずらす
        var caretX = fonts.Measure(shown.AsSpan(0, Math.Min(CaretIndex, shown.Length)), Font);
        if (caretX - _scroll > inner) _scroll = caretX - inner;
        if (caretX - _scroll < 0) _scroll = caretX;
        // 文字は欄の内側で切る
        var clip = painter.Clip;
        var innerMin = Vector2.Transform(new Vector2(Padding.Left, 0), painter.Transform);
        var innerMax = Vector2.Transform(new Vector2(Size.X - Padding.Right, Size.Y), painter.Transform);
        painter.Clip = Intersect(clip, new Vector4(Math.Min(innerMin.X, innerMax.X), Math.Min(innerMin.Y, innerMax.Y), Math.Max(innerMin.X, innerMax.X), Math.Max(innerMin.Y, innerMax.Y)));
        if (Text.Length == 0 && !HasKeyboardFocus) painter.Text(HintText, new Vector2(Padding.Left, y), Font, HintColor);
        else painter.Text(shown, new Vector2(Padding.Left - _scroll, y), Font, ForegroundColor);
        if (HasKeyboardFocus && _blink % 1.0f < 0.6f)
            painter.Rect(new Vector2(Padding.Left + caretX - _scroll, y), new Vector2(Padding.Left + caretX - _scroll + 2, y + lineHeight), ForegroundColor);
        painter.Clip = clip;
    }

    protected internal override void Tick(float deltaTime) { if (HasKeyboardFocus) _blink += deltaTime; }

    protected internal override void OnMouseDown(Vector2 local)
    {
        _blink = 0;
        // クリックした位置の文字の間へ
        var x = local.X - Padding.Left + _scroll;
        var shownText = shown;
        var best = shownText.Length;
        var fonts = UI.Manager?.Resources.Fonts;
        if (fonts != null)
        {
            for (int i = 0; i <= shownText.Length; i++)
            {
                var w = fonts.Measure(shownText.AsSpan(0, i), Font);
                if (w >= x) { best = i > 0 && w - x > x - fonts.Measure(shownText.AsSpan(0, i - 1), Font) ? i - 1 : i; break; }
            }
        }
        CaretIndex = best;
    }

    protected internal override void OnCharacter(char c)
    {
        if (IsReadOnly || c < 32 || c == 127) return;
        if (MaxLength > 0 && Text.Length >= MaxLength) return;
        Text = Text.Insert(CaretIndex, c.ToString());
        CaretIndex++;
        _blink = 0;
        OnTextChanged?.Invoke(Text);
    }

    protected internal override void OnKeyDown(Keys key)
    {
        _blink = 0;
        switch (key)
        {
        case Keys.Back when CaretIndex > 0 && !IsReadOnly:
            Text = Text.Remove(CaretIndex - 1, 1); CaretIndex--; OnTextChanged?.Invoke(Text); break;
        case Keys.Delete when CaretIndex < Text.Length && !IsReadOnly:
            Text = Text.Remove(CaretIndex, 1); OnTextChanged?.Invoke(Text); break;
        case Keys.Left:  CaretIndex = Math.Max(0, CaretIndex - 1); break;
        case Keys.Right: CaretIndex = Math.Min(Text.Length, CaretIndex + 1); break;
        case Keys.Home:  CaretIndex = 0; break;
        case Keys.End:   CaretIndex = Text.Length; break;
        case Keys.Enter:
            OnTextCommitted?.Invoke(Text, UITextCommit.OnEnter);
            UI.Manager?.SetFocus(null, notify: false);
            break;
        case Keys.Escape:
            UI.Manager?.SetFocus(null);
            break;
        }
    }

    protected internal override void OnFocusLost() => OnTextCommitted?.Invoke(Text, UITextCommit.OnUserMovedFocus);
}

// ==========================================================================
// 文字の折り返し (空白で折る。日本語など空白の無い所は文字の間で折る)
// ==========================================================================
public static class UIText
{
    public static List<(int Start, int Length)> Wrap(string text, in UIFont font, float width, UIFonts fonts)
    {
        var lines = new List<(int, int)>();
        text ??= "";
        var lineStart = 0;
        for (int i = 0; i <= text.Length; i++)
        {
            if (i < text.Length && text[i] != '\n') continue;
            wrapParagraph(text, lineStart, i - lineStart, font, width, fonts, lines);
            lineStart = i + 1;
        }
        if (lines.Count == 0) lines.Add((0, 0));
        return lines;
    }

    // 折れる所 : 空白 (空白は次の行に持ち越さない) / 日本語などの文字の前後 (空白が無くても折れる)
    //   以前は空白だけで折っていたので、「Z キーで Menu を手前に」が「Menu」の後の空白で早く折れた
    private static void wrapParagraph(string text, int start, int length, in UIFont font, float width, UIFonts fonts, List<(int, int)> lines)
    {
        if (float.IsInfinity(width) || length == 0) { lines.Add((start, length)); return; }
        var atlas = fonts.Get(font, font.Size);
        var scale = font.Size / atlas.PixelSize;
        var end = start + length;
        var lineStart = start;
        var lineWidth = 0.0f;
        var breakAt = -1;         // 次の行の始まりにできる所
        var breakSkip = false;    // そこが空白 (行に入れない)
        for (int i = start; i < end; i++)
        {
            var c = text[i];
            if (c == ' ') { breakAt = i; breakSkip = true; }
            else if (i > lineStart && (isCjk(c) || isCjk(text[i - 1])) && !isClosing(c)) { breakAt = i; breakSkip = false; }
            var advance = atlas.Get(c).Advance * scale;
            if (lineWidth + advance > width && i > lineStart && c != ' ')
            {
                var next = breakAt > lineStart ? breakAt : i;   // 折れる所が無ければ文字の間で
                var skip = breakAt > lineStart && breakSkip;
                lines.Add((lineStart, next - lineStart));
                lineStart = skip ? next + 1 : next;
                lineWidth = lineStart < i ? fonts.Measure(text.AsSpan(lineStart, i - lineStart), font) : 0.0f;
                breakAt = -1;
            }
            lineWidth += advance;
        }
        lines.Add((lineStart, end - lineStart));
    }

    // 日本語 / 中国語 / 韓国語の文字 (前後で折れる)
    private static bool isCjk(char c) => c >= 0x2E80 && c <= 0x9FFF || c >= 0xAC00 && c <= 0xD7AF || c >= 0xF900 && c <= 0xFAFF || c >= 0xFF00 && c <= 0xFFEF;
    // 行の頭に来ない文字 (句読点 / 閉じかっこ)
    private static bool isClosing(char c) => "、。，．）」』】〕〉》！？ー…・ゃゅょっャュョッ,.!?)]}".IndexOf(c) >= 0;
}
