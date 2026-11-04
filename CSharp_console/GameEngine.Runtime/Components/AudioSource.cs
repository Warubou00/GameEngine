namespace GameEngine;

// ==========================================================================
// 音を鳴らす (Transform は要らない。Spatial にすると、聞く位置 (AudioListener) からの距離で小さくなる。左右の変化は無い)
//   付けると、AudioSystem が C++ 側にボイスを作る (AudioSource 1 つに 1 個)。外す / エンティティを削除すると C++ 側も削除される
//   鳴らす / 止める : world.Get<AudioSource>(entity).Play() / .Stop() (次の AudioSystem の更新で反映される)
//   Volume / Loop は書き換えると、変わった時だけ C++ へ送られる
//   Loop を再生中に false にすると今の周の終わりで止まる。再生中に true にした時は次の Play から
// ==========================================================================
[ComponentInfo(DisplayName = "Audio Source", Category = "Audio")]
public struct AudioSource : IComponent
{
    public Sound Clip;
    public float Volume;        // 1 = そのまま、0 = 無音
    public bool  Loop;
    public bool  PlayOnStart;   // 付けて最初の更新で鳴らす (Clip を差し替えた時も)

    // 距離による音量 (ED6)。MinDistance までは Volume のまま、MaxDistance で 0 (間はまっすぐ下がる)。Transform が要る
    public bool  Spatial;
    public float MinDistance;
    public float MaxDistance;

    internal AudioRequest Request;
    internal bool         Playing;

    public AudioSource() : this(default(Sound))
    {
    }

    public AudioSource(Sound clip)
    {
        Clip        = clip;
        Volume      = 1.0f;
        Loop        = false;
        PlayOnStart = true;
        MinDistance = 2.0f;
        MaxDistance = 30.0f;
    }

    // 再生中か (最後まで鳴り終わる / Stop すると false。AudioSystem の更新の時に決まる)
    public readonly bool IsPlaying => Playing;

    // 最初から鳴らす (鳴っている時も最初に戻る)
    public void Play() => Request = AudioRequest.Play;
    public void Stop() => Request = AudioRequest.Stop;
}

internal enum AudioRequest : byte
{
    None,
    Play,
    Stop,
}
