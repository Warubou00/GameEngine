using System.Numerics;
using System.Runtime.InteropServices;
using GameEngine.Rendering;
using ImGuiNET;

namespace GameEngine.Editor;

// ==========================================================================
// ImGui をエンジンで使う土台 (ED3)
//   - 入力 : エンジンの Input (WinForms のイベント) から、マウス / キー / 打った文字を ImGui へ渡す
//   - 描画 : ImGui の頂点 / インデックス / 描画の命令を、まとめて Graphics.SubmitUI で C++ (RHI の UI のパス) へ渡す
//   - フォント : 日本語が出るように Windows のメイリオを使う (無ければ ImGui の既定の英字フォント)
//   - 窓の並び (docking) は imgui.ini に保存される (次に開いた時も同じ並び)
// ==========================================================================
internal sealed unsafe class ImGuiBackend : IDisposable
{
    private readonly Engine _engine;
    private UIVertex[]      _vertices = new UIVertex[1 << 14];
    private ushort[]        _indices  = new ushort[1 << 15];
    private UIDrawCommand[] _commands = new UIDrawCommand[256];
    private readonly IntPtr _iniPath;

    public ImGuiBackend(Engine engine, string iniPath)
    {
        _engine = engine;
        ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.ConfigFlags |= ImGuiConfigFlags.DockingEnable | ImGuiConfigFlags.NavEnableKeyboard;
        io.BackendFlags |= ImGuiBackendFlags.RendererHasVtxOffset;   // 頂点のずらし (VertexOffset) を C++ が使う → 65536 頂点を超えても uint16 のインデックスで描ける
        // ini の場所 (ImGui は文字列のポインタを持ち続けるので、解放しないメモリに置く)
        _iniPath = Marshal.StringToCoTaskMemUTF8(iniPath);
        io.NativePtr->IniFilename = (byte*)_iniPath;
        ImGui.StyleColorsDark();
        var style = ImGui.GetStyle();
        style.WindowRounding = 3.0f;
        style.FrameRounding  = 2.0f;
        buildFont(io);
    }

    private void buildFont(ImGuiIOPtr io)
    {
        // 英数字 : Segoe UI / 日本語 : メイリオ (ttc の 0 番) を重ねる (MergeMode。先に入れた字が優先)
        //   (以前はメイリオだけだったが、メイリオは \ を ¥ で描くのでパスが読みにくかった)
        //   漢字が多いので大きな画像になるが、作るのは起動時の 1 回だけ
        const string segoe  = @"C:\Windows\Fonts\segoeui.ttf";
        const string meiryo = @"C:\Windows\Fonts\meiryo.ttc";
        var loaded = false;
        if (File.Exists(segoe))
        {
            var config = newFontConfig(merge: false);
            io.Fonts.AddFontFromFileTTF(segoe, 17.0f, config, io.Fonts.GetGlyphRangesDefault());
            ImGuiNative.ImFontConfig_destroy(config);
            loaded = true;
        }
        if (File.Exists(meiryo))
        {
            var config = newFontConfig(merge: loaded);
            io.Fonts.AddFontFromFileTTF(meiryo, 17.0f, config, io.Fonts.GetGlyphRangesJapanese());
            ImGuiNative.ImFontConfig_destroy(config);
            loaded = true;
        }
        if (!loaded) io.Fonts.AddFontDefault();
        io.Fonts.GetTexDataAsRGBA32(out byte* pixels, out int width, out int height, out _);
        var id = _engine.Graphics.CreateUITexture(width, height, new ReadOnlySpan<byte>(pixels, width * height * 4));
        io.Fonts.SetTexID((IntPtr)id);
        io.Fonts.ClearTexData();   // CPU 側の画像はもう要らない
    }

    private static ImFontConfig* newFontConfig(bool merge)
    {
        var config = ImGuiNative.ImFontConfig_ImFontConfig();
        config->FontNo      = 0;
        config->OversampleH = 1;
        config->OversampleV = 1;
        config->PixelSnapH  = 1;
        config->MergeMode   = merge ? (byte)1 : (byte)0;
        return config;
    }

    // フレームの始め : 入力を渡して NewFrame
    public void NewFrame()
    {
        var io = ImGui.GetIO();
        var (width, height) = _engine.ClientSize;
        io.DisplaySize = new Vector2(width, height);
        io.DisplayFramebufferScale = Vector2.One;
        io.DeltaTime = Math.Max(Time.DeltaTime, 1e-4f);

        var mouse = Input.MousePosition;
        io.AddMousePosEvent(mouse.X, mouse.Y);
        io.AddMouseButtonEvent(0, Input.GetMouseButton(MouseButtons.Left));
        io.AddMouseButtonEvent(1, Input.GetMouseButton(MouseButtons.Right));
        io.AddMouseButtonEvent(2, Input.GetMouseButton(MouseButtons.Middle));
        if (Input.ScrollDelta != 0.0f) io.AddMouseWheelEvent(0.0f, Input.ScrollDelta);

        io.AddKeyEvent(ImGuiKey.ModCtrl,  Input.GetKey(Keys.ControlKey));
        io.AddKeyEvent(ImGuiKey.ModShift, Input.GetKey(Keys.ShiftKey));
        io.AddKeyEvent(ImGuiKey.ModAlt,   Input.GetKey(Keys.Menu));
        foreach (var (key, down) in Input.KeyEvents)
        {
            var imguiKey = toImGuiKey(key);
            if (imguiKey != ImGuiKey.None) io.AddKeyEvent(imguiKey, down);
        }
        foreach (var c in Input.TypedCharacters)
        {
            if (c >= 32 && c != 127) io.AddInputCharacter(c);   // 制御文字 (Backspace など) はキーで渡す
        }
        ImGui.NewFrame();
    }

