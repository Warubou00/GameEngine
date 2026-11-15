using System.Numerics;
using System.Runtime.InteropServices;

namespace GameEngine.Editor;

// ==========================================================================
// エディターの確認 (v1.10) : プロファイラーの窓 (Profiler / CPU Usage / GPU Usage / Rendering Stats / Memory)
//   - C++ の計測値を C# から読める (構造体の大きさ / 値 / 名前)
//   - CPU の区間がフレームごとに記録される (エンジンの段 + ゲームの System)
//   - C++ の F1 の窓はエディターでは出ない
//   - 窓の上のクリックがシーンビューにすり抜けない (以前の不具合)
// ==========================================================================
internal sealed partial class EditorTest
{
    private IEnumerable<int> profilerWindows()
    {
        var engine = _app.Engine;
        var windows = _app.ProfilerWindows;

        // ---------- C++ の計測値 ----------
        check(Marshal.SizeOf<ProfilerFrameData>() == 184, $"ProfilerFrameData is 184 bytes like RHI::ProfilerFrameData ({Marshal.SizeOf<ProfilerFrameData>()})");
        var got = Profiler.GetFrame(out var frame);
        check(got && frame.FrameMs > 0.0 && frame.Width > 0 && frame.Height > 0 && frame.DrawCalls + frame.Instances > 0,
              $"Profiler.GetFrame reads the native stats (frame {frame.FrameMs:F2} ms, {frame.Width}x{frame.Height}, {frame.DrawCalls} draws, {frame.Triangles:N0} triangles)");
        var values = new List<(string Name, double Value)>();
        Profiler.ReadValues(values);
        check(values.Any(v => v.Name == "C# update (ms)") && values.Any(v => v.Name == "Render commands" && v.Value > 0),
              $"Profiler.ReadValues reads the reported values by name ({values.Count} values)");
        var infos = new List<(string Name, string Text)>();
        Profiler.ReadInfos(infos);
        check(infos.Any(i => i.Name == "Render pipeline" && i.Text.Length > 0) && Profiler.GpuName.Length > 0,
              $"Profiler.ReadInfos / GpuName ({string.Join(", ", infos.Select(i => i.Name))} / {Profiler.GpuName})");

        // ---------- C++ の F1 の窓は出ない ----------
        engine.Graphics.ProfilerVisible = true;
        check(!engine.Graphics.ProfilerVisible, "the native F1 overlay stays off in the editor (its own ImGui context let clicks through)");
        engine.Graphics.ProfilerVisible = false;

        // ---------- 窓を全部開く : 履歴と CPU の区間 ----------
        windows.ShowOverview = windows.ShowCpu = windows.ShowGpu = windows.ShowRendering = windows.ShowMemory = true;
        windows.Paused = false;
        for (int i = 0; i < 5; i++) yield return 0;
        check(Profiler.Enabled, "CPU samples are recorded while CPU Usage is open");
        var names = windows.LatestSampleNames;
        check(windows.HistoryCount >= 3 && names.Contains("Update (game systems)") && names.Contains("Physics (FixedUpdate + step)") &&
              names.Contains("Editor GUI (LateUpdate hook)") && names.Any(n => n.StartsWith("Render (native")),
              $"frames are recorded with engine stage samples ({windows.HistoryCount} frames, {names.Length} samples)");
        var samples = Profiler.LastFrameSamples.ToArray();
        check(samples.All(s => s.Milliseconds >= 0.0 && s.StartMs >= 0.0) && samples.Where(s => s.Depth == 0).Sum(s => s.Milliseconds) <= Profiler.LastFrameMilliseconds + 0.01,
              $"samples are inside the frame (top level {samples.Where(s => s.Depth == 0).Sum(s => s.Milliseconds):F3} ms <= frame {Profiler.LastFrameMilliseconds:F3} ms)");

        // ゲームの System は再生中だけ。System ごとに入れ子で出る / ゲームのコードの区間
        _app.TogglePlay();
        for (int i = 0; i < 4; i++) yield return 0;
        samples = Profiler.LastFrameSamples.ToArray();
        var update = Array.FindIndex(samples, s => s.Name == "Update (game systems)");
        check(update >= 0 && update + 1 < samples.Length && samples[update + 1].Depth == samples[update].Depth + 1,
              $"each game system gets its own nested sample while playing ({(update >= 0 && update + 1 < samples.Length ? samples[update + 1].Name : "-")})");
        _app.TogglePlay();
        yield return 0;

        // 手で区間を作る (入れ子 / 閉じ忘れはフレームの終わりで閉じる)
        Profiler.BeginSample("Test outer");
        using (Profiler.Sample("Test inner")) { Thread.SpinWait(2000); }
        Profiler.BeginSample("Test unclosed");
        Profiler.EndSample();
        Profiler.EndSample();
        Profiler.BeginSample("Test left open");
        yield return 0;
        samples = Profiler.LastFrameSamples.ToArray();
        var outer = Array.FindIndex(samples, s => s.Name == "Test outer");
        var inner = Array.FindIndex(samples, s => s.Name == "Test inner");
        var leftOpen = Array.FindIndex(samples, s => s.Name == "Test left open");
        check(outer >= 0 && inner == outer + 1 && samples[inner].Depth == samples[outer].Depth + 1 &&
              samples[outer].Milliseconds >= samples[inner].Milliseconds && leftOpen >= 0 && samples[leftOpen].Milliseconds > 0.0,
              "BeginSample / EndSample / using Sample nest, and a sample left open is closed at the end of the frame");

        // 重さ (目安。Console に出すだけ) : 記録している間の 1 区間 (BeginSample + EndSample)。区間の数の上限 (2048) を超えないように 1000 回
        {
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 0; i < 1000; i++) { Profiler.BeginSample("Perf"); Profiler.EndSample(); }
            var ns = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds * 1e6 / 1000;
            Console.WriteLine($"[Perf] Profiler sample while recording : {ns:F1} ns per BeginSample + EndSample");
        }
        yield return 0;

