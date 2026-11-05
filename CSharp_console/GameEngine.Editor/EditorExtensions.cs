using System.Numerics;
using System.Reflection;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// エディターを広げる (ED7 後半)。ゲームのプロジェクトのエディター用のプロジェクト (Project/Scripts/Editor/GameEditor.csproj) に書く
//   (Unity の Editor フォルダと同じ。ゲーム (Game.dll) はエディターに頼らないので、出来上がったゲーム (ED8) には入らない)
//   - [CustomEditor(typeof(T))] + ComponentEditor<T> : インスペクターを自分で描く / シーンビューにギズモを描く
//   - [EditorWindow("メニュー/名前")] + EditorWindow   : 自分の窓 (メニューから開く)
//   - [MenuItem("メニュー/名前")] (static メソッド)       : メニューの項目
//   - [AssetImporter(".拡張子")] + AssetImporter      : その拡張子のファイルを Assets に置いた / 書き換えた時の処理、Project のタイル
//   GameEditor.dll は Game.dll と一緒にビルドし直して読み直す (.cs を保存すると)
// ==========================================================================

/// <summary>この ComponentEditor を、指定したコンポーネントのインスペクター / ギズモに使う</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class CustomEditorAttribute : Attribute
{
    public Type ComponentType { get; }
    public CustomEditorAttribute(Type componentType) => ComponentType = componentType;
}

/// <summary>
/// コンポーネントのインスペクターとギズモ (1 つの型に 1 つ作られ、描く物ごとに Entity が入れ替わる)
/// 普通は ComponentEditor&lt;T&gt; を継ぐ
/// </summary>
public abstract class ComponentEditor
{
    public Entity Entity { get; internal set; }
    public World  World  => EditorApi.World;
    public Engine Engine => EditorApi.Engine;

    /// <summary>インスペクター。値を変えたら component に入れて true を返す (エディターが書き戻し、元に戻せる)。既定は全部のフィールド</summary>
    public virtual bool OnInspectorGUI(ref object component) => DrawDefaultInspector(ref component);

    /// <summary>シーンビュー : このコンポーネントを持つ全部の物で毎フレーム呼ぶ</summary>
    public virtual void OnDrawGizmos(Gizmos gizmos, object component) { }

    /// <summary>シーンビュー : 選んでいる物だけ</summary>
    public virtual void OnDrawGizmosSelected(Gizmos gizmos, object component) { }

    /// <summary>既定のインスペクター (全部のフィールド) を描く</summary>
    protected bool DrawDefaultInspector(ref object component) => EditorExtensions.DrawDefault!(ref component, Entity);
}

/// <summary>型付きの ComponentEditor (T = コンポーネントの struct)</summary>
public abstract class ComponentEditor<T> : ComponentEditor where T : struct
{
    public sealed override bool OnInspectorGUI(ref object component)
    {
        var value = (T)component;
        var changed = OnInspectorGUI(ref value);
        if (changed) component = value;
        return changed;
    }

    public sealed override void OnDrawGizmos(Gizmos gizmos, object component) => OnDrawGizmos(gizmos, (T)component);
    public sealed override void OnDrawGizmosSelected(Gizmos gizmos, object component) => OnDrawGizmosSelected(gizmos, (T)component);

    public virtual bool OnInspectorGUI(ref T component)
    {
        object boxed = component;
        var changed = DrawDefaultInspector(ref boxed);
        component = (T)boxed;
        return changed;
    }

    public virtual void OnDrawGizmos(Gizmos gizmos, T component) { }
    public virtual void OnDrawGizmosSelected(Gizmos gizmos, T component) { }

    protected bool DrawDefaultInspector(ref T component)
    {
        object boxed = component;
        var changed = base.DrawDefaultInspector(ref boxed);
        component = (T)boxed;
        return changed;
    }
}

/// <summary>自分の窓。MenuPath (例 "Game/Stats") のメニューで開く / 閉じる</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class EditorWindowAttribute : Attribute
{
    public string MenuPath { get; }
    public EditorWindowAttribute(string menuPath) => MenuPath = menuPath;
}

