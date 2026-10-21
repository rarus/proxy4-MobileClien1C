using System;
using System.Collections.Generic;
using System.Net.Sockets;
using System.Text;
using System.Threading;

class ProxyServer : IDisposable
{
    TcpListener _listener;
    volatile bool _running;
    readonly object _connsLock = new object();
    readonly List<Conn> _conns = new List<Conn>();
    Config _cfg;
    Action<string> _log;

    public void Start(Config cfg, Action<string> log)
    {
        _cfg = cfg;
        _log = log;
        _listener = new TcpListener(System.Net.IPAddress.Loopback, cfg.ListenPort);
        _listener.Start();
        _running = true;
        Thread t = new Thread(AcceptLoop);
        t.IsBackground = true;
        t.Start();
    }

    public void Stop()
    {
        _running = false;
        try { _listener.Stop(); } catch { }
        lock (_connsLock)
        {
            foreach (Conn c in _conns)
                c.Close();
            _conns.Clear();
        }
    }

    public void Dispose() { Stop(); }

    void AcceptLoop()
    {
        while (_running)
        {
            TcpClient client;
            try { client = _listener.AcceptTcpClient(); }
            catch { break; }
            Conn conn = new Conn(this, client);
            lock (_connsLock) { _conns.Add(conn); }
            conn.Begin();
        }
    }

    void Remove(Conn conn)
    {
        lock (_connsLock) { _conns.Remove(conn); }
    }

    class Conn
    {
        readonly ProxyServer _owner;
        readonly TcpClient _client;
        NetworkStream _clientStream;
        TcpClient _backend;
        NetworkStream _backendStream;
        volatile bool _open;

        public Conn(ProxyServer owner, TcpClient client)
        {
            _owner = owner;
            _client = client;
        }

        public void Begin()
        {
            Thread t = new Thread(Run);
            t.IsBackground = true;
            t.Start();
        }

        public void Close()
        {
            _open = false;
            try { _client.Close(); } catch { }
            try { if (_backend != null) _backend.Close(); } catch { }
        }

        void Run()
        {
            _open = true;
            try
            {
                _clientStream = _client.GetStream();
                _backend = ConnectBackend();
                if (_backend == null)
                {
                    _owner._log("ERROR backend unavailable on port " + _owner._cfg.BackendPort + ", closing client connection");
                    return;
                }
                _backendStream = _backend.GetStream();
                Thread relay = new Thread(RelayBackendToClient);
                relay.IsBackground = true;
                relay.Start();
                ClientLoop();
            }
            catch { }
            finally
            {
                Close();
                _owner.Remove(this);
            }
        }

        TcpClient ConnectBackend()
        {
            TcpClient c = TryConnect();
            if (c != null)
                return c;
            AdbControl.StartBackend(_owner._cfg.BackendPort, _owner._cfg.AdbPath);
            return TryConnect();
        }

        TcpClient TryConnect()
        {
            TcpClient c = new TcpClient();
            try
            {
                c.Connect("127.0.0.1", _owner._cfg.BackendPort);
                c.NoDelay = true;
                return c;
            }
            catch
            {
                try { c.Close(); } catch { }
                return null;
            }
        }

        void RelayBackendToClient()
        {
            byte[] buf = new byte[16384];
            try
            {
                while (_open)
                {
                    int n = _backendStream.Read(buf, 0, buf.Length);
                    if (n <= 0)
                        break;
                    _clientStream.Write(buf, 0, n);
                    _clientStream.Flush();
                }
            }
            catch { }
            Close();
        }

        void ClientLoop()
        {
            while (_open)
            {
                byte[] lenBuf = new byte[4];
                if (!ReadExactly(lenBuf))
                    return;
                int len;
                try { len = Convert.ToInt32(Encoding.ASCII.GetString(lenBuf), 16); }
                catch { SendRaw(lenBuf); return; }
                if (len < 0 || len > 16 * 1024 * 1024)
                {
                    SendRaw(lenBuf);
                    return;
                }
                byte[] payload = new byte[len];
                if (!ReadExactly(payload))
                {
                    if (payload.Length > 0)
                        Forward(lenBuf, payload);
                    return;
                }
                byte[] outPayload = payload;
                bool stream = true;
                if (StartsWithShell(payload))
                {
                    if (RewriteEngine.Matches(payload))
                        outPayload = RewriteOrPassthrough(payload);
                    stream = true;
                }
                else if (StartsWithHost(payload))
                {
                    stream = false;
                }
                Forward(Encoding.ASCII.GetBytes(outPayload.Length.ToString("x4")), outPayload);
                if (stream)
                {
                    RawCopy();
                    return;
                }
            }
        }

        byte[] RewriteOrPassthrough(byte[] payload)
        {
            string ip = IpResolver.Resolve(_owner._cfg.Mode, _owner._cfg.ManualIp);
            string machine = Environment.MachineName;
            if (ip == null)
            {
                _owner._log("REWRITE-SKIP reason=no-ip-resolved mode=" + _owner._cfg.Mode);
                return payload;
            }
            byte[] result = RewriteEngine.Rewrite(payload, ip, machine);
            if (!ReferenceEquals(result, payload))
                _owner._log("REWRITE '" + machine + "' -> '" + ip + "' (mode=" + _owner._cfg.Mode + ")");
            else
                _owner._log("REWRITE-SKIP host=not-local-or-ip mode=" + _owner._cfg.Mode);
            return result;
        }

        bool StartsWithShell(byte[] payload)
        {
            return payload.Length >= 5
                && payload[0] == (byte)'s' && payload[1] == (byte)'h' && payload[2] == (byte)'e'
                && payload[3] == (byte)'l' && payload[4] == (byte)'l';
        }

        bool StartsWithHost(byte[] payload)
        {
            return payload.Length >= 5
                && payload[0] == (byte)'h' && payload[1] == (byte)'o' && payload[2] == (byte)'s'
                && payload[3] == (byte)'t' && payload[4] == (byte)':';
        }

        void Forward(byte[] prefix, byte[] payload)
        {
            _backendStream.Write(prefix, 0, prefix.Length);
            _backendStream.Write(payload, 0, payload.Length);
            _backendStream.Flush();
        }

        void SendRaw(params byte[][] blocks)
        {
            foreach (byte[] b in blocks)
                _backendStream.Write(b, 0, b.Length);
            _backendStream.Flush();
        }

        void RawCopy()
        {
            byte[] buf = new byte[16384];
            while (_open)
            {
                int n = _clientStream.Read(buf, 0, buf.Length);
                if (n <= 0)
                    return;
                _backendStream.Write(buf, 0, n);
                _backendStream.Flush();
            }
        }

        bool ReadExactly(byte[] buf)
        {
            int read = 0;
            while (read < buf.Length)
            {
                int n = _clientStream.Read(buf, read, buf.Length - read);
                if (n <= 0)
                    return false;
                read += n;
            }
            return true;
        }
    }
}
