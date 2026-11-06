using System.Globalization;
using System.Numerics;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GameEngine;

// ==========================================================================
// シェーダーのノードグラフ (SH4)。Assets の「名前.shadergraph.json」
//   ノードを出力 (Output) まで繋ぐと、SH2 と同じ書き方の HLSL (// @param ... + float4 Surface(SurfaceInput IN)) を作る
//   それを ShaderAsset がいつものようにコンパイルする (マテリアルの Shader にこのファイルを入れる)
//
//   ピンの型 : float / float2 / float3 / float4 / Dynamic (繋いだ物の一番大きい物に合わせる) / Texture2D / Texture3D
//     違う大きさを繋ぐと自動で変える (float → 全部同じ値 / 大きい → 前から切る / 小さい → 足りない所は 0、w は 1)
//   繋いでいない入力はノードに書いた値 (Values[入力の名前]) か既定の値 ("@uv" のような物は IN.UV など)
//   エラー : ノードごと (Errors)。コンパイルのエラーも「node_番号」でノードに戻せる (各ノードのコードの前に #line を書く)
// ==========================================================================
public enum PortType { Float = 1, Float2 = 2, Float3 = 3, Float4 = 4, Dynamic = 8, Texture2D = 16, Texture3D = 17 }
public enum NodePropertyKind { Float, Vector, Color, Text, Enum, Bool, Texture2D, Texture3D }

/// <summary>ピン。Default は繋いでいない時の値 : "0.5" / "(1, 0, 0)" / "@uv" "@normal" "@worldpos" "@localpos" "@view" "@time" "@color"</summary>
public sealed record PortDef(string Name, PortType Type, string Default = "0");
/// <summary>ノードの設定 (ピンではない物 : パラメーターの名前、色、選ぶ物など)</summary>
public sealed record NodePropertyDef(string Name, NodePropertyKind Kind, string Default, string[]? Options = null);

public sealed class NodeDef
{
    public required string Type { get; init; }
    public required string Category { get; init; }
    public required string Title { get; init; }
    public PortDef[] Inputs { get; init; } = Array.Empty<PortDef>();
    public PortDef[] Outputs { get; init; } = Array.Empty<PortDef>();
    public NodePropertyDef[] Properties { get; init; } = Array.Empty<NodePropertyDef>();
    public string Help { get; init; } = "";
    public bool IsOutput { get; init; }
    public bool IsParameter { get; init; }
    /// <summary>コードを作る : 入力の式 (型は合わせ済み) → 出力の式</summary>
    public required Func<NodeContext, string[]> Emit { get; init; }
}

public sealed class ShaderGraphNode
{
    public int Id;
    public string Type = "";
    public Vector2 Position;
    /// <summary>設定と、繋いでいない入力の値 (どちらも文字で持つ)</summary>
    public Dictionary<string, string> Values = new(StringComparer.Ordinal);

    public ShaderGraphNode Clone() => new() { Id = Id, Type = Type, Position = Position, Values = new Dictionary<string, string>(Values, StringComparer.Ordinal) };
}

public sealed record ShaderGraphLink(int FromNode, string FromPort, int ToNode, string ToPort);

/// <summary>HLSL を作った結果</summary>
public sealed class ShaderGraphResult
{
    public string Source = "";
    /// <summary>ノード → エラー (出力まで繋がっていない物も入る)</summary>
    public Dictionary<int, string> Errors = new();
    /// <summary>出力まで繋がっている物のエラーを纏めた物 (無ければ null = コンパイルしてよい)。"node_番号:1:1: error: ..." の形</summary>
    public string? Error;
    /// <summary>ノード → 出力ピンの大きさ (1..4。テクスチャは 16 / 17)。エディターがピンの色に使う</summary>
    public Dictionary<int, int[]> OutputTypes = new();
    /// <summary>ノード → Dynamic の入力が合わせた大きさ</summary>
    public Dictionary<int, int> DynamicSizes = new();
}

public sealed class NodeContext
{
    internal NodeContext(ShaderGraphNode node, NodeDef def, StringBuilder code, List<string> parameters, int dynamic)
    {
        Node = node; Def = def; _code = code; _parameters = parameters; Dynamic = dynamic;
    }
    private readonly StringBuilder _code;
    private readonly List<string> _parameters;
    private int _temp;
    public ShaderGraphNode Node { get; }
    public NodeDef Def { get; }
    /// <summary>入力の式 (Def.Inputs の順。型は合わせ済み)</summary>
    public string[] In { get; internal set; } = Array.Empty<string>();
    /// <summary>入力が繋がっているか</summary>
    public bool[] Connected { get; internal set; } = Array.Empty<bool>();
    /// <summary>Dynamic のピンの大きさ (1..4)</summary>
    public int Dynamic { get; }
    public string? Error { get; private set; }

    public string Prop(string name) => Node.Values.TryGetValue(name, out var v) ? v : Def.Properties.FirstOrDefault(p => p.Name == name)?.Default ?? "";
    public float PropFloat(string name) => float.TryParse(Prop(name), NumberStyles.Float, CultureInfo.InvariantCulture, out var f) ? f : 0.0f;
    public Vector4 PropVector(string name) => ShaderAsset.TryParseVector(Prop(name), out var v) ? v : Vector4.Zero;
    public bool PropBool(string name) => Prop(name).Equals("true", StringComparison.OrdinalIgnoreCase);
    public string DynamicType => ShaderGraph.TypeName(Dynamic);

    /// <summary>このノードの一時の変数を作る (戻り値 = 名前)</summary>
    public string Temp(string type, string expression)
    {
        var name = $"_n{Node.Id}_t{_temp++}";
        _code.AppendLine($"    {type} {name} = {expression};");
        return name;
    }
    public void Line(string statement) => _code.AppendLine($"    {statement}");
    /// <summary>// @param の行を足す (例 : "float Speed 1 range(0, 4)")</summary>
    public void Parameter(string line) { _parameters.Add(line); AddedParameter = true; }
    internal bool AddedParameter { get; private set; }
    public void Fail(string message) => Error ??= message;
}

public sealed class ShaderGraph
{
    public const string Extension = ".shadergraph.json";
    public const int FormatVersion = 1;

