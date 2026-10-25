using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Text;
using System.Windows.Forms;

namespace Aloha
{
    public class TorWindow : DafyFrame
    {
        private static readonly Color Fab     = Color.FromArgb(0xFA, 0xFA, 0xFB);
        private static readonly Color Green   = TorrcSyntax.Green;
        private static readonly Color GreenHi = TorrcSyntax.GreenHi;
        private static readonly Color GreenLo = TorrcSyntax.GreenLo;
        private static readonly Color Brightest = TorrcSyntax.GreenTop;
        private static readonly Color Amber   = TorrcSyntax.Amber;
        private static readonly Color Red     = Color.FromArgb(0xFF, 0x5A, 0x4A);
        private const int SB = 17, GUTTER = TorrcSyntax.GUTTER, RESULT_H = 46, STATE_H = 22;

        private enum Kind { Torrc, Circ, Log }
        private bool showTorrc = true, showCirc = true, showLog = true;
        private const int GAP = 3;

        private sealed class Pane
        {
            public Kind Kind; public Panel Gutter; public RichTextBox Box; public DarkScrollBar V, H; public Panel Corner;
            public IEnumerable<Control> All { get { yield return Gutter; yield return Box; yield return V; yield return H; yield return Corner; } }
        }
        private readonly Pane pTorrc, pCirc, pLog;
        private readonly Panel result;
        private readonly TextBox resultText;
        private readonly Label state;
        private readonly Panel tTorrc, tCirc, tLog;
        private readonly Panel[] actions;

        private TorProcess tor;
        private TorControl ctrl;
        private readonly Timer poll = new Timer { Interval = 1200 };
        private readonly Timer colorTimer = new Timer { Interval = 250 };
        private bool pollBusy, suppressDirty, torrcDirty;
        private string status = "stopped", unknownNote, authNote;
        private int boot = -1;
        private List<TorControl.Circuit> circuits = new List<TorControl.Circuit>();
        private string lastCircSig = "";

        public event Action OnRouteRequested;

        public int RoutePort { get { return SocksPort(); } }

