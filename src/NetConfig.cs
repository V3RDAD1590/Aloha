using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Reflection;

namespace Aloha
{
    [DataContract]
    public sealed class NetConfig
    {

        [DataMember] public string ProxyScheme;
        [DataMember] public string ProxyHost;
        [DataMember] public int    ProxyPort;
        [DataMember] public string BypassList;

        [DataMember] public string ProxyMode;

        [DataMember] public string ProxyHttp;
        [DataMember] public string ProxyHttps;
        [DataMember] public string ProxySocks;
        [DataMember] public string ProxySocksVersion;

        [DataMember] public string ProxyPacUrl;

        [DataMember] public bool   ProxyProxyDns;

        [DataMember] public string ProxyAuthUser;
        [DataMember] public string ProxyAuthPass;
        [DataMember] public string ProfileName;

        [DataMember] public string DnsMode;
        [DataMember] public bool   DohEnabled;
        [DataMember] public string DohEndpoint;
        [DataMember] public string HostResolverRules;

        [DataMember] public int InterceptPort;

        [DataMember] public string UserAgent;
        [DataMember] public string Wallpaper;
        [DataMember] public bool   WallpaperEnabled;

        [DataMember] public string SearchTemplate;
        public const string SearchStartpage      = "https://www.startpage.com/sp/search?q={q}";
        public const string SearchStartpageOnion = "http://startpagel6srwcjlue4zgq3zevrujfaow726kjytqbbjyrswwmjzcqd.onion/sp/search?q={q}";

        [DataMember] public bool CfgWebRtcLeakFix;
        [DataMember] public bool CfgWebRtcDisableFully;
        [DataMember] public bool CfgDisableQuic;
        [DataMember] public bool CfgDisableBackgroundNet;
        [DataMember] public bool CfgDisableComponentUpdate;
        [DataMember] public bool CfgDisableDnsPrefetch;
        [DataMember] public bool CfgDisableDomainReliability;
        [DataMember] public bool CfgDisableHyperlinkAudit;

        [DataMember] public bool CoreConsole;

        [DataMember] public bool CoreConsoleTor;
        [DataMember] public bool CoreConsoleVpn;
        [DataMember] public bool CoreConsoleProxy;
        [DataMember] public bool CfgDisableMediaDevices;
        [DataMember] public bool CfgDisablePreconnect;
        [DataMember] public bool CfgDisableSafeBrowsing;
        [DataMember] public bool CfgDisableSync;
        [DataMember] public bool CfgReduceReferrers;

        [DataMember] public bool CfgBlockAllPopups;

        [DataMember] public bool CfgDisableWindowOpen;

        [DataMember] public bool CfgBackgroundNewTabs;
        [DataMember] public bool CfgUseInterceptProxy;
        [DataMember] public bool CfgWin9xBrowserScrollbars;

        [DataMember] public bool CfgUrlHexStrip;

        [DataMember] public bool CfgLoadFavicons;

        [DataMember] public bool CfgMediaCard;
        [DataMember] public bool CfgAllowFridaSandboxOff;
        [DataMember] public bool CfgEmbeddedDevTools;

        [DataMember(Name = "Ipv7Mode")]            public bool   SealedTor;            
        [DataMember(Name = "Ipv7ControlPort")]     public int    SealedTorControlPort; 
        [DataMember(Name = "Ipv7ControlPassword")] public string SealedTorControlPassword; 
        [DataMember(Name = "Ipv7TorrcDir")]        public string SealedTorDir;         
        [DataMember(Name = "Ipv7Console")]         public bool   SealedTorConsole;     
        [DataMember(Name = "Ipv7SharedCircuits")]  public bool   SealedTorSharedCircuits; 
        [DataMember(Name = "Ipv7ExitDefault")]     public string SealedTorExit;        
        public const int SealedTorSocksPort = 9070;

        [DataMember] public int    Ipv7GatewayPort;
        [DataMember] public string Ipv7ClientExe;
        [DataMember(Name = "Ipv7PythonExe")] public string PythonExe;   

        [DataMember] public string DeepExe;
        [DataMember] public string DeepConfig;
        [DataMember] public int    DeepTimeoutSec;

        [DataMember] public bool CaravanWipeTmpOnStart;

        [DataMember] public string ExtraSwitches;

        [DataMember] public string ConfigOverridesJson;

        [DataMember] public System.Collections.Generic.List<string> FavouriteFlags;

        public const int EmbeddedDevToolsPort = 9333;

        [DataMember] public bool CfgSnapViewportToGrid;
        [DataMember] public bool CfgTorLetterbox;
        [DataMember] public int  WinWidth;
        [DataMember] public int  WinHeight;
        [DataMember] public double WebZoom;

        [DataMember] public int  TrackingPrevention;
        [DataMember] public bool DisableAutofill;
        [DataMember] public bool DisablePasswordSave;
        [DataMember] public bool DisableSmartScreen;
        [DataMember] public bool DisableDevTools;
        [DataMember] public bool DisableJavaScript;

