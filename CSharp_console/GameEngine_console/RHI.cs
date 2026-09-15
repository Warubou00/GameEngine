using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

public class RHI : IDisposable
{
    private IntPtr             rhiHandle = IntPtr.Zero;
    private RHIInitializedInfo info;
    private RHIInfo            rhiInfo;

    private Form window = new Form
    {
        Width = 1280,
        Height = 720,
        Text = "C# DirectX12 Engine"
    };

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
    }

    [DllImport("RHI.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern void RHI_Initialize(out RHIInitializedInfo info, RHIInfo rHIInfo);

    public void Initialize(out RHIInitializedInfo info)
    {
        rhiInfo.hwnd        = window.Handle;
        rhiInfo.width       = window.Width;
        rhiInfo.height      = window.Height;
        rhiInfo.enableVSync = 0;

        RHI_Initialize(out info, rhiInfo);

        return;
    }

    public void Render()
    {

    }

    public void Dispose()
    {

    }

    private enum RHI_RESULT
    {
        SUCCEEDED               = 1,
        FAILED                  = -1,
        FAILED_INITIALIZED      = -2,
        FAILED_INVALID_ARGUMENT = -3,
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
            default:
                return "NULL";
        }
    }

    private readonly RHI_RESULT[] _rhiResult =
    {
        RHI_RESULT.SUCCEEDED,
        RHI_RESULT.FAILED,
        RHI_RESULT.FAILED_INITIALIZED,
        RHI_RESULT.FAILED_INVALID_ARGUMENT
    };
}