    public List<ShaderGraphNode> Nodes { get; } = new();
    public List<ShaderGraphLink> Links { get; } = new();
    public int NextId = 1;

    public static bool IsGraph(string? path) => path != null && path.EndsWith(Extension, StringComparison.OrdinalIgnoreCase);

    public ShaderGraphNode? Find(int id) => Nodes.FirstOrDefault(n => n.Id == id);

    public ShaderGraphNode Add(string type, Vector2 position)
    {
        if (!Library.TryGetValue(type, out var def)) throw new ArgumentException($"Unknown node type {type}");
        var node = new ShaderGraphNode { Id = NextId++, Type = type, Position = position };
        // パラメーターの名前は重ならない物にする
        if (def.IsParameter)
        {
            var baseName = def.Properties.First(p => p.Name == "Name").Default;
            var name = baseName;
            for (int i = 1; Nodes.Any(n => n.Values.TryGetValue("Name", out var v) && v == name); i++) name = $"{baseName}{i}";
            node.Values["Name"] = name;
        }
        Nodes.Add(node);
        return node;
    }

    public void Remove(int id)
    {
        Nodes.RemoveAll(n => n.Id == id);
        Links.RemoveAll(l => l.FromNode == id || l.ToNode == id);
    }

    /// <summary>繋ぐ (入力のピンには 1 本だけ : 前の物は外す)。輪になる時は繋がずに false</summary>
    public bool Connect(int fromNode, string fromPort, int toNode, string toPort)
    {
        if (fromNode == toNode || dependsOn(fromNode, toNode)) return false;
        Links.RemoveAll(l => l.ToNode == toNode && l.ToPort == toPort);
        Links.Add(new ShaderGraphLink(fromNode, fromPort, toNode, toPort));
        return true;
    }

    // a が b の結果を使っているか (b → ... → a と繋がっているか)
    private bool dependsOn(int a, int b)
    {
        var stack = new Stack<int>();
        var seen = new HashSet<int>();
        stack.Push(a);
        while (stack.Count > 0)
        {
            var id = stack.Pop();
            if (id == b) return true;
            if (!seen.Add(id)) continue;
            foreach (var link in Links.Where(l => l.ToNode == id)) stack.Push(link.FromNode);
        }
        return false;
    }

    public ShaderGraph Clone()
    {
        var copy = new ShaderGraph { NextId = NextId };
        copy.Nodes.AddRange(Nodes.Select(n => n.Clone()));
        copy.Links.AddRange(Links);
        return copy;
    }

    /// <summary>新しいグラフ : 出力と、色のパラメーターを繋いだ物</summary>
    public static ShaderGraph CreateDefault()
    {
        var graph = new ShaderGraph();
        var output = graph.Add("Output", new Vector2(420, 80));
        var color = graph.Add("ColorParameter", new Vector2(80, 60));
        color.Values["Name"] = "BaseColor";
        color.Values["Default"] = "(0.8, 0.8, 0.8, 1)";
        var roughness = graph.Add("FloatParameter", new Vector2(80, 220));
        roughness.Values["Name"] = "Roughness";
        roughness.Values["Default"] = "0.5";
        graph.Connect(color.Id, "RGB", output.Id, "BaseColor");
        graph.Connect(roughness.Id, "Value", output.Id, "Roughness");
        return graph;
    }

    // ---------------------------------------------------------------------
    // JSON
    // ---------------------------------------------------------------------

