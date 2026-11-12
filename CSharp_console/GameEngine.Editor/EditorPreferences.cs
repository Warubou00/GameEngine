using System.Text;
using System.Text.Json;

namespace GameEngine.Editor;

// ==========================================================================
// エディターの設定 (v1.8)。プロジェクトに関係なく、このエディターの中で覚えておく物 (exe の隣の editor_prefs.json)
//   RunInBackground : エディターが後ろにある時も動かすか (View > Run In Background。既定 false = 止めて CPU / GPU を休ませる)
// ==========================================================================
internal sealed class EditorPreferences
{
    public const string FileName = "editor_prefs.json";
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public bool RunInBackground { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string? FilePath { get; private set; }

    public static EditorPreferences Load(string file)
    {
        EditorPreferences? preferences = null;
        try
        {
            if (File.Exists(file)) preferences = JsonSerializer.Deserialize<EditorPreferences>(File.ReadAllText(file, Encoding.UTF8), JsonOptions);
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[Editor] Cannot read {file} : {ex.Message}");
        }
        preferences ??= new EditorPreferences();
        preferences.FilePath = file;
        return preferences;
    }

    public void Save()
    {
        if (FilePath == null) return;
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOptions), new UTF8Encoding(false)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { Console.WriteLine($"[Editor] Cannot save {FilePath} : {ex.Message}"); }
    }
}
