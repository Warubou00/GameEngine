using System.Reflection;

namespace GameEngine;

// ==========================================================================
// コンポーネントの型の情報 (ED1)
//   型ごとに「保存する / インスペクターに出すフィールド」の一覧を 1 回だけ作って持つ
//   (リフレクションは保存 / 読み込み / エディターの時だけ。ゲームの毎フレームの処理では使わない)
//   ユーザーのコンポーネントも、struct を書いて World に足すだけで保存できる (登録は要らない)。
//   読み込みは型の名前 (FullName) で探す。読み込み済みのアセンブリから見つける
// ==========================================================================
public sealed class ComponentField
{
    public string    Name      { get; }
    public Type      FieldType { get; }
    public FieldInfo Info      { get; }
    public bool      HideInInspector { get; }
    public RangeAttribute? Range  { get; }
    public string?   Tooltip   { get; }
    public string?   Header    { get; }
    public bool      IsLayer     { get; }   // [Layer] (ED6)
    public bool      IsLayerMask { get; }   // [LayerMask] (ED6)

    internal ComponentField(FieldInfo info)
    {
        Info            = info;
        Name            = info.Name;
        FieldType       = info.FieldType;
        HideInInspector = info.GetCustomAttribute<HideInInspectorAttribute>() != null;
        Range           = info.GetCustomAttribute<RangeAttribute>();
        Tooltip         = info.GetCustomAttribute<TooltipAttribute>()?.Text;
        Header          = info.GetCustomAttribute<HeaderAttribute>()?.Text;
        IsLayer         = info.GetCustomAttribute<LayerAttribute>() != null;
        IsLayerMask     = info.GetCustomAttribute<LayerMaskAttribute>() != null;
    }

    public object? GetValue(object boxedComponent) => Info.GetValue(boxedComponent);
    public void    SetValue(object boxedComponent, object? value) => Info.SetValue(boxedComponent, value);
    public override string ToString() => $"{Name} : {FieldType.Name}";
}

public sealed class ComponentType
{
    public Type   Type        { get; }
    public string TypeName    { get; }   // 保存に使う名前 (FullName)
    public string DisplayName { get; }
    public string Category    { get; }
    public bool   Hidden      { get; }
    public bool   DontSave    { get; }
    public IReadOnlyList<ComponentField> Fields { get; }

    internal ComponentType(Type type)
    {
        Type     = type;
        TypeName = type.FullName ?? type.Name;
        var info = type.GetCustomAttribute<ComponentInfoAttribute>();
        DisplayName = info?.DisplayName ?? type.Name;
        Category    = info?.Category ?? "";
        Hidden      = info?.Hidden ?? false;
        DontSave    = info?.DontSave ?? false;
        Fields      = SerializedFields(type);
    }

    // 既定の値 (引数なしのコンストラクターがあれば、それを通した値。Rigidbody の Mass = 1 など)
    public object CreateDefault() => Activator.CreateInstance(Type)!;

    private static readonly Dictionary<Type, IReadOnlyList<ComponentField>> _fieldCache = new();

    // そのアセンブリの型のフィールドの情報を捨てる (ED7)
    internal static void ForgetFields(Assembly assembly)
    {
        lock (_fieldCache)
        {
            foreach (var type in _fieldCache.Keys.Where(t => t.Assembly == assembly).ToList()) _fieldCache.Remove(type);
        }
    }

    // 保存するフィールド (宣言の順。入れ子の struct にも使う。型ごとに 1 回だけ作る)
    public static IReadOnlyList<ComponentField> SerializedFields(Type type)
    {
        lock (_fieldCache)
        {
            if (_fieldCache.TryGetValue(type, out var cached)) return cached;
            var fields = buildFields(type);
            _fieldCache[type] = fields;
            return fields;
        }
    }

    private static List<ComponentField> buildFields(Type type)
    {
        var list = new List<ComponentField>();
        foreach (var field in type.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic))
        {
            if (field.IsInitOnly || field.IsLiteral) continue;   // readonly / const は保存しない
            if (field.IsDefined(typeof(NonSerializedAttribute), false)) continue;
            if (!field.IsPublic && !field.IsDefined(typeof(SerializeFieldAttribute), false)) continue;
            if (field.Name.Contains('<')) continue;               // 自動プロパティの裏のフィールド
            list.Add(new ComponentField(field));
        }
        list.Sort((a, b) => a.Info.MetadataToken.CompareTo(b.Info.MetadataToken));
        return list;
    }

    public override string ToString() => TypeName;
}

