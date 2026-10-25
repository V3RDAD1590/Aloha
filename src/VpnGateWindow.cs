using System;
using System.Collections.Generic;
using System.Drawing;
using System.Net.NetworkInformation;
using System.Threading;
using System.Windows.Forms;

namespace Aloha
{

    public class VpnGateWindow : DafyFrame
    {
        private static readonly Color Green  = Color.FromArgb(0x33, 0xFF, 0x66);
        private static readonly Color Dim    = Color.FromArgb(0x2C, 0x8A, 0x4A);

        private readonly VpnGateClient client = new VpnGateClient();
        private List<VpnGateClient.Server> servers = new List<VpnGateClient.Server>();
        private List<VpnGateClient.Server> view = new List<VpnGateClient.Server>();
        private string filterText = "";
        private int selIdx = 0;
        private int scrollTop = 0;
        private int scrollX = 0;
        private const int CONTENT_W = 730;
        private const int HSB_H = 16;
        private string status = "Fetching relay list...";
        private bool loading = true;

        private static readonly Color Fab     = Color.FromArgb(0xFA, 0xFA, 0xFB);
        private static readonly Color LblDark = Color.FromArgb(0x55, 0x55, 0x55);

        private Panel canvas, topBar, rateBar;
        private Label lblCust, rateLabel;
        private Button btnUseCust;
        private Panel gripCorner;
        private System.Windows.Forms.Timer rateTimer;
        private long lastRx, lastTx;
        private DateTime lastSample = DateTime.MinValue;
        private string lastNicId;
        private Panel list;
        private TextBox filterBox;
        private ComboBox custList;
        private Label footStatus;
        private Font headFont, rowFont;

        private TextBox infoBox;
        private Label   connLabel;

        private const int ROW_H = 18, HEAD_H = 0, SB_W = 16, ARROW_H = 16;

        private const int TB = 30, RB = 20, MinCW = 800, MinCH = 300;

        private List<string> custFiles = new List<string>();

        protected override bool FooterActsAsResizeGrip { get { return true; } }

