using System.Runtime.InteropServices;
using System.Text;
using GameEngine.Native;

namespace GameEngine;

// ==========================================================================
// Render Graph Viewer の記録 (v1.12 P3)
//   1 フレームの描画パス × リソース (レンダーターゲット / 画面 / シーンの深度 / テクスチャ) の
//   読み書き (Read / Write / Clear / 深度 / MSAA の解決) と遷移バリア (D3D12 の状態) を C++ が記録する
//   Capture = true の間だけ記録する (エディターの Render Graph Viewer / Memory の窓が開いている間)
//   以前はどのパスがどのレンダーターゲットをどの状態で使っているか、外から見る方法が無かった
// ==========================================================================

[Flags]
public enum RenderGraphResourceFlags : uint
{
    None     = 0,
    Depth    = 1u << 0,   // 深度
    Imported = 1u << 1,   // 外から来た物 (画面 / シーンの深度 / エディターのシーンの絵 / 普通のテクスチャ)
    Msaa     = 1u << 2,
    Used     = 1u << 3,   // このフレームで使った
    Texture  = 1u << 4,   // 普通のテクスチャ (全画面シェーダーの入力)
}

[Flags]
public enum RenderGraphAccessKind : uint
{
    None         = 0,
    Read         = 1u << 0,   // シェーダーが読む (SRV)
    Write        = 1u << 1,   // 描画先 (色) に描く
    Clear        = 1u << 2,   // 消す
    DepthWrite   = 1u << 3,   // 深度を書く
    DepthTest    = 1u << 4,   // 深度を比べるだけ (半透明)
    Resolve      = 1u << 5,   // MSAA → 1 サンプルの写し
    Barrier      = 1u << 6,   // 遷移バリア (StateBefore → StateAfter)
    Implicit     = 1u << 7,   // 命令には書いていないが読む (影のシャドーマップ)
    ResolvedCopy = 1u << 8,   // バリアが MSAA の写しの方に掛かった

    AnyWrite = Write | Clear | DepthWrite | Resolve,
    AnyRead  = Read | DepthTest,
}

/// <summary>描画パス (Index = この記録の中の番号。Parent = 親の番号、-1 = 一番外)</summary>
public readonly record struct RenderGraphPassInfo(int Index, string Name, int Depth, int Parent);

/// <summary>リソース (Id = レンダーターゲットの番号 / 特別な番号。TextureId = UI で見られる番号 (見られない物は 0xFFFFFFFF))</summary>
public readonly record struct RenderGraphResourceInfo(uint Id, string Name, string Format, int Width, int Height, int Samples, int ArraySize,
                                                      RenderGraphResourceFlags Flags, uint TextureId, long Bytes)
{
    public bool IsDepth    => (Flags & RenderGraphResourceFlags.Depth) != 0;
    public bool IsImported => (Flags & RenderGraphResourceFlags.Imported) != 0;
    public bool IsUsed     => (Flags & RenderGraphResourceFlags.Used) != 0;
    public bool CanPreview => TextureId != 0xFFFFFFFFu;
}

/// <summary>パスでのリソースの使い方 1 つ (Pass = -1 はパスの外)。バリアの時は StateBefore → StateAfter (D3D12_RESOURCE_STATES)</summary>
public readonly record struct RenderGraphAccessInfo(int Pass, uint ResourceId, RenderGraphAccessKind Kind, uint StateBefore, uint StateAfter)
{
    public bool IsBarrier => (Kind & RenderGraphAccessKind.Barrier) != 0;
}

/// <summary>1 フレーム分の記録</summary>
public sealed class RenderGraphSnapshot
{
    public RenderGraphPassInfo[]     Passes    { get; init; } = [];
    public RenderGraphResourceInfo[] Resources { get; init; } = [];
    public RenderGraphAccessInfo[]   Accesses  { get; init; } = [];
    public int FrameNumber { get; init; }
}

