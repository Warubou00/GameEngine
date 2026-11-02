using System.Numerics;
using GameEngine.Native;

namespace GameEngine;

// ゲームパッドのボタン (XInput の並び。DirectInput のパッドも C++ でこの並びにそろえる)
[Flags]
public enum GamepadButton : uint
{
    None          = 0,
    DPadUp        = 0x0001,
    DPadDown      = 0x0002,
    DPadLeft      = 0x0004,
    DPadRight     = 0x0008,
    Start         = 0x0010,
    Back          = 0x0020,   // Select / Share
    LeftStick     = 0x0040,   // L3 (スティックの押し込み)
    RightStick    = 0x0080,   // R3
    LeftShoulder  = 0x0100,   // L1
    RightShoulder = 0x0200,   // R1
    LeftTrigger   = 0x0400,   // L2 をボタンとして見る (XInput は少し引いたら押した扱い)
    RightTrigger  = 0x0800,   // R2
    A             = 0x1000,
    B             = 0x2000,
    X             = 0x4000,
    Y             = 0x8000,
}

public enum GamepadAxis
{
    LeftX,          // -1 (左) ～ 1 (右)
    LeftY,          // -1 (下) ～ 1 (上)
    RightX,
    RightY,
    LeftTrigger,    // 0 ～ 1
    RightTrigger,
}

public enum GamepadType
{
    None,
    XInput,         // Xbox のコントローラーなど (振動できる)
    DirectInput,    // それ以外の USB パッド (振動しない。ボタンの並びは BeginButtonConfig で合わせる)
}

// ==========================================================================
// ゲームパッドの入力 (C++ は Core.dll。XInput と DirectInput のパッドを 4 台までまとめる)
//   Input (キーボード / マウス) と同じ形で読む。pad は 0 ～ 3 (省略すると 0 = 1 台目)
//   GetButton     : 押している間 true
//   GetButtonDown : 押したフレームだけ true / GetButtonUp : 離したフレームだけ true
//   GetAxis       : スティック (-1 ～ 1。デッドゾーンは C++ で除いてある) / トリガー (0 ～ 1)
//   SetVibration  : 振動 (0 ～ 1。XInput のパッドだけ)
//   状態はエンジンのフレームの頭で 1 回、1 回の P/Invoke で全パッド分を受け取る (Engine.Run が呼ぶ)
//   パッドの番号は XInput のユーザー番号。空いている番号に DirectInput のパッドが入る
//   Core.dll が古くて関数が無い時は、何も繋がっていない扱いで動く
// ==========================================================================
public static class Gamepad
{
    public const int MaxCount = GamepadNativeMethods.MaxPads;

    private static readonly GamepadNativeMethods.PadState[] _states = new GamepadNativeMethods.PadState[MaxCount];
    private static readonly uint[] _previousButtons = new uint[MaxCount];
    private static bool _loaded;

    // 繋がっているパッドの数
    public static int ConnectedCount { get; private set; }

    public static bool        IsConnected(int pad = 0) => IsValid(pad) && _states[pad].Connected != 0;
    public static GamepadType GetPadType(int pad = 0)  => IsValid(pad) ? (GamepadType)_states[pad].Type : GamepadType.None;

    public static bool GetButton(GamepadButton button, int pad = 0)
        => IsValid(pad) && (_states[pad].Buttons & (uint)button) != 0;

    public static bool GetButtonDown(GamepadButton button, int pad = 0)
        => IsValid(pad) && (_states[pad].Buttons & ~_previousButtons[pad] & (uint)button) != 0;

    public static bool GetButtonUp(GamepadButton button, int pad = 0)
        => IsValid(pad) && (~_states[pad].Buttons & _previousButtons[pad] & (uint)button) != 0;

    // 押しているボタン全部 (ビット)
    public static GamepadButton GetButtons(int pad = 0) => IsValid(pad) ? (GamepadButton)_states[pad].Buttons : GamepadButton.None;