        public TorWindow() : base("OPT-TOR", "tor")
        {
            Size = new Size(1180, 440);
            MinimumSize = new Size(760, 300);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            KeyPreview = true;
            Font = new Font("Tahoma", 8.25f);
            ClientArea.BackColor = Fab;
            HideFooterCubes();

            SetFooterExtra(MakeFooterToggle("[cmd]",
                () => { try { return NetConfig.Load().CoreConsoleTor; } catch { return false; } },
                v  => { try { var c = NetConfig.Load(); c.CoreConsoleTor = v; c.Save(); } catch { } }));

            state = new Label { AutoSize = false, BackColor = Fab, ForeColor = Color.FromArgb(0x40, 0x40, 0x40), Font = new Font("Tahoma", 8.25f), TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true };

            pTorrc = MakePane(Kind.Torrc, false, Consolas(9.5f));
            pCirc  = MakePane(Kind.Circ,  true,  Consolas(9f));
            pLog   = MakePane(Kind.Log,   true,  Consolas(8.75f));

            result = new DoubleBufferedPanel { BackColor = Color.Black };
            result.Paint += PaintBootBar;
            resultText = new TextBox
            {
                Multiline = true, ReadOnly = true, BorderStyle = BorderStyle.None, WordWrap = true,
                BackColor = Color.Black, ForeColor = GreenLo, Font = Consolas(8.75f),
                Text = "Start reads the torrc above. Check runs tor.exe --verify-config on it (nothing is saved)."
            };
            result.Controls.Add(resultText);

            tTorrc = MakeFooterToggle("torrc", () => showTorrc, v => Toggle(Kind.Torrc));
            tCirc  = MakeFooterToggle("circ",  () => showCirc,  v => Toggle(Kind.Circ));
            tLog   = MakeFooterToggle("log",   () => showLog,   v => Toggle(Kind.Log));

            ClientArea.Controls.Add(state);
            foreach (var p in new[] { pTorrc, pCirc, pLog }) foreach (var c in p.All) ClientArea.Controls.Add(c);
            ClientArea.Controls.Add(result);
            foreach (var t in new[] { tTorrc, tCirc, tLog }) ClientArea.Controls.Add(t);
            foreach (var p in new[] { pTorrc, pCirc, pLog }) { p.V.Attach(p.Box); p.H.Attach(p.Box); }

            var bStart = MakeLabeledButton("Start",  StartTor);
            var bStop  = MakeLabeledButton("Stop",   StopTor);
            var bCheck = MakeLabeledButton("Check",  CheckTorrc);
            var bNym   = MakeLabeledButton("NEWNYM", NewIdentity);
            var bRoute = MakeLabeledButton("Route",  () => { var h = OnRouteRequested; h?.Invoke(); Result("browser routed through Tor's SOCKS " + SocksPort(), false); });

            actions = new[] { bStart, bStop, bCheck, bNym, bRoute };
            foreach (var b in actions) ClientArea.Controls.Add(b);

            pTorrc.Box.ReadOnly = false;
            pTorrc.Box.AcceptsTab = true;
            pTorrc.Box.TextChanged += (s, e) =>
            {
                if (suppressDirty) return;
                torrcDirty = true; colorTimer.Stop(); colorTimer.Start(); pTorrc.Gutter.Invalidate(); ShowState();
            };
            colorTimer.Tick += (s, e) => { colorTimer.Stop(); ColorTorrc(); ShowState(); };
            foreach (var p in new[] { pTorrc, pCirc, pLog })
            {
                var gut = p.Gutter;
                p.Box.VScroll += (s, e) => gut.Invalidate();
                p.Box.SelectionChanged += (s, e) => gut.Invalidate();
                p.Box.TextChanged += (s, e) => gut.Invalidate();
            }
            poll.Tick += (s, e) => Poll();

            ClientArea.Resize += (s, e) => LayoutAll();
            Shown += (s, e) => { LayoutAll(); RegenTorrc(); RenderCircuits(); Log("tor", "panel ready \u2014 the torrc is the config; edit it if you like, then Start."); };
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
                else if (e.KeyCode == Keys.F5) { CheckTorrc(); e.Handled = true; }
                else if (e.Control && e.KeyCode == Keys.D1) { Toggle(Kind.Torrc); e.Handled = true; }
                else if (e.Control && e.KeyCode == Keys.D2) { Toggle(Kind.Circ); e.Handled = true; }
                else if (e.Control && e.KeyCode == Keys.D3) { Toggle(Kind.Log); e.Handled = true; }
            };
            FormClosed += (s, e) => { Cleanup(); colorTimer.Dispose(); poll.Dispose(); };

        }

        private static Font Consolas(float pt) { return new Font("Consolas", pt); }

        private Pane MakePane(Kind kind, bool readOnly, Font f)
        {
            var p = new Pane
            {
                Kind = kind,
                Gutter = new DoubleBufferedPanel { BackColor = Color.Black },
                Box = new RichTextBox
                {
                    BorderStyle = BorderStyle.None, DetectUrls = false, WordWrap = false, ReadOnly = readOnly,
                    BackColor = Color.Black, ForeColor = Green, Font = f,
                    ScrollBars = RichTextBoxScrollBars.ForcedBoth
                },
                V = new DarkScrollBar(false, light: true) { Dock = DockStyle.None },
                H = new DarkScrollBar(true, light: true) { Dock = DockStyle.None },
                Corner = new Panel { BackColor = Color.FromArgb(0xF0, 0xF0, 0xF0) }
            };
            p.Gutter.Paint += (s, e) =>
            {
                if (p.Kind == Kind.Torrc) TorrcSyntax.PaintGutter(e.Graphics, p.Gutter, p.Box);
                else PaintSideGutter(e.Graphics, p);
            };
            return p;
        }

        private void Toggle(Kind k)
        {
            bool t = showTorrc, c = showCirc, l = showLog;
            if (k == Kind.Torrc) t = !t; else if (k == Kind.Circ) c = !c; else l = !l;
            if (!t && !c && !l) return;
            showTorrc = t; showCirc = c; showLog = l;
            tTorrc.Invalidate(); tCirc.Invalidate(); tLog.Invalidate();
            LayoutAll();
        }

