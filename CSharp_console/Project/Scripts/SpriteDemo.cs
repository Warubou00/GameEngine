using System.Numerics;
using GameEngine;

namespace GameEngine_console;

// ==========================================================================
// スプライトのアニメーション (v2.4) の見本 (シーン : Assets/Scenes/sprites.scene.json。Assets > Create Sprite Samples で作る)
//   Runner    : 左右に走り、端で向きを変える (SpriteRenderer.FlipX)。コマの "step" イベントで足音を数える
//   Explosion : Space で走る人の所に出して 1 回だけ再生 (Loop = Once)。"boom" イベントと OnSpriteFinished
//   HUD       : USpriteImage の Coin (C で止める / 再生) と Runner (ゲームの物と同じアセット)
//   キー : Space 爆発 / P 走る人の一時停止 / C 硬貨 (UI) の停止 / 1 2 走る人の速さ
// ==========================================================================
public enum SpriteDemoRole { Runner, Explosion }

public struct SpriteDemo : IComponent
{
    public SpriteDemoRole Role;
    public float Speed;
    public int Steps;
}

[GameSystem]
public sealed class SpriteDemoSystem : Behaviour<SpriteDemo>
{
    private float _direction = 1;
    private Entity _runner, _explosion;
    private USpriteImage? _coin;
    private UTextBlock? _info;
    private int _booms, _finished;

    protected override void Start(ref SpriteDemo demo)
    {
        if (demo.Role == SpriteDemoRole.Runner) _runner = Entity;
        else _explosion = Entity;
    }

    protected override void Update(ref SpriteDemo demo)
    {
        if (demo.Role != SpriteDemoRole.Runner) return;
        bindHud();
        ref var transform = ref Transform;
        ref var animator = ref GetComponent<SpriteAnimator>();
        ref var renderer = ref GetComponent<SpriteRenderer>();
        if (!animator.IsPaused) transform.Position.X += _direction * demo.Speed * animator.Speed * Time.DeltaTime;
        if (transform.Position.X > 3.5f) { _direction = -1; renderer.FlipX = true; }
        if (transform.Position.X < -3.5f) { _direction = 1; renderer.FlipX = false; }

        if (Input.GetKeyDown(Keys.Space) && World.IsAlive(_explosion))
        {
            World.Get<Transform>(_explosion).Position = transform.Position + new Vector3(0, 1, -0.5f);
            World.Get<SpriteAnimator>(_explosion).Play();
        }
        if (Input.GetKeyDown(Keys.P)) { if (animator.IsPaused) animator.Resume(); else animator.Pause(); }
        if (Input.GetKeyDown(Keys.D1)) animator.Speed = 1;
        if (Input.GetKeyDown(Keys.D2)) animator.Speed = 2;
        if (Input.GetKeyDown(Keys.C) && _coin != null) { if (_coin.IsPlaying) _coin.Stop(); else _coin.Play(); }
        if (_info != null) _info.Text = $"steps {demo.Steps}  booms {_booms} (finished {_finished})  speed x{animator.Speed}  |  Space / P / C / 1 2";
    }

    protected override void OnSpriteEvent(ref SpriteDemo demo, string eventName)
    {
        if (eventName == "step") demo.Steps++;
        if (eventName == "boom") { _booms++; Console.WriteLine($"[SpriteDemo] boom #{_booms}"); }
    }

    protected override void OnSpriteFinished(ref SpriteDemo demo)
    {
        _finished++;
        Console.WriteLine($"[SpriteDemo] explosion finished ({_finished})");
    }

    private void bindHud()
    {
        if (_coin != null) return;
        foreach (var entity in World.Entities)
        {
            if (!World.Has<UIWidget>(entity) || UI.GetWidget(entity) is not { } hud) continue;
            _coin = hud.GetWidget<USpriteImage>("Coin");
            _info = hud.GetWidget<UTextBlock>("Info");
            if (_coin != null) _coin.OnFinished += () => Console.WriteLine("[SpriteDemo] coin finished");
        }
    }
}
