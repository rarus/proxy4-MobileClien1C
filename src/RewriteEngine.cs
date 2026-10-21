using System;
using System.Collections.Generic;
using System.Net;
using System.Text;

static class RewriteEngine
{
    const string KeyDebuggerUrl = "/debuggerurl";
    const string KeyWs = "/ws";
    const string MarkerApp = "com.e1c.mobile.";

    public static bool Matches(byte[] payload)
    {
        if (payload == null || payload.Length < 6)
            return false;
        if (!StartsWith(payload, "shell"))
            return false;
        if (IndexOfAscii(payload, 0, ":") < 0)
            return false;
        if (IndexOfAscii(payload, 0, MarkerApp) < 0)
            return false;
        if (IndexOfAscii(payload, 0, KeyDebuggerUrl) >= 0)
            return true;
        if (IndexOfAscii(payload, 0, KeyWs) >= 0)
            return true;
        return false;
    }

    public static byte[] Rewrite(byte[] payload, string replacementHost, string localHostName)
    {
        if (payload == null || replacementHost == null || localHostName == null)
            return payload;
        byte[] repl = Encoding.ASCII.GetBytes(replacementHost);
        List<ReplaceSpan> spans = new List<ReplaceSpan>();
        FindKeyHost(payload, KeyDebuggerUrl, false, localHostName, repl, spans);
        FindKeyHost(payload, KeyWs, true, localHostName, repl, spans);
        if (spans.Count == 0)
            return payload;
        spans.Sort(delegate(ReplaceSpan a, ReplaceSpan b) { return b.Start.CompareTo(a.Start); });
        byte[] result = payload;
        foreach (ReplaceSpan sp in spans)
        {
            byte[] next = new byte[result.Length - sp.Length + repl.Length];
            Buffer.BlockCopy(result, 0, next, 0, sp.Start);
            Buffer.BlockCopy(repl, 0, next, sp.Start, repl.Length);
            Buffer.BlockCopy(result, sp.Start + sp.Length, next, sp.Start + repl.Length, result.Length - sp.Start - sp.Length);
            result = next;
        }
        return result;
    }

    class ReplaceSpan
    {
        public int Start;
        public int Length;
    }

    static void FindKeyHost(byte[] payload, string keyLowerAscii, bool skipSpacesBeforeScheme, string localHostName, byte[] repl, List<ReplaceSpan> spans)
    {
        int pos = 0;
        while (true)
        {
            int keyAt = IndexOfAscii(payload, pos, keyLowerAscii);
            if (keyAt < 0)
                return;
            pos = keyAt + keyLowerAscii.Length;
            if (keyAt > 0 && !IsWordBoundary(payload[keyAt - 1]))
                continue;
            int p = pos;
            if (skipSpacesBeforeScheme)
            {
                while (p < payload.Length && (payload[p] == (byte)' ' || payload[p] == (byte)'\t'))
                    p++;
            }
            p = SkipScheme(payload, p);
            int hostStart = p;
            int hostEnd = p;
            while (hostEnd < payload.Length && !IsHostTerminator(payload[hostEnd]))
                hostEnd++;
            if (hostEnd > hostStart)
            {
                string host = Encoding.ASCII.GetString(payload, hostStart, hostEnd - hostStart);
                bool isIp;
                IPAddress ip;
                if (IPAddress.TryParse(host, out ip))
                    isIp = true;
                else
                    isIp = false;
                if (!isIp && IsLocalName(host, localHostName))
                {
                    ReplaceSpan sp = new ReplaceSpan();
                    sp.Start = hostStart;
                    sp.Length = hostEnd - hostStart;
                    spans.Add(sp);
                }
            }
        }
    }

    static bool IsLocalName(string host, string localHostName)
    {
        if (string.Equals(host, localHostName, StringComparison.OrdinalIgnoreCase))
            return true;
        return host.Length > localHostName.Length
            && host[localHostName.Length] == '.'
            && string.Equals(host.Substring(0, localHostName.Length), localHostName, StringComparison.OrdinalIgnoreCase);
    }

    static bool IsWordBoundary(byte b)
    {
        return b == (byte)' ' || b == (byte)'\t' || b == (byte)'"' || b == (byte)'\r' || b == (byte)'\n';
    }

    static bool IsHostTerminator(byte b)
    {
        return b == (byte)':' || b == (byte)'/' || b == (byte)' ' || b == (byte)'\t' || b == (byte)'"' || b == (byte)'\r' || b == (byte)'\n';
    }

    static int SkipScheme(byte[] payload, int p)
    {
        int after = MatchAscii(payload, p, "http://");
        if (after >= 0)
            return after;
        after = MatchAscii(payload, p, "https://");
        if (after >= 0)
            return after;
        return p;
    }

    static bool StartsWith(byte[] payload, string lowerAscii)
    {
        if (payload.Length < lowerAscii.Length)
            return false;
        for (int i = 0; i < lowerAscii.Length; i++)
        {
            byte b = payload[i];
            if (b >= (byte)'A' && b <= (byte)'Z')
                b += 32;
            if (b != (byte)lowerAscii[i])
                return false;
        }
        return true;
    }

    static int MatchAscii(byte[] payload, int pos, string lowerAscii)
    {
        if (pos + lowerAscii.Length > payload.Length)
            return -1;
        for (int i = 0; i < lowerAscii.Length; i++)
        {
            byte b = payload[pos + i];
            if (b >= (byte)'A' && b <= (byte)'Z')
                b += 32;
            if (b != (byte)lowerAscii[i])
                return -1;
        }
        return pos + lowerAscii.Length;
    }

    static int IndexOfAscii(byte[] payload, int start, string lowerAscii)
    {
        if (lowerAscii.Length == 0 || payload.Length == 0)
            return -1;
        byte first = (byte)lowerAscii[0];
        int last = payload.Length - lowerAscii.Length;
        for (int i = start; i <= last; i++)
        {
            byte b = payload[i];
            if (b >= (byte)'A' && b <= (byte)'Z')
                b += 32;
            if (b != first)
                continue;
            if (MatchAscii(payload, i, lowerAscii) >= 0)
                return i;
        }
        return -1;
    }
}