        private void LayoutAll()
        {
            int w = ClientArea.ClientSize.Width, h = ClientArea.ClientSize.Height;
            if (w < 120 || h < 120) return;
            int x = w - 4;
            foreach (var t in new[] { tLog, tCirc, tTorrc }) { x -= t.Width; t.SetBounds(x, 1, t.Width, 20); x -= 2; t.BringToFront(); }
            x -= 10;
            for (int i = actions.Length - 1; i >= 0; i--) { var b = actions[i]; x -= b.Width; b.SetBounds(x, 1, b.Width, 20); x -= 2; b.BringToFront(); }
            state.SetBounds(8, 0, Math.Max(40, x - 12), STATE_H);

            int top = STATE_H, bodyH = h - top;
            bool right = showCirc || showLog;
            int leftW = !showTorrc ? 0 : right ? w * 48 / 100 : w;
            int rx = showTorrc ? leftW + GAP : 0, rw = w - rx;

            foreach (var p in new[] { pTorrc, pCirc, pLog })
            {
                bool on = p == pTorrc ? showTorrc : p == pCirc ? showCirc : showLog;
                foreach (var c in p.All) c.Visible = on;
            }

            int colX = showTorrc ? 0 : rx, colW = showTorrc ? leftW : rw;
            result.SetBounds(colX, h - RESULT_H, colW, RESULT_H);
            resultText.SetBounds(GUTTER, 6, Math.Max(10, colW - GUTTER - 6), RESULT_H - 8);

            if (showTorrc) Place(pTorrc, 0, top, leftW, bodyH - RESULT_H - 2);
            int rh = showTorrc ? bodyH : bodyH - RESULT_H - 2;
            if (showCirc && showLog)
            {
                int ch = rh * 45 / 100;
                Place(pCirc, rx, top, rw, ch);
                Place(pLog, rx, top + ch + GAP, rw, rh - ch - GAP);
            }
            else if (showCirc) Place(pCirc, rx, top, rw, rh);
            else if (showLog) Place(pLog, rx, top, rw, rh);
            result.Invalidate();
        }

        private void Place(Pane p, int x, int y, int w, int h)
        {
            h = Math.Max(SB + 20, h);
            int edH = h - SB;
            p.Gutter.SetBounds(x, y, GUTTER, edH);
            p.Box.SetBounds(x + GUTTER, y, Math.Max(20, w - GUTTER), h);
            p.V.SetBounds(x + w - SB, y, SB, edH);
            p.H.SetBounds(x, y + edH, w - SB, SB);
            p.Corner.SetBounds(x + w - SB, y + edH, SB, SB);
            p.V.BringToFront(); p.H.BringToFront(); p.Corner.BringToFront();
            p.Gutter.Invalidate();
        }

        private void PaintSideGutter(Graphics g, Pane p)
        {
            var gutter = p.Gutter; var box = p.Box;
            g.Clear(Color.Black);
            using (var pen = new Pen(GreenLo)) g.DrawLine(pen, gutter.Width - 1, 0, gutter.Width - 1, gutter.Height);
            if (box.TextLength == 0) return;
            int first = box.GetLineFromCharIndex(box.GetCharIndexFromPosition(new Point(1, 1)));
            string[] lines = box.Lines;
            int dy = box.Top - gutter.Top;
            for (int i = first; i < lines.Length; i++)
            {
                int ci = box.GetFirstCharIndexFromLine(i); if (ci < 0) break;
                int y = box.GetPositionFromCharIndex(ci).Y + dy;
                if (y > gutter.Height) break;
                string ln = lines[i];
                Color c = Color.Empty; string mark = null;
                if (p.Kind == Kind.Circ)
                {
                    if (ln.StartsWith("#")) { bool built = ln.IndexOf(" BUILT", StringComparison.Ordinal) > 0; c = built ? GreenHi : GreenLo; mark = built ? "\u25CF" : "\u25CB"; }
                }
                else
                {
                    if (ln.IndexOf("[err]", StringComparison.Ordinal) >= 0) { c = Red; mark = "\u25A0"; }
                    else if (ln.IndexOf("[warn]", StringComparison.Ordinal) >= 0) { c = Amber; mark = "\u25B2"; }
                    else if (ln.IndexOf("Bootstrapped", StringComparison.Ordinal) >= 0) { c = GreenHi; mark = "\u25B8"; }
                }
                if (mark != null)
                    TextRenderer.DrawText(g, mark, box.Font, new Rectangle(0, y, gutter.Width - 6, 16), c, TextFormatFlags.Right | TextFormatFlags.NoPadding);
            }
        }

