using System.Diagnostics;
using System.Numerics;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// プロファイラーの窓 (v1.10)
//   View > Analysis : Profiler (概要) / CPU Usage / GPU Usage / Rendering Stats / Memory
//   以前は C++ (RHI.dll) の ImGui の窓 1 つ (F1) に全部を出していた
//     - ImGui のコンテキストがエディターと別だったので、窓の上のクリックが Hierarchy / Scene にもすり抜けていた
//     - ドッキングもできなかった
//   今はエディターの ImGui で描く (C++ からは Profiler.GetFrame / ReadValues で値だけ読む)。F1 の窓はエディターでは切る
//
//   - 履歴 : 最近の HistorySize フレームを持つ。グラフをクリックするとそのフレームを選ぶ (全部の窓がそのフレームを出す)
//   - Pause : 記録を止める (止めている間も選んだフレームを見られる)
//   - どの窓も閉じている間は何もしない (C++ から読まない。CPU の区間も測らない)
// ==========================================================================
internal sealed class ProfilerWindows
{
    public const int HistorySize = 300;

    private readonly Engine _engine;

    public bool ShowOverview, ShowCpu, ShowGpu, ShowRendering, ShowMemory;
    public bool AnyOpen => ShowOverview || ShowCpu || ShowGpu || ShowRendering || ShowMemory;

    /// <summary>概要の窓の「Editor」の欄に出す物 (以前の Stats の窓の中身。EditorApp が渡す)</summary>
    public Action? DrawEditorStats;

    /// <summary>v1.12 P3 : Memory の窓のレンダーターゲットの一覧に使う記録 (Render Graph Viewer の物)</summary>
    public Func<RenderGraphSnapshot?>? RenderGraphSource;

    // ---------- 履歴 (リング) ----------
    private sealed class Frame
    {
        public ProfilerFrameData Data;
        public ProfilerSample[] Samples = new ProfilerSample[64];
        public int    SampleCount;
        public double CpuSampledMs;   // C# の区間を測った時間 (フレームの頭から描画の後まで)
        public (string Name, double Value)[] Values = Array.Empty<(string, double)>();
        public int    ValueCount;
        public long   ManagedBytes;
        public int    FrameNumber;
        public PassTimingSample[] Passes = new PassTimingSample[32];   // v1.10 P2 : 描画パスの時間
        public int    PassCount;
    }
    private readonly Frame[] _history = new Frame[HistorySize];
    private int  _count;      // 入っている数 (最大 HistorySize)
    private int  _head;       // 次に書く場所
    private int  _selected = -1;   // 選んだフレーム (新しい方から何番目。-1 = 最新を追う)
    private bool _paused;
    private int  _frameNumber;

    private readonly List<(string Name, double Value)> _values = new();
    private readonly List<PassTimingSample> _passes = new();   // v1.10 P2
    private string? _selectedPass;                             // v1.10 P2 : GPU Usage の表で選んだパス (グラフに出す)
    private readonly List<(string Name, string Text)>  _infos  = new();
    private double _infosAt = -1.0;
    private readonly float[] _plot = new float[HistorySize];

    public ProfilerWindows(Engine engine)
    {
        _engine = engine;
        for (int i = 0; i < HistorySize; i++) _history[i] = new Frame();
    }

    /// <summary>テスト用 : 入っているフレームの数 / 一番新しいフレーム</summary>
    public int HistoryCount => _count;
    public int RecordedFrames => _frameNumber;   // 記録した数 (履歴が一杯になっても増える)
    public bool Paused { get => _paused; set => _paused = value; }
    public ProfilerFrameData LatestData => _count > 0 ? frameAt(0).Data : default;
    public int LatestSampleCount => _count > 0 ? frameAt(0).SampleCount : 0;
    public string[] LatestSampleNames => _count > 0 ? frameAt(0).Samples.Take(frameAt(0).SampleCount).Select(s => s.Name).ToArray() : Array.Empty<string>();

    // 新しい方から ago 番目 (0 = 最新)
    private Frame frameAt(int ago) => _history[((_head - 1 - ago) % HistorySize + HistorySize) % HistorySize];

    private Frame? selectedFrame
    {
        get
        {
            if (_count == 0) return null;
            var ago = _selected < 0 ? 0 : Math.Min(_selected, _count - 1);
            return frameAt(ago);
        }
    }

