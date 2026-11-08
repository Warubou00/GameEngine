namespace GameEngine;

// ==========================================================================
// コンポーネントの値に付ける印 (保存 / インスペクター。Unity と同じ名前と考え方。ED1)
//   保存される値 : public なフィールド ([NonSerialized] を付けた物は除く) + [SerializeField] を付けた非 public のフィールド
//   internal / private のフィールド (C++ のハンドルや、System が使う途中の値) は保存しない
//   プロパティは保存しない (値は必ずフィールドに持つ)
// ==========================================================================

/// <summary>非 public のフィールドも保存し、インスペクターに出す</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class SerializeFieldAttribute : Attribute
{
}

/// <summary>保存はするが、インスペクターには出さない</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class HideInInspectorAttribute : Attribute
{
}

/// <summary>インスペクターで、この範囲のスライダーにする (数のフィールド)</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class RangeAttribute : Attribute
{
    public float Min { get; }
    public float Max { get; }
    public RangeAttribute(float min, float max) { Min = min; Max = max; }
}

/// <summary>インスペクターで、名前にマウスを乗せると出る説明</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class TooltipAttribute : Attribute
{
    public string Text { get; }
    public TooltipAttribute(string text) => Text = text;
}

/// <summary>インスペクターで、この int をレイヤーの名前の一覧 (ProjectSettings) から選ぶ (ED6)</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class LayerAttribute : Attribute
{
}

/// <summary>インスペクターで、この uint をレイヤーのビットの組 (名前のチェックの並び) で出す (ED6)</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class LayerMaskAttribute : Attribute
{
}

/// <summary>インスペクターで、この string をアセットのパスとして出す (Project から落とせる。v1.4)</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class AssetPathAttribute : Attribute
{
    public AssetKind Kind { get; }
    public AssetPathAttribute(AssetKind kind) => Kind = kind;
}

/// <summary>インスペクターで、このフィールドの前に見出しを出す</summary>
[AttributeUsage(AttributeTargets.Field)]
public sealed class HeaderAttribute : Attribute
{
    public string Text { get; }
    public HeaderAttribute(string text) => Text = text;
}

/// <summary>
/// コンポーネントの表示名 / 分類 (コンポーネントを足すメニュー)。付けなくても、どの struct もコンポーネントとして保存できる
/// Hidden = true : メニューに出さない (エンジンが付ける物)。DontSave = true : シーンに保存しない (計測用など)
/// </summary>
[AttributeUsage(AttributeTargets.Struct)]
public sealed class ComponentInfoAttribute : Attribute
{
    public string? DisplayName { get; init; }
    public string? Category    { get; init; }
    public bool    Hidden      { get; init; }
    public bool    DontSave    { get; init; }
}
