using System;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;

namespace VanguardSwitch
{
    static class Program
    {
        [STAThread]
        static void Main(string[] args)
        {
            using (var mutex = new Mutex(true, "VanguardSwitch.SingleInstance", out bool first))
            {
                if (!first)
                {
                    // after an update the old copy may still be closing
                    if (!args.Contains("--updated")) return;
                    try { if (!mutex.WaitOne(15000)) return; }
                    catch (AbandonedMutexException) { }
                }
                System.Threading.Tasks.Task.Run(Updater.Cleanup);

                // Windows 11 24H2+ only lists loaded drivers with SeDebugPrivilege enabled
                try { Process.EnterDebugMode(); } catch { }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new MainForm());
            }
        }
    }
}