    // ---------------------------------------------------------------------
    // 毎フレーム (EditorApp.update の中、窓を描く前)
    // ---------------------------------------------------------------------
    public void Update()
    {
        // CPU の区間は CPU Usage の窓が開いている間だけ測る (閉じている間は BeginSample が bool を見るだけ)
        Profiler.Enabled = ShowCpu && !_paused;
        // v1.10 P2 : 描画パスの GPU の時間 (タイムスタンプ) は GPU Usage の窓が開いている間だけ計る
        Profiler.PassTimingEnabled = ShowGpu && !_paused;
        if (!AnyOpen || _paused) return;

        if (!Profiler.GetFrame(out var data)) return;
        var frame = _history[_head];
        frame.Data = data;
        frame.FrameNumber = ++_frameNumber;
        // C# の区間 (前のフレームの物。C++ の値も前のフレームの描画の物なので揃う)
        var samples = Profiler.LastFrameSamples;
        if (frame.Samples.Length < samples.Length) frame.Samples = new ProfilerSample[Math.Max(samples.Length, frame.Samples.Length * 2)];
        samples.CopyTo(frame.Samples);
        frame.SampleCount  = samples.Length;
        frame.CpuSampledMs = Profiler.LastFrameMilliseconds;
        // 値 (名前の文字列は C++ 側で一度読んだ物を使い回すので、ここでは配列に写すだけ)
        Profiler.ReadValues(_values);
        if (frame.Values.Length < _values.Count) frame.Values = new (string, double)[Math.Max(_values.Count, 16)];
        _values.CopyTo(frame.Values);
        frame.ValueCount   = _values.Count;
        frame.ManagedBytes = GC.GetTotalMemory(false);
        // v1.10 P2 : 描画パスの時間 (GPU / C++ の CPU)
        Profiler.ReadPassTimings(_passes);
        if (frame.Passes.Length < _passes.Count) frame.Passes = new PassTimingSample[Math.Max(_passes.Count, frame.Passes.Length * 2)];
        _passes.CopyTo(frame.Passes);
        frame.PassCount = _passes.Count;
        _head = (_head + 1) % HistorySize;
        if (_count < HistorySize) _count++;
        if (_selected >= 0 && _selected < HistorySize - 1) _selected++;   // 選んだフレームが流れないように (新しい方から数えるので 1 つずれる)

        // 文字列の情報 (描画パイプラインの名前など) はめったに変わらないので 0.5 秒ごと
        if (Time.TotalTime - _infosAt >= 0.5 || _infosAt < 0.0) { Profiler.ReadInfos(_infos); _infosAt = Time.TotalTime; }
    }

    public void Draw()
    {
        if (ShowOverview)  drawOverview();
        if (ShowCpu)       drawCpu();
        if (ShowGpu)       drawGpu();
        if (ShowRendering) drawRendering();
        if (ShowMemory)    drawMemory();
    }

    // ---------------------------------------------------------------------
    // 共通の部品
    // ---------------------------------------------------------------------

    // 上の帯 : 一時停止 / 最新へ / 消す / 今見ているフレーム
    private void drawToolbar()
    {
        if (ImGui.Checkbox("Pause", ref _paused)) { }
        ImGui.SameLine();
        if (ImGui.SmallButton("Latest")) _selected = -1;
        ImGui.SameLine();
        if (ImGui.SmallButton("Clear")) { _count = 0; _head = 0; _selected = -1; }
        ImGui.SameLine();
        if (selectedFrame is { } f)
            ImGui.TextDisabled(_selected < 0 ? $"frame #{f.FrameNumber} (latest)" : $"frame #{f.FrameNumber} ({_selected} frames ago)");
        else
            ImGui.TextDisabled("(no frames recorded)");
    }

    // 履歴のグラフ (古い → 新しい)。クリック / ドラッグでフレームを選ぶ。選んだフレームに縦線
    private void drawGraph(string id, Func<Frame, double> value, string overlay, float height = 60.0f)
    {
        var n = _count;
        float max = 0.0001f;
        for (int i = 0; i < n; i++)
        {
            var v = (float)value(frameAt(n - 1 - i));
            _plot[i] = v;
            if (v > max) max = v;
        }
        ImGui.PlotLines(id, ref _plot[0], Math.Max(n, 1), 0, overlay, 0.0f, max * 1.2f, new Vector2(-1.0f, height));
        var min = ImGui.GetItemRectMin();
        var size = ImGui.GetItemRectSize();
        if (n > 1 && ImGui.IsItemHovered() && ImGui.IsMouseDown(ImGuiMouseButton.Left))
        {
            var t = Math.Clamp((ImGui.GetMousePos().X - min.X) / Math.Max(size.X, 1.0f), 0.0f, 1.0f);
            var index = (int)MathF.Round(t * (n - 1));   // 0 = 一番古い
            _selected = n - 1 - index;
            _paused = true;   // 選んだら止める (流れていくと見られないので)
        }
        if (_selected >= 0 && n > 1)
        {
            var x = min.X + size.X * (n - 1 - Math.Min(_selected, n - 1)) / (float)(n - 1);
            ImGui.GetWindowDrawList().AddLine(new Vector2(x, min.Y), new Vector2(x, min.Y + size.Y), 0xFF40C0FFu, 1.5f);
        }
    }

