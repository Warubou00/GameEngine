using System.Numerics;
using System.Runtime.InteropServices;

namespace GameEngine.Native;

// ==========================================================================
// Renderer.dll (C++) の関数の宣言
//   C# から呼ぶ C++ の入口はここだけ (C++ 側は Renderer/Source/rendererDll.cpp)
//   - モデル・描画オブジェクトはハンドル (uint) でやり取りする。ポインタは受け取らない
//   - 1 フレームに何度も使う操作は配列でまとめて渡す (P/Invoke は 1 回 数十 ns かかるため)
//   - 構造体は blittable (int / float / IntPtr だけ) にして、コピー無しで渡す
//   エンジンの利用者は直接使わない (Engine / AssetManager / System 経由で使う)
// ==========================================================================
internal static unsafe class NativeMethods
{
    private const string Dll = "Renderer.dll";

    // C++ の Renderer::INVALID_HANDLE と同じ
    public const uint InvalidHandle = 0xFFFFFFFFu;

    // C++ の WindowInfo と同じ並び
    [StructLayout(LayoutKind.Sequential)]
    public struct WindowDesc
    {
        public IntPtr HWnd;
        public uint   Width;
        public uint   Height;
        public uint   EnableVSync;
    }

    // C++ の RHI::InitializeResult と同じ並び (1 = 成功)
    [StructLayout(LayoutKind.Sequential)]
    public struct InitializeResult
    {
        public int Device;
        public int CommandContext;
        public int Buffer;
        public int Texture;
        public int Pipeline;

        public readonly bool AllSucceeded =>
            Device == 1 && CommandContext == 1 && Buffer == 1 && Texture == 1 && Pipeline == 1;
    }

    // C++ の Renderer::CameraDesc と同じ並び (float ×12 + uint)。左手系 (+Z が奥)
    [StructLayout(LayoutKind.Sequential)]
    public struct CameraDesc : IEquatable<CameraDesc>
    {
        public Vector3 Position;
        public Vector3 Forward;
        public Vector3 Up;
        public float   FieldOfViewY;   // ラジアン
        public float   NearZ;
        public float   FarZ;
        public uint    Orthographic;

        public readonly bool Equals(CameraDesc o) =>
            Position == o.Position && Forward == o.Forward && Up == o.Up &&
            FieldOfViewY == o.FieldOfViewY && NearZ == o.NearZ && FarZ == o.FarZ && Orthographic == o.Orthographic;
        public override readonly bool Equals(object? obj) => obj is CameraDesc o && Equals(o);
        public override readonly int GetHashCode() => HashCode.Combine(Position, Forward, Up, FieldOfViewY, NearZ, FarZ, Orthographic);
    }

    // ---------- 描画パス (C++ の RHI::RenderTargetDesc / RenderCommand と同じ並び) ----------
    public const int MaxPassColorTargets = 4;
    public const int MaxPassInputs       = 8;
    public const int MaxPassParams       = 8;

    [StructLayout(LayoutKind.Sequential)]
    public struct RenderTargetDesc
    {
        public uint    Format;       // RenderTextureFormat
        public float   Scale;        // 画面に対する倍率 (0 なら Width / Height)
        public uint    Width;
        public uint    Height;
        public Vector4 ClearColor;
    }

    // 120byte (C++ 側は static_assert で確かめている)
    [StructLayout(LayoutKind.Sequential)]
    public struct RenderCommand
    {
        public uint         Type;          // RHI::RenderCommandType
        public uint         ColorCount;
        public fixed uint   Colors[MaxPassColorTargets];
        public uint         Depth;
        public uint         Arg0;
        public uint         Arg1;
        public Vector4      ClearColor;
        public float        ClearDepth;
        public fixed uint   Inputs[MaxPassInputs];
        public fixed float  Params[MaxPassParams];
    }

    // C++ の RHI::LightData と同じ並び (48byte)
    [StructLayout(LayoutKind.Sequential)]
    public struct LightData
    {
        public Vector3 Position;
        public float   Range;
        public Vector3 Direction;
        public uint    Type;        // 0 : 平行光源 / 1 : 点光源
        public Vector3 Color;
        public float   Intensity;
    }

    // C++ の RHI::LightingSettings と同じ並び (16byte)
    [StructLayout(LayoutKind.Sequential)]
    public struct LightingSettings
    {
        public uint    Model;       // LightingModel
        public Vector3 Ambient;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LogCallback([MarshalAs(UnmanagedType.LPStr)] string message);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void WLogCallback([MarshalAs(UnmanagedType.LPWStr)] string message);

    // ---------- 初期化 / 終了 / 画面 ----------

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetLogCallback(LogCallback callback, WLogCallback wcallback);

    // C++ 側はポインタで受けるので ref で渡す (以前は値渡しで、x64 の呼び出し規約で偶然動いていた : 2-12)
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_Initialize(out InitializeResult result, ref WindowDesc window);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_CleanUp();

    // C++ の bool は 1 バイトなので I1 で受ける (既定の 4 バイト BOOL だと上位バイトのゴミを拾う)
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    public static extern bool Renderer_Resize(uint width, uint height);

    // commands : 描画パイプラインが積んだ命令 (以前は引数が無く、シーンをバックバッファに描くだけだった)
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_Render(RenderCommand* commands, uint commandCount);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_WaitForGpu();

    // ---------- モデル / 描画オブジェクト ----------

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Renderer_LoadModel([MarshalAs(UnmanagedType.LPWStr)] string filePath, out uint subMeshCount);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_CreateRenderObjects(uint model, uint count, uint* outHandles);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_DestroyRenderObjects(uint* handles, uint count);

    // matrices : Matrix4x4 (16 float) × count。System.Numerics と DirectXMath は同じ並び (行優先・行ベクトル)
    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetRenderObjectTransforms(uint* handles, Matrix4x4* matrices, uint count);

    // ---------- カメラ / 設定 / プロファイラー ----------

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetCamera(ref CameraDesc camera);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetIndirectDraw([MarshalAs(UnmanagedType.I1)] bool enabled);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetInstancing([MarshalAs(UnmanagedType.I1)] bool enabled);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetGpuCulling([MarshalAs(UnmanagedType.I1)] bool enabled);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetOcclusionCulling([MarshalAs(UnmanagedType.I1)] bool enabled);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_ReportProfilerValue([MarshalAs(UnmanagedType.LPStr)] string name, double value);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetProfilerInfo([MarshalAs(UnmanagedType.LPStr)] string name, [MarshalAs(UnmanagedType.LPStr)] string text);

    // ---------- 描画パス / ライト ----------

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Renderer_CreateRenderTarget(ref RenderTargetDesc desc);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_DestroyRenderTarget(uint handle);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern uint Renderer_LoadFullscreenShader([MarshalAs(UnmanagedType.LPWStr)] string filePath, [MarshalAs(UnmanagedType.LPStr)] string entry);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetLights(LightData* lights, uint count);

    [DllImport(Dll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetLightingSettings(ref LightingSettings settings);
}
