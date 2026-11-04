namespace GameEngine;

// ==========================================================================
// 入力のアクション (ED6)。ProjectSettings の InputActions / InputAxes に名前とキー・ボタンを書き、ゲームのコードは名前で読む
//   if (InputActions.GetDown("Jump")) ...         // どれかのキー / ボタンを押した瞬間
//   var move = InputActions.GetAxis("Horizontal"); // -1〜1 (キーの + / - と、ゲームパッドの軸の大きい方)
//   以前はゲームのコードが Input.GetKey(Keys.Space) / Gamepad.GetButton(...) を直接読んでいた (割り当てを変えるにはコードを直す)
//   名前が無い時は false / 0 (間違った名前は 1 回だけ Console に出す)
// ==========================================================================
public struct InputActionBinding
{
    public string          Name = "";
    public Keys[]          Keys = Array.Empty<Keys>();
    public GamepadButton[] Buttons = Array.Empty<GamepadButton>();
    public InputActionBinding() { }
}

public struct InputAxisBinding
{
    public string      Name = "";
    public Keys        Positive = Keys.None;
    public Keys        Negative = Keys.None;
    public Keys        AltPositive = Keys.None;
    public Keys        AltNegative = Keys.None;
    public bool        UseGamepadAxis;
    public GamepadAxis GamepadAxis;
    [Range(0.0f, 0.9f)] public float DeadZone = 0.15f;
    public bool        Invert;
    public InputAxisBinding() { }
}

public static class InputActions
{
    private static Dictionary<string, InputActionBinding> _actions = new(StringComparer.OrdinalIgnoreCase);
    private static Dictionary<string, InputAxisBinding>   _axes    = new(StringComparer.OrdinalIgnoreCase);
    private static readonly HashSet<string> _warned = new();

    // ProjectSettings.Apply が呼ぶ
    internal static void Set(IEnumerable<InputActionBinding> actions, IEnumerable<InputAxisBinding> axes)
    {
        _actions = new(StringComparer.OrdinalIgnoreCase);
        foreach (var a in actions) if (!string.IsNullOrEmpty(a.Name)) _actions[a.Name] = a;
        _axes = new(StringComparer.OrdinalIgnoreCase);
        foreach (var a in axes) if (!string.IsNullOrEmpty(a.Name)) _axes[a.Name] = a;
    }

    public static bool Get(string action)     => test(action, Input.GetKey, b => Gamepad.GetButton(b));
    public static bool GetDown(string action) => test(action, Input.GetKeyDown, b => Gamepad.GetButtonDown(b));
    public static bool GetUp(string action)   => test(action, Input.GetKeyUp, b => Gamepad.GetButtonUp(b));

    public static float GetAxis(string axis)
    {
        if (!_axes.TryGetValue(axis, out var a)) { warn(axis); return 0.0f; }
        var keys = 0.0f;
        if (pressed(a.Positive) || pressed(a.AltPositive)) keys += 1.0f;
        if (pressed(a.Negative) || pressed(a.AltNegative)) keys -= 1.0f;
        var pad = 0.0f;
        if (a.UseGamepadAxis)
        {
            pad = Gamepad.GetAxis(a.GamepadAxis);
            if (MathF.Abs(pad) < a.DeadZone) pad = 0.0f;
            else pad = MathF.Sign(pad) * (MathF.Abs(pad) - a.DeadZone) / (1.0f - a.DeadZone);   // 遊びの外を 0〜1 に伸ばす
        }
        var value = MathF.Abs(pad) > MathF.Abs(keys) ? pad : keys;
        return a.Invert ? -value : value;
    }

    public static IEnumerable<string> ActionNames => _actions.Keys;
    public static IEnumerable<string> AxisNames   => _axes.Keys;

    private static bool pressed(Keys key) => key != Keys.None && Input.GetKey(key);

    private static bool test(string action, Func<Keys, bool> key, Func<GamepadButton, bool> button)
    {
        if (!_actions.TryGetValue(action, out var a)) { warn(action); return false; }
        foreach (var k in a.Keys) if (key(k)) return true;
        foreach (var b in a.Buttons) if (button(b)) return true;
        return false;
    }

    private static void warn(string name)
    {
        if (_warned.Add(name)) Console.WriteLine($"[Input] No input action / axis named '{name}' (Edit > Project Settings > InputActions / InputAxes)");
    }
}
