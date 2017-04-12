using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using DokanNetMirror;

namespace CmisDrive.Tests
{
    class Tests
    {
        private static void Main(string[] args)
        {
            ReadFileTest();
        }
 
        private static void CreateDirectoryTest()
        {
            new Mirror("admin", "admin", Constants.ATOM_PUB_URL).CreateDirectory("\\New folder", null);
        }

        private static void ReadFileTest()
        {
            byte[] buffer = new byte[4096];
            int bytesRead;
            new Mirror("admin", "admin", Constants.ATOM_PUB_URL).ReadFile("\\hello.txt", buffer, out bytesRead, 0, null);
        }
    }
}
