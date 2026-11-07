using System.Diagnostics;

namespace GameEngine.Editor;

// ==========================================================================
// dotnet (ゲームのコードのビルドに使う .NET SDK) の呼び出し (Package 化で足した)
//   Package : エディターの exe の 1 つ上の dotnet/dotnet.exe (<エンジン名>_v<版>/dotnet。同梱の SDK) を使う
//             .NET を入れていない PC でも、ゲームのコード (Scripts の .cs) をビルドできる
//   開発 (リポジトリのビルド) : 同梱の SDK が無いので、今まで通り PATH の dotnet を使う
//   (以前は ScriptHost / GameBuilder がそれぞれ "dotnet" を PATH から起動していた)
//   EngineAssemblyDir : ゲームの csproj に渡すエンジンの DLL の場所 (リポジトリの外のプロジェクトは GameEngine.Runtime.dll をここから参照する)
// ==========================================================================
internal static class DotnetTool
{
    /// <summary>同梱の dotnet.exe (無ければ null)</summary>
    public static string? Bundled { get; } = findBundled();

    /// <summary>Package のエディターか (同梱の .NET がある。リポジトリを探さない)</summary>
    public static bool IsPackaged => Bundled != null;

    /// <summary>エディターの exe のフォルダ (最後に \ 付き)。ゲームの csproj の EngineAssemblyDir / EditorAssemblyDir</summary>
    public static string EditorDir => AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

    private static string? findBundled()
    {
        var exe = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "dotnet", "dotnet.exe"));
        return File.Exists(exe) ? exe : null;
    }

    /// <summary>dotnet を起動する準備 (出力は読む / 窓は出さない)。引数は 1 つずつ足す</summary>
    public static ProcessStartInfo Create(string workingDirectory, IEnumerable<string> arguments)
    {
        var info = new ProcessStartInfo(Bundled ?? "dotnet")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true,
            WorkingDirectory = workingDirectory,
        };
        foreach (var argument in arguments) info.ArgumentList.Add(argument);   // 空白や \ を含むパスでも引用符を気にしなくてよい
        // 初めての起動の案内 / 利用状況の送信をしない (出力を読んでエラーを探すため。外部の PC で勝手に通信しない)
        info.Environment["DOTNET_NOLOGO"] = "1";
        info.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        info.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        info.Environment["DOTNET_GENERATE_ASPNET_CERTIFICATE"] = "false";
        if (Bundled != null)
        {
            // 同梱の SDK だけを使う (PC に別の .NET があっても混ぜない)
            info.Environment["DOTNET_ROOT"] = Path.GetDirectoryName(Bundled)!;
            info.Environment["DOTNET_MULTILEVEL_LOOKUP"] = "0";
            info.Environment.Remove("MSBuildSDKsPath");
            info.Environment.Remove("MSBUILD_EXE_PATH");
        }
        return info;
    }

    /// <summary>
    /// 同梱の SDK が残したビルドのサーバー (VBCSCompiler / MSBuild のノード) を止める (エディターを閉じる時。Package だけ)
    /// 止めないと、使わなくなってからも約 10 分残り、Package のフォルダを消す / 動かすことができなかった (確かめた時に分かった)
    /// 開発 (PC の dotnet) では止めない (他のビルドも使っているため)
    /// </summary>
    public static void ShutdownBuildServers()
    {
        if (Bundled == null) return;
        try
        {
            using var process = Process.Start(Create(AppContext.BaseDirectory, new[] { "build-server", "shutdown" }))!;
            process.StandardOutput.ReadToEndAsync();
            process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15000)) Console.WriteLine("[Scripts] dotnet build-server shutdown did not finish");
        }
        catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            Console.WriteLine($"[Scripts] Cannot stop the build servers : {ex.Message}");
        }
    }

    /// <summary>dotnet build (戻り値 : 成功したか / 出力)</summary>
    public static (bool Ok, string Log) Build(string project, string configuration, params string[] extra)
    {
        try
        {
            var arguments = new List<string> { "build", project, "-c", configuration, "-nologo", "-v", "q", "-clp:NoSummary", $"-p:EngineAssemblyDir={EditorDir}" };
            arguments.AddRange(extra);
            using var process = Process.Start(Create(Path.GetDirectoryName(project)!, arguments))!;
            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode == 0, output.Result + error);
        }
        catch (Exception ex)
        {
            var hint = Bundled == null ? " (.NET SDK not found : install the .NET 9 SDK)" : "";
            return (false, $"dotnet build could not run : {ex.Message}{hint}");
        }
    }
}
