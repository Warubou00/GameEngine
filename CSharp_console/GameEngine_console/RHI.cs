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
}