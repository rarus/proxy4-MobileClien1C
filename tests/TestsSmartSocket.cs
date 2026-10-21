using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class FakeAdbServer : IDisposable
{
    TcpListener _listener;
    Thread _thread;
    volatile bool _stopped;
    public int Port;
    public List<string> ReceivedCommands = new List<string>();
    public volatile string LastShell;
    public volatile byte[] RawAfterShell;

    public void Start() { Start(0); }

    public void Start(int port)
    {
        _listener = new TcpListener(IPAddress.Loopback, port);
        _listener.Start();
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _thread = new Thread(AcceptLoop);
        _thread.IsBackground = true;
        _thread.Start();
    }

    void AcceptLoop()
    {
        try
        {
            while (!_stopped)
            {
                TcpClient client;
                try { client = _listener.AcceptTcpClient(); } catch { break; }
                Thread t = new Thread(delegate() { Serve(client); });
                t.IsBackground = true;
                t.Start();
            }
        }
        catch { }
    }

    void Serve(TcpClient client)
    {
        try
        {
            NetworkStream s = client.GetStream();
            while (true)
            {
                byte[] raw = ReadPacketRaw(s);
                string cmd = raw == null ? null : Encoding.ASCII.GetString(raw);
                if (cmd == null)
                    break;
                lock (ReceivedCommands) { ReceivedCommands.Add(cmd); }
                if (cmd == "host:version")
                {
                    Write(s, Encoding.ASCII.GetBytes("OKAY"));
                    WritePacket(s, Encoding.ASCII.GetBytes("001f"));
                }
                else if (cmd.StartsWith("host:transport"))
                {
                    Write(s, Encoding.ASCII.GetBytes("OKAY"));
                }
                else if (cmd.StartsWith("shell:") || cmd.StartsWith("shell,v2"))
                {
                    LastShell = Encoding.UTF8.GetString(raw).TrimEnd('\n');
                    Write(s, Encoding.ASCII.GetBytes("OKAY"));
                    System.IO.MemoryStream ms = new System.IO.MemoryStream();
                    byte[] buf = new byte[4096];
                    try
                    {
                        int n;
                        while ((n = s.Read(buf, 0, buf.Length)) > 0)
                            ms.Write(buf, 0, n);
                    }
                    catch { }
                    RawAfterShell = ms.ToArray();
                    try { client.Close(); } catch { }
                    return;
                }
                else if (cmd == "host:devices")
                {
                    Write(s, Encoding.ASCII.GetBytes("OKAY"));
                    WritePacket(s, Encoding.ASCII.GetBytes("127.0.0.1:21523\tdevice\nemulator-x\toffline\n"));
                }
                else if (cmd == "host:kill")
                {
                    Write(s, Encoding.ASCII.GetBytes("OKAY"));
                    try { client.Close(); } catch { }
                    Stop();
                    return;
                }
                else if (cmd.StartsWith("host:connect:"))
                {
                    Write(s, Encoding.ASCII.GetBytes("OKAY"));
                    WritePacket(s, Encoding.ASCII.GetBytes("connected to " + cmd.Substring(13) + "\n"));
                }
                else
                {
                    Write(s, Encoding.ASCII.GetBytes("FAIL"));
                    WritePacket(s, Encoding.ASCII.GetBytes("unknown\n"));
                }
            }
            client.Close();
        }
        catch { }
    }

    public void Stop()
    {
        _stopped = true;
        try { _listener.Stop(); } catch { }
    }

    public bool WaitForStop(int timeoutMs)
    {
        int waited = 0;
        while (!_stopped && waited < timeoutMs) { Thread.Sleep(50); waited += 50; }
        return _stopped;
    }

    public void Dispose() { Stop(); }

    internal static string ReadPacket(NetworkStream s)
    {
        byte[] raw = ReadPacketRaw(s);
        return raw == null ? null : Encoding.ASCII.GetString(raw);
    }

    internal static byte[] ReadPacketRaw(NetworkStream s)
    {
        byte[] lenBuf = new byte[4];
        if (!ReadExactly(s, lenBuf, 4))
            return null;
        int len = Convert.ToInt32(Encoding.ASCII.GetString(lenBuf), 16);
        byte[] buf = new byte[len];
        if (!ReadExactly(s, buf, len))
            return null;
        return buf;
    }

    internal static void WritePacket(NetworkStream s, byte[] payload)
    {
        byte[] head = Encoding.ASCII.GetBytes(payload.Length.ToString("x4"));
        Write(s, head);
        Write(s, payload);
    }

    internal static bool ReadExactly(NetworkStream s, byte[] buf, int count)
    {
        int read = 0;
        while (read < count)
        {
            int n = s.Read(buf, read, count - read);
            if (n <= 0)
                return false;
            read += n;
        }
        return true;
    }

    static void Write(NetworkStream s, byte[] data)
    {
        s.Write(data, 0, data.Length);
        s.Flush();
    }
}

static class TestsSmartSocket
{
    public static void Register()
    {
        SelfTest.Register("Ss_VersionDevicesKill", delegate
        {
            using (FakeAdbServer fake = new FakeAdbServer())
            {
                fake.Start();
                int version = SmartSocket.QueryVersion(fake.Port);
                if (version != 31)
                    return false;
                List<string> devices = SmartSocket.QueryDevices(fake.Port);
                if (devices.Count != 2 || devices[0] != "127.0.0.1:21523" || devices[1] != "emulator-x")
                    return false;
                SmartSocket.SendKill(fake.Port);
                return fake.WaitForStop(3000);
            }
        });
        SelfTest.Register("Ss_NotAdbServer", delegate
        {
            TcpListener garbage = new TcpListener(IPAddress.Loopback, 0);
            garbage.Start();
            int port = ((IPEndPoint)garbage.LocalEndpoint).Port;
            Thread t = new Thread(delegate()
            {
                try
                {
                    TcpClient c = garbage.AcceptTcpClient();
                    byte[] resp = Encoding.ASCII.GetBytes("HTTP/1.1 400 Bad Request\r\n\r\n");
                    c.GetStream().Write(resp, 0, resp.Length);
                    c.GetStream().Flush();
                }
                catch { }
            });
            t.IsBackground = true;
            t.Start();
            try
            {
                SmartSocket.QueryVersion(port);
                garbage.Stop();
                return false;
            }
            catch (Exception)
            {
                garbage.Stop();
                return true;
            }
        });
        SelfTest.Register("Ss_Connect", delegate
        {
            using (FakeAdbServer fake = new FakeAdbServer())
            {
                fake.Start();
                bool ok = SmartSocket.SendConnect("127.0.0.1:21523", fake.Port);
                if (!ok)
                    return false;
                lock (fake.ReceivedCommands)
                {
                    foreach (string c in fake.ReceivedCommands)
                        if (c == "host:connect:127.0.0.1:21523")
                            return true;
                }
                return false;
            }
        });
    }
}
