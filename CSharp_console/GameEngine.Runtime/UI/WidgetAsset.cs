using System.Numerics;
using System.Reflection;
using System.Text;
using System.Text.Json;

namespace GameEngine;

// ==========================================================================
// Widget のアセット (.widget.json。v1.7。UE の Widget Blueprint の Designer の部分)
//   {
//     "format": "TsumugiWidget", "version": 1,
//     "designSize": [1920, 1080],                    ← 基準の解像度 (この大きさで作る。画面に合わせて拡大縮小)
//     "root": { "type": "CanvasPanel", "name": "Root",
//               "props": { 既定と違う値だけ }, "slot": { 既定と違う値だけ }, "children": [ ... ] }
//   }
//   画像は "Assets/..." のパスで持つ (ゲームの書き出しで一緒に入る)
//   1 つの Widget アセットを、いくつのシーン (UIWidget コンポーネント) / コードからでも使える (作る度に別の物ができる)
// ==========================================================================
public sealed class WidgetAsset
{
    public const string Extension = ".widget.json";
    public const string Format = "TsumugiWidget";
    public const int Version = 1;
    public static readonly Vector2 DefaultDesignSize = new(1920, 1080);

    /// <summary>アセットのパス ("Assets/UI/HUD.widget.json")</summary>
    public string Path { get; internal set; }
    public Vector2 DesignSize { get; private set; } = DefaultDesignSize;
    private string _json = "";

    public WidgetAsset(string path, string json)
    {
        Path = path;
        SetJson(json);
    }

    /// <summary>中身を差し替える (ファイルを読み直した時)。前に作った物はそのまま</summary>
    public void SetJson(string json)
    {
        _json = json;
        DesignSize = WidgetSerializer.ReadDesignSize(json);
    }

    public string Json => _json;

    /// <summary>Widget のツリーを新しく作る</summary>
    public UWidget? CreateTree() => WidgetSerializer.Load(_json).Root;

    /// <summary>このアセットの UserWidget を作る (まだ画面には出ない。AddToViewport で出す)</summary>
    public UUserWidget CreateInstance()
    {
        var (root, size) = WidgetSerializer.Load(_json);
        return new UUserWidget(root, size, this);
    }

    public override string ToString() => $"WidgetAsset({Path})";
}

// ==========================================================================
// 画面に出す 1 枚 (UE の UUserWidget)。アセットから作るか、コードで組んだツリーを渡して作る
//   AddToViewport(z) で出す (z の大きい物が手前) / RemoveFromParent で外す / IsVisible で表示を切り替える
// ==========================================================================
public sealed class UUserWidget
{
    public WidgetAsset? Asset { get; }
    public UWidget? Root { get; private set; }
    public Vector2 DesignSize { get; set; }
    public int ZOrder { get; private set; }
    public UIVisibility Visibility { get; set; } = UIVisibility.Visible;
    /// <summary>シーンの UIWidget コンポーネントから作った時、その持ち主 (コードで作った物は Null)</summary>
    public Entity Entity { get; internal set; }
    internal long Order;   // 同じ ZOrder の中の順 (後で出した物が手前)

    public UUserWidget(UWidget? root, Vector2 designSize, WidgetAsset? asset = null)
    {
        Asset = asset;
        DesignSize = designSize.X > 0 && designSize.Y > 0 ? designSize : WidgetAsset.DefaultDesignSize;
        SetRoot(root);
    }

    public string Name => Asset != null ? System.IO.Path.GetFileName(Asset.Path).Replace(WidgetAsset.Extension, "") : Root?.Name ?? "UserWidget";

    public void SetRoot(UWidget? root)
    {
        Root = root;
        if (root != null) foreach (var widget in root.Descendants()) widget.Owner = this;
    }

    /// <summary>見えるか (Collapsed / Hidden でない)。false にすると見えず、マウスも当たらない</summary>
    public bool IsVisible
    {
        get => Visibility is not (UIVisibility.Collapsed or UIVisibility.Hidden);
        set => Visibility = value ? UIVisibility.Visible : UIVisibility.Collapsed;
    }

    public bool IsInViewport => UI.Manager?.Contains(this) ?? false;

