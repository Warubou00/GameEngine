using GameEngine;

namespace GameEngine_console;

// ==========================================================================
// 音の操作 (キー)
//   F7 : 鳴らす / 止める (鳴っている時は止め、止まっている時は最初から鳴らす)
//   F8 : 音量を 1.0 → 0.5 → 0.2 → 1.0 …
//   鳴っているかはプロファイラー (F1) の "Sounds playing" に出る
// ==========================================================================
public sealed class SoundHotkeySystem : ISystem
{
    private readonly Entity _speaker;
    private bool _wasPlaying;

    public SoundHotkeySystem(Entity speaker)
    {
        _speaker = speaker;
    }

    public void Update(World world)
    {
        if (!world.Has<AudioSource>(_speaker)) return;
        ref var source = ref world.Get<AudioSource>(_speaker);

        // 鳴り始め / 鳴り終わりをコンソールに出す (変わった時だけ)
        if (source.IsPlaying != _wasPlaying)
        {
            _wasPlaying = source.IsPlaying;
            Console.WriteLine($"[Sound] {(_wasPlaying ? "Playing" : "Stopped")} ({Time.TotalTime:F2} s)");
        }

        if (Input.GetKeyDown(Keys.F7))
        {
            if (source.IsPlaying) source.Stop();
            else                  source.Play();
            Console.WriteLine($"[Sound] F7 : {(source.IsPlaying ? "Stop" : "Play")} ({Time.TotalTime:F2} s)");
        }
        if (Input.GetKeyDown(Keys.F8))
        {
            source.Volume = source.Volume > 0.75f ? 0.5f : source.Volume > 0.35f ? 0.2f : 1.0f;
            Console.WriteLine($"[Sound] Volume {source.Volume:F1}");
        }
    }
}
