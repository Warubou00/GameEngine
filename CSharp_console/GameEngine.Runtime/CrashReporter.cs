using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace GameEngine;

// ==========================================================================
// クラッシュレポート (v1.8)。落ちた時に、原因を調べるための物をフォルダにまとめて自動で書く
//   <Root>/Crash_yyyyMMdd_HHmmss/
//     report.txt : C# の例外 (全文 / 内側の例外 / スタック)、版、OS、.NET、CPU / メモリ、起動からの時間、エンジンの様子、読み込んだ DLL
//     crash.dmp  : ミニダンプ (Visual Studio で開くと落ちた所が見える)
//     native.txt : C++ 側で落ちた時 (アクセス違反 等)。例外のコード / モジュール + オフセット / スタック (Core.dll の crashHandler.cpp)
//     log.txt    : このセッションのログ (Console の出力。C++ のログも)
//   Root : Install の preferredRoot (書けなければ %LOCALAPPDATA%\<エンジンの名前>\CrashReports\<appName>)。新しい MaxReports 個だけ残す
//
//   受け取る物 : AppDomain.UnhandledException / WinForms のイベントの中の例外 (ThrowException にして外へ出す) / 呼ぶ側の catch (Report)
//                / C++ の例外 (Core.dll の SetUnhandledExceptionFilter)
//   ランチャー (GameEngine.Runtime を参照しない) もこのファイルをそのまま入れて使うので、エンジンの他の型は使わない
// ==========================================================================
public static class CrashReporter
{
    private const string CoreDll = "Core.dll";
    private static readonly object _lock = new();
    private static readonly Dictionary<string, string> _info = new();
    private static SessionLogWriter? _sessionLog;
    private static bool _nativeInstalled;
    private static Exception? _reported;   // 同じ例外で 2 つ作らない (Player の catch → UnhandledException 等)

    /// <summary>レポートを置くフォルダ (Install の後)</summary>
    public static string? Root { get; private set; }

    /// <summary>レポートに書くアプリの名前 ("Editor" / "Player" / "Launcher" / ゲームの名前)</summary>
    public static string AppName { get; private set; } = "";

    /// <summary>残すレポートの数 (古い物から消す)</summary>
    public static int MaxReports { get; set; } = 10;

    /// <summary>落ちた時に、レポートの場所を箱で知らせるか (自動の確認では false)</summary>
    public static bool ShowDialog { get; set; } = true;

    /// <summary>レポートに書くエンジンの様子 (Engine が入れる。フレーム数 / 再生中か 等)</summary>
    public static Func<string>? StateProvider { get; set; }

    /// <summary>最後に書いたレポートのフォルダ</summary>
    public static string? LastReportFolder { get; private set; }

    /// <summary>C++ 側 (Core.dll) のクラッシュも受け取っているか</summary>
    public static bool NativeInstalled => _nativeInstalled;

    /// <summary>
    /// 受け取りを始める。Main の最初に (窓を作る前に) 呼ぶ。Console.SetOut で書き先を替えるなら、その後に呼ぶ (そのログも写すため)
    /// </summary>
    public static void Install(string appName, string? preferredRoot = null, bool continueOnUIException = false)
    {
        AppName = appName;
        Root = chooseRoot(preferredRoot);
        if (Root == null) return;
        prune(MaxReports);
        openSessionLog();

        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            if (e.ExceptionObject is Exception ex) Report(ex, $"Unhandled exception (thread \"{Thread.CurrentThread.Name ?? Environment.CurrentManagedThreadId.ToString()}\")");
        };
        TaskScheduler.UnobservedTaskException += (_, e) => Console.WriteLine($"[Crash] Unobserved task exception : {e.Exception.GetBaseException().Message}");
        // WinForms のイベントの中の例外 : 以前は WinForms の既定の箱 (続ける / 終わる) が出て、何も残らなかった
        //   continueOnUIException (エディター) : レポートを書いて続ける (落とさない)。それ以外 : 外へ出して (呼ぶ側の catch / UnhandledException で) 書く
        try
        {
            if (continueOnUIException)
            {
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
                Application.ThreadException += (_, e) =>
                {
                    Console.WriteLine($"[Crash] Exception in a UI event (continued) : {e.Exception}");
                    Report(e.Exception, "Exception in a UI event (continued)", fatal: false);
                };
            }
            else Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
        }
        catch (InvalidOperationException) { }   // 窓を作った後だと替えられない (その時は既定のまま)