        // 一時停止 : 記録が止まる
        windows.Paused = true;
        var recorded = windows.RecordedFrames;   // 履歴の数は一杯 (300) で止まるので、記録した数で見る
        for (int i = 0; i < 3; i++) yield return 0;
        check(windows.RecordedFrames == recorded && !Profiler.Enabled, "Pause stops recording (and CPU sampling)");
        windows.Paused = false;
        for (int i = 0; i < 2; i++) yield return 0;
        check(windows.RecordedFrames > recorded, "recording resumes after Pause");

        // ---------- 描画パス (v1.10 P2) ----------
        // GPU Usage が開いている間はパスごとに GPU の時間を計る (数フレーム遅れ)
        check(Profiler.PassTimingEnabled, "pass GPU timing is on while GPU Usage is open");
        for (int i = 0; i < 4; i++) yield return 0;
        var passes = new List<PassTimingSample>();
        Profiler.ReadPassTimings(passes);
        var passNames = passes.Select(p => p.Name).ToList();
        var camera0 = passes.FindIndex(p => p.Name == "Camera 0");
        var opaque  = passes.FindIndex(p => p.Name == "Opaque");
        check(camera0 >= 0 && opaque > camera0 && passes[opaque].Depth == passes[camera0].Depth + 1 &&
              passNames.Contains("Clear + Sky") && passNames.Contains("Transparent") && passNames.Contains("Tonemap") &&
              passNames.Contains("UI (ImGui / game UI)") && passNames.Contains("Skinning (compute)"),
              $"render passes are reported in order with nesting ({string.Join(", ", passNames.Take(12))}{(passNames.Count > 12 ? ", ..." : "")})");
        var gpuFrame = Profiler.GetFrame(out var passFrame) ? passFrame : default;
        var topGpu = passes.Where(p => p.Depth == 0 && p.HasGpu).Sum(p => p.GpuMilliseconds);
        check(passes.All(p => p.HasGpu && p.GpuMilliseconds >= 0.0 && p.CpuMilliseconds >= 0.0) && passes[opaque].GpuMilliseconds > 0.0 &&
              topGpu <= gpuFrame.GpuTotalMs * 1.5 + 0.05,
              $"each pass has GPU / CPU time (opaque {passes[opaque].GpuMilliseconds:F3} ms, top-level sum {topGpu:F3} ms, frame GPU {gpuFrame.GpuTotalMs:F3} ms)");
        var shadows = passes.FindIndex(p => p.Name == "Shadows");
        check(shadows < 0 || (shadows + 1 < passes.Count && passes[shadows + 1].Name.StartsWith("Cascade") && passes[shadows + 1].Depth == passes[shadows].Depth + 1),
              shadows < 0 ? "no shadow passes in this scene (skipped)" : "shadow cascades are nested under Shadows");