        private void PaintBootBar(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int w = result.Width;
            using (var b = new SolidBrush(Color.FromArgb(0x16, 0x22, 0x18))) g.FillRectangle(b, 0, 0, w, 2);
            if (boot < 0) return;
            int fill = (int)(w * Math.Min(100, boot) / 100.0);
            using (var b = new SolidBrush(boot >= 100 ? GreenHi : Green)) g.FillRectangle(b, 0, 0, fill, 2);
            string pct = boot >= 100 ? "up" : boot + "%";
            TextRenderer.DrawText(g, pct, Consolas(7.5f), new Rectangle(0, 4, GUTTER - 4, 14), boot >= 100 ? GreenHi : GreenLo, TextFormatFlags.Right | TextFormatFlags.NoPadding);
        }

        private void ShowState()
        {
            var sb = new StringBuilder(status);
            sb.Append("  ·  SOCKS ").Append(SocksPort()).Append("  ·  control ").Append(ControlPort());
            if (authNote != null) sb.Append("  ·  ").Append(authNote);
            if (circuits.Count > 0) sb.Append("  ·  ").Append(circuits.FindAll(c => c.Status == "BUILT").Count).Append(" built");
            if (torrcDirty) sb.Append("  — hand-edited");
            if (unknownNote != null) sb.Append("  — ").Append(unknownNote);
            state.Text = sb.ToString();
            state.ForeColor = unknownNote != null || status.StartsWith("fail") || status.StartsWith("control:")
                ? Color.FromArgb(0xA0, 0x60, 0x00) : Color.FromArgb(0x40, 0x40, 0x40);
        }

        private void SetStatus(string s) { status = s; ShowState(); }

        private void Result(string text, bool bad)
        {
            if (resultText.IsDisposed) return;
            resultText.ForeColor = bad ? Amber : GreenHi;
            resultText.Text = text;
        }

        private int SocksPort()   { int p; return TryReadPort(pTorrc.Box.Text, "SocksPort", out p) ? p : 9050; }
        private int ControlPort() { int p; return TryReadPort(pTorrc.Box.Text, "ControlPort", out p) ? p : 9051; }

        private void RegenTorrc()
        {
            string text;
            try { text = new TorProcess("", 9050, 9051).BuildTorrc(); }
            catch (Exception ex) { text = "# could not build torrc: " + ex.Message; }
            suppressDirty = true;
            pTorrc.Box.Text = text.Replace("\r\n", "\n").Replace("\n", "\r\n");
            suppressDirty = false;
            torrcDirty = false;
            ColorTorrc(); ShowState();
        }

        private void ColorTorrc()
        {
            if (pTorrc.Box.IsDisposed) return;
            bool was = suppressDirty; suppressDirty = true;
            List<string> unknown;
            try { unknown = TorrcSyntax.Colorize(pTorrc.Box); } finally { suppressDirty = was; }
            unknownNote = unknown.Count == 0 ? null : "not a known option: " + string.Join(", ", unknown);
        }

        private static bool TryReadPort(string torrc, string key, out int port)
        {
            port = 0;
            foreach (var raw in (torrc ?? "").Replace("\r", "").Split('\n'))
            {
                string ln = raw.Trim();
                if (ln.StartsWith(key + " ", StringComparison.OrdinalIgnoreCase))
                {
                    string rest = ln.Substring(key.Length).Trim();
                    int sp = rest.IndexOf(' ');
                    if (sp > 0) rest = rest.Substring(0, sp);
                    int colon = rest.LastIndexOf(':'); if (colon >= 0) rest = rest.Substring(colon + 1);
                    return int.TryParse(rest, out port) && port > 0;
                }
            }
            return false;
        }

