using System;
using System.IO;
using System.Text;

static class Log
{
    static readonly object Sync = new object();
    static string _path;

    public static void Init(string path)
    {
        lock (Sync)
        {
            _path = path;
            try
            {
                string dir = Path.GetDirectoryName(Path.GetFullPath(path));
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.AppendAllText(path, "=== proxy4_MobileClien1C started " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " ===" + Environment.NewLine, new UTF8Encoding(true));
            }
            catch { _path = null; }
        }
    }

    public static void Info(string message)
    {
        string line = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + " " + message;
        lock (Sync)
        {
            Console.WriteLine(line);
            if (_path != null)
            {
                try { File.AppendAllText(_path, line + Environment.NewLine, Encoding.UTF8); }
                catch { }
            }
        }
    }
}