        [DataMember] public bool   BlockDownloads;
        [DataMember] public bool   ConfirmDownloads;
        [DataMember] public bool   HideDownloadPopup;
        [DataMember] public bool   CfgDownloadToast;
        [DataMember] public string DownloadDir;

        [DataMember] public bool ShowBookmarkButton;

        [DataMember] public bool EnableFridaTracing;
        [DataMember] public int  FridaSampleRate;
        [DataMember] public bool EnableNmapScanning;
        [DataMember] public string NmapDefaultTarget;
        [DataMember] public bool EnablePrimarch;

        public NetConfig() { SetDefaults(); }

        [OnDeserializing]
        private void OnDeserializing(StreamingContext ctx) { SetDefaults(); }

        private void SetDefaults()
        {
            ProxyScheme = "none"; ProxyHost = "127.0.0.1"; ProxyPort = 9050;
            BypassList  = "localhost,127.0.0.1"; ProfileName = "default";
            ProxyMode = "single";
            ProxyHttp = ""; ProxyHttps = ""; ProxySocks = ""; ProxySocksVersion = "5";
            ProxyPacUrl = ""; ProxyProxyDns = false;
            ProxyAuthUser = ""; ProxyAuthPass = "";

            DnsMode = "remote"; DohEnabled = false;
            DohEndpoint = "https://cloudflare-dns.com/dns-query"; HostResolverRules = "";

            CoreConsole = false; CoreConsoleTor = false; CoreConsoleProxy = false;
            CoreConsoleVpn = true;

            InterceptPort = 8889;

            SealedTorControlPort = 9071;
            SealedTorControlPassword = ""; 
            SealedTorDir = "";             
            SealedTorConsole = false;
            SealedTorSharedCircuits = false;
            SealedTorExit = "";
            Ipv7GatewayPort = 8888;
            Ipv7ClientExe = "";
            PythonExe = "";
            DeepExe = "";
            DeepConfig = "";
            DeepTimeoutSec = 35;
            CaravanWipeTmpOnStart = false;
            ExtraSwitches = "";
            ConfigOverridesJson = "";      
            FavouriteFlags = new List<string>();
            WebZoom = 1.0;

            UserAgent = ""; Wallpaper = ""; WallpaperEnabled = true;
            SearchTemplate = "";   

            CfgWebRtcLeakFix = true;  CfgWebRtcDisableFully = false; CfgDisableQuic = false;
            CfgDisableBackgroundNet = true; CfgDisableComponentUpdate = false; CfgDisableDnsPrefetch = true;
            CfgDisableDomainReliability = true; CfgDisableHyperlinkAudit = true; CfgDisableMediaDevices = false;
            CfgDisablePreconnect = true; CfgDisableSafeBrowsing = false; CfgDisableSync = true;
            CfgReduceReferrers = true; CfgUseInterceptProxy = false; CfgWin9xBrowserScrollbars = false;
            CfgBlockAllPopups = false;
            CfgDisableWindowOpen = false;
            CfgBackgroundNewTabs = false;
            CfgUrlHexStrip = true;
            CfgLoadFavicons = false;
            CfgMediaCard = true;

            TrackingPrevention = 2; DisableAutofill = false; DisablePasswordSave = true;
            DisableSmartScreen = false; DisableDevTools = false; DisableJavaScript = false;

            BlockDownloads = false;
            ConfirmDownloads = false;
            HideDownloadPopup = false;
            CfgDownloadToast = true;
            DownloadDir = "";

            ShowBookmarkButton = true;

            EnableFridaTracing = false;
            FridaSampleRate = 10;
            EnableNmapScanning = false;
            NmapDefaultTarget = "192.168.1.0/24";
            EnablePrimarch = false;
        }

        static bool Has(string s) { return !string.IsNullOrWhiteSpace(s); }

