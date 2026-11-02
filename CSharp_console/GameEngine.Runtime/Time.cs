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

    // ---------- 固定の時間刻み (物理。P1) ----------
    // 物理は描画のフレームと関係なく、この刻みで進む (速さが違う PC でも同じ動きになる)
    private static float _fixedDeltaTime = 1.0f / 60.0f;
    public static float FixedDeltaTime
    {
        get => _fixedDeltaTime;
        set => _fixedDeltaTime = Math.Clamp(value, 1.0f / 1000.0f, 0.1f);
    }
    // 1 フレームで進める刻みの上限 (重い時に、追いつこうとしてさらに重くならないように)
    public static int    MaxFixedSteps { get; set; } = 5;
    // 前の刻みから次の刻みまでの、今の描画の位置 (0〜1。剛体の補間に使う)
    public static float  FixedAlpha    { get; internal set; }
    // FixedUpdate の中か (中なら DeltaTime の代わりに FixedDeltaTime を使う)
    public static bool   InFixedStep   { get; private set; }
    public static double FixedTime     { get; private set; }   // 固定の刻みで数えた起動からの秒
    // このフレームで進めた刻みの数
    public static int    FixedStepsThisFrame { get; internal set; }

    internal static void BeginFixedStep() => InFixedStep = true;
    internal static void EndFixedStep()
    {
        InFixedStep = false;
        FixedTime  += _fixedDeltaTime;
    }

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
