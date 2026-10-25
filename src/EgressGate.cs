using System;
using System.Collections.Generic;
using System.IO;

namespace Aloha.Net
{
    public sealed class EgressGate
    {

        public static readonly EgressGate Current = new EgressGate();

        public string ProxyEndpoint = "socks5://127.0.0.1:9050";

        private bool _enabled;
        private bool _killSwitch;
        private readonly HashSet<string> _allow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public event EventHandler StateChanged;

        public bool Enabled
        {
            get { EnsureLoaded(); return _enabled; }
            set { if (_enabled != value) { _enabled = value; Save(); Raise(); } }
        }
        public bool KillSwitch
        {
            get { EnsureLoaded(); return _killSwitch; }
            set { if (_killSwitch != value) { _killSwitch = value; Save(); Raise(); } }
        }

        public void Allow(string host) { _allow.Add(host); }
        public void ClearAllowlist() { _allow.Clear(); }
        public IEnumerable<string> Allowlist { get { return _allow; } }

        public bool ShouldAllow(string host, int port)
        {
            EnsureLoaded();
            if (IsLoopback(host)) return true;
            if (!Enabled) return true;
            if (KillSwitch) return false;
            return _allow.Contains(host);
        }

        public string BuildBrowserArguments()
        {
            return string.Join(" ", new[]
            {
                "--proxy-server=" + ProxyEndpoint,
                "--force-webrtc-ip-handling-policy=disable_non_proxied_udp", 
                "--disable-quic",                                            
            });
        }

        private static bool IsLoopback(string host)
        {
            if (string.IsNullOrEmpty(host)) return false;
            if (host == "localhost" || host == "127.0.0.1" || host == "::1" || host == "[::1]") return true;
            return host.StartsWith("127.", StringComparison.Ordinal);
        }

        private void Raise() { var h = StateChanged; if (h != null) h(this, EventArgs.Empty); }

        private bool _loaded;
        private static string CfgPath { get { return Path.Combine(Aloha.AlohaPaths.Root, "egress.cfg"); } }

        private void EnsureLoaded() { if (!_loaded) { _loaded = true; Load(); } }

        public void Load()
        {
            try
            {
                if (!File.Exists(CfgPath)) return;
                var parts = File.ReadAllText(CfgPath).Trim().Split(',');
                if (parts.Length > 0) _enabled = parts[0] == "1";
                if (parts.Length > 1) _killSwitch = parts[1] == "1";
            }
            catch { }
        }

        private void Save()
        {
            try
            {
                Directory.CreateDirectory(Aloha.AlohaPaths.Root);
                File.WriteAllText(CfgPath, (_enabled ? "1" : "0") + "," + (_killSwitch ? "1" : "0"));
            }
            catch { }
        }

    }
}
