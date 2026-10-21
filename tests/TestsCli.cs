using System;
using System.Diagnostics;
using System.IO;
using System.Threading;

static class TestsCli
{
    public static void Register()
    {
        SelfTest.Register("Stop_CliNotRunning", delegate
        {
            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            ProcessStartInfo psi = new ProcessStartInfo(exe, "stop /event:SelfTestScoped");
            psi.RedirectStandardOutput = true;
            psi.UseShellExecute = false;
            psi.CreateNoWindow = true;
            using (Process p = Process.Start(psi))
            {
                string output = p.StandardOutput.ReadToEnd();
                if (!p.WaitForExit(5000))
                    return false;
                return p.ExitCode == 1 && output.IndexOf("not running", StringComparison.OrdinalIgnoreCase) >= 0;
            }
        });
        SelfTest.Register("Stop_EventScopedDoesNotTouchProduction", delegate
        {
            bool createdNew;
            EventWaitHandle prod = new EventWaitHandle(false, EventResetMode.ManualReset, "Local\\proxy4_MobileClien1CStop", out createdNew);
            try
            {
                string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
                ProcessStartInfo psi = new ProcessStartInfo(exe, "stop /event:SelfTestScoped2");
                psi.RedirectStandardOutput = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(5000))
                        return false;
                    if (p.ExitCode != 1 || output.IndexOf("not running", StringComparison.OrdinalIgnoreCase) < 0)
                        return false;
                }
                return !prod.WaitOne(0);
            }
            finally
            {
                prod.Close();
            }
        });
    }
}

