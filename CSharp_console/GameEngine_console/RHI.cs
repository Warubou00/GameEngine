using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public class RHI : IDisposable
{
    private IntPtr             rhiHandle = IntPtr.Zero;
    private RHIInitializedInfo info;
    private RHIInfo            rhiInfo;

    private const string WindowTitle = "C# DirectX12 Engine";

    // Width / Height は枠とタイトルバーを含んだサイズなので、描画領域(ClientSize)で指定する
    // (以前は外枠のサイズを渡していたため、スワップチェーンが実際の描画領域より大きかった : 2-4)
    private Form window = new Form
    {
        ClientSize = new System.Drawing.Size(1280, 720),
        Text = WindowTitle
    };

    private bool isInitialized = false;
    private bool isClosed      = false;
    private bool isDisposed    = false;

    // ※FPS などの計測値は ImGui のプロファイラーウィンドウに表示する (F1 で表示 / 非表示)
    //   以前はここで計測してタイトルバーに出していた

    public struct RHIInfo
    {
        public IntPtr hwnd;
        public int width;
        public int height;
        public int enableVSync;
    }

    public struct RHIInitializedInfo
    {
        public int InitializedDevice;
        public int InitializedCommandContext;
        public int InitializedBuffer;
        public int InitializedTexture;
        public int InitializedPipeline;
    }

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void LogCallbackDelegate(
            [MarshalAs(UnmanagedType.LPStr)] string message);

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    public delegate void WLogCallbackDelegate(
            [MarshalAs(UnmanagedType.LPWStr)] string message);

    private static LogCallbackDelegate _logCallback;
    private static WLogCallbackDelegate _wLogCallback;

    // ------------------------------------------------------------------
    // C# から呼ぶのは Renderer.dll だけ (Phase 3 で RHI.dll から入口を移した)
    //   C# → Renderer.dll (モデル / カメラ / 描画リスト) → RHI.dll (GPU) → Core.dll
    // RHI.dll / Core.dll は Renderer.dll が読み込むので、出力フォルダに置いておくだけでよい
    // ------------------------------------------------------------------
    private const string EngineDll = "Renderer.dll";

    [DllImport(EngineDll, CallingConvention = CallingConvention.Cdecl)]
    public static extern void Renderer_SetLogCallback(LogCallbackDelegate callback, WLogCallbackDelegate wcallback);

    // C++ 側はポインタで受けるので ref で渡す
    // (以前は値渡しで、x64 の呼び出し規約がたまたま参照渡しにしていたため動いていた : 2-12)
    [DllImport(EngineDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void Renderer_Initialize(out RHIInitializedInfo info, ref RHIInfo rHIInfo);

    // C++ の bool は 1 バイトなので I1 で受ける (既定の 4 バイト BOOL だと上位バイトのゴミを拾う)
    [DllImport(EngineDll, CallingConvention = CallingConvention.Cdecl)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static extern bool Renderer_Resize(uint width, uint height);

    [DllImport(EngineDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void Renderer_CleanUp();

    [DllImport(EngineDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void Renderer_Render();

    [DllImport(EngineDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void Renderer_TestLoadModel(float posX, float posY, float posZ);

    [DllImport(EngineDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void Renderer_TestSpawnGrid(uint count);

    // C++ の bool は 1 バイトなので I1 で渡す
    [DllImport(EngineDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void Renderer_SetIndirectDraw([MarshalAs(UnmanagedType.I1)] bool enabled);

    [DllImport(EngineDll, CallingConvention = CallingConvention.Cdecl)]
    private static extern void Renderer_SetInstancing([MarshalAs(UnmanagedType.I1)] bool enabled);

    public void Initialize(out RHIInitializedInfo info)
    {
        rhiInfo.hwnd        = window.Handle;
        rhiInfo.width       = window.ClientSize.Width;
        rhiInfo.height      = window.ClientSize.Height;
        rhiInfo.enableVSync = 0;   // 0 : VSync OFF (対応環境ではテアリングを許可して上限なしで描画する)

        // ウィンドウを閉じたらメインループを抜ける (以前は while(true) で終了処理が呼ばれなかった)
        window.FormClosed += (sender, e) => isClosed = true;

        // 描画領域のサイズが変わったらスワップチェーンを作り直す (最小化中は 0 になるので送らない)
        window.ClientSizeChanged += (sender, e) =>
        {
            if (!isInitialized || window.WindowState == FormWindowState.Minimized) return;
            if (window.ClientSize.Width <= 0 || window.ClientSize.Height <= 0) return;
            Renderer_Resize((uint)window.ClientSize.Width, (uint)window.ClientSize.Height);
        };

        window.Show();

        _logCallback  = new LogCallbackDelegate(Log);
        _wLogCallback = new WLogCallbackDelegate(Log);

        Renderer_SetLogCallback(_logCallback, _wLogCallback);

        Renderer_Initialize(out info, ref rhiInfo);
        isInitialized = isAllInitialized(info);

        return;
    }

    // ウィンドウが開いている間 true (メインループの条件に使う)
    public bool IsOpen => !isClosed && !window.IsDisposed;

    public void Test_LoadModel(float posX, float posY, float posZ)
    {
        Renderer_TestLoadModel(posX, posY, posZ);
    }

    // 負荷テスト : 読み込んだモデルを count 個並べる
    public void Test_SpawnGrid(uint count)
    {
        Renderer_TestSpawnGrid(count);
    }

    // ExecuteIndirect の ON / OFF (プロファイラーのチェックボックスでも切り替えられる)
    public void SetIndirectDraw(bool enabled)
    {
        Renderer_SetIndirectDraw(enabled);
    }

    // インスタンシングの ON / OFF (プロファイラーのチェックボックスでも切り替えられる)
    public void SetInstancing(bool enabled)
    {
        Renderer_SetInstancing(enabled);
    }

    public void Render()
    {
        // 最小化中は描画しても見えないので、CPU / GPU を休ませる
        if (window.WindowState == FormWindowState.Minimized)
        {
            System.Threading.Thread.Sleep(16);
            return;
        }

        Renderer_Render();
    }

    public void Dispose()
    {
        // 2 回呼ばれても C++ 側を 2 回解放しないようにする
        if (isDisposed) return;
        isDisposed = true;

        Renderer_CleanUp();
        if (!window.IsDisposed) window.Dispose();
    }

    private enum RHI_RESULT
    {
        SUCCEEDED                = 1,
        FAILED                   = -1,
        FAILED_INITIALIZED       = -2,
        FAILED_INVALID_ARGUMENT  = -3,
        FAILED_CREATE_ANY_OBJECT = -4,
        FAILED_COMPILED_SHADER   = -5,
    }

    public string CheckRHIResult(int id)
    {
        switch(id)
        {
            case (int)RHI_RESULT.SUCCEEDED:
                return "RHI_RESULT_SUCCEEDED";
            case (int)RHI_RESULT.FAILED:
                return "RHI_RESULT_FAILED";
            case (int)RHI_RESULT.FAILED_INITIALIZED:
                return "RHI_RESULT_FAILED_INITIALIZED";
            case (int)RHI_RESULT.FAILED_INVALID_ARGUMENT:
                return "RHI_RESULT_FAILED_INVALID_ARGUMENT";
            case (int)RHI_RESULT.FAILED_CREATE_ANY_OBJECT:
                return "RHI_RESULT_FAILED_CREATE_ANY_OBJECT";
            case (int)RHI_RESULT.FAILED_COMPILED_SHADER:
                return "RHI_RESULT_FAILED_COMPILED_SHADER";
            default:
                return "NULL";
        }
    }

    public bool isAllInitialized(RHIInitializedInfo info)
    {
        if (info.InitializedDevice         == 1 &&
            info.InitializedCommandContext == 1 &&
            info.InitializedBuffer         == 1 &&
            info.InitializedTexture        == 1 &&
            info.InitializedPipeline       == 1
            )
            return true;

        return false;
    }

    private void Log(string message)
    {
        Console.WriteLine(message);
    }

    private readonly RHI_RESULT[] _rhiResult =
    {
        RHI_RESULT.SUCCEEDED,
        RHI_RESULT.FAILED,
        RHI_RESULT.FAILED_INITIALIZED,
        RHI_RESULT.FAILED_INVALID_ARGUMENT,
        RHI_RESULT.FAILED_CREATE_ANY_OBJECT,
        RHI_RESULT.FAILED_COMPILED_SHADER
    };
}