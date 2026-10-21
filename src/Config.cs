using System;
using System.IO;
using System.Text;

class Config
{
    public int ListenPort = 5037;
    public int BackendPort = 5038;
    public string Mode = "pc";
    public string ManualIp = "";
    public string AdbPath = "";
    public string LogFile = "proxy4_MobileClien1C.log";

    public static Config Load(string path)
    {
        Config c = new Config();
        if (!File.Exists(path))
        {
            File.WriteAllText(path, c.RenderTemplate(), new UTF8Encoding(false));
            return c;
        }
        foreach (string raw in File.ReadAllLines(path))
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith("#") || line.StartsWith("["))
                continue;
            int eq = line.IndexOf('=');
            if (eq < 0)
                continue;
            string k = line.Substring(0, eq).Trim().ToLowerInvariant();
            string v = line.Substring(eq + 1).Trim();
            int p;
            if (k == "listen_port" && int.TryParse(v, out p))
                c.ListenPort = p;
            else if (k == "backend_port" && int.TryParse(v, out p))
                c.BackendPort = p;
            else if (k == "mode")
                c.Mode = v.ToLowerInvariant();
            else if (k == "manual_ip")
                c.ManualIp = v;
            else if (k == "adb_path")
                c.AdbPath = v;
            else if (k == "log_file")
                c.LogFile = v;
        }
        return c;
    }

    string RenderTemplate()
    {
        StringBuilder sb = new StringBuilder();
        sb.AppendLine("# proxy4_MobileClien1C configuration");
        sb.AppendLine();
        sb.AppendLine("[server]");
        sb.AppendLine("listen_port=" + ListenPort);
        sb.AppendLine("backend_port=" + BackendPort);
        sb.AppendLine();
        sb.AppendLine("[rewrite]");
        sb.AppendLine("# mode: vm = 10.0.2.2 (host alias for VM NAT); pc = current PC IPv4 (auto); manual = manual_ip");
        sb.AppendLine("mode=" + Mode);
        sb.AppendLine("manual_ip=" + ManualIp);
        sb.AppendLine();
        sb.AppendLine("[adb]");
        sb.AppendLine("# adb.exe is searched in current directory first, then by adb_path");
        sb.AppendLine("adb_path=" + AdbPath);
        sb.AppendLine();
        sb.AppendLine("[log]");
        sb.AppendLine("log_file=" + LogFile);
        return sb.ToString();
    }
}
