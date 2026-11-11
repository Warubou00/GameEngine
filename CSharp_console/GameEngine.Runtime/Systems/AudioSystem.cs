using System.Numerics;
using GameEngine.Native;

namespace GameEngine;

// ==========================================================================
// AudioSource を C++ (Audio.dll) のボイスへ反映する (エンジン組み込み。LightSystem の後に動く)
//   1. まだボイスの無い AudioSource は作る (Clip を差し替えた時は作り直す)。PlayOnStart なら鳴らす
//   2. Volume / Loop は変わった時だけ送る。Play() / Stop() の要求を送る
//   3. ループしない再生中の物だけ「鳴り終わったか」を 1 回の呼び出しでまとめて調べる
//   4. 外された / 削除された AudioSource のボイスは次の更新で削除する
//   ボイスの状態はエンティティの番号で引く表に持つ (World.Set で AudioSource を置き換えても、ボイスを失わない)
//   何も鳴っていない・何も変わっていないシーンでは C++ を呼ばない (P/Invoke は 0 回)
// ==========================================================================
internal sealed unsafe class AudioSystem : ISystem
{
    private struct VoiceState
    {
        public uint  Voice;
        public uint  Clip;
        public float Volume;
        public bool  Loop;
        public bool  Active;
        public bool  Playing;
    }

    private readonly bool _enabled;
    private VoiceState[] _states = new VoiceState[64];   // エンティティの番号 → ボイス
    private readonly List<uint> _pendingDestroy = new();

    // 毎フレームの割り当てを避けるため、配列は使い回す (足りなくなった時だけ大きくする)
    private uint[] _pollVoices  = new uint[16];
    private int[]  _pollIndices = new int[16];
    private byte[] _pollResults = new byte[16];

    // プロファイラー用 : 鳴っている AudioSource の数
    public int PlayingCount { get; private set; }

    // v1.6 : AudioSource のコピーからの Play() / Stop() (持ち主のエンティティ宛て)。次の Update の始めに本物へ入れる
    //   エンジンは 1 つなので、最後に作った AudioSystem が受け取る
    private static AudioSystem? s_current;
    private readonly List<(Entity Entity, AudioRequest Request)> _posted = new();

    internal static void Post(Entity owner, AudioRequest request)
    {
        if (owner.IsNull || s_current is not { _enabled: true } system) return;
        system._posted.Add((owner, request));
    }

    // AudioSource.IsPlaying : 持ち主のボイスの状態 (知らなければ fallback = その AudioSource が覚えている値)
    internal static bool IsPlayingOf(Entity owner, bool fallback)
    {
        if (owner.IsNull || s_current is not { } system || (uint)owner.Id >= (uint)system._states.Length) return fallback;
        ref var state = ref system._states[owner.Id];
        return state.Active ? state.Playing : fallback;
    }

    public AudioSystem(World world, bool enabled)
    {
        _enabled = enabled;
        s_current = this;
        // 付けた時に持ち主を書く (v1.6。Behaviour の Start で GetComponent したコピーにも入っているように)
        var sources = world.GetPool<AudioSource>();
        sources.Added += entity => sources.Get(entity.Id).Owner = entity;
        // AudioSource が外れたら (エンティティの削除も含む)、C++ のボイスも消す
        world.GetPool<AudioSource>().Removed += (entity, _) =>
        {
            if ((uint)entity.Id >= (uint)_states.Length) return;
            ref var state = ref _states[entity.Id];
            if (state.Active && state.Voice != AudioNativeMethods.InvalidHandle) _pendingDestroy.Add(state.Voice);
            state = default;
        };
    }

    public void Update(World world) => Update(world, Vector3.Zero);

