using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Text;

namespace Aloha
{

    public class CoreProxyClient
    {

        public class CoreServer
        {
            public string Protocol = "vmess";  
            public string Remark    = "";
            public string Address   = "";
            public int    Port      = 0;
            public string Id        = "";       
            public int    AlterId   = 0;
            public string Security  = "auto";   
            public string Network   = "tcp";    
            public string Tls       = "none";   
            public string Sni       = "";
            public string Host      = "";       
            public string Path      = "";       
            public string Flow      = "";       
            public string Fingerprint = "";     
            public string PublicKey = "";       
            public string ShortId   = "";       
            public string SsMethod  = "";       
            public string SsPassword= "";
            public string Raw       = "";       

            public string Display
            {
                get
                {
                    string r = string.IsNullOrEmpty(Remark) ? (Address + ":" + Port) : Remark;
                    return "[" + Protocol + "] " + r;
                }
            }

            public override string ToString() { return Display; }
        }

        private readonly string coreName;
        private Process proc;
        private string activeLabel;

        public int SocksPort { get; private set; }
        public static CoreProxyClient Active { get; private set; }
        public bool Connected { get { return proc != null && !proc.HasExited; } }
        public string ActiveLabel { get { return activeLabel; } }
        public string CoreName { get { return coreName; } }

        public event Action<string> Log;

        public CoreProxyClient(string core, int socksPort)
        {
            coreName = (core ?? "v2ray").Trim().ToLowerInvariant();
            SocksPort = socksPort > 0 ? socksPort : (coreName == "xray" ? 10809 : 10808);
        }

        private static string BaseDir
        {
            get
            {
                return Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
            }
        }
        public string CoreDir    { get { return Path.Combine(BaseDir, coreName); } }
        public string ConfigsDir { get { return Path.Combine(CoreDir, "configs"); } }
        public string UsedConfigsDir { get { return Path.Combine(CoreDir, "config", "tmp"); } }        
        public string FavouritesDir  { get { return Path.Combine(CoreDir, "config", "favourites"); } }  
        private string ExeName   { get { return coreName + ".exe"; } }

        public string SafeName(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return coreName;
            var sb = new System.Text.StringBuilder();
            foreach (char c in s.Trim())
                sb.Append(Array.IndexOf(Path.GetInvalidFileNameChars(), c) >= 0 ? '_' : c);
            string name = sb.ToString();
            if (name.Length > 80) name = name.Substring(0, 80);
            return name.Length == 0 ? coreName : name;
        }

        private string FindExe()
        {
            try
            {
                var direct = Path.Combine(CoreDir, ExeName);
                if (File.Exists(direct)) return direct;
                if (Directory.Exists(CoreDir))
                {
                    var hits = Directory.GetFiles(CoreDir, ExeName, SearchOption.AllDirectories);
                    if (hits.Length > 0) return hits[0];
                }
            }
            catch { }
            return null;
        }

        public List<CoreServer> FetchServers(string subscriptionUrl, out string error)
        {
            error = null;
            string body = null;

            if (!string.IsNullOrWhiteSpace(subscriptionUrl))
            {
                try
                {
                    ServicePointManager.SecurityProtocol =
                        SecurityProtocolType.Tls12 | SecurityProtocolType.Tls11 | SecurityProtocolType.Tls;
                    using (var wc = new WebClient())
                    {
                        wc.Encoding = Encoding.UTF8;
                        wc.Headers[HttpRequestHeader.UserAgent] = "Aloha/" + Form1.VERSION;
                        body = wc.DownloadString(subscriptionUrl);
                    }
                    TrySaveSnapshot(body);
                }
                catch (Exception ex)
                {
                    error = "subscription fetch failed: " + ex.Message + " \u2014 using last saved list";
                    body = TryLoadSnapshot();
                }
            }
            else
            {
                body = TryLoadSnapshot();
                if (body == null) error = "no subscription URL set and no saved list";
            }

            var servers = new List<CoreServer>();
            if (string.IsNullOrWhiteSpace(body))
            {
                if (error == null) error = "empty subscription";
                return servers;
            }

            foreach (var link in DecodeSubscription(body))
            {
                var s = ParseLink(link);
                if (s != null) servers.Add(s);
            }
            if (servers.Count == 0 && error == null) error = "no usable links in subscription";
            return servers;
        }

