using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace GameEngine.Editor;

// ==========================================================================
// スクリプトの雛形と、外のエディター (Visual Studio) で開く (v1.1。Unity の Create > C# Script / ダブルクリックと同じ)
//   - Project の右クリック > Create > Scripts > Behaviour : 今のフォルダに .cs を作る (コンポーネント + [GameSystem] の System)
//     Assets の .cs も Game.csproj がビルドする (Assets/**/Editor/ の下は GameEditor.csproj)
//   - .cs をダブルクリック : プロジェクトのフォルダに <プロジェクト名>.sln を作り (Game / GameEditor の csproj)、
//     Visual Studio でその sln と .cs を開く。同じ sln を開いている Visual Studio があれば、そこで .cs を開く
//     Visual Studio が無い PC : sln を Windows の関連付けで開く (無ければ .cs を関連付けで開く)
//   - Library/EngineReferences.props : IDE でビルドする時のエンジン / エディターの DLL の場所 (エディターのビルドは引数で渡すので要らない)
// ==========================================================================
internal static class ScriptEditor
{
    // ---------------------------------------------------------------------
    // 雛形
    // ---------------------------------------------------------------------

    public const string DefaultBehaviourName = "NewBehaviour";

    /// <summary>ファイルの名前を C# の型の名前にする (使えない字は _ / 数字で始まれば _ を付ける)</summary>
    public static string ToTypeName(string stem)
    {
        var name = new string(stem.Select(c => char.IsLetterOrDigit(c) || c == '_' ? c : '_').ToArray());
        if (name.Length == 0) return DefaultBehaviourName;
        return char.IsDigit(name[0]) ? "_" + name : name;
    }

    /// <summary>Behaviour の雛形 (コンポーネントの値 + 毎フレーム動かす System)</summary>
    public static string BehaviourTemplate(string typeName) => $$"""
        using System.Numerics;
        using GameEngine;

        namespace GameEngine_console;

        // ==========================================================================
        // {{typeName}}
        //   コンポーネント : Inspector の Add Component で物に付ける。public の値は Inspector で変えられ、シーンに保存される
        //   System : 再生中、毎フレーム Update が呼ばれる ([GameSystem] を付けると、エディターが見つけて登録する)
        // ==========================================================================
        public struct {{typeName}} : IComponent
        {
            public float Speed;
        }

        [GameSystem]
        public sealed class {{typeName}}System : ISystem
        {
            public void Update(World world)
            {
                world.ForEach((Entity entity, ref Transform transform, ref {{typeName}} data) =>
                {
                    // 例 : transform.Position += Vector3.UnitZ * data.Speed * Time.DeltaTime;
                });
            }
        }

        """.Replace("\r\n", "\n").Replace("\n", "\r\n");

    /// <summary>.cs を書く (UTF-8 BOM 付き / CRLF。Visual Studio の既定と同じ)</summary>
    public static void WriteScript(string file, string text) => File.WriteAllText(file, text, new UTF8Encoding(true));

    /// <summary>
    /// 作ったばかりの雛形の名前を変えた時 : 中の型の名前もファイルの名前に合わせる (Unity と同じ)。
    /// 雛形から書き換えていなければ書き直す。戻り値 = 書き直したか
    /// </summary>
    public static bool RenameTemplate(string file, string oldStem, string newStem)
    {
        if (!File.Exists(file)) return false;
        var text = File.ReadAllText(file);
        if (text.TrimStart('﻿') != BehaviourTemplate(ToTypeName(oldStem))) return false;
        WriteScript(file, BehaviourTemplate(ToTypeName(newStem)));
        return true;
    }

    // ---------------------------------------------------------------------
    // sln
    // ---------------------------------------------------------------------

    private const string CSharpSdkProjectType = "{9A19103F-16F7-4668-BE54-9A1E7A4F7556}";

    public static string SolutionPath(string projectRoot) =>
        Path.Combine(projectRoot, Path.GetFileName(Path.GetFullPath(projectRoot).TrimEnd('\\', '/')) + ".sln");

