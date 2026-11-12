using System.Text;
using System.Text.Json;

namespace GameEngine;

// ==========================================================================
// 書き出したゲームの設定 (ED8 後半)。書き出したフォルダの Data/player.json
//   エディターの Build Game の窓で決め、書き出す時に書く。Player (GameEngine.Player.exe) が起動した時に読む
//   シーンのパスはアセットのパス ("Assets/Scenes/...")。最初のシーンは StartScene (無ければ Scenes の 0 番)
// ==========================================================================
public sealed class PlayerSettings
{
    public const string FileName = "player.json";

    public string       ProductName  { get; set; } = "Game";   // 窓のタイトル / exe の名前
    public int          Width        { get; set; } = 1280;     // 描画領域の大きさ
    public int          Height       { get; set; } = 720;
    public bool         VSync        { get; set; } = true;
    public bool         ShowProfiler { get; set; }             // C++ のプロファイラーを最初から出すか (F1 で切り替え)
    public bool         RunInBackground { get; set; }          // v1.8 : 後ろにある時も動かすか (false = 止めて CPU / GPU を休ませる。Engine.RunInBackground)
    public string       StartScene   { get; set; } = "";
    public List<string> Scenes       { get; set; } = new();     // 入れるシーン (ゲームのコードが engine.LoadScene で切り替える物も)

    public string FirstScene => !string.IsNullOrEmpty(StartScene) ? StartScene : Scenes.FirstOrDefault() ?? "";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);
    public static PlayerSettings FromJson(string json) => JsonSerializer.Deserialize<PlayerSettings>(json, JsonOptions) ?? new PlayerSettings();
    public static PlayerSettings Load(string file) => File.Exists(file) ? FromJson(File.ReadAllText(file, Encoding.UTF8)) : new PlayerSettings();
    public void Save(string file) => File.WriteAllText(file, ToJson(), new UTF8Encoding(false));
}
