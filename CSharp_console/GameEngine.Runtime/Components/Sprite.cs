using System.Numerics;

namespace GameEngine;

// ==========================================================================
// スプライト (v2.4) : GameObject に画像を板で出す (SpriteRenderer) / 画像を順に切り替える (SpriteAnimator)
//   SpriteRenderer だけ : Sprite の画像を出す。SpriteAnimator も付けると、アニメーションの今のコマを出す
//   板は Transform の XY 面 (表は -Z の向き = カメラから +Z を見て正面)。大きさ = 画像のピクセル / PixelsPerUnit (m) × Transform.Scale
//   描き方は半透明 (アルファでブレンド)。同じ画像 + 同じ色の物はマテリアルを共有する (たくさん置いても C++ のマテリアルが増えない)
//
//   // スクリプトから
//   ref var animator = ref GetComponent<SpriteAnimator>();
//   animator.Play(Assets.LoadSpriteAnimation("Assets/Sprites/Run.spriteanim.json"));
//   ...
//   protected override void OnSpriteEvent(ref Player player, string name) { if (name == "step") ... }   // Behaviour<T>
//   protected override void OnSpriteFinished(ref Player player) { ... }                                  // Once で最後まで行った
// ==========================================================================

/// <summary>板の向き</summary>
public enum SpriteBillboard
{
    /// <summary>Transform の回転のまま</summary>
    None,
    /// <summary>いつもカメラの方を向く (カメラと同じ傾き)</summary>
    Camera,
    /// <summary>縦の軸 (Y) だけ回ってカメラを向く (木 / 人など)</summary>
    Vertical,
}

[ComponentInfo(DisplayName = "Sprite Renderer", Category = "Rendering")]
public struct SpriteRenderer : IComponent
{
    /// <summary>出す画像 (SpriteAnimator が無い / 再生する物が無い時)</summary>
    [AssetPath(AssetKind.Texture)] public string Sprite;

    /// <summary>画像に掛ける色 (A = 不透明度)</summary>
    public Vector4 Color;

    public bool FlipX;
    public bool FlipY;

    /// <summary>板の大きさ (m)。0 = 画像のピクセル / PixelsPerUnit</summary>
    public Vector2 Size;

    /// <summary>Sprite の画像の、このピクセル数を 1 m にする (アニメーションのコマはアセットの値)</summary>
    public float PixelsPerUnit;

    /// <summary>Sprite の画像の中心 (0,0 = 左下 / 1,1 = 右上。アニメーションのコマはアセットの値)</summary>
    public Vector2 Pivot;

    public SpriteBillboard Billboard;

    /// <summary>影を落とす (既定 off)</summary>
    public bool CastShadows;

    // ---------- エンジンが使う ----------
    internal uint Handle;            // C++ の描画オブジェクト
    internal bool Created;
    internal uint AppliedMaterial;   // 付けた共有のマテリアルの C++ の番号 + 1 (0 = 無し)
    internal uint AppliedFlagsPlusOne;
    internal Matrix4x4 SentMatrix;
    internal bool HasSentMatrix;
    // SpriteAnimator が書く今のコマ (このフレーム)
    internal SpriteFrameTexture AnimatedFrame;
    internal Vector2 AnimatedPivot;
    internal float AnimatedPixelsPerUnit;
    internal long AnimatedStamp;     // 書いたフレーム (古ければ使わない)
    internal bool AnimatedHidden;    // SpriteAnimator.HideWhenStopped で隠す

    public SpriteRenderer()
    {
        Sprite = "";
        Color = Vector4.One;
        PixelsPerUnit = 100f;
        Pivot = new Vector2(0.5f, 0.5f);
    }

    public SpriteRenderer(string sprite) : this() => Sprite = sprite;

    /// <summary>C++ に描画オブジェクトがあるか (画像が読めていて、有効な物)</summary>
    public readonly bool IsCreated => Created;

    /// <summary>今出しているテクスチャの番号 (確認用。無ければ 0xFFFFFFFF)</summary>
    public uint CurrentTexture { readonly get; internal set; }

    /// <summary>今描いている板の行列 (Quad の -0.5〜0.5 → ワールド。エディターのクリックで選ぶのに使う。描いていなければ単位行列)</summary>
    public readonly Matrix4x4 QuadMatrix => Created && HasSentMatrix ? SentMatrix : Matrix4x4.Identity;