        private static IEnumerable<string> DecodeSubscription(string body)
        {
            string text = (body ?? "").Trim();
            string decoded = TryBase64(text);
            if (decoded != null) text = decoded;
            foreach (var ln in text.Replace("\r", "").Split('\n'))
            {
                var t = ln.Trim();
                if (t.Length == 0 || t[0] == '#') continue;
                bool bad = false;
                foreach (char c in t) if (c < ' ') { bad = true; break; }
                if (!bad) yield return t;
            }
        }

        public List<string> ListConfigFiles()
        {
            var outp = new List<string>();
            try
            {
                if (Directory.Exists(ConfigsDir))
                    foreach (var f in Directory.GetFiles(ConfigsDir, "*.*", SearchOption.AllDirectories))
                        outp.Add(f);
            }
            catch { }
            outp.Sort(StringComparer.OrdinalIgnoreCase);
            return outp;
        }

        public List<CoreServer> LoadBundledServers()
        {
            var servers = new List<CoreServer>();
            foreach (var f in ListConfigFiles())
            {
                if (f.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) continue;
                string content;
                try { content = File.ReadAllText(f); } catch { continue; }
                foreach (var link in DecodeSubscription(content))
                {
                    var s = ParseLink(link);
                    if (s != null) servers.Add(s);
                }
            }
            return servers;
        }

        public static CoreServer ParseLink(string link)
        {
            if (string.IsNullOrWhiteSpace(link)) return null;
            link = link.Trim();
            try
            {
                if (link.StartsWith("vmess://", StringComparison.OrdinalIgnoreCase))
                    return ParseVmess(link.Substring(8));
                if (link.StartsWith("vless://", StringComparison.OrdinalIgnoreCase))
                    return ParseUriStyle(link, "vless");
                if (link.StartsWith("trojan://", StringComparison.OrdinalIgnoreCase))
                    return ParseUriStyle(link, "trojan");
                if (link.StartsWith("ss://", StringComparison.OrdinalIgnoreCase))
                    return ParseSs(link);
            }
            catch { }
            return null;
        }

        private static CoreServer ParseVmess(string b64)
        {
            string json = TryBase64(b64);
            if (json == null) return null;
            Func<string, string> g = key => JsonField(json, key);
            var s = new CoreServer
            {
                Protocol = "vmess",
                Remark   = g("ps"),
                Address  = g("add"),
                Port     = ParseInt(g("port")),
                Id       = g("id"),
                AlterId  = ParseInt(g("aid")),
                Security = string.IsNullOrEmpty(g("scy")) ? "auto" : g("scy"),
                Network  = string.IsNullOrEmpty(g("net")) ? "tcp"  : g("net"),
                Tls      = string.IsNullOrEmpty(g("tls")) ? "none" : g("tls"),
                Sni      = g("sni"),
                Host     = g("host"),
                Path     = g("path"),
                Raw      = "vmess://" + b64
            };
            if (string.IsNullOrEmpty(s.Sni)) s.Sni = s.Host;
            return (s.Port > 0 && s.Address.Length > 0) ? s : null;
        }

