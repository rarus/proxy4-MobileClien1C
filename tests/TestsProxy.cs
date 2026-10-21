using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;

static class TestsProxy
{
    static byte[] B(string s) { return Encoding.UTF8.GetBytes(s); }

    static int FreePort()
    {
        TcpListener l = new TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        int port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    static ProxyServer StartProxyWithFake(int listenPort, int backendPort, FakeAdbServer fake, List<string> log)
    {
        fake.Start(backendPort);
        Config cfg = new Config();
        cfg.ListenPort = listenPort;
        cfg.BackendPort = backendPort;
        cfg.Mode = "vm";
        ProxyServer px = new ProxyServer();
        px.Start(cfg, delegate(string line) { lock (log) { log.Add(line); } });
        TcpClient probe = new TcpClient();
        int waited = 0;
        while (waited < 5000)
        {
            try
            {
                probe.Connect("127.0.0.1", listenPort);
                probe.Close();
                return px;
            }
            catch
            {
                Thread.Sleep(50);
                waited += 50;
            }
        }
        throw new Exception("proxy did not start listening");
    }

    public static void Register()
    {
        SelfTest.Register("Px_EndToEndRewrite", delegate
        {
            int listenPort = FreePort();
            int backendPort = FreePort();
            using (FakeAdbServer fake = new FakeAdbServer())
            {
                List<string> log = new List<string>();
                using (ProxyServer px = StartProxyWithFake(listenPort, backendPort, fake, log))
                {
                    string machine = Environment.MachineName;
                    using (TcpClient c = new TcpClient("127.0.0.1", listenPort))
                    using (NetworkStream s = c.GetStream())
                    {
                        byte[] transport = Encoding.ASCII.GetBytes("host:transport|127.0.0.1:21523");
                        s.Write(Encoding.ASCII.GetBytes(transport.Length.ToString("x4")), 0, 4);
                        s.Write(transport, 0, transport.Length);
                        byte[] st = new byte[4];
                        FakeAdbServer.ReadExactly(s, st, 4);
                        if (Encoding.ASCII.GetString(st) != "OKAY")
                            return false;
                        string cmd = "shell:am broadcast -n com.e1c.mobile.client/com.e1c.mobile.Starter -a com.e1c.mobile.START_CMD --es cmd \"ENTERPRISE /IBNameРЎС‡РµС‚С‡РёРє /DEBUGGERURLhttp://" + machine + ":1560 /WS http://" + machine + ":8080/MKClient\"";
                        byte[] payload = B(cmd);
                        s.Write(Encoding.ASCII.GetBytes(payload.Length.ToString("x4")), 0, 4);
                        s.Write(payload, 0, payload.Length);
                        FakeAdbServer.ReadExactly(s, st, 4);
                        if (Encoding.ASCII.GetString(st) != "OKAY")
                            return false;
                        s.Close();
                    }
                    int waited = 0;
                    while (fake.LastShell == null && waited < 5000) { Thread.Sleep(50); waited += 50; }
                    if (fake.LastShell == null)
                        return false;
                    string got = fake.LastShell;
                    return got.Contains("10.0.2.2:1560") && got.Contains("10.0.2.2:8080")
                        && got.IndexOf(machine, StringComparison.OrdinalIgnoreCase) < 0
                        && got.Contains("/IBNameРЎС‡РµС‚С‡РёРє");
                }
            }
        });
        SelfTest.Register("Px_PassthroughHostCommand", delegate
        {
            int listenPort = FreePort();
            int backendPort = FreePort();
            using (FakeAdbServer fake = new FakeAdbServer())
            {
                List<string> log = new List<string>();
                using (ProxyServer px = StartProxyWithFake(listenPort, backendPort, fake, log))
                {
                    using (TcpClient c = new TcpClient("127.0.0.1", listenPort))
                    using (NetworkStream s = c.GetStream())
                    {
                        byte[] payload = Encoding.ASCII.GetBytes("host:devices");
                        s.Write(Encoding.ASCII.GetBytes(payload.Length.ToString("x4")), 0, 4);
                        s.Write(payload, 0, payload.Length);
                        byte[] st = new byte[4];
                        FakeAdbServer.ReadExactly(s, st, 4);
                        if (Encoding.ASCII.GetString(st) != "OKAY")
                            return false;
                        string packet = FakeAdbServer.ReadPacket(s);
                        return packet != null && packet.Contains("127.0.0.1:21523");
                    }
                }
            }
        });
        SelfTest.Register("Px_TransportIdAndShellV2", delegate
        {
            int listenPort = FreePort();
            int backendPort = FreePort();
            using (FakeAdbServer fake = new FakeAdbServer())
            {
                List<string> log = new List<string>();
                using (ProxyServer px = StartProxyWithFake(listenPort, backendPort, fake, log))
                {
                    string machine = Environment.MachineName;
                    using (TcpClient c = new TcpClient("127.0.0.1", listenPort))
                    using (NetworkStream s = c.GetStream())
                    {
                        byte[] transport = Encoding.ASCII.GetBytes("host:transport-id:42");
                        s.Write(Encoding.ASCII.GetBytes(transport.Length.ToString("x4")), 0, 4);
                        s.Write(transport, 0, transport.Length);
                        byte[] st = new byte[4];
                        FakeAdbServer.ReadExactly(s, st, 4);
                        if (Encoding.ASCII.GetString(st) != "OKAY")
                            return false;
                        string cmd = "shell,v2,raw:am broadcast -n com.e1c.mobile.client/com.e1c.mobile.Starter --es cmd \"x /DEBUGGERURLhttp://" + machine + ":1560 /WS http://" + machine + ":8080/MKClient\"";
                        byte[] payload = B(cmd);
                        s.Write(Encoding.ASCII.GetBytes(payload.Length.ToString("x4")), 0, 4);
                        s.Write(payload, 0, payload.Length);
                        FakeAdbServer.ReadExactly(s, st, 4);
                        s.Close();
                    }
                    int waited = 0;
                    while (fake.LastShell == null && waited < 5000) { Thread.Sleep(50); waited += 50; }
                    if (fake.LastShell == null)
                        return false;
                    string got = fake.LastShell;
                    return got.Contains("10.0.2.2:1560") && got.Contains("10.0.2.2:8080")
                        && got.IndexOf(machine, StringComparison.OrdinalIgnoreCase) < 0;
                }
            }
        });
        SelfTest.Register("Px_StreamAfterShell", delegate
        {
            int listenPort = FreePort();
            int backendPort = FreePort();
            using (FakeAdbServer fake = new FakeAdbServer())
            {
                List<string> log = new List<string>();
                using (ProxyServer px = StartProxyWithFake(listenPort, backendPort, fake, log))
                {
                    using (TcpClient c = new TcpClient("127.0.0.1", listenPort))
                    using (NetworkStream s = c.GetStream())
                    {
                        byte[] payload = Encoding.ASCII.GetBytes("shell:echo hello");
                        s.Write(Encoding.ASCII.GetBytes(payload.Length.ToString("x4")), 0, 4);
                        s.Write(payload, 0, payload.Length);
                        byte[] st = new byte[4];
                        FakeAdbServer.ReadExactly(s, st, 4);
                        byte[] tail = Encoding.ASCII.GetBytes("raw-bytes-after-shell");
                        s.Write(tail, 0, tail.Length);
                        s.Flush();
                        ((TcpClient)c).Close();
                    }
                    int waited = 0;
                    while (fake.RawAfterShell == null && waited < 5000) { Thread.Sleep(50); waited += 50; }
                    if (fake.RawAfterShell == null)
                        return false;
                    return Encoding.ASCII.GetString(fake.RawAfterShell) == "raw-bytes-after-shell";
                }
            }
        });
    }
}
