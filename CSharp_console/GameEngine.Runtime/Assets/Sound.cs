using GameEngine.Native;

namespace GameEngine;

// ==========================================================================
// 読み込んだサウンド (wav。C++ (Audio.dll) 側にある PCM を指すハンドル)
//   AudioSource に入れて使う。同じ Sound を何個の AudioSource で鳴らしても、PCM は共有される
//   対応形式 : リニア PCM (8 / 16 / 24 / 32bit)、32bit float、WAVE_FORMAT_EXTENSIBLE
// ==========================================================================
public readonly struct Sound
{
    internal readonly uint Handle;
    public readonly float  Length;   // 秒
    public readonly string Path;

    internal Sound(uint handle, float length, string path)
    {
        Handle = handle;
        Length = length;
        Path   = path;
    }

    public bool IsValid => Path != null && Handle != AudioNativeMethods.InvalidHandle;
    public override string ToString() => $"Sound({Path}, {Length:F2} s)";
}
