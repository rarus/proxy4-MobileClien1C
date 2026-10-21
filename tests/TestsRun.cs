using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

static class TestsRun
{
    static int FreePort()
    {
        TcpListener l = new TcpListener(IPAddress.Loopback, 0);
        l.Start();
        int port = ((IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    public static void Register()
    {
        SelfTest.Register("Run_Exit2OnOccupiedPort", delegate
        {
            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string exeDir = Path.GetDirectoryName(exe);
            int occupied = FreePort();
            TcpListener garbage = new TcpListener(IPAddress.Loopback, occupied);
            garbage.Start();
            string ini = Path.Combine(exeDir, "run_test.ini");
            string logFile = Path.Combine(exeDir, "run_test.log");
            try
            {
                File.WriteAllText(ini,
                    "listen_port=" + occupied + "\r\nbackend_port=" + FreePort() + "\r\nmode=vm\r\nlog_file=run_test.log\r\n",
                    Encoding.ASCII);
                File.Delete(logFile);
                ProcessStartInfo psi = new ProcessStartInfo(exe, "run \"" + ini + "\"");
                psi.RedirectStandardOutput = true;
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    string output = p.StandardOutput.ReadToEnd();
                    if (!p.WaitForExit(20000))
                        return false;
                    if (p.ExitCode != 2)
                        return false;
                    string log = File.Exists(logFile) ? File.ReadAllText(logFile) : "";
                    return log.Contains("non-adb");
                }
            }
            finally
            {
                garbage.Stop();
                try { File.Delete(ini); } catch { }
                try { File.Delete(logFile); } catch { }
            }
        });
        SelfTest.Register("Run_ScopedStopAndHonestLog", delegate
        {
            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            string exeDir = Path.GetDirectoryName(exe);
            int listen = FreePort();
            int backend = FreePort();
            string ini = Path.Combine(exeDir, "run_test2.ini");
            string logFile = Path.Combine(exeDir, "run_test2.log");
            try
            {
                File.WriteAllText(ini,
                    "listen_port=" + listen + "\r\nbackend_port=" + backend + "\r\nmode=vm\r\nlog_file=run_test2.log\r\n",
                    Encoding.ASCII);
                File.Delete(logFile);
                ProcessStartInfo psi = new ProcessStartInfo(exe, "run \"" + ini + "\"");
                psi.UseShellExecute = false;
                psi.CreateNoWindow = true;
                using (Process p = Process.Start(psi))
                {
                    bool started = false;
                    int waited = 0;
                    while (!started && waited < 10000)
                    {
                        Thread.Sleep(100);
                        waited += 100;
                        started = File.Exists(logFile) && File.ReadAllText(logFile).Contains("proxying");
                    }
                    if (!started)
                        return false;
                    ProcessStartInfo stopPsi = new ProcessStartInfo(exe, "stop /event:proxy4_MobileClien1CStop_" + listen);
                    stopPsi.RedirectStandardOutput = true;
                    stopPsi.UseShellExecute = false;
                    stopPsi.CreateNoWindow = true;
                    using (Process sp = Process.Start(stopPsi))
                    {
                        sp.StandardOutput.ReadToEnd();
                        if (!sp.WaitForExit(10000) || sp.ExitCode != 0)
                            return false;
                    }
                    if (!p.WaitForExit(20000))
                        return false;
                    string log = File.ReadAllText(logFile);
                    return log.Contains("stopped") && log.Contains("restore failed");
                }
            }
            finally
            {
                try { File.Delete(ini); } catch { }
                try { File.Delete(logFile); } catch { }
            }
        });
        SelfTest.Register("ShutdownRestore_FalseWithoutBackend", delegate
        {
            Config cfg = new Config();
            cfg.ListenPort = FreePort();
            cfg.BackendPort = FreePort();
            cfg.AdbPath = "";
            return !Program.ShutdownAndRestore(cfg, new System.Collections.Generic.List<string>(), null);
        });
    }
}