    /// <summary>sln を書く (中身が同じなら書かない : 開いている Visual Studio が「外で変わった」と聞かないように)。戻り値 = sln のパス</summary>
    public static string WriteSolution(string projectRoot)
    {
        var sln = SolutionPath(projectRoot);
        var projects = new List<(string Name, string Relative, string Guid)>();
        foreach (var relative in new[] { @"Scripts\Game.csproj", @"Scripts\Editor\GameEditor.csproj" })
            if (File.Exists(Path.Combine(projectRoot, relative))) projects.Add((Path.GetFileNameWithoutExtension(relative), relative, stableGuid(relative)));
        var text = new StringBuilder();
        text.Append("\r\nMicrosoft Visual Studio Solution File, Format Version 12.00\r\n# Visual Studio Version 17\r\n");
        text.Append("VisualStudioVersion = 17.0.31903.59\r\nMinimumVisualStudioVersion = 10.0.40219.1\r\n");
        foreach (var (name, relative, guid) in projects)
            text.Append($"Project(\"{CSharpSdkProjectType}\") = \"{name}\", \"{relative}\", \"{guid}\"\r\nEndProject\r\n");
        text.Append("Global\r\n\tGlobalSection(SolutionConfigurationPlatforms) = preSolution\r\n");
        text.Append("\t\tDebug|Any CPU = Debug|Any CPU\r\n\t\tRelease|Any CPU = Release|Any CPU\r\n\tEndGlobalSection\r\n");
        text.Append("\tGlobalSection(ProjectConfigurationPlatforms) = postSolution\r\n");
        foreach (var (_, _, guid) in projects)
            foreach (var configuration in new[] { "Debug", "Release" })
                text.Append($"\t\t{guid}.{configuration}|Any CPU.ActiveCfg = {configuration}|Any CPU\r\n\t\t{guid}.{configuration}|Any CPU.Build.0 = {configuration}|Any CPU\r\n");
        text.Append("\tEndGlobalSection\r\n\tGlobalSection(SolutionProperties) = preSolution\r\n\t\tHideSolutionNode = FALSE\r\n\tEndGlobalSection\r\nEndGlobal\r\n");
        writeIfChanged(sln, text.ToString(), new UTF8Encoding(true));
        WriteReferences(projectRoot);
        return sln;
    }

    /// <summary>Library/EngineReferences.props (IDE でビルドする時のエンジン / エディターの DLL の場所 = 今のエディターのフォルダ)</summary>
    public static string WriteReferences(string projectRoot)
    {
        var file = Path.Combine(projectRoot, "Library", "EngineReferences.props");
        var dir = DotnetTool.EditorDir;
        var text = "<Project>\r\n" +
                   "  <!-- エディターが書く (v1.1)。Visual Studio などでゲームの csproj をビルドする時の、エンジン / エディターの DLL の場所 -->\r\n" +
                   "  <PropertyGroup>\r\n" +
                   $"    <EngineAssemblyDir Condition=\"'$(EngineAssemblyDir)' == ''\">{dir}</EngineAssemblyDir>\r\n" +
                   $"    <EditorAssemblyDir Condition=\"'$(EditorAssemblyDir)' == ''\">{dir}</EditorAssemblyDir>\r\n" +
                   "  </PropertyGroup>\r\n</Project>\r\n";
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        writeIfChanged(file, text, new UTF8Encoding(false));
        return file;
    }

    private static void writeIfChanged(string file, string text, Encoding encoding)
    {
        if (File.Exists(file) && File.ReadAllText(file) == text) return;
        File.WriteAllText(file, text, encoding);
    }

    // パスから決まる Id (sln を書き直しても同じ Id にする)
    private static string stableGuid(string text) => new Guid(MD5.HashData(Encoding.UTF8.GetBytes(text.ToLowerInvariant()))).ToString("B").ToUpperInvariant();

