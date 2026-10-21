using System;

static class TestsIp
{
    public static void Register()
    {
        SelfTest.Register("Ip_VmMode", delegate
        {
            return IpResolver.Resolve("vm", "") == "10.0.2.2";
        });
        SelfTest.Register("Ip_ManualValid", delegate
        {
            return IpResolver.Resolve("manual", "192.168.1.1") == "192.168.1.1";
        });
        SelfTest.Register("Ip_ManualInvalid", delegate
        {
            if (IpResolver.Resolve("manual", "abc") != null)
                return false;
            if (IpResolver.Resolve("manual", "") != null)
                return false;
            return true;
        });
        SelfTest.Register("Ip_PcModeSmoke", delegate
        {
            string ip = IpResolver.Resolve("pc", "");
            if (ip == null)
                return false;
            System.Net.IPAddress parsed;
            if (!System.Net.IPAddress.TryParse(ip, out parsed))
                return false;
            if (parsed.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
                return false;
            return ip != "192.168.56.1" && ip != "172.25.112.1";
        });
    }
}