        private static CoreServer ParseUriStyle(string link, string proto)
        {
            int at = link.IndexOf("://", StringComparison.Ordinal);
            string rest = link.Substring(at + 3);

            string remark = "";
            int hash = rest.IndexOf('#');
            if (hash >= 0) { remark = Uri.UnescapeDataString(rest.Substring(hash + 1)); rest = rest.Substring(0, hash); }

            string query = "";
            int q = rest.IndexOf('?');
            if (q >= 0) { query = rest.Substring(q + 1); rest = rest.Substring(0, q); }

            string id = "";
            int at2 = rest.IndexOf('@');
            if (at2 >= 0) { id = Uri.UnescapeDataString(rest.Substring(0, at2)); rest = rest.Substring(at2 + 1); }

            int slash = rest.IndexOf('/');
            if (slash >= 0) rest = rest.Substring(0, slash);
            string host = rest; int port = 0;
            int colon = rest.LastIndexOf(':');
            if (colon >= 0) { host = rest.Substring(0, colon); port = ParseInt(rest.Substring(colon + 1)); }
            if (host.StartsWith("[") && host.EndsWith("]")) host = host.Substring(1, host.Length - 2);

            var p = ParseQuery(query.Replace("&amp;", "&"));
            string net = Get(p, "type", "tcp");
            string sec = Get(p, "security", "none");
            string hostHdr = Get(p, "host", "");
            var s = new CoreServer
            {
                Protocol    = proto,
                Remark      = remark,
                Address     = host,
                Port        = port,
                Id          = id,
                Network     = net,
                Tls         = sec,
                Host        = hostHdr,
                Sni         = Get(p, "sni", hostHdr),
                Path        = Uri.UnescapeDataString(Get(p, "path", Get(p, "serviceName", ""))),
                Flow        = Get(p, "flow", ""),
                Fingerprint = Get(p, "fp", ""),
                PublicKey   = Get(p, "pbk", ""),
                ShortId     = Get(p, "sid", ""),
                Raw         = link
            };
            return (s.Port > 0 && s.Address.Length > 0) ? s : null;
        }

        private static CoreServer ParseSs(string link)
        {
            string rest = link.Substring(5);
            string remark = "";
            int hash = rest.IndexOf('#');
            if (hash >= 0) { remark = Uri.UnescapeDataString(rest.Substring(hash + 1)); rest = rest.Substring(0, hash); }

            string method = "", pass = "", host = ""; int port = 0;
            int at = rest.IndexOf('@');
            if (at >= 0)
            {
                string mp = TryBase64(rest.Substring(0, at)) ?? rest.Substring(0, at);
                string hp = rest.Substring(at + 1);
                int c = mp.IndexOf(':');
                if (c >= 0) { method = mp.Substring(0, c); pass = mp.Substring(c + 1); }
                int colon = hp.LastIndexOf(':');
                if (colon >= 0) { host = hp.Substring(0, colon); port = ParseInt(hp.Substring(colon + 1)); }
            }
            else
            {
                string all = TryBase64(rest) ?? rest;
                int at2 = all.IndexOf('@');
                if (at2 < 0) return null;
                string mp = all.Substring(0, at2);
                string hp = all.Substring(at2 + 1);
                int c = mp.IndexOf(':');
                if (c >= 0) { method = mp.Substring(0, c); pass = mp.Substring(c + 1); }
                int colon = hp.LastIndexOf(':');
                if (colon >= 0) { host = hp.Substring(0, colon); port = ParseInt(hp.Substring(colon + 1)); }
            }
            var s = new CoreServer
            {
                Protocol = "shadowsocks", Remark = remark,
                Address = host, Port = port,
                SsMethod = method, SsPassword = pass, Raw = link
            };
            return (s.Port > 0 && s.Address.Length > 0) ? s : null;
        }

        public string BuildConfigJson(CoreServer s) { return BuildConfigJson(s, false); }

        public string BuildConfigJson(CoreServer s, bool withRules)
        {
            string inbound = "{ \"listen\": \"127.0.0.1\", \"port\": " + SocksPort
                + ", \"protocol\": \"socks\", \"settings\": { \"udp\": true, \"auth\": \"noauth\" } }";

            var outs = new List<string> { BuildOutbound(s, withRules ? "proxy" : null) };
            if (withRules)
            {
                outs.Add("{ \"protocol\": \"freedom\", \"tag\": \"direct\" }");
                outs.Add("{ \"protocol\": \"blackhole\", \"tag\": \"block\" }");
            }

            var sb = new StringBuilder();
            sb.Append("{\n  \"inbounds\": [").Append(inbound).Append("],\n");
            sb.Append("  \"outbounds\": [").Append(string.Join(", ", outs)).Append("]");
            if (withRules) sb.Append(",\n  \"routing\": ").Append(BuildRouting());
            sb.Append("\n}\n");
            return sb.ToString();
        }