    /// <summary>画面に出す (UE の AddToViewport)。もう出ていれば ZOrder だけ変える</summary>
    public void AddToViewport(int zOrder = 0)
    {
        if (UI.Manager is not { } manager) throw new InvalidOperationException("UI is not ready (no Engine).");
        manager.Add(this, zOrder);
    }

    /// <summary>画面から外す (UE の RemoveFromParent)</summary>
    public void RemoveFromParent() => UI.Manager?.Remove(this);

    /// <summary>重なりの順を変える (大きい物が手前)</summary>
    public void SetZOrder(int zOrder)
    {
        if (ZOrder == zOrder) return;
        ZOrder = zOrder;
        UI.Manager?.Resort();
    }

    internal void SetZOrderInternal(int zOrder) => ZOrder = zOrder;

    public UWidget? GetWidget(string name) => Root?.FindWidget(name);
    public T? GetWidget<T>(string name) where T : UWidget => Root?.FindWidget(name) as T;

    public override string ToString() => $"UserWidget({Name}, z {ZOrder}{(IsVisible ? "" : ", hidden")})";
}

// ==========================================================================
// Widget の種類 (アセットの "type" ↔ C# の型)
// ==========================================================================
public static class WidgetTypes
{
    private static readonly Dictionary<string, Type> _byName = new(StringComparer.Ordinal);
    private static readonly Dictionary<Type, string> _names = new();

    static WidgetTypes()
    {
        foreach (var type in typeof(UWidget).Assembly.GetTypes())
            if (type.IsSubclassOf(typeof(UWidget)) && !type.IsAbstract && type.IsPublic) Register(type);
    }

    /// <summary>自分の Widget の型を足す (引数の無いコンストラクターが要る)</summary>
    public static void Register(Type type)
    {
        if (!type.IsSubclassOf(typeof(UWidget)) || type.IsAbstract) throw new ArgumentException($"{type} is not a widget type.");
        var name = type.Name.Length > 1 && type.Name[0] == 'U' && char.IsUpper(type.Name[1]) ? type.Name[1..] : type.Name;
        _byName[name] = type;
        _names[type] = name;
    }

    public static string NameOf(Type type) => _names.TryGetValue(type, out var name) ? name : type.Name;
    public static Type? Find(string name) => _byName.GetValueOrDefault(name);
    public static IEnumerable<(string Name, Type Type)> All => _byName.Select(p => (p.Key, p.Value)).OrderBy(p => p.Key, StringComparer.Ordinal);

    public static UWidget Create(string name) =>
        Find(name) is { } type ? (UWidget)Activator.CreateInstance(type)! : throw new InvalidDataException($"Unknown widget type '{name}'");

    public static T Create<T>(string name = "") where T : UWidget, new()
    {
        var widget = new T();
        widget.Name = string.IsNullOrEmpty(name) ? NameOf(typeof(T)) : name;
        return widget;
    }

    /// <summary>保存する値 (public なフィールド。[NonSerialized] と Name は除く)</summary>
    public static IReadOnlyList<FieldInfo> Fields(Type type)
    {
        lock (_fields)
        {
            if (_fields.TryGetValue(type, out var list)) return list;
            list = type.GetFields(BindingFlags.Public | BindingFlags.Instance)
                       .Where(f => !f.IsDefined(typeof(NonSerializedAttribute), false) && !f.IsInitOnly && f.Name != nameof(UWidget.Name) && f.FieldType != typeof(Delegate) && !typeof(Delegate).IsAssignableFrom(f.FieldType))
                       .OrderBy(f => f.DeclaringType == typeof(UWidget) ? 1 : 0)   // 種類ごとの値を先、共通の値 (Visibility など) を後
                       .ThenBy(f => f.MetadataToken)
                       .ToList();
            _fields[type] = list;
            return list;
        }
    }
    private static readonly Dictionary<Type, IReadOnlyList<FieldInfo>> _fields = new();

    public static IReadOnlyList<FieldInfo> SlotFields { get; } = typeof(UIPanelSlot).GetFields(BindingFlags.Public | BindingFlags.Instance).ToList();
}