    public string ToJson()
    {
        using var stream = new MemoryStream();
        using (var w = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            w.WriteStartObject();
            w.WriteNumber("version", FormatVersion);
            w.WriteNumber("nextId", NextId);
            w.WriteStartArray("nodes");
            foreach (var node in Nodes)
            {
                w.WriteStartObject();
                w.WriteNumber("id", node.Id);
                w.WriteString("type", node.Type);
                w.WriteNumber("x", MathF.Round(node.Position.X, 1));
                w.WriteNumber("y", MathF.Round(node.Position.Y, 1));
                if (node.Values.Count > 0)
                {
                    w.WriteStartObject("values");
                    foreach (var (key, value) in node.Values.OrderBy(p => p.Key, StringComparer.Ordinal)) w.WriteString(key, value);
                    w.WriteEndObject();
                }
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteStartArray("links");
            foreach (var link in Links)
            {
                w.WriteStartObject();
                w.WriteNumber("from", link.FromNode);
                w.WriteString("out", link.FromPort);
                w.WriteNumber("to", link.ToNode);
                w.WriteString("in", link.ToPort);
                w.WriteEndObject();
            }
            w.WriteEndArray();
            w.WriteEndObject();
        }
        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static ShaderGraph FromJson(string json)
    {
        var graph = new ShaderGraph();
        using var document = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        var root = document.RootElement;
        if (root.TryGetProperty("nodes", out var nodes))
            foreach (var n in nodes.EnumerateArray())
            {
                var node = new ShaderGraphNode
                {
                    Id = n.GetProperty("id").GetInt32(),
                    Type = n.GetProperty("type").GetString() ?? "",
                    Position = new Vector2(n.TryGetProperty("x", out var x) ? x.GetSingle() : 0, n.TryGetProperty("y", out var y) ? y.GetSingle() : 0),
                };
                if (n.TryGetProperty("values", out var values))
                    foreach (var property in values.EnumerateObject())
                        node.Values[property.Name] = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() ?? "" : property.Value.GetRawText();
                graph.Nodes.Add(node);
            }
        if (root.TryGetProperty("links", out var links))
            foreach (var l in links.EnumerateArray())
                graph.Links.Add(new ShaderGraphLink(l.GetProperty("from").GetInt32(), l.GetProperty("out").GetString() ?? "", l.GetProperty("to").GetInt32(), l.GetProperty("in").GetString() ?? ""));
        graph.NextId = Math.Max(root.TryGetProperty("nextId", out var next) ? next.GetInt32() : 1, graph.Nodes.Count == 0 ? 1 : graph.Nodes.Max(n => n.Id) + 1);
        return graph;
    }

    public static ShaderGraph Load(string file) => FromJson(File.ReadAllText(file, Encoding.UTF8));

    // ---------------------------------------------------------------------
    // HLSL を作る
    // ---------------------------------------------------------------------

    public static string TypeName(int size) => size <= 1 ? "float" : $"float{size}";

    private static readonly Regex Identifier = new(@"^[A-Za-z][A-Za-z0-9_]*$", RegexOptions.Compiled);
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "Surface", "SurfaceInput", "Lit", "ToLinear", "IN", "Time", "MainLight", "SceneDepth", "SceneWorldPosition", "HasSceneDepth",
        "float", "float2", "float3", "float4", "int", "uint", "bool", "half", "double", "true", "false", "return", "if", "else", "for", "while",
        "in", "out", "inout", "static", "const", "struct", "cbuffer", "register", "sampler", "texture", "matrix", "vector", "discard", "break", "continue",
    };

    /// <summary>HLSL (SH2 の書き方) を作る</summary>
    public ShaderGraphResult Generate()
    {
        var result = new ShaderGraphResult();
        var outputs = Nodes.Where(n => Library.TryGetValue(n.Type, out var d) && d.IsOutput).ToList();
        foreach (var extra in outputs.Skip(1)) result.Errors[extra.Id] = "Only one Output node is used (this one is ignored)";
        var output = outputs.FirstOrDefault();

        var parameters = new List<string>();
        var bodies = new Dictionary<int, string>();
        var exprs = new Dictionary<int, string[]>();
        var state = new Dictionary<int, int>();   // 1 = 作っている (輪) / 2 = 出来た
        var order = new List<int>();
        var withParameters = new HashSet<int>();   // @param を書いたノード (繋いでいなくてもエラーはコンパイルを止める)

        // パラメーターの名前を先に調べる (重なり / 使えない名前)
        var names = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var node in Nodes)
        {
            if (!Library.TryGetValue(node.Type, out var def) || !def.IsParameter) continue;
            var name = node.Values.TryGetValue("Name", out var v) ? v.Trim() : "";
            if (!Identifier.IsMatch(name) || Keywords.Contains(name) || name.StartsWith("_")) result.Errors[node.Id] = $"'{name}' cannot be a parameter name (letters, digits and _, starting with a letter)";
            else if (names.TryGetValue(name, out var other)) { result.Errors[node.Id] = $"The parameter name '{name}' is used twice"; result.Errors.TryAdd(other, $"The parameter name '{name}' is used twice"); }
            else names[name] = node.Id;
        }

        string[] visit(ShaderGraphNode node)
        {
            if (exprs.TryGetValue(node.Id, out var done)) return done;
            if (state.TryGetValue(node.Id, out var s) && s == 1) { result.Errors[node.Id] = "The links make a loop"; return Array.Empty<string>(); }
            state[node.Id] = 1;
            if (!Library.TryGetValue(node.Type, out var def))
            {
                result.Errors[node.Id] = $"Unknown node type '{node.Type}'";
                state[node.Id] = 2;
                return exprs[node.Id] = Array.Empty<string>();
            }
            var inputs = new string[def.Inputs.Length];
            var sizes = new int[def.Inputs.Length];
            var connected = new bool[def.Inputs.Length];
            string? error = result.Errors.GetValueOrDefault(node.Id);
            for (int i = 0; i < def.Inputs.Length; i++)
            {
                var port = def.Inputs[i];
                var link = Links.FirstOrDefault(l => l.ToNode == node.Id && l.ToPort == port.Name);
                var source = link != null ? Find(link.FromNode) : null;
                if (source != null && Library.TryGetValue(source.Type, out var sourceDef))
                {
                    var outIndex = Array.FindIndex(sourceDef.Outputs, o => o.Name == link!.FromPort);
                    var sourceExprs = visit(source);
                    if (outIndex < 0) { error ??= $"{port.Name} : the link's output '{link!.FromPort}' does not exist"; }
                    else
                    {
                        var sourceType = result.OutputTypes.TryGetValue(source.Id, out var sourceTypes) && outIndex < sourceTypes.Length ? sourceTypes[outIndex] : (int)sourceDef.Outputs[outIndex].Type;
                        var isTexture = port.Type is PortType.Texture2D or PortType.Texture3D;
                        if (isTexture || sourceType >= 16)
                        {
                            if (sourceType != (int)port.Type) error ??= $"{port.Name} : needs {(isTexture ? port.Type.ToString() : "a number")}, but {(sourceType >= 16 ? ((PortType)sourceType).ToString() : TypeName(sourceType))} is connected";
                            else { inputs[i] = outIndex < sourceExprs.Length ? sourceExprs[outIndex] : ""; sizes[i] = sourceType; connected[i] = true; }
                        }
                        else
                        {
                            inputs[i] = outIndex < sourceExprs.Length ? sourceExprs[outIndex] : literal(Vector4.Zero, sourceType);
                            sizes[i] = sourceType;
                            connected[i] = true;
                        }
                    }
                }
                if (connected[i]) continue;
                if (port.Type is PortType.Texture2D or PortType.Texture3D) { error ??= $"Connect a {port.Type} parameter to {port.Name}"; inputs[i] = ""; sizes[i] = (int)port.Type; continue; }
                var text = node.Values.TryGetValue(port.Name, out var value) ? value : port.Default;
                if (special(text) is { } sp) { inputs[i] = sp.Expr; sizes[i] = sp.Size; }
                else if (ShaderAsset.TryParseVector(text, out var vector))
                {
                    var count = text.Trim().TrimStart('(').TrimEnd(')').Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries).Length;
                    var size = port.Type == PortType.Dynamic ? count : (int)port.Type;
                    inputs[i] = literal(vector, size);
                    sizes[i] = size;
                }
                else { error ??= $"{port.Name} : cannot read the value '{text}'"; inputs[i] = "0.0f"; sizes[i] = 1; }
            }
            // Dynamic : 入力の一番大きい物 (繋いだ物を優先。どれも繋いでいなければ書いた値の大きさ)
            var dynamic = 1;
            var anyConnected = false;
            for (int i = 0; i < def.Inputs.Length; i++)
                if (def.Inputs[i].Type == PortType.Dynamic && connected[i]) { dynamic = Math.Max(dynamic, sizes[i]); anyConnected = true; }
            if (!anyConnected)
                for (int i = 0; i < def.Inputs.Length; i++)
                    if (def.Inputs[i].Type == PortType.Dynamic) dynamic = Math.Max(dynamic, sizes[i]);
            dynamic = Math.Clamp(dynamic, 1, 4);
            for (int i = 0; i < def.Inputs.Length; i++)
            {
                var port = def.Inputs[i];
                if (port.Type is PortType.Texture2D or PortType.Texture3D) continue;
                var target = port.Type == PortType.Dynamic ? dynamic : (int)port.Type;
                inputs[i] = Cast(inputs[i], sizes[i], target);
            }
            var code = new StringBuilder();
            var context = new NodeContext(node, def, code, parameters, dynamic) { In = inputs, Connected = connected };
            string[] outs;
            try { outs = def.Emit(context); }
            catch (Exception ex) when (ex is FormatException or ArgumentException or IndexOutOfRangeException) { outs = Array.Empty<string>(); context.Fail(ex.Message); }
            error ??= context.Error;
            if (context.AddedParameter) withParameters.Add(node.Id);
            // 出力は変数に入れる (使う所が何回あっても 1 回だけ計算する)
            var types = new int[def.Outputs.Length];
            var names2 = new string[def.Outputs.Length];
            for (int o = 0; o < def.Outputs.Length; o++)
            {
                var type = def.Outputs[o].Type;
                types[o] = type == PortType.Dynamic ? dynamic : (int)type;
                if (type is PortType.Texture2D or PortType.Texture3D) { names2[o] = o < outs.Length ? outs[o] : ""; continue; }
                var expr = o < outs.Length ? outs[o] : literal(Vector4.Zero, types[o]);
                names2[o] = $"_n{node.Id}_{def.Outputs[o].Name}";
                code.AppendLine($"    {TypeName(types[o])} {names2[o]} = {expr};");
            }
            result.OutputTypes[node.Id] = types;
            if (def.Inputs.Any(p => p.Type == PortType.Dynamic)) result.DynamicSizes[node.Id] = dynamic;
            if (error != null) result.Errors[node.Id] = error;
            bodies[node.Id] = code.ToString();
            order.Add(node.Id);
            state[node.Id] = 2;
            return exprs[node.Id] = names2;
        }

