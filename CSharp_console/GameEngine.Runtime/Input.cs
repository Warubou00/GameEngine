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
    private static Vector2      _mousePosition;
    private static Vector2      _lastMousePosition;
    private static bool         _hasMousePosition;
    private static Vector2      _mouseDelta;
    private static float        _scrollDelta;

    public static bool GetKey(Keys key)     => _down.Contains(key);
    public static bool GetKeyDown(Keys key) => _pressed.Contains(key);
    public static bool GetKeyUp(Keys key)   => _released.Contains(key);

    public static bool    GetMouseButton(MouseButtons button) => (_buttons & button) != 0;
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
        };
        form.KeyUp += (_, e) =>
        {
            if (_down.Remove(e.KeyCode)) _released.Add(e.KeyCode);
        };
        form.MouseDown  += (_, e) => _buttons |= e.Button;
        form.MouseUp    += (_, e) => _buttons &= ~e.Button;
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
            _buttons = MouseButtons.None;
        };
        form.MouseLeave += (_, _) => _hasMousePosition = false;
    }

    // フレームの終わりに、このフレームだけの値を消す
    internal static void EndFrame()
    {
        _pressed.Clear();
        _released.Clear();
        _mouseDelta  = Vector2.Zero;
        _scrollDelta = 0;
    }
}