// ==========================================================================
// 保存 / 読み込み (既定と違う値だけ書く。読む時は無い値は既定のまま)
// ==========================================================================
public static class WidgetSerializer
{
    private static readonly JsonDocumentOptions Options = new() { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true };

    public static string Save(UWidget? root, Vector2 designSize)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteString("format", WidgetAsset.Format);
            w.WriteNumber("version", WidgetAsset.Version);
            w.WritePropertyName("designSize");
            writeValue(w, designSize);
            if (root != null)
            {
                w.WritePropertyName("root");
                WriteWidget(w, root, includeSlot: false);
            }
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>1 つの Widget (と子孫) を書く</summary>
    public static void WriteWidget(Utf8JsonWriter w, UWidget widget, bool includeSlot = true)
    {
        w.WriteStartObject();
        w.WriteString("type", widget.TypeName);
        w.WriteString("name", widget.Name);
        var defaults = (UWidget)Activator.CreateInstance(widget.GetType())!;
        var wroteProps = false;
        foreach (var field in WidgetTypes.Fields(widget.GetType()))
        {
            var value = field.GetValue(widget);
            if (Equals(value, field.GetValue(defaults))) continue;
            if (!wroteProps) { w.WritePropertyName("props"); w.WriteStartObject(); wroteProps = true; }
            w.WritePropertyName(field.Name);
            writeValue(w, value);
        }
        if (wroteProps) w.WriteEndObject();
        if (includeSlot && widget.Parent != null)
        {
            var slotDefaults = widget.Parent.CreateSlot();
            var wroteSlot = false;
            foreach (var field in WidgetTypes.SlotFields)
            {
                var value = field.GetValue(widget.Slot);
                if (Equals(value, field.GetValue(slotDefaults))) continue;
                if (!wroteSlot) { w.WritePropertyName("slot"); w.WriteStartObject(); wroteSlot = true; }
                w.WritePropertyName(field.Name);
                writeValue(w, value);
            }
            if (wroteSlot) w.WriteEndObject();
        }
        if (widget.Children.Count > 0)
        {
            w.WriteStartArray("children");
            foreach (var child in widget.Children) WriteWidget(w, child);
            w.WriteEndArray();
        }
        w.WriteEndObject();
    }

    public static (UWidget? Root, Vector2 DesignSize) Load(string json)
    {
        using var document = JsonDocument.Parse(json, Options);
        var root = document.RootElement;
        var size = root.TryGetProperty("designSize", out var s) ? (Vector2)readValue(s, typeof(Vector2), Vector2.Zero)! : WidgetAsset.DefaultDesignSize;
        if (size.X <= 0 || size.Y <= 0) size = WidgetAsset.DefaultDesignSize;
        var widget = root.TryGetProperty("root", out var r) && r.ValueKind == JsonValueKind.Object ? ReadWidget(r, null) : null;
        return (widget, size);
    }

    public static Vector2 ReadDesignSize(string json)
    {
        try { return Load(json).DesignSize; }
        catch (JsonException) { return WidgetAsset.DefaultDesignSize; }
    }

