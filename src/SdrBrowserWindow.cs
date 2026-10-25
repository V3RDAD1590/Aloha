using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Windows.Forms;

namespace Aloha
{

    public class SdrBrowserWindow : DafyFrame
    {
        private static readonly Color Fab   = Color.FromArgb(0xFA, 0xFA, 0xFB);
        private static readonly Color Ink    = Color.FromArgb(0x20, 0x20, 0x20);
        private static readonly Color Lime    = Color.FromArgb(0x2e, 0xe0, 0x5a);
        private static readonly Color LimeDim = Color.FromArgb(0x14, 0x60, 0x2a);

        private static Color JetColor(double v)
        {
            if (v < 0) v = 0; else if (v > 1) v = 1;
            double r = JetClamp(1.5 - Math.Abs(4 * v - 3));
            double g = JetClamp(1.5 - Math.Abs(4 * v - 2));
            double b = JetClamp(1.5 - Math.Abs(4 * v - 1));
            return Color.FromArgb((int)(r * 255), (int)(g * 255), (int)(b * 255));
        }
        private static double JetClamp(double x) { return x < 0 ? 0 : (x > 1 ? 1 : x); }

        private class Rx { public string name, url, loc, band, users; }

        private readonly List<Rx> rows = new List<Rx>();
        private readonly Action<string> openInTab;
        private readonly NetConfig cfg;

        private ComboBox providerBox;
        private TextBox hostBox;
        private Panel canvas;
        private Panel body;
        private Panel gripCorner;
        private DarkScrollBar vbar;
        private int scroll = 0, selIdx = -1;
        private string statusText = "enter a host, or reload a list";
        private long waterPhase = 0;
        private System.Windows.Forms.Timer tick;

        private KiwiWaterfallClient wf;
        private Bitmap wfBmp;
        private readonly object wfLock = new object();
        private Button wfBtn;
        private bool wfLive;

        private const int ROW_H = 18, SB_W = 16, WATER_H = 46, TOP_H = 64;

        private static readonly string[] ListNames = { "(manual host only)", "KiwiSDR public", "KiwiSDR linkfanel", "WebSDR.org" };
        private static readonly string[] ListUrls  = { "", "http://kiwisdr.com/public/", "http://rx.linkfanel.net/", "http://www.websdr.org/" };

        public SdrBrowserWindow(Action<string> openInTab, NetConfig cfg = null) : base("SDR", "SDR \u2014 receiver browser")
        {
            this.openInTab = openInTab;
            this.cfg = cfg;

            Size = new Size(1400, 500);
            MinimumSize = new Size(460, 170);
            ClientArea.BackColor = Color.Black;

            BuildCanvas();
            BuildHeader();

            tick = new System.Windows.Forms.Timer { Interval = 60 };
            tick.Tick += (s, e) =>
            {
                waterPhase++;

                if (wf != null && !wf.Running && wfBtn != null && wfBtn.Text == "wf ■") { wfBtn.Text = "wf ▸"; wfLive = false; }
                canvas.Invalidate();
            };
            tick.Start();
            FormClosed += (s, e) =>
            {
                try { tick?.Stop(); tick?.Dispose(); } catch { }
                try { wf?.Stop(); } catch { }
                lock (wfLock) { wfBmp?.Dispose(); wfBmp = null; }
            };
        }

        private void BuildHeader()
        {
            var top = new Panel { Dock = DockStyle.Top, Height = TOP_H, BackColor = Fab };

            var lblP = new Label { Left = 8, Top = 8, Width = 46, Text = "list", ForeColor = Ink, BackColor = Color.Transparent };
            providerBox = new ComboBox { Left = 54, Top = 5, Width = 190, DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (var n in ListNames) providerBox.Items.Add(n);
            providerBox.SelectedIndex = 1;

            var reload = new Button { Left = 250, Top = 4, Width = 66, Text = "reload" };
            reload.Click += async (s, e) => await RefreshList();

            var lblH = new Label { Left = 8, Top = 38, Width = 46, Text = "host", ForeColor = Ink, BackColor = Color.Transparent };
            hostBox = new TextBox { Left = 54, Top = 35, Width = 460, Text = "http://" };
            hostBox.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) OpenHost(); };