    public static float GetAxis(GamepadAxis axis, int pad = 0)
    {
        if (!IsValid(pad)) return 0.0f;
        ref readonly var s = ref _states[pad];
        return axis switch
        {
            GamepadAxis.LeftX        => s.LeftX,
            GamepadAxis.LeftY        => s.LeftY,
            GamepadAxis.RightX       => s.RightX,
            GamepadAxis.RightY       => s.RightY,
            GamepadAxis.LeftTrigger  => s.LeftTrigger,
            GamepadAxis.RightTrigger => s.RightTrigger,
            _                        => 0.0f,
        };
    }

    public static Vector2 GetLeftStick(int pad = 0)  => IsValid(pad) ? new Vector2(_states[pad].LeftX,  _states[pad].LeftY)  : Vector2.Zero;
    public static Vector2 GetRightStick(int pad = 0) => IsValid(pad) ? new Vector2(_states[pad].RightX, _states[pad].RightY) : Vector2.Zero;

    // 振動 (left = 低い周波数のモーター / right = 高い周波数のモーター。0 ～ 1)。止めるまで続く
    public static void SetVibration(float left, float right, int pad = 0)
    {
        if (_loaded && IsValid(pad)) GamepadNativeMethods.Controller_SetVibration((uint)pad, left, right);
    }

    public static void StopVibration(int pad = 0) => SetVibration(0.0f, 0.0f, pad);

    // DirectInput のパッドの抜き差しを反映する (XInput のパッドは自動で反映される)
    // 列挙は C++ の別のスレッドで 100 ms ほどかかり、その間 DirectInput のパッドは繋がっていない扱いになる
    // 戻り値 : 始めたか (列挙中なら false)
    public static bool RescanDevices() => _loaded && GamepadNativeMethods.Controller_RescanDevices();

    // DirectInput のパッドのボタンの割り当てを始める (A B X Y L1 L2 R1 R2 Start Back の順に押してもらう)
    // 戻り値 : 始められたか (XInput のパッド / 繋がっていない時は false)
    public static bool BeginButtonConfig(int pad = 0)
        => _loaded && IsValid(pad) && GamepadNativeMethods.Controller_BeginButtonConfig((uint)pad);

    // ボタンの割り当て中に、次に押してほしいボタン (割り当て中でなければ null)
    public static GamepadButton? ButtonConfigWaitingFor
    {
        get
        {
            if (!_loaded) return null;
            var step = GamepadNativeMethods.Controller_GetButtonConfigStep();
            return step >= 0 && step < ConfigOrder.Length ? ConfigOrder[step] : null;
        }
    }

    // C++ の DirectInputGamePadButtonA → … → Select の順
    private static readonly GamepadButton[] ConfigOrder =
    {
        GamepadButton.A, GamepadButton.B, GamepadButton.X, GamepadButton.Y,
        GamepadButton.LeftShoulder, GamepadButton.LeftTrigger, GamepadButton.RightShoulder, GamepadButton.RightTrigger,
        GamepadButton.Start, GamepadButton.Back,
    };

    private static bool IsValid(int pad) => (uint)pad < MaxCount;

    // ---------- エンジンから呼ぶ ----------

    internal static void Initialize()
    {
        try
        {
            _loaded = GamepadNativeMethods.Controller_Initialize();
        }
        catch (Exception e) when (e is DllNotFoundException or EntryPointNotFoundException)
        {
            Console.WriteLine("[Engine] Core.dll has no controller functions. Gamepads are disabled.");
        }
    }

    // フレームの頭で 1 回 (全パッド分を 1 回の P/Invoke で受け取る)
    internal static unsafe void Update()
    {
        for (var i = 0; i < MaxCount; i++) _previousButtons[i] = _states[i].Buttons;
        if (!_loaded) return;

        fixed (GamepadNativeMethods.PadState* states = _states)
        {
            ConnectedCount = (int)GamepadNativeMethods.Controller_Update(states, MaxCount);
        }
    }

    internal static void Shutdown()
    {
        if (!_loaded) return;
        GamepadNativeMethods.Controller_Shutdown();   // 振動を止めてから解放される
        _loaded = false;
        Array.Clear(_states);
        Array.Clear(_previousButtons);
        ConnectedCount = 0;
    }
}
