using System.Numerics;

namespace GameEngine;

// ==========================================================================
// UI (v1.7) で使う値の型 (UE の UMG / Slate と同じ名前と意味)
//   単位は「デザインの単位」(Widget アセットの基準の解像度。既定 1920 x 1080)。画面の大きさに合わせて拡大縮小される
//   色は Vector4 (R, G, B, A。0〜1。sRGB のまま)
// ==========================================================================

/// <summary>見える / 当たるか (UE の ESlateVisibility)</summary>
public enum UIVisibility
{
    Visible,                // 見える + マウスが当たる
    Collapsed,              // 見えない + 場所も取らない
    Hidden,                 // 見えない (場所は取る)
    HitTestInvisible,       // 見える。自分も子もマウスが当たらない
    SelfHitTestInvisible,   // 見える。自分には当たらない (子には当たる)
}

public enum UIHorizontalAlignment { Fill, Left, Center, Right }
public enum UIVerticalAlignment   { Fill, Top, Center, Bottom }

/// <summary>Box の並びでの大きさ (Auto = 中身の大きさ / Fill = 余りを FillWeight の比で分ける)</summary>
public enum UISizeRule { Auto, Fill }

public enum UIOrientation { Horizontal, Vertical }

public enum UITextJustify { Left, Center, Right }

/// <summary>ブラシの描き方 (UE の ESlateBrushDrawType)</summary>
public enum UIBrushDrawType
{
    None,         // 描かない
    Image,        // 画像を四角いっぱいに伸ばす (画像が無ければ色の四角)
    Box,          // 9 分割 (Margin = 角の大きさ。画像の幅 / 高さに対する比 0〜0.5)
    RoundedBox,   // 角の丸い四角 (CornerRadius。色だけ)
}

/// <summary>ScaleBox の拡大のしかた (UE の EStretch)</summary>
public enum UIStretch { None, Fill, ScaleToFit, ScaleToFitX, ScaleToFitY, ScaleToFill, UserSpecified }

/// <summary>子を自分の四角で切り取るか</summary>
public enum UIClipping { Inherit, ClipToBounds }

/// <summary>ProgressBar の伸びる向き</summary>
public enum UIProgressFill { LeftToRight, RightToLeft, FillFromCenter, TopToBottom, BottomToTop }

/// <summary>文字の欄の確定のしかた (UE の ETextCommit)</summary>
public enum UITextCommit { Default, OnEnter, OnUserMovedFocus, OnCleared }

/// <summary>余白 (左 / 上 / 右 / 下)</summary>
public struct UIMargin : IEquatable<UIMargin>
{
    public float Left, Top, Right, Bottom;

    public UIMargin(float uniform) { Left = Top = Right = Bottom = uniform; }
    public UIMargin(float horizontal, float vertical) { Left = Right = horizontal; Top = Bottom = vertical; }
    public UIMargin(float left, float top, float right, float bottom) { Left = left; Top = top; Right = right; Bottom = bottom; }

    public readonly float Horizontal => Left + Right;
    public readonly float Vertical   => Top + Bottom;
    public readonly Vector2 Total    => new(Left + Right, Top + Bottom);
    public readonly Vector2 TopLeft  => new(Left, Top);

    public readonly bool Equals(UIMargin o) => Left == o.Left && Top == o.Top && Right == o.Right && Bottom == o.Bottom;
    public override readonly bool Equals(object? obj) => obj is UIMargin m && Equals(m);
    public override readonly int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);
    public override readonly string ToString() => $"({Left}, {Top}, {Right}, {Bottom})";
}

/// <summary>CanvasPanel のアンカー (親の大きさに対する比 0〜1。Minimum == Maximum なら点、違えば伸びる)</summary>
public struct UIAnchors : IEquatable<UIAnchors>
{
    public Vector2 Minimum;
    public Vector2 Maximum;

    public UIAnchors(float x, float y) { Minimum = Maximum = new Vector2(x, y); }
    public UIAnchors(float minX, float minY, float maxX, float maxY) { Minimum = new Vector2(minX, minY); Maximum = new Vector2(maxX, maxY); }

    public static UIAnchors TopLeft     => new(0, 0);
    public static UIAnchors Center      => new(0.5f, 0.5f);
    public static UIAnchors Fill        => new(0, 0, 1, 1);

    public readonly bool StretchX => Minimum.X != Maximum.X;
    public readonly bool StretchY => Minimum.Y != Maximum.Y;

    public readonly bool Equals(UIAnchors o) => Minimum == o.Minimum && Maximum == o.Maximum;
    public override readonly bool Equals(object? obj) => obj is UIAnchors a && Equals(a);
    public override readonly int GetHashCode() => HashCode.Combine(Minimum, Maximum);
}

/// <summary>描き方 (UE の FSlateBrush)。画像 (テクスチャのアセット) と色</summary>
public struct UIBrush : IEquatable<UIBrush>
{
    [AssetPath(AssetKind.Texture)] public string Image;   // 空なら白 (色だけの四角)
    public Vector4 Tint;
    public UIBrushDrawType DrawAs;
    public UIMargin Margin;        // Box : 角の大きさ (画像に対する比)
    public float CornerRadius;     // RoundedBox : 角の半径 (デザインの単位)
    public Vector2 ImageSize;      // Image の欲しい大きさ (0 = 画像の大きさ)

    public UIBrush()
    {
        Image  = "";
        Tint   = Vector4.One;
        DrawAs = UIBrushDrawType.Image;
    }

    public static UIBrush Color(Vector4 color) => new() { Tint = color };
    public static UIBrush Rounded(Vector4 color, float radius) => new() { Tint = color, DrawAs = UIBrushDrawType.RoundedBox, CornerRadius = radius };

