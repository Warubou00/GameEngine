using System.Diagnostics;
using GameEngine;

namespace GameEngine.Player;

// ==========================================================================
// Data.pak を展開した一時フォルダ (v1.3)
//   %TEMP%\TsumugiPlayer\<番号>\ (隠し) に Data / Shader を書き出す。Game.dll はファイルにせず、中身だけ持つ
//   使っている間は .lock を開いたままにする。終わる時 (Dispose) にフォルダごと消す
//   落ちて消せなかった前の一時フォルダは、次の起動の時に消す (.lock を開けた = 誰も使っていない物だけ)
// ==========================================================================
internal sealed class UnpackedData : IDisposable
{
    public string Root { get; }
    public string Data => Path.Combine(Root, "Data");
    public byte[]? GameDll { get; }
    private readonly FileStream _lock;

    private UnpackedData(string root, FileStream lockFile, byte[]? gameDll)
    {
        Root = root;
        _lock = lockFile;
        GameDll = gameDll;
    }

    public static UnpackedData Open(string pakFile)
    {
        var start = Stopwatch.GetTimestamp();
        var parent = Path.Combine(Path.GetTempPath(), "TsumugiPlayer");
        Directory.CreateDirectory(parent);
        cleanStale(parent);
        var root = Path.Combine(parent, $"{Environment.ProcessId}_{Guid.NewGuid():N}");
        var directory = Directory.CreateDirectory(root);
        directory.Attributes |= FileAttributes.Hidden;
        var lockFile = new FileStream(Path.Combine(root, ".lock"), FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1, FileOptions.DeleteOnClose);
        try
        {
            using var pak = new Pak.Reader(pakFile);
            var (files, bytes) = pak.ExtractAll(root, name => name.Equals("Game.dll", StringComparison.OrdinalIgnoreCase));
            var gameDll = pak.ReadAllBytes("Game.dll");
            Console.WriteLine($"[Player] Unpacked {Pak.FileName} : {files} files, {bytes / 1024.0 / 1024.0:F1} MB in {Stopwatch.GetElapsedTime(start).TotalMilliseconds:F0} ms" +
                              $"{(gameDll != null ? " (Game.dll in memory)" : "")}");
            return new UnpackedData(root, lockFile, gameDll);
        }
        catch
        {
            lockFile.Dispose();
            tryDelete(root);
            throw;
        }
    }

    // 前の起動が残した一時フォルダ (使っていない物だけ) を消す
    private static void cleanStale(string parent)
    {
        foreach (var dir in Directory.EnumerateDirectories(parent))
        {
            var lockPath = Path.Combine(dir, ".lock");
            try
            {
                if (File.Exists(lockPath)) { using var probe = new FileStream(lockPath, FileMode.Open, FileAccess.ReadWrite, FileShare.None); }
            }
            catch (IOException) { continue; }   // 使っている (動いている Player)
            tryDelete(dir);
        }
    }

    private static void tryDelete(string dir)
    {
        try { Directory.Delete(dir, true); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }

    public void Dispose()
    {
        _lock.Dispose();
        tryDelete(Root);
    }
}
