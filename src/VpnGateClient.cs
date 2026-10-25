using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net;
using System.Text;

namespace Aloha
{

    public class VpnGateClient
    {
        public class Server
        {
            public string Host;
            public string Ip;
            public string Country;
            public string CountryShort;
            public long   Sessions;
            public long   SpeedBps;
            public int    Ping;
            public int    Score;
            public long   Uptime;
            public long   TotalUsers;
            public long   TotalTraffic;
            public string LogType;
            public string Operator;
            public string Message;
            public string OvpnBase64;

            public string Proto = "?";   
            public int    Port  = 0;

            public string SpeedMbps
            {
                get { return (SpeedBps / 1_000_000.0).ToString("0.0", CultureInfo.InvariantCulture) + " Mbps"; }
            }

            public string UptimeText
            {
                get
                {
                    if (Uptime <= 0) return "?";
                    var ts = TimeSpan.FromMilliseconds(Uptime);
                    if (ts.TotalDays >= 1) return ((int)ts.TotalDays) + "d " + ts.Hours + "h";
                    if (ts.TotalHours >= 1) return ts.Hours + "h " + ts.Minutes + "m";
                    return ts.Minutes + "m";
                }
            }

            public string TrafficText
            {
                get
                {
                    double b = TotalTraffic;
                    string[] u = { "B", "KB", "MB", "GB", "TB", "PB" };
                    int k = 0;
                    while (b >= 1024 && k < u.Length - 1) { b /= 1024; k++; }
                    return b.ToString("0.0", CultureInfo.InvariantCulture) + " " + u[k];
                }
            }
        }

        private const string ApiUrl = "https://www.vpngate.net/api/iphone/";
        private Process vpn;
        private string logPath;
        private string activeHost;
        private string lastConfigPath;
        private string lastLabel;

        public static VpnGateClient Active { get; private set; }

        public bool Connected { get { return vpn != null && !vpn.HasExited; } }
        public string ActiveHost { get { return activeHost; } }
        public string LogPath { get { return logPath; } }

        public List<Server> FetchServers(out string error)
        {
            error = null;
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                string csv;
                using (var wc = new WebClient())
                {
                    wc.Headers.Add("User-Agent", "Aloha/1.0");
                    csv = wc.DownloadString(ApiUrl);
                }
                var live = ParseCsv(csv);
                if (live.Count > 0) { TrySaveSnapshot(csv); return live; }
                error = "API returned no usable servers";
            }
            catch (Exception ex)
            {
                error = ex.Message;
            }

            string snap = TryLoadSnapshot();
            if (snap != null)
            {
                var cached = ParseCsv(snap);
                if (cached.Count > 0)
                {
                    error = "live fetch failed" + (error != null ? " (" + error + ")" : "") + " \u2014 using saved snapshot";
                    return cached;
                }
            }
            return new List<Server>();
        }

        private List<Server> ParseCsv(string csv)
        {
            var list = new List<Server>();
            if (string.IsNullOrEmpty(csv)) return list;
            foreach (var raw in csv.Split('\n'))
            {
                string line = raw.Trim();
                if (line.Length == 0 || line[0] == '*' || line[0] == '#') continue;
                if (line.StartsWith("HostName", StringComparison.OrdinalIgnoreCase)) continue;

                var c = line.Split(',');
                if (c.Length < 15) continue;
                try
                {
                    var sv = new Server
                    {
                        Host         = c[0],
                        Ip           = c[1],
                        Score        = (int)ParseLong(c[2]),
                        Ping         = (int)ParseLong(c[3]),
                        SpeedBps     = ParseLong(c[4]),
                        Country      = c[5],
                        CountryShort = c[6],
                        Sessions     = ParseLong(c[7]),
                        Uptime       = ParseLong(c[8]),
                        TotalUsers   = ParseLong(c[9]),
                        TotalTraffic = ParseLong(c[10]),
                        LogType      = c[11],
                        Operator     = c[12],
                        Message      = c[13],
                        OvpnBase64   = c[c.Length - 1]
                    };
                    DeriveProto(sv);
                    list.Add(sv);
                }
                catch {  }
            }
            return list;
        }

