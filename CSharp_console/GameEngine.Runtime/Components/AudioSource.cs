namespace GameEngine;

// ==========================================================================
// 音を鳴らす (Transform は要らない。Spatial にすると、聞く位置 (AudioListener) からの距離で小さくなる。左右の変化は無い)
//   付けると、AudioSystem が C++ 側にボイスを作る (AudioSource 1 つに 1 個)。外す / エンティティを削除すると C++ 側も削除される
//   鳴らす / 止める : world.Get<AudioSource>(entity).Play() / .Stop() (次の AudioSystem の更新で反映される)
//     v1.6 : Behaviour のフィールドに取っておいたコピー (AudioSource a = GetComponent<AudioSource>()) の Play() / Stop() でも鳴る
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
    // 持ち主のエンティティ (v1.6。付けた時 / AudioSystem の更新で入る。保存しない)
    //   struct なので「AudioSource a = GetComponent<AudioSource>();」はコピーになる。コピーの Play() / Stop() / IsPlaying も
    //   この持ち主を通して本物へ届くようにする (以前はコピーの Request が書き換わるだけで、鳴らなかった)
    internal Entity       Owner;

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
    //   v1.6 : コピーからでも本物の状態を返す
    public readonly bool IsPlaying => AudioSystem.IsPlayingOf(Owner, Playing);

    // 最初から鳴らす (鳴っている時も最初に戻る)
    //   v1.6 : フィールドに取っておいたコピーから呼んでも鳴る (Unity と同じ書き方)。Volume / Loop の書き換えは ref で (コピーには届かない)
    public void Play() { Request = AudioRequest.Play; AudioSystem.Post(Owner, AudioRequest.Play); }
    public void Stop() { Request = AudioRequest.Stop; AudioSystem.Post(Owner, AudioRequest.Stop); }
}

internal enum AudioRequest : byte
{
    None,
    Play,
    Stop,
}
