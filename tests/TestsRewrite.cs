using System;
using System.Text;

static class TestsRewrite
{
    static byte[] B(string s) { return Encoding.UTF8.GetBytes(s); }
    static string S(byte[] b) { return Encoding.UTF8.GetString(b); }

    const string RealCommand = "shell:am broadcast -n com.e1c.mobile.client/com.e1c.mobile.Starter -a com.e1c.mobile.START_CMD --es cmd \"ENTERPRISE /IBNameTestBase /O Normal /AppAutoCheckMode /AppAutoCheckVersion+ /UseHwLicenses+ /TComp -SDC /Lru /VLru /DisableStartupMessages /DisplayPerformance /TechnicalSpecialistMode /EnableCheckModal /EnableCheckExtensionsAndAddInsSyncCalls /DEBUG -http -attach /DEBUGGERURLhttp://PC-NAME:1560 /WS http://PC-NAME:8080/MKClient\"";

    public static void Register()
    {
        SelfTest.Register("Rw_MatchesRealCommand", delegate
        {
            return RewriteEngine.Matches(B(RealCommand));
        });
        SelfTest.Register("Rw_NoMatchWithoutMarkers", delegate
        {
            if (RewriteEngine.Matches(B("shell:am start -n com.android.settings/.Settings")))
                return false;
            if (RewriteEngine.Matches(B("host:devices")))
                return false;
            return true;
        });
        SelfTest.Register("Rw_ReplacesBothHosts", delegate
        {
            byte[] res = RewriteEngine.Rewrite(B(RealCommand), "192.168.168.168", "PC-NAME");
            string s = S(res);
            return s.Contains("/DEBUGGERURLhttp://192.168.168.168:1560")
                && s.Contains("/WS http://192.168.168.168:8080/MKClient")
                && !s.Contains("PC-NAME")
                && s.Contains("/IBNameTestBase");
        });
        SelfTest.Register("Rw_IpHostUntouched", delegate
        {
            byte[] src = B("shell:am broadcast -n com.e1c.mobile.client/x --es cmd \"x /DEBUGGERURLhttp://10.0.2.2:1560 /WS http://127.0.0.1:8080/a\"");
            byte[] res = RewriteEngine.Rewrite(src, "192.168.168.168", "PC-NAME");
            return ReferenceEquals(res, src);
        });
        SelfTest.Register("Rw_ForeignNameUntouched", delegate
        {
            byte[] src = B("shell:am broadcast -n com.e1c.mobile.client/x --es cmd \"x /DEBUGGERURLhttp://other-pc:1560 /WS http://server01:8080/a\"");
            byte[] res = RewriteEngine.Rewrite(src, "192.168.168.168", "PC-NAME");
            return ReferenceEquals(res, src);
        });
        SelfTest.Register("Rw_DifferentHostsPerKey", delegate
        {
            byte[] res = RewriteEngine.Rewrite(
                B("shell:am broadcast -n com.e1c.mobile.client/x --es cmd \"x /DEBUGGERURLhttp://PC-NAME:1560 /WS http://other-pc:8080/a\""),
                "192.168.168.168", "PC-NAME");
            string s = S(res);
            return s.Contains("/DEBUGGERURLhttp://192.168.168.168:1560") && s.Contains("/WS http://other-pc:8080/a");
        });
        SelfTest.Register("Rw_CaseInsensitiveAndFqdn", delegate
        {
            byte[] res = RewriteEngine.Rewrite(
                B("shell:am broadcast -n com.e1c.mobile.client/x --es cmd \"x /DEBUGGERURLhttp://PC-NAME:1560 /WS http://PC-NAME.corp.local:8080/a\""),
                "10.1.1.1", "PC-NAME");
            string s = S(res);
            return s.Contains("/DEBUGGERURLhttp://10.1.1.1:1560") && s.Contains("/WS http://10.1.1.1:8080/a");
        });
        SelfTest.Register("Rw_CyrillicLengthSafety", delegate
        {
            byte[] src = B(RealCommand);
            string repl = "192.168.168.168";
            byte[] res = RewriteEngine.Rewrite(src, repl, "PC-NAME");
            int expected = src.Length - 2 * Encoding.UTF8.GetBytes("PC-NAME").Length + 2 * Encoding.UTF8.GetBytes(repl).Length;
            return res.Length == expected && S(res).Contains("TestBase");
        });
        SelfTest.Register("Rw_WsNoScheme", delegate
        {
            byte[] res = RewriteEngine.Rewrite(
                B("shell:am broadcast -n com.e1c.mobile.client/x --es cmd \"x /WS PC-NAME:8080/a\""),
                "10.5.5.5", "PC-NAME");
            return S(res).Contains("/WS 10.5.5.5:8080/a") || S(res).Contains("/WS " + "10.5.5.5" + ":8080/a");
        });
    }
}
