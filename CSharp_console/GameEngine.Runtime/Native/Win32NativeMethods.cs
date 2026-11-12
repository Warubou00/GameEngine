using System.Runtime.InteropServices;

namespace GameEngine.Native;

// ==========================================================================
// Windows の関数 (v1.8 : ウィンドウが後ろにあるかの判定 / 止めている間のメッセージ待ち)
// ==========================================================================
internal static class Win32NativeMethods
{
    public const uint QS_ALLINPUT = 0x04FF;
    public const uint MWMO_INPUTAVAILABLE = 0x0004;

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    public static extern uint GetWindowThreadProcessId(IntPtr window, out uint processId);

    // メッセージが来るか timeout (ms) が過ぎるまで眠る (Thread.Sleep と違い、メッセージが来ればすぐ起きる)
    [DllImport("user32.dll")]
    public static extern uint MsgWaitForMultipleObjectsEx(uint count, IntPtr handles, uint milliseconds, uint wakeMask, uint flags);
}