        // Deferred のパス (メインのカメラを Deferred にする)
        var deferredCamera = _app.CreateCamera();
        {
            ref var c = ref World.Get<Camera>(deferredCamera);
            c.Depth = -100;
            c.RenderPath = RenderPath.Deferred;
        }
        for (int i = 0; i < 6; i++) yield return 0;
        Profiler.ReadPassTimings(passes);
        passNames = passes.Select(p => p.Name).ToList();
        check(passNames.Contains("GBuffer") && passNames.Contains("Deferred Lighting") && passNames.Contains("Forward (custom + transparent)"),
              $"Deferred reports its passes ({string.Join(", ", passNames.Take(10))})");
        World.DestroyEntity(deferredCamera);
        for (int i = 0; i < 3; i++) yield return 0;

        // レンダーターゲットの名前
        var named = GameEngine.Rendering.RenderTexture.Create(GameEngine.Rendering.RenderTextureFormat.RGBA8, 0.25f, name: "Profiler Test RT");
        var nameOk = named.Name == "Profiler Test RT";
        named.Name = "Profiler Test RT (renamed)";
        check(nameOk && named.Name == "Profiler Test RT (renamed)" && GameEngine.Rendering.RenderTexture.BackBuffer.Name == "BackBuffer",
              "RenderTexture.Create(name:) / Name (also sent to D3D12 SetName)");
        named.Dispose();

        // GPU Usage を閉じると GPU の時間は計らない (-1)。CPU の時間は出る
        windows.ShowGpu = false;
        for (int i = 0; i < 5; i++) yield return 0;
        Profiler.ReadPassTimings(passes);
        check(!Profiler.PassTimingEnabled && passes.Count > 0 && passes.All(p => !p.HasGpu) && passes.Any(p => p.CpuMilliseconds > 0.0),
              $"closing GPU Usage stops pass timestamps (GPU -1, CPU still measured; {passes.Count} passes)");

        // 重さ (目安。Console に出すだけ) : パスのタイムスタンプを書く / 書かないで、GPU の合計とシーンの記録 (C++) の平均を比べる
        {
            var sums = new double[4];
            for (int round = 0; round < 4; round++)
            {
                var on = round % 2 == 1;   // 切 → 入 → 切 → 入
                windows.ShowGpu = on;
                for (int i = 0; i < 10; i++) yield return 0;   // 切り替えが効くまで (数フレーム遅れ)
                double gpu = 0, record = 0;
                int n = 0;
                for (int i = 0; i < 300; i++)
                {
                    yield return 0;
                    if (Profiler.GetFrame(out var pf) && pf.GpuValid) { gpu += pf.GpuTotalMs; record += pf.SceneRecordMs; n++; }
                }
                Console.WriteLine($"[Perf] pass timestamps {(on ? "ON " : "OFF")} : GPU total {gpu / Math.Max(n, 1):F3} ms, record scene {record / Math.Max(n, 1):F3} ms ({n} frames)");
            }
            windows.ShowGpu = false;
            yield return 0;
        }

        // ---------- Render Graph Viewer (v1.12 P3) ----------
        foreach (var step in renderGraphChecks()) yield return step;
        // ---------- Frame Debugger (v1.13 P4) ----------
        foreach (var step in frameDebuggerChecks()) yield return step;

        // 計測の窓を閉じる (CPU Usage を閉じると区間も測らない)
        windows.ShowCpu = windows.ShowGpu = windows.ShowRendering = windows.ShowMemory = false;
        windows.ShowOverview = false;
        _app.CloseToolWindowsForTest();   // 前の確認が開いたままの窓 (Script Reference / Shader Graph / Widget Designer) もシーンの上に浮いていることがある
        yield return 0;
        check(!Profiler.Enabled, "CPU sampling stops when CPU Usage is closed");
        {
            var t0 = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 0; i < 1000000; i++) { Profiler.BeginSample("Perf"); Profiler.EndSample(); }
            var ns = System.Diagnostics.Stopwatch.GetElapsedTime(t0).TotalMilliseconds * 1e6 / 1000000;
            Console.WriteLine($"[Perf] Profiler sample while not recording : {ns:F2} ns per BeginSample + EndSample");
        }

        // ---------- クリックがすり抜けない ----------
        // シーンビューの真ん中に Profiler の窓を重ねて、その上をクリックする → シーンは反応しない (選んだ物も変わらない)
        // 先に窓を全部閉じて (imgui.ini に残った浮いた窓の位置がシーンを覆うことがある)、シーンビューを前に出す
        // (前の確認の窓がタブで重なっていることがある)。その絵の真ん中にマウスを置く
        ImGuiNET.ImGui.SetWindowFocus("Scene");
        yield return 0;
        yield return 0;
        var center = _app.SceneImageRect.Position + _app.SceneImageRect.Size * 0.5f;
        Input.Simulate(center, MouseButtons.None);
        yield return 0;
        yield return 0;
        var sceneUnder = _app.SceneHovered;
        windows.ShowOverview = true;
        windows.TestPlacements["Profiler"] = (center - new Vector2(200, 150), new Vector2(400, 300));
        yield return 0;
        yield return 0;
        var selected = _app.Selected;
        Input.Simulate(center, MouseButtons.None);
        yield return 0;
        Input.Simulate(center, MouseButtons.Left);
        yield return 0;
        var hoveredWhileDown = windows.OverviewHovered;
        var sceneWhileDown = _app.SceneHovered;
        Input.Simulate(center, MouseButtons.None);
        yield return 0;
        yield return 0;
        check(sceneUnder && hoveredWhileDown && !sceneWhileDown && _app.Selected == selected,
              $"a click on the Profiler window does not reach the Scene view (scene under it {sceneUnder}, window hovered {hoveredWhileDown}, scene hovered {sceneWhileDown})");
    }
}