            var go = new Button { Left = 520, Top = 34, Width = 80, Text = "waterfall \u25B8", Anchor = AnchorStyles.Top | AnchorStyles.Right };
            go.Click += (s, e) => OpenHost();

            var webBtn = new Button { Left = 604, Top = 34, Width = 52, Text = "web \u25B8", Anchor = AnchorStyles.Top | AnchorStyles.Right };
            webBtn.Click += (s, e) => OpenWeb();

            wfBtn = new Button { Left = 660, Top = 34, Width = 46, Text = "wf \u25B8", Anchor = AnchorStyles.Top | AnchorStyles.Right };
            wfBtn.Click += (s, e) => ToggleWaterfall();

            top.Controls.Add(lblP); top.Controls.Add(providerBox); top.Controls.Add(reload);
            top.Controls.Add(lblH); top.Controls.Add(hostBox); top.Controls.Add(go); top.Controls.Add(webBtn); top.Controls.Add(wfBtn);
            ClientArea.Controls.Add(top);
        }

        private void BuildCanvas()
        {
            canvas = new Panel { BackColor = Color.Black, Dock = DockStyle.Fill };
            typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(canvas, true, null);
            canvas.Paint += Canvas_Paint;
            canvas.MouseDown += Canvas_MouseDown;
            canvas.MouseWheel += (s, e) => { scroll = Math.Max(0, scroll - Math.Sign(e.Delta)); SyncBar(); canvas.Invalidate(); };

            vbar = new DarkScrollBar(false) { Width = SB_W, Dock = DockStyle.Right };

            vbar.ValueChanged += (s, e) => { scroll = vbar.Value; canvas.Invalidate(); };
            gripCorner = new Panel { BackColor = Color.FromArgb(0xF0, 0xF0, 0xF0), Width = SB_W, Height = SB_W };
            MakeResizeGrip(gripCorner);

            body = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
            body.Controls.Add(canvas);
            body.Controls.Add(vbar);
            ClientArea.Controls.Add(body);
            canvas.Controls.Add(gripCorner);
            canvas.Resize += (s, e) => { LayoutGrip(); SyncBar(); };
            LayoutGrip();
            SyncBar();
        }

        private void SyncBar()
        {
            if (vbar == null) return;
            vbar.SetValueModel(rows.Count, VisibleRows);
            vbar.Value = scroll;
            scroll = vbar.Value;
        }

        private void ToggleWaterfall()
        {
            if (wf != null && wf.Running) { StopWaterfall(); return; }

            string host = (hostBox.Text ?? "").Trim();
            if (!HasHost(host)) { statusText = "enter a receiver host first"; canvas.Invalidate(); return; }

            wf = new KiwiWaterfallClient();
            wf.Status += msg => { statusText = msg; SafeInvalidate(); };
            wf.FrameReceived += OnWfFrame;
            wfLive = false;
            wf.Start(host, zoom: 0);
            wfBtn.Text = "wf ■";
        }

        private void StopWaterfall()
        {
            try { wf?.Stop(); } catch { }
            wfLive = false;
            wfBtn.Text = "wf ▸";
            statusText = "waterfall stopped";
            canvas.Invalidate();
        }

        private void OnWfFrame(byte[] bins)
        {
            if (bins == null || bins.Length == 0) return;
            lock (wfLock)
            {
                if (wfBmp == null || wfBmp.Width != bins.Length)
                { wfBmp?.Dispose(); wfBmp = new Bitmap(bins.Length, WATER_H); }

                using (var g = Graphics.FromImage(wfBmp))
                    g.DrawImage(wfBmp, new Rectangle(0, -1, wfBmp.Width, wfBmp.Height));

                int y = wfBmp.Height - 1;
                for (int x = 0; x < bins.Length; x++)
                {
                    int v = bins[x];
                    Color jc = JetColor(v / 255.0);
                    wfBmp.SetPixel(x, y, Color.FromArgb(255, jc.R, jc.G, jc.B));
                }
            }
            wfLive = true;
            SafeInvalidate();
        }

        private void SafeInvalidate()
        {
            try { if (canvas != null && canvas.IsHandleCreated) canvas.BeginInvoke((Action)(() => canvas.Invalidate())); }
            catch { }
        }

        private void LayoutGrip()
        {
            if (gripCorner != null)
                gripCorner.SetBounds(canvas.ClientSize.Width - SB_W, canvas.ClientSize.Height - SB_W, SB_W, SB_W);
        }

