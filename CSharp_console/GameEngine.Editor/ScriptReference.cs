using System.Diagnostics;
using System.Globalization;
using System.Numerics;
using System.Reflection;
using System.Text;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// スクリプトリファレンス (ED9。Help > Script Reference / インスペクターのコンポーネントの右クリック > Open Reference)
//   左 : 型の一覧 (検索)。Components (エンジン / ゲーム) / Systems / Engine API / Editor API / Game (Editor)
//   右 : 選んだ型の
//     - 種類 / 名前空間 / アセンブリ / 継承元 (object → ValueType → 型 など) / 実装しているインターフェイス
//     - 説明 (ソースのコメント。SourceDocs) / 使い方 (型から作ったコードの例。コピーできる)
//     - フィールド (型 / 既定の値 / 説明) / プロパティ / メソッド / enum の値 / この型を継ぐ・実装する型
//     - 今のシーンで持っている物の数 (選べる) / ソースを開く
//   ゲームの型は Type を持ち続けない (名前で覚えて毎フレーム引く)。持つと、ゲームの DLL を読み直した時に古い DLL が解放されない (ED7)
// ==========================================================================
internal sealed class ScriptReference
{
    private readonly EditorApp _app;
    private readonly SourceDocs _engineDocs = new();   // エンジン + エディター (埋め込み)
    private SourceDocs _gameDocs = new();               // Project/Scripts (ファイル)
    private readonly string _scriptsFolder;
    private readonly string? _repoRoot;                 // リポジトリ (エンジンのソースを開く。無ければ開けない)

    private sealed record Entry(string Key, string Group, string Name);
    private List<Entry> _entries = new();
    private int _builtForLoad = -1;
    private string _selected = "";           // Key ("R:型の名前" / "E:" / "G:" / "GE:")
    private readonly List<string> _history = new();
    private string _search = "";
    private bool _scrollToSelected;

    private static readonly Assembly RuntimeAssembly = typeof(World).Assembly;
    private static readonly Assembly EditorAssembly  = typeof(ScriptReference).Assembly;

    public int EntryCount => _entries.Count;
    public string Selected => _selected;

    public ScriptReference(EditorApp app, string scriptsFolder)
    {
        _app = app;
        _scriptsFolder = scriptsFolder;
        _engineDocs.AddEmbedded(EditorAssembly, "RuntimeSource/");
        _engineDocs.AddEmbedded(EditorAssembly, "EditorSource/");
        for (var dir = DotnetTool.IsPackaged ? null : new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)   // Package のエディターは埋め込んだソースだけ
            if (File.Exists(Path.Combine(dir.FullName, "GameEngine.sln"))) { _repoRoot = dir.FullName; break; }
    }

    // ---------------------------------------------------------------------
    // 型の一覧 (ゲームの DLL を読み直したら作り直す)
    // ---------------------------------------------------------------------

    private void rebuild()
    {
        _builtForLoad = _app.Scripts.LoadCount;
        _gameDocs = new SourceDocs();
        _gameDocs.AddFolder(_scriptsFolder);
        var entries = new List<Entry>();
        void add(string prefix, Type type, string group) => entries.Add(new Entry(prefix + type.FullName, group, nameOf(type)));

        foreach (var type in publicTypes(RuntimeAssembly))
        {
            if (typeof(IComponent).IsAssignableFrom(type) && type.IsValueType)
            {
                var info = ComponentRegistry.Get(type);
                add("R:", type, "Components/Engine" + (info.Category.Length > 0 ? "/" + info.Category : ""));
            }
            else if (type == typeof(ISystem) || type == typeof(IFixedSystem) || type == typeof(GameSystemAttribute) || type == typeof(IComponent)) add("R:", type, "Basics");
            else add("R:", type, "Engine API");
        }
        foreach (var type in publicTypes(EditorAssembly)) add("E:", type, "Editor API");
        if (_app.Scripts.Assembly is { } game)
        {
            foreach (var type in publicTypes(game))
            {
                if (typeof(IComponent).IsAssignableFrom(type) && type.IsValueType) add("G:", type, "Components/Game");
                else if (type.IsDefined(typeof(GameSystemAttribute), false)) add("G:", type, "Systems/Game");
                else add("G:", type, "Game");
            }
        }
        if (_app.Scripts.EditorAssembly is { } gameEditor)
            foreach (var type in publicTypes(gameEditor)) add("GE:", type, "Game (Editor)");
        _entries = entries.OrderBy(e => groupOrder(e.Group)).ThenBy(e => e.Group, StringComparer.Ordinal).ThenBy(e => e.Name, StringComparer.OrdinalIgnoreCase).ToList();
        if (_selected.Length == 0 || resolve(_selected) == null) _selected = "R:" + typeof(IComponent).FullName;
    }

