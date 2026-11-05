using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace GameEngine.Editor;

// ==========================================================================
// ソースのコメントから型の説明を読む (ED9 : スクリプトリファレンス)
//   型の前のコメント (// / ///。==== の線は除く) = 型の説明
//   フィールド / プロパティ / メソッドの行の後ろの // か、すぐ上のコメント = メンバーの説明
//   エンジン (GameEngine.Runtime) とエディターのソースは、エディターの exe に埋め込んである (ビルドした版と同じ説明。リポジトリの外でも読める)
//   ゲームのコード (Project/Scripts) はファイルから読む (保存すると読み直す)
//   ※ C# を正しく解析するのではなく、このエンジンのソースの書き方 (1 行 1 宣言 / コメントは前か後ろ) を前提に、行で読む
// ==========================================================================
internal sealed class SourceDocs
{
    internal sealed class TypeDoc
    {
        public string Name = "";
        public string Namespace = "";
        public string Summary = "";
        public string Source = "";       // ファイル (埋め込みの名前か、ファイルのパス)
        public int    Line;              // 1 から
        public bool   IsFile;            // Source が開けるファイルか
        public Dictionary<string, string> Members = new(StringComparer.Ordinal);
    }

    private readonly Dictionary<string, List<TypeDoc>> _types = new(StringComparer.Ordinal);
    public int FileCount { get; private set; }

    private static readonly Regex TypeDeclaration = new(
        @"^\s*(?:\[[^\]]*\]\s*)*(?:(?:public|internal|private|protected|readonly|static|sealed|abstract|partial|ref|unsafe|file)\s+)*(?:struct|class|interface|enum|record(?:\s+struct)?)\s+(\w+)",
        RegexOptions.Compiled);
    private static readonly Regex MemberDeclaration = new(
        @"^\s*(?:\[[^\]]*\]\s*)*public\s+(?:(?:static|readonly|const|override|virtual|abstract|unsafe|required|new|ref|extern)\s+)*[\w<>\[\],\.\?\(\) ]+?\s+(\w+)\s*(?:[;={(<]|=>|$)",
        RegexOptions.Compiled);
    private static readonly Regex EnumMember = new(@"^\s*(\w+)\s*(?:=[^,/]*)?,?\s*(?://.*)?$", RegexOptions.Compiled);
    private static readonly Regex Namespace = new(@"^\s*namespace\s+([\w\.]+)", RegexOptions.Compiled);
    private static readonly Regex XmlTag = new(@"<see\s+cref=""(?:\w:)?([^""]+)""\s*/>|</?\w+[^>]*>", RegexOptions.Compiled);

    // 埋め込んだソース (LogicalName が prefix で始まる物) を全部読む
    public void AddEmbedded(Assembly assembly, string prefix)
    {
        foreach (var name in assembly.GetManifestResourceNames().Where(n => n.StartsWith(prefix, StringComparison.Ordinal)))
        {
            using var stream = assembly.GetManifestResourceStream(name)!;
            using var reader = new StreamReader(stream, Encoding.UTF8);
            Add(name, reader.ReadToEnd(), isFile: false);
        }
    }

    // フォルダの .cs を全部読む (bin / obj は見ない)
    public void AddFolder(string folder)
    {
        if (!Directory.Exists(folder)) return;
        foreach (var file in Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(folder, file).Replace('\\', '/');
            if (relative.StartsWith("bin/") || relative.StartsWith("obj/") || relative.Contains("/bin/") || relative.Contains("/obj/")) continue;
            try { Add(file, File.ReadAllText(file, Encoding.UTF8), isFile: true); }
            catch (IOException) { }   // 保存中など
        }
    }

    public void Add(string source, string text, bool isFile)
    {
        FileCount++;
        var lines = text.Replace("\r\n", "\n").Split('\n');
        var ns = "";
        for (int i = 0; i < lines.Length; i++)
        {
            if (Namespace.Match(lines[i]) is { Success: true } nsMatch) { ns = nsMatch.Groups[1].Value; continue; }
            if (lines[i].TrimStart().StartsWith("//")) continue;
            var match = TypeDeclaration.Match(stripComment(lines[i]));
            if (!match.Success) continue;
            var doc = new TypeDoc { Name = match.Groups[1].Value, Namespace = ns, Summary = commentAbove(lines, i), Source = source, Line = i + 1, IsFile = isFile };
            readMembers(lines, i, doc, isEnum: Regex.IsMatch(lines[i], @"\benum\s+"));
            if (!_types.TryGetValue(doc.Name, out var list)) _types[doc.Name] = list = new List<TypeDoc>();
            list.Add(doc);
        }
    }

