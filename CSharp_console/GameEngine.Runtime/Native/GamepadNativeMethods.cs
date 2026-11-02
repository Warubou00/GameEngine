using System.Runtime.InteropServices;

namespace GameEngine.Native;

// ==========================================================================
// Core.dll (C++) のコントローラーの関数の宣言
//   C++ 側は Core/Source/Controller/controllerDll.cpp (XInput と DirectInput のパッドを 4 台までまとめる)
//   - 毎フレームの状態は Controller_Update 1 回で、全パッド分を配列で受け取る
//   - ログは Core.dll の Logger に出る (Renderer_SetLogCallback で設定したものと同じ)
//   エンジンの利用者は直接使わない (Gamepad 経由で使う)
// ==========================================================================
internal static unsafe class GamepadNativeMethods
{
    private const string Dll = "Core.dll";

    // C++ の GAMEPAD_MAX_PADS と同じ
    public const int MaxPads = 4;

    // C++ の ControllerPadState と同じ並び (36 バイト)
    [StructLayout(LayoutKind.Sequential)]
    public struct PadState
    {
        public uint  Connected;      // 1 = 繋がっている
        public uint  Type;           // 0 = 無し / 1 = XInput / 2 = DirectInput
        public uint  Buttons;        // GamepadButton のビット
        public float LeftX;          // -1 (左) ～ 1 (右)
        public float LeftY;          // -1 (下) ～ 1 (上)
        public float RightX;
        public float RightY;
        public float LeftTrigger;    // 0 ～ 1
        public float RightTrigger;
    }

    // ---------- 初期化 / 終了 ----------

    // 戻り値 : 常に true (XInput はすぐ読める。DirectInput は C++ の別のスレッドで用意し、終わった後の Controller_Update から出てくる)
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Controller_Initialize();

    // 振動を止めてから解放する
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Controller_Shutdown();

    // ---------- 毎フレーム ----------

    // 全パッドを読んで outStates[0 ～ count-1] に入れる。戻り値は繋がっているパッドの数
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Controller_Update(PadState* outStates, uint count);

    // 振動 (0 ～ 1)。XInput のパッドだけ。送るのは次の Controller_Update
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Controller_SetVibration(uint pad, float left, float right);

    // ---------- DirectInput ----------

    // パッドを C++ の別のスレッドで列挙し直す (100 ms ほど。その間 DirectInput のパッドは出てこない)。戻り値は始めたか (列挙中なら false)
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Controller_RescanDevices();

    // ボタンの割り当てを始める (A B X Y L L2 R R2 Start Select の順に押す)
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Controller_BeginButtonConfig(uint pad);

    // -1 = していない / 0 ～ 9 = 次に押してほしいボタン
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern int Controller_GetButtonConfigStep();
}
