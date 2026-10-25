using System;
using System.IO;
using System.Text;
using System.Security.Cryptography;
using Microsoft.Web.WebView2.Core;

namespace Aloha.Net
{
    public static class BridgeGuard
    {

        private static readonly string[] TrustedOrigins =
        {
            "https://config.aloha",
            "https://devtools.aloha",
            "https://home.aloha",
        };

        public static event EventHandler StateChanged;

        private static bool _enabled = true;
        private static bool _loaded;

        public static bool Enabled
        {
            get { EnsureLoaded(); return _enabled; }
            set { EnsureLoaded(); if (_enabled != value) { _enabled = value; Save(); var h = StateChanged; if (h != null) h(null, EventArgs.Empty); } }
        }

        public static string SessionToken { get; } = NewToken();

        public static bool IsTrustedOrigin(string source)
        {
            if (string.IsNullOrEmpty(source)) return false;
            foreach (var o in TrustedOrigins)
                if (source.StartsWith(o, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        public static bool Authorize(CoreWebView2WebMessageReceivedEventArgs e)
        {
            if (!Enabled) return true;
            try { return IsTrustedOrigin(e != null ? e.Source : null); }
            catch { return false; }
        }

        private static string NewToken()
        {
            var b = new byte[32];
            using (var r = RandomNumberGenerator.Create()) r.GetBytes(b);
            var sb = new StringBuilder(64);
            foreach (var x in b) sb.Append(x.ToString("x2"));
            return sb.ToString();
        }

        private static string CfgPath { get { return Path.Combine(Aloha.AlohaPaths.Root, "bridgeguard.cfg"); } }
        private static void EnsureLoaded() { if (!_loaded) { _loaded = true; Load(); } }
        private static void Load()
        {
            try { if (File.Exists(CfgPath)) _enabled = File.ReadAllText(CfgPath).Trim() != "0"; }
            catch { }
        }
        private static void Save()
        {
            try { Directory.CreateDirectory(Aloha.AlohaPaths.Root); File.WriteAllText(CfgPath, _enabled ? "1" : "0"); }
            catch { }
        }
    }
}