    // 型の説明を探す (同じ名前が複数ある時は名前空間の合う物 / 説明のある物)
    public TypeDoc? Find(Type type)
    {
        var name = type.IsGenericType ? type.Name[..type.Name.IndexOf('`')] : type.Name;
        if (!_types.TryGetValue(name, out var list)) return null;
        return list.FirstOrDefault(d => d.Namespace == (type.Namespace ?? "") && d.Summary.Length > 0)
            ?? list.FirstOrDefault(d => d.Namespace == (type.Namespace ?? ""))
            ?? list.FirstOrDefault(d => d.Summary.Length > 0) ?? list[0];
    }

    // 宣言のすぐ上のコメント (属性の行は飛ばす)
    private static string commentAbove(string[] lines, int index)
    {
        var j = index - 1;
        while (j >= 0 && lines[j].TrimStart().StartsWith("[") && !lines[j].TrimStart().StartsWith("//")) j--;
        var collected = new List<string>();
        for (; j >= 0; j--)
        {
            var trimmed = lines[j].Trim();
            if (!trimmed.StartsWith("//")) break;
            collected.Add(clean(trimmed));
        }
        collected.Reverse();
        // 線 (====) と前後の空の行を除く
        var result = collected.Where(l => !Regex.IsMatch(l, @"^[=\-─]{4,}$")).ToList();
        while (result.Count > 0 && result[0].Length == 0) result.RemoveAt(0);
        while (result.Count > 0 && result[^1].Length == 0) result.RemoveAt(result.Count - 1);
        return string.Join("\n", result);
    }

    // "//" / "///" と XML の印を取る (字下げの 2 つ目からの空白は残す : 「//   続き」のような書き方)
    private static string clean(string comment)
    {
        var text = comment.StartsWith("///") ? comment[3..] : comment[2..];
        if (text.StartsWith(' ')) text = text[1..];
        text = XmlTag.Replace(text, m => m.Groups[1].Success ? m.Groups[1].Value : "");
        return text.Replace("&lt;", "<").Replace("&gt;", ">").Replace("&amp;", "&").TrimEnd();
    }

    // 型の中 (中括弧の 1 段目) の public なメンバー
    private static void readMembers(string[] lines, int start, TypeDoc doc, bool isEnum)
    {
        var depth = 0;
        var opened = false;
        for (int i = start; i < lines.Length; i++)
        {
            var code = stripComment(lines[i]);
            var depthAtStart = depth;
            foreach (var c in stripStrings(code))
            {
                if (c == '{') { depth++; opened = true; }
                else if (c == '}') depth--;
            }
            if (!opened) continue;
            if (opened && depth <= 0 && i > start) return;
            if (depthAtStart != 1 || i == start) continue;   // 型の中のすぐの段だけ
            string? name = null;
            if (isEnum)
            {
                if (EnumMember.Match(lines[i]) is { Success: true } e && code.Trim().Length > 0 && !code.Contains('{')) name = e.Groups[1].Value;
            }
            else if (MemberDeclaration.Match(code) is { Success: true } m)
            {
                name = m.Groups[1].Value;
                if (name is "class" or "struct" or "enum" or "interface" or "record") name = null;   // 中の型
            }
            if (name == null) continue;
            var trailing = trailingComment(lines[i]);
            var above = commentAbove(lines, i);
            var text = string.Join("\n", new[] { above, trailing }.Where(s => !string.IsNullOrEmpty(s)));
            if (text.Length > 0 && !doc.Members.ContainsKey(name)) doc.Members[name] = text;
        }
    }

    private static string trailingComment(string line)
    {
        var index = commentStart(line);
        return index < 0 || line[..index].Trim().Length == 0 ? "" : clean(line[index..].Trim());
    }

    private static string stripComment(string line)
    {
        var index = commentStart(line);
        return index < 0 ? line : line[..index];
    }

    // 文字列の外の "//" の位置 (無ければ -1)
    private static int commentStart(string line)
    {
        var inString = false;
        var inChar = false;
        for (int i = 0; i < line.Length - 1; i++)
        {
            var c = line[i];
            if (c == '\\' && (inString || inChar)) { i++; continue; }
            if (c == '"' && !inChar) inString = !inString;
            else if (c == '\'' && !inString) inChar = !inChar;
            else if (!inString && !inChar && c == '/' && line[i + 1] == '/') return i;
        }
        return -1;
    }

    private static string stripStrings(string code) => Regex.Replace(code, @"""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'", "");
}
