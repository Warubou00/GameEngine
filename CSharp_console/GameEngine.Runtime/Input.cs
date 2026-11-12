using System.Numerics;

namespace GameEngine;

// ==========================================================================
// キーボード / マウスの入力 (ウィンドウのイベントを集めて、フレーム単位で読めるようにする)
//   GetKey     : 押している間 true
//   GetKeyDown : 押したフレームだけ true / GetKeyUp : 離したフレームだけ true
//   MouseDelta : このフレームでマウスが動いた量 (ピクセル。下 / 右が +)
// ==========================================================================
public static class Input
{
    private static readonly HashSet<Keys> _down     = new();
    private static readonly HashSet<Keys> _pressed  = new();
    private static readonly HashSet<Keys> _released = new();

    private static MouseButtons _buttons;
    private static MouseButtons _buttonsPressed, _buttonsReleased;   // v1.7 : このフレームに押した / 離したボタン (UI のクリック)
    private static Vector2      _mousePosition;
    private static Vector2      _lastMousePosition;
    private static bool         _hasMousePosition;
    private static Vector2      _mouseDelta;
    private static float        _scrollDelta;

    // このフレームに打った文字 / 押した・離したキー (順番どおり。エディターの GUI (ImGui) に渡す。ED3)
    private static readonly List<char> _typed = new();
    private static readonly List<(Keys Key, bool Down)> _keyEvents = new();
    public static IReadOnlyList<char> TypedCharacters => _typed;
    public static IReadOnlyList<(Keys Key, bool Down)> KeyEvents => _keyEvents;

    public static bool GetKey(Keys key)     => _down.Contains(key);
    public static bool GetKeyDown(Keys key) => _pressed.Contains(key);
    public static bool GetKeyUp(Keys key)   => _released.Contains(key);

    public static bool    GetMouseButton(MouseButtons button) => (_buttons & button) != 0;
    /// <summary>このフレームに押したか (v1.7。同じフレームで押して離しても true)</summary>
    public static bool    GetMouseButtonDown(MouseButtons button) => (_buttonsPressed & button) != 0;
    /// <summary>このフレームに離したか (v1.7)</summary>
    public static bool    GetMouseButtonUp(MouseButtons button) => (_buttonsReleased & button) != 0;
    public static Vector2 MousePosition => _mousePosition;
    public static Vector2 MouseDelta    => _mouseDelta;
    public static float   ScrollDelta   => _scrollDelta;   // ホイール 1 目盛り = 1

    internal static void Attach(Form form)
    {
        // フォームの上の部品よりも先にキーを受け取る
        form.KeyPreview = true;

        form.KeyDown += (_, e) =>
        {
            if (_down.Add(e.KeyCode)) _pressed.Add(e.KeyCode);
            _keyEvents.Add((e.KeyCode, true));
        };
        form.KeyUp += (_, e) =>
        {
            if (_down.Remove(e.KeyCode)) _released.Add(e.KeyCode);
            _keyEvents.Add((e.KeyCode, false));
        };
        form.KeyPress += (_, e) => _typed.Add(e.KeyChar);
        form.MouseDown  += (_, e) => { _buttons |= e.Button; _buttonsPressed |= e.Button; };
        form.MouseUp    += (_, e) => { _buttons &= ~e.Button; _buttonsReleased |= e.Button; };
        form.MouseWheel += (_, e) => _scrollDelta += e.Delta / 120.0f;
        form.MouseMove  += (_, e) =>
        {
            _mousePosition = new Vector2(e.X, e.Y);
            if (_hasMousePosition) _mouseDelta += _mousePosition - _lastMousePosition;
            _lastMousePosition = _mousePosition;
            _hasMousePosition  = true;
        };
        // ウィンドウから離れた時は、押しっぱなしのまま残らないように全部離したことにする
        form.Deactivate += (_, _) =>
        {
            foreach (var key in _down) _released.Add(key);
            _down.Clear();
            _buttonsReleased |= _buttons;
            _buttons = MouseButtons.None;
        };
        form.MouseLeave += (_, _) => _hasMousePosition = false;
    }

    // フレームの終わりに、このフレームだけの値を消す
    internal static void EndFrame()
    {
        _pressed.Clear();
        _released.Clear();
        _typed.Clear();
        _keyEvents.Clear();
        _mouseDelta  = Vector2.Zero;
        _scrollDelta = 0;
        _buttonsPressed = _buttonsReleased = MouseButtons.None;
        // 確認用の入力 (v1.7) を次のフレームへ
        _buttonsPressed = _simPressed; _buttonsReleased = _simReleased; _mouseDelta = _simDelta; _typed.AddRange(_simTyped);
        _simPressed = _simReleased = MouseButtons.None; _simDelta = Vector2.Zero; _simTyped.Clear();
    }

    // 確認用 (v1.7) : マウスの位置 / ボタンを入れる (エディターの自動の確認で UI のクリックを試す)
    //   確認のコードはフレームの途中 (LateUpdate) で呼ぶので、押した / 離した / 動いた量は次のフレームの物にする
    //   (そのまま入れると、このフレームの終わりの EndFrame で消えて、次のフレームの UI に届かなかった)
    private static MouseButtons _simPressed, _simReleased;
    private static Vector2 _simDelta;
    private static readonly List<char> _simTyped = new();

    public static void Simulate(Vector2 position, MouseButtons buttons)
    {
        _simDelta += position - _mousePosition;
        _mousePosition = position;
        _simPressed  |= buttons & ~_buttons;
        _simReleased |= _buttons & ~buttons;
        _buttons = buttons;
    }

    /// <summary>確認用 (v1.7) : 文字を打ったことにする (次のフレーム)</summary>
    public static void SimulateTyping(string text) => _simTyped.AddRange(text);
}