    private static int groupOrder(string group) => group switch
    {
        "Basics" => 0,
        _ when group.StartsWith("Components/Engine") => 1,
        "Components/Game" => 2,
        "Systems/Game" => 3,
        "Game" => 4,
        "Engine API" => 5,
        "Editor API" => 6,
        _ => 7,
    };

    private static IEnumerable<Type> publicTypes(Assembly assembly)
    {
        Type[] types;
        try { types = assembly.GetTypes(); }
        catch (ReflectionTypeLoadException ex) { types = ex.Types.Where(t => t != null).ToArray()!; }
        return types.Where(t => (t.IsPublic || t.IsNestedPublic) && !t.Name.StartsWith('<') && !typeof(Delegate).IsAssignableFrom(t));
    }

    // Key → Type (ゲームの型は今の DLL から引く)
    private Type? resolve(string key)
    {
        var colon = key.IndexOf(':');
        if (colon < 0) return null;
        var name = key[(colon + 1)..];
        var assembly = key[..colon] switch
        {
            "R" => RuntimeAssembly,
            "E" => EditorAssembly,
            "G" => _app.Scripts.Assembly,
            "GE" => _app.Scripts.EditorAssembly,
            _ => null,
        };
        return assembly?.GetType(name);
    }

    private string? keyOf(Type type)
    {
        var name = type.IsGenericType && !type.IsGenericTypeDefinition ? type.GetGenericTypeDefinition().FullName : type.FullName;
        if (name == null) return null;
        var key = type.Assembly == RuntimeAssembly ? "R:" : type.Assembly == EditorAssembly ? "E:" :
                  type.Assembly == _app.Scripts.Assembly ? "G:" : type.Assembly == _app.Scripts.EditorAssembly ? "GE:" : null;
        return key == null ? null : key + name;
    }

    private SourceDocs.TypeDoc? docOf(Type type) => (type.Assembly == RuntimeAssembly || type.Assembly == EditorAssembly ? _engineDocs : _gameDocs).Find(type);

    /// <summary>型を選んで開く (インスペクターの Open Reference / テスト)</summary>
    public bool Show(Type type)
    {
        if (_builtForLoad != _app.Scripts.LoadCount) rebuild();
        var key = keyOf(type);
        if (key == null || _entries.All(e => e.Key != key)) return false;
        select(key);
        _scrollToSelected = true;
        return true;
    }

    private void select(string key)
    {
        if (key == _selected) return;
        if (_selected.Length > 0) _history.Add(_selected);
        if (_history.Count > 50) _history.RemoveAt(0);
        _selected = key;
    }

    // ---------------------------------------------------------------------
    // 窓
    // ---------------------------------------------------------------------

    public void Draw(ref bool open)
    {
        if (!open) return;
        if (_builtForLoad != _app.Scripts.LoadCount) rebuild();
        ImGui.SetNextWindowSize(new Vector2(980, 680), ImGuiCond.FirstUseEver);
        if (!ImGui.Begin("Script Reference", ref open)) { ImGui.End(); return; }

        // ---------- 左 : 一覧 ----------
        if (ImGui.BeginChild("##list", new Vector2(260, 0), ImGuiChildFlags.Borders | ImGuiChildFlags.ResizeX))
        {
            ImGui.SetNextItemWidth(-1);
            ImGui.InputTextWithHint("##search", "Search types / members", ref _search, 128);
            var filter = _search.Trim();
            string? currentGroup = null;
            var groupOpen = false;
            foreach (var entry in _entries)
            {
                if (filter.Length > 0 && !matches(entry, filter)) continue;
                if (entry.Group != currentGroup)
                {
                    if (groupOpen) ImGui.TreePop();
                    currentGroup = entry.Group;
                    var flags = filter.Length > 0 || !entry.Group.Contains("API") || _selected.StartsWith(entry.Key[..2]) && _entries.Any(e => e.Group == entry.Group && e.Key == _selected)
                        ? ImGuiTreeNodeFlags.DefaultOpen : ImGuiTreeNodeFlags.None;
                    if (filter.Length > 0) ImGui.SetNextItemOpen(true);
                    groupOpen = ImGui.TreeNodeEx($"{entry.Group} ({_entries.Count(e => e.Group == entry.Group)})##{entry.Group}", flags);
                }
                if (!groupOpen) continue;
                if (ImGui.Selectable($"{entry.Name}##{entry.Key}", entry.Key == _selected)) select(entry.Key);
                if (entry.Key == _selected && _scrollToSelected) { ImGui.SetScrollHereY(0.3f); _scrollToSelected = false; }
            }
            if (groupOpen) ImGui.TreePop();
        }
        ImGui.EndChild();
        ImGui.SameLine();

        // ---------- 右 : 中身 ----------
        if (ImGui.BeginChild("##detail", Vector2.Zero, ImGuiChildFlags.None))
        {
            var type = resolve(_selected);
            if (type == null) ImGui.TextDisabled("Select a type");
            else drawType(type);
        }
        ImGui.EndChild();
        ImGui.End();
    }

