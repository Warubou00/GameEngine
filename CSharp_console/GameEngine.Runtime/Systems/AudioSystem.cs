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

    public AudioSystem(World world, bool enabled)
    {
        _enabled = enabled;
        // AudioSource が外れたら (エンティティの削除も含む)、C++ のボイスも消す
        world.GetPool<AudioSource>().Removed += (entity, _) =>
        {
            if ((uint)entity.Id >= (uint)_states.Length) return;
            ref var state = ref _states[entity.Id];
            if (state.Active && state.Voice != AudioNativeMethods.InvalidHandle) _pendingDestroy.Add(state.Voice);
            state = default;
        };
    }

    public void Update(World world)
    {
        if (!_enabled) return;
        flushDestroy();

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

            // 2. 変わった物だけ送る
            if (source.Volume != state.Volume)
            {
                AudioNativeMethods.Audio_SetVolume(state.Voice, source.Volume);
                state.Volume = source.Volume;
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

    private void flushDestroy()
    {
        if (_pendingDestroy.Count == 0) return;
        foreach (var voice in _pendingDestroy) AudioNativeMethods.Audio_DestroyVoice(voice);
        _pendingDestroy.Clear();
    }
}
