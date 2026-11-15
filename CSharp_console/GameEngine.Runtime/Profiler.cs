using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using GameEngine.Native;

namespace GameEngine;

// ==========================================================================
// プロファイラー (v1.10)
//   - CPU の区間 : Profiler.BeginSample("名前") 〜 EndSample()、または using (Profiler.Sample("名前")) { ... }
//     エンジンのフレームの各段 (System の Update / 物理 / UI / 描画 …) もここで測る
//     Enabled が false の間は何もしない (エディターの CPU Usage の窓が開いている間だけ true)
//   - C++ の計測値 : GetFrame (FPS / CPU / GPU / 描画数 / VRAM …)、ReadValues (ReportProfilerValue で足された値)、ReadInfos
//   以前は C++ (RHI.dll) の ImGui の窓 (F1) に出すだけで、C# からは読めなかった
//   (エディターでは、その窓の上のクリックがエディターにもすり抜けていた。今はエディターの窓で出す)
// ==========================================================================

/// <summary>C++ の 1 フレーム分の計測値 (RHI::ProfilerFrameData と同じ並び : 184 byte)。GPU の値は数フレーム前のもの</summary>
[StructLayout(LayoutKind.Sequential)]
public struct ProfilerFrameData
{
    public double FrameMs, WaitMs, SceneRecordMs, UiMs, PresentMs;
    public double GpuSceneMs, GpuUiMs, GpuTotalMs;
    public double AverageFps, AverageFrameMs, MinFrameMs, MaxFrameMs;
    public ulong  Triangles, UploadBytes, UploadRingUsed, UploadRingSize, VramUsage, VramBudget;
    public uint   DrawCalls, Instances, VisibleInstances, VisibleDraws, Objects;
    public uint   Width, Height, FramesInFlight;
    public uint   Flags;
    private uint  _padding;

    public readonly bool GpuValid          => (Flags & (1u << 0)) != 0;
    public readonly bool Instancing        => (Flags & (1u << 1)) != 0;
    public readonly bool GpuCulling        => (Flags & (1u << 2)) != 0;
    public readonly bool OcclusionCulling  => (Flags & (1u << 3)) != 0;
    public readonly bool VisibleValid      => (Flags & (1u << 4)) != 0;
    public readonly bool IndirectDraw      => (Flags & (1u << 5)) != 0;
    public readonly bool IndirectArgsReused => (Flags & (1u << 6)) != 0;
    public readonly bool VSync             => (Flags & (1u << 7)) != 0;
    public readonly bool Tearing           => (Flags & (1u << 8)) != 0;
    public readonly bool VramValid         => (Flags & (1u << 9)) != 0;
}

/// <summary>CPU の区間 1 つ (Depth = 入れ子の深さ。StartMs = フレームの頭からの時間)</summary>
public readonly record struct ProfilerSample(string Name, int Depth, double StartMs, double Milliseconds);

/// <summary>
/// 描画パス 1 つの時間 (v1.10 P2。RenderCommandBuffer.BeginPass の区切り)。数フレーム前の値
/// GpuMilliseconds : GPU の時間 (Profiler.PassTimingEnabled の間だけ。計っていない時は -1) / CpuMilliseconds : C++ が命令を積んだ時間
/// </summary>
public readonly record struct PassTimingSample(string Name, int Depth, double GpuMilliseconds, double CpuMilliseconds)
{
    public bool HasGpu => GpuMilliseconds >= 0.0;
}

public static unsafe class Profiler
{
    private const int MaxSamples = 2048;
    private const int MaxDepth   = 32;

    private static ProfilerSample[] _current = new ProfilerSample[MaxSamples];
    private static ProfilerSample[] _last    = new ProfilerSample[MaxSamples];
    private static int  _currentCount, _lastCount;
    private static readonly int[]  _open      = new int[MaxDepth];   // 開いている区間の _current の番号
    private static readonly long[] _openStart = new long[MaxDepth];
    private static int  _depth;
    private static long _frameStart;
    private static bool _inFrame;   // このフレームで記録している (BeginFrame の時の Enabled。フレームの途中で Enabled が変わっても、そのフレームは最後まで同じ)
    private static double _lastFrameMs;

    /// <summary>CPU の区間を記録するか (false の間は BeginSample / EndSample は何もしない。変えた次のフレームから効く)</summary>
    public static bool Enabled { get; set; }