    // 検索 : 型の名前 / グループ / メンバーの名前
    private bool matches(Entry entry, string filter)
    {
        if (entry.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) || entry.Group.Contains(filter, StringComparison.OrdinalIgnoreCase)) return true;
        var type = resolve(entry.Key);
        return type != null && type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Any(m => m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase));
    }

    private void drawType(Type type)
    {
        var doc = docOf(type);
        var isComponent = typeof(IComponent).IsAssignableFrom(type) && type.IsValueType;

        // ---------- 見出し ----------
        if (_history.Count > 0)
        {
            if (ImGui.SmallButton("< Back")) { _selected = _history[^1]; _history.RemoveAt(_history.Count - 1); _scrollToSelected = true; return; }
            ImGui.SameLine();
        }
        ImGui.TextColored(new Vector4(0.55f, 0.8f, 1.0f, 1.0f), nameOf(type));
        ImGui.SameLine();
        ImGui.TextDisabled(kindOf(type));
        if (isComponent && ComponentRegistry.Get(type) is { } info && info.DisplayName != type.Name)
        {
            ImGui.SameLine();
            ImGui.TextDisabled($"(\"{info.DisplayName}\" in the inspector)");
        }
        ImGui.TextDisabled($"namespace {type.Namespace}   /   {type.Assembly.GetName().Name}.dll");

        // 継承元
        ImGui.Text("Inherits :");
        var chain = new List<Type>();
        for (var t = type; t != null; t = t.BaseType) chain.Add(t);
        chain.Reverse();
        for (int i = 0; i < chain.Count; i++)
        {
            ImGui.SameLine();
            if (i > 0) { ImGui.TextDisabled("->"); ImGui.SameLine(); }
            typeLink(chain[i], chain[i] == type);
        }
        var interfaces = type.GetInterfaces().Where(i => !(type.BaseType?.GetInterfaces().Contains(i) ?? false)).OrderBy(i => i.Namespace == "GameEngine" ? 0 : 1).ToList();
        if (interfaces.Count > 0)
        {
            ImGui.Text("Implements :");
            foreach (var i in interfaces) { ImGui.SameLine(); typeLink(i, false); }
        }
        foreach (var attribute in type.GetCustomAttributesData().Where(a => a.AttributeType.Namespace?.StartsWith("GameEngine") == true))
        {
            ImGui.TextDisabled("Attribute :");
            ImGui.SameLine();
            typeLink(attribute.AttributeType, false);
            var args = string.Join(", ", attribute.ConstructorArguments.Select(a => literal(a.Value)).Concat(attribute.NamedArguments.Select(a => $"{a.MemberName} = {literal(a.TypedValue.Value)}")));
            if (args.Length > 0) { ImGui.SameLine(); ImGui.TextDisabled($"({args})"); }
        }

        // ソース / シーン
        if (doc != null)
        {
            var file = sourceFile(doc);
            ImGui.TextDisabled($"Source : {Path.GetFileName(doc.Source.Replace('\\', '/'))}:{doc.Line}");
            if (file != null)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Open Source")) openFile(file);
            }
        }
        if (isComponent)
        {
            var pool = _app.Engine.World.GetPool(type);
            ImGui.SameLine();
            ImGui.TextDisabled($"   In this scene : {pool.Count}");
            if (pool.Count > 0)
            {
                ImGui.SameLine();
                if (ImGui.SmallButton("Select"))
                {
                    var world = _app.Engine.World;
                    var entities = world.Entities.Where(e => pool.Has(e.Id)).ToList();
                    var index = entities.IndexOf(_app.Selected);
                    _app.Selected = entities[(index + 1) % entities.Count];   // 押すたびに次の物
                }
            }
        }
        ImGui.Separator();

        // ---------- 説明 ----------
        if (doc is { Summary.Length: > 0 }) { ImGui.PushTextWrapPos(); ImGui.TextUnformatted(doc.Summary); ImGui.PopTextWrapPos(); }
        else ImGui.TextDisabled("(no description in the source)");

        // ---------- 使い方 ----------
        var usage = usageOf(type);
        if (usage.Length > 0)
        {
            ImGui.SeparatorText("Usage");
            if (ImGui.SmallButton("Copy##usage")) ImGui.SetClipboardText(usage);
            var lines = usage.Count(c => c == '\n') + 1;
            ImGui.PushStyleColor(ImGuiCol.ChildBg, new Vector4(0.08f, 0.09f, 0.11f, 1.0f));
            if (ImGui.BeginChild("##usage", new Vector2(0, lines * ImGui.GetTextLineHeightWithSpacing() + 12 + ImGui.GetStyle().ScrollbarSize), ImGuiChildFlags.Borders, ImGuiWindowFlags.HorizontalScrollbar))
                ImGui.TextUnformatted(usage);
            ImGui.EndChild();
            ImGui.PopStyleColor();
        }

        // ---------- メンバー ----------
        if (type.IsEnum) drawEnum(type, doc);
        else
        {
            drawFields(type, doc, isComponent);
            drawProperties(type, doc);
            drawMethods(type, doc);
        }

        // ---------- この型を継ぐ / 実装する型 ----------
        if (!type.IsSealed && !type.IsValueType)
        {
            var derived = _entries.Select(e => resolve(e.Key)).Where(t => t != null && t != type && isDerived(t, type)).Cast<Type>().ToList();
            if (derived.Count > 0)
            {
                ImGui.SeparatorText(type.IsInterface ? $"Implemented by ({derived.Count})" : typeof(Attribute).IsAssignableFrom(type) ? $"Used on ({derived.Count})" : $"Derived types ({derived.Count})");
                drawTypeList(derived);
            }
        }
        if (typeof(Attribute).IsAssignableFrom(type) && type.IsSealed)
        {
            var users = _entries.Select(e => resolve(e.Key)).Where(t => t != null && t.IsDefined(type, false)).Cast<Type>().ToList();
            if (users.Count > 0) { ImGui.SeparatorText($"Used on ({users.Count})"); drawTypeList(users); }
        }
    }

    private static bool isDerived(Type candidate, Type baseType)
    {
        if (baseType.IsGenericTypeDefinition)
        {
            for (var t = candidate.BaseType; t != null; t = t.BaseType)
                if (t.IsGenericType && t.GetGenericTypeDefinition() == baseType) return true;
            return false;
        }
        return baseType.IsAssignableFrom(candidate);
    }

    private void drawTypeList(List<Type> types)
    {
        var first = true;
        foreach (var t in types.OrderBy(t => t.Name, StringComparer.OrdinalIgnoreCase))
        {
            if (!first) ImGui.SameLine();
            if (!first && ImGui.GetContentRegionAvail().X < 140) ImGui.NewLine();
            typeLink(t, false);
            first = false;
        }
    }

    // 型の名前 (一覧にあれば押すと開く)
    private void typeLink(Type type, bool current)
    {
        var key = keyOf(type);
        var name = nameOf(type);
        if (current) { ImGui.TextColored(new Vector4(0.55f, 0.8f, 1.0f, 1.0f), name); return; }
        if (key != null && _entries.Any(e => e.Key == key))
        {
            ImGui.PushStyleColor(ImGuiCol.Text, new Vector4(0.45f, 0.75f, 1.0f, 1.0f));
            ImGui.PushStyleColor(ImGuiCol.Button, Vector4.Zero);
            if (ImGui.SmallButton($"{name}##{key}")) { select(key); _scrollToSelected = true; }
            ImGui.PopStyleColor(2);
        }
        else ImGui.TextDisabled(name);
    }

    // ---------------------------------------------------------------------
    // メンバー
    // ---------------------------------------------------------------------

    private void drawFields(Type type, SourceDocs.TypeDoc? doc, bool isComponent)
    {
        var fields = type.GetFields(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(f => !f.IsSpecialName).ToList();
        if (fields.Count == 0) return;
        object? defaults = null;
        if (type.IsValueType && !type.IsEnum) { try { defaults = Activator.CreateInstance(type); } catch (Exception) { } }
        var serialized = isComponent ? ComponentRegistry.Get(type).Fields.ToDictionary(f => f.Name) : new Dictionary<string, ComponentField>();
        ImGui.SeparatorText($"Fields ({fields.Count})");
        if (!ImGui.BeginTable("##fields", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable)) return;
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 150);
        ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 120);
        ImGui.TableSetupColumn("Default", ImGuiTableColumnFlags.WidthFixed, 110);
        ImGui.TableSetupColumn("Description", ImGuiTableColumnFlags.WidthStretch);
        ImGui.TableHeadersRow();
        foreach (var field in fields)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted((field.IsStatic ? (field.IsLiteral ? "const " : "static ") : "") + field.Name);
            ImGui.TableNextColumn();
            typeLink(field.FieldType, false);
            ImGui.TableNextColumn();
            object? value = null;
            try { value = field.IsStatic ? field.GetValue(null) : defaults != null ? field.GetValue(defaults) : null; } catch (Exception) { }
            ImGui.TextDisabled(field.IsStatic || defaults != null ? shortValue(value) : "");
            ImGui.TableNextColumn();
            var notes = new List<string>();
            if (doc != null && doc.Members.TryGetValue(field.Name, out var text)) notes.Add(text);
            if (serialized.TryGetValue(field.Name, out var info))
            {
                if (info.Tooltip != null && !notes.Contains(info.Tooltip)) notes.Add(info.Tooltip);
                if (info.Range != null) notes.Add(FormattableString.Invariant($"[Range {info.Range.Min} .. {info.Range.Max}]"));
                if (info.HideInInspector) notes.Add("(hidden in the inspector)");
            }
            else if (isComponent && !field.IsStatic) notes.Add("(not saved / not in the inspector)");
            ImGui.PushTextWrapPos();
            ImGui.TextUnformatted(string.Join("\n", notes));
            ImGui.PopTextWrapPos();
        }
        ImGui.EndTable();
    }

    private void drawProperties(Type type, SourceDocs.TypeDoc? doc)
    {
        var properties = type.GetProperties(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly).Where(p => p.GetIndexParameters().Length == 0).ToList();
        if (properties.Count == 0) return;
        ImGui.SeparatorText($"Properties ({properties.Count})");
        if (!ImGui.BeginTable("##properties", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.Resizable)) return;
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 170);
        ImGui.TableSetupColumn("Type", ImGuiTableColumnFlags.WidthFixed, 140);
        ImGui.TableSetupColumn("Description", ImGuiTableColumnFlags.WidthStretch);
        foreach (var property in properties)
        {
            var getter = property.GetMethod is { IsPublic: true };
            var setter = property.SetMethod is { IsPublic: true };
            var isStatic = (property.GetMethod ?? property.SetMethod)?.IsStatic == true;
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted((isStatic ? "static " : "") + property.Name);
            ImGui.TableNextColumn();
            typeLink(property.PropertyType, false);
            ImGui.SameLine();
            ImGui.TextDisabled(getter && setter ? "{get; set;}" : getter ? "{get;}" : "{set;}");
            ImGui.TableNextColumn();
            ImGui.PushTextWrapPos();
            ImGui.TextUnformatted(doc != null && doc.Members.TryGetValue(property.Name, out var text) ? text : "");
            ImGui.PopTextWrapPos();
        }
        ImGui.EndTable();
    }

    private void drawMethods(Type type, SourceDocs.TypeDoc? doc)
    {
        var methods = type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
            .Where(m => !m.IsSpecialName && m.GetBaseDefinition().DeclaringType == m.DeclaringType || !m.IsSpecialName && m.DeclaringType!.IsAbstract)
            .Where(m => m.Name is not ("ToString" or "Equals" or "GetHashCode" or "Deconstruct" or "<Clone>$" or "PrintMembers"))
            .OrderBy(m => m.IsStatic ? 0 : 1).ThenBy(m => m.Name, StringComparer.Ordinal).ToList();
        var constructors = type.GetConstructors().Where(c => c.GetParameters().Length > 0).ToList();
        if (methods.Count + constructors.Count == 0) return;
        ImGui.SeparatorText($"Methods ({methods.Count + constructors.Count})");
        foreach (var constructor in constructors)
            ImGui.TextUnformatted($"new {nameOf(type)}({parameters(constructor)})");
        foreach (var method in methods)
        {
            var signature = $"{(method.IsStatic ? "static " : "")}{(method.IsAbstract && !type.IsInterface ? "abstract " : method.IsVirtual && !method.IsFinal && !type.IsInterface ? "virtual " : "")}" +
                            $"{nameOf(method.ReturnType)} {method.Name}{genericArguments(method)}({parameters(method)})";
            ImGui.TextUnformatted(signature);
            if (doc != null && doc.Members.TryGetValue(method.Name, out var text))
            {
                ImGui.Indent();
                ImGui.PushTextWrapPos();
                ImGui.TextDisabled(text);
                ImGui.PopTextWrapPos();
                ImGui.Unindent();
            }
        }
    }

    private void drawEnum(Type type, SourceDocs.TypeDoc? doc)
    {
        var names = Enum.GetNames(type);
        ImGui.SeparatorText($"Values ({names.Length})   underlying : {nameOf(Enum.GetUnderlyingType(type))}");
        if (!ImGui.BeginTable("##enum", 3, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV)) return;
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthFixed, 150);
        ImGui.TableSetupColumn("Value", ImGuiTableColumnFlags.WidthFixed, 80);
        ImGui.TableSetupColumn("Description", ImGuiTableColumnFlags.WidthStretch);
        foreach (var name in names)
        {
            ImGui.TableNextRow();
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(name);
            ImGui.TableNextColumn();
            ImGui.TextDisabled(Convert.ToUInt64(Enum.Parse(type, name)) is var v && type.IsDefined(typeof(FlagsAttribute)) ? $"0x{v:X}" : Convert.ToString(Convert.ChangeType(Enum.Parse(type, name), Enum.GetUnderlyingType(type)), CultureInfo.InvariantCulture));
            ImGui.TableNextColumn();
            ImGui.TextUnformatted(doc != null && doc.Members.TryGetValue(name, out var text) ? text : "");
        }
        ImGui.EndTable();
    }

    // ---------------------------------------------------------------------
    // 使い方 (型から作るコードの例)
    // ---------------------------------------------------------------------

    internal string usageOf(Type type)
    {
        var b = new StringBuilder();
        var name = nameOf(type);
        var variable = char.ToLowerInvariant(type.Name[0]) + type.Name[1..];
        if (variable is "static" or "event" or "object" or "base" or "params") variable = "@" + variable;

        if (typeof(IComponent).IsAssignableFrom(type) && type.IsValueType)
        {
            var fields = ComponentRegistry.Get(type).Fields.Where(f => !f.HideInInspector).ToList();
            object? defaults = null;
            try { defaults = Activator.CreateInstance(type); } catch (Exception) { }
            var initializers = fields.Select(f => (f.Name, Value: defaults != null ? literal(f.Info.GetValue(defaults), f.FieldType) : null))
                                     .Where(f => f.Value != null).Take(3).Select(f => $"{f.Name} = {f.Value}").ToList();
            b.AppendLine("// エンティティに付ける");
            b.AppendLine("var entity = world.CreateEntity(\"Name\");");
            if (type != typeof(Transform) && type != typeof(EntityInfo)) b.AppendLine("world.Add(entity, new Transform());");
            b.AppendLine(initializers.Count > 0 ? $"world.Add(entity, new {name} {{ {string.Join(", ", initializers)} }});" : $"world.Add(entity, new {name}());");
            foreach (var factory in type.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => m.ReturnType == type).Take(3))
                b.AppendLine($"world.Add(entity, {name}.{factory.Name}({string.Join(", ", factory.GetParameters().Select(p => p.Name))}));   // 作る関数");
            b.AppendLine();
            b.AppendLine("// 読む / 書く (ref で受けると、そのまま書き換わる)");
            b.AppendLine($"if (world.Has<{name}>(entity))");
            b.AppendLine("{");
            b.AppendLine($"    ref var {variable} = ref world.Get<{name}>(entity);");
            if (fields.FirstOrDefault() is { } first) b.AppendLine($"    var value = {variable}.{first.Name};");
            b.AppendLine("}");
            b.AppendLine();
            b.AppendLine("// System の Update の中で、持っている物の全部に");
            b.AppendLine(type == typeof(Transform)
                ? "world.ForEach((Entity e, ref Transform transform) =>"
                : $"world.ForEach((Entity e, ref Transform transform, ref {name} {variable}) =>");
            b.AppendLine("{");
            b.AppendLine("});");
            b.AppendLine();
            b.Append($"world.Remove<{name}>(entity);   // 外す");
            return b.ToString();
        }
        if (type == typeof(ISystem) || type == typeof(IFixedSystem) || type.IsDefined(typeof(GameSystemAttribute), false))
        {
            var order = type.GetCustomAttribute<GameSystemAttribute>()?.Order ?? 0;
            var className = type.IsInterface ? "MySystem" : name;
            b.AppendLine("// Project/Scripts の .cs に書くと、エディターが自動で作って登録する (保存するとビルドし直す)");
            b.AppendLine(order != 0 ? $"[GameSystem(Order = {order})]" : "[GameSystem]");
            b.AppendLine($"public sealed class {className} : {(type == typeof(IFixedSystem) ? "IFixedSystem" : "ISystem")}");
            b.AppendLine("{");
            b.AppendLine("    public " + className + "(Engine engine) { }   // 引数なしでもよい");
            b.AppendLine();
            if (type == typeof(IFixedSystem))
            {
                b.AppendLine("    // 固定の刻み (Time.FixedDeltaTime) で、物理の前に呼ばれる");
                b.AppendLine("    public void FixedUpdate(World world)");
            }
            else
            {
                b.AppendLine("    // 毎フレーム (再生中だけ)");
                b.AppendLine("    public void Update(World world)");
            }
            b.AppendLine("    {");
            b.AppendLine("        world.ForEach((Entity e, ref Transform transform) => { });");
            b.AppendLine("    }");
            b.Append('}');
            return b.ToString();
        }
        if (type == typeof(IComponent))
        {
            b.AppendLine("// Project/Scripts の .cs に書く (public なフィールドがインスペクターに出て、シーンに保存される)");
            b.AppendLine("[ComponentInfo(DisplayName = \"My Component\", Category = \"Game\")]   // 無くてもよい");
            b.AppendLine("public struct MyComponent : IComponent");
            b.AppendLine("{");
            b.AppendLine("    [Range(0, 10)] public float Speed;");
            b.AppendLine("    [Tooltip(\"説明\")] public Vector3 Direction;");
            b.AppendLine("    public MyComponent() { Speed = 1; Direction = Vector3.UnitZ; }   // 既定の値");
            b.Append('}');
            return b.ToString();
        }
        if (typeof(Attribute).IsAssignableFrom(type) && !type.IsAbstract)
        {
            var shortName = name.EndsWith("Attribute") ? name[..^"Attribute".Length] : name;
            var ctor = type.GetConstructors().OrderByDescending(c => c.GetParameters().Length).FirstOrDefault();
            var args = ctor == null ? "" : string.Join(", ", ctor.GetParameters().Select(p => p.ParameterType == typeof(Type) ? "typeof(MyComponent)" : p.ParameterType == typeof(string) ? "\"...\"" : p.Name));
            var named = type.GetProperties().Where(p => p.SetMethod?.IsPublic == true && p.DeclaringType == type).Select(p => $"{p.Name} = ...").Take(2).ToList();
            var all = string.Join(", ", new[] { args }.Concat(named).Where(s => s.Length > 0));
            b.Append($"[{shortName}{(all.Length > 0 ? $"({all})" : "")}]");
            return b.ToString();
        }
        if (type.IsAbstract && !type.IsSealed && type.IsClass)
        {
            var overridable = type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                .Where(m => (m.IsAbstract || m.IsVirtual && !m.IsFinal) && (m.IsPublic || m.IsFamily) && m.DeclaringType != typeof(object) && !m.IsSpecialName).ToList();
            var baseName = type.IsGenericTypeDefinition ? name.Replace(string.Join(", ", type.GetGenericArguments().Select(a => a.Name)), "MyComponent") : name;
            var attribute = type.Assembly == EditorAssembly ? EditorAssembly.GetTypes().FirstOrDefault(a => typeof(Attribute).IsAssignableFrom(a) && a.Name.StartsWith(type.Name.Split('`')[0].Replace("Component", ""))) : null;
            b.AppendLine("// Project/Scripts/Editor の .cs に書く (エディターの拡張)");
            if (attribute != null) b.AppendLine($"[{attribute.Name[..^"Attribute".Length]}({(attribute == typeof(CustomEditorAttribute) ? "typeof(MyComponent)" : "...")})]");
            b.AppendLine($"public sealed class My{type.Name.Split('`')[0]} : {baseName}");
            b.AppendLine("{");
            foreach (var method in overridable.Where(m => m.DeclaringType == type || type.IsGenericType).DistinctBy(m => m.Name + parameters(m)).Take(4))
                b.AppendLine($"    {(method.IsPublic ? "public" : "protected")} override {nameOf(method.ReturnType)} {method.Name}({parameters(method)}) {{ }}");
            b.Append('}');
            return b.ToString();
        }
        if (type.IsAbstract && type.IsSealed)   // static class
        {
            var members = type.GetProperties(BindingFlags.Public | BindingFlags.Static).Where(p => p.GetMethod?.IsPublic == true).Take(3).ToList();
            foreach (var p in members) b.AppendLine($"var {char.ToLowerInvariant(p.Name[0])}{p.Name[1..]} = {name}.{p.Name};");
            foreach (var m in type.GetMethods(BindingFlags.Public | BindingFlags.Static).Where(m => !m.IsSpecialName).DistinctBy(m => m.Name).Take(3))
                b.AppendLine($"{name}.{m.Name}({string.Join(", ", m.GetParameters().Select(p => p.Name))});");
            return b.ToString().TrimEnd();
        }
        return "";
    }

    // ---------------------------------------------------------------------
    // 文字にする
    // ---------------------------------------------------------------------

    internal static string kindOf(Type type) =>
        type.IsEnum ? "enum" :
        type.IsInterface ? "interface" :
        type.IsValueType ? (typeof(IComponent).IsAssignableFrom(type) ? "struct (component)" : "struct") :
        typeof(Attribute).IsAssignableFrom(type) ? "attribute" :
        type.IsAbstract && type.IsSealed ? "static class" :
        type.IsAbstract ? "abstract class" :
        type.IsSealed ? "sealed class" : "class";

    internal static string nameOf(Type type)
    {
        if (type.IsByRef) return "ref " + nameOf(type.GetElementType()!);
        if (type.IsArray) return nameOf(type.GetElementType()!) + "[]";
        if (Nullable.GetUnderlyingType(type) is { } inner) return nameOf(inner) + "?";
        if (type.IsGenericParameter) return type.Name;
        var simple = type == typeof(float) ? "float" : type == typeof(int) ? "int" : type == typeof(uint) ? "uint" : type == typeof(bool) ? "bool" :
                     type == typeof(string) ? "string" : type == typeof(double) ? "double" : type == typeof(void) ? "void" : type == typeof(object) ? "object" :
                     type == typeof(long) ? "long" : type == typeof(ulong) ? "ulong" : type == typeof(byte) ? "byte" : type == typeof(short) ? "short" :
                     type == typeof(ushort) ? "ushort" : type == typeof(char) ? "char" : null;
        if (simple != null) return simple;
        var name = type.Name;
        if (type.IsGenericType)
        {
            name = name[..name.IndexOf('`')];
            name += "<" + string.Join(", ", type.GetGenericArguments().Select(nameOf)) + ">";
        }
        return type.IsNested && !type.IsGenericParameter ? nameOf(type.DeclaringType!) + "." + name : name;
    }

    private static string parameters(MethodBase method) =>
        string.Join(", ", method.GetParameters().Select(p =>
            (p.IsOut ? "out " : p.ParameterType.IsByRef ? (p.IsIn ? "in " : "ref ") : "") +
            nameOf(p.ParameterType.IsByRef ? p.ParameterType.GetElementType()! : p.ParameterType) + " " + p.Name +
            (p.HasDefaultValue ? " = " + literal(p.DefaultValue) : "")));

    private static string genericArguments(MethodInfo method) =>
        method.IsGenericMethodDefinition ? "<" + string.Join(", ", method.GetGenericArguments().Select(a => a.Name)) + ">" : "";

    // 値を C# の書き方に (書けない物は null)
    private static string? literal(object? value, Type? type = null)
    {
        type ??= value?.GetType();
        switch (value)
        {
        case null: return type == null || !type.IsValueType ? "null" : null;
        case float f: return f.ToString("R", CultureInfo.InvariantCulture) + "f";
        case double d: return d.ToString("R", CultureInfo.InvariantCulture);
        case bool v: return v ? "true" : "false";
        case string s: return "\"" + s.Replace("\"", "\\\"") + "\"";
        case Vector3 v: return v == Vector3.Zero ? "Vector3.Zero" : v == Vector3.One ? "Vector3.One" : FormattableString.Invariant($"new Vector3({v.X}f, {v.Y}f, {v.Z}f)");
        case Vector2 v: return FormattableString.Invariant($"new Vector2({v.X}f, {v.Y}f)");
        case Vector4 v: return FormattableString.Invariant($"new Vector4({v.X}f, {v.Y}f, {v.Z}f, {v.W}f)");
        case Quaternion q: return q == Quaternion.Identity ? "Quaternion.Identity" : FormattableString.Invariant($"new Quaternion({q.X}f, {q.Y}f, {q.Z}f, {q.W}f)");
        case Enum e: return $"{nameOf(e.GetType())}.{e}";
        case Type t: return $"typeof({nameOf(t)})";
        case int or uint or long or ulong or short or ushort or byte: return Convert.ToString(value, CultureInfo.InvariantCulture)! + (value is uint ? "u" : "");
        default: return null;
        }
    }

    private static string shortValue(object? value) => value switch
    {
        null => "null",
        float f => f.ToString("G6", CultureInfo.InvariantCulture),
        Vector3 v => FormattableString.Invariant($"({v.X:G4}, {v.Y:G4}, {v.Z:G4})"),
        Vector4 v => FormattableString.Invariant($"({v.X:G4}, {v.Y:G4}, {v.Z:G4}, {v.W:G4})"),
        Quaternion q => q == Quaternion.Identity ? "Identity" : FormattableString.Invariant($"({q.X:G3}, {q.Y:G3}, {q.Z:G3}, {q.W:G3})"),
        uint u when u == uint.MaxValue => "0xFFFFFFFF",
        Entity e => e.IsNull ? "Null" : e.ToString() ?? "",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? "",
    };

    // ソースのファイル (ゲーム : そのまま / エンジン・エディター : リポジトリがあればその中)
    private string? sourceFile(SourceDocs.TypeDoc doc)
    {
        if (doc.IsFile) return File.Exists(doc.Source) ? doc.Source : null;
        if (_repoRoot == null) return null;
        var relative = doc.Source.Replace('\\', '/');
        var path = relative.StartsWith("RuntimeSource/") ? Path.Combine(_repoRoot, "CSharp_console", "GameEngine.Runtime", relative["RuntimeSource/".Length..])
                 : relative.StartsWith("EditorSource/")  ? Path.Combine(_repoRoot, "CSharp_console", "GameEngine.Editor", relative["EditorSource/".Length..]) : null;
        return path != null && File.Exists(path) ? path : null;
    }

    private static void openFile(string file)
    {
        try { Process.Start(new ProcessStartInfo(file) { UseShellExecute = true }); }
        catch (Exception ex) { Console.WriteLine($"[Reference] Cannot open {file} : {ex.Message}"); }
    }

    // 確認用 : 型の説明と、メンバーの説明の数
    internal (string Summary, int Members)? DocFor(Type type) => docOf(type) is { } doc ? (doc.Summary, doc.Members.Count) : null;
    internal IEnumerable<(string Group, string Name)> Entries
    {
        get
        {
            if (_builtForLoad != _app.Scripts.LoadCount) rebuild();
            return _entries.Select(e => (e.Group, e.Name));
        }
    }
}
