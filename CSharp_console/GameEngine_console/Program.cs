using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace GameEngine_console
{
    internal class Program
    {
        public static void Main(string[] args)
        {
            RHI rHI = new RHI();
            RHI.RHIInitializedInfo info;

            rHI.Initialize(out info);

            if(info.InitializedDevice == 0)
            {
                Console.WriteLine("Initialized Device");
            }
            else
            {
                Console.WriteLine("Failed Initialize Device");
            }

            if (info.InitializedCommandContext == 0)
            {
                Console.WriteLine("Initialized CommandContext");
            }
            else
            {
                Console.WriteLine("Failed Initialize CommandContext");
            }

            if(info.InitializedBuffer == 0)
            {
                Console.WriteLine("Initialized Buffer");
            }
            else
            {
                Console.WriteLine("Failed Initialize Buffer");
            }

            if(info.InitializedTexture == 0)
            {
                Console.WriteLine("Initialized Texture");
            }
            else
            {
                Console.WriteLine("Failed Initialize Texture");
            }

        }
    }
}