        private int VisibleRows => Math.Max(1, (canvas.ClientSize.Height - WATER_H) / ROW_H);

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int W = canvas.ClientSize.Width, H = canvas.ClientSize.Height;
            g.Clear(Color.Black);

            int listH = H - WATER_H;
            using (var ff = new Font("Consolas", 8f))
            using (var lb = new SolidBrush(Lime))
            using (var db = new SolidBrush(LimeDim))
            {
                int rows_shown = Math.Min(VisibleRows, rows.Count - scroll);
                for (int i = 0; i < rows_shown; i++)
                {
                    int ri = i + scroll;
                    if (ri < 0 || ri >= rows.Count) break;
                    var r = rows[ri];
                    int y = i * ROW_H;
                    if (ri == selIdx)
                        using (var sel = new SolidBrush(Color.FromArgb(0x10, 0x40, 0x20)))
                            g.FillRectangle(sel, 0, y, W - SB_W, ROW_H);
                    string line = (r.loc ?? r.name ?? "?");
                    if (!string.IsNullOrEmpty(r.band)) line += "   " + r.band;
                    if (!string.IsNullOrEmpty(r.users)) line += "   " + r.users + "u";
                    g.DrawString(line, ff, lb, 6, y + 2);
                    g.DrawString(r.url ?? "", ff, db, W - SB_W - 260, y + 2);
                }

                if (rows.Count == 0)
                    using (var sb2 = new SolidBrush(Color.FromArgb(0x7d, 0xff, 0xb0)))
                        g.DrawString(statusText, ff, sb2, 8, 6);
            }

