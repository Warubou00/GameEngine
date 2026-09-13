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

            bool isOk = rHI.Initialize(5);

            if(isOk )
            {
                Console.WriteLine("OK");
            }
            else
            {
                Console.WriteLine("NG");
            }

        }
    }
}