    /// <summary>前のフレームの CPU の区間 (始まった順。Enabled の間だけ増える)</summary>
    public static ReadOnlySpan<ProfilerSample> LastFrameSamples => _last.AsSpan(0, _lastCount);

    /// <summary>前のフレームで区間を測った時間 (BeginFrame から EndFrame まで。ms)</summary>
    public static double LastFrameMilliseconds => _lastFrameMs;

    /// <summary>区間を始める (EndSample と対にする。入れ子にできる)</summary>
    public static void BeginSample(string name)
    {
        if (!_inFrame) return;
        if (_depth >= MaxDepth) { _depth++; return; }   // 深すぎる分は数だけ数えて記録しない
        if (_currentCount >= MaxSamples) { _open[_depth++] = -1; return; }   // 区間が多すぎる分も記録しない (-1 = 記録していない)
        var now = Stopwatch.GetTimestamp();
        _open[_depth]      = _currentCount;
        _openStart[_depth] = now;
        _current[_currentCount++] = new ProfilerSample(name, _depth, ticksToMs(now - _frameStart), 0.0);
        _depth++;
    }

    /// <summary>最後に始めた区間を終える</summary>
    public static void EndSample()
    {
        // 以前は Enabled も見ていたので、フレームの途中で切ると閉じ忘れを閉じる EndFrame のループが終わらなかった
        if (!_inFrame || _depth == 0) return;
        _depth--;
        if (_depth >= MaxDepth) return;
        var index = _open[_depth];
        if (index < 0 || index >= _currentCount) return;
        var sample = _current[index];
        _current[index] = sample with { Milliseconds = ticksToMs(Stopwatch.GetTimestamp() - _openStart[_depth]) };
    }

    /// <summary>using で囲んだ間を測る : using (Profiler.Sample("AI")) { ... }</summary>
    public static Scope Sample(string name)
    {
        BeginSample(name);
        return new Scope(_inFrame);
    }

    public readonly struct Scope : IDisposable
    {
        private readonly bool _active;
        internal Scope(bool active) => _active = active;
        public void Dispose() { if (_active) EndSample(); }
    }

    // ---------- エンジンが呼ぶ (フレームの区切り) ----------

    internal static void BeginFrame()
    {
        _frameStart   = Stopwatch.GetTimestamp();
        _currentCount = 0;
        _depth        = 0;
        _inFrame      = Enabled;
    }

    internal static void EndFrame()
    {
        if (!_inFrame) { _lastCount = 0; return; }
        // 閉じ忘れの区間 (ゲームのコードの例外など) はここで閉じる
        while (_depth > 0) EndSample();
        _inFrame = false;
        _lastFrameMs = ticksToMs(Stopwatch.GetTimestamp() - _frameStart);
        (_current, _last) = (_last, _current);
        _lastCount = _currentCount;
        _currentCount = 0;
    }

    private static double ticksToMs(long ticks) => ticks * 1000.0 / Stopwatch.Frequency;

    // ---------- C++ の計測値 ----------

    /// <summary>C++ の最新の計測値 (描画の初期化の前は false)</summary>
    public static bool GetFrame(out ProfilerFrameData frame)
    {
        frame = default;
        fixed (ProfilerFrameData* p = &frame)
        {
            try { return NativeMethods.Renderer_GetProfilerFrame(p); }
            catch (EntryPointNotFoundException) { return false; }
        }
    }

    private static double[] _values = new double[64];
    private static readonly List<string> _valueNames = new();
    private static readonly byte[] _text = new byte[512];

    /// <summary>
    /// ReportProfilerValue などで足された値 (名前, 値) を output に入れる (足された順。名前は増えた時だけ C++ から読む)
    /// </summary>
    public static void ReadValues(List<(string Name, double Value)> output)
    {
        output.Clear();
        uint count;
        try
        {
            fixed (double* v = _values) count = NativeMethods.Renderer_GetProfilerValues(v, (uint)_values.Length);
            if (count > _values.Length)
            {
                _values = new double[Math.Max((int)count, _values.Length * 2)];
                fixed (double* v = _values) count = NativeMethods.Renderer_GetProfilerValues(v, (uint)_values.Length);
            }
            // 名前は足された順で変わらないので、増えた分だけ読む
            while (_valueNames.Count < count) _valueNames.Add(readValueName(_valueNames.Count));
        }
        catch (EntryPointNotFoundException) { return; }
        for (int i = 0; i < count && i < _valueNames.Count; i++) output.Add((_valueNames[i], _values[i]));
    }

