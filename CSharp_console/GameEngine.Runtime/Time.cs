using System.Diagnostics;

namespace GameEngine;

// ==========================================================================
// 時間 (毎フレームの初めに Engine が進める)
// ==========================================================================
public static class Time
{
    private static readonly Stopwatch _clock = Stopwatch.StartNew();
    private static long _lastTicks;

    // 前のフレームからの経過秒。止まっていた後 (デバッガーなど) に大きく飛ばないよう 0.1 秒で切る
    public static float  DeltaTime  { get; private set; }
    public static double TotalTime  { get; private set; }   // 起動からの秒
    public static long   FrameCount { get; private set; }

    public const float MaxDeltaTime = 0.1f;

    internal static void Tick()
    {
        var now = _clock.ElapsedTicks;
        if (FrameCount == 0) _lastTicks = now;
        var seconds = (now - _lastTicks) / (double)Stopwatch.Frequency;
        _lastTicks = now;

        DeltaTime  = (float)Math.Min(seconds, MaxDeltaTime);
        TotalTime  = _clock.Elapsed.TotalSeconds;
        FrameCount++;
    }
}