public abstract class EditorWindow
{
    public bool IsOpen { get; set; }
    public virtual string Title => GetType().Name;
    public World  World  => EditorApi.World;
    public Engine Engine => EditorApi.Engine;
    /// <summary>窓の中身 (ImGui で描く)</summary>
    public abstract void OnGUI();
}

/// <summary>メニューの項目 (引数の無い static メソッドに付ける。例 "Game/Create Spinning Box")</summary>
[AttributeUsage(AttributeTargets.Method, Inherited = false)]
public sealed class MenuItemAttribute : Attribute
{
    public string Path { get; }
    public MenuItemAttribute(string path) => Path = path;
}

/// <summary>その拡張子のファイルの取り込み (例 ".palette.txt"。小文字で比べる)</summary>
[AttributeUsage(AttributeTargets.Class, Inherited = false)]
public sealed class AssetImporterAttribute : Attribute
{
    public string[] Extensions { get; }
    public AssetImporterAttribute(params string[] extensions) => Extensions = extensions.Select(e => e.ToLowerInvariant()).ToArray();
}

public abstract class AssetImporter
{
    /// <summary>Project のタイルの文字と色 (ABGR)</summary>
    public virtual string Label => "File";
    public virtual uint   Color => 0xFF606060;
    /// <summary>Assets に置いた / 書き換えた時 (assetPath = "Assets/...")</summary>
    public abstract void OnImport(string assetPath, string fullPath);
    /// <summary>Project でダブルクリックした時</summary>
    public virtual void OnOpen(string assetPath, string fullPath) { }
}

// ==========================================================================
// シーンビューに描く (ImGui の線。ワールドの位置で渡す)
// ==========================================================================
public sealed class Gizmos
{
    private readonly SceneCamera _camera;
    private readonly ImDrawListPtr _draw;
    internal int Count;   // 描いた数 (確認用)

    internal Gizmos(SceneCamera camera, ImDrawListPtr draw)
    {
        _camera = camera;
        _draw = draw;
    }

    public static uint Rgba(float r, float g, float b, float a = 1.0f) =>
        ((uint)(Math.Clamp(a, 0, 1) * 255) << 24) | ((uint)(Math.Clamp(b, 0, 1) * 255) << 16) | ((uint)(Math.Clamp(g, 0, 1) * 255) << 8) | (uint)(Math.Clamp(r, 0, 1) * 255);

    public void Line(Vector3 a, Vector3 b, uint color, float thickness = 1.5f)
    {
        if (!_camera.Project(a, out var pa) || !_camera.Project(b, out var pb)) return;
        _draw.AddLine(pa, pb, color, thickness);
        Count++;
    }

    public void Circle(Vector3 center, Vector3 normal, float radius, uint color, int segments = 48)
    {
        normal = Vector3.Normalize(normal);
        var u = Vector3.Normalize(Vector3.Cross(normal, MathF.Abs(normal.Y) < 0.9f ? Vector3.UnitY : Vector3.UnitX));
        var v = Vector3.Cross(normal, u);
        var previous = center + u * radius;
        for (int i = 1; i <= segments; i++)
        {
            var angle = i * MathF.Tau / segments;
            var point = center + (u * MathF.Cos(angle) + v * MathF.Sin(angle)) * radius;
            Line(previous, point, color);
            previous = point;
        }
    }

    public void WireSphere(Vector3 center, float radius, uint color)
    {
        Circle(center, Vector3.UnitX, radius, color);
        Circle(center, Vector3.UnitY, radius, color);
        Circle(center, Vector3.UnitZ, radius, color);
    }

    public void WireBox(Vector3 center, Quaternion rotation, Vector3 size, uint color)
    {
        Span<Vector3> c = stackalloc Vector3[8];
        for (int i = 0; i < 8; i++)
            c[i] = center + Vector3.Transform(new Vector3((i & 1) != 0 ? 0.5f : -0.5f, (i & 2) != 0 ? 0.5f : -0.5f, (i & 4) != 0 ? 0.5f : -0.5f) * size, rotation);
        for (int i = 0; i < 8; i++)
            for (int bit = 1; bit < 8; bit <<= 1)
                if ((i & bit) == 0) Line(c[i], c[i | bit], color);
    }

