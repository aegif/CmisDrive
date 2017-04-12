using DokanNet;
using System;

namespace DokanNetMirror
{
    internal class Programm
    {
        private static void Main(string[] args)
        {
            try
            {
                string user =
                    args.Length > 0
                    ? args[0]
                    : "admin";

                string password =
                    args.Length > 1
                    ? args[1]
                    : "admin";

                string cmisURL =
                    args.Length > 2
                    ? args[2]
                    : Constants.ATOM_PUB_URL;

                string driveLetter =
                    args.Length > 3
                    ? args[3]
                    : "n:\\";

                Mirror mirror = new Mirror(cmisURL, user, password);
                mirror.Mount(driveLetter, DokanOptions.DebugMode, 1);

                Console.WriteLine("Success");
            }
            catch (DokanException ex)
            {
                Console.WriteLine("Error: " + ex.Message);
            }
        }
    }
}