        private static string CacheSnapshotPath()
        {
            return Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Aloha", "vpngate_servers.csv");
        }

        private static void TrySaveSnapshot(string csv)
        {
            try
            {
                string p = CacheSnapshotPath();
                Directory.CreateDirectory(Path.GetDirectoryName(p));
                File.WriteAllText(p, csv);
            }
            catch { }
        }

        private static string TryLoadSnapshot()
        {
            try
            {
                string cache = CacheSnapshotPath();
                if (File.Exists(cache)) return File.ReadAllText(cache);
            }
            catch { }
            try
            {
                string baseDir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
                string[] bundled =
                {
                    Path.Combine(baseDir, "vpngate", "vpngate_servers.csv"),
                    Path.Combine(baseDir, "vpngate_servers.csv")
                };
                foreach (var p in bundled) if (File.Exists(p)) return File.ReadAllText(p);
            }
            catch { }
            return null;
        }

        private static long ParseLong(string s)
        {
            long v; return long.TryParse(s, NumberStyles.Any, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        private static void DeriveProto(Server s)
        {
            try
            {
                string cfg = Encoding.UTF8.GetString(Convert.FromBase64String(s.OvpnBase64));
                foreach (var raw in cfg.Split('\n'))
                {
                    string ln = raw.Trim();
                    if (ln.StartsWith("proto ", StringComparison.OrdinalIgnoreCase))
                    {
                        string p = ln.Substring(6).Trim().ToLowerInvariant();
                        if (p.StartsWith("tcp")) s.Proto = "tcp";
                        else if (p.StartsWith("udp")) s.Proto = "udp";
                    }
                    else if (ln.StartsWith("remote ", StringComparison.OrdinalIgnoreCase))
                    {
                        var parts = ln.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        if (parts.Length >= 3) { int pt; if (int.TryParse(parts[2], out pt)) s.Port = pt; }
                    }
                }
            }
            catch {  }
        }

        public string Connect(Server s)
        {
            if (s == null) return "no server selected";
            if (string.IsNullOrEmpty(s.OvpnBase64)) return "server has no OpenVPN config";

            string ovpn;
            try { ovpn = Encoding.UTF8.GetString(Convert.FromBase64String(s.OvpnBase64)); }
            catch { return "could not decode the server config"; }

            string cfgPath = Path.Combine(Path.GetTempPath(), "aloha_vpngate.ovpn");
            try { File.WriteAllText(cfgPath, ovpn); }
            catch (Exception ex) { return "could not write config: " + ex.Message; }

            return LaunchOpenVpn(cfgPath, s.Host);
        }

        public string ConnectConfigFile(string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path)) return "config file not found";
            return LaunchOpenVpn(path, Path.GetFileNameWithoutExtension(path));
        }

        public List<string> ListConfigFiles()
        {
            var outp = new List<string>();
            try
            {
                string baseDir = Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
                string[] dirs =
                {
                    Path.Combine(baseDir, "openvpn", "config"),
                    Path.Combine(baseDir, "openvpn", "config-auto")
                };
                foreach (var d in dirs)
                {
                    if (!Directory.Exists(d)) continue;
                    foreach (var f in Directory.GetFiles(d, "*.ovpn", SearchOption.AllDirectories))
                        outp.Add(f);
                }
            }
            catch { }
            outp.Sort(StringComparer.OrdinalIgnoreCase);
            return outp;
        }

