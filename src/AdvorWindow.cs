using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace Aloha
{

    public class AdvorWindow : DafyFrame
    {

        private static readonly Color Green   = Color.FromArgb(0x7F, 0xAE, 0x86);
        private static readonly Color Dim     = Color.FromArgb(0x4A, 0x6E, 0x50);
        private static readonly Color GreenHi = Color.FromArgb(0xB4, 0xDC, 0xBA);
        private static readonly Color Fab     = Color.FromArgb(0xFA, 0xFA, 0xFB);
        private static readonly Color LblDark = Color.FromArgb(0x55, 0x55, 0x55);

        private TorControl ctrl;
        private readonly GeoIp geo = new GeoIp();
        private List<TorControl.Relay> relays = new List<TorControl.Relay>();
        private List<TorControl.Relay> view  = new List<TorControl.Relay>();
        private string filterText = "";
        private int selIdx = 0, scrollTop = 0;
        private bool loading = false;
        private string status = "not connected";

        private Panel topBar, rightPanel, list;
        private TextBox portInput, filterBox;
        private Label footStatus;
        private Font headFont, rowFont;

        private ComboBox sectionBox;
        private readonly Dictionary<string, Panel> sections = new Dictionary<string, Panel>();

        private TextBox exitBox, entryBox, excludeBox;
        private CheckBox strictCheck;

        private TextBox bridgesBox; private CheckBox useBridgesCheck;

        private ComboBox proxyType; private TextBox proxyHostBox;

        private CheckBox fascistCheck; private TextBox reachBox;

        private TextBox cbtBox, ncpBox, mcdBox; private CheckBox distinctCheck, guardsCheck;

        private TextBox mapFromBox, mapToBox;

        private TextBox orPortBox, nickBox, contactBox, relayRateBox, relayBurstBox;
        private CheckBox allowExitCheck;
        private System.Windows.Forms.Timer trafficTimer;
        private Label trafficReadout; private CheckBox logTrafficCheck;
        private long tRead, tWritten; private DateTime tStamp = DateTime.MinValue;

        private TextBox pathBox;

        private CheckBox autoIdCheck; private TextBox autoIdMinBox; private Label autoIdCountdown;
        private DateTime nextIdentityUtc = DateTime.MinValue;

        private TextBox onionTargetBox, onionVPortBox; private CheckBox onionKeepKeyCheck; private ListBox onionList;
        private readonly Dictionary<string, string> onionPorts = new Dictionary<string, string>();

        private TextBox sfUrlBox, sfSocksBox; private ComboBox sfParts; private Panel sfPanel; private Label sfStatus;
        private Button sfFetchBtn, sfCancelBtn;
        private TorStripedFetch sf; private System.Threading.CancellationTokenSource sfCts;
        private System.Windows.Forms.Timer sfTimer; private long sfLastDone; private DateTime sfLastStamp; private double sfRate; private DateTime sfStart;
        private string sfLastFile;

        private Panel bwPanel;
        private readonly List<double> bwDown = new List<double>(), bwUp = new List<double>();
        private const int BW_H = 62, BW_SAMPLES = 150;
        private readonly NetConfig cfg;

        private Panel circuitPanel;
        private List<TorControl.Circuit> circuits = new List<TorControl.Circuit>();
        private Dictionary<string, TorControl.Relay> fpMap;
        private int circuitScrollTop = 0;
        private Rectangle cUpRect, cDownRect, cThumbRect;
        private class CircRow
        {
            public bool Header;
            public string Text;
            public Color HeadColor;
            public string CircuitId;
            public TorControl.Relay Relay;
            public string Nick;
            public TorControl.Stream Stream;
        }
        private readonly List<CircRow> circRows = new List<CircRow>();
        private List<TorControl.Stream> streams = new List<TorControl.Stream>();
        private string selCircuitId;

        private const int TB = 30, RW = 304;

        private const int ROW_H = 18, HEAD_H = 0, SB_W = 16, ARROW_H = 16;

        protected override bool FooterActsAsResizeGrip { get { return false; } }
        private Panel canvas;

        public AdvorWindow() : this(null) { }

        public AdvorWindow(NetConfig config) : base("OPT-ADVOR", "ADVOR \u2014 Advanced Onion Router")
        {
            cfg = config;
            Size = new Size(960, 610);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            KeyPreview = true;
            ClientArea.BackColor = Fab;
            canvas = SetupAlohaScroll(Fab);

            headFont = new Font("Lucida Console", 8.5f, FontStyle.Bold);
            rowFont  = new Font("Consolas", 9f);

            topBar = new Panel { BackColor = Fab };
            var lblCtl = new Label { Text = "control", Left = 6, Top = 8, AutoSize = true, ForeColor = LblDark, BackColor = Color.Transparent };
            portInput = new TextBox { Left = 58, Top = 5, Width = 56, Text = "9051", BorderStyle = BorderStyle.Fixed3D, BackColor = Color.Black, ForeColor = Green, Font = new Font("Consolas", 8.5f) };
            var btnConnect = new Button { Text = "Connect", Left = 120, Top = 4, Width = 72, Height = 22, FlatStyle = FlatStyle.System };
            var btnRefresh = new Button { Text = "Refresh", Left = 196, Top = 4, Width = 72, Height = 22, FlatStyle = FlatStyle.System };
            var lblFilter = new Label { Text = "filter", Left = 284, Top = 8, AutoSize = true, ForeColor = LblDark, BackColor = Color.Transparent };
            filterBox = new TextBox { Left = 322, Top = 5, Width = 220, BorderStyle = BorderStyle.Fixed3D, BackColor = Color.Black, ForeColor = Green, Font = new Font("Consolas", 8.5f) };
            btnConnect.Click += (s, e) => Connect();
            btnRefresh.Click += (s, e) => { LoadRelays(); RefreshCircuits(); };
            filterBox.TextChanged += (s, e) => { filterText = filterBox.Text ?? ""; ApplyFilter(); list.Invalidate(); };

            footStatus = new Label { Left = 552, Top = 8, Height = 16, AutoSize = false, ForeColor = LblDark, BackColor = Color.Transparent,
                                     Text = status, AutoEllipsis = true, TextAlign = ContentAlignment.MiddleLeft };
            topBar.Controls.AddRange(new Control[] { lblCtl, portInput, btnConnect, btnRefresh, lblFilter, filterBox, footStatus });

            rightPanel = new Panel { BackColor = Fab };
            BuildRightPanel();

            list = new Panel { BackColor = Color.Black, BorderStyle = BorderStyle.Fixed3D };
            typeof(Panel).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(list, true, null);
            list.Paint += List_Paint;
            list.MouseDown += List_MouseDown;
            list.MouseWheel += (s, e) => ScrollBy(e.Delta > 0 ? -3 : 3);

            circuitPanel = new Panel { BackColor = Color.Black, BorderStyle = BorderStyle.Fixed3D };
            typeof(Panel).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(circuitPanel, true, null);
            circuitPanel.Paint += Circuits_Paint;
            circuitPanel.MouseDown += Circuits_MouseDown;
            circuitPanel.MouseWheel += (s, e) => CircuitScrollBy(e.Delta > 0 ? -2 : 2);

            canvas.Controls.Add(topBar);
            canvas.Controls.Add(rightPanel);
            canvas.Controls.Add(list);
            canvas.Controls.Add(circuitPanel);
            list.BringToFront();
            circuitPanel.BringToFront();

            this.KeyDown += Advor_KeyDown;
            canvas.Resize += (s, e) => LayoutAdvor();
            this.Shown += (s, e) => { LayoutAdvor(); Connect(); };
            trafficTimer = new System.Windows.Forms.Timer { Interval = 2000 };
            trafficTimer.Tick += TrafficTick;
            sfTimer = new System.Windows.Forms.Timer { Interval = 500 };
            sfTimer.Tick += (s, e) => SfTick();
            this.FormClosed += (s, e) =>
            {
                try { trafficTimer?.Stop(); } catch { }
                try { sfTimer?.Stop(); sfCts?.Cancel(); } catch { }
                try { ctrl?.Dispose(); } catch { }
                headFont?.Dispose(); rowFont?.Dispose();
            };
        }

        private Label L(Panel p, string t, int y) { var l = new Label { Text = t, Left = 8, Top = y, AutoSize = true, ForeColor = LblDark, BackColor = Color.Transparent }; p.Controls.Add(l); return l; }

        private TextBox Box(Panel p, int y, int h) { var b = new TextBox { Left = 8, Top = y, Width = RW - 16, Height = h, Multiline = h > 24, ScrollBars = h > 24 ? ScrollBars.Vertical : ScrollBars.None, BorderStyle = BorderStyle.Fixed3D, Font = new Font("Consolas", 8.5f), BackColor = Color.Black, ForeColor = Green }; p.Controls.Add(b); return b; }
        private Button Btn(Panel p, string t, int x, int y, int w, Action a) { var b = new Button { Text = t, Left = x, Top = y, Width = w, Height = 22, FlatStyle = FlatStyle.System }; b.Click += (s, e) => a(); p.Controls.Add(b); return b; }
        private CheckBox Chk(Panel p, string t, int y, bool on = false) { var c = new CheckBox { Text = t, Left = 8, Top = y, AutoSize = true, Checked = on, ForeColor = LblDark, BackColor = Color.Transparent }; p.Controls.Add(c); return c; }
        private Label Hint(Panel p, int y, string t, int h = 96) { var l = new Label { Left = 8, Top = y, Width = RW - 16, Height = h, AutoSize = false, ForeColor = LblDark, BackColor = Color.Transparent, Font = new Font("Tahoma", 7.75f), Text = t }; p.Controls.Add(l); return l; }

        private void BuildRightPanel()
        {
            sectionBox = new ComboBox { Left = 8, Top = 6, Width = RW - 16, DropDownStyle = ComboBoxStyle.DropDownList };
            sectionBox.Items.AddRange(new object[] { "Circuit", "Identity", "Bridges", "Proxy", "Firewall", "Tuning", "Address map", "Onion services", "Server (relay)", "Striped fetch" });
            sectionBox.SelectedIndexChanged += (s, e) => ShowSection((string)sectionBox.SelectedItem);
            rightPanel.Controls.Add(sectionBox);

            sections["Circuit"]        = BuildCircuit();
            sections["Identity"]       = BuildIdentity();
            sections["Bridges"]        = BuildBridges();
            sections["Proxy"]          = BuildProxy();
            sections["Firewall"]       = BuildFirewall();
            sections["Tuning"]         = BuildTuning();
            sections["Address map"]    = BuildAddrMap();
            sections["Onion services"] = BuildOnions();
            sections["Server (relay)"] = BuildServer();
            sections["Striped fetch"]  = BuildStriped();
            foreach (var kv in sections) { kv.Value.Visible = false; rightPanel.Controls.Add(kv.Value); }

            bwPanel = new Panel { BackColor = Color.Black, BorderStyle = BorderStyle.Fixed3D };
            typeof(Panel).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(bwPanel, true, null);
            bwPanel.Paint += Bw_Paint;
            rightPanel.Controls.Add(bwPanel);

            sectionBox.SelectedIndex = 0;
        }

        private void ShowSection(string name)
        {
            foreach (var kv in sections) kv.Value.Visible = (kv.Key == name);
        }

        private Panel NewSection() { return new Panel { BackColor = Fab, Left = 0, Top = 34, Width = RW, Height = 416 }; }

        private Panel BuildCircuit()
        {
            var p = NewSection();
            L(p, "Exit nodes", 4);     Btn(p, "+ sel", RW - 70, 2, 60, () => AddSelectedTo(exitBox));    exitBox    = Box(p, 22, 38);
            L(p, "Entry nodes", 66);   Btn(p, "+ sel", RW - 70, 64, 60, () => AddSelectedTo(entryBox));  entryBox   = Box(p, 84, 38);
            L(p, "Exclude nodes", 128);Btn(p, "+ sel", RW - 70, 126, 60, () => AddSelectedTo(excludeBox)); excludeBox = Box(p, 146, 38);
            strictCheck = Chk(p, "StrictNodes (obey strictly)", 192);
            Btn(p, "Apply route", 8, 216, 116, ApplyRoute);
            Btn(p, "Clear", 130, 216, 76, ClearRoute);
            Hint(p, 248, "Pick a relay on the left, then \"+ sel\" to add its $fingerprint, or type {de}, {us} "
                       + "country codes one per line. StrictNodes forces Tor to obey \u2014 too small a set breaks "
                       + "connectivity. Exits are volunteer-run; treat as untrusted.", 62);

            L(p, "Build a path (hops in order)", 314);
            Btn(p, "+ sel", RW - 70, 312, 60, () => AddSelectedTo(pathBox));
            pathBox = Box(p, 332, 38);
            Btn(p, "Build circuit", 8, 376, 116, BuildPath);
            Btn(p, "Close selected", 130, 376, 110, CloseSelectedCircuit);
            return p;
        }

        private Panel BuildIdentity()
        {
            var p = NewSection();
            Btn(p, "New identity now", 8, 6, 140, () => { if (!Ready()) return; string e; if (ctrl.NewIdentity(out e)) { ctrl.ClearDnsCache(); SetStatus("NEWNYM sent \u2014 new circuits building"); } else SetStatus("NEWNYM failed: " + e); });
            autoIdCheck = Chk(p, "new identity automatically every", 40);
            autoIdMinBox = Box(p, 62, 22); autoIdMinBox.Width = 60; autoIdMinBox.Text = "10";
            var lblMin = new Label { Text = "minutes", Left = 74, Top = 65, AutoSize = true, ForeColor = LblDark, BackColor = Color.Transparent }; p.Controls.Add(lblMin);
            autoIdCountdown = new Label { Left = 8, Top = 92, Width = RW - 16, Height = 18, AutoSize = false, ForeColor = LblDark, BackColor = Color.Transparent, Font = new Font("Consolas", 8.5f), Text = "auto: off" };
            p.Controls.Add(autoIdCountdown);
            autoIdCheck.CheckedChanged += (s, e) => { nextIdentityUtc = autoIdCheck.Checked ? DateTime.UtcNow.AddMinutes(AutoIdMinutes()) : DateTime.MinValue; UpdateAutoIdLabel(); };
            autoIdMinBox.TextChanged += (s, e) => { if (autoIdCheck.Checked) { nextIdentityUtc = DateTime.UtcNow.AddMinutes(AutoIdMinutes()); UpdateAutoIdLabel(); } };
            Hint(p, 120, "SIGNAL NEWNYM asks Tor for fresh circuits and clears its DNS cache. Honest: Tor may "
                       + "rate-limit repeated signals (about one per 10 s) and a new exit is likely, not guaranteed. "
                       + "The timer runs only while this window is open and connected.", 80);
            return p;
        }

        private Panel BuildOnions()
        {
            var p = NewSection();
            L(p, "local target (host:port)", 4);   onionTargetBox = Box(p, 22, 22); onionTargetBox.Text = "127.0.0.1:8080";
            L(p, "virtual port", 50);              onionVPortBox  = Box(p, 68, 22); onionVPortBox.Width = 60; onionVPortBox.Text = "80";
            onionKeepKeyCheck = Chk(p, "keep the key (same .onion after a restart)", 96, true);
            Btn(p, "Publish onion", 8, 120, 116, PublishOnion);
            Btn(p, "Re-add saved", 130, 120, 100, RepublishSavedOnions);
            L(p, "live onion services (this Tor)", 150);
            onionList = new ListBox { Left = 8, Top = 168, Width = RW - 16, Height = 96, BackColor = Color.Black, ForeColor = Green, Font = new Font("Consolas", 8.5f), BorderStyle = BorderStyle.Fixed3D, HorizontalScrollbar = true };
            p.Controls.Add(onionList);
            Btn(p, "Copy .onion", 8, 270, 100, CopySelectedOnion);
            Btn(p, "Remove", 114, 270, 80, RemoveSelectedOnion);
            Btn(p, "Refresh", 200, 270, 70, RefreshOnions);
            Hint(p, 300, "ADD_ONION publishes a local port as a v3 onion service on the spot \u2014 no torrc edit, no "
                       + "restart. A CGI mount, a local site, anything on 127.0.0.1. \"keep the key\" saves the private "
                       + "key under Data\\tor-data\\onions so the address survives Tor restarts (Re-add saved).", 100);
            return p;
        }

        private Panel BuildStriped()
        {
            var p = NewSection();
            L(p, "URL (http/https)", 4); sfUrlBox = Box(p, 22, 22);
            L(p, "stripes", 50);
            sfParts = new ComboBox { Left = 8, Top = 68, Width = 60, DropDownStyle = ComboBoxStyle.DropDownList };
            sfParts.Items.AddRange(new object[] { "2", "4", "6", "8", "12", "16" }); sfParts.SelectedIndex = 1;
            p.Controls.Add(sfParts);
            var lblS = new Label { Text = "SOCKS port", Left = 80, Top = 53, AutoSize = true, ForeColor = LblDark, BackColor = Color.Transparent }; p.Controls.Add(lblS);
            sfSocksBox = new TextBox { Left = 80, Top = 68, Width = 60, BorderStyle = BorderStyle.Fixed3D, Font = new Font("Consolas", 8.5f), BackColor = Color.Black, ForeColor = Green, Text = DefaultSocksPort().ToString() }; p.Controls.Add(sfSocksBox);
            sfFetchBtn  = Btn(p, "Fetch", 150, 67, 64, StartStriped);
            sfCancelBtn = Btn(p, "Cancel", 218, 67, 64, () => { try { sfCts?.Cancel(); } catch { } });
            sfCancelBtn.Enabled = false;
            sfPanel = new Panel { Left = 8, Top = 98, Width = RW - 16, Height = 150, BackColor = Color.Black, BorderStyle = BorderStyle.Fixed3D };
            typeof(Panel).GetProperty("DoubleBuffered", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)?.SetValue(sfPanel, true, null);
            sfPanel.Paint += Sf_Paint;
            p.Controls.Add(sfPanel);
            sfStatus = new Label { Left = 8, Top = 254, Width = RW - 16, Height = 34, AutoSize = false, ForeColor = LblDark, BackColor = Color.Transparent, Font = new Font("Consolas", 8f), Text = "idle" };
            p.Controls.Add(sfStatus);
            Btn(p, "Open folder", 8, 292, 100, () => { try { string d = DownloadDir(); if (Directory.Exists(d)) System.Diagnostics.Process.Start("explorer.exe", "\"" + d + "\""); } catch { } });
            Hint(p, 320, "One file, N circuits: the resource is split into byte ranges and every range is pulled "
                       + "over its OWN Tor circuit at the same time (distinct SOCKS logins = distinct circuits), each "
                       + "piece written at its offset \u2014 the file recombines itself at ~N\u00d7 one circuit's speed. "
                       + "Watch the circuits inset: N streams on N circuits. Needs a server that honours Range.", 92);
            return p;
        }

        private Panel BuildBridges()
        {
            var p = NewSection();
            useBridgesCheck = Chk(p, "UseBridges", 4);
            L(p, "Bridge lines (one per line)", 28);
            bridgesBox = Box(p, 48, 90);
            Btn(p, "Apply bridges", 8, 144, 130, ApplyBridges);
            Hint(p, 176, "Each line is a Bridge address (obfs4/webtunnel/plain). Applied live via SETCONF. "
                       + "Pluggable-transport bridges also need the transport binary bundled and a "
                       + "ClientTransportPlugin line in the torrc \u2014 set that in the Tor window's torrc box.");
            return p;
        }

        private Panel BuildProxy()
        {
            var p = NewSection();
            L(p, "Outbound proxy type", 4);
            proxyType = new ComboBox { Left = 8, Top = 24, Width = RW - 16, DropDownStyle = ComboBoxStyle.DropDownList };
            proxyType.Items.AddRange(new object[] { "Socks5Proxy", "Socks4Proxy", "HTTPSProxy" });
            proxyType.SelectedIndex = 0;
            p.Controls.Add(proxyType);
            L(p, "host:port", 54);
            proxyHostBox = Box(p, 74, 22);
            Btn(p, "Apply proxy", 8, 104, 116, ApplyProxy);
            Btn(p, "Clear", 130, 104, 76, ClearProxy);
            Hint(p, 136, "Routes Tor's own connections out through another proxy first (proxy \u2192 Tor). "
                       + "Leave host:port blank and Clear to remove. Useful behind a corporate proxy.");
            return p;
        }

        private Panel BuildFirewall()
        {
            var p = NewSection();
            fascistCheck = Chk(p, "FascistFirewall (only reach common ports)", 4);
            L(p, "ReachableAddresses (e.g. *:80,*:443)", 28);
            reachBox = Box(p, 48, 22);
            Btn(p, "Apply firewall", 8, 78, 130, ApplyFirewall);
            Hint(p, 110, "For restrictive networks: FascistFirewall limits Tor to ports 80/443. "
                       + "ReachableAddresses is an explicit allowlist of address:port patterns Tor may use. "
                       + "If your network blocks Tor outright, use Bridges instead.");
            return p;
        }

        private Panel BuildTuning()
        {
            var p = NewSection();
            L(p, "CircuitBuildTimeout (s)", 4);   cbtBox = Box(p, 22, 22);
            L(p, "NewCircuitPeriod (s)", 50);     ncpBox = Box(p, 68, 22);
            L(p, "MaxCircuitDirtiness (s)", 96);  mcdBox = Box(p, 114, 22);
            distinctCheck = Chk(p, "EnforceDistinctSubnets", 146, true);
            guardsCheck   = Chk(p, "UseEntryGuards", 170, true);
            Btn(p, "Apply tuning", 8, 196, 130, ApplyTuning);
            Hint(p, 228, "Circuit lifetimes and guard behaviour. Blank numeric fields are left untouched. "
                       + "Disabling UseEntryGuards rotates entry relays more (worse for anonymity). "
                       + "EnforceDistinctSubnets keeps hops in different /16s.");
            return p;
        }

        private Panel BuildAddrMap()
        {
            var p = NewSection();
            L(p, "From (address)", 4);  mapFromBox = Box(p, 22, 22);
            L(p, "To (address)", 50);   mapToBox   = Box(p, 68, 22);
            Btn(p, "Add map", 8, 98, 116, AddAddrMap);
            Hint(p, 130, "MAPADDRESS remaps one address to another inside Tor \u2014 e.g. pin a host to a "
                       + "specific .onion, or alias a name. Takes effect immediately for new streams.");
            return p;
        }

        private Panel BuildServer()
        {
            var p = NewSection();
            L(p, "ORPort (inbound, must be reachable)", 4);  orPortBox    = Box(p, 22, 22);
            L(p, "Nickname", 50);                            nickBox      = Box(p, 68, 22);
            L(p, "ContactInfo", 96);                         contactBox   = Box(p, 114, 22);
            L(p, "RelayBandwidthRate KB/s", 142);            relayRateBox = Box(p, 160, 22);
            L(p, "RelayBandwidthBurst KB/s", 188);           relayBurstBox= Box(p, 206, 22);
            allowExitCheck = Chk(p, "Allow exit traffic (LEGAL EXPOSURE)", 234);
            Btn(p, "Start relay", 8, 260, 116, ApplyServer);
            Btn(p, "Stop relay", 130, 260, 90, StopServer);
            Hint(p, 290, "Donates bandwidth as a Tor relay. ORPort must be reachable from the internet "
                       + "(forward the port — Tor self-tests). Default is a NON-exit relay. Enabling exit "
                       + "means other people's traffic leaves through your IP: serious abuse / legal exposure, "
                       + "leave OFF unless you know what that means. Relays should stay online; a transient "
                       + "browser relay is poor for the network. Not persisted — add to torrc to keep it.");
            trafficReadout = new Label { Left = 8, Top = 392, Width = RW - 16, Height = 18, AutoSize = false, ForeColor = LblDark, BackColor = Color.Transparent, Font = new Font("Consolas", 8.5f), Text = "traffic: idle" };
            p.Controls.Add(trafficReadout);
            logTrafficCheck = Chk(p, "log traffic to tor/tmp", 414, true);
            return p;
        }

        private void ApplyServer()
        {
            if (!Ready()) return;
            string port = (orPortBox.Text ?? "").Trim();
            int pn;
            if (port.Length == 0 || !int.TryParse(port, out pn) || pn <= 0 || pn >= 65536) { SetStatus("enter a valid ORPort"); return; }
            var sets = new List<string> { "ORPort=" + pn };
            string nick = (nickBox.Text ?? "").Trim();
            if (nick.Length > 0) sets.Add("Nickname=\"" + nick + "\"");
            string contact = (contactBox.Text ?? "").Trim();
            if (contact.Length > 0) sets.Add("ContactInfo=\"" + contact + "\"");
            int n;
            if (int.TryParse((relayRateBox.Text ?? "").Trim(), out n) && n > 0)  sets.Add("RelayBandwidthRate=\"" + n + " KBytes\"");
            if (int.TryParse((relayBurstBox.Text ?? "").Trim(), out n) && n > 0) sets.Add("RelayBandwidthBurst=\"" + n + " KBytes\"");
            if (allowExitCheck.Checked) sets.Add("ExitRelay=1");
            else { sets.Add("ExitRelay=0"); sets.Add("ExitPolicy=\"reject *:*\""); }
            string err;
            if (ctrl.SetConf(string.Join(" ", sets), out err))
                { Persist(); SetStatus("relay starting on ORPort " + pn + " + saved — Tor self-tests reachability (watch STDOUT)"); }
            else SetStatus("relay start failed: " + err);
        }

        private void StopServer()
        {
            if (!Ready()) return;
            string err;
            if (ctrl.ResetConf("ORPort RelayBandwidthRate RelayBandwidthBurst ExitRelay", out err)) { Persist(); SetStatus("relay stopped + saved"); }
            else SetStatus("stop failed: " + err);
        }

        private const int MinCW = 740, MinCH = 544;
        private void LayoutAdvor()
        {
            int cw = Math.Max(AlohaContent.Width,  MinCW);
            int ch = Math.Max(AlohaContent.Height, MinCH);
            topBar.SetBounds(0, 0, cw, TB);
            if (footStatus != null) footStatus.SetBounds(552, 8, Math.Max(60, cw - 552 - 8), 16);
            rightPanel.SetBounds(cw - RW, TB, RW, ch - TB);

            int rh = ch - TB;
            int secH = Math.Max(200, rh - 34 - BW_H - 6);
            foreach (var kv in sections) kv.Value.SetBounds(0, 34, RW, secH);
            if (bwPanel != null) { bwPanel.SetBounds(4, rh - BW_H - 2, RW - 8, BW_H); bwPanel.Invalidate(); }

            int leftW = Math.Max(120, cw - RW);
            int leftH = ch - TB;
            const int gap = 6;
            int circuitH = Math.Min(300, Math.Max(140, leftH / 3));
            int relayH = Math.Max(100, leftH - circuitH - gap);
            circuitH = leftH - relayH - gap;
            list.SetBounds(0, TB, leftW, relayH);
            circuitPanel.SetBounds(0, TB + relayH + gap, leftW, circuitH);
            list.Invalidate();
            circuitPanel.Invalidate();
        }

        private void SetStatus(string s) { status = s; if (footStatus != null) { footStatus.Text = s; footStatus.Invalidate(); } }
        private bool Ready() { if (ctrl == null || !ctrl.Authenticated) { SetStatus("connect first"); return false; } return true; }
        private void Persist() { if (ctrl == null || !ctrl.Authenticated) return; string e; ctrl.SaveConf(out e); ctrl.NewIdentity(out e); }
        private int ParsePort() { int v; return (int.TryParse((portInput.Text ?? "").Trim(), out v) && v > 0 && v < 65536) ? v : 9051; }

        private static string CookieHint()
        {
            var t = TorProcess.Active;
            if (t != null && t.Running) return t.CookiePath;
            string portable = Path.Combine(AlohaPaths.TorData, "control_auth_cookie");
            if (File.Exists(portable)) return portable;
            string legacy = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Aloha", "tor-data", "control_auth_cookie");
            return File.Exists(legacy) ? legacy : portable;
        }

        private int ControlPort()
        {
            var t = TorProcess.Active;
            if (t != null && t.Running)
            {
                if (portInput != null && portInput.Text.Trim() != t.ControlPort.ToString()) portInput.Text = t.ControlPort.ToString();
                return t.ControlPort;
            }
            return ParsePort();
        }

        private void Connect()
        {
            try { ctrl?.Dispose(); } catch { }
            ctrl = null;
            int port = ControlPort();

            ctrl = new TorControl(CookieHint(), port);
            string err;
            if (!ctrl.Connect(out err))
            {
                ctrl = null;
                SetStatus(err.StartsWith("no Tor control port")
                    ? err + " \u2014 start Tor (Options \u203a Tor) or fix the control port"
                    : err);
                return;
            }
            SetStatus("connected via " + ctrl.AuthMethod +
                      (ctrl.CookieFileUsed.Length > 0 ? " (" + ctrl.CookieFileUsed + ")" : "") + " \u2014 loading relays...");
            LoadRelays();
            RefreshCircuits();
            RefreshOnions();
            if (sfSocksBox != null && TorProcess.Active != null && TorProcess.Active.Running) sfSocksBox.Text = TorProcess.Active.SocksPort.ToString();
            tStamp = DateTime.MinValue;
            trafficTimer?.Start();
        }

        private void LoadRelays()
        {
            if (!Ready()) return;
            loading = true; list.Invalidate();
            string hint = CookieHint(); int port = ControlPort();
            var shared = ctrl;
            var t = new Thread(() =>
            {

                string err = null;
                List<TorControl.Relay> rows = null;
                try
                {
                    TorControl bulk = new TorControl(hint, port);
                    string cerr;
                    bool own = bulk.Connect(out cerr);
                    if (!own) { bulk.Dispose(); bulk = shared; }
                    try { rows = bulk.GetAllRelays(out err); }
                    finally { if (own) bulk.Dispose(); }
                    if (err == null && rows != null)
                    {
                        foreach (var r in rows)
                        {

                            try { r.Country = geo.Lookup(r.Ip); } catch { r.Country = "??"; }
                        }
                    }
                }
                catch (Exception ex)
                {
                    err = ex.GetType().Name + ": " + ex.Message;
                }
                if (err == null && rows == null) err = "control returned no relay list";

                Action apply = () =>
                {
                    loading = false;
                    try
                    {
                        if (err != null) { SetStatus("relay fetch failed: " + err); list.Invalidate(); return; }
                        rows.Sort((a, b) => b.BandwidthKb.CompareTo(a.BandwidthKb));
                        relays = rows;
                        fpMap = new Dictionary<string, TorControl.Relay>();
                        foreach (var rr in relays)
                            if (!string.IsNullOrEmpty(rr.Fingerprint)) fpMap[rr.Fingerprint.ToUpperInvariant()] = rr;
                        ApplyFilter();
                        RebuildCircuitRows();
                    }
                    catch (Exception ex)
                    {
                        SetStatus("relay list failed to build: " + ex.Message);
                    }
                    list.Invalidate();
                    if (circuitPanel != null) circuitPanel.Invalidate();
                };

                try
                {
                    if (IsHandleCreated && !IsDisposed) BeginInvoke(apply);
                    else loading = false;
                }
                catch { loading = false; }
            });
            t.IsBackground = true;
            t.Start();
        }

        private void ApplyFilter()
        {
            if (string.IsNullOrEmpty(filterText)) view = new List<TorControl.Relay>(relays);
            else
            {
                string f = filterText.ToLowerInvariant();
                view = relays.FindAll(r =>
                    (r.Nick ?? "").ToLowerInvariant().Contains(f) ||
                    (r.Country ?? "").ToLowerInvariant().Contains(f) ||
                    (r.Fingerprint ?? "").ToLowerInvariant().Contains(f) ||
                    FlagStr(r).ToLowerInvariant().Contains(f));
            }
            selIdx = 0; scrollTop = 0;
            string flt = string.IsNullOrEmpty(filterText) ? "" : " (filter: " + filterText + ")";
            SetStatus(view.Count + " relays" + flt + (geo.Loaded ? "" : "  [geoip not loaded: regions show ??]"));
        }

        private static string FlagStr(TorControl.Relay r)
        {
            var sb = new StringBuilder();
            sb.Append(r.IsExit ? 'E' : '-'); sb.Append(r.IsGuard ? 'G' : '-');
            sb.Append(r.IsFast ? 'F' : '-'); sb.Append(r.IsStable ? 'S' : '-');
            return sb.ToString();
        }

        private TorControl.Relay Sel() { return (selIdx >= 0 && selIdx < view.Count) ? view[selIdx] : null; }

        private void AddSelectedTo(TextBox box)
        {
            var r = Sel();
            if (r == null || string.IsNullOrEmpty(r.Fingerprint)) { SetStatus("select a relay first"); return; }
            string tok = "$" + r.Fingerprint, cur = box.Text ?? "";
            if (cur.Contains(tok)) return;
            box.Text = (cur.Trim().Length == 0) ? tok : (cur.TrimEnd() + "\r\n" + tok);
        }

        private void AddRelayTo(TextBox box, TorControl.Relay r)
        {
            if (box == null || r == null || string.IsNullOrEmpty(r.Fingerprint)) return;
            string tok = "$" + r.Fingerprint, cur = box.Text ?? "";
            if (!cur.Contains(tok)) box.Text = (cur.Trim().Length == 0) ? tok : (cur.TrimEnd() + "\r\n" + tok);
        }

        private void OpenDetail(TorControl.Relay r)
        {
            if (r == null) return;
            var d = new AdvorDetailWindow(r);
            d.OnUseExit  += n => { sectionBox.SelectedItem = "Circuit"; AddRelayTo(exitBox,  n); SetStatus("added " + n.Nick + " to exit \u2014 press Apply route"); };
            d.OnUseEntry += n => { sectionBox.SelectedItem = "Circuit"; AddRelayTo(entryBox, n); SetStatus("added " + n.Nick + " to entry \u2014 press Apply route"); };
            d.Show(this);
        }

        private bool pollBusy;

        private void TrafficTick(object sender, EventArgs e) { StartPoll(true); }

        private void StartPoll(bool withTraffic)
        {
            var c = ctrl;
            if (c == null || !c.Authenticated || pollBusy) return;
            pollBusy = true;
            bool doNewnym = AutoIdentityDue();
            ThreadPool.QueueUserWorkItem(_ =>
            {
                List<TorControl.Circuit> cs = null; List<TorControl.Stream> ss = null;
                string cerr = null, serr = null, terr = null, nerr = null;
                long rd = 0, wr = 0; bool gotTraffic = false, newnymOk = false;
                try
                {
                    if (doNewnym) { newnymOk = c.NewIdentity(out nerr); if (newnymOk) c.ClearDnsCache(); }
                    cs = c.GetCircuits(out cerr);
                    ss = c.GetStreams(out serr);
                    if (withTraffic) gotTraffic = c.GetTraffic(out rd, out wr, out terr);
                }
                catch (Exception ex) { cerr = ex.Message; }

                Action apply = () =>
                {
                    pollBusy = false;
                    try
                    {
                        if (IsDisposed || ctrl != c) return;
                        if (doNewnym)
                        {
                            SetStatus(newnymOk ? "auto NEWNYM sent " + DateTime.Now.ToString("HH:mm:ss") : "auto NEWNYM failed: " + nerr);
                            nextIdentityUtc = DateTime.UtcNow.AddMinutes(AutoIdMinutes());
                        }
                        UpdateAutoIdLabel();
                        if (cerr != null) SetStatus("circuits: " + cerr);
                        else
                        {
                            circuits = cs;
                            if (serr == null && ss != null) streams = ss;
                            RebuildCircuitRows();
                            circuitPanel?.Invalidate();
                        }
                        if (withTraffic && gotTraffic) ApplyTraffic(rd, wr);
                    }
                    catch (Exception ex) { SetStatus("poll apply failed: " + ex.Message); }
                };
                try { if (IsHandleCreated && !IsDisposed) BeginInvoke(apply); else pollBusy = false; }
                catch { pollBusy = false; }
            });
        }

        private void ApplyTraffic(long rd, long wr)
        {
            var now = DateTime.UtcNow;
            string rate = "";
            if (tStamp != DateTime.MinValue)
            {
                double secs = (now - tStamp).TotalSeconds;
                if (secs > 0.1)
                {
                    double dr = (rd - tRead) / secs, dw = (wr - tWritten) / secs;
                    rate = "  (\u2193 " + Rate(dr) + " \u2191 " + Rate(dw) + ")";

                    bwDown.Add(Math.Max(0, dr)); bwUp.Add(Math.Max(0, dw));
                    while (bwDown.Count > BW_SAMPLES) bwDown.RemoveAt(0);
                    while (bwUp.Count > BW_SAMPLES) bwUp.RemoveAt(0);
                    bwPanel?.Invalidate();
                }
            }
            tRead = rd; tWritten = wr; tStamp = now;
            if (trafficReadout != null) trafficReadout.Text = "\u2193 " + Bytes(rd) + " \u00B7 \u2191 " + Bytes(wr) + rate;
            if (logTrafficCheck != null && logTrafficCheck.Checked) LogTraffic(rd, wr);
        }

        private void Bw_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int w = bwPanel.ClientSize.Width, h = bwPanel.ClientSize.Height;
            g.Clear(Color.Black);
            using (var grid = new Pen(Color.FromArgb(0x14, 0x2A, 0x18)))
                for (int y = h / 4; y < h; y += Math.Max(8, h / 4)) g.DrawLine(grid, 0, y, w, y);
            double max = 1024;
            foreach (var v in bwDown) if (v > max) max = v;
            foreach (var v in bwUp) if (v > max) max = v;
            DrawSeries(g, bwDown, Green, max, w, h);
            DrawSeries(g, bwUp, Color.FromArgb(0xFF, 0xB6, 0x2E), max, w, h);
            string cur = bwDown.Count > 0 ? "\u2193 " + Rate(bwDown[bwDown.Count - 1]) + "  \u2191 " + Rate(bwUp[bwUp.Count - 1]) : "bandwidth \u2014 waiting for samples";
            using (var f = new Font("Consolas", 7.5f))
            using (var b = new SolidBrush(Green))
                g.DrawString(cur + "   peak " + Rate(max), f, b, 4, 2);
        }

        private static void DrawSeries(Graphics g, List<double> s, Color c, double max, int w, int h)
        {
            if (s.Count < 2) return;
            var pts = new PointF[s.Count];
            float step = (float)(w - 2) / (BW_SAMPLES - 1);
            float x0 = w - 1 - step * (s.Count - 1);
            for (int i = 0; i < s.Count; i++)
                pts[i] = new PointF(x0 + i * step, (float)(h - 2 - (s[i] / max) * (h - 16)));
            using (var p = new Pen(c, 1.5f)) g.DrawLines(p, pts);
        }

        private int AutoIdMinutes() { int m; return (autoIdMinBox != null && int.TryParse(autoIdMinBox.Text.Trim(), out m) && m >= 1) ? m : 10; }

        private void UpdateAutoIdLabel()
        {
            if (autoIdCountdown == null) return;
            if (autoIdCheck == null || !autoIdCheck.Checked || nextIdentityUtc == DateTime.MinValue) { autoIdCountdown.Text = "auto: off"; return; }
            var left = nextIdentityUtc - DateTime.UtcNow;
            if (left < TimeSpan.Zero) left = TimeSpan.Zero;
            autoIdCountdown.Text = "auto: next identity in " + ((int)left.TotalMinutes).ToString("0") + ":" + left.Seconds.ToString("00");
        }

        private bool AutoIdentityDue()
        {
            if (autoIdCheck == null || !autoIdCheck.Checked) return false;
            if (nextIdentityUtc == DateTime.MinValue) nextIdentityUtc = DateTime.UtcNow.AddMinutes(AutoIdMinutes());
            if (DateTime.UtcNow < nextIdentityUtc) return false;
            nextIdentityUtc = DateTime.UtcNow.AddMinutes(AutoIdMinutes());
            return true;
        }

        private void BuildPath()
        {
            if (!Ready()) return;
            string path = Joined(pathBox);
            if (path.Length == 0) { SetStatus("add hops to the path first (\"+ sel\" on a relay, entry first, exit last)"); return; }
            string id, err;
            if (ctrl.ExtendCircuit(path, out id, out err))
            {
                selCircuitId = id;
                SetStatus("building circuit #" + id + " along " + path.Split(',').Length + " hops \u2014 watch the inset");
                RefreshCircuits();
            }
            else SetStatus("EXTENDCIRCUIT failed: " + err);
        }

        private void CloseSelectedCircuit()
        {
            if (!Ready()) return;
            if (string.IsNullOrEmpty(selCircuitId)) { SetStatus("click a circuit heading in the inset first"); return; }
            string err;
            if (ctrl.CloseCircuit(selCircuitId, out err)) { SetStatus("closed circuit #" + selCircuitId); selCircuitId = null; RefreshCircuits(); }
            else SetStatus("CLOSECIRCUIT failed: " + err);
        }

        private static string OnionKeyDir { get { return Path.Combine(AlohaPaths.TorData, "onions"); } }

        private void PublishOnion()
        {
            if (!Ready()) return;
            string target = (onionTargetBox.Text ?? "").Trim();
            string vport = (onionVPortBox.Text ?? "").Trim();
            int vp;
            if (target.Length == 0 || target.IndexOf(':') < 0) { SetStatus("target must be host:port, e.g. 127.0.0.1:8080"); return; }
            if (!int.TryParse(vport, out vp) || vp <= 0 || vp > 65535) { SetStatus("virtual port must be 1..65535"); return; }
            string portSpec = vp + "," + target;
            string id, key, err;
            if (!ctrl.AddOnion("NEW:ED25519-V3", new[] { portSpec }, true, out id, out key, out err)) { SetStatus("ADD_ONION failed: " + err); return; }
            onionPorts[id] = portSpec;
            if (onionKeepKeyCheck.Checked && !string.IsNullOrEmpty(key))
            {
                try
                {
                    Directory.CreateDirectory(OnionKeyDir);
                    File.WriteAllText(Path.Combine(OnionKeyDir, id + ".key"), key + "\r\n" + portSpec + "\r\n");
                }
                catch (Exception ex) { SetStatus("published, but the key could not be saved: " + ex.Message); }
            }
            SetStatus("published " + id + ".onion \u2192 " + target + " (copied to clipboard)");
            try { Clipboard.SetText(id + ".onion"); } catch { }
            RefreshOnions();
        }

        private void RepublishSavedOnions()
        {
            if (!Ready()) return;
            int added = 0, failed = 0;
            try
            {
                if (!Directory.Exists(OnionKeyDir)) { SetStatus("no saved onion keys under " + OnionKeyDir); return; }
                string liveErr; var live = ctrl.GetOnions(out liveErr);
                foreach (var f in Directory.GetFiles(OnionKeyDir, "*.key"))
                {
                    string wantId = Path.GetFileNameWithoutExtension(f);
                    if (live.Contains(wantId)) continue;
                    var lines = File.ReadAllLines(f);
                    if (lines.Length < 2) continue;
                    string id, key, err;
                    if (ctrl.AddOnion(lines[0].Trim(), new[] { lines[1].Trim() }, true, out id, out key, out err)) { onionPorts[id] = lines[1].Trim(); added++; }
                    else failed++;
                }
            }
            catch (Exception ex) { SetStatus("re-add failed: " + ex.Message); return; }
            SetStatus("re-added " + added + " saved onion service(s)" + (failed > 0 ? ", " + failed + " failed" : ""));
            RefreshOnions();
        }

        private void RefreshOnions()
        {
            if (ctrl == null || !ctrl.Authenticated || onionList == null) return;
            string err;
            var ids = ctrl.GetOnions(out err);
            onionList.Items.Clear();
            foreach (var id in ids)
            {
                string ports; onionPorts.TryGetValue(id, out ports);
                if (ports == null)
                {
                    try { var f = Path.Combine(OnionKeyDir, id + ".key"); if (File.Exists(f)) { var l = File.ReadAllLines(f); if (l.Length > 1) ports = l[1].Trim(); } } catch { }
                }
                onionList.Items.Add(id + ".onion" + (ports != null ? "   \u2192 " + ports.Replace(",", " \u2192 ") : ""));
            }
            if (onionList.Items.Count == 0) onionList.Items.Add("(none published on this Tor)");
        }

        private string SelectedOnionId()
        {
            var s = onionList != null ? onionList.SelectedItem as string : null;
            if (string.IsNullOrEmpty(s) || s.StartsWith("(")) return null;
            int i = s.IndexOf(".onion", StringComparison.Ordinal);
            return i > 0 ? s.Substring(0, i) : null;
        }

        private void CopySelectedOnion()
        {
            var id = SelectedOnionId();
            if (id == null) { SetStatus("select an onion service in the list"); return; }
            try { Clipboard.SetText(id + ".onion"); SetStatus("copied " + id + ".onion"); } catch { }
        }

        private void RemoveSelectedOnion()
        {
            if (!Ready()) return;
            var id = SelectedOnionId();
            if (id == null) { SetStatus("select an onion service in the list"); return; }
            string err;
            if (ctrl.DelOnion(id, out err))
            {
                onionPorts.Remove(id);
                try { var f = Path.Combine(OnionKeyDir, id + ".key"); if (File.Exists(f)) File.Delete(f); } catch { }
                SetStatus("removed " + id + ".onion (and its saved key)");
                RefreshOnions();
            }
            else SetStatus("DEL_ONION failed: " + err);
        }

        private int DefaultSocksPort()
        {
            var t = TorProcess.Active;
            if (t != null && t.Running) return t.SocksPort;
            if (cfg != null && (cfg.ProxyScheme ?? "").StartsWith("socks", StringComparison.OrdinalIgnoreCase) && cfg.ProxyPort > 0) return cfg.ProxyPort;
            return 9050;
        }

        private string DownloadDir()
        {
            if (cfg != null && !string.IsNullOrWhiteSpace(cfg.DownloadDir)) return cfg.DownloadDir;
            return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");
        }

        private async void StartStriped()
        {
            if (sf != null) { SetStatus("a striped fetch is already running"); return; }
            string url = (sfUrlBox.Text ?? "").Trim();
            if (url.Length == 0) { SetStatus("enter a URL"); return; }
            if (!url.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && !url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
            int parts; int.TryParse((string)sfParts.SelectedItem, out parts); if (parts <= 0) parts = 4;
            int socks; if (!int.TryParse(sfSocksBox.Text.Trim(), out socks) || socks <= 0) socks = DefaultSocksPort();

            string name;
            try { name = Path.GetFileName(new Uri(url).AbsolutePath); } catch { name = ""; }
            if (string.IsNullOrWhiteSpace(name) || name == "/") name = "striped_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".bin";
            name = Uri.UnescapeDataString(name);
            foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
            string dest = Path.Combine(DownloadDir(), name);
            int k = 1; string stem = Path.GetFileNameWithoutExtension(dest), ext = Path.GetExtension(dest);
            while (File.Exists(dest)) dest = Path.Combine(DownloadDir(), stem + " (" + (k++) + ")" + ext);

            sf = new TorStripedFetch { ProxyHost = "127.0.0.1", ProxyPort = socks };
            sf.Log += m => { try { ActivityLog.General("striped: " + m); } catch { } };
            sfCts = new System.Threading.CancellationTokenSource();
            sfFetchBtn.Enabled = false; sfCancelBtn.Enabled = true;
            sfLastDone = 0; sfLastStamp = DateTime.UtcNow; sfRate = 0; sfStart = DateTime.UtcNow; sfLastFile = null;
            sfStatus.Text = "probing " + url + " via 127.0.0.1:" + socks + " \u2026";
            sfPanel.Invalidate();
            sfTimer.Start();
            SetStatus("striped fetch: " + parts + " circuits \u2192 " + name);
            try
            {
                var fetch = sf;
                string saved = await fetch.DownloadAsync(url, dest, parts, sfCts.Token);
                double secs = Math.Max(0.001, (DateTime.UtcNow - sfStart).TotalSeconds);
                sfLastFile = saved;
                sfStatus.Text = "saved " + Path.GetFileName(saved) + "  \u00B7  " + Bytes(fetch.TotalBytes) + " in " + secs.ToString("0.0") + " s  \u00B7  avg " + Rate(fetch.TotalBytes / secs) + " over " + fetch.Stripes.Count + " circuit(s)";
                SetStatus("striped fetch done \u2192 " + saved);
                ActivityLog.General("striped fetch saved " + saved);
            }
            catch (OperationCanceledException) { sfStatus.Text = "cancelled"; SetStatus("striped fetch cancelled"); try { File.Delete(dest); } catch { } }
            catch (Exception ex) { sfStatus.Text = "failed: " + ex.Message; SetStatus("striped fetch failed: " + ex.Message); }
            finally
            {
                sfTimer.Stop();
                sfFetchBtn.Enabled = true; sfCancelBtn.Enabled = false;
                sfPanel.Invalidate();
                sf = null; sfCts = null;
            }
        }

        private void SfTick()
        {
            var f = sf;
            if (f == null) return;
            long done = 0; foreach (var s in f.Stripes) done += s.Done;
            var now = DateTime.UtcNow;
            double secs = (now - sfLastStamp).TotalSeconds;
            if (secs >= 0.4)
            {
                double inst = (done - sfLastDone) / secs;
                sfRate = sfRate <= 0 ? inst : sfRate * 0.6 + inst * 0.4;
                sfLastDone = done; sfLastStamp = now;
            }
            if (f.TotalBytes > 0)
            {
                double eta = sfRate > 1 ? (f.TotalBytes - done) / sfRate : -1;
                sfStatus.Text = Bytes(done) + " / " + Bytes(f.TotalBytes) + "  \u00B7  " + Rate(sfRate) + (eta >= 0 ? "  \u00B7  ~" + ((int)eta / 60) + ":" + ((int)eta % 60).ToString("00") + " left" : "") +
                                (f.Stripes.Count > 0 ? "  \u00B7  " + f.Stripes.Count + " circuits" : "");
            }
            else if (f.Stripes.Count > 0) sfStatus.Text = Bytes(done) + " received  \u00B7  " + Rate(sfRate);
            sfPanel.Invalidate();
        }

        private void Sf_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int w = sfPanel.ClientSize.Width, h = sfPanel.ClientSize.Height;
            g.Clear(Color.Black);
            var f = sf;
            List<TorStripedFetch.Stripe> stripes = f != null ? new List<TorStripedFetch.Stripe>(f.Stripes) : null;
            using (var fnt = new Font("Consolas", 7.5f))
            {
                if (stripes == null || stripes.Count == 0)
                {
                    using (var b = new SolidBrush(Dim))
                        g.DrawString(f != null ? "probing\u2026" : (sfLastFile != null ? "last: " + Path.GetFileName(sfLastFile) : "no fetch yet \u2014 every stripe is one circuit"), fnt, b, 6, 6);
                    return;
                }
                int rowH = Math.Max(10, Math.Min(22, (h - 8) / stripes.Count));
                int y = 4;
                foreach (var s in stripes)
                {
                    double frac = s.Length > 0 ? Math.Min(1.0, (double)s.Done / s.Length) : 0;
                    int barW = w - 12;
                    using (var bg = new SolidBrush(Color.FromArgb(0x10, 0x1E, 0x12))) g.FillRectangle(bg, 6, y, barW, rowH - 3);
                    Color fill = s.Status == "done" ? GreenHi : (s.Status.StartsWith("fail") ? Color.FromArgb(0xB6, 0x43, 0x1F) : Green);
                    using (var fb = new SolidBrush(fill)) g.FillRectangle(fb, 6, y, (int)(barW * frac), rowH - 3);
                    string label = "#" + s.Index + " " + (s.Length > 0 ? ((int)(frac * 100)).ToString().PadLeft(3) + "%" : Bytes(s.Done)) + "  " + s.Status + "  " + s.Isolation;
                    using (var tb = new SolidBrush(frac > 0.5 ? Color.Black : Green)) g.DrawString(Clip(label, 60), fnt, tb, 8, y + Math.Max(0, (rowH - 3 - 11) / 2));
                    y += rowH;
                }
            }
        }

        private static string Bytes(long b)
        {
            if (b >= 1L << 30) return (b / 1073741824.0).ToString("0.00") + " GB";
            if (b >= 1L << 20) return (b / 1048576.0).ToString("0.0") + " MB";
            if (b >= 1L << 10) return (b / 1024.0).ToString("0.0") + " KB";
            return b + " B";
        }
        private static string Rate(double bps)
        {
            if (bps >= 1 << 20) return (bps / 1048576.0).ToString("0.0") + " MB/s";
            if (bps >= 1 << 10) return (bps / 1024.0).ToString("0.0") + " KB/s";
            return ((long)bps) + " B/s";
        }

        private void LogTraffic(long rd, long wr)
        {
            try
            {
                string dir;
                try { dir = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "tor", "tmp"); System.IO.Directory.CreateDirectory(dir); }
                catch { dir = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Aloha", "tor-data", "tmp"); System.IO.Directory.CreateDirectory(dir); }
                System.IO.File.AppendAllText(System.IO.Path.Combine(dir, "relay_traffic.log"),
                    DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "\tread=" + rd + "\twritten=" + wr + "\r\n");
            }
            catch { }
        }

        private static string Joined(TextBox box)
        {
            var outp = new List<string>();
            foreach (var raw in (box.Text ?? "").Replace("\r", "").Split('\n'))
                foreach (var tok in raw.Split(new[] { ' ', ',', '\t' }, StringSplitOptions.RemoveEmptyEntries))
                    outp.Add(tok.Trim());
            return string.Join(",", outp);
        }

        private void RunSet(string setconf)
        {
            if (!Ready()) return;
            string e;
            if (ctrl.SetConf(setconf, out e)) { Persist(); SetStatus("applied + saved \u00B7 " + setconf); }
            else SetStatus("failed: " + e);
        }

        private void ApplyRoute()
        {
            if (!Ready()) return;
            var sets = new List<string>(); var resets = new List<string>();
            void One(string key, TextBox box) { string v = Joined(box); if (v.Length > 0) sets.Add(key + "=\"" + v + "\""); else resets.Add(key); }
            One("ExitNodes", exitBox); One("EntryNodes", entryBox); One("ExcludeNodes", excludeBox);
            sets.Add("StrictNodes=" + (strictCheck.Checked ? "1" : "0"));
            string err;
            if (resets.Count > 0 && !ctrl.ResetConf(string.Join(" ", resets), out err)) { SetStatus("reset failed: " + err); return; }
            if (sets.Count > 0 && !ctrl.SetConf(string.Join(" ", sets), out err)) { SetStatus("apply failed: " + err); return; }
            Persist();
            SetStatus("route applied + saved to torrc \u2014 circuits rebuilt");
        }

        private void ClearRoute()
        {
            exitBox.Text = entryBox.Text = excludeBox.Text = ""; strictCheck.Checked = false;
            if (ctrl != null && ctrl.Authenticated)
            {
                string err;
                if (ctrl.ResetConf("ExitNodes EntryNodes ExcludeNodes StrictNodes", out err)) { Persist(); SetStatus("route cleared + saved"); }
                else SetStatus("clear failed: " + err);
            }
        }

        private void ApplyBridges()
        {
            if (!Ready()) return;
            var lines = new List<string>();
            foreach (var raw in (bridgesBox.Text ?? "").Replace("\r", "").Split('\n'))
            { string t = raw.Trim(); if (t.Length > 0) lines.Add(t); }
            string err;
            if (lines.Count == 0 && !useBridgesCheck.Checked)
            { if (ctrl.ResetConf("UseBridges Bridge", out err)) { Persist(); SetStatus("bridges cleared + saved"); } else SetStatus("failed: " + err); return; }
            var sb = new StringBuilder("UseBridges=" + (useBridgesCheck.Checked ? "1" : "0"));
            foreach (var b in lines) sb.Append(" Bridge=\"" + b + "\"");
            if (ctrl.SetConf(sb.ToString(), out err)) { Persist(); SetStatus("bridges applied + saved (" + lines.Count + ")"); } else SetStatus("failed: " + err);
        }

        private void ApplyProxy()
        {
            if (!Ready()) return;
            string type = (string)proxyType.SelectedItem, host = (proxyHostBox.Text ?? "").Trim();
            string err;
            if (host.Length == 0) { if (ctrl.ResetConf(type, out err)) { Persist(); SetStatus(type + " cleared"); } else SetStatus("failed: " + err); return; }
            RunSet(type + "=" + host);
        }

        private void ClearProxy()
        {
            proxyHostBox.Text = "";
            if (!Ready()) return;
            string err;
            if (ctrl.ResetConf("Socks5Proxy Socks4Proxy HTTPSProxy", out err)) { Persist(); SetStatus("proxies cleared"); } else SetStatus("failed: " + err);
        }

        private void ApplyFirewall()
        {
            if (!Ready()) return;
            var sb = new StringBuilder("FascistFirewall=" + (fascistCheck.Checked ? "1" : "0"));
            string reach = Joined(reachBox);
            string err;
            if (reach.Length > 0) sb.Append(" ReachableAddresses=\"" + reach + "\"");
            else { ctrl.ResetConf("ReachableAddresses", out err); }
            if (ctrl.SetConf(sb.ToString(), out err)) { Persist(); SetStatus("firewall applied + saved"); } else SetStatus("failed: " + err);
        }

        private void ApplyTuning()
        {
            if (!Ready()) return;
            var sets = new List<string>();
            void Num(string key, TextBox box) { string v = (box.Text ?? "").Trim(); int n; if (v.Length > 0 && int.TryParse(v, out n) && n >= 0) sets.Add(key + "=" + n); }
            Num("CircuitBuildTimeout", cbtBox); Num("NewCircuitPeriod", ncpBox); Num("MaxCircuitDirtiness", mcdBox);
            sets.Add("EnforceDistinctSubnets=" + (distinctCheck.Checked ? "1" : "0"));
            sets.Add("UseEntryGuards=" + (guardsCheck.Checked ? "1" : "0"));
            RunSet(string.Join(" ", sets));
        }

        private void AddAddrMap()
        {
            if (!Ready()) return;
            string from = (mapFromBox.Text ?? "").Trim(), to = (mapToBox.Text ?? "").Trim();
            if (from.Length == 0 || to.Length == 0) { SetStatus("enter both From and To"); return; }
            string err;
            if (ctrl.MapAddress(from, to, out err)) SetStatus("mapped " + from + " \u2192 " + to); else SetStatus("map failed: " + err);
        }

        private int VisibleRows { get { return Math.Max(1, (list.ClientSize.Height - HEAD_H) / ROW_H); } }
        private int MaxScroll   { get { return Math.Max(0, view.Count - VisibleRows); } }
        private void ScrollBy(int d) { scrollTop = Math.Max(0, Math.Min(MaxScroll, scrollTop + d)); list.Invalidate(); }
        private void EnsureVisible() { if (selIdx < scrollTop) scrollTop = selIdx; else if (selIdx >= scrollTop + VisibleRows) scrollTop = selIdx - VisibleRows + 1; }

        private void Advor_KeyDown(object sender, KeyEventArgs e)
        {

            if (e.KeyCode == Keys.Delete && !string.IsNullOrEmpty(selCircuitId) && !(ActiveControl is TextBoxBase))
            { CloseSelectedCircuit(); e.Handled = true; return; }
            if (loading || view.Count == 0) return;
            if (e.KeyCode == Keys.Up)        { selIdx = Math.Max(0, selIdx - 1); EnsureVisible(); list.Invalidate(); e.Handled = true; }
            else if (e.KeyCode == Keys.Down) { selIdx = Math.Min(view.Count - 1, selIdx + 1); EnsureVisible(); list.Invalidate(); e.Handled = true; }
            else if (e.KeyCode == Keys.PageDown) { selIdx = Math.Min(view.Count - 1, selIdx + VisibleRows); EnsureVisible(); list.Invalidate(); e.Handled = true; }
            else if (e.KeyCode == Keys.PageUp)   { selIdx = Math.Max(0, selIdx - VisibleRows); EnsureVisible(); list.Invalidate(); e.Handled = true; }
        }

        private void List_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int w = list.ClientSize.Width, h = list.ClientSize.Height;
            int listW = w - SB_W;
            int cNick = 8, cCty = 218, cFlags = 268, cBw = 330, cFp = 412;

            if (loading)
            {
                using (var gb = new SolidBrush(Green)) g.DrawString("loading relays...", rowFont, gb, cNick, HEAD_H + 6);
                DrawScrollbar(g, w, h); return;
            }

            var clip = g.Clip;
            g.SetClip(new Rectangle(0, HEAD_H, listW, h - HEAD_H));
            int end = Math.Min(view.Count, scrollTop + VisibleRows);
            for (int i = scrollTop; i < end; i++)
            {
                var r = view[i];
                int y = HEAD_H + (i - scrollTop) * ROW_H;
                bool seld = (i == selIdx);
                if (seld) using (var sb = new SolidBrush(Green)) g.FillRectangle(sb, 0, y, listW, ROW_H);
                Brush fg = seld ? Brushes.Black : new SolidBrush(r.IsExit ? Green : Dim);
                g.DrawString(Clip(r.Nick, 26), rowFont, fg, cNick, y + 2);
                g.DrawString(r.Country ?? "??", rowFont, fg, cCty, y + 2);
                g.DrawString(FlagStr(r), rowFont, fg, cFlags, y + 2);
                g.DrawString(r.BandwidthKb.ToString(), rowFont, fg, cBw, y + 2);
                g.DrawString(Clip(r.Fingerprint, 16), rowFont, fg, cFp, y + 2);
                if (!seld) fg.Dispose();
            }
            g.Clip = clip;
            DrawScrollbar(g, w, h);
        }

        private Rectangle upRect, downRect, thumbRect;
        private void DrawScrollbar(Graphics g, int w, int h)
        {
            int x = w - SB_W, top = HEAD_H, bot = h;
            upRect = new Rectangle(x, top, SB_W, ARROW_H);
            downRect = new Rectangle(x, bot - ARROW_H, SB_W, ARROW_H);
            using (var track = new SolidBrush(Color.FromArgb(0xF4, 0xF4, 0xF4))) g.FillRectangle(track, x, top, SB_W, bot - top);
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
            DrawBevelButton(g, upRect); DrawBevelButton(g, downRect);
            DrawArrow(g, upRect, true); DrawArrow(g, downRect, false);
        }

        private void DrawBevelButton(Graphics g, Rectangle r)
        {
            using (var f = new SolidBrush(Color.FromArgb(0xF0, 0xF0, 0xF0))) g.FillRectangle(f, r);
            using (var hi = new Pen(Color.White)) { g.DrawLine(hi, r.Left, r.Top, r.Right - 1, r.Top); g.DrawLine(hi, r.Left, r.Top, r.Left, r.Bottom - 1); }
            using (var sh = new Pen(Color.FromArgb(0xB0, 0xB0, 0xB0))) { g.DrawLine(sh, r.Left, r.Bottom - 1, r.Right - 1, r.Bottom - 1); g.DrawLine(sh, r.Right - 1, r.Top, r.Right - 1, r.Bottom - 1); }
        }

        private void DrawArrow(Graphics g, Rectangle r, bool up)
        {
            int cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
            using (var p = new Pen(Color.FromArgb(0x50, 0x50, 0x50), 1.4f))
            {
                if (up) g.DrawLines(p, new[] { new Point(cx - 4, cy + 2), new Point(cx, cy - 3), new Point(cx + 4, cy + 2) });
                else    g.DrawLines(p, new[] { new Point(cx - 4, cy - 3), new Point(cx, cy + 2), new Point(cx + 4, cy - 3) });
            }
        }

        private void List_MouseDown(object sender, MouseEventArgs e)
        {
            if (upRect.Contains(e.Location))   { ScrollBy(-1); return; }
            if (downRect.Contains(e.Location)) { ScrollBy(1); return; }
            if (e.X >= list.ClientSize.Width - SB_W) { if (!thumbRect.IsEmpty) ScrollBy(e.Y < thumbRect.Top ? -VisibleRows : VisibleRows); return; }
            if (loading || view.Count == 0 || e.Y < HEAD_H) return;
            int idx = scrollTop + (e.Y - HEAD_H) / ROW_H;
            if (idx >= 0 && idx < view.Count)
            {
                selIdx = idx; list.Invalidate();
                if (e.Clicks >= 2) OpenDetail(view[idx]);
            }
        }

        private void RefreshCircuits() { StartPoll(false); }

        private void RebuildCircuitRows()
        {
            circRows.Clear();
            var byCirc = new Dictionary<string, List<TorControl.Stream>>();
            if (streams != null)
                foreach (var s in streams)
                {
                    string k = string.IsNullOrEmpty(s.CircuitId) ? "0" : s.CircuitId;
                    List<TorControl.Stream> l;
                    if (!byCirc.TryGetValue(k, out l)) byCirc[k] = l = new List<TorControl.Stream>();
                    l.Add(s);
                }
            if (circuits != null)
            {
                foreach (var c in circuits)
                {
                    string purpose = string.IsNullOrEmpty(c.Purpose) ? "" : "  \u00b7  " + c.Purpose;
                    bool built = string.Equals(c.Status, "BUILT", StringComparison.OrdinalIgnoreCase);
                    List<TorControl.Stream> mine;
                    byCirc.TryGetValue(c.Id, out mine);
                    string streamsTag = (mine != null && mine.Count > 0) ? "  \u00b7  " + mine.Count + (mine.Count == 1 ? " stream" : " streams") : "";
                    circRows.Add(new CircRow { Header = true, CircuitId = c.Id, HeadColor = built ? Green : Dim,
                        Text = "#" + c.Id + "  " + (c.Status ?? "") + purpose + streamsTag });
                    int hops = c.Relays != null ? c.Relays.Count : 0;
                    for (int i = 0; i < hops; i++)
                    {
                        string fp = (c.Fingerprints != null && i < c.Fingerprints.Count) ? c.Fingerprints[i] : "";
                        TorControl.Relay rel = null;
                        if (fpMap != null && !string.IsNullOrEmpty(fp)) fpMap.TryGetValue(fp.ToUpperInvariant(), out rel);
                        circRows.Add(new CircRow { Header = false, CircuitId = c.Id, Relay = rel, Nick = c.Relays[i] });
                    }
                    if (mine != null)
                        foreach (var s in mine) circRows.Add(new CircRow { Header = false, CircuitId = c.Id, Stream = s });
                }
            }
            List<TorControl.Stream> loose;
            if (byCirc.TryGetValue("0", out loose) && loose.Count > 0)
            {
                circRows.Add(new CircRow { Header = true, CircuitId = "0", HeadColor = Dim, Text = "unattached  \u00b7  " + loose.Count + " stream(s) waiting for a circuit" });
                foreach (var s in loose) circRows.Add(new CircRow { Header = false, CircuitId = "0", Stream = s });
            }
            int max = Math.Max(0, circRows.Count - CircuitVisibleRows);
            if (circuitScrollTop > max) circuitScrollTop = max;
        }

        private int CircuitVisibleRows { get { return circuitPanel == null ? 1 : Math.Max(1, (circuitPanel.ClientSize.Height - HEAD_H) / ROW_H); } }
        private int CircuitMaxScroll   { get { return Math.Max(0, circRows.Count - CircuitVisibleRows); } }
        private void CircuitScrollBy(int d) { circuitScrollTop = Math.Max(0, Math.Min(CircuitMaxScroll, circuitScrollTop + d)); if (circuitPanel != null) circuitPanel.Invalidate(); }

        private void Circuits_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int w = circuitPanel.ClientSize.Width, h = circuitPanel.ClientSize.Height;
            int listW = w - SB_W;
            int cNick = 8, cCty = 218, cFlags = 268, cBw = 330, cFp = 412;

            if (ctrl == null || !ctrl.Authenticated)
            {
                using (var gb = new SolidBrush(Dim)) g.DrawString("(connect to see circuits)", rowFont, gb, cNick, HEAD_H + 6);
                DrawCircuitScrollbar(g, w, h); return;
            }
            if (circRows.Count == 0)
            {
                using (var gb = new SolidBrush(Dim)) g.DrawString("(no circuits yet \u2014 browse to build some)", rowFont, gb, cNick, HEAD_H + 6);
                DrawCircuitScrollbar(g, w, h); return;
            }

            var clip = g.Clip;
            g.SetClip(new Rectangle(0, HEAD_H, listW, h - HEAD_H));
            int end = Math.Min(circRows.Count, circuitScrollTop + CircuitVisibleRows);
            for (int i = circuitScrollTop; i < end; i++)
            {
                var row = circRows[i];
                int y = HEAD_H + (i - circuitScrollTop) * ROW_H;
                if (row.Header)
                {
                    bool selHead = selCircuitId != null && row.CircuitId == selCircuitId && row.CircuitId != "0";
                    using (var hb = new SolidBrush(Color.FromArgb(0x12, 0x12, 0x12))) g.FillRectangle(hb, 0, y, listW, ROW_H);
                    if (selHead) using (var sp = new Pen(Green)) g.DrawRectangle(sp, 0, y, listW - 1, ROW_H - 1);
                    using (var tb = new SolidBrush(row.HeadColor)) g.DrawString(Clip(row.Text, 70) + (selHead ? "   [selected — Del closes]" : ""), rowFont, tb, cNick, y + 2);
                }
                else if (row.Stream != null)
                {

                    var st = row.Stream;
                    bool ok = string.Equals(st.Status, "SUCCEEDED", StringComparison.OrdinalIgnoreCase);
                    using (var fg = new SolidBrush(ok ? Green : Dim))
                    {
                        g.DrawString("   ⇢ #" + st.Id, rowFont, fg, cNick, y + 2);
                        g.DrawString(Clip(st.Target ?? "?", 34), rowFont, fg, cNick + 88, y + 2);
                        g.DrawString(st.Status ?? "", rowFont, fg, cFp, y + 2);
                    }
                }
                else
                {
                    var r = row.Relay;
                    Color col = (r != null && r.IsExit) ? Green : Dim;
                    using (var fg = new SolidBrush(col))
                    {
                        g.DrawString("\u2192 " + Clip(r != null ? r.Nick : (row.Nick ?? "?"), 24), rowFont, fg, cNick, y + 2);
                        g.DrawString(r != null ? (r.Country ?? "??") : "??",      rowFont, fg, cCty, y + 2);
                        g.DrawString(r != null ? FlagStr(r) : "----",            rowFont, fg, cFlags, y + 2);
                        g.DrawString(r != null ? r.BandwidthKb.ToString() : "?", rowFont, fg, cBw, y + 2);
                        g.DrawString(r != null ? Clip(r.Fingerprint, 16) : "(left consensus)", rowFont, fg, cFp, y + 2);
                    }
                }
            }
            g.Clip = clip;
            DrawCircuitScrollbar(g, w, h);
        }

        private void DrawCircuitScrollbar(Graphics g, int w, int h)
        {
            int x = w - SB_W, top = HEAD_H, bot = h;
            cUpRect   = new Rectangle(x, top, SB_W, ARROW_H);
            cDownRect = new Rectangle(x, bot - ARROW_H, SB_W, ARROW_H);
            using (var track = new SolidBrush(Color.FromArgb(0xF4, 0xF4, 0xF4))) g.FillRectangle(track, x, top, SB_W, bot - top);
            int trackH = (bot - ARROW_H) - (top + ARROW_H);
            if (circRows.Count > 0 && CircuitMaxScroll > 0 && trackH > 8)
            {
                double frac = (double)CircuitVisibleRows / circRows.Count;
                int thumbH = Math.Max(14, (int)(trackH * frac));
                int range = trackH - thumbH;
                int ty = top + ARROW_H + (int)((double)circuitScrollTop / CircuitMaxScroll * range);
                cThumbRect = new Rectangle(x + 1, ty, SB_W - 2, thumbH);
                DrawBevelButton(g, cThumbRect);
            }
            else cThumbRect = Rectangle.Empty;
            DrawBevelButton(g, cUpRect); DrawBevelButton(g, cDownRect);
            DrawArrow(g, cUpRect, true); DrawArrow(g, cDownRect, false);
        }

        private void Circuits_MouseDown(object sender, MouseEventArgs e)
        {
            if (cUpRect.Contains(e.Location))   { CircuitScrollBy(-1); return; }
            if (cDownRect.Contains(e.Location)) { CircuitScrollBy(1); return; }
            if (e.X >= circuitPanel.ClientSize.Width - SB_W)
            { if (!cThumbRect.IsEmpty) CircuitScrollBy(e.Y < cThumbRect.Top ? -CircuitVisibleRows : CircuitVisibleRows); return; }
            if (e.Y < HEAD_H || circRows.Count == 0) return;
            int idx = circuitScrollTop + (e.Y - HEAD_H) / ROW_H;
            if (idx < 0 || idx >= circRows.Count) return;
            var row = circRows[idx];
            if (row.Header)
            {

                selCircuitId = row.CircuitId == "0" ? null : (selCircuitId == row.CircuitId ? null : row.CircuitId);
                circuitPanel.Invalidate();
                if (selCircuitId != null) SetStatus("circuit #" + selCircuitId + " selected — Delete or \"Close selected\" closes it");
                return;
            }
            if (row.Relay != null) OpenDetail(row.Relay);
        }

        private static string Clip(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n - 1) + "\u2026";
        }
    }
}
