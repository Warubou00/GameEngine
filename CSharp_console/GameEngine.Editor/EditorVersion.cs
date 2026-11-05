using System.Reflection;

namespace GameEngine.Editor;

// ==========================================================================
// エディターの版 (ED9)。GameEngine.Editor.csproj の <Version>。ランチャーと同じ読み方
//   Version : "0.9.0" / Commit : git のコミット (SDK が InformationalVersion の "+" の後ろに付けた物。無ければ "")
//   プロジェクトを開くと、プロジェクトのフォルダの ProjectVersion.txt にこの版を書く (ランチャーが「どの版で開いたか」を出す)
// ==========================================================================
public static class EditorVersion
{
    private static readonly string Informational =
        typeof(EditorVersion).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.0.0";

    public static string Version => Informational.Split('+')[0];
    public static string Commit  => Informational.Contains('+') ? Informational.Split('+')[1] : "";
    public static string Display => Commit.Length > 0 ? $"{Version} ({Commit[..Math.Min(7, Commit.Length)]})" : Version;

    public const string ProjectVersionFile = "ProjectVersion.txt";

    // プロジェクトに、開いたエディターの版を書く ("EditorVersion: 0.9.0")。git に入れるので、コミットは書かない / 同じなら書かない
    public static void WriteProjectVersion(string projectRoot)
    {
        try
        {
            var file = Path.Combine(projectRoot, ProjectVersionFile);
            var text = $"EditorVersion: {Version}\r\n";
            if (File.Exists(file) && File.ReadAllText(file) == text) return;
            File.WriteAllText(file, text, new System.Text.UTF8Encoding(false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[Editor] Cannot write {ProjectVersionFile} : {ex.Message}");
        }
    }
}