// 見た目の確認 (--editor-test-section fd-view) : Frame Debugger を開いて Opaque の途中で止め、しばらく待つ (v1.13 P4)
internal sealed partial class EditorTest
{
    private IEnumerable<int> frameDebuggerView()
    {
        var (width, height) = _app.Engine.ClientSize;
        var fd = _app.FrameDebuggerWindow;
        fd.Show = true;
        fd.TestPlacement = (new Vector2(0, 30), new Vector2(width, height - 30));
        fd.SetEnabled(true);
        for (int i = 0; i < 40 && fd.WaitingForList; i++) yield return 0;
        if (fd.List is { } list)
        {
            var opaque = Array.FindIndex(list.Passes, p => p.Name == "Opaque");
            var draws = list.Events.Select((e, i) => (e, i)).Where(x => x.e.Pass == opaque && x.e.Type == FrameDebugEventType.SceneDraw).Select(x => x.i).ToArray();
            if (draws.Length > 0) fd.Selected = draws[draws.Length / 2];
        }
        var until = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency * 10;
        while (System.Diagnostics.Stopwatch.GetTimestamp() < until) yield return 0;
        fd.SetEnabled(false);
        fd.Show = false;
        yield return 0;
    }
}

// 見た目の確認 (--editor-test-section profiler-view) : 窓を全部並べて再生し、しばらく待つ (この間に窓を撮る)
internal sealed partial class EditorTest
{
    private IEnumerable<int> profilerView()
    {
        var windows = _app.ProfilerWindows;
        var (width, height) = _app.Engine.ClientSize;
        windows.ShowOverview = windows.ShowCpu = windows.ShowGpu = windows.ShowRendering = windows.ShowMemory = true;
        float w = width / 3.0f, h = (height - 40) / 2.0f;
        windows.TestPlacements["CPU Usage"]       = (new Vector2(0, 30), new Vector2(w, h));
        windows.TestPlacements["GPU Usage"]       = (new Vector2(w, 30), new Vector2(w, h));
        windows.TestPlacements["Rendering Stats"] = (new Vector2(w * 2, 30), new Vector2(w, h));
        windows.TestPlacements["Memory"]          = (new Vector2(0, 30 + h), new Vector2(w, h));
        windows.TestPlacements["Profiler"]        = (new Vector2(w, 30 + h), new Vector2(w, h));
        // v1.12 P3 : Render Graph Viewer は下の段を広く使う (Memory / Profiler の上に重ねる)。HDR を選んで中身も出す
        var viewer = _app.RenderGraphViewer;
        viewer.Show = true;
        viewer.TestPlacement = (new Vector2(0, 30 + h * 0.6f), new Vector2(width, height - 30 - h * 0.6f));
        for (int i = 0; i < 3; i++) yield return 0;
        foreach (var r in viewer.Snapshot?.Resources ?? []) if (r.Name == "HDR (Forward)") viewer.SelectedResource = r.Id;
        _app.TogglePlay();
        var until = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency * 10;   // 10 秒
        while (System.Diagnostics.Stopwatch.GetTimestamp() < until) yield return 0;
        _app.TogglePlay();
        yield return 0;
    }
}