public static unsafe class RenderGraphCapture
{
    // C++ の RHI::RenderGraphPass / RenderGraphResource / RenderGraphAccess と同じ並び
    [StructLayout(LayoutKind.Sequential)] private struct NativePass { public uint NameId, Depth, Parent, Flags; }
    [StructLayout(LayoutKind.Sequential)] private struct NativeResource
    {
        public ulong Bytes;
        public uint Id, DxgiFormat, Width, Height, Samples, ArraySize, Flags, TextureId;
        private uint _padding0, _padding1;
    }
    [StructLayout(LayoutKind.Sequential)] private struct NativeAccess { public uint Pass, ResourceId, Kind, StateBefore, StateAfter; }

    [DllImport("Renderer.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Renderer_SetRenderGraphCapture([MarshalAs(UnmanagedType.I1)] bool enabled);
    [DllImport("Renderer.dll", CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Renderer_GetRenderGraph(NativePass* passes, uint passCapacity, uint* passCount,
                                                       NativeResource* resources, uint resourceCapacity, uint* resourceCount,
                                                       NativeAccess* accesses, uint accessCapacity, uint* accessCount);
    [DllImport("Renderer.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern uint Renderer_GetRenderTargetName(uint handle, byte* name, uint size);
    [DllImport("Renderer.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void Renderer_SetDebugReadTarget(uint handle);

    /// <summary>C++ の特別な番号</summary>
    public const uint BackBufferId = 0xFFFFFFFEu, SceneDepthId = 0xFFFFFFFDu, MsaaDepthId = 0xFFFFFFF0u, TextureInputBit = 0x40000000u;

    /// <summary>確認用 : C++ と同じ大きさか (16 / 48 / 20)</summary>
    public static (int Pass, int Resource, int Access) NativeSizes => (sizeof(NativePass), sizeof(NativeResource), sizeof(NativeAccess));

    private static bool _capture;
    private static NativePass[]     _passes    = new NativePass[64];
    private static NativeResource[] _resources = new NativeResource[64];
    private static NativeAccess[]   _accesses  = new NativeAccess[512];
    private static readonly byte[]  _text = new byte[256];
    private static int _frames;

    /// <summary>記録するか (true の間だけ C++ が毎フレーム記録する。変えた次のフレームから)</summary>
    public static bool Capture
    {
        get => _capture;
        set
        {
            if (_capture == value) return;
            _capture = value;
            try { Renderer_SetRenderGraphCapture(value); } catch (EntryPointNotFoundException) { }
        }
    }

    /// <summary>
    /// UI (ImGui) で中身を見るレンダーターゲット (毎フレーム UI の前に読める状態にする)。
    /// null / 見られない物で止める。RenderGraphResourceInfo.TextureId を ImGui.Image に渡す
    /// </summary>
    public static void SetPreviewTarget(RenderGraphResourceInfo? resource)
    {
        var id = resource is { CanPreview: true, IsImported: false } r && r.Id < TextureInputBit ? r.Id : 0xFFFFFFFFu;
        try { Renderer_SetDebugReadTarget(id); } catch (EntryPointNotFoundException) { }
    }

    /// <summary>最後に記録したフレームを読む (記録が無ければ null)</summary>
    public static RenderGraphSnapshot? Read()
    {
        uint passCount = 0, resourceCount = 0, accessCount = 0;
        bool ok;
        try
        {
            ok = call(ref passCount, ref resourceCount, ref accessCount);
            if (ok && (passCount > _passes.Length || resourceCount > _resources.Length || accessCount > _accesses.Length))
            {
                if (passCount > _passes.Length)         _passes    = new NativePass[passCount * 2];
                if (resourceCount > _resources.Length)  _resources = new NativeResource[resourceCount * 2];
                if (accessCount > _accesses.Length)     _accesses  = new NativeAccess[accessCount * 2];
                ok = call(ref passCount, ref resourceCount, ref accessCount);
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
        var resources = new RenderGraphResourceInfo[resourceCount];
        for (int i = 0; i < resourceCount; i++)
        {
            var r = _resources[i];
            resources[i] = new RenderGraphResourceInfo(r.Id, nameOf(r), FormatName(r.DxgiFormat), (int)r.Width, (int)r.Height, (int)Math.Max(1, r.Samples),
                                                       (int)Math.Max(1, r.ArraySize), (RenderGraphResourceFlags)r.Flags, r.TextureId, (long)r.Bytes);
        }
        var accesses = new RenderGraphAccessInfo[accessCount];
        for (int i = 0; i < accessCount; i++)
        {
            var a = _accesses[i];
            accesses[i] = new RenderGraphAccessInfo(a.Pass == 0xFFFFFFFFu ? -1 : (int)a.Pass, a.ResourceId, (RenderGraphAccessKind)a.Kind, a.StateBefore, a.StateAfter);
        }
        return new RenderGraphSnapshot { Passes = passes, Resources = resources, Accesses = accesses, FrameNumber = ++_frames };
    }

    private static bool call(ref uint passCount, ref uint resourceCount, ref uint accessCount)
    {
        fixed (NativePass* p = _passes)
        fixed (NativeResource* r = _resources)
        fixed (NativeAccess* a = _accesses)
        fixed (uint* pc = &passCount)
        fixed (uint* rc = &resourceCount)
        fixed (uint* ac = &accessCount)
        {
            return Renderer_GetRenderGraph(p, (uint)_passes.Length, pc, r, (uint)_resources.Length, rc, a, (uint)_accesses.Length, ac);
        }
    }

    private static string nameOf(in NativeResource r) => r.Id == MsaaDepthId ? $"SceneDepth (MSAA {r.Samples}x)" : ResourceName(r.Id);

    /// <summary>リソースの名前 (レンダーターゲットは RenderTexture.Name。v1.13 P4 : Frame Debugger も使う)</summary>
    public static string ResourceName(uint id)
    {
        if (id == BackBufferId) return "BackBuffer";
        if (id == SceneDepthId) return "SceneDepth";
        if (id == MsaaDepthId)  return "SceneDepth (MSAA)";
        if ((id & TextureInputBit) != 0 && id < 0x80000000u) return $"Texture #{id & ~TextureInputBit}";
        try { fixed (byte* t = _text) Renderer_GetRenderTargetName(id, t, (uint)_text.Length); }
        catch (EntryPointNotFoundException) { return $"RenderTarget {id}"; }
        var length = Array.IndexOf(_text, (byte)0);
        var name = Encoding.UTF8.GetString(_text, 0, length < 0 ? _text.Length : length);
        return name.Length > 0 ? name : $"RenderTarget {id}";
    }

    /// <summary>DXGI_FORMAT の名前 (エンジンが使う物だけ)</summary>
    public static string FormatName(uint dxgi) => dxgi switch
    {
        0  => "-",
        10 => "RGBA16F",
        24 => "RGB10A2",
        26 => "R11G11B10F",
        28 => "RGBA8",
        29 => "RGBA8_SRGB",
        34 => "RG16F",
        39 => "R32 (typeless)",
        40 => "D32F",
        41 => "R32F",
        54 => "R16F",
        61 => "R8",
        _  => $"DXGI {dxgi}",
    };

    /// <summary>D3D12_RESOURCE_STATES の名前 (例 : "RENDER_TARGET"、"NON_PIXEL_SHADER_RESOURCE | PIXEL_SHADER_RESOURCE")</summary>
    public static string StateName(uint state)
    {
        if (state == 0) return "COMMON / PRESENT";
        var names = new List<string>();
        void add(uint bit, string name) { if ((state & bit) == bit) { names.Add(name); state &= ~bit; } }
        add(0x1,    "VERTEX_AND_CONSTANT_BUFFER");
        add(0x2,    "INDEX_BUFFER");
        add(0x4,    "RENDER_TARGET");
        add(0x8,    "UNORDERED_ACCESS");
        add(0x10,   "DEPTH_WRITE");
        add(0x20,   "DEPTH_READ");
        add(0x40,   "NON_PIXEL_SHADER_RESOURCE");
        add(0x80,   "PIXEL_SHADER_RESOURCE");
        add(0x100,  "STREAM_OUT");
        add(0x200,  "INDIRECT_ARGUMENT");
        add(0x400,  "COPY_DEST");
        add(0x800,  "COPY_SOURCE");
        add(0x1000, "RESOLVE_DEST");
        add(0x2000, "RESOLVE_SOURCE");
        if (state != 0) names.Add($"0x{state:X}");
        return string.Join(" | ", names);
    }
}