    private static bool beginTable(string id, int columns = 2)
    {
        return ImGui.BeginTable(id, columns, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp);
    }

    private static void row(string name, string value)
    {
        ImGui.TableNextRow();
        ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(name);
        ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(value);
    }

    private static string ms(double v) => $"{v:F3} ms";

    internal static string Bytes(double bytes)
    {
        const double kb = 1024.0, mb = kb * 1024.0, gb = mb * 1024.0;
        if (bytes >= gb) return $"{bytes / gb:F2} GB";
        if (bytes >= mb) return $"{bytes / mb:F2} MB";
        if (bytes >= kb) return $"{bytes / kb:F1} KB";
        return $"{bytes:F0} B";
    }

    private static IEnumerable<(string Name, double Value)> values(Frame f) => f.Values.Take(f.ValueCount);

    // 値の分け方 : GPU で始まる → GPU の窓 / Physics で始まる → CPU の窓 (物理) / (ms) の付いた物 → CPU の窓 / 残り → Rendering の窓
    private static bool isGpuValue(string name) => name.StartsWith("GPU", StringComparison.Ordinal);
    private static bool isPhysicsValue(string name) => name.StartsWith("Physics", StringComparison.Ordinal);
    private static bool isCpuTiming(string name) => !isGpuValue(name) && !isPhysicsValue(name) && name.Contains("(ms)", StringComparison.Ordinal);
    private static bool isMemoryValue(string name) => name.Contains("bytes", StringComparison.OrdinalIgnoreCase) || name.Contains("memory", StringComparison.OrdinalIgnoreCase);

    // ---------------------------------------------------------------------
    // Profiler (概要)
    // ---------------------------------------------------------------------
    /// <summary>確認用 : 次のフレームで、その名前の窓をドッキングから外してこの位置 / 大きさに置く (例 : シーンビューの上に重ねる)</summary>
    public readonly Dictionary<string, (Vector2 Position, Vector2 Size)> TestPlacements = new();

    // 窓を始める (確認用の置き場所があれば先に当てる)
    private bool begin(string name, ref bool open)
    {
        if (TestPlacements.Remove(name, out var placement))
        {
            ImGui.SetNextWindowDockID(0, ImGuiCond.Always);
            ImGui.SetNextWindowPos(placement.Position, ImGuiCond.Always);
            ImGui.SetNextWindowSize(placement.Size, ImGuiCond.Always);
            ImGui.SetNextWindowFocus();
        }
        return ImGui.Begin(name, ref open);
    }
    /// <summary>確認用 : 前のフレームで Profiler の窓にマウスが乗っていたか / 窓の位置と大きさ</summary>
    public bool OverviewHovered { get; private set; }
    public (Vector2 Position, Vector2 Size) OverviewRect { get; private set; }