public static class ComponentRegistry
{
    private static readonly Dictionary<Type, ComponentType>   _byType = new();
    private static readonly Dictionary<string, ComponentType> _byName = new(StringComparer.Ordinal);
    private static Dictionary<string, Type>? _knownTypes;   // 名前 → struct の型 (読み込み済みのアセンブリから)
    // 捨てたアセンブリ (ED7 : 読み直した古いゲームの DLL。解放されるまで GetAssemblies に残るので、見ないようにする)
    // 弱い参照で持つ (強く持つと解放されない)
    private static readonly List<WeakReference<Assembly>> _retired = new();

    /// <summary>
    /// アセンブリの型を忘れる (ED7 : ゲームの DLL を読み直す前)。型の情報 / 名前の表 / フィールドの情報から外し、以後は探さない
    /// </summary>
    public static void Forget(Assembly assembly)
    {
        lock (_lock)
        {
            foreach (var type in _byType.Keys.Where(t => t.Assembly == assembly).ToList())
            {
                _byName.Remove(_byType[type].TypeName);
                _byType.Remove(type);
            }
            _retired.Add(new WeakReference<Assembly>(assembly));
            _knownTypes = null;
        }
        ComponentType.ForgetFields(assembly);
    }

    private static bool isRetired(Assembly assembly)
    {
        foreach (var weak in _retired) if (weak.TryGetTarget(out var retired) && retired == assembly) return true;
        return false;
    }
    private static readonly object _lock = new();

    public static ComponentType Get(Type type)
    {
        lock (_lock)
        {
            if (_byType.TryGetValue(type, out var found)) return found;
            if (!type.IsValueType || type.IsPrimitive || type.IsEnum) throw new ArgumentException($"{type} is not a component (struct).");
            var created = new ComponentType(type);
            _byType.Add(type, created);
            _byName[created.TypeName] = created;
            return created;
        }
    }

    public static ComponentType Get<T>() where T : struct => Get(typeof(T));

    /// <summary>保存した名前から型を探す (無ければ null。後から読み込んだアセンブリも探し直す)</summary>
    public static ComponentType? Find(string typeName)
    {
        lock (_lock)
        {
            if (_byName.TryGetValue(typeName, out var found)) return found;
        }
        var type = findType(typeName, rescan: false) ?? findType(typeName, rescan: true);
        return type == null ? null : Get(type);
    }

    /// <summary>コンポーネントの一覧 (IComponent を付けた public な struct。読み込み済みのアセンブリから。コンポーネントを足すメニュー用)</summary>
    public static IEnumerable<ComponentType> KnownComponents(bool rescan = false)
    {
        var names = knownTypes(rescan);
        foreach (var type in names.Values.OrderBy(t => t.FullName, StringComparer.Ordinal))
        {
            if (typeof(IComponent).IsAssignableFrom(type)) yield return Get(type);
        }
    }

    private static Type? findType(string typeName, bool rescan) =>
        knownTypes(rescan).TryGetValue(typeName, out var type) ? type : null;

    private static Dictionary<string, Type> knownTypes(bool rescan)
    {
        lock (_lock)
        {
            if (_knownTypes != null && !rescan) return _knownTypes;
            var runtime = typeof(World).Assembly;
            var map = new Dictionary<string, Type>(StringComparer.Ordinal);
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                // エンジンと、エンジンを参照するアセンブリ (ゲームのコード) だけを見る。捨てたアセンブリは見ない (ED7)
                if (assembly != runtime && !assembly.GetReferencedAssemblies().Any(r => r.Name == runtime.GetName().Name)) continue;
                if (isRetired(assembly)) continue;
                Type[] types;
                try { types = assembly.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray()!; }
                foreach (var type in types)
                {
                    if (!type.IsValueType || type.IsEnum || type.IsPrimitive || type.IsGenericTypeDefinition || !type.IsPublic) continue;
                    if (type.FullName != null) map[type.FullName] = type;
                }
            }
            _knownTypes = map;
            return map;
        }
    }
}
