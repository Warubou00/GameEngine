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
            Console.WriteLine($"Pipeline       : {rhi.CheckRHIResult(info.InitializedPipeline)}");

            //rhi.DebugExecute();

            rhi.Test_LoadModel(0.0f, 5.0f, -10.0f);

            // ウィンドウが閉じられたらループを抜けて終了処理を呼ぶ
            // (以前は while(true) だったため、閉じても RHI_CleanUp が呼ばれなかった)
            while (rhi.IsOpen)
            {
                System.Windows.Forms.Application.DoEvents();

                if (rhi.IsOpen && rhi.isAllInitialized(info))
                    rhi.Render();
            }

            // GPU の完了を待ってから C++ 側を解放する
            rhi.Dispose();
        }
    }
}