    private void drawOverview()
    {
        var open = begin("Profiler", ref ShowOverview);
        OverviewHovered = ImGui.IsWindowHovered(ImGuiHoveredFlags.ChildWindows);
        OverviewRect = (ImGui.GetWindowPos(), ImGui.GetWindowSize());
        if (open)
        {
            drawToolbar();
            if (selectedFrame is { } f)
            {
                var d = f.Data;
                ImGui.Text($"{d.AverageFps:F1} FPS  ({d.AverageFrameMs:F2} ms)");
                ImGui.SameLine();
                ImGui.TextDisabled($"min {d.MinFrameMs:F2} / max {d.MaxFrameMs:F2}  (0.5 s average)");
                if (Profiler.GpuName is { Length: > 0 } gpu) ImGui.TextDisabled(gpu);
                ImGui.TextDisabled($"{d.Width}x{d.Height}  VSync {(d.VSync ? "ON" : "OFF")}  Tearing {(d.Tearing ? "ON" : "OFF")}  Frames in flight {d.FramesInFlight}");
                foreach (var (name, text) in _infos) ImGui.TextDisabled($"{name} : {text}");

                drawGraph("##cpuFrame", x => x.Data.FrameMs, $"CPU frame {d.FrameMs:F2} ms");
                drawGraph("##gpuFrame", x => x.Data.GpuValid ? x.Data.GpuTotalMs : 0.0, d.GpuValid ? $"GPU {d.GpuTotalMs:F3} ms" : "GPU (measuring...)");

                if (beginTable("overview"))
                {
                    row("CPU frame", ms(d.FrameMs));
                    row("GPU total", d.GpuValid ? ms(d.GpuTotalMs) : "(measuring...)");
                    row("Draw calls", (d.GpuCulling && d.VisibleValid ? d.VisibleDraws : d.DrawCalls).ToString("N0"));
                    row("Triangles", d.Triangles.ToString("N0"));
                    row("VRAM", d.VramValid ? $"{Bytes(d.VramUsage)} / {Bytes(d.VramBudget)}" : "(unavailable)");
                    ImGui.EndTable();
                }
                ImGui.TextDisabled("Details : View > Analysis > CPU Usage / GPU Usage / Rendering Stats / Memory");
            }
            if (DrawEditorStats != null && ImGui.CollapsingHeader("Editor", ImGuiTreeNodeFlags.DefaultOpen)) DrawEditorStats();
        }
        ImGui.End();
    }

    // ---------------------------------------------------------------------
    // CPU Usage
    // ---------------------------------------------------------------------
    private bool _cpuTimeline = true;

