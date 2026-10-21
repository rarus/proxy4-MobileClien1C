using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

static class Program
{
    const string StopEventName = "Local\\proxy4_MobileClien1CStop";
    const string MutexName = "Local\\proxy4_MobileClien1CMutex";

    static EventWaitHandle _stopEvent;
    static ProxyServer _proxy;
    static Config _cfg;
    static List<string> _serials = new List<string>();
    static readonly object ShutdownLock = new object();
    static bool _shutdownDone;
    static bool _lastRestoreResult;

    static int Main(string[] args)
    {
        try { Console.OutputEncoding = System.Text.Encoding.UTF8; } catch { }
        try
        {
            System.IO.StreamWriter stdout = new System.IO.StreamWriter(Console.OpenStandardOutput(), new System.Text.UTF8Encoding(false));
            stdout.AutoFlush = true;
            Console.SetOut(stdout);
        }
        catch { }
        if (args.Length > 0 && args[0] == "selftest")
        {
            TestsRewrite.Register();
            TestsIp.Register();
            TestsSmartSocket.Register();
            TestsProxy.Register();
            TestsRun.Register();
            TestsCli.Register();
            return SelfTest.RunAll(args.Length > 1 ? args[1] : null);
        }
        if (args.Length > 0 && args[0] == "stop")
            return HandleStop(args);
        if (args.Length == 0)
            return RunProxy(null);
        if (args.Length > 1 && args[0] == "run")
            return RunProxy(args[1]);
        Console.WriteLine("unknown arguments: " + string.Join(" ", args));
        Console.WriteLine("usage: proxy4_MobileClien1C.exe [run <ini>] | stop [/event:name] | selftest [filter]");
        return 1;
    }

    static int RunProxy(string iniPath)
    {
        Banner.Print();
        bool createdNew;
        string exeDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location);
        if (iniPath == null)
            iniPath = Path.Combine(exeDir, "proxy4_MobileClien1C.ini");
        _cfg = Config.Load(iniPath);
        bool scoped = !iniPath.EndsWith("proxy4_MobileClien1C.ini", StringComparison.OrdinalIgnoreCase);
        string mutexName = scoped ? MutexName + "_" + _cfg.ListenPort : MutexName;
        Mutex mutex = new Mutex(false, mutexName, out createdNew);
        if (!createdNew)
        {
            Console.WriteLine("proxy4_MobileClien1C is already running");
            return 1;
        }
        Log.Init(Path.Combine(exeDir, _cfg.LogFile));
        string stopEventName = scoped
            ? StopEventName + "_" + _cfg.ListenPort
            : StopEventName;
        _stopEvent = new EventWaitHandle(false, EventResetMode.ManualReset, stopEventName);
        Console.Title = "proxy4_MobileClien1C " + _cfg.ListenPort + "->" + _cfg.BackendPort + " mode=" + _cfg.Mode;
        Console.CancelKeyPress += delegate(object sender, ConsoleCancelEventArgs e)
        {
            e.Cancel = true;
            _stopEvent.Set();
        };
        AppDomain.CurrentDomain.ProcessExit += delegate
        {
            try { if (_cfg != null) ShutdownAndRestore(_cfg, _serials, _proxy); } catch { }
        };
        Log.Info("starting: listen=" + _cfg.ListenPort + " backend=" + _cfg.BackendPort + " mode=" + _cfg.Mode);
        try
        {
            _serials = AdbControl.MigrateExistingServer(_cfg.ListenPort, _cfg.BackendPort);
            Log.Info("migration done, devices captured: " + _serials.Count);
            if (!AdbControl.StartBackend(_cfg.BackendPort, _cfg.AdbPath))
                Log.Info("ERROR: adb backend is not available on port " + _cfg.BackendPort + " (adb.exe not found in current directory or adb_path, and no server responds); waiting for connections anyway");
            int reconnected = AdbControl.ReconnectDevices(_serials, _cfg.BackendPort);
            Log.Info("reconnected " + reconnected + "/" + _serials.Count + " device(s) to backend");
            _proxy = new ProxyServer();
            _proxy.Start(_cfg, Log.Info);
        }
        catch (System.IO.IOException ex)
        {
            Log.Info("ERROR: port " + _cfg.ListenPort + " is occupied by a non-adb process (" + ex.Message + "), exiting");
            return 2;
        }
        catch (Exception ex)
        {
            Log.Info("ERROR: startup failed: " + ex.GetType().Name + ": " + ex.Message);
            try { ShutdownAndRestore(_cfg, _serials, _proxy); } catch { }
            return 2;
        }
        Log.Info("proxying 127.0.0.1:" + _cfg.ListenPort + " -> 127.0.0.1:" + _cfg.BackendPort + ", waiting for debug commands (Ctrl+C or proxy4_MobileClien1C.exe stop to exit)");
        _stopEvent.WaitOne();
        bool restored = ShutdownAndRestore(_cfg, _serials, _proxy);
        Log.Info(restored
            ? "stopped, adb server restored on port " + _cfg.ListenPort
            : "stopped, WARNING: adb server restore failed on port " + _cfg.ListenPort + " (adb.exe not found or port busy); run any adb command to recover");
        GC.KeepAlive(mutex);
        return 0;
    }

    public static bool ShutdownAndRestore(Config cfg, List<string> serials, ProxyServer proxy)
    {
        lock (ShutdownLock)
        {
            if (_shutdownDone)
                return _lastRestoreResult;
            _shutdownDone = true;
            _lastRestoreResult = false;
            try { if (proxy != null) proxy.Stop(); } catch { }
            try { SmartSocket.SendKill(cfg.BackendPort); } catch { }
            try
            {
                if (AdbControl.StartBackend(cfg.ListenPort, cfg.AdbPath))
                {
                    AdbControl.ReconnectDevices(serials, cfg.ListenPort);
                    _lastRestoreResult = true;
                }
            }
            catch { }
            return _lastRestoreResult;
        }
    }

    static int HandleStop(string[] args)
    {
        string eventName = StopEventName;
        for (int i = 1; i < args.Length; i++)
        {
            if (args[i].StartsWith("/event:", StringComparison.OrdinalIgnoreCase))
                eventName = "Local\\" + args[i].Substring(7);
        }
        EventWaitHandle stop;
        try
        {
            stop = EventWaitHandle.OpenExisting(eventName);
        }
        catch (Exception)
        {
            Console.WriteLine("proxy4_MobileClien1C is not running");
            return 1;
        }
        stop.Set();
        Console.WriteLine("stop signal sent");
        return 0;
    }
}