            DrawWaterfall(g, 0, listH, W, WATER_H);
        }

        private void DrawWaterfall(Graphics g, int x, int y, int w, int h)
        {
            using (var bg = new SolidBrush(Color.FromArgb(0x02, 0x10, 0x08)))
                g.FillRectangle(bg, x, y, w, h);
            using (var top = new Pen(LimeDim)) g.DrawLine(top, x, y, x + w, y);

            if (wfLive)
            {
                lock (wfLock)
                {
                    if (wfBmp != null)
                    {
                        g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.NearestNeighbor;
                        g.DrawImage(wfBmp, new Rectangle(x, y + 1, w, h - 1));
                    }
                }
                using (var ff2 = new Font("Consolas", 6.5f))
                using (var lb2 = new SolidBrush(Lime))
                    g.DrawString("waterfall (live)", ff2, lb2, x + 6, y + 4);
                return;
            }
            var rng = new Random(1234);
            for (int col = 0; col < w; col += 2)
            {

                double t = (waterPhase * 0.05) + col * 0.03;
                double peak1 = Math.Exp(-Math.Pow((col - (w * 0.35 + Math.Sin(t) * 40)) / 30.0, 2));
                double peak2 = Math.Exp(-Math.Pow((col - (w * 0.7 + Math.Cos(t * 0.7) * 60)) / 18.0, 2));
                double v = 0.12 + 0.8 * peak1 + 0.6 * peak2 + 0.08 * rng.NextDouble();
                if (v > 1) v = 1;
                int barH = (int)(v * (h - 6));
                Color jc = JetColor(v);
                using (var p = new Pen(Color.FromArgb((int)(60 + v * 180), jc.R, jc.G, jc.B)))
                    g.DrawLine(p, x + col, y + h - 2, x + col, y + h - 2 - barH);
            }
            using (var ff = new Font("Consolas", 6.5f))
            using (var lb = new SolidBrush(Lime))
                g.DrawString("waterfall (preview)", ff, lb, x + 6, y + 4);
        }

        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            int listH = canvas.ClientSize.Height - WATER_H;
            if (e.Y >= listH) return;
            int ri = e.Y / ROW_H + scroll;
            if (ri >= 0 && ri < rows.Count)
            {
                selIdx = ri;
                if (e.Clicks >= 2) { hostBox.Text = rows[ri].url; OpenHost(); }
                canvas.Invalidate();
            }
        }

        private void OpenHost()
        {
            string url = (hostBox.Text ?? "").Trim();
            if (!HasHost(url)) { statusText = "enter a receiver host first"; canvas.Invalidate(); return; }
            try
            {
                var w = new SdrWaterfallWindow(url, openInTab);
                w.StartPosition = FormStartPosition.CenterScreen;
                w.Show();
                w.BringToFront();
                statusText = "waterfall window: " + url;
            }
            catch (Exception ex) { statusText = "open failed: " + ex.Message; }
            canvas.Invalidate();
        }

        private void OpenWeb()
        {
            string url = (hostBox.Text ?? "").Trim();
            if (!HasHost(url)) { statusText = "enter a receiver host first"; canvas.Invalidate(); return; }
            if (!url.StartsWith("http")) url = "http://" + url;
            try { openInTab?.Invoke(url); statusText = "opening web client " + url; }
            catch (Exception ex) { statusText = "open failed: " + ex.Message; }
            canvas.Invalidate();
        }

        private static bool HasHost(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return false;
            s = s.Replace("http://", "").Replace("https://", "").Trim();
            int sl = s.IndexOf('/'); if (sl >= 0) s = s.Substring(0, sl);
            return s.Length >= 2 && !s.StartsWith(":");
        }

        private async System.Threading.Tasks.Task RefreshList()
        {
            int idx = Math.Max(0, providerBox.SelectedIndex);
            if (idx == 0 || string.IsNullOrEmpty(ListUrls[idx]))
            { statusText = "manual host mode \u2014 type a receiver URL above"; rows.Clear(); scroll = 0; SyncBar(); canvas.Invalidate(); return; }

            statusText = "fetching " + ListNames[idx] + " ...";
            rows.Clear(); canvas.Invalidate();

            var mode = SocksFetch.Mode.Direct;
            string pHost = "127.0.0.1"; int pPort = 0;
            ResolveProxy(out mode, out pHost, out pPort);
            string via = mode == SocksFetch.Mode.Direct ? "direct"
                       : (mode == SocksFetch.Mode.Socks5 ? "socks5 " : "http-proxy ") + pHost + ":" + pPort;
            statusText = "fetching " + ListNames[idx] + " via " + via + " ...";
            canvas.Invalidate();

            string url = ListUrls[idx];
            try
            {
                var r = await System.Threading.Tasks.Task.Run(
                    () => SocksFetch.GetFollow(url, mode, pHost, pPort, 20000));

                rows.AddRange(idx == 3 ? ParseWebSdr(r.Body) : ParseKiwi(r.Body));
                string how = "";
                if (rows.Count == 0) { rows.AddRange(ParseAnyLinks(r.Body, url)); how = " [generic]"; }

                string trace = "HTTP " + r.Status + ", " + r.Bytes + "B"
                             + (r.Hops > 0 ? ", " + r.Hops + " redirect(s)" : "")
                             + ", via " + via;

                if (rows.Count > 0)
                    statusText = ListNames[idx] + how + " \u2014 " + rows.Count + " receivers  (" + trace + ")";
                else
                {

                    string dump = SaveRaw(r);
                    statusText = "0 rows. " + trace + ". final url: " + r.FinalUrl
                               + "  \u2014 raw saved to " + dump;
                }
            }
            catch (Exception ex)
            {
                statusText = "fetch failed via " + via + ": " + ex.Message;
            }
            scroll = 0; selIdx = -1; SyncBar(); canvas.Invalidate();
        }

        private static string SaveRaw(SocksFetch.Result r)
        {
            try
            {
                string path = System.IO.Path.Combine(AlohaPaths.Root, "sdr-last-fetch.html");
                System.IO.File.WriteAllText(path,
                    "<!-- final url: " + r.FinalUrl + "\r\n     status: " + r.Status +
                    "\r\n     content-type: " + (r.ContentType ?? "?") + " -->\r\n" + r.Body);
                return path;
            }
            catch (Exception ex) { return "(could not save: " + ex.Message + ")"; }
        }

        private static List<Rx> ParseAnyLinks(string html, string baseUrl)
        {
            var res = new List<Rx>();
            if (string.IsNullOrEmpty(html)) return res;
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in Regex.Matches(html,
                     "<a[^>]+href=[\"']([^\"']+)[\"'][^>]*>(.*?)</a>",
                     RegexOptions.IgnoreCase | RegexOptions.Singleline))
            {
                string href = m.Groups[1].Value.Trim();
                string text = Strip(Regex.Replace(m.Groups[2].Value, "<[^>]+>", " "));
                if (href.StartsWith("//")) href = "http:" + href;
                if (!href.StartsWith("http", StringComparison.OrdinalIgnoreCase)) continue;
                if (text.Length < 2) continue;

                string probe = (href + " " + text).ToLowerInvariant();
                bool looks = probe.Contains("sdr") || probe.Contains("kiwi") || probe.Contains("receiver")
                          || Regex.IsMatch(href, @":8073|:8074|/kiwi");
                if (!looks) continue;
                if (!seen.Add(href)) continue;

                res.Add(new Rx { name = text, url = href, loc = text, band = "", users = "" });
                if (res.Count > 400) break;
            }
            return res;
        }

        private void ResolveProxy(out SocksFetch.Mode mode, out string host, out int port)
        {
            mode = SocksFetch.Mode.Direct; host = "127.0.0.1"; port = 0;
            if (cfg == null) return;
            try
            {
                if (cfg.SealedTor)
                { mode = SocksFetch.Mode.Socks5; port = NetConfig.SealedTorSocksPort; return; }

                if (cfg.CfgUseInterceptProxy && cfg.InterceptPort > 0)
                { mode = SocksFetch.Mode.HttpProxy; port = cfg.InterceptPort; return; }

                string scheme = (cfg.ProxyScheme ?? "none").ToLowerInvariant();
                if (scheme == "socks5" || scheme == "socks5h" || scheme == "socks4")
                {
                    mode = SocksFetch.Mode.Socks5;
                    host = string.IsNullOrEmpty(cfg.ProxyHost) ? "127.0.0.1" : cfg.ProxyHost;
                    port = cfg.ProxyPort;
                }
                else if (scheme == "http")
                {
                    mode = SocksFetch.Mode.HttpProxy;
                    host = string.IsNullOrEmpty(cfg.ProxyHost) ? "127.0.0.1" : cfg.ProxyHost;
                    port = cfg.ProxyPort;
                }
                if (port <= 0) { mode = SocksFetch.Mode.Direct; port = 0; }
            }
            catch { mode = SocksFetch.Mode.Direct; }
        }

        private static List<Rx> ParseKiwi(string html)
        {
            var res = new List<Rx>();
            if (string.IsNullOrEmpty(html)) return res;
            foreach (var b in Regex.Split(html, "cl-name"))
            {
                string url = Match(b, "href=[\"']([^\"']+)[\"']");
                if (string.IsNullOrEmpty(url)) continue;
                if (url.StartsWith("//")) url = "http:" + url;
                if (!url.StartsWith("http")) continue;
                string name = Strip(Match(b, ">([^<]{2,60})</a>"));
                string loc  = Strip(Match(b, "cl-loc[^>]*>([^<]{2,60})<"));
                string users = Strip(Match(b, "cl-users[^>]*>([^<]{1,12})<"));
                string band = Strip(Match(b, "(\\d[\\d.]*\\s*-\\s*\\d[\\d.]*\\s*[kMG]?Hz)"));
                res.Add(new Rx { name = name, url = url, loc = string.IsNullOrEmpty(loc) ? name : loc, band = band, users = users });
                if (res.Count > 400) break;
            }
            return res;
        }
        private static List<Rx> ParseWebSdr(string html)
        {
            var res = new List<Rx>();
            if (string.IsNullOrEmpty(html)) return res;
            foreach (Match m in Regex.Matches(html, "<a[^>]+href=[\"'](http[^\"']+)[\"'][^>]*>([^<]{2,70})</a>"))
            {
                string url = m.Groups[1].Value, loc = Strip(m.Groups[2].Value);
                if (!url.Contains("sdr") && !loc.ToLowerInvariant().Contains("sdr")) continue;
                res.Add(new Rx { name = loc, url = url, loc = loc, band = "", users = "" });
                if (res.Count > 400) break;
            }
            return res;
        }
        private static string Match(string s, string pat)
        {
            if (string.IsNullOrEmpty(s)) return "";
            var m = Regex.Match(s, pat, RegexOptions.IgnoreCase);
            return m.Success ? (m.Groups.Count > 1 ? m.Groups[1].Value : m.Value) : "";
        }
        private static string Strip(string s) => string.IsNullOrEmpty(s) ? "" : Regex.Replace(s, "\\s+", " ").Trim();
    }
}