        private void CheckTorrc()
        {
            string exe = null;
            try { exe = TorProcess.FindTorExe(); } catch { }
            Result("checking with tor.exe …", false);
            TorrcSyntax.Verify(exe, pTorrc.Box.Text, this, (ok, verdict) =>
            {
                Result(verdict, !ok);
                Log("check", verdict.Replace("\r\n", " | "));
            });
        }

        private void StartTor()
        {
            if (tor != null && tor.Running) { Result("already running", false); return; }
            tor = new TorProcess("", SocksPort(), ControlPort());
            if (torrcDirty) { tor.TorrcOverride = pTorrc.Box.Text; Log("tor", "using the hand-edited torrc"); }
            tor.Line   += ln => Log(null, ln);
            tor.Ready  += () => Marshal(() => { boot = 100; result.Invalidate(); SetStatus("bootstrapped"); ConnectControl(); });
            tor.Failed += why => Marshal(() => { SetStatus("failed"); Result("FAILED: " + why, true); Log("tor", "FAILED: " + why); });
            Log("tor", "—— starting tor.exe (SOCKS " + tor.SocksPort + " / control " + tor.ControlPort + ") ——");
            boot = 0; result.Invalidate();
            SetStatus("starting");
            if (tor.Start()) poll.Start();
        }

        private void StopTor()
        {
            poll.Stop();
            try { ctrl?.Dispose(); } catch { }
            ctrl = null; authNote = null;
            try { tor?.Stop(); } catch { }
            circuits.Clear(); lastCircSig = "";
            boot = -1; result.Invalidate();
            RenderCircuits();
            SetStatus("stopped");
            Log("tor", "—— stopped ——");
        }

        private void ConnectControl()
        {
            if (tor == null) return;
            try
            {
                ctrl = new TorControl(tor.CookiePath, tor.ControlPort);
                string err;
                if (!ctrl.Connect(out err)) { SetStatus("control: failed"); Result(err, true); Log("tor", "control auth failed: " + err); ctrl = null; }
                else
                {
                    authNote = ctrl.AuthMethod.ToLowerInvariant();
                    SetStatus("ready");
                    Result("control port authenticated (" + ctrl.AuthMethod + (ctrl.CookieFileUsed.Length > 0 ? ", " + ctrl.CookieFileUsed : "") + ")", false);
                }
            }
            catch (Exception ex) { SetStatus("control: error"); Result(ex.Message, true); }
        }

        private void NewIdentity()
        {
            if (ctrl == null || !ctrl.Authenticated) { Result("not connected — Start first", true); return; }
            string err;
            if (ctrl.NewIdentity(out err)) { ctrl.ClearDnsCache(); Result("NEWNYM sent, DNS cache cleared — new streams get new circuits (Tor may rate-limit this to one per ~10 s)", false); }
            else Result("NEWNYM failed: " + err, true);
        }

        private void Poll()
        {
            if (tor == null) return;
            if (!tor.Running) { SetStatus("tor exited"); boot = -1; result.Invalidate(); poll.Stop(); return; }
            if (tor.BootstrapPercent < 100)
            {
                if (tor.BootstrapPercent != boot) { boot = tor.BootstrapPercent; result.Invalidate(); SetStatus("bootstrapping " + boot + "%"); }
                return;
            }
            if (ctrl == null || !ctrl.Authenticated || pollBusy) return;

            pollBusy = true;
            var c = ctrl;
            System.Threading.ThreadPool.QueueUserWorkItem(_ =>
            {
                List<TorControl.Circuit> cs = null; string err = null;
                try { cs = c.GetCircuits(out err); } catch (Exception ex) { err = ex.Message; }
                Marshal(() =>
                {
                    pollBusy = false;
                    if (ctrl != c) return;
                    if (err == null && cs != null) { circuits = cs; RenderCircuits(); ShowState(); }
                });
                if (!IsHandleCreated || IsDisposed) pollBusy = false;
            });
        }