    // listenerFallback : AudioListener が無い時に聞く位置 (カメラの位置。ED6)
    public void Update(World world, Vector3 listenerFallback)
    {
        if (!_enabled) return;
        flushDestroy();
        applyPosted(world);

        // 聞く位置と全体の音量 (ED6) : 最初の有効な AudioListener
        var listenerPosition = listenerFallback;
        var listenerVolume = 1.0f;
        var listeners = world.GetPool<AudioListener>();
        var transforms = world.GetPool<Transform>();
        for (int i = 0; i < listeners.Count; i++)
        {
            var entity = listeners.Entities[i];
            if (!world.IsActive(entity)) continue;
            listenerVolume = listeners.Components[i].Volume;
            if (transforms.Has(entity.Id)) listenerPosition = transforms.Get(entity.Id).WorldPosition;
            break;
        }

        var pool       = world.GetPool<AudioSource>();
        var entities   = pool.Entities;
        var components = pool.Components;
        var pollCount  = 0;
        var playing    = 0;

        for (int i = 0; i < components.Length; i++)
        {
            ref var source = ref components[i];
            var id = entities[i].Id;
            if (id >= _states.Length) Array.Resize(ref _states, Math.Max(id + 1, _states.Length * 2));
            ref var state = ref _states[id];
            // World.Set などで丸ごと置き換えられた時は持ち主が消えているので書き直す (v1.6)
            if (source.Owner != entities[i]) source.Owner = entities[i];

            // 1. ボイスを作る (初めて見た / Clip が変わった)
            var clip = source.Clip.IsValid ? source.Clip.Handle : AudioNativeMethods.InvalidHandle;
            if (!state.Active || state.Clip != clip)
            {
                if (state.Active && state.Voice != AudioNativeMethods.InvalidHandle) AudioNativeMethods.Audio_DestroyVoice(state.Voice);
                state = new VoiceState
                {
                    Active = true,
                    Clip   = clip,
                    // 再生できない環境 (音の出る機器が無い) では InvalidHandle になり、以後は何もしない
                    Voice  = clip != AudioNativeMethods.InvalidHandle ? AudioNativeMethods.Audio_CreateVoice(clip) : AudioNativeMethods.InvalidHandle,
                    Volume = 1.0f,    // C++ のボイスの初期値と同じ (1 なら送らない)
                    Loop   = false,
                };
                if (source.PlayOnStart && source.Request == AudioRequest.None) source.Request = AudioRequest.Play;
            }
            if (state.Voice == AudioNativeMethods.InvalidHandle)
            {
                source.Request = AudioRequest.None;
                source.Playing = false;
                continue;
            }

            // 無効な物 (ED2) : 鳴っていれば止める。Play の頼みは有効になるまで残す (PlayOnStart は有効になった時に鳴る)
            if (!world.IsActive(entities[i]))
            {
                if (state.Playing) { AudioNativeMethods.Audio_Stop(state.Voice); state.Playing = false; }
                if (source.Request == AudioRequest.Stop) source.Request = AudioRequest.None;
                source.Playing = false;
                continue;
            }

            // 2. 変わった物だけ送る (音量 = Volume × 全体 × 距離。ED6)
            var volume = source.Volume * listenerVolume;
            if (source.Spatial && transforms.Has(id))
            {
                var distance = Vector3.Distance(transforms.Get(id).WorldPosition, listenerPosition);
                var range = MathF.Max(source.MaxDistance - source.MinDistance, 1e-3f);
                volume *= Math.Clamp(1.0f - (distance - source.MinDistance) / range, 0.0f, 1.0f);
            }
            if (volume != state.Volume)
            {
                AudioNativeMethods.Audio_SetVolume(state.Voice, volume);
                state.Volume = volume;
            }
            if (source.Loop != state.Loop)
            {
                AudioNativeMethods.Audio_SetLoop(state.Voice, source.Loop);
                state.Loop = source.Loop;
            }
            switch (source.Request)
            {
                case AudioRequest.Play:
                    state.Playing = AudioNativeMethods.Audio_Play(state.Voice);
                    break;
                case AudioRequest.Stop:
                    AudioNativeMethods.Audio_Stop(state.Voice);
                    state.Playing = false;
                    break;
            }
            source.Request = AudioRequest.None;
            source.Playing = state.Playing;

            // 3. ループしない再生中の物は、鳴り終わったかを後でまとめて調べる (ループ中の物は止めるまで鳴り続ける)
            if (state.Playing)
            {
                playing++;
                if (!state.Loop)
                {
                    if (pollCount == _pollVoices.Length)
                    {
                        Array.Resize(ref _pollVoices,  pollCount * 2);
                        Array.Resize(ref _pollIndices, pollCount * 2);
                        Array.Resize(ref _pollResults, pollCount * 2);
                    }
                    _pollVoices[pollCount]  = state.Voice;
                    _pollIndices[pollCount] = i;
                    pollCount++;
                }
            }
        }

        if (pollCount > 0)
        {
            fixed (uint* voices = _pollVoices)
            fixed (byte* results = _pollResults)
            {
                AudioNativeMethods.Audio_GetPlaying(voices, (uint)pollCount, results);
            }
            for (int n = 0; n < pollCount; n++)
            {
                if (_pollResults[n] != 0) continue;
                var index = _pollIndices[n];
                _states[entities[index].Id].Playing = false;
                components[index].Playing = false;
                playing--;
            }
        }
        PlayingCount = playing;
    }

    // 再生していない間 (エディターの編集中 / 一時停止) に毎フレーム呼ぶ (v1.6)
    //   外された AudioSource のボイスの削除だけ行う。以前は Update が再生中しか呼ばれず、Stop でシーンを消しても
    //   削除の予約が残ったままで、音が鳴り続けた。編集中に届いた Play() は捨てる (編集中は鳴らさない)
    public void UpdateStopped()
    {
        if (!_enabled) return;
        flushDestroy();
        _posted.Clear();
    }

    // 全部の音を止めて、ボイスを削除する (v1.6。Engine.EndPlay : エディターの Stop)
    //   AudioSource は次に再生した時に、ボイスを作り直す (PlayOnStart ならその時に鳴る)
    public void StopAll()
    {
        if (!_enabled) return;
        flushDestroy();
        _posted.Clear();
        for (int i = 0; i < _states.Length; i++)
        {
            ref var state = ref _states[i];
            if (state.Active && state.Voice != AudioNativeMethods.InvalidHandle) AudioNativeMethods.Audio_DestroyVoice(state.Voice);
            state = default;
        }
        PlayingCount = 0;
    }

    // コピーから届いた Play() / Stop() を本物の AudioSource へ (届いた順。同じ物へ何度も来たら最後の物)
    private void applyPosted(World world)
    {
        if (_posted.Count == 0) return;
        var pool = world.GetPool<AudioSource>();
        foreach (var (entity, request) in _posted)
        {
            if (!world.IsAlive(entity) || !pool.Has(entity.Id)) continue;   // もう無い物 (削除された) は捨てる
            pool.Get(entity.Id).Request = request;
        }
        _posted.Clear();
    }

    private void flushDestroy()
    {
        if (_pendingDestroy.Count == 0) return;
        foreach (var voice in _pendingDestroy) AudioNativeMethods.Audio_DestroyVoice(voice);
        _pendingDestroy.Clear();
    }
}