        private static string BuildRouting()
        {
            return "{ \"domainStrategy\": \"IPIfNonMatch\", \"rules\": ["
                + "{ \"type\": \"field\", \"outboundTag\": \"block\", \"domain\": [\"geosite:category-ads-all\", \"geosite:malware\", \"geosite:phishing\"] }, "
                + "{ \"type\": \"field\", \"outboundTag\": \"direct\", \"domain\": [\"geosite:ir\"] }, "
                + "{ \"type\": \"field\", \"outboundTag\": \"direct\", \"ip\": [\"geoip:ir\", \"geoip:private\"] }"
                + "] }";
        }

        private string BuildOutbound(CoreServer s, string tag)
        {
            var parts = new List<string>();
            if (!string.IsNullOrEmpty(tag)) parts.Add("\"tag\": " + Js(tag));
            if (s.Protocol == "vmess" || s.Protocol == "vless")
            {
                var user = new List<string> { "\"id\": " + Js(s.Id) };
                if (s.Protocol == "vmess")
                {
                    user.Add("\"alterId\": " + s.AlterId);
                    user.Add("\"security\": " + Js(string.IsNullOrEmpty(s.Security) ? "auto" : s.Security));
                }
                else
                {
                    user.Add("\"encryption\": \"none\"");
                    if (!string.IsNullOrEmpty(s.Flow)) user.Add("\"flow\": " + Js(s.Flow));
                }
                string vnext = "{ \"address\": " + Js(s.Address) + ", \"port\": " + s.Port
                    + ", \"users\": [{ " + string.Join(", ", user) + " }] }";
                parts.Add("\"protocol\": " + Js(s.Protocol));
                parts.Add("\"settings\": { \"vnext\": [" + vnext + "] }");
            }
            else if (s.Protocol == "trojan")
            {
                string srv = "{ \"address\": " + Js(s.Address) + ", \"port\": " + s.Port
                    + ", \"password\": " + Js(s.Id) + " }";
                parts.Add("\"protocol\": \"trojan\"");
                parts.Add("\"settings\": { \"servers\": [" + srv + "] }");
            }
            else
            {
                string srv = "{ \"address\": " + Js(s.Address) + ", \"port\": " + s.Port
                    + ", \"method\": " + Js(s.SsMethod) + ", \"password\": " + Js(s.SsPassword) + " }";
                parts.Add("\"protocol\": \"shadowsocks\"");
                parts.Add("\"settings\": { \"servers\": [" + srv + "] }");
            }
            parts.Add("\"streamSettings\": " + BuildStream(s));
            return "{ " + string.Join(", ", parts) + " }";
        }