    /// <summary>1 つの Widget (と子孫) を読む (parent があれば、その子として足す)</summary>
    public static UWidget ReadWidget(JsonElement e, UPanelWidget? parent)
    {
        var typeName = e.TryGetProperty("type", out var t) ? t.GetString() ?? "" : "";
        UWidget widget;
        try { widget = WidgetTypes.Create(typeName); }
        catch (InvalidDataException)
        {
            Console.WriteLine($"[UI] Unknown widget type '{typeName}' (replaced by Spacer)");
            widget = new USpacer();
        }
        widget.Name = e.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        if (e.TryGetProperty("props", out var props) && props.ValueKind == JsonValueKind.Object)
        {
            foreach (var field in WidgetTypes.Fields(widget.GetType()))
                if (props.TryGetProperty(field.Name, out var value)) field.SetValue(widget, readValue(value, field.FieldType, field.GetValue(widget)));
        }
        if (parent != null)
        {
            var slot = parent.InsertChildAt(parent.ChildrenCount, widget);
            if (slot != null && e.TryGetProperty("slot", out var slotJson) && slotJson.ValueKind == JsonValueKind.Object)
            {
                foreach (var field in WidgetTypes.SlotFields)
                    if (slotJson.TryGetProperty(field.Name, out var value)) field.SetValue(slot, readValue(value, field.FieldType, field.GetValue(slot)));
            }
        }
        if (e.TryGetProperty("children", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            foreach (var child in children.EnumerateArray())
            {
                if (widget is UPanelWidget panel && panel.CanAddMoreChildren) ReadWidget(child, panel);
                else Console.WriteLine($"[UI] {widget} cannot have more children (skipped)");
            }
        }
        return widget;
    }

    /// <summary>Widget (と子孫) の写し (置き方も写す。親には足さない)</summary>
    public static UWidget Clone(UWidget widget)
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream)) WriteWidget(w, widget, includeSlot: false);
        using var document = JsonDocument.Parse(stream.ToArray());
        var copy = ReadWidget(document.RootElement, null);
        copy.Slot = widget.Slot.Clone();
        return copy;
    }

    // ---------------------------------------------------------------------
    // 値
    // ---------------------------------------------------------------------

    private static void writeValue(Utf8JsonWriter w, object? value)
    {
        switch (value)
        {
        case null: w.WriteNullValue(); break;
        case string s: w.WriteStringValue(s); break;
        case bool b: w.WriteBooleanValue(b); break;
        case float f: w.WriteNumberValue(MathF.Round(f, 4)); break;
        case int i: w.WriteNumberValue(i); break;
        case Enum en: w.WriteStringValue(en.ToString()); break;
        case Vector2 v: w.WriteStartArray(); w.WriteNumberValue(v.X); w.WriteNumberValue(v.Y); w.WriteEndArray(); break;
        case Vector4 v: w.WriteStartArray(); w.WriteNumberValue(v.X); w.WriteNumberValue(v.Y); w.WriteNumberValue(v.Z); w.WriteNumberValue(v.W); w.WriteEndArray(); break;
        default:
            if (value.GetType().IsValueType)
            {
                w.WriteStartObject();
                foreach (var field in value.GetType().GetFields(BindingFlags.Public | BindingFlags.Instance))
                {
                    w.WritePropertyName(field.Name);
                    writeValue(w, field.GetValue(value));
                }
                w.WriteEndObject();
            }
            else w.WriteStringValue(value.ToString());
            break;
        }
    }

    private static object? readValue(JsonElement e, Type type, object? current)
    {
        try
        {
            if (type == typeof(string)) return e.ValueKind == JsonValueKind.String ? e.GetString() ?? "" : current;
            if (type == typeof(bool)) return e.ValueKind is JsonValueKind.True or JsonValueKind.False ? e.GetBoolean() : current;
            if (type == typeof(float)) return e.ValueKind == JsonValueKind.Number ? e.GetSingle() : current;
            if (type == typeof(int)) return e.ValueKind == JsonValueKind.Number ? e.GetInt32() : current;
            if (type.IsEnum) return e.ValueKind == JsonValueKind.String && Enum.TryParse(type, e.GetString(), out var en) ? en : current;
            if (type == typeof(Vector2)) return e.ValueKind == JsonValueKind.Array && e.GetArrayLength() >= 2 ? new Vector2(e[0].GetSingle(), e[1].GetSingle()) : current;
            if (type == typeof(Vector4)) return e.ValueKind == JsonValueKind.Array && e.GetArrayLength() >= 4 ? new Vector4(e[0].GetSingle(), e[1].GetSingle(), e[2].GetSingle(), e[3].GetSingle()) : current;
            if (type.IsValueType && e.ValueKind == JsonValueKind.Object)
            {
                var boxed = current ?? Activator.CreateInstance(type)!;
                foreach (var field in type.GetFields(BindingFlags.Public | BindingFlags.Instance))
                    if (e.TryGetProperty(field.Name, out var value)) field.SetValue(boxed, readValue(value, field.FieldType, field.GetValue(boxed)));
                return boxed;
            }
        }
        catch (Exception ex) when (ex is FormatException or InvalidOperationException)
        {
            // 形の違う値は既定のまま
        }
        return current;
    }
}
