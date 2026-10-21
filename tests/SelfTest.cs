using System;
using System.Collections.Generic;

static class SelfTest
{
    static readonly List<KeyValuePair<string, Func<bool>>> Tests = new List<KeyValuePair<string, Func<bool>>>();

    public static void Register(string name, Func<bool> fn)
    {
        Tests.Add(new KeyValuePair<string, Func<bool>>(name, fn));
    }

    public static int RunAll(string filter)
    {
        if (Tests.Count == 0)
        {
            Console.WriteLine("0 tests");
            return 0;
        }
        int failed = 0;
        int ran = 0;
        foreach (KeyValuePair<string, Func<bool>> t in Tests)
        {
            if (!string.IsNullOrEmpty(filter) && t.Key.IndexOf(filter, StringComparison.OrdinalIgnoreCase) < 0)
                continue;
            ran++;
            bool ok;
            try
            {
                ok = t.Value();
            }
            catch (Exception ex)
            {
                Console.WriteLine("ERROR " + t.Key + ": " + ex.Message);
                failed++;
                continue;
            }
            Console.WriteLine((ok ? "PASS " : "FAIL ") + t.Key);
            if (!ok)
                failed++;
        }
        Console.WriteLine(ran + "/" + Tests.Count + " tests, " + failed + " failed");
        return failed == 0 ? 0 : 1;
    }
}
