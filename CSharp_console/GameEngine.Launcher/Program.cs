using System.Text;

namespace GameEngine.Launcher;

// ==========================================================================
// ランチャー (ED9) : GameEngine.Launcher.exe [--data <folder>] [--test]
//   --data : 設定 (launcher.json) と入れた版 (Editors) の置き場。既定は %LOCALAPPDATA%\GameEngine
//   --test : 窓を出さずに確認して終わる (LauncherTest。結果は標準出力と終了コード。--keep で入れた版を残す)
//   --tab installs : 最初に Installs の頁を出す
// ==========================================================================
internal static class Program
{
    [STAThread]
    public static int Main(string[] args)
    {
        var options = new Dictionary<string, string>();
        for (int i = 0; i + 1 < args.Length; i++) if (args[i].StartsWith("--") && !args[i + 1].StartsWith("--")) options[args[i]] = args[++i];
        var data = options.GetValueOrDefault("--data", Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameEngine"));

        if (args.Contains("--test"))
        {
            // WinExe なので、親のコンソールに出す (無ければ出ない。終了コードで分かる)
            AttachConsole(-1);
            var stdout = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = true };
            Console.SetOut(stdout);
            return LauncherTest.Run(new EditorInstalls(data), options, keep: args.Contains("--keep"));
        }

        ApplicationConfiguration.Initialize();
        var form = new LauncherForm(new EditorInstalls(data));
        if (options.GetValueOrDefault("--tab") == "installs") form.ShowInstalls();   // 最初に Installs の頁を出す
        Application.Run(form);
        return 0;
    }

    [System.Runtime.InteropServices.DllImport("kernel32.dll")]
    private static extern bool AttachConsole(int processId);
}