    private void drawCpu()
    {
        if (begin("CPU Usage", ref ShowCpu))
        {
            drawToolbar();
            if (selectedFrame is { } f)
            {
                var d = f.Data;
                drawGraph("##cpu", x => x.Data.FrameMs, $"CPU frame {d.FrameMs:F2} ms");
                drawGraph("##cpuUpdate", x => x.CpuSampledMs, $"C# frame (sampled) {f.CpuSampledMs:F2} ms", 40.0f);

                // ---------- C# の区間 (タイムライン + 表) ----------
                if (ImGui.CollapsingHeader("C# frame (samples)", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    if (f.SampleCount == 0)
                    {
                        ImGui.TextDisabled("(no samples yet)");
                    }
                    else
                    {
                        ImGui.Checkbox("Timeline", ref _cpuTimeline);
                        if (_cpuTimeline) drawTimeline(f);
                        drawSampleTable(f);
                    }
                    ImGui.TextDisabled("Game code : Profiler.BeginSample(\"name\") / EndSample(), or using (Profiler.Sample(\"name\")) { }");
                }

                // ---------- C++ の描画 (待ち / 記録 / Present) ----------
                if (ImGui.CollapsingHeader("Render thread (native)", ImGuiTreeNodeFlags.DefaultOpen) && beginTable("native"))
                {
                    row("Frame", ms(d.FrameMs));
                    row("  Wait (latency / fence)", ms(d.WaitMs));
                    row("  Record scene", ms(d.SceneRecordMs));
                    row("  F1 overlay (Player only)", ms(d.UiMs));
                    row("  Present", ms(d.PresentMs));
                    ImGui.EndTable();
                }

                // ---------- 描画パスごとの C++ の記録の時間 (v1.10 P2) ----------
                if (f.PassCount > 0 && ImGui.CollapsingHeader("Render passes (C++ record)", ImGuiTreeNodeFlags.DefaultOpen))
                    drawPassTable(f, gpu: false);

                // ---------- 報告された時間 / 物理 ----------
                if (ImGui.CollapsingHeader("Reported timings", ImGuiTreeNodeFlags.DefaultOpen) && beginTable("timings"))
                {
                    foreach (var (name, value) in values(f)) if (isCpuTiming(name)) row(name, $"{value:F3}");
                    ImGui.EndTable();
                }
                if (values(f).Any(v => isPhysicsValue(v.Name)) && ImGui.CollapsingHeader("Physics", ImGuiTreeNodeFlags.DefaultOpen) && beginTable("physics"))
                {
                    foreach (var (name, value) in values(f))
                        if (isPhysicsValue(name)) row(name, name.Contains("(ms)") ? $"{value:F3}" : $"{value:N0}");
                    ImGui.EndTable();
                }
            }
        }
        ImGui.End();
    }

    // 区間の帯 (横 = 時間、縦 = 入れ子の深さ)。上に乗るとその区間の名前と時間
    private static void drawTimeline(Frame f)
    {
        const float rowHeight = 18.0f;
        var maxDepth = 0;
        double end = 0.0;
        for (int i = 0; i < f.SampleCount; i++)
        {
            maxDepth = Math.Max(maxDepth, f.Samples[i].Depth);
            end = Math.Max(end, f.Samples[i].StartMs + f.Samples[i].Milliseconds);
        }
        end = Math.Max(end, f.CpuSampledMs);
        if (end <= 0.0) return;

        var width = ImGui.GetContentRegionAvail().X;
        var height = (maxDepth + 1) * rowHeight;
        var origin = ImGui.GetCursorScreenPos();
        ImGui.InvisibleButton("##timeline", new Vector2(Math.Max(width, 1.0f), height));
        var draw = ImGui.GetWindowDrawList();
        var mouse = ImGui.GetMousePos();
        var hovered = ImGui.IsItemHovered();
        draw.AddRectFilled(origin, origin + new Vector2(width, height), 0x30FFFFFFu);
        for (int i = 0; i < f.SampleCount; i++)
        {
            var s = f.Samples[i];
            var x0 = origin.X + (float)(s.StartMs / end) * width;
            var x1 = origin.X + (float)((s.StartMs + s.Milliseconds) / end) * width;
            if (x1 - x0 < 1.0f) x1 = x0 + 1.0f;
            var y0 = origin.Y + s.Depth * rowHeight;
            var min = new Vector2(x0, y0 + 1.0f);
            var max = new Vector2(x1, y0 + rowHeight - 1.0f);
            draw.AddRectFilled(min, max, colorOf(s.Name));
            draw.AddRect(min, max, 0x60000000u);
            if (x1 - x0 > 40.0f)
            {
                draw.PushClipRect(min, max, true);
                draw.AddText(new Vector2(x0 + 3.0f, y0 + 2.0f), 0xFF000000u, s.Name);
                draw.PopClipRect();
            }
            if (hovered && mouse.X >= min.X && mouse.X <= max.X && mouse.Y >= min.Y && mouse.Y <= max.Y)
                ImGui.SetTooltip($"{s.Name}\n{s.Milliseconds:F3} ms (start {s.StartMs:F3} ms)");
        }
    }

    // 名前から決まる色 (同じ区間はいつも同じ色)
    private static uint colorOf(string name)
    {
        uint hash = 2166136261u;
        foreach (var c in name) hash = (hash ^ c) * 16777619u;
        var hue = (hash % 360u) / 360.0f;
        ImGui.ColorConvertHSVtoRGB(hue, 0.45f, 0.95f, out var r, out var g, out var b);
        return 0xFF000000u | ((uint)(b * 255) << 16) | ((uint)(g * 255) << 8) | (uint)(r * 255);
    }

    // 区間の表 (入れ子は字下げ)。Self = 子の区間を除いた時間
    private static void drawSampleTable(Frame f)
    {
        if (!ImGui.BeginTable("samples", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY,
                              new Vector2(0.0f, Math.Min(22.0f * (f.SampleCount + 1), 360.0f)))) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Sample", ImGuiTableColumnFlags.WidthStretch, 3.0f);
        ImGui.TableSetupColumn("Total ms");
        ImGui.TableSetupColumn("Self ms");
        ImGui.TableSetupColumn("% frame");
        ImGui.TableHeadersRow();
        var frameMs = Math.Max(f.CpuSampledMs, 0.0001);
        for (int i = 0; i < f.SampleCount; i++)
        {
            var s = f.Samples[i];
            var children = 0.0;
            for (int j = i + 1; j < f.SampleCount && f.Samples[j].Depth > s.Depth; j++)
                if (f.Samples[j].Depth == s.Depth + 1) children += f.Samples[j].Milliseconds;
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0); ImGui.TextUnformatted(new string(' ', s.Depth * 3) + s.Name);
            ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted($"{s.Milliseconds:F3}");
            ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted($"{Math.Max(0.0, s.Milliseconds - children):F3}");
            ImGui.TableSetColumnIndex(3); ImGui.TextUnformatted($"{s.Milliseconds / frameMs * 100.0:F1}");
        }
        ImGui.EndTable();
    }