        public string BuildBrowserArguments()
        {
            var a = new List<string>();

            if (CfgWebRtcDisableFully)
                a.Add("--force-webrtc-ip-handling-policy=disable_non_proxied_udp");
            else if (CfgWebRtcLeakFix)
                a.Add("--force-webrtc-ip-handling-policy=default_public_interface_only");

            if (CfgDisableQuic)              a.Add("--disable-quic");
            if (CfgDisableBackgroundNet)     a.Add("--disable-background-networking");
            if (CfgDisableComponentUpdate)   a.Add("--disable-component-update");
            if (CfgDisableDnsPrefetch)       a.Add("--dns-prefetch-disable");
            if (CfgDisableDomainReliability) a.Add("--disable-domain-reliability");
            if (CfgDisableHyperlinkAudit)    a.Add("--no-pings");
            if (CfgDisableSync)              a.Add("--disable-sync");
            if (CfgReduceReferrers)          a.Add("--no-referrers"); 

            if (SealedTor)
            {

                a.Add("--proxy-server=socks5://127.0.0.1:" + SealedTorSocksPort.ToString(CultureInfo.InvariantCulture));
            }
            else if (CfgUseInterceptProxy)
            {
                a.Add("--proxy-server=http://127.0.0.1:" + InterceptPort.ToString(CultureInfo.InvariantCulture));
            }
            else
            {

                string pm = string.IsNullOrWhiteSpace(ProxyMode) ? "single" : ProxyMode.ToLowerInvariant();
                switch (pm)
                {
                    case "direct":
                        a.Add("--no-proxy-server");
                        break;

                    case "system":

                        break;

                    case "auto":
                        a.Add("--proxy-auto-detect");
                        break;

                    case "pac":
                        if (Has(ProxyPacUrl)) a.Add("--proxy-pac-url=" + ProxyPacUrl.Trim());
                        break;

                    case "perscheme":
                    {

                        var parts = new List<string>();
                        if (Has(ProxyHttp))  parts.Add("http="  + ProxyHttp.Trim());
                        if (Has(ProxyHttps)) parts.Add("https=" + ProxyHttps.Trim());
                        if (Has(ProxySocks))
                        {
                            string sp = string.Equals(ProxySocksVersion, "4", StringComparison.Ordinal)
                                      ? "socks4://" : "socks5://";
                            parts.Add("socks=" + sp + ProxySocks.Trim());
                        }
                        if (parts.Count > 0)
                            a.Add("--proxy-server=" + string.Join(";", parts.ToArray()));
                        break;
                    }

                    default:
                        if (!string.Equals(ProxyScheme, "none", StringComparison.OrdinalIgnoreCase) && Has(ProxyHost))
                        {
                            string scheme = string.Equals(ProxyScheme, "socks5h", StringComparison.OrdinalIgnoreCase)
                                          ? "socks5" : ProxyScheme;
                            a.Add("--proxy-server=" + scheme + "://" + ProxyHost + ":" + ProxyPort.ToString(CultureInfo.InvariantCulture));
                        }
                        break;
                }
            }

            if (Has(BypassList) && !string.Equals(ProxyMode, "direct", StringComparison.OrdinalIgnoreCase))
                a.Add("--proxy-bypass-list=" + BypassList.Trim());

            if (Has(HostResolverRules)) a.Add("--host-resolver-rules=" + HostResolverRules);

            if (DohEnabled && Has(DohEndpoint))
            {
                a.Add("--dns-over-https-mode=secure");
                a.Add("--dns-over-https-templates=" + DohEndpoint);
            }

            if (CfgAllowFridaSandboxOff)     a.Add("--no-sandbox");

            if (CfgEmbeddedDevTools)
            {
                a.Add("--remote-debugging-port=" + EmbeddedDevToolsPort);
                a.Add("--remote-debugging-address=127.0.0.1");
                a.Add("--remote-allow-origins=https://devtools.aloha");
            }

            if (!string.IsNullOrWhiteSpace(ExtraSwitches))
                a.Add(ExtraSwitches.Trim());

            return string.Join(" ", a);
        }

        public static NetConfig Default()
        {
            return new NetConfig
            {
                ProxyScheme = "socks5", ProxyHost = "127.0.0.1", ProxyPort = 9050,
                BypassList = "localhost,127.0.0.1", ProfileName = "tor",
            };
        }

        public static NetConfig DirectProfile()
        {
            return new NetConfig
            {
                ProxyScheme = "none", ProxyHost = "", ProxyPort = 9050,
                BypassList = "", ProfileName = "default",
            };
        }

        public static string DefaultPath
        {
            get { return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "AlohaNet.json"); }
        }

        public void Save() { AlohaCfg.Save(this); }
        public static NetConfig Load() { return AlohaCfg.Load(); }

        public void Save(string path)
        {

            var ser = new DataContractJsonSerializer(typeof(NetConfig));
            string tmp = path + ".tmp";
            using (var fs = File.Create(tmp)) ser.WriteObject(fs, this);
            if (File.Exists(path)) File.Replace(tmp, path, null);
            else File.Move(tmp, path);
        }

        public static NetConfig Load(string path)
        {
            if (!File.Exists(path)) return new NetConfig();
            try
            {
                var ser = new DataContractJsonSerializer(typeof(NetConfig));
                using (var fs = File.OpenRead(path))
                    return (ser.ReadObject(fs) as NetConfig) ?? new NetConfig();
            }
            catch (Exception ex)
            {

                try
                {
                    string bad = path + ".bad";
                    if (File.Exists(bad)) File.Delete(bad);
                    File.Copy(path, bad);
                    ActivityLog.General("NetConfig: could not read " + path + " — " + ex.Message
                                        + " (kept a copy at " + Path.GetFileName(bad) + ")");
                }
                catch { }
                return new NetConfig();
            }
        }

        public void CopyFrom(NetConfig other)
        {
            if (other == null) return;
            foreach (var f in typeof(NetConfig).GetFields(BindingFlags.Public | BindingFlags.Instance))
            {
                if (f.IsInitOnly || f.IsLiteral) continue;
                f.SetValue(this, f.GetValue(other));
            }
        }
    }
}
