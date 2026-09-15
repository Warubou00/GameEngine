using System;
using System.Collections.Generic;
using System.Drawing.Text;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GameEngine_console
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            RHI rhi = new RHI();
            RHI.RHIInitializedInfo info;

            rhi.Initialize(out info);

            Console.WriteLine($"Device         : {rhi.CheckRHIResult(info.InitializedDevice)}");
            Console.WriteLine($"CommandContext : {rhi.CheckRHIResult(info.InitializedCommandContext)}");
            Console.WriteLine($"Buffer         : {rhi.CheckRHIResult(info.InitializedBuffer)}");
            Console.WriteLine($"Texture        : {rhi.CheckRHIResult(info.InitializedTexture)}");
        }
    }
}