    // ---------------------------------------------------------------------
    // GPU Usage
    // ---------------------------------------------------------------------
    private void drawGpu()
    {
        if (begin("GPU Usage", ref ShowGpu))
        {
            drawToolbar();
            if (selectedFrame is { } f)
            {
                var d = f.Data;
                drawGraph("##gpuTotal", x => x.Data.GpuValid ? x.Data.GpuTotalMs : 0.0, d.GpuValid ? $"GPU total {d.GpuTotalMs:F3} ms" : "GPU (measuring...)");
                drawGraph("##gpuScene", x => x.Data.GpuValid ? x.Data.GpuSceneMs : 0.0, d.GpuValid ? $"Scene {d.GpuSceneMs:F3} ms" : "", 40.0f);
                if (beginTable("gpu"))
                {
                    if (d.GpuValid)
                    {
                        row("Total", ms(d.GpuTotalMs));
                        row("  Scene (render passes + editor / game UI)", ms(d.GpuSceneMs));
                        row("  F1 overlay (Player only)", ms(d.GpuUiMs));
                    }
                    else row("Total", "(measuring...)");
                    var skin = _engine.SkinningStats;
                    if (skin.Objects > 0) row("Skinning (compute)", $"{skin.GpuMilliseconds:F3} ms ({skin.GpuInstances} objects, {skin.GpuVertices:N0} vertices)");
                    foreach (var (name, value) in values(f))
                        if (isGpuValue(name)) row(name, name.Contains("(ms)") ? $"{value:F3}" : $"{value:N0}");
                    ImGui.EndTable();
                }
                // ---------- 描画パスごと (v1.10 P2) ----------
                if (ImGui.CollapsingHeader("Render passes", ImGuiTreeNodeFlags.DefaultOpen))
                {
                    if (_selectedPass != null)
                    {
                        var name = _selectedPass;
                        drawGraph("##passGraph", x => passGpu(x, name), $"{name} : {passGpu(f, name):F3} ms (click a row to change, right-click to clear)", 40.0f);
                    }
                    if (f.PassCount == 0) ImGui.TextDisabled("(no render passes yet)");
                    else drawPassTable(f, gpu: true);
                    ImGui.TextDisabled("Passes come from RenderCommandBuffer.BeginPass (also shown as PIX / RenderDoc events).");
                }
                ImGui.TextDisabled("GPU values are a few frames old (timestamp queries are read back after the GPU finishes).");
            }
        }
        ImGui.End();
    }

    // 同じ名前のパスの GPU の時間の合計 (カメラが複数あると同じ名前が何度も出る。入れ子の子は親に含まれるので一番浅い物だけ足す)
    private static double passGpu(Frame f, string name)
    {
        double sum = 0.0;
        var depth = int.MaxValue;
        for (int i = 0; i < f.PassCount; i++) if (f.Passes[i].Name == name) depth = Math.Min(depth, f.Passes[i].Depth);
        for (int i = 0; i < f.PassCount; i++)
        {
            var p = f.Passes[i];
            if (p.Name == name && p.Depth == depth && p.HasGpu) sum += p.GpuMilliseconds;
        }
        return sum;
    }

    // 描画パスの表 (入れ子は字下げ)。gpu = true : GPU の時間 / 割合 / CPU、false : CPU (C++ が命令を積んだ時間) だけ
    private void drawPassTable(Frame f, bool gpu)
    {
        var columns = gpu ? 4 : 2;
        if (!ImGui.BeginTable(gpu ? "passesGpu" : "passesCpu", columns,
                              ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY,
                              new Vector2(0.0f, Math.Min(22.0f * (f.PassCount + 1), 420.0f)))) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Pass", ImGuiTableColumnFlags.WidthStretch, 3.0f);
        if (gpu)
        {
            ImGui.TableSetupColumn("GPU ms");
            ImGui.TableSetupColumn("% GPU");
        }
        ImGui.TableSetupColumn("CPU ms");
        ImGui.TableHeadersRow();
        // 割合の分母 : 一番外 (深さ 0) のパスの GPU の合計
        double total = 0.0;
        for (int i = 0; i < f.PassCount; i++) if (f.Passes[i].Depth == 0 && f.Passes[i].HasGpu) total += f.Passes[i].GpuMilliseconds;
        for (int i = 0; i < f.PassCount; i++)
        {
            var p = f.Passes[i];
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            var label = new string(' ', p.Depth * 3) + p.Name + "##pass" + i;
            if (gpu)
            {
                if (ImGui.Selectable(label, _selectedPass == p.Name, ImGuiSelectableFlags.SpanAllColumns)) _selectedPass = p.Name;
                if (ImGui.IsItemClicked(ImGuiMouseButton.Right)) _selectedPass = null;
                ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(p.HasGpu ? $"{p.GpuMilliseconds:F3}" : "-");
                ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted(p.HasGpu && total > 0.0 ? $"{p.GpuMilliseconds / total * 100.0:F1}" : "-");
                ImGui.TableSetColumnIndex(3); ImGui.TextUnformatted($"{p.CpuMilliseconds:F3}");
            }
            else
            {
                ImGui.TextUnformatted(new string(' ', p.Depth * 3) + p.Name);
                ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted($"{p.CpuMilliseconds:F3}");
            }
        }
        ImGui.EndTable();
    }