    // ---------------------------------------------------------------------
    // 開く
    // ---------------------------------------------------------------------

    private static string? _devenv;
    private static bool _searched;

    /// <summary>Visual Studio (devenv.exe)。入っていなければ null</summary>
    public static string? VisualStudio
    {
        get
        {
            if (_searched) return _devenv;
            _searched = true;
            try
            {
                var vswhere = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
                if (!File.Exists(vswhere)) return null;
                var info = new ProcessStartInfo(vswhere) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true };
                foreach (var a in new[] { "-latest", "-property", "productPath" }) info.ArgumentList.Add(a);
                using var process = Process.Start(info)!;
                var path = process.StandardOutput.ReadToEnd().Trim();
                process.WaitForExit();
                if (path.EndsWith("devenv.exe", StringComparison.OrdinalIgnoreCase) && File.Exists(path)) _devenv = path;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or IOException) { }
            return _devenv;
        }
    }

    // 起動した Visual Studio が sln を読み終わる (窓のタイトルが "<sln の名前> - ...") のを 3 分まで待って、/edit でファイルを開く
    private static void openWhenReady(string devenv, Process started, string name, string file)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed.TotalMinutes < 3)
        {
            Thread.Sleep(1000);
            try
            {
                started.Refresh();
                if (started.HasExited) return;
                if (!started.MainWindowTitle.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase)) continue;
                Thread.Sleep(2000);   // 窓が出た直後は、プロジェクトをまだ読んでいる
                Process.Start(new ProcessStartInfo(devenv) { UseShellExecute = false, Arguments = $"/edit \"{file}\"" });
                return;
            }
            catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception) { return; }
        }
    }

    /// <summary>sln を作り、外のエディターで開く (file があればそのファイルも)。戻り値 = 何で開いたか (Console に出す)</summary>
    public static string Open(string projectRoot, string? file)
    {
        var sln = WriteSolution(projectRoot);
        try
        {
            if (VisualStudio is { } devenv)
            {
                // 同じ sln を開いている Visual Studio (窓のタイトルが "<sln の名前> - Microsoft Visual Studio")
                var name = Path.GetFileNameWithoutExtension(sln);
                var running = Process.GetProcessesByName("devenv").Any(p =>
                {
                    try { return p.MainWindowTitle.StartsWith(name + " ", StringComparison.OrdinalIgnoreCase); }
                    catch (InvalidOperationException) { return false; }
                });
                if (running && file != null)
                {
                    Process.Start(new ProcessStartInfo(devenv) { UseShellExecute = false, Arguments = $"/edit \"{file}\"" });
                    return $"Visual Studio (open {name}.sln) : {Path.GetFileName(file)}";
                }
                // 新しく開く : sln を開き、Visual Studio の窓に sln の名前が出たら (読み終わったら) /edit でそのファイルを開く
                //   (以前は /Command "File.OpenFile ..." で渡していたが、Visual Studio が引用符を読めず「ファイルが見つからない」と出た)
                var started = Process.Start(new ProcessStartInfo(devenv) { UseShellExecute = false, Arguments = $"\"{sln}\"" })!;
                if (file != null) Task.Run(() => openWhenReady(devenv, started, name, file));
                return $"Visual Studio : {Path.GetFileName(sln)}{(file != null ? $" / {Path.GetFileName(file)}" : "")}";
            }
            // Visual Studio が無い : sln の関連付け (Rider など) → 無ければ .cs の関連付け
            try
            {
                Process.Start(new ProcessStartInfo(sln) { UseShellExecute = true });
                return $"{Path.GetFileName(sln)} (Windows file association)";
            }
            catch (System.ComponentModel.Win32Exception) when (file != null)
            {
                Process.Start(new ProcessStartInfo(file) { UseShellExecute = true });
                return $"{Path.GetFileName(file)} (Windows file association; no app for .sln)";
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            return $"Cannot open {Path.GetFileName(file ?? sln)} : {ex.Message}";
        }
    }
}
