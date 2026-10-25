using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Aloha
{

    public class SealedTorWindow : DafyFrame
    {
        private static readonly Color Fab    = Color.FromArgb(0xFA, 0xFA, 0xFB);
        private static readonly Color DimGry = Color.FromArgb(0x77, 0x77, 0x77);
        public  static readonly Color TorGreen = Color.FromArgb(0x7F, 0xAE, 0x86);
        private const int LOG_H = 124;
        private const int BAR_ROW = 24;
        public const int SocksPort = NetConfig.SealedTorSocksPort, ControlPort = 9071, DnsPort = 9073;

        private readonly NetConfig cfg;
        private readonly Action<bool> routeChanged;

        private TorProcess tor7;
        private readonly string torDir;
        private readonly string torExe;
        private readonly string dataDir;
        private string UserTorrc { get { return Path.Combine(dataDir, "torrc7.user"); } }

        private TextBox statusBox, logBox;
        private Panel logWell;
        private Panel btnStart, btnStop, btnFresh, btnTorrc;
        private Panel tCmd, tIso, tRoute;
        private Torrc7Window torrcWin;
        private System.Windows.Forms.Timer poll, cmdBoot;
        private int cmdBootPct = -1;
        private bool _torWasUp;

        public SealedTorWindow(NetConfig config, Action<bool> onRouteChanged)
            : base("OPT-SEALED", "Sealed tor")
        {
            cfg = config ?? new NetConfig();
            routeChanged = onRouteChanged;

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            torDir  = !string.IsNullOrWhiteSpace(cfg.SealedTorDir) ? cfg.SealedTorDir : Path.Combine(baseDir, "torrc7");
            torExe  = Path.Combine(torDir, "tor", "tor.exe");
            dataDir = Path.Combine(torDir, "Data", "Tor");

            tRoute = MakeFooterToggle("[route]",
                () => cfg.SealedTor,
                v  => { cfg.SealedTor = v; try { cfg.Save(); } catch { }
                        LogLine("sealed | browser " + (v ? "ROUTED through 127.0.0.1:" + SocksPort + " (engine restarts)" : "no longer routed (engine restarts)"));
                        Render(); try { routeChanged?.Invoke(v); } catch (Exception ex) { LogLine("sealed | ! " + ex.Message); } });
            tIso = MakeFooterToggle("[per site]",
                () => !cfg.SealedTorSharedCircuits,
                v  => { cfg.SealedTorSharedCircuits = !v; try { cfg.Save(); } catch { } Render();
                        LogLine("sealed | circuits: " + (v ? "ONE PER SITE (IsolateDestAddr)" : "SHARED") + (TorUp ? "  (Stop + Start for the torrc to follow)" : "")); });
            tCmd = MakeFooterToggle("[cmd]",
                () => cfg.SealedTorConsole,
                v  => { cfg.SealedTorConsole = v; try { cfg.Save(); } catch { }
                        LogLine("sealed | [cmd] " + (v ? "ON — tor.exe opens in its own console; this log shows only Aloha's lines" : "OFF — output captured here")
                                + (TorUp ? "  (applies on the next Start)" : "")); });
            foreach (var t in new[] { tRoute, tIso, tCmd })
                t.Width = Math.Max(t.Width, TextRenderer.MeasureText(t.Text, new Font("Lucida Console", 7f, FontStyle.Bold)).Width + 12);

            Size = new Size(860, 400);
            MinimumSize = new Size(560, 300);
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = true;
            KeyPreview = true;
            ClientArea.BackColor = Fab;

            var canvas = SetupAlohaScroll(Fab);
            statusBox = new TextBox
            {
                Multiline = true, ReadOnly = true, WordWrap = false,
                ScrollBars = ScrollBars.None, BorderStyle = BorderStyle.None,
                BackColor = Fab, ForeColor = Color.Black,
                Font = new Font("Consolas", 9.5f), Left = 0, Top = 0
            };
            canvas.Controls.Add(statusBox);

            btnStart = MakeLabeledButton("Start", Start);
            btnStop  = MakeLabeledButton("Stop", Stop);
            btnFresh = MakeLabeledButton("New circuit", NewCircuit);
            btnTorrc = MakeLabeledButton("torrc…", OpenTorrc);
            btnTorrc.Width += 14;
            foreach (var b in new[] { btnStart, btnStop, btnFresh, btnTorrc })
            {
                b.BackColor = Fab;
                ClientArea.Controls.Add(b);
                b.BringToFront();
            }

            logWell = new Panel { BackColor = Color.Black, Padding = new Padding(4, 3, 0, 0) };
            logWell.Paint += (s, e) =>
            {
                using (var dk = new Pen(Color.FromArgb(0x80, 0x80, 0x80))) e.Graphics.DrawLine(dk, 0, 0, logWell.Width, 0);
                using (var md = new Pen(Color.FromArgb(0x40, 0x40, 0x40))) e.Graphics.DrawLine(md, 0, 1, logWell.Width, 1);
            };
            logBox = new TextBox
            {
                Multiline = true, ReadOnly = true, WordWrap = false,
                ScrollBars = ScrollBars.Vertical, BorderStyle = BorderStyle.None,
                BackColor = Color.Black, ForeColor = TorGreen,
                Font = new Font("Consolas", 9f), Dock = DockStyle.Fill
            };
            logWell.Controls.Add(logBox);
            ClientArea.Controls.Add(logWell);
            logWell.BringToFront();
            ClientArea.Controls.Add(tRoute);
            ClientArea.Controls.Add(tIso);
            ClientArea.Controls.Add(tCmd);
            SetAlohaBottomInset(LOG_H + BAR_ROW);

            void LayoutBody()
            {
                var r = AlohaBottomStripRect;
                int rowY = r.Y + (BAR_ROW - 20) / 2;
                int x = r.Right - 4;
                foreach (var c in new Control[] { tRoute, tIso, tCmd }) { x -= c.Width; c.SetBounds(x, rowY, c.Width, 20); x -= FooterGap; c.BringToFront(); }
                x -= 10;
                foreach (var c in new Control[] { btnTorrc, btnFresh, btnStop, btnStart })
                { x -= c.Width; c.SetBounds(Math.Max(4, x), rowY, c.Width, 20); x -= 6; c.BringToFront(); }
                logWell.SetBounds(0, r.Y + BAR_ROW, Math.Max(40, r.Width), Math.Max(20, r.Height - BAR_ROW));
                logWell.BringToFront();
            }
            ClientArea.Resize += (s, e) => LayoutBody();
            Shown += (s, e) => LayoutBody();

            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; } };
            FormClosed += (s, e) => { try { poll?.Stop(); poll?.Dispose(); cmdBoot?.Stop(); cmdBoot?.Dispose(); } catch { } };

            Render();
            poll = new System.Windows.Forms.Timer { Interval = 1000 };
            poll.Tick += (s, e) =>
            {
                if (_torWasUp && !TorUp) { _torWasUp = false; LogLine("sealed | ! Tor STOPPED unexpectedly (see the lines above for the reason)"); }
                if (TorUp) _torWasUp = true;
                Render();
            };
            poll.Start();
        }

        private void LogLine(string s)
        {
            if (s == null) return;
            ActivityLog.General(s);
            if (logBox == null || logBox.IsDisposed) return;
            try
            {
                if (logBox.InvokeRequired) { logBox.BeginInvoke((Action)(() => LogLine(s))); return; }
                logBox.AppendText(s + "\r\n");
                if (logBox.TextLength > 60000) logBox.Text = logBox.Text.Substring(logBox.TextLength - 40000);
                logBox.SelectionStart = logBox.TextLength;
                logBox.ScrollToCaret();
            }
            catch { }
        }

        private bool TorUp => tor7 != null && tor7.Running;

        private void Render()
        {
            if (statusBox == null) return;
            var sb = new StringBuilder();
            sb.AppendLine();
            sb.AppendLine("  NETWORK         : SEALED TOR   ·  any domain — Tor decides what is reachable");
            sb.AppendLine("  Tor (/torrc7/)  : " + TorStatusText());
            sb.AppendLine("  SOCKS           : 127.0.0.1:" + SocksPort + "        (socks5h — names resolve inside Tor)");
            sb.AppendLine("  control         : 127.0.0.1:" + ControlPort + "        (cookie auth · New circuit = NEWNYM)");
            sb.AppendLine("  DNS             : 127.0.0.1:" + DnsPort);
            sb.AppendLine("  circuits        : " + (cfg.SealedTorSharedCircuits ? "shared" : "one per site (IsolateDestAddr)"));
            sb.AppendLine("  exit            : " + (string.IsNullOrEmpty(cfg.SealedTorExit) ? "any" : "{" + cfg.SealedTorExit + "}"));
            sb.AppendLine("  browser         : " + (cfg.SealedTor ? "ROUTED through the sealed Tor" : "not routed  ([route], or Options › Proxy › Sealed tor)"));
            statusBox.Text = sb.ToString();
            statusBox.Size = MeasureBlock(statusBox.Text, statusBox.Font);

            SetEnabled(btnStart, !TorUp);
            SetEnabled(btnStop, TorUp);
            SetEnabled(btnFresh, TorUp);
            if (btnTorrc != null) { btnTorrc.Text = File.Exists(UserTorrc) ? "torrc… *" : "torrc…"; btnTorrc.Invalidate(); }
        }

        private string TorStatusText()
        {
            if (!TorUp) return "stopped";
            int b = tor7.BootstrapPercent;
            if (cmdBootPct >= 0 && b == 0) b = cmdBootPct;
            return (b >= 100 ? "RUNNING (bootstrapped)" : "bootstrapping " + b + "%") + (cfg.SealedTorConsole ? "  [cmd]" : "");
        }

        private static void SetEnabled(Panel b, bool on)
        {
            if (b == null) return;
            b.Enabled = on;
            b.Cursor = on ? Cursors.Hand : Cursors.Default;
            b.ForeColor = on ? Color.Black : DimGry;
            b.Invalidate();
        }

        private void Start()
        {
            if (!File.Exists(torExe))
            {
                MessageBox.Show(this, "Sealed Tor binary not found:\n" + torExe + "\n\nDrop a Tor Expert Bundle into:\n" + Path.Combine(torDir, "tor") + "\\",
                    "Sealed tor — tor.exe missing", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                tor7 = new TorProcess(null, SocksPort, ControlPort);
                tor7.ExePathOverride = torExe;
                tor7.DataDirOverride = dataDir;
                try { Directory.CreateDirectory(dataDir); } catch (Exception de) { LogLine("sealed | ! could not create the data dir: " + de.Message); }
                tor7.TorrcOverride = EffectiveTorrc();
                tor7.ConsoleOverride = cfg.SealedTorConsole;
                tor7.Line   += s => LogLine("tor    | " + s);
                tor7.Ready  += () => LogLine("tor    | * bootstrapped — SOCKS 127.0.0.1:" + SocksPort + " is up");
                tor7.Failed += s => LogLine("tor    | ! FAILED: " + s);
                tor7.Start();
                LogLine("sealed | Tor starting  (binary: " + torExe + ")" + (cfg.SealedTorConsole ? "  [cmd]" : ""));
                LogLine("sealed | data dir: " + dataDir);
                if (cfg.SealedTorConsole) StartCmdBootProbe();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Could not start the sealed Tor:\n" + ex.Message, "Sealed tor", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            Render();
        }

        private void Stop()
        {
            try { if (tor7 != null) { tor7.Stop(); ActivityLog.General("Sealed tor: stopped"); } } catch { }
            tor7 = null;
            try { cmdBoot?.Stop(); } catch { }
            cmdBootPct = -1;
            Render();
        }

        public string BuildTorrc()
        {
            var sb = new StringBuilder();
            sb.AppendLine("# torrc7 — the sealed instance (written by SealedTorWindow)");
            sb.AppendLine("DataDirectory " + dataDir);

            sb.AppendLine("SocksPort " + SocksPort + (cfg.SealedTorSharedCircuits ? "" : " IsolateDestAddr"));
            sb.AppendLine("ControlPort " + ControlPort);

            sb.AppendLine("CookieAuthentication 1");
            sb.AppendLine("DNSPort " + DnsPort);
            sb.AppendLine("DisableNetwork 0");
            if (!string.IsNullOrEmpty(cfg.SealedTorExit))
            {
                sb.AppendLine("ExitNodes {" + cfg.SealedTorExit + "}");
                sb.AppendLine("StrictNodes 1");
            }
            return sb.ToString();
        }

        private string EffectiveTorrc()
        {
            try
            {
                if (File.Exists(UserTorrc))
                {
                    string t = File.ReadAllText(UserTorrc, Encoding.UTF8);
                    if (!string.IsNullOrWhiteSpace(t)) { LogLine("sealed | using the hand-edited torrc7.user"); return t; }
                }
            }
            catch (Exception ex) { LogLine("sealed | ! torrc7.user unreadable (" + ex.Message + ") — using the generated torrc"); }
            return BuildTorrc();
        }

        private void OpenTorrc()
        {
            try
            {
                if (torrcWin == null || torrcWin.IsDisposed)
                {
                    torrcWin = new Torrc7Window(UserTorrc, BuildTorrc, LogLine, torExe, cfg);
                    torrcWin.FormClosed += (s, e) => Render();
                }
                torrcWin.Show();
                if (torrcWin.WindowState == FormWindowState.Minimized) torrcWin.WindowState = FormWindowState.Normal;
                torrcWin.BringToFront();
            }
            catch (Exception ex) { LogLine("sealed | ! torrc window: " + ex.GetType().Name + ": " + ex.Message); }
        }

        private void StartCmdBootProbe()
        {
            cmdBootPct = 0;
            cmdBoot?.Stop(); cmdBoot?.Dispose();
            cmdBoot = new System.Windows.Forms.Timer { Interval = 1000 };
            bool busy = false;
            int ticks = 0;
            cmdBoot.Tick += async (s, e) =>
            {
                if (busy) return; busy = true;
                try
                {
                    if (!TorUp || ++ticks > 300) { cmdBoot.Stop(); return; }
                    int pct = await TorControl.OnceAsync(ControlPortCfg, CookieHint, cfg.SealedTorControlPassword,
                        c => c.GetBootstrapPercent(), -1, err =>
                        {
                            if (err == null || err.StartsWith("no Tor control port", StringComparison.Ordinal) || err == lastCtlError) return;
                            lastCtlError = err;
                            try { BeginInvoke((Action)(() => LogLine("tor    | " + err))); } catch { }
                        });
                    if (pct >= 0 && pct != cmdBootPct) { cmdBootPct = pct; LogLine("tor    | bootstrapped " + pct + "%  (control port)"); Render(); }
                    if (pct >= 100) cmdBoot.Stop();
                }
                finally { busy = false; }
            };
            cmdBoot.Start();
        }
        private string lastCtlError;
        private int ControlPortCfg { get { return cfg.SealedTorControlPort > 0 ? cfg.SealedTorControlPort : ControlPort; } }
        private string CookieHint { get { return Path.Combine(dataDir, "control_auth_cookie"); } }

        private async void NewCircuit()
        {
            string why = null;
            bool ok = await TorControl.OnceAsync(ControlPortCfg, CookieHint, cfg.SealedTorControlPassword,
                c => { string e; bool r = c.NewIdentity(out e); why = e; return r; }, false, e => why = e);
            LogLine(ok ? "sealed | new circuits (NEWNYM)" : "tor    | NEWNYM failed — " + why);
        }

        private static Size MeasureBlock(string text, Font f)
        {
            int maxW = 40, lines = 0;
            foreach (var line in text.Replace("\r", "").Split('\n'))
            {
                int w = TextRenderer.MeasureText(line, f).Width;
                if (w > maxW) maxW = w;
                lines++;
            }
            int lh = TextRenderer.MeasureText("Ag", f).Height + 2;
            return new Size(maxW + 12, Math.Max(1, lines) * lh + 8);
        }
    }
}