    // ---------------------------------------------------------------------
    // Rendering Stats
    // ---------------------------------------------------------------------
    private void drawRendering()
    {
        if (begin("Rendering Stats", ref ShowRendering))
        {
            drawToolbar();
            if (selectedFrame is { } f)
            {
                var d = f.Data;
                drawGraph("##draws", x => x.Data.GpuCulling && x.Data.VisibleValid ? x.Data.VisibleDraws : x.Data.DrawCalls,
                          $"Draw calls {(d.GpuCulling && d.VisibleValid ? d.VisibleDraws : d.DrawCalls):N0}", 40.0f);

                // 描き方の切り替え (以前は F1 の窓にあった。効果をその場で比べられる)
                var g = _engine.Graphics;
                var indirect = g.IndirectDraw;
                if (ImGui.Checkbox("ExecuteIndirect", ref indirect)) g.IndirectDraw = indirect;
                ImGui.SameLine(); ImGui.TextDisabled(d.IndirectDraw ? (d.IndirectArgsReused ? "(args reused)" : "(args written)") : "(direct draw)");
                var instancing = g.Instancing;
                if (ImGui.Checkbox("Instancing", ref instancing)) g.Instancing = instancing;
                var culling = g.GpuCulling;
                if (ImGui.Checkbox("GPU culling", ref culling)) g.GpuCulling = culling;
                if (d.GpuCulling) { ImGui.SameLine(); ImGui.TextDisabled("(frustum)"); }
                else if (culling) { ImGui.SameLine(); ImGui.TextDisabled("(needs ExecuteIndirect)"); }
                var occlusion = g.OcclusionCulling;
                if (ImGui.Checkbox("Occlusion (Hi-Z)", ref occlusion)) g.OcclusionCulling = occlusion;
                if (d.OcclusionCulling) { ImGui.SameLine(); ImGui.TextDisabled("(two-phase)"); }
                else if (occlusion && !d.GpuCulling) { ImGui.SameLine(); ImGui.TextDisabled("(needs GPU culling)"); }

                if (beginTable("rendering"))
                {
                    row("Draw calls (CPU)", d.DrawCalls.ToString("N0"));
                    if (d.GpuCulling && d.VisibleValid)
                    {
                        row("Draw calls (after GPU culling)", d.VisibleDraws.ToString("N0"));
                        row("Visible instances (GPU)", d.VisibleInstances.ToString("N0"));
                    }
                    row("Instances", d.Instances.ToString("N0"));
                    row("Triangles", d.Triangles.ToString("N0"));
                    row("Objects", d.Objects.ToString("N0"));
                    row("Custom shader draws", g.CustomDrawCount.ToString("N0"));
                    row("Render size", $"{d.Width} x {d.Height}");
                    foreach (var (name, value) in values(f))
                        if (!isGpuValue(name) && !isPhysicsValue(name) && !isCpuTiming(name) && !isMemoryValue(name)) row(name, $"{value:N0}");
                    ImGui.EndTable();
                }
            }
        }
        ImGui.End();
    }

    // ---------------------------------------------------------------------
    // Memory
    // ---------------------------------------------------------------------
    private bool _rtSortBySize = true;