    public readonly bool Equals(UIBrush o) => (Image ?? "") == (o.Image ?? "") && Tint == o.Tint && DrawAs == o.DrawAs && Margin.Equals(o.Margin) &&
                                              CornerRadius == o.CornerRadius && ImageSize == o.ImageSize;
    public override readonly bool Equals(object? obj) => obj is UIBrush b && Equals(b);
    public override readonly int GetHashCode() => HashCode.Combine(Image, Tint, DrawAs, Margin, CornerRadius, ImageSize);
}

/// <summary>描く時だけの動き / 拡大 / 回転 (並びには影響しない。UE の FWidgetTransform)</summary>
public struct UIRenderTransform : IEquatable<UIRenderTransform>
{
    public Vector2 Translation;
    public Vector2 Scale;
    public float   Angle;    // 度 (時計回り)
    public Vector2 Pivot;    // 回転 / 拡大の中心 (自分の大きさに対する比)

    public UIRenderTransform()
    {
        Scale = Vector2.One;
        Pivot = new Vector2(0.5f, 0.5f);
    }

    public readonly bool IsIdentity => Translation == Vector2.Zero && Scale == Vector2.One && Angle == 0.0f;

    public readonly bool Equals(UIRenderTransform o) => Translation == o.Translation && Scale == o.Scale && Angle == o.Angle && Pivot == o.Pivot;
    public override readonly bool Equals(object? obj) => obj is UIRenderTransform t && Equals(t);
    public override readonly int GetHashCode() => HashCode.Combine(Translation, Scale, Angle, Pivot);
}

/// <summary>文字の形 (Windows に入っているフォントの名前 / 大きさ (デザインの単位のピクセル) / 太字 / 斜体)</summary>
public struct UIFont : IEquatable<UIFont>
{
    public string Family;
    public float  Size;
    public bool   Bold;
    public bool   Italic;

    public const string DefaultFamily = "Meiryo";

    public UIFont()
    {
        Family = DefaultFamily;
        Size   = 24.0f;
    }

    public UIFont(float size, bool bold = false) : this() { Size = size; Bold = bold; }

    public readonly bool Equals(UIFont o) => (Family ?? "") == (o.Family ?? "") && Size == o.Size && Bold == o.Bold && Italic == o.Italic;
    public override readonly bool Equals(object? obj) => obj is UIFont f && Equals(f);
    public override readonly int GetHashCode() => HashCode.Combine(Family, Size, Bold, Italic);
}

/// <summary>
/// 親の Panel の中での置き方 (UE の UPanelSlot。全部の Panel の分を 1 つにまとめた物)
///   CanvasPanel : Anchors / Offsets / Alignment / AutoSize / ZOrder
///   HorizontalBox / VerticalBox / ScrollBox : Padding / Size / FillWeight / 揃え
///   Overlay / Border / Button / SizeBox / ScaleBox / WidgetSwitcher / CheckBox : Padding / 揃え
///   UniformGridPanel : Row / Column / 揃え
/// </summary>
public sealed class UIPanelSlot
{
    // ---- CanvasPanel ----
    public UIAnchors Anchors;
    /// <summary>アンカーが点の時 : Left / Top = 位置、Right / Bottom = 幅 / 高さ。伸びる時 : 親の端からの距離</summary>
    public UIMargin  Offsets = new(0, 0, 100, 30);
    /// <summary>自分のどこを位置に合わせるか (0,0 = 左上 / 0.5,0.5 = 真ん中)</summary>
    public Vector2   Alignment;
    /// <summary>大きさを中身に合わせる (Offsets の幅 / 高さを使わない)</summary>
    public bool      AutoSize;
    /// <summary>描く順 (大きい物が手前)</summary>
    public int       ZOrder;

    // ---- Box / Overlay / 中身を 1 つ持つ物 ----
    public UIMargin  Padding;
    public UISizeRule Size;
    public float     FillWeight = 1.0f;
    public UIHorizontalAlignment HorizontalAlignment;
    public UIVerticalAlignment   VerticalAlignment;

    // ---- UniformGridPanel ----
    public int Row;
    public int Column;

    public UIPanelSlot Clone() => (UIPanelSlot)MemberwiseClone();

    /// <summary>CanvasPanel : 位置 (アンカーが点の時)</summary>
    public Vector2 Position
    {
        get => new(Offsets.Left, Offsets.Top);
        set { Offsets.Left = value.X; Offsets.Top = value.Y; }
    }

    /// <summary>CanvasPanel : 大きさ (アンカーが点の時)</summary>
    public Vector2 SizeValue
    {
        get => new(Offsets.Right, Offsets.Bottom);
        set { Offsets.Right = value.X; Offsets.Bottom = value.Y; }
    }
}

/// <summary>色の便利な値</summary>
public static class UIColors
{
    public static Vector4 White => Vector4.One;
    public static Vector4 Black => new(0, 0, 0, 1);
    public static Vector4 Clear => Vector4.Zero;
    public static Vector4 Rgba(float r, float g, float b, float a = 1.0f) => new(r, g, b, a);

    // Vector4 (0〜1) → 頂点の色 (R8G8B8A8。下位バイトが R。ImGui と同じ)
    internal static uint Pack(Vector4 c, float opacity)
    {
        static uint b(float v) => (uint)Math.Clamp((int)(v * 255.0f + 0.5f), 0, 255);
        return b(c.X) | (b(c.Y) << 8) | (b(c.Z) << 16) | (b(c.W * opacity) << 24);
    }
}
