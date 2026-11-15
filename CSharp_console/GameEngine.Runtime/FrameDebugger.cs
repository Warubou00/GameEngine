using System.Numerics;
using System.Runtime.InteropServices;
using GameEngine.Native;
using GameEngine.Rendering;

namespace GameEngine;

// ==========================================================================
// Frame Debugger (v1.13 P4。Unity の Frame Debugger に当たる)
//   1 フレームの描画をイベント (消す / シーンの描画 1 回 / 全画面 / パーティクル / 自分のシェーダーの 1 個 …) に分け、
//   StopEvent のイベントまで描いて止め、その時の描画先 (色の何枚目か / 深度) を確認用の絵 (Output) に写す
//   - Enabled の間は ExecuteIndirect / GPU カリングを使わず 1 個ずつ描く (描画ごとに止めるため。少し重くなる)
//   - 毎フレーム同じフレームを描き直して止める (エディターは Frame Debugger の間ゲームを一時停止する)
//   - 止めた時のイベントの一覧は止めた所まで。全部の一覧は StopEvent = -1 (止めない) のフレームで取る
//   以前は描画の途中の絵を見る方法が無く、PIX / RenderDoc を使うしかなかった
// ==========================================================================

public enum FrameDebugEventType : uint
{
    ClearColor      = 1,
    ClearDepth      = 2,
    SceneDraw       = 3,   // DrawScene の 1 回の描画 (メッシュ × マテリアル。インスタンシングならまとめた数)
    Fullscreen      = 4,
    UpdateParticles = 5,
    DrawParticles   = 6,
    CustomDraw      = 7,   // 自分のシェーダー (SH2) の 1 個
    Preview         = 8,   // プレビューの窓 (AN5)
}

/// <summary>イベント 1 つ (Pass = Passes の番号、-1 はパスの外。Colors / Depth = その時の描画先)</summary>
public sealed record FrameDebugEventInfo(int Index, FrameDebugEventType Type, int Pass, uint[] Colors, uint Depth, uint Arg0, uint Arg1, uint View, uint Group,
                                         uint IndexCount, uint InstanceCount, uint Object, uint Material, uint[] Inputs, float[] Params, Vector4 Clear);

/// <summary>止めた時の結果 (Status : 0 = 写した / 1 = 止めていない / 2 = その描画先が無い / 3 = 写せない (画面 / 深度の配列))</summary>
public readonly record struct FrameDebugResult(int EventCount, bool Stopped, bool Captured, uint Source, int SourceWidth, int SourceHeight, string SourceFormat, int Status);

public sealed class FrameDebugSnapshot
{
    public FrameDebugEventInfo[] Events { get; init; } = [];
    public RenderGraphPassInfo[] Passes { get; init; } = [];
    public FrameDebugResult Result { get; init; }
}

