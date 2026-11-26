using System;
using System.Threading;
using System.Windows.Forms;
using DroidDeck.Core;
using DroidDeck.UI;

namespace DroidDeck
{
    internal static class Program
    {
        private static Mutex _singleInstanceMutex;

        [STAThread]
        private static void Main()
        {
            bool createdNew;
            _singleInstanceMutex = new Mutex(true, "DroidDeck_SingleInstance_Mutex", out createdNew);
            if (!createdNew)
            {
                MessageBox.Show("DroidDeck 已经在运行中。", "DroidDeck", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                // Per-Monitor V2 DPI awareness for crisp text & exact scaling on Windows 10/11
                NativeMethods.SetProcessDpiAwarenessContext(new IntPtr(-4));
            }
            catch
            {
                NativeMethods.SetProcessDPIAware();
            }
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }
    }
}
