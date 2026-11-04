using System.Numerics;

namespace GameEngine;

// ==========================================================================
// 聞く位置と全体の音量 (ED6)。シーンに 1 つ (普通はカメラと同じエンティティに付ける)
//   AudioSource.Spatial の音は、この位置 (無ければカメラの位置) からの距離で小さくなる
//   Volume は全部の音に掛かる (以前は engine.Audio.MasterVolume をコードで設定するだけだった)
// ==========================================================================
[ComponentInfo(DisplayName = "Audio Listener", Category = "Audio")]
public struct AudioListener : IComponent
{
    [Range(0.0f, 2.0f)] public float Volume = 1.0f;

    public AudioListener()
    {
    }
}