    private void drawRenderTargetMemory()
    {
        var snapshot = RenderGraphSource?.Invoke();
        if (snapshot == null) { ImGui.TextDisabled("(capturing...)"); return; }
        var list = snapshot.Resources.Where(r => r.Bytes > 0).ToList();
        list = _rtSortBySize ? list.OrderByDescending(r => r.Bytes).ToList() : list.OrderBy(r => r.Name, StringComparer.Ordinal).ToList();
        var total = list.Sum(r => r.Bytes);
        ImGui.Text($"{list.Count} resources, {Bytes(total)}");
        ImGui.SameLine();
        ImGui.Checkbox("Sort by size", ref _rtSortBySize);
        if (!ImGui.BeginTable("rtMemory", 4, ImGuiTableFlags.RowBg | ImGuiTableFlags.BordersInnerV | ImGuiTableFlags.SizingStretchProp | ImGuiTableFlags.ScrollY,
                              new Vector2(0.0f, Math.Min(22.0f * (list.Count + 1), 320.0f)))) return;
        ImGui.TableSetupScrollFreeze(0, 1);
        ImGui.TableSetupColumn("Name", ImGuiTableColumnFlags.WidthStretch, 3.0f);
        ImGui.TableSetupColumn("Format");
        ImGui.TableSetupColumn("Size");
        ImGui.TableSetupColumn("Memory");
        ImGui.TableHeadersRow();
        foreach (var r in list)
        {
            ImGui.TableNextRow();
            ImGui.TableSetColumnIndex(0);
            if (r.IsUsed) ImGui.TextUnformatted(r.Name); else ImGui.TextDisabled(r.Name + " (unused this frame)");
            ImGui.TableSetColumnIndex(1); ImGui.TextUnformatted(r.Format + (r.Samples > 1 ? $" x{r.Samples}" : ""));
            ImGui.TableSetColumnIndex(2); ImGui.TextUnformatted($"{r.Width}x{r.Height}{(r.ArraySize > 1 ? $"x{r.ArraySize}" : "")}");
            ImGui.TableSetColumnIndex(3); ImGui.TextUnformatted(Bytes(r.Bytes));
        }
        ImGui.EndTable();
    }
    private void drawMemory()
    {
        if (begin("Memory", ref ShowMemory))
        {
            drawToolbar();
            if (selectedFrame is { } f)
            {
                var d = f.Data;
                drawGraph("##managed", x => x.ManagedBytes / (1024.0 * 1024.0), $"Managed heap {Bytes(f.ManagedBytes)}", 40.0f);
                if (d.VramValid)
                {
                    ImGui.ProgressBar((float)(d.VramUsage / (double)Math.Max(d.VramBudget, 1UL)), new Vector2(-1.0f, 0.0f),
                                      $"VRAM {Bytes(d.VramUsage)} / {Bytes(d.VramBudget)}");
                }
                if (beginTable("gpuMemory"))
                {
                    row("VRAM (usage / budget)", d.VramValid ? $"{Bytes(d.VramUsage)} / {Bytes(d.VramBudget)}" : "(unavailable)");
                    row("Mesh buffers", Bytes(_engine.Graphics.MeshBytesUsed));
                    row("Upload (this frame)", Bytes(d.UploadBytes));
                    row("Upload ring", $"{Bytes(d.UploadRingUsed)} / {Bytes(d.UploadRingSize)}");
                    row("Models loaded", _engine.Graphics.LoadedModelCount.ToString("N0"));
                    row("Materials loaded", _engine.Assets.LoadedMaterials.Count().ToString("N0"));
                    foreach (var (name, value) in values(f)) if (isMemoryValue(name)) row(name, Bytes(value));
                    ImGui.EndTable();
                }
                if (ImGui.CollapsingHeader("Managed (.NET)", ImGuiTreeNodeFlags.DefaultOpen) && beginTable("managed"))
                {
                    var info = GC.GetGCMemoryInfo();
                    row("Heap (allocated objects)", Bytes(f.ManagedBytes));
                    row("Heap size (committed)", Bytes(info.HeapSizeBytes));
                    row("GC collections (gen0 / gen1 / gen2)", $"{GC.CollectionCount(0)} / {GC.CollectionCount(1)} / {GC.CollectionCount(2)}");
                    row("Allocated this process", Bytes(GC.GetTotalAllocatedBytes()));
                    ImGui.EndTable();
                }
                if (ImGui.CollapsingHeader("Process") && beginTable("process"))
                {
                    using var process = Process.GetCurrentProcess();
                    row("Working set", Bytes(process.WorkingSet64));
                    row("Private bytes", Bytes(process.PrivateMemorySize64));
                    ImGui.EndTable();
                }
                // v1.12 P3 : レンダーターゲットごとの GPU のメモリ (以前は「予定」と出していた)
                if (ImGui.CollapsingHeader("Render targets", ImGuiTreeNodeFlags.DefaultOpen)) drawRenderTargetMemory();
            }
        }
        ImGui.End();
    }
}