    public void Label(Vector3 position, string text, uint color = 0xFFFFFFFF)
    {
        if (!_camera.Project(position, out var p)) return;
        _draw.AddText(p, color, text);
        Count++;
    }
}

// ==========================================================================
// エディターの操作 (拡張から使う)
// ==========================================================================
public static class EditorApi
{
    internal static EditorApp? App;

    public static Engine Engine => App!.Engine;
    public static World  World  => App!.Engine.World;
    public static AssetDatabase Assets => App!.Database;

    /// <summary>選んでいる物 (入れると選ぶ)</summary>
    public static Entity Selected
    {
        get => App!.Selected;
        set => App!.Selected = value;
    }

    /// <summary>エディターのカメラの位置と前の向き (物を置く所を決める時など)</summary>
    public static Vector3 CameraPosition => App!.EditorCameraPosition;
    public static Vector3 CameraForward  => App!.EditorCameraForward;

    /// <summary>名前の付いたエンティティを作る (Transform 付き。元に戻せる。作ってから足したコンポーネントも一緒に戻る)</summary>
    public static Entity CreateEntity(string name, Vector3 position, Action<Entity>? setup = null) => App!.CreateEntityWith(name, position, setup);

    /// <summary>コンポーネントの値を変える (元に戻せる)</summary>
    public static void Modify<T>(Entity entity, Func<T, T> change) where T : struct => App!.ModifyComponent(entity, change);
}

// ==========================================================================
// 読み込んだ拡張の一覧 (エディターの中だけ)。ゲームのコードを読み直す前に Clear する (古い型を持たないように)
// ==========================================================================
internal static class EditorExtensions
{
    internal delegate bool DrawDefaultInspector(ref object component, Entity owner);
    internal static DrawDefaultInspector? DrawDefault;

    public static readonly Dictionary<Type, ComponentEditor> Editors = new();
    public static readonly List<(string Path, EditorWindow Window)> Windows = new();
    public static readonly List<(string Path, MethodInfo Method)> MenuItems = new();
    public static readonly List<(string[] Extensions, AssetImporter Importer)> Importers = new();

    public static int Count => Editors.Count + Windows.Count + MenuItems.Count + Importers.Count;

    public static void Scan(Assembly assembly)
    {
        foreach (var type in assembly.GetTypes())
        {
            try
            {
                if (type.GetCustomAttribute<CustomEditorAttribute>() is { } custom && typeof(ComponentEditor).IsAssignableFrom(type))
                    Editors[custom.ComponentType] = (ComponentEditor)Activator.CreateInstance(type)!;
                if (type.GetCustomAttribute<EditorWindowAttribute>() is { } window && typeof(EditorWindow).IsAssignableFrom(type))
                    Windows.Add((window.MenuPath, (EditorWindow)Activator.CreateInstance(type)!));
                if (type.GetCustomAttribute<AssetImporterAttribute>() is { } importer && typeof(AssetImporter).IsAssignableFrom(type))
                    Importers.Add((importer.Extensions, (AssetImporter)Activator.CreateInstance(type)!));
                foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic))
                {
                    if (method.GetCustomAttribute<MenuItemAttribute>() is { } item && method.GetParameters().Length == 0) MenuItems.Add((item.Path, method));
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Editor] Cannot use {type.FullName} : {(ex.InnerException ?? ex).Message}");
            }
        }
        Console.WriteLine($"[Editor] Extensions : {Editors.Count} inspectors, {Windows.Count} windows, {MenuItems.Count} menu items, {Importers.Count} importers");
    }

    public static void Clear()
    {
        Editors.Clear();
        Windows.Clear();
        MenuItems.Clear();
        Importers.Clear();
    }

    public static ComponentEditor? EditorFor(Type componentType) => Editors.TryGetValue(componentType, out var editor) ? editor : null;

    public static AssetImporter? ImporterFor(string path)
    {
        var name = path.ToLowerInvariant();
        foreach (var (extensions, importer) in Importers)
            foreach (var extension in extensions)
                if (name.EndsWith(extension)) return importer;
        return null;
    }
}