        public VpnGateWindow() : base("OPT-VPN", "VPN Gate")
        {

            Size = new Size(820, 470);

            MinimumSize = new Size(MinCW + 20, MinCH + 60);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            KeyPreview = true;
            ClientArea.BackColor = Color.Black;

            canvas = new Panel { BackColor = Color.Black, Dock = DockStyle.Fill };
            ClientArea.Controls.Add(canvas);

            headFont = new Font("Lucida Console", 8.5f, FontStyle.Bold);
            rowFont  = new Font("Consolas", 9f);

            topBar = new Panel { BackColor = Fab };
            var btnConnect    = new Button { Text = "Connect",    Left = 6,   Top = 4, Width = 78, Height = 22, FlatStyle = FlatStyle.System };
            var btnDisconnect = new Button { Text = "Disconnect", Left = 88,  Top = 4, Width = 86, Height = 22, FlatStyle = FlatStyle.System };
            var btnRefresh    = new Button { Text = "Refresh",    Left = 178, Top = 4, Width = 72, Height = 22, FlatStyle = FlatStyle.System };
            var lblFilter     = new Label  { Text = "filter", Left = 262, Top = 8, AutoSize = true, ForeColor = LblDark, BackColor = Color.Transparent };
            filterBox = new TextBox { Left = 300, Top = 5, Width = 210, BorderStyle = BorderStyle.Fixed3D };
            btnConnect.Click    += (s, e) => ConnectTo(Sel());
            btnDisconnect.Click += (s, e) => DoDisconnect();
            btnRefresh.Click    += (s, e) => { StartFetch(); RefreshCustomConfigs(); };
            filterBox.TextChanged += (s, e) => { filterText = filterBox.Text ?? ""; ApplyFilter(); list.Invalidate(); };

            lblCust = new Label { Text = "custom", Left = 522, Top = 8, AutoSize = true, ForeColor = LblDark, BackColor = Color.Transparent };
            custList = new ComboBox
            {
                Left = 566, Top = 5, Width = 172, DropDownStyle = ComboBoxStyle.DropDownList,
                Font = new Font("Consolas", 8.5f)
            };
            btnUseCust = new Button { Text = "Use", Left = 742, Top = 4, Width = 48, Height = 22, FlatStyle = FlatStyle.System };
            btnUseCust.Click += (s, e) => ConnectSelectedCustom();

            custList.SelectedIndexChanged += (s, e) =>
            {
                if (custList.SelectedIndex == custFiles.Count) BrowseForConfig();
            };

            topBar.Controls.AddRange(new Control[] { btnConnect, btnDisconnect, btnRefresh, lblFilter, filterBox, lblCust, custList, btnUseCust });

            list = new Panel { BackColor = Color.Black, BorderStyle = BorderStyle.None };
            typeof(Panel).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(list, true, null);
            list.Paint += List_Paint;
            list.MouseDown += List_MouseDown;
            list.MouseWheel += (s, e) => ScrollBy(e.Delta > 0 ? -3 : 3);

            rateBar = new Panel { BackColor = Fab };
            rateLabel = new Label
            {
                Left = 8, Top = 3, Width = 600, Height = 15, AutoSize = false,
                ForeColor = LblDark, BackColor = Color.Transparent,
                Font = new Font("Consolas", 8.5f), Text = "  no tunnel"
            };
            rateBar.Controls.Add(rateLabel);

            gripCorner = new Panel { BackColor = Color.FromArgb(0xF0, 0xF0, 0xF0), Width = SB_W, Height = HSB_H };
            gripCorner.Paint += GripCorner_Paint;
            MakeResizeGrip(gripCorner);

            canvas.Controls.Add(topBar);
            canvas.Controls.Add(rateBar);
            canvas.Controls.Add(list);

            canvas.Controls.Add(gripCorner);
            list.BringToFront();
            gripCorner.BringToFront();

            rateTimer = new System.Windows.Forms.Timer { Interval = 1000 };
            rateTimer.Tick += SampleRate;
            rateTimer.Start();

            this.KeyDown += VpnGateWindow_KeyDown;
            canvas.Resize += (s, e) => LayoutVpn();
            this.Shown += (s, e) => { LayoutVpn(); StartFetch(); RefreshCustomConfigs(); Log("VPN Gate — fetching relay list…"); };
            this.Activated += (s, e) => RefreshCustomConfigs();
            this.FormClosed += (s, e) =>
            {
                try { rateTimer?.Stop(); rateTimer?.Dispose(); } catch { }
                headFont?.Dispose(); rowFont?.Dispose();

            };
        }

        private void ConnectSelectedCustom()
        {
            int i = custList != null ? custList.SelectedIndex : -1;
            if (custList != null && i == custFiles.Count) { BrowseForConfig(); return; }
            if (i < 0 || i >= custFiles.Count) { BrowseForConfig(); return; }
            ConnectCustom(custFiles[i]);
        }

        private void BrowseForConfig()
        {
            try
            {
                using (var d = new OpenFileDialog())
                {
                    d.Title  = "Choose an OpenVPN config";
                    d.Filter = "OpenVPN config (*.ovpn)|*.ovpn|All files (*.*)|*.*";
                    d.CheckFileExists = true;
                    string seed = ConfigDropDir();
                    if (seed != null) d.InitialDirectory = seed;
                    if (d.ShowDialog(this) != DialogResult.OK) { SelectFirstConfig(); return; }

                    string path = d.FileName;
                    if (!custFiles.Contains(path))
                    {
                        custFiles.Add(path);
                        custList.Items.Insert(custFiles.Count - 1, System.IO.Path.GetFileName(path));
                    }
                    custList.SelectedIndex = custFiles.IndexOf(path);
                    ConnectCustom(path);
                }
            }
            catch (Exception ex) { Log("browse failed: " + ex.Message); }
        }

        private static string ConfigDropDir()
        {
            try
            {
                string baseDir = System.IO.Path.GetDirectoryName(
                    System.Reflection.Assembly.GetExecutingAssembly().Location) ?? ".";
                string d = System.IO.Path.Combine(baseDir, "openvpn", "config");
                if (System.IO.Directory.Exists(d)) return d;
                return System.IO.Directory.Exists(baseDir) ? baseDir : null;
            }
            catch { return null; }
        }

        private void SelectFirstConfig()
        {
            if (custList == null) return;
            custList.SelectedIndex = custFiles.Count > 0 ? 0 : -1;
        }

