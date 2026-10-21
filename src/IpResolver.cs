using System;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

static class IpResolver
{
    const string VmHost = "10.0.2.2";

    public static string Resolve(string mode, string manualIp)
    {
        if (mode == "vm")
            return VmHost;
        if (mode == "manual")
        {
            IPAddress ip;
            if (!string.IsNullOrEmpty(manualIp) && IPAddress.TryParse(manualIp, out ip) && ip.AddressFamily == AddressFamily.InterNetwork)
                return manualIp;
            return null;
        }
        if (mode == "pc")
            return DetectPcIp();
        return null;
    }

    static string DetectPcIp()
    {
        NetworkInterface best = null;
        foreach (NetworkInterface nic in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (nic.OperationalStatus != OperationalStatus.Up)
                continue;
            if (nic.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            string desc = nic.Description.ToLowerInvariant();
            if (desc.Contains("virtualbox") || desc.Contains("hyper-v") || desc.Contains("vethernet")
                || desc.Contains("wsl") || desc.Contains("tap") || desc.Contains("loopback")
                || desc.Contains("bluestacks") || desc.Contains("microvirt"))
                continue;
            string ip = FirstIpv4(nic);
            if (ip == null)
                continue;
            bool hasGateway = nic.GetIPProperties().GatewayAddresses.Count > 0;
            if (hasGateway)
                return ip;
            if (best == null)
                best = nic;
        }
        if (best != null)
            return FirstIpv4(best);
        return null;
    }

    static string FirstIpv4(NetworkInterface nic)
    {
        foreach (UnicastIPAddressInformation a in nic.GetIPProperties().UnicastAddresses)
        {
            if (a.Address.AddressFamily == AddressFamily.InterNetwork && !a.Address.ToString().StartsWith("169.254."))
                return a.Address.ToString();
        }
        return null;
    }
}
