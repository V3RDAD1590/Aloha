using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Aloha
{

    public class BrowserWindow : DafyFrame
    {

        protected override bool FloatsAboveMain { get { return false; } }

        public const int MaxTabs = 4;

        public int WindowIndex { get { return windowIndex; } }
        public int TabCount { get { lock (caravanLock) { return strip.Count; } } }

        private readonly int windowIndex;
        private readonly string tmpRoot;
        private readonly NetConfig cfg;

        private AlohaChrome strip;
        private Panel host;

        private readonly List<WebView2> engines = new List<WebView2>();

        private readonly List<List<int>> chains = new List<List<int>>();

        private readonly object caravanLock = new object();

        public BrowserWindow(NetConfig config, int index, string firstUrl)
            : base("WIN-" + index, "Aloha — window " + index)
        {
            cfg = config ?? new NetConfig();
            windowIndex = index;

            string baseTmp = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tmp");
            tmpRoot = Path.Combine(baseTmp, "window" + index);
            try { Directory.CreateDirectory(tmpRoot); } catch { }

            Size = new Size(1000, 680);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            ClientArea.BackColor = Color.FromArgb(0xF0, 0xF0, 0xF0);

            SetHeaderHeight(0);

            BuildChrome();

            _ = AddTab(firstUrl ?? "about:blank", null);
        }

        private void BuildChrome()
        {
            strip = new AlohaChrome { Dock = DockStyle.Top };
            strip.ActivateRequested += i => SelectTab(i);
            strip.CloseRequested    += i => CloseTab(i);
            strip.NewTabRequested   += () => { _ = AddTab("about:blank", null); };

            strip.NavigateRequested += url => NavigateActive(url);
            strip.BackRequested     += () => { var wv = strip.ActiveView as WebView2; try { if (wv?.CoreWebView2?.CanGoBack == true) wv.CoreWebView2.GoBack(); } catch { } };
            strip.ForwardRequested  += () => { var wv = strip.ActiveView as WebView2; try { if (wv?.CoreWebView2?.CanGoForward == true) wv.CoreWebView2.GoForward(); } catch { } };
            strip.ReloadRequested   += () => { var wv = strip.ActiveView as WebView2; try { wv?.CoreWebView2?.Reload(); } catch { } };
            strip.HomeRequested     += () => NavigateActive("about:blank");   

            var menu = new ContextMenuStrip();
            menu.Items.Add("New Tab", null, (s, e) => { _ = AddTab("about:blank", null); });
            menu.Items.Add("Reload",  null, (s, e) => { try { (strip.ActiveView as WebView2)?.CoreWebView2?.Reload(); } catch { } });
            menu.Items.Add(new ToolStripSeparator());
            menu.Items.Add("Close Tab", null, (s, e) => { if (strip.ActiveIndex >= 0) CloseTab(strip.ActiveIndex); });
            strip.MenuRequested += () => menu.Show(strip, new System.Drawing.Point(strip.Width - 40, 60));

            host = new Panel { Dock = DockStyle.Fill, BackColor = Color.White };

            ClientArea.Controls.Add(host);
            ClientArea.Controls.Add(strip);
        }

        private async Task<WebView2> MakeEngine(int tabSlot, List<int> chain)
        {
            var wv = new WebView2 { Dock = DockStyle.Fill };

            string udf = Path.Combine(tmpRoot, "tab" + tabSlot);
            try { Directory.CreateDirectory(udf); } catch { }

            string args = cfg.BuildBrowserArguments();
            if (chain != null && chain.Count > 0)
            {

                args += " --proxy-server=socks5://127.0.0.1:" + chain[0];
            }

            var opts = new CoreWebView2EnvironmentOptions { AdditionalBrowserArguments = args };
            var env = await CoreWebView2Environment.CreateAsync(null, udf, opts);
            await wv.EnsureCoreWebView2Async(env);

            wv.CoreWebView2.NewWindowRequested += (o, a) =>
            {
                a.Handled = true;
                if (!string.IsNullOrEmpty(a.Uri)) _ = AddTab(a.Uri, null);
            };
            wv.CoreWebView2.DocumentTitleChanged += (o, a) =>
            {
                int idx = engines.IndexOf(wv);
                if (idx >= 0) strip.SetTitle(idx, wv.CoreWebView2.DocumentTitle);
            };
            wv.CoreWebView2.SourceChanged += (o, a) =>
            {
                if (wv == strip.ActiveView)
                    strip.SetAddress(AlohaConfigBridge.Pretty(wv.Source?.ToString()));
            };
            wv.CoreWebView2.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
            wv.CoreWebView2.WebResourceRequested += (o, a) =>
            {

                try
                {
                    if (!cfg.CfgLoadFavicons)
                    {
                        string u = a.Request.Uri.ToLowerInvariant();
                        if (u.Contains("favicon") || u.Contains("apple-touch-icon"))
                        {
                            var core0 = o as CoreWebView2;
                            if (core0 != null)
                            {
                                a.Response = core0.Environment.CreateWebResourceResponse(
                                    null, 403, "Blocked", "");
                                return;
                            }
                        }
                    }
                }
                catch { }
            };
            wv.CoreWebView2.FaviconChanged += async (o, a) =>
            {
                try
                {
                    if (!cfg.CfgLoadFavicons) return;
                    int idx = engines.IndexOf(wv);
                    if (idx < 0) return;
                    string fav = wv.CoreWebView2.FaviconUri;
                    if (string.IsNullOrEmpty(fav)) { strip.SetIcon(idx, null); return; }
                    using (var st = await wv.CoreWebView2.GetFaviconAsync(CoreWebView2FaviconImageFormat.Png))
                    {

                        if (st != null)
                            using (var tmp = Image.FromStream(st))
                                strip.SetIcon(idx, new Bitmap(tmp));
                    }
                }
                catch { }
            };

            return wv;
        }

        public async Task<bool> AddTab(string url, List<int> chain)
        {
            WebView2 wv;
            int slot;
            lock (caravanLock)
            {
                if (strip.Count >= MaxTabs)
                {
                    ActivityLog.Network("caravan w" + windowIndex + ": full (max " + MaxTabs + " tabs)");
                    return false;
                }
                slot = strip.Count;
            }

            try { wv = await MakeEngine(slot, chain); }
            catch (Exception ex) { ActivityLog.Network("caravan w" + windowIndex + ": engine failed - " + ex.Message); return false; }

            lock (caravanLock)
            {
                engines.Add(wv);
                chains.Add(chain != null ? new List<int>(chain) : new List<int>());
                int i = strip.AddTab("New Tab", wv);
                ShowEngine(i);
            }

            try { wv.CoreWebView2.Navigate(Normalize(url)); } catch { }
            return true;
        }

        private void ShowEngine(int i)
        {
            host.Controls.Clear();
            var wv = strip.ViewAt(i) as WebView2;
            if (wv != null) { host.Controls.Add(wv); wv.BringToFront(); }
            if (wv != null) strip.SetAddress(AlohaConfigBridge.Pretty(wv.Source?.ToString()));
        }

        private void SelectTab(int i)
        {
            lock (caravanLock)
            {
                strip.SetActive(i);
                ShowEngine(i);
            }
        }

        private void CloseTab(int i)
        {
            WebView2 dying = null;
            List<int> dyingChain = null;

            lock (caravanLock)
            {
                if (i < 0 || i >= engines.Count) return;

                dying = engines[i];
                dyingChain = chains[i];

                engines.RemoveAt(i);
                chains.RemoveAt(i);
                strip.RemoveTab(i);

                var stillUsed = new HashSet<int>();
                foreach (var ch in chains)
                    foreach (int port in ch)
                        stillUsed.Add(port);

                var toReclaim = new List<int>();
                if (dyingChain != null)
                    foreach (int port in dyingChain)
                        if (!stillUsed.Contains(port))
                            toReclaim.Add(port);

                foreach (int port in toReclaim)
                    ActivityLog.Network("caravan w" + windowIndex + ": hop " + port + " orphaned -> reclaim");

                if (strip.Count > 0) ShowEngine(strip.ActiveIndex);
                else host.Controls.Clear();
            }

            if (dying != null)
            {
                try
                {
                    var core = dying.CoreWebView2;
                    dying.Dispose();
                }
                catch {  }
            }

            if (strip.Count == 0) Close();
        }

        private void NavigateActive(string url)
        {
            var wv = strip.ActiveView as WebView2;
            if (wv == null) return;
            try { wv.CoreWebView2.Navigate(Normalize(url)); } catch { }
        }

        private string Normalize(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return "about:blank";
            string low = s.Trim().ToLowerInvariant();
            if (low == "aloha://config" || low == "aloha:config" || low == "aloha://config/")
                return AlohaConfigBridge.PageUrl;
            if (low == "aloha://blank" || low == "aloha:blank") return "about:blank";
            if (s.Contains("://") || s.StartsWith("about:")) return s;
            if (s.Contains(".") && !s.Contains(" ")) return "http://" + s;
            return SearchUrl(s);
        }

        private string SearchUrl(string query)
        {
            string q = Uri.EscapeDataString(query);
            string tmpl = cfg != null ? cfg.SearchTemplate : null;
            if (!string.IsNullOrWhiteSpace(tmpl) && tmpl.Contains("{q}"))
                return tmpl.Replace("{q}", q);
            bool onTor = cfg != null && cfg.ProxyPort == 9050
                         && !string.IsNullOrEmpty(cfg.ProxyScheme)
                         && cfg.ProxyScheme.StartsWith("socks", StringComparison.OrdinalIgnoreCase);
            return (onTor ? NetConfig.SearchStartpageOnion : NetConfig.SearchStartpage).Replace("{q}", q);
        }

        public async Task SetTabChain(int i, List<int> chain)
        {
            string url;
            lock (caravanLock)
            {
                if (i < 0 || i >= engines.Count) return;
                url = (engines[i].Source?.ToString()) ?? "about:blank";
            }

            CloseTab(i);
            await AddTab(url, chain);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {

            lock (caravanLock)
            {
                foreach (var wv in engines)
                    try { wv.Dispose(); } catch { }
                engines.Clear();
                chains.Clear();
            }
            try { if (Directory.Exists(tmpRoot)) Directory.Delete(tmpRoot, true); } catch { }
            base.OnFormClosed(e);
        }
    }
}
