using System.Runtime.InteropServices;

namespace GameEngine.Native;

// ==========================================================================
// Audio.dll (C++) の関数の宣言
//   C++ 側は Audio/Source/audioDll.cpp (XAudio2 で wav を鳴らす)
//   - サウンド (読み込んだ wav) とボイス (AudioSource 1 つに 1 個) はハンドル (uint) でやり取りする
//   - 「再生中か」は毎フレーム調べるので、配列でまとめて 1 回で聞く
//   - ログは Core.dll の Logger に出る (Renderer_SetLogCallback で設定したものと同じ)
//   エンジンの利用者は直接使わない (AssetManager.LoadSound / AudioSource / AudioSystem 経由で使う)
// ==========================================================================
internal static unsafe class AudioNativeMethods
{
    private const string Dll = "Audio.dll";

    // C++ の Audio::INVALID_HANDLE と同じ
    public const uint InvalidHandle = 0xFFFFFFFFu;

    // ---------- 初期化 / 終了 ----------

    // 戻り値 : 再生できるか (音の出る機器が無い時は false。それでも LoadSound はできる)
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Audio_Initialize();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Audio_Shutdown();

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Audio_SetMasterVolume(float volume);

    // ---------- サウンド ----------

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Audio_LoadSound([MarshalAs(UnmanagedType.LPWStr)] string filePath, out float lengthSeconds);

    // このサウンドを使っているボイスも破棄される
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Audio_DestroySound(uint sound);

    // ---------- ボイス ----------

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Audio_CreateVoice(uint sound);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Audio_DestroyVoice(uint voice);

    // C++ の bool は 1 バイトなので I1 で受ける / 渡す
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Audio_Play(uint voice);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Audio_Stop(uint voice);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Audio_SetVolume(uint voice, float volume);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Audio_SetLoop(uint voice, [MarshalAs(UnmanagedType.I1)] bool loop);

    // outPlaying[i] = 1 なら再生中
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Audio_GetPlaying(uint* voices, uint count, byte* outPlaying);
}
