using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Sockets;
using System.Threading;

static class AdbControl
{
    public static string FindAdb(string iniAdbPath)
    {
        string local = Path.Combine(Environment.CurrentDirectory, "adb.exe");
        if (File.Exists(local))
            return local;
        string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        string besideExe = Path.Combine(exeDir, "adb.exe");
        if (File.Exists(besideExe))
            return besideExe;
        if (!string.IsNullOrEmpty(iniAdbPath) && File.Exists(iniAdbPath))
            return iniAdbPath;
        return null;
    }

    public static bool StartBackend(int port, string iniAdbPath)
    {
        try
        {
            SmartSocket.QueryVersion(port);
            return true;
        }
        catch (IOException) { }
        string adb = FindAdb(iniAdbPath);
        if (adb == null)
            return false;
        ProcessStartInfo psi = new ProcessStartInfo();
        psi.FileName = adb;
        psi.Arguments = "start-server";
        psi.UseShellExecute = false;
        psi.CreateNoWindow = true;
        psi.EnvironmentVariables["ANDROID_ADB_SERVER_PORT"] = port.ToString();
        try
        {
            using (Process p = Process.Start(psi))
            {
                if (p != null && !p.WaitForExit(15000))
                    return false;
            }
        }
        catch
        {
            return false;
        }
        try
        {
            SmartSocket.QueryVersion(port);
            return true;
        }
        catch (IOException)
        {
            return false;
        }
    }

    public static List<string> MigrateExistingServer(int listenPort, int backendPort)
    {
        List<string> serials = new List<string>();
        TcpClient probe = new TcpClient();
        try
        {
            probe.Connect("127.0.0.1", listenPort);
            probe.Close();
        }
        catch
        {
            return serials;
        }
        int version = SmartSocket.QueryVersion(listenPort);
        try
        {
            serials = SmartSocket.QueryDevices(listenPort);
        }
        catch (IOException)
        {
            serials = new List<string>();
        }
        SmartSocket.SendKill(listenPort);
        int waited = 0;
        while (waited < 5000)
        {
            TcpClient p2 = new TcpClient();
            try
            {
                p2.Connect("127.0.0.1", listenPort);
                p2.Close();
                Thread.Sleep(200);
                waited += 200;
            }
            catch
            {
                return serials;
            }
        }
        return serials;
    }

    public static int ReconnectDevices(List<string> serials, int backendPort)
    {
        int ok = 0;
        foreach (string serial in serials)
        {
            if (serial.IndexOf(':') < 0)
                continue;
            try
            {
                if (SmartSocket.SendConnect(serial, backendPort))
                    ok++;
            }
            catch (IOException) { }
        }
        return ok;
    }
}