        private void UpdateServerInfo()
        {
            if (infoBox == null) return;
            var s = Sel();
            if (s == null) { infoBox.Text = "(no relay selected)"; return; }
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("Host     : " + (s.Host ?? ""));
            sb.AppendLine("IP       : " + (s.Ip ?? ""));
            sb.AppendLine("Region   : " + (s.Country ?? "") + " (" + (s.CountryShort ?? "") + ")");
            sb.AppendLine("Proto    : " + (s.Proto ?? "?").ToUpperInvariant() + "  port " + s.Port);
            sb.AppendLine("Score    : " + s.Score);
            sb.AppendLine("Ping     : " + s.Ping + " ms");
            sb.AppendLine("Speed    : " + s.SpeedMbps);
            sb.AppendLine("Sessions : " + s.Sessions);
            sb.AppendLine("Uptime   : " + s.UptimeText);
            sb.AppendLine("Users    : " + s.TotalUsers);
            sb.AppendLine("Traffic  : " + s.TrafficText);
            sb.AppendLine("Log type : " + (s.LogType ?? ""));
            sb.AppendLine("Operator : " + (s.Operator ?? ""));
            if (!string.IsNullOrEmpty(s.Message)) { sb.AppendLine(); sb.AppendLine("Message:"); sb.AppendLine(s.Message); }
            infoBox.Text = sb.ToString();
            infoBox.SelectionStart = 0; infoBox.SelectionLength = 0;
        }

        private void UpdateConnLabel()
        {
            if (connLabel == null) return;
            connLabel.Text = client.Connected
                ? "connected\r\nhost: " + (client.ActiveHost ?? "?")
                : "not connected";
        }

        private void Log(string line)
        {
            Action a = () =>
            {
                SetStatus(line);
                try { ActivityLog.Network("vpn: " + line); } catch { }
            };
            try
            {
                if (IsHandleCreated && !IsDisposed && InvokeRequired) BeginInvoke(a);
                else a();
            }
            catch { }
        }

        private void LayoutVpn()
        {
            if (canvas == null || list == null) return;

            int cw = Math.Max(canvas.ClientSize.Width,  240);
            int ch = Math.Max(canvas.ClientSize.Height, 120);
            topBar.SetBounds(0, 0, cw, TB);
            rateBar.SetBounds(0, TB, cw, RB);

            const int pad = 8, gap = 6;
            btnUseCust.Left = Math.Max(340, cw - pad - btnUseCust.Width);
            custList.Left   = btnUseCust.Left - gap - custList.Width;
            lblCust.Left    = custList.Left  - gap - lblCust.Width;
            filterBox.Width = Math.Max(90, lblCust.Left - gap - filterBox.Left);
            rateLabel.Width = Math.Max(120, cw - 16);

            list.SetBounds(0, TB + RB, cw, ch - TB - RB);

            if (gripCorner != null)
                gripCorner.SetBounds(cw - SB_W, ch - HSB_H, SB_W, HSB_H);

            list.Invalidate();
        }

        private void SetStatus(string s)
        {
            status = s;
            if (footStatus != null) { footStatus.Text = s; footStatus.Invalidate(); }
        }