    /// <summary>SetProfilerInfo で足された文字列 (名前, 文字列。例 : "Render pipeline" → "Forward / PBR ...")</summary>
    public static void ReadInfos(List<(string Name, string Text)> output)
    {
        output.Clear();
        try
        {
            var count = NativeMethods.Renderer_GetProfilerInfoCount();
            var name = new byte[128];
            for (uint i = 0; i < count; i++)
            {
                fixed (byte* n = name)
                fixed (byte* t = _text)
                {
                    if (!NativeMethods.Renderer_GetProfilerInfo(i, n, (uint)name.Length, t, (uint)_text.Length)) continue;
                }
                output.Add((zeroTerminated(name), zeroTerminated(_text)));
            }
        }
        catch (EntryPointNotFoundException) { }
    }

    private static string? _gpuName;

    /// <summary>GPU の名前 (例 : "NVIDIA GeForce RTX 2070")</summary>
    public static string GpuName
    {
        get
        {
            if (_gpuName is { Length: > 0 }) return _gpuName;
            try
            {
                fixed (byte* t = _text) NativeMethods.Renderer_GetGpuName(t, (uint)_text.Length);
                _gpuName = zeroTerminated(_text);
            }
            catch (EntryPointNotFoundException) { _gpuName = ""; }
            return _gpuName;
        }
    }

    /// <summary>
    /// C++ (RHI.dll) の F1 の窓を使うか (エディターは false : 自分の窓で出す。F1 も入力の横取りもしなくなる)
    /// </summary>
    public static void SetOverlayEnabled(bool enabled)
    {
        try { NativeMethods.Renderer_SetProfilerOverlayEnabled(enabled); } catch (EntryPointNotFoundException) { }
    }

    // ---------- 描画パスの時間 (v1.10 P2) ----------

    private static bool _passTiming;
    private static NativeMethods.PassTiming[] _passBuffer = new NativeMethods.PassTiming[64];
    private static readonly Dictionary<uint, string> _passNames = new();

    /// <summary>描画パスごとに GPU の時間を計るか (タイムスタンプを書く。エディターの GPU Usage の窓が開いている間だけ true)</summary>
    public static bool PassTimingEnabled
    {
        get => _passTiming;
        set
        {
            if (_passTiming == value) return;
            _passTiming = value;
            try { NativeMethods.Renderer_SetPassTimingEnabled(value); } catch (EntryPointNotFoundException) { }
        }
    }

    /// <summary>最後に読めたフレームの描画パスの時間を output に入れる (始まった順。入れ子は Depth)</summary>
    public static void ReadPassTimings(List<PassTimingSample> output)
    {
        output.Clear();
        uint count;
        try
        {
            fixed (NativeMethods.PassTiming* p = _passBuffer) count = NativeMethods.Renderer_GetPassTimings(p, (uint)_passBuffer.Length);
            if (count > _passBuffer.Length)
            {
                _passBuffer = new NativeMethods.PassTiming[Math.Max((int)count, _passBuffer.Length * 2)];
                fixed (NativeMethods.PassTiming* p = _passBuffer) count = NativeMethods.Renderer_GetPassTimings(p, (uint)_passBuffer.Length);
            }
        }
        catch (EntryPointNotFoundException) { return; }
        for (int i = 0; i < count && i < _passBuffer.Length; i++)
        {
            var t = _passBuffer[i];
            output.Add(new PassTimingSample(PassName(t.NameId), (int)t.Depth, t.GpuMs, t.CpuMs));
        }
    }

    // 番号 → 名前 (C++ に一度だけ聞く。番号は変わらない)
    internal static string PassName(uint id)
    {
        if (_passNames.TryGetValue(id, out var name)) return name;
        fixed (byte* t = _text) NativeMethods.Renderer_GetPassName(id, t, (uint)_text.Length);
        name = zeroTerminated(_text);
        _passNames[id] = name;
        return name;
    }

    private static string readValueName(int index)
    {
        fixed (byte* t = _text) NativeMethods.Renderer_GetProfilerValueName((uint)index, t, (uint)_text.Length);
        return zeroTerminated(_text);
    }

    private static string zeroTerminated(byte[] bytes)
    {
        var length = Array.IndexOf(bytes, (byte)0);
        return Encoding.UTF8.GetString(bytes, 0, length < 0 ? bytes.Length : length);
    }
}