        private void RenderCircuits()
        {
            var b = pCirc.Box;
            if (b.IsDisposed) return;
            var sig = new StringBuilder();
            foreach (var c in circuits) sig.Append(c.Id).Append(c.Status).Append(c.Relays.Count).Append(';');
            if (sig.ToString() == lastCircSig && b.TextLength > 0) return;
            lastCircSig = sig.ToString();

            b.SuspendLayout();
            b.Clear();
            if (circuits.Count == 0)
            {
                Put(b, tor != null && tor.Running ? "(no circuits yet)" : "(start Tor to see circuits)", GreenLo, true);
            }
            else
            {
                Put(b, "id    state    purpose          age   path", GreenLo, true);
                foreach (var c in circuits)
                {
                    bool built = c.Status == "BUILT";
                    Put(b, ("#" + c.Id).PadRight(6), built ? Brightest : GreenLo, false);
                    Put(b, (c.Status ?? "").PadRight(9), built ? Green : c.Status == "FAILED" ? Amber : GreenLo, false);
                    Put(b, (c.Purpose ?? "GENERAL").ToLowerInvariant().PadRight(17), GreenLo, false);
                    Put(b, (TorControl.CircuitAge(c.TimeCreated) ?? "").PadRight(6), Green, false);
                    if (c.Relays.Count == 0) Put(b, "(building)", GreenLo, false);
                    for (int i = 0; i < c.Relays.Count; i++)
                    {
                        if (i > 0) Put(b, " ─ ", GreenLo, false);
                        Put(b, c.Relays[i], built ? GreenHi : Green, false);
                    }
                    if (!string.IsNullOrEmpty(c.RendQuery)) Put(b, "  ⇢ " + c.RendQuery.Substring(0, Math.Min(16, c.RendQuery.Length)) + "….onion", GreenLo, false);
                    Put(b, "", Green, true);
                }
            }
            b.ResumeLayout();
            b.SelectionStart = 0; b.ScrollToCaret();
            pCirc.Gutter.Invalidate();
        }

        private static void Put(RichTextBox b, string text, Color c, bool newline)
        {
            b.SelectionStart = b.TextLength;
            b.SelectionLength = 0;
            b.SelectionColor = c;
            b.AppendText(text + (newline ? "\n" : ""));
        }

        private int logLines;
        private void Log(string who, string line)
        {
            Marshal(() =>
            {
                var b = pLog.Box;
                if (b.IsDisposed) return;
                string ln = (who != null ? "aloha  " + who + " | " : "") + (line ?? "");

                int sev = ln.IndexOf('[');
                Color c = who != null ? GreenLo
                        : ln.Contains("[err]") ? Red
                        : ln.Contains("[warn]") ? Amber
                        : ln.Contains("Bootstrapped") ? GreenHi : Green;
                if (who == null && sev > 0 && sev < 32) { Put(b, ln.Substring(0, sev), GreenLo, false); Put(b, ln.Substring(sev), c, true); }
                else Put(b, ln, c, true);
                if (++logLines > 4000) { b.Select(0, b.GetFirstCharIndexFromLine(1000)); b.ReadOnly = false; b.SelectedText = ""; b.ReadOnly = true; logLines -= 1000; }
                b.SelectionStart = b.TextLength; b.ScrollToCaret(); pLog.Gutter.Invalidate();

                if (who == null && (ln.Contains("[warn]") || ln.Contains("[err]"))) Result(ln.Substring(Math.Max(0, sev)), true);
                else if (who == null && ln.Contains("Bootstrapped")) Result(ln.Substring(Math.Max(0, sev)), false);
            });
        }

        private void Marshal(Action a) { try { if (IsHandleCreated && !IsDisposed) BeginInvoke(a); } catch { } }

        private void Cleanup()
        {
            try { poll.Stop(); } catch { }
            try { ctrl?.Dispose(); } catch { }
            try { tor?.Stop(); } catch { }
        }
    }
}