        private void StartFetch()
        {
            loading = true; SetStatus("Fetching relay list..."); list.Invalidate();
            var t = new Thread(() =>
            {
                string err;
                var rows = client.FetchServers(out err);

                Action apply = () =>
                {
                    if (err != null) { SetStatus("Fetch failed: " + err); Log("fetch failed: " + err); loading = false; list.Invalidate(); return; }
                    rows.Sort((a, b) => b.SpeedBps.CompareTo(a.SpeedBps));
                    servers = rows;
                    loading = false;
                    ApplyFilter();
                    Log(servers.Count + " relays loaded");
                    list.Invalidate();
                };
                try
                {
                    if (this.IsHandleCreated && !this.IsDisposed)
                        this.BeginInvoke(apply);
                }
                catch {  }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void ApplyFilter()
        {
            if (string.IsNullOrEmpty(filterText))
                view = new List<VpnGateClient.Server>(servers);
            else
            {
                string f = filterText.ToLowerInvariant();
                view = servers.FindAll(s =>
                    (s.Host ?? "").ToLowerInvariant().Contains(f) ||
                    (s.Ip ?? "").ToLowerInvariant().Contains(f) ||
                    (s.Country ?? "").ToLowerInvariant().Contains(f) ||
                    (s.CountryShort ?? "").ToLowerInvariant().Contains(f) ||
                    (s.Proto ?? "").ToLowerInvariant().Contains(f));
            }
            selIdx = 0; scrollTop = 0;
            string flt = string.IsNullOrEmpty(filterText) ? "" : " (filter: " + filterText + ")";
            SetStatus(view.Count + " relays" + flt + " \u00B7 volunteer-run, treat as untrusted exit");
            UpdateServerInfo();
        }

        private int VisibleRows { get { return Math.Max(1, (list.ClientSize.Height - HEAD_H - HSB_H) / ROW_H); } }
        private int MaxScroll   { get { return Math.Max(0, view.Count - VisibleRows); } }

        private void ScrollBy(int d)
        {
            scrollTop = Math.Max(0, Math.Min(MaxScroll, scrollTop + d));
            list.Invalidate();
        }

        private void EnsureVisible()
        {
            if (selIdx < scrollTop) scrollTop = selIdx;
            else if (selIdx >= scrollTop + VisibleRows) scrollTop = selIdx - VisibleRows + 1;
        }

        private void VpnGateWindow_KeyDown(object sender, KeyEventArgs e)
        {
            if (loading || view.Count == 0) return;
            if (e.KeyCode == Keys.Up)        { selIdx = Math.Max(0, selIdx - 1); EnsureVisible(); UpdateServerInfo(); list.Invalidate(); e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { selIdx = Math.Min(view.Count - 1, selIdx + 1); EnsureVisible(); UpdateServerInfo(); list.Invalidate(); e.Handled = true; }
            else if (e.KeyCode == Keys.PageDown) { selIdx = Math.Min(view.Count - 1, selIdx + VisibleRows); EnsureVisible(); UpdateServerInfo(); list.Invalidate(); e.Handled = true; }
            else if (e.KeyCode == Keys.PageUp)   { selIdx = Math.Max(0, selIdx - VisibleRows); EnsureVisible(); UpdateServerInfo(); list.Invalidate(); e.Handled = true; }
            else if (e.KeyCode == Keys.Enter)  { ConnectTo(Sel()); e.Handled = true; }
            else if (e.KeyCode == Keys.Space)  { OpenDetail(Sel()); e.Handled = true; }
        }

        private void OpenDetail(VpnGateClient.Server s)
        {
            if (s == null) return;
            var d = new VpnDetailWindow(s);
            d.OnConnect += ConnectTo;
            d.Show();
            d.BringToFront();
        }

        private VpnGateClient.Server Sel()
        {
            return (selIdx >= 0 && selIdx < view.Count) ? view[selIdx] : null;
        }

        private void ConnectTo(VpnGateClient.Server s)
        {
            if (s == null) return;
            SetStatus("Connecting to " + s.Host + " (" + s.Country + ")...");
            Log("connect \u2192 " + s.Host + " \u00b7 " + s.Country + " \u00b7 " + (s.Proto ?? "?").ToUpperInvariant() + " " + s.Port);
            string err = client.Connect(s);
            string msg = err == null
                ? "Connected: " + s.Host + " \u00B7 " + s.Country + " \u00B7 " + s.Proto + " " + s.Port + " \u00B7 click to disconnect"
                : "Connect failed: " + err;
            SetStatus(msg);
            Log(err == null ? "  ok \u2014 tunnel up (live log in OpenVPN's window)" : "  FAILED: " + err);
            UpdateConnLabel();
        }

        private void DoDisconnect()
        {
            string h = client.ActiveHost;
            client.Disconnect();
            SetStatus("Disconnected" + (h != null ? " from " + h : ""));
            Log("disconnect" + (h != null ? " \u2014 " + h : ""));
            UpdateConnLabel();
        }

        private void List_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int w = list.ClientSize.Width, h = list.ClientSize.Height;
            int listW = w - SB_W;
            int listH = h - HSB_H;
            int ox = -scrollX;

            int cHost = 8, cIp = 230, cCty = 380, cSess = 480, cSpd = 545, cPing = 625, cProto = 675;

            if (loading)
            {
                using (var gb = new SolidBrush(Green)) g.DrawString("fetching...", rowFont, gb, cHost, HEAD_H + 6);
                DrawScrollbar(g, w, h);
                DrawHScrollbar(g, w, h);
                return;
            }

            var clip = g.Clip;
            g.SetClip(new Rectangle(0, HEAD_H, listW, listH - HEAD_H));
            int end = Math.Min(view.Count, scrollTop + VisibleRows);
            for (int i = scrollTop; i < end; i++)
            {
                var s = view[i];
                int y = HEAD_H + (i - scrollTop) * ROW_H;
                bool seld = (i == selIdx);
                if (seld) using (var sb = new SolidBrush(Green)) g.FillRectangle(sb, 0, y, listW, ROW_H);
                Brush fg = seld ? Brushes.Black : new SolidBrush(Green);
                Brush pf = seld ? Brushes.Black : new SolidBrush(s.Proto == "udp" ? Green : Dim);

                g.DrawString(Clip(s.Host, 28),     rowFont, fg, ox + cHost, y + 2);
                g.DrawString(Clip(s.Ip, 18),       rowFont, fg, ox + cIp, y + 2);
                g.DrawString(Clip(s.Country, 13),  rowFont, fg, ox + cCty, y + 2);
                g.DrawString(s.Sessions.ToString(),rowFont, fg, ox + cSess, y + 2);
                g.DrawString(s.SpeedMbps,          rowFont, fg, ox + cSpd, y + 2);
                g.DrawString(s.Ping.ToString(),    rowFont, fg, ox + cPing, y + 2);
                g.DrawString(s.Proto.ToUpperInvariant(), rowFont, pf, ox + cProto, y + 2);

                if (!seld) { fg.Dispose(); pf.Dispose(); }
            }
            g.Clip = clip;

            DrawScrollbar(g, w, h);

            DrawScrollbar(g, w, h);
            DrawHScrollbar(g, w, h);
        }

        private Rectangle upRect, downRect, thumbRect;
        private void DrawScrollbar(Graphics g, int w, int h)
        {
            int x = w - SB_W;
            int top = HEAD_H, bot = h - HSB_H;
            upRect   = new Rectangle(x, top, SB_W, ARROW_H);
            downRect = new Rectangle(x, bot - ARROW_H, SB_W, ARROW_H);

            using (var track = new SolidBrush(Color.FromArgb(0xF4, 0xF4, 0xF4)))
                g.FillRectangle(track, x, top, SB_W, bot - top);

            int trackH = (bot - ARROW_H) - (top + ARROW_H);
            if (view.Count > 0 && MaxScroll > 0 && trackH > 8)
            {
                double frac = (double)VisibleRows / view.Count;
                int thumbH = Math.Max(14, (int)(trackH * frac));
                int range = trackH - thumbH;
                int ty = top + ARROW_H + (int)((double)scrollTop / MaxScroll * range);
                thumbRect = new Rectangle(x + 1, ty, SB_W - 2, thumbH);
                DrawBevelButton(g, thumbRect);
            }
            else thumbRect = Rectangle.Empty;

            DrawBevelButton(g, upRect);
            DrawBevelButton(g, downRect);
            DrawArrow(g, upRect, true);
            DrawArrow(g, downRect, false);
        }

        private Rectangle leftRect, rightRect, hThumbRect;
        private int MaxScrollX { get { return Math.Max(0, CONTENT_W - (list.ClientSize.Width - SB_W)); } }
        private void DrawHScrollbar(Graphics g, int w, int h)
        {
            int y = h - HSB_H;
            int left = 0, right = w - SB_W;
            leftRect  = new Rectangle(left, y, ARROW_H, HSB_H);
            rightRect = new Rectangle(right - ARROW_H, y, ARROW_H, HSB_H);

            using (var track = new SolidBrush(Color.FromArgb(0xF4, 0xF4, 0xF4)))
                g.FillRectangle(track, left, y, right - left, HSB_H);

            int trackW = (right - ARROW_H) - (left + ARROW_H);
            int maxX = MaxScrollX;
            if (maxX > 0 && trackW > 8)
            {
                double frac = (double)(list.ClientSize.Width - SB_W) / CONTENT_W;
                int thumbW = Math.Max(18, (int)(trackW * frac));
                int range = trackW - thumbW;
                int tx = left + ARROW_H + (int)((double)scrollX / maxX * range);
                hThumbRect = new Rectangle(tx, y + 1, thumbW, HSB_H - 2);
                DrawBevelButton(g, hThumbRect);
            }
            else hThumbRect = Rectangle.Empty;

            DrawBevelButton(g, leftRect);
            DrawBevelButton(g, rightRect);
            DrawHArrow(g, leftRect, true);
            DrawHArrow(g, rightRect, false);

        }

        private void DrawHArrow(Graphics g, Rectangle r, bool leftward)
        {
            int cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
            using (var p = new Pen(Color.FromArgb(0x50, 0x50, 0x50), 1.4f))
            {
                if (leftward)
                    g.DrawLines(p, new[] { new Point(cx + 2, cy - 4), new Point(cx - 3, cy), new Point(cx + 2, cy + 4) });
                else
                    g.DrawLines(p, new[] { new Point(cx - 2, cy - 4), new Point(cx + 3, cy), new Point(cx - 2, cy + 4) });
            }
        }

        private void ScrollXBy(int d)
        {
            scrollX = Math.Max(0, Math.Min(MaxScrollX, scrollX + d));
            list.Invalidate();
        }

        private void DrawBevelButton(Graphics g, Rectangle r)
        {
            using (var f = new SolidBrush(Color.FromArgb(0xF0, 0xF0, 0xF0))) g.FillRectangle(f, r);
            using (var hi = new Pen(Color.White))
            { g.DrawLine(hi, r.Left, r.Top, r.Right - 1, r.Top); g.DrawLine(hi, r.Left, r.Top, r.Left, r.Bottom - 1); }
            using (var sh = new Pen(Color.FromArgb(0xB0, 0xB0, 0xB0)))
            { g.DrawLine(sh, r.Left, r.Bottom - 1, r.Right - 1, r.Bottom - 1); g.DrawLine(sh, r.Right - 1, r.Top, r.Right - 1, r.Bottom - 1); }
            using (var sh2 = new Pen(Color.FromArgb(0xC8, 0xC8, 0xC8)))
            { g.DrawLine(sh2, r.Left + 1, r.Bottom - 2, r.Right - 2, r.Bottom - 2); }
        }

        private void DrawArrow(Graphics g, Rectangle r, bool up)
        {
            int cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
            using (var p = new Pen(Color.FromArgb(0x50, 0x50, 0x50), 1.4f))
            {
                if (up)
                    g.DrawLines(p, new[] { new Point(cx - 4, cy + 2), new Point(cx, cy - 3), new Point(cx + 4, cy + 2) });
                else
                    g.DrawLines(p, new[] { new Point(cx - 4, cy - 3), new Point(cx, cy + 2), new Point(cx + 4, cy - 3) });
            }
        }

        private void List_MouseDown(object sender, MouseEventArgs e)
        {

            if (leftRect.Contains(e.Location))  { ScrollXBy(-40); return; }
            if (rightRect.Contains(e.Location)) { ScrollXBy(40); return; }
            if (e.Y >= list.ClientSize.Height - HSB_H)
            {
                if (!hThumbRect.IsEmpty)
                    ScrollXBy(e.X < hThumbRect.Left ? -120 : 120);
                return;
            }

            if (upRect.Contains(e.Location))   { ScrollBy(-1); return; }
            if (downRect.Contains(e.Location)) { ScrollBy(1); return; }
            if (e.X >= list.ClientSize.Width - SB_W)
            {
                if (!thumbRect.IsEmpty)
                    ScrollBy(e.Y < thumbRect.Top ? -VisibleRows : VisibleRows);
                return;
            }

            if (loading || view.Count == 0 || e.Y < HEAD_H || e.Y >= list.ClientSize.Height - HSB_H) return;
            int idx = scrollTop + (e.Y - HEAD_H) / ROW_H;
            if (idx >= 0 && idx < view.Count)
            {
                selIdx = idx; UpdateServerInfo(); list.Invalidate();
                if (e.Clicks >= 2) OpenDetail(view[idx]);
            }
        }

        private void RefreshCustomConfigs()
        {
            custFiles = client.ListConfigFiles();
            if (custList == null) return;
            string prev = custList.SelectedItem as string;
            custList.BeginUpdate();
            custList.Items.Clear();
            foreach (var f in custFiles) custList.Items.Add(System.IO.Path.GetFileName(f));

            custList.Items.Add("Browse for an .ovpn file\u2026");
            custList.EndUpdate();
            custList.Enabled = true;

            int pi = prev != null ? custList.Items.IndexOf(prev) : -1;
            if (pi >= 0 && pi < custFiles.Count) custList.SelectedIndex = pi;
            else if (custFiles.Count > 0)        custList.SelectedIndex = 0;
            else                                  custList.SelectedIndex = -1;
        }

        private void ConnectCustom(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            string name = System.IO.Path.GetFileName(path);
            SetStatus("Connecting via " + name + "...");
            Log("connect (custom) \u2192 " + name);
            string err = client.ConnectConfigFile(path);
            string msg = err == null
                ? "Connected via " + name + " \u00b7 system-wide \u00b7 click to disconnect"
                : "Connect failed: " + err;
            SetStatus(msg);
            Log(err == null ? "  ok \u2014 tunnel up" : "  FAILED: " + err);
            UpdateConnLabel();
        }

        private void GripCorner_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            var r = new Rectangle(0, 0, gripCorner.Width, gripCorner.Height);
            using (var f = new SolidBrush(Color.FromArgb(0xF0, 0xF0, 0xF0))) g.FillRectangle(f, r);
            using (var hi = new Pen(Color.White))
            { g.DrawLine(hi, r.Left, r.Top, r.Right - 1, r.Top); g.DrawLine(hi, r.Left, r.Top, r.Left, r.Bottom - 1); }
            using (var sh = new Pen(Color.FromArgb(0xB0, 0xB0, 0xB0)))
            { g.DrawLine(sh, r.Left, r.Bottom - 1, r.Right - 1, r.Bottom - 1); g.DrawLine(sh, r.Right - 1, r.Top, r.Right - 1, r.Bottom - 1); }

            using (var dark = new Pen(Color.FromArgb(0x90, 0x90, 0x90)))
            using (var lite = new Pen(Color.White))
            {
                for (int i = 0; i < 3; i++)
                {
                    int o = 3 + i * 4;
                    g.DrawLine(dark, r.Right - 2, r.Bottom - o - 1, r.Right - o - 1, r.Bottom - 2);
                    g.DrawLine(lite, r.Right - 2, r.Bottom - o,     r.Right - o,     r.Bottom - 2);
                }
            }
        }

        private void SampleRate(object sender, EventArgs e)
        {
            if (rateLabel == null || rateLabel.IsDisposed) return;
            try
            {
                var ni = FindTunnelNic();
                if (ni == null)
                {
                    rateLabel.Text = "  no tunnel";
                    lastSample = DateTime.MinValue; lastNicId = null;
                    return;
                }

                var st = ni.GetIPv4Statistics();
                long rx = st.BytesReceived, tx = st.BytesSent;
                var now = DateTime.UtcNow;

                if (lastSample != DateTime.MinValue && lastNicId == ni.Id)
                {
                    double dt = (now - lastSample).TotalSeconds;
                    if (dt > 0.2)
                        rateLabel.Text = "  \u2193 " + Rate((rx - lastRx) / dt)
                                       + "     \u2191 " + Rate((tx - lastTx) / dt)
                                       + "      " + ni.Name;
                }
                else rateLabel.Text = "  \u2193 --        \u2191 --         " + ni.Name;

                lastNicId = ni.Id; lastRx = rx; lastTx = tx; lastSample = now;
            }
            catch {  }
        }

        private static NetworkInterface FindTunnelNic()
        {
            try
            {
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    string d = ((ni.Description ?? "") + " " + (ni.Name ?? "")).ToLowerInvariant();
                    if (d.Contains("tap-windows") || d.Contains("tap-win32") ||
                        d.Contains("wintun")      || d.Contains("openvpn"))
                        return ni;
                }
            }
            catch { }
            return null;
        }

        private static string Rate(double bps)
        {
            if (bps < 0) bps = 0;
            if (bps < 1024)          return ((int)bps) + " B/s";
            if (bps < 1024 * 1024)   return (bps / 1024).ToString("0.0") + " KB/s";
            return (bps / (1024 * 1024)).ToString("0.00") + " MB/s";
        }

        private static string Clip(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n - 1) + "\u2026";
        }
    }
}