        private string BuildStream(CoreServer s)
        {
            string net = string.IsNullOrEmpty(s.Network) ? "tcp" : s.Network;
            string sec = (string.IsNullOrEmpty(s.Tls) || s.Tls == "none") ? "none" : s.Tls;

            var parts = new List<string>
            {
                "\"network\": " + Js(net),
                "\"security\": " + Js(sec)
            };

            if (sec == "tls")
            {
                var t = new List<string> { "\"serverName\": " + Js(string.IsNullOrEmpty(s.Sni) ? s.Address : s.Sni) };
                if (!string.IsNullOrEmpty(s.Fingerprint)) t.Add("\"fingerprint\": " + Js(s.Fingerprint));
                t.Add("\"allowInsecure\": false");
                parts.Add("\"tlsSettings\": { " + string.Join(", ", t) + " }");
            }
            else if (sec == "reality")
            {
                var t = new List<string> { "\"serverName\": " + Js(string.IsNullOrEmpty(s.Sni) ? s.Address : s.Sni) };
                if (!string.IsNullOrEmpty(s.Fingerprint)) t.Add("\"fingerprint\": " + Js(s.Fingerprint));
                if (!string.IsNullOrEmpty(s.PublicKey))   t.Add("\"publicKey\": " + Js(s.PublicKey));
                if (!string.IsNullOrEmpty(s.ShortId))     t.Add("\"shortId\": " + Js(s.ShortId));
                parts.Add("\"realitySettings\": { " + string.Join(", ", t) + " }");
            }

            if (net == "ws")
                parts.Add("\"wsSettings\": { \"path\": " + Js(string.IsNullOrEmpty(s.Path) ? "/" : s.Path)
                    + ", \"headers\": { \"Host\": " + Js(string.IsNullOrEmpty(s.Host) ? s.Address : s.Host) + " } }");
            else if (net == "grpc")
                parts.Add("\"grpcSettings\": { \"serviceName\": " + Js(s.Path) + " }");
            else if (net == "h2")
                parts.Add("\"httpSettings\": { \"path\": " + Js(string.IsNullOrEmpty(s.Path) ? "/" : s.Path)
                    + ", \"host\": [" + Js(string.IsNullOrEmpty(s.Host) ? s.Address : s.Host) + "] }");

            return "{ " + string.Join(", ", parts) + " }";
        }

        public string Connect(CoreServer s, bool withRules, out string configPath)
        {
            configPath = null;
            if (s == null) return "no server selected";
            string json = BuildConfigJson(s, withRules);
            string path = Path.Combine(UsedConfigsDir, SafeName(s.Display) + ".json");
            try { Directory.CreateDirectory(UsedConfigsDir); File.WriteAllText(path, json); }
            catch (Exception ex) { return "could not write config: " + ex.Message; }
            configPath = path;
            return Launch(path, s.Display);
        }

        public string ConnectRaw(string json, string label, out string configPath)
        {
            configPath = null;
            if (string.IsNullOrWhiteSpace(json)) return "empty config";
            string path = Path.Combine(UsedConfigsDir, SafeName(label) + ".json");
            try { Directory.CreateDirectory(UsedConfigsDir); File.WriteAllText(path, json); }
            catch (Exception ex) { return "could not write config: " + ex.Message; }
            configPath = path;
            return Launch(path, string.IsNullOrEmpty(label) ? coreName : label);
        }

        public string ConnectConfigFile(string jsonPath)
        {
            if (string.IsNullOrEmpty(jsonPath) || !File.Exists(jsonPath)) return "config file not found";
            return Launch(jsonPath, Path.GetFileNameWithoutExtension(jsonPath));
        }

        private string Launch(string configPath, string label)
        {
            string exe = FindExe();
            if (exe == null)
                return ExeName + " not found (expected in .\\" + coreName + "\\)";

            Disconnect();
            try
            {

                bool console = false;
                try { console = NetConfig.Load().CoreConsoleProxy; } catch { }
                var psi = new ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "run -c \"" + configPath + "\"",   
                    UseShellExecute = false,
                    CreateNoWindow = !console,
                    RedirectStandardOutput = !console,
                    RedirectStandardError = !console,
                    WindowStyle = console ? ProcessWindowStyle.Normal : ProcessWindowStyle.Hidden,
                    WorkingDirectory = Path.GetDirectoryName(exe)
                };

                psi.EnvironmentVariables["XRAY_LOCATION_ASSET"] = CoreDir;
                psi.EnvironmentVariables["V2RAY_LOCATION_ASSET"] = CoreDir;
                proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
                if (!console)
                {
                    proc.OutputDataReceived += (o, e) => { if (e.Data != null) RaiseLog(e.Data); };
                    proc.ErrorDataReceived  += (o, e) => { if (e.Data != null) RaiseLog(e.Data); };
                }
                proc.Start();
                if (!console) { proc.BeginOutputReadLine(); proc.BeginErrorReadLine(); }
                ProcessLeash.Leash(proc);
                activeLabel = label;
                Active = this;
                RaiseLog(coreName + " started \u2192 SOCKS 127.0.0.1:" + SocksPort + "  (" + label + ")");
                return null;
            }
            catch (Exception ex)
            {
                proc = null; activeLabel = null;
                return "launch failed: " + ex.Message;
            }
        }