        private string LaunchOpenVpn(string cfgPath, string label)
        {
            string exe = FindOpenVpn();
            if (exe == null)
                return "openvpn.exe not found (expected next to Aloha in .\\openvpn\\)";

            Disconnect();

            logPath = Path.Combine(Path.GetTempPath(), "aloha_openvpn.log");

            try
            {

                bool console = false;
                try { console = NetConfig.Load().CoreConsoleVpn; } catch { }
                var psi = console
                    ? new ProcessStartInfo
                    {
                        FileName = "cmd.exe",
                        Arguments = "/k \"\"" + exe + "\" --config \"" + cfgPath + "\" --verb 4\"",
                        UseShellExecute = true, Verb = "runas", CreateNoWindow = false,
                        WorkingDirectory = Path.GetDirectoryName(exe)
                    }
                    : new ProcessStartInfo
                    {
                        FileName = exe,
                        Arguments = "--log \"" + logPath + "\" --config \"" + cfgPath + "\"",
                        UseShellExecute = true,
                        Verb = "runas",
                        CreateNoWindow = false,
                        WorkingDirectory = Path.GetDirectoryName(exe)
                    };
                vpn = Process.Start(psi);
                ProcessLeash.Leash(vpn);
                activeHost = label;
                lastConfigPath = cfgPath;
                lastLabel = label;
                Active = this;
                return null;
            }
            catch (Exception ex)
            {

                vpn = null; activeHost = null;
                return "launch failed: " + ex.Message;
            }
        }

        public string ChangeNetworkIp(string requestedIp, out string note)
        {
            note = null;
            if (!Connected) { note = "no active VPN tunnel \u2014 connect one first"; return "not connected"; }
            if (string.IsNullOrEmpty(lastConfigPath) || !File.Exists(lastConfigPath))
            { note = "the active config is no longer on disk"; return "no config to restart"; }

            string baseCfg;
            try { baseCfg = File.ReadAllText(lastConfigPath); }
            catch (Exception ex) { note = ex.Message; return "could not read the active config"; }

            var sb = new StringBuilder();
            foreach (var raw in baseCfg.Replace("\r", "").Split('\n'))
            {
                string ln = raw.Trim();
                if (ln.StartsWith("setenv UV_REQ_IP", StringComparison.OrdinalIgnoreCase)) continue;
                if (ln.Equals("push-peer-info", StringComparison.OrdinalIgnoreCase)) continue;
                sb.AppendLine(raw);
            }
            if (!string.IsNullOrEmpty(requestedIp))
            {
                sb.AppendLine("push-peer-info");
                sb.AppendLine("setenv UV_REQ_IP " + requestedIp);
            }

            string work = Path.Combine(Path.GetTempPath(), "aloha_vpn_reip.ovpn");
            try { File.WriteAllText(work, sb.ToString()); }
            catch (Exception ex) { note = ex.Message; return "could not write the working config"; }

            string err = LaunchOpenVpn(work, lastLabel ?? "vpn");
            if (err != null) { note = err; return err; }
            note = string.IsNullOrEmpty(requestedIp)
                ? "tunnel restarting \u2014 the server reassigns your address"
                : "tunnel restarting; requested " + requestedIp + " via peer-info (your own server honours it, public exits ignore it)";
            return null;
        }

        public void Disconnect()
        {
            try { if (vpn != null && !vpn.HasExited) vpn.Kill(); }
            catch { }
            try { vpn?.Dispose(); } catch { }
            vpn = null; activeHost = null;
            if (Active == this) Active = null;
        }

        private static string FindOpenVpn()
        {
            string baseDir = Path.GetDirectoryName(
                System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
            string[] candidates =
            {
                Path.Combine(baseDir, "openvpn", "bin", "openvpn.exe"),   
                Path.Combine(baseDir, "openvpn", "openvpn.exe"),
                Path.Combine(baseDir, "openvpn.exe"),
                @"C:\Program Files\OpenVPN\bin\openvpn.exe",
                @"C:\Program Files (x86)\OpenVPN\bin\openvpn.exe"
            };
            foreach (var p in candidates) if (File.Exists(p)) return p;
            return null;
        }
    }
}