// ==========================================================================
// v1.12 P3 : Render Graph Viewer (パスごとのリソースの読み書き / バリア) と Memory のレンダーターゲットの一覧
// ==========================================================================
internal sealed partial class EditorTest
{
    private IEnumerable<int> renderGraphChecks()
    {
        var viewer = _app.RenderGraphViewer;
        var windows = _app.ProfilerWindows;
        check(RenderGraphCapture.NativeSizes == (16, 48, 20), $"render graph structs match C++ (pass / resource / access = {RenderGraphCapture.NativeSizes})");

        viewer.Show = true;
        viewer.Live = true;
        for (int i = 0; i < 4; i++) yield return 0;
        var s = viewer.Snapshot;
        check(RenderGraphCapture.Capture && s != null && s.Passes.Length > 5 && s.Accesses.Length > 10,
              $"opening the viewer captures a frame ({s?.Passes.Length} passes, {s?.Resources.Length} resources, {s?.Accesses.Length} accesses)");
        if (s == null) { viewer.Show = false; yield break; }

        int passOf(string name) => Array.FindIndex(s.Passes, p => p.Name == name);
        RenderGraphResourceInfo? resource(string name) { foreach (var r in s.Resources) if (r.Name == name) return r; return null; }
        RenderGraphAccessKind kindOf(int pass, uint id)
        {
            var kind = RenderGraphAccessKind.None;
            foreach (var a in s.Accesses) if (a.Pass == pass && a.ResourceId == id && !a.IsBarrier) kind |= a.Kind;
            return kind;
        }
        var hdr   = resource("HDR (Forward)");
        var ldr   = resource("LDR (FXAA input)");
        var view  = resource("Scene View (editor)");
        var depth = resource("SceneDepth");
        check(hdr != null && ldr != null && view is { IsImported: true } && depth is { IsDepth: true, IsImported: true } && resource("BackBuffer") != null,
              $"resources are named (HDR / LDR / Scene View (imported) / SceneDepth / BackBuffer; {string.Join(", ", s.Resources.Where(r => r.IsUsed).Select(r => r.Name))})");
        if (hdr == null || ldr == null || view == null || depth == null) { viewer.Show = false; yield break; }

        var opaque = passOf("Opaque");
        var tonemap = passOf("Tonemap");
        var fxaa = passOf("FXAA");
        check(opaque >= 0 && (kindOf(opaque, hdr.Value.Id) & RenderGraphAccessKind.Write) != 0 &&
              (kindOf(opaque, depth.Value.Id) & RenderGraphAccessKind.DepthWrite) != 0,
              $"Opaque writes HDR and depth ({kindOf(opaque, hdr.Value.Id)} / {kindOf(opaque, depth.Value.Id)})");
        check(tonemap >= 0 && fxaa >= 0 && (kindOf(tonemap, hdr.Value.Id) & RenderGraphAccessKind.Read) != 0 && (kindOf(tonemap, ldr.Value.Id) & RenderGraphAccessKind.Write) != 0 &&
              (kindOf(fxaa, ldr.Value.Id) & RenderGraphAccessKind.Read) != 0 && (kindOf(fxaa, view.Value.Id) & RenderGraphAccessKind.Write) != 0,
              "Tonemap reads HDR -> writes LDR, FXAA reads LDR -> writes the Scene View (back buffer in the editor)");
        var hdrBarrier = s.Accesses.Any(a => a.IsBarrier && a.ResourceId == hdr.Value.Id && a.Pass == tonemap && a.StateBefore == 0x4u && (a.StateAfter & 0x80u) != 0);
        check(hdrBarrier, "the barrier HDR RENDER_TARGET -> PIXEL_SHADER_RESOURCE is recorded in the Tonemap pass");
        var shadow = resource("Shadow Map");
        check(shadow == null || (kindOf(opaque, shadow.Value.Id) & (RenderGraphAccessKind.Read | RenderGraphAccessKind.Implicit)) == (RenderGraphAccessKind.Read | RenderGraphAccessKind.Implicit),
              shadow == null ? "no shadow map in this scene (skipped)" : "Opaque reads the shadow map implicitly (not written in the command)");
        check(s.Accesses.All(a => a.Pass >= 0) && s.Passes.Any(p => p.Name == "Finish (debug lines)"),
              $"every access belongs to a pass ({s.Accesses.Count(a => a.Pass < 0)} outside)");
        check(viewer.ColumnCount > 5 && viewer.RowCount > 3, $"the viewer builds its grid ({viewer.ColumnCount} pass columns x {viewer.RowCount} resource rows, {viewer.Warnings.Count} warnings)");
        check(s.Resources.First(r => r.Name == "HDR (Forward)").Bytes >= 1024L * 1024, $"render target memory comes from D3D12 ({ProfilerWindows.Bytes(hdr.Value.Bytes)} for HDR)");

        // 中身の絵 (選んだリソースを UI の前に読める状態にする)。数フレーム描いて止まらない
        viewer.SelectedResource = hdr.Value.Id;
        for (int i = 0; i < 3; i++) yield return 0;
        check(hdr.Value.CanPreview && !depth.Value.CanPreview, "a color render target can be previewed (depth cannot)");

        // Live を切ると同じ記録のまま
        viewer.Live = false;
        var frozen = viewer.Snapshot;
        for (int i = 0; i < 3; i++) yield return 0;
        check(ReferenceEquals(frozen, viewer.Snapshot), "Live off keeps the captured frame");
        viewer.Live = true;

        // Live でもチラつかない : 影の遠い段を間引くので、記録の形はフレームごとに変わる (17 / 18 パス) が、
        // 表は間隔ごとに、一番多くのパスを含むフレームで、形が変わった時だけ差し替える
        {
            var shapes = new HashSet<int>();
            for (int i = 0; i < 8; i++) { yield return 0; if (RenderGraphCapture.Read() is { } f) shapes.Add(f.Passes.Length); }
            var replaced = viewer.ReplaceCount;
            var passCounts = new HashSet<int>();
            var until = System.Diagnostics.Stopwatch.GetTimestamp() + System.Diagnostics.Stopwatch.Frequency * 2;   // 2 秒 (0.5 秒の間隔が 4 回)
            while (System.Diagnostics.Stopwatch.GetTimestamp() < until)
            {
                yield return 0;
                if (viewer.Snapshot is { } shown) passCounts.Add(shown.Passes.Length);
            }
            check(viewer.ReplaceCount - replaced <= 1 && passCounts.Count == 1 && passCounts.First() == shapes.Max(),
                  $"Live does not flicker : the frames alternate between {string.Join(" / ", shapes.Order())} passes, the graph keeps {string.Join(" / ", passCounts)} " +
                  $"(replaced {viewer.ReplaceCount - replaced} times in 2 s)");
        }

        // MSAA : 解決 (Resolve) と MSAA の深度
        var msaaCamera = _app.CreateCamera();
        {
            ref var c = ref World.Get<Camera>(msaaCamera);
            c.Depth = -100;
            c.AntiAliasing = GameEngine.Rendering.AntiAliasing.Msaa4x;
        }
        for (int i = 0; i < 6; i++) yield return 0;
        viewer.UpdateNow();   // 間隔を待たずに差し替える (Live は 0.5 秒ごと)
        for (int i = 0; i < 2; i++) yield return 0;
        s = viewer.Snapshot!;
        var msaaHdr = s.Resources.FirstOrDefault(r => r.Name == "HDR (Forward)");
        var resolved = s.Accesses.Any(a => a.ResourceId == msaaHdr.Id && (a.Kind & RenderGraphAccessKind.Resolve) != 0);
        var msaaDepth = s.Resources.Any(r => r.Id == RenderGraphCapture.MsaaDepthId && r.IsUsed);
        check(msaaHdr.Samples > 1 && resolved && msaaDepth,
              $"MSAA : HDR is resolved before it is read, and the MSAA depth is used (samples {msaaHdr.Samples}, resolve {resolved}, MSAA depth {msaaDepth})");
        World.DestroyEntity(msaaCamera);
        for (int i = 0; i < 3; i++) yield return 0;

        // Memory の窓 : レンダーターゲットの一覧 (Render Graph Viewer を閉じても Memory が開いていれば記録する)
        viewer.Show = false;
        windows.ShowMemory = true;
        for (int i = 0; i < 3; i++) yield return 0;
        check(RenderGraphCapture.Capture && viewer.Snapshot != null && viewer.Snapshot.Resources.Sum(r => r.Bytes) > 0,
              $"Memory lists render target memory ({ProfilerWindows.Bytes(viewer.Snapshot?.Resources.Sum(r => r.Bytes) ?? 0)} total)");
        windows.ShowMemory = false;
        yield return 0;
        check(!RenderGraphCapture.Capture, "capture stops when the viewer and Memory are closed");

        // 重さ (目安) : 記録する / しないで、記録 (C++) の時間を比べる
        {
            for (int round = 0; round < 4; round++)
            {
                var on = round % 2 == 1;
                viewer.Show = on;
                for (int i = 0; i < 10; i++) yield return 0;
                double record = 0; int n = 0;
                for (int i = 0; i < 300; i++)
                {
                    yield return 0;
                    if (Profiler.GetFrame(out var pf)) { record += pf.SceneRecordMs; n++; }
                }
                Console.WriteLine($"[Perf] render graph capture {(on ? "ON " : "OFF")} : record scene {record / Math.Max(n, 1):F3} ms ({n} frames)");
            }
            viewer.Show = false;
            yield return 0;
        }
    }
}