        public void Disconnect()
        {
            try { if (proc != null && !proc.HasExited) { proc.Kill(); proc.WaitForExit(1500); } }
            catch { }
            if (proc != null) { try { proc.Dispose(); } catch { } }
            proc = null;
            activeLabel = null;
            if (Active == this) Active = null;
        }

        private void RaiseLog(string line) { var h = Log; if (h != null) h(line); }

        private static string Js(string s)
        {
            if (s == null) s = "";
            var sb = new StringBuilder("\"");
            foreach (char c in s)
            {
                switch (c)
                {
                    case '\"': sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            sb.Append("\"");
            return sb.ToString();
        }

        private static int ParseInt(string s)
        {
            int v; return int.TryParse((s ?? "").Trim(), out v) ? v : 0;
        }

        private static string Get(Dictionary<string, string> d, string key, string def)
        {
            string v;
            return (d.TryGetValue(key, out v) && !string.IsNullOrEmpty(v)) ? v : def;
        }

        private static string JsonField(string json, string key)
        {
            string pat = "\"" + key + "\"";
            int i = json.IndexOf(pat, StringComparison.Ordinal);
            if (i < 0) return "";
            i = json.IndexOf(':', i + pat.Length);
            if (i < 0) return "";
            i++;
            while (i < json.Length && (json[i] == ' ' || json[i] == '\t')) i++;
            if (i >= json.Length) return "";
            if (json[i] == '"')
            {
                int end = json.IndexOf('"', i + 1);
                return end < 0 ? "" : json.Substring(i + 1, end - i - 1);
            }
            int e2 = i;
            while (e2 < json.Length && json[e2] != ',' && json[e2] != '}' && json[e2] != '\r' && json[e2] != '\n') e2++;
            return json.Substring(i, e2 - i).Trim();
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(query)) return d;
            foreach (var kv in query.Split('&'))
            {
                if (kv.Length == 0) continue;
                int eq = kv.IndexOf('=');
                if (eq < 0) d[Uri.UnescapeDataString(kv)] = "";
                else d[Uri.UnescapeDataString(kv.Substring(0, eq))] = Uri.UnescapeDataString(kv.Substring(eq + 1));
            }
            return d;
        }

        private static string TryBase64(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;

            var tb = new StringBuilder(s.Length);
            foreach (char ch in s) if (!char.IsWhiteSpace(ch)) tb.Append(ch == '-' ? '+' : ch == '_' ? '/' : ch);
            string t = tb.ToString();
            switch (t.Length % 4) { case 2: t += "=="; break; case 3: t += "="; break; case 1: return null; }
            try
            {
                var text = Encoding.UTF8.GetString(Convert.FromBase64String(t));

                int ctl = 0;
                foreach (char c in text) if (c < ' ' && c != '\r' && c != '\n' && c != '\t') ctl++;
                if (ctl > 8 && ctl * 50 > text.Length) return null;
                return text;
            }
            catch { return null; }
        }

        private string SnapshotPath() { return Path.Combine(Path.GetTempPath(), "aloha_" + coreName + "_sub.txt"); }
        private void TrySaveSnapshot(string body) { try { File.WriteAllText(SnapshotPath(), body ?? ""); } catch { } }
        private string TryLoadSnapshot() { try { return File.Exists(SnapshotPath()) ? File.ReadAllText(SnapshotPath()) : null; } catch { return null; } }
    }
}