        // 出力から辿る (使われる物 = 出力まで繋がっている物)。その後、残りも調べる (エラーと型を出すため)
        var used = new HashSet<int>();
        if (output != null)
        {
            visit(output);
            used.UnionWith(order);
        }
        foreach (var node in Nodes) visit(node);
        // パラメーターは繋いでいなくてもマテリアルに出す (値を残す)。作るのは visit の中なので全部辿った後に並べる

        var b = new StringBuilder();
        b.AppendLine("// Generated from a shader graph (SH4). Edit the .shadergraph.json in the Shader Graph window");
        if (output != null)
        {
            string prop(string name, string fallback) => output.Values.TryGetValue(name, out var v) ? v : fallback;
            var transparent = prop("Queue", "Opaque") == "Transparent";
            b.AppendLine($"// @queue {(transparent ? "transparent" : "opaque")}");
            var blend = prop("Blend", "Auto");
            if (blend != "Auto") b.AppendLine($"// @blend {blend.ToLowerInvariant()}");
            b.AppendLine($"// @cull {prop("Cull", "Back").ToLowerInvariant()}");
            var zwrite = prop("ZWrite", "Auto");
            if (zwrite != "Auto") b.AppendLine($"// @zwrite {zwrite.ToLowerInvariant()}");
        }
        foreach (var line in parameters) b.AppendLine($"// @param {line}");
        b.Append(Helpers);
        b.AppendLine("float4 Surface(SurfaceInput IN)");
        b.AppendLine("{");
        foreach (var id in order.Where(used.Contains))
        {
            b.AppendLine($"#line 1 \"node_{id}\"");
            b.Append(bodies[id]);
        }
        if (output == null) b.AppendLine("    return float4(1.0f, 0.0f, 1.0f, 1.0f);");
        b.AppendLine("}");
        result.Source = b.ToString();

