using System;

namespace Automation.Tests
{
    internal static class FrmMainConstructorSmoke
    {
        [STAThread]
        private static int Main()
        {
            try
            {
                using (var form = new FrmMain())
                {
                }
                Console.WriteLine("FrmMain constructor: PASS");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex);
                return 1;
            }
        }
    }
}