public static unsafe class FrameDebugger
{
    [StructLayout(LayoutKind.Sequential)]
    private struct NativeSettings
    {
        public uint Enabled, StopEvent, ViewShader, OutputTarget, Slot, ChannelMask;
        public float LevelMin, LevelMax;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeEvent
    {
        public uint Type, Pass, ColorCount;
        public fixed uint Colors[4];
        public uint Depth, Arg0, Arg1, View, Group, IndexCount, InstanceCount, Object, Material;
        public fixed uint Inputs[8];
        public fixed float Params[8];
        public fixed float Clear[4];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeResult { public uint EventCount, Stopped, Captured, Source, SourceWidth, SourceHeight, SourceFormat, Status; }

    [StructLayout(LayoutKind.Sequential)] private struct NativePass { public uint NameId, Depth, Parent, Flags; }

    [DllImport("Renderer.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Renderer_SetFrameDebugger(NativeSettings* settings);
    [DllImport("Renderer.dll", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Renderer_GetFrameDebugEvents(NativeEvent* events, uint eventCapacity, uint* eventCount,
                                                            NativePass* passes, uint passCapacity, uint* passCount, NativeResult* result);
    [DllImport("Renderer.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint Renderer_FindRenderObjectByRhiObject(uint rhiObject);

    /// <summary>確認用 : C++ と同じ大きさか (32 / 144 / 32)</summary>
    public static (int Settings, int Event, int Result) NativeSizes => (sizeof(NativeSettings), sizeof(NativeEvent), sizeof(NativeResult));

    public const string ShaderPath = "../Shader/frameDebug.shader";

    /// <summary>動かすか (Apply で C++ に送る)</summary>
    public static bool Enabled { get; set; }
    /// <summary>このイベントまで描いて止める (-1 = 止めない : 全部描いて、全部のイベントを数える)</summary>
    public static int StopEvent { get; set; } = -1;
    /// <summary>写す描画先 : 0〜3 = 色の何枚目 (MRT) / 4 = 深度</summary>
    public static int Slot { get; set; }
    /// <summary>出すチャンネル (bit0 R, bit1 G, bit2 B, bit3 A。1 つだけなら白黒)</summary>
    public static uint ChannelMask { get; set; } = 0x7;
    /// <summary>Levels : 下〜上を 0〜1 に広げる (HDR の明るい所 / 深度の差を見る)</summary>
    public static float LevelMin { get; set; }
    public static float LevelMax { get; set; } = 1.0f;

    private static RenderTexture? _output;
    private static FullscreenShader? _viewShader;

    /// <summary>写した絵 (RGBA8。画面と同じ大きさ)。ImGui.Image に渡す番号 (まだ無ければ 0xFFFFFFFF)</summary>
    public static uint OutputTextureId { get; private set; } = 0xFFFFFFFFu;

    /// <summary>今の設定を C++ に送る (毎フレーム、描く前に呼ぶ。エディターの Frame Debugger の窓)</summary>
    public static void Apply()
    {
        var settings = new NativeSettings { Enabled = Enabled ? 1u : 0u, StopEvent = StopEvent < 0 ? 0xFFFFFFFFu : (uint)StopEvent,
                                            ViewShader = 0xFFFFFFFFu, OutputTarget = 0xFFFFFFFFu, Slot = (uint)Math.Clamp(Slot, 0, 4),
                                            ChannelMask = ChannelMask & 0xF, LevelMin = LevelMin, LevelMax = LevelMax };
        if (Enabled)
        {
            try
            {
                _viewShader ??= FullscreenShader.Load(ShaderPath, "PSFrameDebugView");
                _output ??= RenderTexture.Create(RenderTextureFormat.RGBA8, 1.0f, name: "Frame Debugger View");
                OutputTextureId = NativeMethods.Renderer_GetRenderTargetTexture(_output.Handle);
                settings.ViewShader   = _viewShader.Handle;
                settings.OutputTarget = _output.Handle;
            }
            catch (InvalidOperationException ex) { Console.WriteLine($"[FrameDebugger] {ex.Message}"); }
        }
        try { Renderer_SetFrameDebugger(&settings); } catch (EntryPointNotFoundException) { }
    }

    private static NativeEvent[] _events = new NativeEvent[256];
    private static NativePass[]  _passes = new NativePass[64];

    /// <summary>最後のフレームのイベント (止めた時は止めた所まで) / パス / 結果</summary>
    public static FrameDebugSnapshot? Read()
    {
        uint eventCount = 0, passCount = 0;
        NativeResult result = default;
        bool ok;
        try
        {
            ok = call(&eventCount, &passCount, &result);
            if (ok && (eventCount > _events.Length || passCount > _passes.Length))
            {
                if (eventCount > _events.Length) _events = new NativeEvent[eventCount * 2];
                if (passCount > _passes.Length)  _passes = new NativePass[passCount * 2];
                ok = call(&eventCount, &passCount, &result);
            }
        }
        catch (EntryPointNotFoundException) { return null; }
        if (!ok) return null;

        var passes = new RenderGraphPassInfo[passCount];
        for (int i = 0; i < passCount; i++)
        {
            var p = _passes[i];
            passes[i] = new RenderGraphPassInfo(i, Profiler.PassName(p.NameId), (int)p.Depth, p.Parent == 0xFFFFFFFFu ? -1 : (int)p.Parent);
        }
        var events = new FrameDebugEventInfo[eventCount];
        for (int i = 0; i < eventCount; i++)
        {
            ref var e = ref _events[i];
            var colors = new uint[Math.Min(e.ColorCount, 4u)];
            for (int c = 0; c < colors.Length; c++) colors[c] = e.Colors[c];
            var inputs = new uint[8];
            var parameters = new float[8];
            for (int k = 0; k < 8; k++) { inputs[k] = e.Inputs[k]; parameters[k] = e.Params[k]; }
            events[i] = new FrameDebugEventInfo(i, (FrameDebugEventType)e.Type, e.Pass == 0xFFFFFFFFu ? -1 : (int)e.Pass, colors, e.Depth, e.Arg0, e.Arg1, e.View, e.Group,
                                                e.IndexCount, e.InstanceCount, e.Object, e.Material, inputs, parameters, new Vector4(e.Clear[0], e.Clear[1], e.Clear[2], e.Clear[3]));
        }
        var r = new FrameDebugResult((int)result.EventCount, result.Stopped != 0, result.Captured != 0, result.Source, (int)result.SourceWidth, (int)result.SourceHeight,
                                     RenderGraphCapture.FormatName(result.SourceFormat), (int)result.Status);
        return new FrameDebugSnapshot { Events = events, Passes = passes, Result = r };
    }

    private static bool call(uint* eventCount, uint* passCount, NativeResult* result)
    {
        fixed (NativeEvent* e = _events)
        fixed (NativePass* p = _passes)
        {
            return Renderer_GetFrameDebugEvents(e, (uint)_events.Length, eventCount, p, (uint)_passes.Length, passCount, result);
        }
    }

    /// <summary>描画の物 (RHI の ObjectHandle) → エンティティ (MeshRenderer の物。見つからなければ Entity.Null)</summary>
    public static Entity FindEntity(World world, uint rhiObject)
    {
        if (rhiObject == 0xFFFFFFFFu) return Entity.Null;
        uint handle;
        try { handle = Renderer_FindRenderObjectByRhiObject(rhiObject); } catch (EntryPointNotFoundException) { return Entity.Null; }
        if (handle == 0xFFFFFFFFu) return Entity.Null;
        var pool = world.GetPool<MeshRenderer>();
        var renderers = pool.Components;
        var entities = pool.Entities;
        for (int i = 0; i < renderers.Length; i++)
            if (renderers[i].Created && renderers[i].Handle == handle) return entities[i];
        return Entity.Null;
    }

    /// <summary>全画面シェーダーの番号 → "ファイル : 入口" (FullscreenShader.Load で読んだ物。分からなければ番号)</summary>
    public static string FullscreenShaderName(uint handle) =>
        FullscreenShader.Loaded.TryGetValue(handle, out var shader) ? $"{Path.GetFileName(shader.Path)} : {shader.Entry}" : $"shader #{handle}";

    /// <summary>描画先の名前 (RenderTexture.Name。無ければ番号)</summary>
    public static string TargetName(uint id) => id == 0xFFFFFFFFu ? "-" : RenderGraphCapture.ResourceName(id);
}