    /// <summary>今の板の大きさ (m。Transform.Scale を掛ける前)</summary>
    public Vector2 CurrentSize { readonly get; internal set; }
}

[ComponentInfo(DisplayName = "Sprite Animator", Category = "Rendering")]
public struct SpriteAnimator : IComponent
{
    public SpriteAnimation? Animation;

    /// <summary>再生の速さ (1 = そのまま / 2 = 倍 / 負は不可)</summary>
    public float Speed;

    /// <summary>再生を始めた時 (/ 物ができた時) に自動で再生する</summary>
    public bool PlayOnStart;

    public SpriteLoopOverride Loop;

    /// <summary>再生中 (ゲームの再生中) に止まっている間は出さない (爆発など、1 回だけ出す物。編集中は出す)</summary>
    public bool HideWhenStopped;

    // ---------- エンジンが使う ----------
    internal Entity Owner;
    internal bool Playing;
    internal bool Paused;
    internal bool Started;     // PlayOnStart を済ませた
    internal float Time;
    internal long Step = -1;   // 前のフレームの何コマ目か (イベントを送るため。-1 = まだ)
    internal int Frame;
    internal int AnimationVersion = -1;
    internal SpriteAnimation? Applied;

    public SpriteAnimator()
    {
        Speed = 1f;
        PlayOnStart = true;
    }

    public SpriteAnimator(SpriteAnimation? animation) : this() => Animation = animation;

    /// <summary>編集中 (再生していない時) もアニメーションを動かす (エディターの設定。イベントは送らない)</summary>
    public static bool PreviewInEditor
    {
        get => SpriteAnimationSystem.PreviewInEditor;
        set => SpriteAnimationSystem.PreviewInEditor = value;
    }

    /// <summary>再生中か (一時停止中も true)</summary>
    public readonly bool IsPlaying => Playing;
    public readonly bool IsPaused => Paused;
    /// <summary>今のコマの番号</summary>
    public readonly int CurrentFrame => Frame;
    /// <summary>再生を始めてからの時間 (秒。Speed を掛けた後)</summary>
    public readonly float CurrentTime => Time;
    /// <summary>1 回分の中の位置 (0〜1)</summary>
    public readonly float NormalizedTime => Animation is { } a && a.Duration > 0 ? (Time % a.Duration) / a.Duration : 0f;

    /// <summary>再生する (animation = null は今の Animation。restart = false なら同じ物の再生中は続ける)</summary>
    public void Play(SpriteAnimation? animation = null, bool restart = true)
    {
        if (animation != null && animation != Animation) { Animation = animation; restart = true; }
        if (!restart && Playing) { Paused = false; SpriteAnimationSystem.Post(Owner, SpriteRequest.Resume); return; }
        Playing = true; Paused = false; Started = true; Time = 0; Step = -1;
        SpriteAnimationSystem.Post(Owner, SpriteRequest.Play, Animation);
    }

    /// <summary>止めて最初のコマに戻す</summary>
    public void Stop()
    {
        Playing = false; Paused = false; Started = true; Time = 0; Step = -1;
        SpriteAnimationSystem.Post(Owner, SpriteRequest.Stop);
    }

    public void Pause() { if (Playing) Paused = true; SpriteAnimationSystem.Post(Owner, SpriteRequest.Pause); }
    public void Resume() { Paused = false; SpriteAnimationSystem.Post(Owner, SpriteRequest.Resume); }

    /// <summary>コマへ飛ぶ (再生中ならそこから続ける。イベントは送らない)</summary>
    public void SetFrame(int frame)
    {
        var time = 0f;
        if (Animation != null) for (int i = 0; i < Math.Clamp(frame, 0, Animation.FrameCount); i++) time += Animation.FrameDuration(i);
        SetTime(time);
    }

    /// <summary>時間へ飛ぶ (秒。イベントは送らない)</summary>
    public void SetTime(float time)
    {
        Time = Math.Max(0, time); Step = -1; Started = true;
        SpriteAnimationSystem.Post(Owner, SpriteRequest.SetTime, null, Time);
    }
}