        try
        {
            _nativeInstalled = Crash_Install(Root, _sessionLog?.FilePath, appName);
            foreach (var (key, value) in _info) Crash_SetInfo(key, value);
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            _nativeInstalled = false;   // Core.dll の無い exe (ランチャー) は C# の例外だけ
        }
        Console.WriteLine($"[Crash] Crash reports : {Root}{(_nativeInstalled ? "" : " (managed only)")}");
    }

    /// <summary>レポートに書く情報 (今のシーン 等)。value = null で消す。C++ のクラッシュのレポートにも書かれる</summary>
    public static void SetInfo(string key, string? value)
    {
        lock (_lock)
        {
            if (value == null) _info.Remove(key);
            else _info[key] = value;
        }
        if (!_nativeInstalled) return;
        try { Crash_SetInfo(key, value); }
        catch (Exception) { }
    }

    /// <summary>
    /// 例外のレポートを書く。戻り値 = 書いたフォルダ (書けなければ null)
    /// fatal = true なら ShowDialog の時に箱で知らせる (この後プロセスが終わる時)
    /// </summary>
    public static string? Report(Exception exception, string kind = "Unhandled exception", bool fatal = true)
    {
        lock (_lock)
        {
            if (ReferenceEquals(_reported, exception)) return LastReportFolder;
            _reported = exception;
            string? folder = null;
            try
            {
                if (Root == null) return null;
                // C++ 側が先に書いていれば (C++ で落ちた後に .NET が例外にした時)、同じフォルダに足す
                folder = nativeReportFolder() ?? createReportFolder(Root);
                if (folder == null) return null;
                File.WriteAllText(Path.Combine(folder, "report.txt"), buildReport(exception, kind), new UTF8Encoding(true));
                var dump = Path.Combine(folder, "crash.dmp");
                if (!File.Exists(dump)) writeMiniDump(dump);
                _sessionLog?.Flush();
                if (_sessionLog != null && File.Exists(_sessionLog.FilePath))
                {
                    using var source = new FileStream(_sessionLog.FilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
                    using var target = File.Create(Path.Combine(folder, "log.txt"));
                    source.CopyTo(target);
                }
                if (_nativeInstalled && fatal) Crash_SetHandled();   // 続ける時 (fatal = false) は、この後の C++ のクラッシュもまた書く
                LastReportFolder = folder;
                Console.WriteLine($"[Crash] Crash report saved : {folder}");
                _sessionLog?.Flush();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Crash] Cannot write the crash report : {ex.Message}");
            }
            if (fatal && folder != null && ShowDialog && Environment.UserInteractive) showDialog(exception, folder);
            return folder;
        }
    }

    /// <summary>普通に終わる時に呼ぶ (このセッションのログを消す。落ちた時だけレポートに残る)</summary>
    public static void Shutdown()
    {
        var log = _sessionLog;
        if (log == null) return;
        _sessionLog = null;
        log.CloseAndDelete();
        // 1 回も落ちていなければ、空のフォルダを残さない (ゲームのフォルダに CrashReports が増えないように)
        try { if (Root != null && !Directory.EnumerateFileSystemEntries(Root).Any()) Directory.Delete(Root); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    /// <summary>
    /// 確認用にわざと落とす (--crash-test)。managed = C# の例外 / native = C++ でアクセス違反 / thread = C++ の別のスレッドで
    /// / stackoverflow = C++ でスタックオーバーフロー
    /// </summary>
    public static void TestCrash(string kind)
    {
        Console.WriteLine($"[Crash] Crash test : {kind}");
        _sessionLog?.Flush();
        switch (kind)
        {
            case "managed":       throw new InvalidOperationException("Crash test (managed exception)");
            case "native":        Crash_Test(1); break;
            case "thread":        Crash_Test(2); break;
            case "stackoverflow": Crash_Test(3); break;
            default: Console.WriteLine($"[Crash] Unknown crash test : {kind} (managed / native / thread / stackoverflow)"); break;
        }
    }

    // ---------- 中身 ----------

    // 書けるフォルダ (preferredRoot → %LOCALAPPDATA%)
    private static string? chooseRoot(string? preferredRoot)
    {
        var product = Assembly.GetEntryAssembly()?.GetCustomAttribute<AssemblyProductAttribute>()?.Product ?? "GameEngine";
        var fallback = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), product, "CrashReports", AppName);
        foreach (var candidate in new[] { preferredRoot, fallback })
        {
            if (string.IsNullOrEmpty(candidate)) continue;
            try
            {
                Directory.CreateDirectory(candidate);
                var probe = Path.Combine(candidate, $".write_test_{Environment.ProcessId}");
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return Path.GetFullPath(candidate);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        return null;
    }

    private static void openSessionLog()
    {
        // 前に強制終了した物のログは消す (動いている別のプロセスの物は開かれているので消せない)
        foreach (var old in Directory.EnumerateFiles(Root!, "Session_*.log"))
        {
            try { File.Delete(old); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
        try
        {
            _sessionLog = new SessionLogWriter(Console.Out, Path.Combine(Root!, $"Session_{Environment.ProcessId}.log"));
            Console.SetOut(_sessionLog);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _sessionLog = null;
        }
    }

    // 新しい物から keep 個を残して消す
    private static void prune(int keep)
    {
        try
        {
            var folders = Directory.GetDirectories(Root!, "Crash_*").OrderByDescending(f => f, StringComparer.Ordinal).ToList();
            foreach (var folder in folders.Skip(Math.Max(keep, 1)))
            {
                try { Directory.Delete(folder, true); }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    private static string? createReportFolder(string root)
    {
        var baseName = Path.Combine(root, $"Crash_{DateTime.Now:yyyyMMdd_HHmmss}");
        var folder = baseName;
        for (int i = 2; Directory.Exists(folder); i++) folder = $"{baseName}_{i}";
        Directory.CreateDirectory(folder);
        prune(MaxReports);
        return folder;
    }

    private static string? nativeReportFolder()
    {
        if (!_nativeInstalled) return null;
        try
        {
            var buffer = new char[1024];
            var length = Crash_GetReportFolder(buffer, buffer.Length);
            return length > 0 && length < buffer.Length ? new string(buffer, 0, length) : null;
        }
        catch (Exception) { return null; }
    }

    private static string buildReport(Exception exception, string kind)
    {
        var text = new StringBuilder();
        var entry = Assembly.GetEntryAssembly();
        string attribute<T>(Func<T, string> get) where T : Attribute => entry?.GetCustomAttribute<T>() is { } a ? get(a) : "?";
        void line(string name, object? value) => text.Append(name.PadRight(12)).Append(": ").Append(value).Append("\r\n");
        void safe(string name, Func<object?> value)
        {
            try { line(name, value()); }
            catch (Exception ex) { line(name, $"(unavailable : {ex.GetType().Name})"); }
        }

        text.Append("==== Crash report ====\r\n");
        line("App", AppName);
        line("Engine", $"{attribute<AssemblyProductAttribute>(a => a.Product)} {attribute<AssemblyInformationalVersionAttribute>(a => a.InformationalVersion.Split('+')[0])}");
        line("Time", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        safe("Uptime", () => (DateTime.Now - Process.GetCurrentProcess().StartTime).ToString(@"hh\:mm\:ss"));
        line("Exe", Environment.ProcessPath);
        line("Arguments", string.Join(' ', Environment.GetCommandLineArgs().Skip(1)));
        line("Directory", Environment.CurrentDirectory);
        line("OS", RuntimeInformation.OSDescription);
        line(".NET", $"{RuntimeInformation.FrameworkDescription} ({RuntimeInformation.ProcessArchitecture}) {Path.GetDirectoryName(typeof(object).Assembly.Location)}");
        safe("CPU", () => $"{Microsoft.Win32.Registry.GetValue(@"HKEY_LOCAL_MACHINE\HARDWARE\DESCRIPTION\System\CentralProcessor\0", "ProcessorNameString", "?")?.ToString()?.Trim()} ({Environment.ProcessorCount} threads)");
        safe("Memory", () =>
        {
            using var self = Process.GetCurrentProcess();
            var total = GC.GetGCMemoryInfo().TotalAvailableMemoryBytes;
            return $"working set {self.WorkingSet64 >> 20} MB / private {self.PrivateMemorySize64 >> 20} MB / managed heap {GC.GetTotalMemory(false) >> 20} MB / system {total >> 20} MB";
        });
        lock (_lock) foreach (var (key, value) in _info) line(key, value);
        if (StateProvider != null) safe("Engine state", () => StateProvider());

        text.Append("\r\n---- ").Append(kind).Append(" ----\r\n");
        text.Append(exception).Append("\r\n");
        // AggregateException 等の中身も (ToString に出ない物があるため)
        if (exception is AggregateException aggregate)
            foreach (var inner in aggregate.InnerExceptions) text.Append("\r\n-- inner --\r\n").Append(inner).Append("\r\n");

        text.Append("\r\n---- Loaded modules ----\r\n");
        try
        {
            using var self = Process.GetCurrentProcess();
            foreach (ProcessModule module in self.Modules)
            {
                text.Append("  ").Append(module.ModuleName.PadRight(40)).Append(' ')
                    .Append((module.FileVersionInfo.FileVersion ?? "").PadRight(24)).Append(' ').Append(module.FileName).Append("\r\n");
            }
        }
        catch (Exception ex) { text.Append("  (unavailable : ").Append(ex.Message).Append(")\r\n"); }
        return text.ToString();
    }

    // ミニダンプ (C# の例外の時。C++ の例外の時は Core.dll が例外の場所付きで書く)
    private static void writeMiniDump(string file)
    {
        try
        {
            using var stream = new FileStream(file, FileMode.Create, FileAccess.ReadWrite, FileShare.None);
            using var self = Process.GetCurrentProcess();
            const uint type = 0x00000040 | 0x00001000 | 0x00000020 | 0x00000004;   // IndirectlyReferencedMemory | ThreadInfo | UnloadedModules | HandleData
            if (!MiniDumpWriteDump(self.Handle, (uint)self.Id, stream.SafeFileHandle, type, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero))
                Console.WriteLine($"[Crash] MiniDumpWriteDump failed : 0x{Marshal.GetLastWin32Error():X}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Crash] Cannot write the dump : {ex.Message}");
        }
    }

    private static void showDialog(Exception exception, string folder)
    {
        try
        {
            var answer = MessageBox.Show($"{AppName} has crashed.\n\n{exception.GetType().Name} : {exception.Message}\n\n" +
                                         $"A crash report was saved to :\n{folder}\n\nOpen the folder?",
                                         $"{AppName} - Crash", MessageBoxButtons.YesNo, MessageBoxIcon.Error);
            if (answer == DialogResult.Yes) Process.Start(new ProcessStartInfo("explorer.exe", $"\"{folder}\"") { UseShellExecute = true });
        }
        catch (Exception) { }
    }

    // ---------- このセッションのログ (Console の出力を元の書き先とファイルの両方へ。行ごとにファイルへ書き出す) ----------
    // C++ で落ちた時も Core.dll がこのファイルを写すので、行の終わりで毎回 Flush する
    private sealed class SessionLogWriter : TextWriter
    {
        private readonly TextWriter _original;
        private readonly StreamWriter _file;
        private readonly object _writeLock = new();
        private bool _closed;

        public string FilePath { get; }

        public SessionLogWriter(TextWriter original, string path)
        {
            _original = original;
            FilePath = path;
            _file = new StreamWriter(new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete), new UTF8Encoding(false));
        }

        public override Encoding Encoding => _original.Encoding;

        public override void Write(char value)
        {
            lock (_writeLock)
            {
                _original.Write(value);
                if (_closed) return;
                _file.Write(value);
                if (value == '\n') _file.Flush();
            }
        }

        public override void Write(string? value)
        {
            if (value == null) return;
            lock (_writeLock)
            {
                _original.Write(value);
                if (_closed) return;
                _file.Write(value);
                if (value.Contains('\n')) _file.Flush();
            }
        }

        public override void Write(char[] buffer, int index, int count) => Write(new string(buffer, index, count));

        public override void WriteLine(string? value) => Write((value ?? "") + Environment.NewLine);

        public override void Flush()
        {
            lock (_writeLock)
            {
                _original.Flush();
                if (!_closed) _file.Flush();
            }
        }

        public void CloseAndDelete()
        {
            lock (_writeLock)
            {
                if (_closed) return;
                _closed = true;
                _file.Dispose();
            }
            try { File.Delete(FilePath); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
        }
    }

    // ---------- Core.dll (C++。Core/Source/Crash/crashDll.cpp) / dbghelp.dll (Windows) ----------
    [DllImport(CoreDll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Crash_Install(string root, string? sessionLog, string appName);

    [DllImport(CoreDll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern void Crash_SetInfo(string key, string? value);

    [DllImport(CoreDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void Crash_SetHandled();

    [DllImport(CoreDll, CallingConvention = CallingConvention.Cdecl, CharSet = CharSet.Unicode)]
    private static extern int Crash_GetReportFolder([Out] char[] buffer, int length);

    [DllImport(CoreDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void Crash_Test(int kind);

    [DllImport("dbghelp.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool MiniDumpWriteDump(IntPtr process, uint processId, SafeFileHandle file, uint dumpType,
                                                 IntPtr exceptionParam, IntPtr userStreamParam, IntPtr callbackParam);
}