        // コンパイルを止めるエラー : 使われるノードの物 / 出力が無い
        var blocking = result.Errors.Where(e => used.Contains(e.Key) || withParameters.Contains(e.Key) || Find(e.Key) is { } n && Library.TryGetValue(n.Type, out var d) && d.IsParameter).ToList();
        if (output == null) result.Error = "graph:1:1: error: Add an Output node";
        else if (blocking.Count > 0) result.Error = string.Join("\n", blocking.Select(e => $"node_{e.Key}:1:1: error: {e.Value}"));
        return result;
    }

    // コンパイルのエラー (DXC の「ファイル:行:列: error: ...」) → ノードごと
    private static readonly Regex CompileError = new(@"node_(\d+):\d+:\d+:\s*(?:fatal\s+)?error:\s*(.*)", RegexOptions.Compiled);
    public static Dictionary<int, string> ErrorsByNode(string? error)
    {
        var map = new Dictionary<int, string>();
        if (string.IsNullOrEmpty(error)) return map;
        foreach (Match m in CompileError.Matches(error))
        {
            var id = int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture);
            map[id] = map.TryGetValue(id, out var before) ? $"{before}\n{m.Groups[2].Value.Trim()}" : m.Groups[2].Value.Trim();
        }
        return map;
    }

    /// <summary>大きさを変える (1 → n : 全部同じ値 / 大きい → 前から切る / 小さい → 0 で埋める、w は 1)</summary>
    public static string Cast(string expr, int from, int to)
    {
        if (from == to || from >= 16) return expr;
        if (from == 1) return $"(({TypeName(to)})({expr}))";
        if (to < from) return $"({expr}).{"xyzw"[..to]}";
        return (from, to) switch
        {
            (2, 3) => $"float3({expr}, 0.0f)",
            (2, 4) => $"float4({expr}, 0.0f, 1.0f)",
            _      => $"float4({expr}, 1.0f)",   // 3 → 4
        };
    }

    private static string f(float v)
    {
        if (float.IsNaN(v) || float.IsInfinity(v)) v = 0;
        var s = v.ToString("0.0#######", CultureInfo.InvariantCulture);
        return s + "f";
    }

    private static string literal(Vector4 v, int size) => size switch
    {
        1 => f(v.X),
        2 => $"float2({f(v.X)}, {f(v.Y)})",
        3 => $"float3({f(v.X)}, {f(v.Y)}, {f(v.Z)})",
        _ => $"float4({f(v.X)}, {f(v.Y)}, {f(v.Z)}, {f(v.W)})",
    };

    private static (string Expr, int Size)? special(string text) => text.Trim() switch
    {
        "@uv"       => ("IN.UV", 2),
        "@normal"   => ("IN.Normal", 3),
        "@worldpos" => ("IN.WorldPosition", 3),
        "@localpos" => ("IN.LocalPosition", 3),
        "@view"     => ("IN.ViewDirection", 3),
        "@time"     => ("IN.Time", 1),
        "@color"    => ("IN.Color", 4),
        "@screenuv" => ("IN.ScreenUV", 2),
        _ => null,
    };

    /// <summary>入力の既定の値を人が読める形に (エディターの表示用)</summary>
    public static string DescribeDefault(string text) => text.Trim() switch
    {
        "@uv" => "UV", "@normal" => "Normal", "@worldpos" => "World Pos", "@localpos" => "Local Pos", "@view" => "View Dir",
        "@time" => "Time", "@color" => "Vertex Color", "@screenuv" => "Screen UV",
        var t => t,
    };

    // ノイズなどの関数 (Surface の前に入れる)
    private const string Helpers = """
        float _sgHash(float2 p) { p = frac(p * float2(123.34f, 456.21f)); p += dot(p, p + 45.32f); return frac(p.x * p.y); }
        float2 _sgHash2(float2 p) { float n = _sgHash(p); return float2(n, _sgHash(p + n + 17.17f)); }
        float _sgValueNoise(float2 uv)
        {
            float2 i = floor(uv), f = frac(uv); f = f * f * (3.0f - 2.0f * f);
            float a = _sgHash(i), b = _sgHash(i + float2(1, 0)), c = _sgHash(i + float2(0, 1)), d = _sgHash(i + float2(1, 1));
            return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
        }
        float _sgGradientNoise(float2 uv)
        {
            float2 i = floor(uv), f = frac(uv); float2 u = f * f * f * (f * (f * 6.0f - 15.0f) + 10.0f);
            float2 ga = _sgHash2(i) * 2.0f - 1.0f, gb = _sgHash2(i + float2(1, 0)) * 2.0f - 1.0f;
            float2 gc = _sgHash2(i + float2(0, 1)) * 2.0f - 1.0f, gd = _sgHash2(i + float2(1, 1)) * 2.0f - 1.0f;
            float a = dot(ga, f), b = dot(gb, f - float2(1, 0)), c = dot(gc, f - float2(0, 1)), d = dot(gd, f - float2(1, 1));
            return lerp(lerp(a, b, u.x), lerp(c, d, u.x), u.y) * 0.7071f + 0.5f;
        }
        float2 _sgVoronoi(float2 uv)   // x = 一番近い点までの距離 / y = そのセルの乱数
        {
            float2 i = floor(uv), f = frac(uv); float best = 8.0f; float cell = 0.0f;
            [unroll] for (int y = -1; y <= 1; ++y)
            [unroll] for (int x = -1; x <= 1; ++x)
            {
                float2 o = float2(x, y); float2 p = _sgHash2(i + o);
                float d = length(o + p - f);
                if (d < best) { best = d; cell = p.x; }
            }
            return float2(best, cell);
        }

        """;

    // ---------------------------------------------------------------------
    // ノードの一覧
    // ---------------------------------------------------------------------

    public static readonly IReadOnlyDictionary<string, NodeDef> Library = buildLibrary();
    public static readonly string[] Categories = { "Input", "Constant", "Parameter", "Texture", "Math", "Vector", "Noise", "Channel", "Output" };

    private static PortDef P(string name, PortType type, string def = "0") => new(name, type, def);
    private const PortType F1 = PortType.Float, F2 = PortType.Float2, F3 = PortType.Float3, F4 = PortType.Float4, D = PortType.Dynamic;

    private static Dictionary<string, NodeDef> buildLibrary()
    {
        var list = new List<NodeDef>();
        void add(string type, string category, string title, PortDef[] ins, PortDef[] outs, Func<NodeContext, string[]> emit, string help, NodePropertyDef[]? props = null, bool isOutput = false, bool isParameter = false) =>
            list.Add(new NodeDef { Type = type, Category = category, Title = title, Inputs = ins, Outputs = outs, Emit = emit, Help = help, Properties = props ?? Array.Empty<NodePropertyDef>(), IsOutput = isOutput, IsParameter = isParameter });
        PortDef[] none = Array.Empty<PortDef>();

        // ---------- 入力 ----------
        add("UV", "Input", "UV", none, new[] { P("UV", F2) }, c => new[] { "IN.UV" }, "Texture coordinates of the mesh");
        add("Position", "Input", "Position", none, new[] { P("Position", F3) }, c => new[] { c.Prop("Space") == "Local" ? "IN.LocalPosition" : "IN.WorldPosition" },
            "World or object space position of the pixel", new[] { new NodePropertyDef("Space", NodePropertyKind.Enum, "World", new[] { "World", "Local" }) });
        add("Normal", "Input", "Normal", none, new[] { P("Normal", F3) }, c => new[] { "IN.Normal" }, "World normal (faces the camera)");
        add("ViewDirection", "Input", "View Direction", none, new[] { P("Direction", F3) }, c => new[] { "IN.ViewDirection" }, "Direction from the camera to the pixel (length 1)");
        add("CameraPosition", "Input", "Camera Position", none, new[] { P("Position", F3) }, c => new[] { "IN.CameraPosition" }, "World position of the camera");
        add("VertexColor", "Input", "Vertex Color", none, new[] { P("RGBA", F4), P("RGB", F3), P("A", F1) }, c => new[] { "IN.Color", "IN.Color.rgb", "IN.Color.a" }, "Color of the mesh vertices");
        add("ScreenUV", "Input", "Screen UV", none, new[] { P("UV", F2) }, c => new[] { "IN.ScreenUV" }, "Position on the screen (0..1)");
        add("Time", "Input", "Time", none, new[] { P("Time", F1), P("Sin", F1), P("Cos", F1) }, c => new[] { "IN.Time", "sin(IN.Time)", "cos(IN.Time)" }, "Seconds since play started");

        // ---------- 定数 ----------
        add("Float", "Constant", "Float", none, new[] { P("Value", F1) }, c => new[] { f(c.PropFloat("Value")) }, "A number", new[] { new NodePropertyDef("Value", NodePropertyKind.Float, "1") });
        add("Vector2", "Constant", "Vector2", none, new[] { P("Value", F2) }, c => new[] { literal(c.PropVector("Value"), 2) }, "Two numbers", new[] { new NodePropertyDef("Value", NodePropertyKind.Vector, "(0, 0)") });
        add("Vector3", "Constant", "Vector3", none, new[] { P("Value", F3) }, c => new[] { literal(c.PropVector("Value"), 3) }, "Three numbers", new[] { new NodePropertyDef("Value", NodePropertyKind.Vector, "(0, 0, 0)") });
        add("Vector4", "Constant", "Vector4", none, new[] { P("Value", F4) }, c => new[] { literal(c.PropVector("Value"), 4) }, "Four numbers", new[] { new NodePropertyDef("Value", NodePropertyKind.Vector, "(0, 0, 0, 0)") });
        add("Color", "Constant", "Color", none, new[] { P("RGBA", F4), P("RGB", F3) }, c =>
        {
            var v = c.Temp("float4", literal(c.PropVector("Color"), 4));
            var linear = c.Temp("float4", $"float4(ToLinear({v}.rgb), {v}.a)");
            return new[] { linear, $"{linear}.rgb" };
        }, "A color (picked in sRGB, output linear)", new[] { new NodePropertyDef("Color", NodePropertyKind.Color, "(1, 1, 1, 1)") });

        // ---------- パラメーター (マテリアルで変えられる) ----------
        var nameProp = (string def) => new NodePropertyDef("Name", NodePropertyKind.Text, def);
        add("FloatParameter", "Parameter", "Float Parameter", none, new[] { P("Value", F1) }, c =>
        {
            var name = c.Prop("Name").Trim();
            float min = c.PropFloat("Min"), max = c.PropFloat("Max");
            c.Parameter($"float {name} {f(c.PropFloat("Default")).TrimEnd('f')}{(max > min ? $" range({f(min).TrimEnd('f')}, {f(max).TrimEnd('f')})" : "")}");
            return new[] { name };
        }, "A number the material can change", new[] { nameProp("Value"), new NodePropertyDef("Default", NodePropertyKind.Float, "1"), new NodePropertyDef("Min", NodePropertyKind.Float, "0"), new NodePropertyDef("Max", NodePropertyKind.Float, "1") }, isParameter: true);
        add("ColorParameter", "Parameter", "Color Parameter", none, new[] { P("RGBA", F4), P("RGB", F3) }, c =>
        {
            var name = c.Prop("Name").Trim();
            var v = c.PropVector("Default");
            c.Parameter($"color {name} ({f(v.X).TrimEnd('f')}, {f(v.Y).TrimEnd('f')}, {f(v.Z).TrimEnd('f')}, {f(v.W).TrimEnd('f')})");
            var linear = c.Temp("float4", $"float4(ToLinear({name}.rgb), {name}.a)");
            return new[] { linear, $"{linear}.rgb" };
        }, "A color the material can change (output linear)", new[] { nameProp("Color"), new NodePropertyDef("Default", NodePropertyKind.Color, "(1, 1, 1, 1)") }, isParameter: true);
        add("VectorParameter", "Parameter", "Vector Parameter", none, new[] { P("Value", F4) }, c =>
        {
            var name = c.Prop("Name").Trim();
            var v = c.PropVector("Default");
            c.Parameter($"vector {name} ({f(v.X).TrimEnd('f')}, {f(v.Y).TrimEnd('f')}, {f(v.Z).TrimEnd('f')}, {f(v.W).TrimEnd('f')})");
            return new[] { name };
        }, "Four numbers the material can change", new[] { nameProp("Vector"), new NodePropertyDef("Default", NodePropertyKind.Vector, "(0, 0, 0, 0)") }, isParameter: true);
        add("Texture2DParameter", "Parameter", "Texture 2D", none, new[] { P("Texture", PortType.Texture2D) }, c =>
        {
            var name = c.Prop("Name").Trim();
            var path = c.Prop("Default").Trim();
            if (path.Length == 0) path = "white";
            if (path.Contains(' ')) c.Fail("The texture path cannot contain spaces");
            c.Parameter($"texture {name} {path}");
            return new[] { name };
        }, "A texture the material can change (white / black / an asset path)", new[] { nameProp("MainTexture"), new NodePropertyDef("Default", NodePropertyKind.Texture2D, "white") }, isParameter: true);
        add("Texture3DParameter", "Parameter", "Texture 3D", none, new[] { P("Texture", PortType.Texture3D) }, c =>
        {
            var name = c.Prop("Name").Trim();
            var path = c.Prop("Default").Trim();
            if (!Noise3D.TryParse(path, out _, out _)) c.Fail($"'{path}' : use builtin:<{string.Join("|", Noise3D.Kinds)}>[:size]");
            c.Parameter($"texture3d {name} {path}");
            return new[] { name };
        }, "A 3D texture made by the engine (builtin:PerlinWorley3D:64 ...)", new[] { nameProp("Volume"), new NodePropertyDef("Default", NodePropertyKind.Texture3D, "builtin:PerlinWorley3D:64") }, isParameter: true);

        // ---------- テクスチャ ----------
        add("SampleTexture2D", "Texture", "Sample Texture 2D", new[] { P("Texture", PortType.Texture2D), P("UV", F2, "@uv") },
            new[] { P("RGBA", F4), P("RGB", F3), P("R", F1), P("G", F1), P("B", F1), P("A", F1) }, c =>
            {
                var t = c.Temp("float4", $"{c.In[0]}.Sample(g_Sampler, {c.In[1]})");
                if (c.PropBool("sRGB")) t = c.Temp("float4", $"float4(ToLinear({t}.rgb), {t}.a)");
                return new[] { t, $"{t}.rgb", $"{t}.r", $"{t}.g", $"{t}.b", $"{t}.a" };
            }, "Reads a 2D texture (sRGB : color textures are converted to linear)", new[] { new NodePropertyDef("sRGB", NodePropertyKind.Bool, "true") });
        add("SampleTexture3D", "Texture", "Sample Texture 3D", new[] { P("Texture", PortType.Texture3D), P("UVW", F3, "@localpos") },
            new[] { P("RGBA", F4), P("R", F1), P("G", F1), P("B", F1), P("A", F1) }, c =>
            {
                var t = c.Temp("float4", $"{c.In[0]}.Sample(g_Sampler, {c.In[1]})");
                return new[] { t, $"{t}.r", $"{t}.g", $"{t}.b", $"{t}.a" };
            }, "Reads a 3D texture (repeats)");
        add("TilingOffset", "Texture", "Tiling And Offset", new[] { P("UV", F2, "@uv"), P("Tiling", F2, "(1, 1)"), P("Offset", F2, "(0, 0)") }, new[] { P("UV", F2) },
            c => new[] { $"{c.In[0]} * {c.In[1]} + {c.In[2]}" }, "UV × Tiling + Offset");
        add("Panner", "Texture", "Panner", new[] { P("UV", F2, "@uv"), P("Speed", F2, "(0.1, 0)"), P("Time", F1, "@time") }, new[] { P("UV", F2) },
            c => new[] { $"{c.In[0]} + {c.In[1]} * {c.In[2]}" }, "Moves the UV over time");

        // ---------- 計算 (Dynamic : 繋いだ物の大きさ) ----------
        void binary(string type, string title, string op, string a, string b, string help) =>
            add(type, "Math", title, new[] { P("A", D, a), P("B", D, b) }, new[] { P("Out", D) }, c => new[] { string.Format(op, c.In[0], c.In[1]) }, help);
        binary("Add", "Add", "({0} + {1})", "0", "0", "A + B");
        binary("Subtract", "Subtract", "({0} - {1})", "0", "0", "A - B");
        binary("Multiply", "Multiply", "({0} * {1})", "1", "1", "A × B (each component)");
        binary("Divide", "Divide", "({0} / {1})", "1", "1", "A ÷ B");
        binary("Minimum", "Minimum", "min({0}, {1})", "0", "0", "The smaller one");
        binary("Maximum", "Maximum", "max({0}, {1})", "0", "0", "The larger one");
        binary("Power", "Power", "pow(max({0}, 0.0f), {1})", "1", "2", "A to the power of B (A below 0 is treated as 0)");
        binary("Modulo", "Modulo", "fmod({0}, {1})", "0", "1", "The remainder of A ÷ B");
        add("Step", "Math", "Step", new[] { P("Edge", D, "0.5"), P("In", D, "0") }, new[] { P("Out", D) }, c => new[] { $"step({c.In[0]}, {c.In[1]})" }, "1 if In ≥ Edge, else 0");
        add("Lerp", "Math", "Lerp", new[] { P("A", D, "0"), P("B", D, "1"), P("T", D, "0.5") }, new[] { P("Out", D) }, c => new[] { $"lerp({c.In[0]}, {c.In[1]}, {c.In[2]})" }, "A + (B - A) × T");
        add("Clamp", "Math", "Clamp", new[] { P("In", D, "0"), P("Min", D, "0"), P("Max", D, "1") }, new[] { P("Out", D) }, c => new[] { $"clamp({c.In[0]}, {c.In[1]}, {c.In[2]})" }, "Keeps In between Min and Max");
        add("Smoothstep", "Math", "Smoothstep", new[] { P("Edge0", D, "0"), P("Edge1", D, "1"), P("In", D, "0.5") }, new[] { P("Out", D) }, c => new[] { $"smoothstep({c.In[0]}, {c.In[1]}, {c.In[2]})" }, "Smooth 0 → 1 between the edges");
        add("Remap", "Math", "Remap", new[] { P("In", D, "0.5"), P("InMin", D, "0"), P("InMax", D, "1"), P("OutMin", D, "0"), P("OutMax", D, "1") }, new[] { P("Out", D) },
            c => new[] { $"({c.In[3]} + ({c.In[0]} - {c.In[1]}) / ({c.In[2]} - {c.In[1]}) * ({c.In[4]} - {c.In[3]}))" }, "Maps In from [InMin, InMax] to [OutMin, OutMax]");
        void unary(string type, string title, string op, string help, string def = "0") =>
            add(type, "Math", title, new[] { P("In", D, def) }, new[] { P("Out", D) }, c => new[] { string.Format(op, c.In[0]) }, help);
        unary("OneMinus", "One Minus", "(1.0f - {0})", "1 - In");
        unary("Negate", "Negate", "(-{0})", "-In");
        unary("Absolute", "Absolute", "abs({0})", "|In|");
        unary("Saturate", "Saturate", "saturate({0})", "Clamps to 0..1");
        unary("Floor", "Floor", "floor({0})", "Rounds down");
        unary("Fraction", "Fraction", "frac({0})", "The part after the point");
        unary("SquareRoot", "Square Root", "sqrt(max({0}, 0.0f))", "√In", "1");
        unary("Sine", "Sine", "sin({0})", "sin(In) (radians)");
        unary("Cosine", "Cosine", "cos({0})", "cos(In) (radians)");
        unary("Exponential", "Exponential", "exp({0})", "e^In");

        // ---------- ベクトル ----------
        add("Dot", "Vector", "Dot Product", new[] { P("A", D, "(0, 0, 1)"), P("B", D, "(0, 0, 1)") }, new[] { P("Out", F1) }, c => new[] { $"dot({c.In[0]}, {c.In[1]})" }, "A · B");
        add("Cross", "Vector", "Cross Product", new[] { P("A", F3, "(1, 0, 0)"), P("B", F3, "(0, 1, 0)") }, new[] { P("Out", F3) }, c => new[] { $"cross({c.In[0]}, {c.In[1]})" }, "A × B");
        add("Normalize", "Vector", "Normalize", new[] { P("In", D, "(0, 0, 1)") }, new[] { P("Out", D) }, c => new[] { c.Dynamic == 1 ? $"sign({c.In[0]})" : $"normalize({c.In[0]})" }, "Makes the length 1");
        add("Length", "Vector", "Length", new[] { P("In", D, "(0, 0, 1)") }, new[] { P("Out", F1) }, c => new[] { $"length({c.In[0]})" }, "The length");
        add("Distance", "Vector", "Distance", new[] { P("A", D, "(0, 0, 0)"), P("B", D, "(0, 0, 0)") }, new[] { P("Out", F1) }, c => new[] { $"distance({c.In[0]}, {c.In[1]})" }, "Distance between A and B");
        add("Fresnel", "Vector", "Fresnel", new[] { P("Normal", F3, "@normal"), P("View", F3, "@view"), P("Power", F1, "5") }, new[] { P("Out", F1) },
            c => new[] { $"pow(1.0f - saturate(dot(normalize({c.In[0]}), -normalize({c.In[1]}))), max({c.In[2]}, 0.0f))" }, "Bright at the edges (1 where the surface faces away)");

        // ---------- ノイズ ----------
        add("ValueNoise", "Noise", "Value Noise", new[] { P("UV", F2, "@uv"), P("Scale", F1, "10") }, new[] { P("Out", F1) }, c => new[] { $"_sgValueNoise({c.In[0]} * {c.In[1]})" }, "Smooth random values (0..1)");
        add("GradientNoise", "Noise", "Gradient Noise", new[] { P("UV", F2, "@uv"), P("Scale", F1, "10") }, new[] { P("Out", F1) }, c => new[] { $"_sgGradientNoise({c.In[0]} * {c.In[1]})" }, "Perlin noise (0..1)");
        add("Voronoi", "Noise", "Voronoi", new[] { P("UV", F2, "@uv"), P("Scale", F1, "5") }, new[] { P("Distance", F1), P("Cell", F1) }, c =>
        {
            var v = c.Temp("float2", $"_sgVoronoi({c.In[0]} * {c.In[1]})");
            return new[] { $"{v}.x", $"{v}.y" };
        }, "Cells (distance to the nearest point / a random value per cell)");
        add("Noise3D", "Noise", "Noise 3D", new[] { P("Position", F3, "@worldpos"), P("Scale", F1, "0.1") }, new[] { P("Out", F1), P("RGBA", F4) }, c =>
        {
            var kind = c.Prop("Kind");
            var size = (int)Math.Clamp(c.PropFloat("Size"), 8, 256);
            var name = $"Noise3D_{c.Node.Id}";
            c.Parameter($"texture3d {name} builtin:{kind}:{size}");
            var t = c.Temp("float4", $"{name}.Sample(g_Sampler, {c.In[0]} * {c.In[1]})");
            return new[] { kind == "PerlinWorley3D" ? $"{t}.b" : $"{t}.r", t };
        }, "Tileable 3D noise made by the engine (a 3D texture)",
            new[] { new NodePropertyDef("Kind", NodePropertyKind.Enum, "Perlin3D", Noise3D.Kinds), new NodePropertyDef("Size", NodePropertyKind.Float, "64") });

        // ---------- 分解 / 合成 ----------
        add("Split", "Channel", "Split", new[] { P("In", F4, "(0, 0, 0, 0)") }, new[] { P("R", F1), P("G", F1), P("B", F1), P("A", F1) },
            c => new[] { $"{c.In[0]}.x", $"{c.In[0]}.y", $"{c.In[0]}.z", $"{c.In[0]}.w" }, "Takes the components apart");
        add("Combine", "Channel", "Combine", new[] { P("R", F1, "0"), P("G", F1, "0"), P("B", F1, "0"), P("A", F1, "1") }, new[] { P("RGBA", F4), P("RGB", F3), P("RG", F2) },
            c => new[] { $"float4({c.In[0]}, {c.In[1]}, {c.In[2]}, {c.In[3]})", $"float3({c.In[0]}, {c.In[1]}, {c.In[2]})", $"float2({c.In[0]}, {c.In[1]})" }, "Makes a vector from numbers");

        // ---------- 出力 ----------
        add("Output", "Output", "Output", new[]
            {
                P("BaseColor", F3, "(0.8, 0.8, 0.8)"), P("Alpha", F1, "1"), P("Emission", F3, "(0, 0, 0)"), P("Metallic", F1, "0"), P("Roughness", F1, "0.5"),
                P("Normal", F3, "@normal"), P("AlphaClip", F1, "0"),
            }, none, c =>
            {
                var lit = c.Prop("Lighting") != "Unlit";
                var color = lit ? c.Temp("float3", $"Lit({c.In[0]}, {c.In[5]}, IN.WorldPosition, saturate({c.In[3]}), clamp({c.In[4]}, 0.02f, 1.0f)) + {c.In[2]}")
                                : c.Temp("float3", $"{c.In[0]} + {c.In[2]}");
                if (c.Connected[6] || (float.TryParse(c.Node.Values.GetValueOrDefault("AlphaClip", "0"), NumberStyles.Float, CultureInfo.InvariantCulture, out var clip) && clip > 0))
                    c.Line($"clip({c.In[1]} - {c.In[6]});");
                c.Line($"return float4({color}, {c.In[1]});");
                return Array.Empty<string>();
            }, "The surface (Lit : scene lighting with BaseColor / Metallic / Roughness. Unlit : BaseColor as is)", new[]
            {
                new NodePropertyDef("Lighting", NodePropertyKind.Enum, "Lit", new[] { "Lit", "Unlit" }),
                new NodePropertyDef("Queue", NodePropertyKind.Enum, "Opaque", new[] { "Opaque", "Transparent" }),
                new NodePropertyDef("Blend", NodePropertyKind.Enum, "Auto", new[] { "Auto", "Opaque", "Alpha", "Additive", "Premultiplied" }),
                new NodePropertyDef("Cull", NodePropertyKind.Enum, "Back", new[] { "Back", "Front", "None" }),
                new NodePropertyDef("ZWrite", NodePropertyKind.Enum, "Auto", new[] { "Auto", "On", "Off" }),
            }, isOutput: true);

        return list.ToDictionary(d => d.Type, StringComparer.Ordinal);
    }
}
