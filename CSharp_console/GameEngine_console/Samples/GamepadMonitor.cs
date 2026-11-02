using GameEngine;

namespace GameEngine_console;

// ==========================================================================
// ゲームパッドの確認用
//   プロファイラー (F1) : "Gamepads" (繋がっている数) と、1 台目の種類 / ボタン / スティック / トリガー
//   コンソール          : パッドの抜き差しと、押したボタンの名前
//   A を押している間    : 振動 (左のモーター = LT、右のモーター = RT の引き具合。引いていない時は 0.3)
//   F9                  : DirectInput のパッドを列挙し直す (挿し直した時)
//   F10                 : 1 台目が DirectInput のパッドなら、ボタンの割り当てを始める (コンソールの指示どおりに押す)
//   カメラはエンジンの FreeCameraSystem がパッドでも動かす (右スティック : 向き / 左スティック : 移動 / RT LT : 上下 / RB : 速く)
// ==========================================================================
public sealed class GamepadMonitorSystem : ISystem
{
    private readonly Engine _engine;
    private readonly GamepadType[] _types = new GamepadType[Gamepad.MaxCount];
    private bool _vibrating;
    private GamepadButton? _configWaiting;

    public GamepadMonitorSystem(Engine engine)
    {
        _engine = engine;
    }

    public void Update(World world)
    {
        // 抜き差し (変わった時だけ出す)
        for (var pad = 0; pad < Gamepad.MaxCount; pad++)
        {
            var type = Gamepad.GetPadType(pad);
            if (type == _types[pad]) continue;
            Console.WriteLine(type == GamepadType.None
                ? $"[Gamepad] Pad {pad} disconnected ({_types[pad]})"
                : $"[Gamepad] Pad {pad} connected ({type})");
            _types[pad] = type;
        }

        // 押したボタン (どのパッドでも)
        for (var pad = 0; pad < Gamepad.MaxCount; pad++)
        {
            if (!Gamepad.IsConnected(pad)) continue;
            foreach (var button in AllButtons)
            {
                if (Gamepad.GetButtonDown(button, pad)) Console.WriteLine($"[Gamepad] Pad {pad} : {button}");
            }
        }

        // 振動 (A を押している間。変わった時だけ C++ へ送る)
        var vibrate = Gamepad.GetButton(GamepadButton.A);
        if (vibrate)
        {
            var left  = MathF.Max(0.3f, Gamepad.GetAxis(GamepadAxis.LeftTrigger));
            var right = MathF.Max(0.3f, Gamepad.GetAxis(GamepadAxis.RightTrigger));
            Gamepad.SetVibration(left, right);   // 同じ値なら C++ 側で送らない
        }
        else if (_vibrating)
        {
            Gamepad.StopVibration();
        }
        _vibrating = vibrate;

        if (Input.GetKeyDown(Keys.F9))
        {
            Console.WriteLine(Gamepad.RescanDevices()
                ? "[Gamepad] F9 : rescanning DirectInput pads (the result is logged by [Controller])"
                : "[Gamepad] F9 : already rescanning");
        }
        if (Input.GetKeyDown(Keys.F10))
        {
            Console.WriteLine(Gamepad.BeginButtonConfig(0)
                ? "[Gamepad] F10 : button config started (pad 0)"
                : "[Gamepad] F10 : pad 0 is not a DirectInput pad (XInput pads need no config)");
        }
        var waiting = Gamepad.ButtonConfigWaitingFor;
        if (waiting != _configWaiting)
        {
            Console.WriteLine(waiting is { } next ? $"[Gamepad] Press {next}" : "[Gamepad] Button config finished");
            _configWaiting = waiting;
        }

        // プロファイラー (1 台目だけ。毎フレーム文字列を渡すので数は絞る。抜けた時も 0 で上書きする)
        _engine.ReportProfilerValue("Gamepads", Gamepad.ConnectedCount);
        {
            _engine.ReportProfilerValue("Pad0 type (1 XInput / 2 DInput)", (int)Gamepad.GetPadType(0));
            _engine.ReportProfilerValue("Pad0 buttons", (uint)Gamepad.GetButtons(0));
            _engine.ReportProfilerValue("Pad0 left X",  Gamepad.GetAxis(GamepadAxis.LeftX));
            _engine.ReportProfilerValue("Pad0 left Y",  Gamepad.GetAxis(GamepadAxis.LeftY));
            _engine.ReportProfilerValue("Pad0 right X", Gamepad.GetAxis(GamepadAxis.RightX));
            _engine.ReportProfilerValue("Pad0 right Y", Gamepad.GetAxis(GamepadAxis.RightY));
            _engine.ReportProfilerValue("Pad0 LT",      Gamepad.GetAxis(GamepadAxis.LeftTrigger));
            _engine.ReportProfilerValue("Pad0 RT",      Gamepad.GetAxis(GamepadAxis.RightTrigger));
        }
    }

    private static readonly GamepadButton[] AllButtons = Enum.GetValues<GamepadButton>().Where(b => b != GamepadButton.None).ToArray();
}
