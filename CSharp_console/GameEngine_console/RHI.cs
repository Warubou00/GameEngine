using System;
using System.Runtime.InteropServices;

public class RHI : IDisposable
{
    private IntPtr rhiHandle = IntPtr.Zero;
    private bool succeesed = false;

    [DllImport("RHI.dll", CallingConvention = CallingConvention.Cdecl)]
    private static extern bool RHI_Initialize(int num);

    public bool Initialize(int num)
    {
        return RHI_Initialize(num);
    }

    public void Render()
    {

    }

    public void Dispose()
    {

    }
}