// ==========================================================================
// v1.13 P4 : Frame Debugger (イベントごとに止めて、その時の描画先を写す)
// ==========================================================================
internal sealed partial class EditorTest
{
    private IEnumerable<int> waitFrameDebugger(FrameDebuggerWindow fd, int index)
    {
        // 止める所を変えてから、その所で止めたフレームの結果が読めるまで (C++ の結果は 1〜2 フレーム遅れる)
        fd.Selected = index;
        for (int i = 0; i < 30; i++)
        {
            yield return 0;
            if (fd.Current is { } c && c.Result.Stopped && c.Events.Length == index + 1) break;
        }
    }

    private IEnumerable<int> frameDebuggerChecks()
    {
        var fd = _app.FrameDebuggerWindow;
        check(FrameDebugger.NativeSizes == (32, 144, 32), $"frame debugger structs match C++ (settings / event / result = {FrameDebugger.NativeSizes})");

        // 再生中に Enable すると一時停止する
        _app.TogglePlay();
        for (int i = 0; i < 3; i++) yield return 0;
        fd.Show = true;
        fd.SetEnabled(true);
        check(_app.IsPlaying && _app.IsPaused, "enabling the Frame Debugger pauses the game");
        for (int i = 0; i < 40 && fd.WaitingForList; i++) yield return 0;
        var list = fd.List;
        check(list != null && list.Events.Length > 10 && !list.Result.Stopped,
              $"the full event list is collected from an unstopped frame ({list?.Events.Length} events)");
        if (list == null) { fd.SetEnabled(false); fd.Show = false; _app.TogglePlay(); yield break; }
        var types = list.Events.Select(e => e.Type).ToHashSet();
        check(types.Contains(FrameDebugEventType.ClearColor) && types.Contains(FrameDebugEventType.ClearDepth) && types.Contains(FrameDebugEventType.SceneDraw) &&
              types.Contains(FrameDebugEventType.Fullscreen),
              $"events : clear / scene draws / fullscreen ({string.Join(", ", types)})");
        Profiler.GetFrame(out var fdFrame);
        check(!fdFrame.IndirectDraw && !fdFrame.GpuCulling, "while enabled the scene is drawn one by one (no ExecuteIndirect / GPU culling)");

        // 最後のイベントで止める (既定)
        foreach (var step in waitFrameDebugger(fd, list.Events.Length - 1)) yield return step;
        check(fd.Current is { Result: { Stopped: true, Captured: true, Status: 0 } } && fd.Current.Events.Length == list.Events.Length,
              $"stopping at the last event captures its target ({fd.Current?.Result})");

        // Opaque の最初のシーンの描画 : 描画先は HDR、物はエンティティに結び付く
        int passIndex(string name) => Array.FindIndex(list.Passes, p => p.Name == name);
        var opaquePass = passIndex("Opaque");
        var firstOpaque = Array.FindIndex(list.Events, e => e.Pass == opaquePass && e.Type == FrameDebugEventType.SceneDraw);
        check(firstOpaque >= 0, $"the Opaque pass has scene draws (event {firstOpaque + 1})");
        if (firstOpaque < 0) { fd.SetEnabled(false); fd.Show = false; _app.TogglePlay(); yield break; }
        fd.Slot = 0;
        foreach (var step in waitFrameDebugger(fd, firstOpaque)) yield return step;
        var r = fd.Current?.Result;
        check(r is { Captured: true } && FrameDebugger.TargetName(r.Value.Source) == "HDR (Forward)" && r.Value.SourceFormat == "RGBA16F" && fd.Current!.Events.Length == firstOpaque + 1,
              $"stopping at the first opaque draw shows the HDR target ({(r == null ? "-" : FrameDebugger.TargetName(r.Value.Source))} {r?.SourceFormat} {r?.SourceWidth}x{r?.SourceHeight})");
        var mapped = list.Events.Where(e => e.Type == FrameDebugEventType.SceneDraw && e.View == 0)
                                .Select(e => FrameDebugger.FindEntity(World, e.Object)).Where(e => !e.IsNull).ToList();
        check(mapped.Count > 0 && mapped.All(e => World.Has<MeshRenderer>(e)),
              $"scene draws map back to entities with a MeshRenderer ({mapped.Count} draws, e.g. {(mapped.Count > 0 && World.Has<EntityInfo>(mapped[0]) ? World.Get<EntityInfo>(mapped[0]).Name : "-")})");

        // 深度 (Slot 4)
        fd.Slot = 4;
        foreach (var step in waitFrameDebugger(fd, firstOpaque)) yield return step;
        for (int i = 0; i < 3; i++) yield return 0;   // Slot の変更が効いたフレーム
        r = fd.Current?.Result;
        check(r is { Captured: true } && r.Value.SourceFormat == "D32F" && r.Value.Source == RenderGraphCapture.SceneDepthId,
              $"the depth slot shows the scene depth ({r?.SourceFormat}, status {r?.Status})");

        // 影の段 (深度の配列) は写せない
        var cascadePass = passIndex("Cascade 0");
        var shadowDraw = cascadePass < 0 ? -1 : Array.FindIndex(list.Events, e => e.Pass == cascadePass && e.Type == FrameDebugEventType.SceneDraw);
        if (shadowDraw >= 0)
        {
            foreach (var step in waitFrameDebugger(fd, shadowDraw)) yield return step;
            for (int i = 0; i < 3; i++) yield return 0;
            check(fd.Current is { Result: { Captured: false, Status: 3 } }, $"the shadow map array is reported as not viewable (status {fd.Current?.Result.Status})");
        }
        else check(true, "no shadow cascades in this scene (skipped)");

        // 全画面 (Tonemap) : 描画先 LDR、シェーダーの名前
        fd.Slot = 0;
        var tonemap = Array.FindIndex(list.Events, e => e.Type == FrameDebugEventType.Fullscreen && e.Pass == passIndex("Tonemap"));
        check(tonemap >= 0 && FrameDebugger.FullscreenShaderName(list.Events[tonemap].Arg0) == "postProcess.shader : PSTonemap" &&
              FrameDebugger.TargetName(list.Events[tonemap].Colors[0]) == "LDR (FXAA input)" && FrameDebugger.TargetName(list.Events[tonemap].Inputs[0]) == "HDR (Forward)",
              $"the Tonemap event knows its shader, target and inputs ({(tonemap >= 0 ? FrameDebugger.FullscreenShaderName(list.Events[tonemap].Arg0) : "-")})");
        foreach (var step in waitFrameDebugger(fd, tonemap)) yield return step;
        check(fd.Current is { Result.Captured: true } && FrameDebugger.TargetName(fd.Current.Result.Source) == "LDR (FXAA input)", $"stopping at Tonemap shows the LDR target ({fd.Current?.Result}, events {fd.Current?.Events.Length}, tonemap {tonemap + 1}, last {(fd.Current is { Events.Length: > 0 } cc ? $"{FrameDebuggerWindow.Summary(cc.Events[^1])} colors [{string.Join(",", cc.Events[^1].Colors.Select(FrameDebugger.TargetName))}] pass {cc.Events[^1].Pass}" : "-")}, source name {(fd.Current == null ? "-" : FrameDebugger.TargetName(fd.Current.Result.Source))}, slot {fd.Slot})");

        // 一覧を取り直しても同じ (影の段の間引きを止めているので、フレームごとにイベントの並びが変わらない)
        fd.RequestList();
        for (int i = 0; i < 40 && fd.WaitingForList; i++) yield return 0;
        check(fd.List != null && fd.List.Events.Length == list.Events.Length && fd.List.Events.Select(e => e.Type).SequenceEqual(list.Events.Select(e => e.Type)),
              $"the event list is stable between frames ({fd.List?.Events.Length} / {list.Events.Length})");

        // 重さ (目安。Console に出すだけ) : Frame Debugger 切 / 入 (止めない) の記録 (C++) と GPU の合計
        for (int round = 0; round < 4; round++)
        {
            var on = round % 2 == 1;
            fd.SetEnabled(on);
            fd.Selected = -1;
            for (int i = 0; i < 12; i++) yield return 0;
            double record = 0, gpu = 0; int n = 0;
            for (int i = 0; i < 300; i++)
            {
                yield return 0;
                if (Profiler.GetFrame(out var pf) && pf.GpuValid) { record += pf.SceneRecordMs; gpu += pf.GpuTotalMs; n++; }
            }
            Console.WriteLine($"[Perf] frame debugger {(on ? "ON " : "OFF")} : record scene {record / Math.Max(n, 1):F3} ms, GPU {gpu / Math.Max(n, 1):F3} ms ({n} frames)");
        }

        // Disable : 再生を戻し、まとめて描く道に戻る
        fd.SetEnabled(false);
        for (int i = 0; i < 4; i++) yield return 0;
        Profiler.GetFrame(out var after);
        check(_app.IsPlaying && !_app.IsPaused && after.IndirectDraw && !FrameDebugger.Enabled,
              $"disabling resumes the game and ExecuteIndirect (indirect {after.IndirectDraw})");
        fd.Show = false;
        _app.TogglePlay();
        for (int i = 0; i < 3; i++) yield return 0;
    }
}