    // フレームの終わり : ImGui の描画の内容を 1 つの配列にまとめて C++ へ
    public void Render()
    {
        ImGui.Render();
        var drawData = ImGui.GetDrawData();
        var vertexCount = drawData.TotalVtxCount;
        var indexCount  = drawData.TotalIdxCount;
        if (_vertices.Length < vertexCount) Array.Resize(ref _vertices, vertexCount * 2);
        if (_indices.Length < indexCount)   Array.Resize(ref _indices, indexCount * 2);

        var commandCount = 0;
        var vertexBase = 0;
        var indexBase  = 0;
        var displayPos = drawData.DisplayPos;
        for (int n = 0; n < drawData.CmdListsCount; n++)
        {
            var list = drawData.CmdLists[n];
            var vtx = list.VtxBuffer;
            var idx = list.IdxBuffer;
            fixed (UIVertex* v = &_vertices[vertexBase])
                Buffer.MemoryCopy((void*)vtx.Data, v, (long)(_vertices.Length - vertexBase) * sizeof(UIVertex), (long)vtx.Size * sizeof(UIVertex));
            fixed (ushort* i = &_indices[indexBase])
                Buffer.MemoryCopy((void*)idx.Data, i, (long)(_indices.Length - indexBase) * sizeof(ushort), (long)idx.Size * sizeof(ushort));

            for (int c = 0; c < list.CmdBuffer.Size; c++)
            {
                var cmd = list.CmdBuffer[c];
                if (cmd.UserCallback != IntPtr.Zero || cmd.ElemCount == 0) continue;
                if (commandCount == _commands.Length) Array.Resize(ref _commands, commandCount * 2);
                var clip = cmd.ClipRect;
                _commands[commandCount++] = new UIDrawCommand
                {
                    IndexCount   = cmd.ElemCount,
                    IndexOffset  = (uint)indexBase + cmd.IdxOffset,
                    VertexOffset = (uint)vertexBase + cmd.VtxOffset,
                    Texture      = (uint)(ulong)cmd.TextureId,
                    Clip         = new Vector4(clip.X - displayPos.X, clip.Y - displayPos.Y, clip.Z - displayPos.X, clip.W - displayPos.Y),
                };
            }
            vertexBase += vtx.Size;
            indexBase  += idx.Size;
        }
        _engine.Graphics.SubmitUI(_vertices.AsSpan(0, vertexBase), _indices.AsSpan(0, indexBase), _commands.AsSpan(0, commandCount));
    }

    private static ImGuiKey toImGuiKey(Keys key)
    {
        if (key >= Keys.A && key <= Keys.Z)     return ImGuiKey.A + (key - Keys.A);
        if (key >= Keys.D0 && key <= Keys.D9)   return ImGuiKey._0 + (key - Keys.D0);
        if (key >= Keys.F1 && key <= Keys.F12)  return ImGuiKey.F1 + (key - Keys.F1);
        return key switch
        {
            Keys.Tab => ImGuiKey.Tab, Keys.Left => ImGuiKey.LeftArrow, Keys.Right => ImGuiKey.RightArrow,
            Keys.Up => ImGuiKey.UpArrow, Keys.Down => ImGuiKey.DownArrow, Keys.PageUp => ImGuiKey.PageUp,
            Keys.PageDown => ImGuiKey.PageDown, Keys.Home => ImGuiKey.Home, Keys.End => ImGuiKey.End,
            Keys.Insert => ImGuiKey.Insert, Keys.Delete => ImGuiKey.Delete, Keys.Back => ImGuiKey.Backspace,
            Keys.Space => ImGuiKey.Space, Keys.Enter => ImGuiKey.Enter, Keys.Escape => ImGuiKey.Escape,
            Keys.ControlKey => ImGuiKey.LeftCtrl, Keys.ShiftKey => ImGuiKey.LeftShift, Keys.Menu => ImGuiKey.LeftAlt,
            _ => ImGuiKey.None,
        };
    }

    public void Dispose()
    {
        ImGui.DestroyContext();
        Marshal.FreeCoTaskMem(_iniPath);
    }
}

// ==========================================================================
// 窓の並びを組む (ImGui の DockBuilder。ImGui.NET には無いので cimgui を直接呼ぶ)
// ==========================================================================
internal static unsafe class DockBuilder
{
    private const string Lib = "cimgui";
    public const int DockSpaceFlag = 1 << 10;   // ImGuiDockNodeFlags_DockSpace (内部の値)

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint igDockBuilderAddNode(uint nodeId, int flags);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void igDockBuilderRemoveNode(uint nodeId);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void igDockBuilderSetNodeSize(uint nodeId, Vector2 size);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern uint igDockBuilderSplitNode(uint nodeId, ImGuiDir splitDir, float ratio, uint* outAtDir, uint* outOpposite);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void igDockBuilderDockWindow(byte* windowName, uint nodeId);
    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)] public static extern void igDockBuilderFinish(uint nodeId);

    public static void DockWindow(string name, uint node)
    {
        var bytes = System.Text.Encoding.UTF8.GetBytes(name + "\0");
        fixed (byte* p = bytes) igDockBuilderDockWindow(p, node);
    }

    public static (uint AtDir, uint Opposite) Split(uint node, ImGuiDir dir, float ratio)
    {
        uint a, b;
        igDockBuilderSplitNode(node, dir, ratio, &a, &b);
        return (a, b);
    }
}
