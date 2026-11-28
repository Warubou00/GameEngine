using System.Numerics;

namespace GameEngine;

// ==========================================================================
// スプライトのアニメーションを UI に出す (v2.4)。Widget Designer の Palette > Common > SpriteImage
//   Animation (.spriteanim.json) のコマを順に描く。Animation が無ければ Image (画像) を出す
//   SizeToFrame = コマのピクセルの大きさを欲しい大きさにする (false は ImageSize)
//   時間は Tick で進む (UI の更新。Widget Designer の中でも動く)。イベントは C# の event (OnFrameEvent / OnFinished)
//
//   var hud = UI.CreateWidget("Assets/UI/HUD.widget.json");
//   var coin = hud.GetWidget<USpriteImage>("Coin");
//   coin.OnFinished += () => ...;
//   coin.Play(Assets.LoadSpriteAnimation("Assets/Sprites/CoinSpin.spriteanim.json"));
// ==========================================================================
public sealed class USpriteImage : UWidget
{
    [AssetPath(AssetKind.SpriteAnimation)] public string Animation = "";
    /// <summary>Animation が無い時に出す画像</summary>
    [AssetPath(AssetKind.Texture)] public string Image = "";
    public float Speed = 1f;
    public bool AutoPlay = true;
    public SpriteLoopOverride Loop = SpriteLoopOverride.UseAsset;
    public Vector4 ColorAndOpacity = Vector4.One;
    public bool FlipX;
    public bool FlipY;
    /// <summary>コマのピクセルの大きさにする (false = ImageSize)</summary>
    public bool SizeToFrame = true;
    public Vector2 ImageSize = new(64, 64);

    /// <summary>コマに入った (イベントの名前の付いたコマ)</summary>
    public event Action<string>? OnFrameEvent;
    /// <summary>最後まで行って止まった (Loop = Once の時だけ)</summary>
    public event Action? OnFinished;

    private SpriteAnimation? _animation;   // コードで Play(asset) した物 (Animation のパスより優先)
    private SpriteAnimation? _loaded;
    private string? _loadedPath;
    private bool _playing, _paused, _started;
    private float _time;
    private long _step = -1;
    private int _frame;

    public bool IsPlaying => _playing;
    public bool IsPaused => _paused;
    public int CurrentFrame => _frame;
    public float CurrentTime => _time;

    /// <summary>今のアニメーション (コードで渡した物か、Animation のパスから読んだ物)</summary>
    public SpriteAnimation? CurrentAnimation => _animation ?? resolveAnimation();

    public void Play(SpriteAnimation? animation = null, bool restart = true)
    {
        if (animation != null && animation != CurrentAnimation) { _animation = animation; restart = true; }
        if (!restart && _playing) { _paused = false; return; }
        _playing = true; _paused = false; _started = true; _time = 0; _step = -1;
    }

    public void Stop() { _playing = false; _paused = false; _started = true; _time = 0; _step = -1; }
    public void Pause() { if (_playing) _paused = true; }
    public void Resume() => _paused = false;

    public void SetFrame(int frame)
    {
        var a = CurrentAnimation;
        float time = 0;
        if (a != null) for (int i = 0; i < Math.Clamp(frame, 0, a.FrameCount); i++) time += a.FrameDuration(i);
        SetTime(time);
    }

    public void SetTime(float time) { _time = Math.Max(0, time); _step = -1; _started = true; }

    private SpriteAnimation? resolveAnimation()
    {
        if (string.IsNullOrEmpty(Animation)) { _loaded = null; _loadedPath = null; return null; }
        if (_loadedPath == Animation) return _loaded;
        _loadedPath = Animation;
        _loaded = null;
        try { _loaded = UI.Manager?.Assets.LoadSpriteAnimation(Animation); }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Text.Json.JsonException or UnauthorizedAccessException)
        {
            Console.WriteLine($"[UI] Sprite animation not loaded : {Animation} ({ex.Message})");
        }
        return _loaded;
    }

    protected internal override void Tick(float deltaTime)
    {
        var a = CurrentAnimation;
        if (a == null || a.FrameCount == 0) { _frame = 0; return; }
        if (!_started) { _started = true; if (AutoPlay) { _playing = true; _time = 0; _step = -1; } }
        if (_playing && !_paused) _time += deltaTime * Math.Max(0, Speed);
        var loop = a.LoopFor(Loop);
        var sample = a.Sample(_time, loop);
        _frame = sample.Frame;
        if (!_playing) return;
        long from = _step < 0 ? sample.Step : _step + 1;
        if (sample.Step - from >= 64) from = sample.Step - 63;
        for (long s = from; s <= sample.Step; s++)
        {
            var name = a.Frames[a.FrameOfStep(s, loop)].Event;
            if (!string.IsNullOrEmpty(name)) OnFrameEvent?.Invoke(name);
        }
        _step = sample.Step;
        if (sample.Finished) { _playing = false; OnFinished?.Invoke(); }
    }

    /// <summary>エディター (Widget Designer) のプレビュー : 時間だけ進めてコマを決める (イベントは送らない。ゲームでは使わない)</summary>
    public void PreviewTick(float deltaTime)
    {
        var a = CurrentAnimation;
        if (a == null || a.FrameCount == 0) { _frame = 0; return; }
        _time += deltaTime * Math.Max(0, Speed);
        var sample = a.Sample(_time, a.LoopFor(Loop));
        if (sample.Finished) _time = 0;   // プレビューは繰り返して見せる
        _frame = sample.Frame;
    }

    private SpriteFrameTexture currentTexture(UIContext? context)
    {
        var a = CurrentAnimation;
        if (a != null && a.FrameCount > 0) return a.TextureOf(Math.Clamp(_frame, 0, a.FrameCount - 1));
        return string.IsNullOrEmpty(Image) ? SpriteFrameTexture.Empty : UI.Manager?.Assets.LoadSpriteTexture(Image) ?? SpriteFrameTexture.Empty;
    }

    protected override Vector2 ComputeDesiredSize(UIContext context)
    {
        if (!SizeToFrame) return ImageSize;
        var texture = currentTexture(context);
        return texture.IsValid ? texture.Size : ImageSize;
    }

    protected override void OnPaint(UIPainter painter)
    {
        var texture = currentTexture(null);
        if (!texture.IsValid) return;
        painter.Texture(texture.Texture, Vector2.Zero, Size, ColorAndOpacity, FlipX, FlipY);
    }
}
