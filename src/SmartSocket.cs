using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Sockets;
using System.Text;

static class SmartSocket
{
    const int TimeoutMs = 2000;

    public static int QueryVersion(int port)
    {
        using (TcpClient c = Connect(port))
        using (NetworkStream s = c.GetStream())
        {
            byte[] payload = Encoding.ASCII.GetBytes("host:version");
            Write(s, Prefix(payload), payload);
            string status = ReadStatus(s);
            if (status != "OKAY")
                throw new IOException("not an adb server: " + status + " on port " + port);
            byte[] body = ReadPacket(s);
            if (body == null)
                throw new IOException("no version response on port " + port);
            return Convert.ToInt32(Encoding.ASCII.GetString(body).Trim(), 16);
        }
    }

    public static List<string> QueryDevices(int port)
    {
        List<string> serials = new List<string>();
        using (TcpClient c = Connect(port))
        using (NetworkStream s = c.GetStream())
        {
            byte[] payload = Encoding.ASCII.GetBytes("host:devices");
            Write(s, Prefix(payload), payload);
            string status = ReadStatus(s);
            if (status != "OKAY")
                throw new IOException("host:devices failed: " + status);
            byte[] body = ReadPacket(s);
            if (body == null)
                return serials;
            foreach (string line in Encoding.ASCII.GetString(body).Split(new char[] { '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string[] parts = line.Split('\t');
                if (parts.Length >= 1 && parts[0].Length > 0)
                    serials.Add(parts[0]);
            }
        }
        return serials;
    }

    public static bool SendConnect(string serial, int port)
    {
        using (TcpClient c = Connect(port))
        using (NetworkStream s = c.GetStream())
        {
            byte[] payload = Encoding.ASCII.GetBytes("host:connect:" + serial);
            Write(s, Prefix(payload), payload);
            return ReadStatus(s) == "OKAY";
        }
    }

    public static void SendKill(int port)
    {
        using (TcpClient c = Connect(port))
        using (NetworkStream s = c.GetStream())
        {
            byte[] payload = Encoding.ASCII.GetBytes("host:kill");
            Write(s, Prefix(payload), payload);
            try { ReadStatus(s); } catch (IOException) { }
        }
    }

    static TcpClient Connect(int port)
    {
        TcpClient c = new TcpClient();
        try
        {
            c.Connect("127.0.0.1", port);
            c.SendTimeout = TimeoutMs;
            c.ReceiveTimeout = TimeoutMs;
            return c;
        }
        catch (Exception)
        {
            c.Close();
            throw new IOException("cannot connect to adb server port " + port);
        }
    }

    static byte[] Prefix(byte[] payload)
    {
        return Encoding.ASCII.GetBytes(payload.Length.ToString("x4"));
    }

    static void Write(NetworkStream s, params byte[][] blocks)
    {
        foreach (byte[] b in blocks)
            s.Write(b, 0, b.Length);
        s.Flush();
    }

    static string ReadStatus(NetworkStream s)
    {
        byte[] buf = new byte[4];
        if (!ReadExactly(s, buf, 4))
            throw new IOException("no status response");
        return Encoding.ASCII.GetString(buf);
    }

    static byte[] ReadPacket(NetworkStream s)
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

    static bool ReadExactly(NetworkStream s, byte[] buf, int count)
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
}
