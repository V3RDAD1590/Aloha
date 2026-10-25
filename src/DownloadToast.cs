using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace Aloha
{
    public sealed class DownloadToast : Form
    {

        private static readonly Color Ink     = Color.Black;
        private static readonly Color Green   = Color.FromArgb(0xB4, 0xDC, 0xBA);
        private static readonly Color GreenHi = Color.FromArgb(0xE6, 0xF4, 0xE8);
        private static readonly Color GreenLo = Color.FromArgb(0x24, 0x38, 0x29);
        private static readonly Color Red     = Color.FromArgb(0xFF, 0x1A, 0x1A);
        private static readonly Color NavTop  = Color.FromArgb(252, 252, 253);
        private static readonly Color Plate   = Color.FromArgb(0xFA, 0xFA, 0xFB);
        private static readonly Color BarLime = Color.FromArgb(0xB4, 0xDC, 0xBA);
        private static readonly Color NavBot  = Color.FromArgb(216, 216, 220);
        private static readonly Color BevDk   = Color.FromArgb(0x80, 0x80, 0x80);
        private static readonly Color BevLt   = Color.White;
        private static readonly Color BtnFace = Color.FromArgb(0xF0, 0xF0, 0xF0);

        private const int W = 450, M = 8, ROW_GAP = 10;
        private const int NAME_H = 18, BAR_H = 15, SPEED_H = 16, BTN_H = BAR_H - 4, SCOPE_H = 34;
        private const int ROW_H = NAME_H + 4 + BAR_H + 3 + BAR_H + 4 + BTN_H + 5 + SCOPE_H;
        private const int MAX_ROWS = 3, LINGER_MS = 45000, SAMPLES = 120;

        private sealed class Btn { public string Id, Text; public Rectangle R; public bool Enabled; }

        private sealed class Row
        {
            public DownloadRecord Rec;
            public CoreWebView2DownloadOperation Op;
            public DateTime DoneAt = DateTime.MinValue;
            public Rectangle Close;
            public readonly List<Btn> Buttons = new List<Btn>();
            public long LastBytes; public DateTime LastAt = DateTime.UtcNow; public double Rate;
            public readonly ChunkSample[] Scope = new ChunkSample[SAMPLES];
            public int Head;
            public Rectangle ScopeRect; public int ScopeX0, ScopeBw;
            public ChunkSample Hover, Selected;
            public string Sha;
            public bool Hashing;
        }

        private readonly Form _owner;
        private readonly Func<Rectangle> _anchor;
        private readonly Action _openDownloads;
        private readonly Action<string> _openInTab;
        private readonly Action<DownloadRecord> _retry;
        private readonly List<Row> _rows = new List<Row>();
        private readonly Timer _tick = new Timer { Interval = 250 };
        private readonly Font fName  = new Font("Tahoma", 8.25f, FontStyle.Bold);
        private readonly Font fLine  = new Font("Tahoma", 7f);
        private readonly Font fMono  = new Font("Consolas", 8.25f);
        private readonly Font fBtn   = new Font("Tahoma", 6.75f);
        private Btn _hover, _pressed;

        public DownloadToast(Form owner, Func<Rectangle> anchor, Action openDownloads,
                             Action<string> openInTab, Action<DownloadRecord> retry)
        {
            _owner = owner; _anchor = anchor; _openDownloads = openDownloads; _openInTab = openInTab; _retry = retry;
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Plate;
            Width = W; Height = M * 2 + ROW_H;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            _tick.Tick += (s, e) => Tick();
            owner.LocationChanged += (s, e) => Place();
            owner.Resize += (s, e) => Place();
        }

        protected override bool ShowWithoutActivation { get { return true; } }
        protected override CreateParams CreateParams
        {
            get { var cp = base.CreateParams; cp.ExStyle |= 0x08000000  | 0x00000080 ; return cp; }
        }

        public void Track(DownloadRecord rec, CoreWebView2DownloadOperation op = null)
        {
            if (rec == null) return;
            foreach (var r in _rows) if (ReferenceEquals(r.Rec, rec)) { if (op != null) r.Op = op; return; }
            _rows.Insert(0, new Row { Rec = rec, Op = op, LastBytes = rec.ReceivedBytes });
            while (_rows.Count > MAX_ROWS) _rows.RemoveAt(_rows.Count - 1);
            if (!_tick.Enabled) _tick.Start();
            Relayout();
            if (!Visible) { try { Show(_owner); } catch { Show(); } }
            Place();
            Invalidate();
        }

        public bool Pinned { get; private set; }
        public event Action PinnedChanged;

        public void SetPinned(bool on, IList<DownloadRecord> recent, Func<DownloadRecord, CoreWebView2DownloadOperation> opFor)
        {
            Pinned = on;
            if (on)
            {
                if (recent != null)
                    for (int i = Math.Max(0, recent.Count - MAX_ROWS); i < recent.Count; i++)
                    {
                        var rec = recent[i]; bool have = false;
                        foreach (var r in _rows) if (ReferenceEquals(r.Rec, rec)) { have = true; break; }
                        if (!have) { _rows.Insert(0, new Row { Rec = rec, Op = opFor != null ? opFor(rec) : null, LastBytes = rec.ReceivedBytes }); }
                    }
                while (_rows.Count > MAX_ROWS) _rows.RemoveAt(_rows.Count - 1);
                if (!_tick.Enabled) _tick.Start();
                Relayout();
                if (!Visible) { try { Show(_owner); } catch { Show(); } }
                Place(); Invalidate();
            }
            else
            {

                _rows.RemoveAll(r => IsFinal(r.Rec));
                if (_rows.Count == 0) { _tick.Stop(); Hide(); } else { Relayout(); Invalidate(); }
            }
            var h = PinnedChanged; if (h != null) h();
        }

        public void Changed()
        {
            if (IsDisposed) return;
            try { BeginInvoke((Action)(() => { if (!IsDisposed) Invalidate(); })); } catch { }
        }

        private static bool IsFinal(DownloadRecord d)
        {
            string s = d.State ?? "";
            return s == "completed" || s == "cancelled" || s == "blocked" || s.StartsWith("interrupted", StringComparison.Ordinal);
        }

        private void Tick()
        {
            bool changed = false;
            var now = DateTime.UtcNow;
            for (int i = _rows.Count - 1; i >= 0; i--)
            {
                var r = _rows[i];
                bool fin = IsFinal(r.Rec);
                if (fin && r.DoneAt == DateTime.MinValue) { r.DoneAt = now; changed = true; }
                if (!fin) r.DoneAt = DateTime.MinValue;

                bool sticky = r.Rec.State != "completed" || DangerousExt(r.Rec.FileName) != null;
                if (fin && !sticky && !Pinned && (now - r.DoneAt).TotalMilliseconds > LINGER_MS) { _rows.RemoveAt(i); changed = true; continue; }

                double dt = (now - r.LastAt).TotalSeconds;
                if (dt > 0)
                {
                    long got = r.Rec.ReceivedBytes;
                    double inst = got >= r.LastBytes ? (got - r.LastBytes) / dt : 0;
                    r.Rate = r.Rate <= 0 ? inst : r.Rate * 0.75 + inst * 0.25;
                    if (!fin)
                    {

                        r.Scope[r.Head] = new ChunkSample
                        {
                            At = now, Seconds = dt, Rate = inst,
                            From = got >= r.LastBytes ? r.LastBytes : 0, To = got - 1,
                            Restarted = got < r.LastBytes, State = r.Rec.State ?? ""
                        };
                        r.Head = (r.Head + 1) % SAMPLES;
                    }
                    r.LastBytes = got; r.LastAt = now;
                }
            }
            if (_rows.Count == 0 && !Pinned) { _tick.Stop(); Hide(); return; }
            if (changed) Relayout();
            Invalidate();
        }

        private void Relayout()
        {
            int n = Math.Max(1, _rows.Count);
            Height = M * 2 + n * ROW_H + (n - 1) * ROW_GAP;
            Place();
        }

        private void Place()
        {
            if (!Visible && _rows.Count == 0) return;
            try { var a = _anchor(); Location = new Point(a.Right - W - 8, a.Top + 8); } catch { }
        }

        private static readonly HashSet<string> Red_ = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "exe","scr","com","pif","cpl","msi","msp","mst","msc","bat","cmd","vbs","vbe","js","jse","wsf","wsh","wsc",
            "hta","ps1","psm1","psd1","ps1xml","jar","reg","inf","lnk","url","scf","chm","application","appref-ms",
            "gadget","settingcontent-ms","library-ms","search-ms","searchconnector-ms","xll","iqy","slk",
            "docm","dotm","xlsm","xltm","xlam","pptm","potm","ppam","ppsm","sldm","one","onepkg",
            "iso","img","vhd","vhdx","appx","appxbundle","msix","msixbundle","diagcab","cab","ws","sys","dll","ocx"
        };

        internal static string DangerousExt(string name)
        {
            if (string.IsNullOrEmpty(name)) return null;
            int dot = name.LastIndexOf('.');
            string ext = dot >= 0 && dot < name.Length - 1 ? name.Substring(dot + 1) : "";
            if (name.IndexOf('\u202E') >= 0 || name.IndexOf('\u202D') >= 0) return ext.Length > 0 ? ext : name;
            return ext.Length > 0 && Red_.Contains(ext) ? ext : null;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            using (var br = new SolidBrush(Plate)) g.FillRectangle(br, ClientRectangle);
            Raised(g, ClientRectangle);

            if (_rows.Count == 0)
            {
                TextRenderer.DrawText(g, "no downloads yet  \u00B7  the \u21E9 button hides this", fLine,
                    new Rectangle(M, M, Width - 2 * M, ROW_H), BevDk, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                return;
            }
            int y = M;
            for (int i = 0; i < _rows.Count; i++)
            {
                if (i > 0)
                {

                    int dy = y - ROW_GAP / 2;
                    using (var dk = new Pen(BevDk)) g.DrawLine(dk, M, dy, Width - M - 1, dy);
                    using (var lt = new Pen(BevLt)) g.DrawLine(lt, M, dy + 1, Width - M - 1, dy + 1);
                }
                PaintRow(g, _rows[i], y);
                y += ROW_H + ROW_GAP;
            }
        }

        private void PaintRow(Graphics g, Row r, int y)
        {
            var d = r.Rec;
            string st = d.State ?? "";
            bool done = st == "completed", fin = IsFinal(d), paused = st == "paused", retrying = st.StartsWith("retrying", StringComparison.Ordinal);
            bool failed = fin && !done;
            int x0 = M, xr = Width - M;

            r.Close = new Rectangle(xr - 14, y + 2, 14, 14);
            TextRenderer.DrawText(g, "\u00D7", fName, r.Close, Ink, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
            DrawName(g, string.IsNullOrEmpty(d.FileName) ? "(file)" : d.FileName, new Rectangle(x0, y, r.Close.Left - 6 - x0, NAME_H));
            y += NAME_H + 4;

            var bar = new Rectangle(x0, y, xr - x0, BAR_H);
            var bin = Sunken(g, bar);
            double frac = d.TotalBytes > 0 ? Math.Min(1.0, (double)d.ReceivedBytes / d.TotalBytes) : (done ? 1.0 : -1);
            if (frac >= 0)
            {
                using (var b = new SolidBrush(BarLime)) g.FillRectangle(b, bin.X, bin.Y, (int)(bin.Width * frac), bin.Height);
            }
            else if (!fin)
            {
                int span = bin.Width / 5, off = (int)((DateTime.UtcNow.Ticks / 20000) % (bin.Width + span)) - span;
                var seg = Rectangle.Intersect(bin, new Rectangle(bin.X + off, bin.Y, span, bin.Height));
                using (var b = new SolidBrush(BarLime)) g.FillRectangle(b, seg);
            }
            y += BAR_H + 3;

            var dir = new Rectangle(x0, y, xr - x0, BAR_H);
            var din = Sunken(g, dir);
            string where = DirOf(d.Path);
            TextRenderer.DrawText(g, where.Length == 0 ? "(engine default folder)" : where, fMono, new Rectangle(din.X + 3, din.Y, din.Width - 6, din.Height),
                Green, TextFormatFlags.VerticalCenter | TextFormatFlags.PathEllipsis | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            y += BAR_H + 4;

            r.Buttons.Clear();
            int bx = x0;
            Action<string, string, bool> add = (id, text, en) =>
            {
                int bw = Math.Max(40, TextRenderer.MeasureText(text, fBtn).Width + 10);
                var b = new Btn { Id = id, Text = text, R = new Rectangle(bx, y, bw, BTN_H), Enabled = en };
                r.Buttons.Add(b); bx += bw + 4;
            };
            if (done)
            {
                add("open", "Open", true);
                add("folder", "Folder", true);
            }
            else if (failed)
            {
                add("retry", "Retry", st != "blocked");
            }
            else
            {
                add(paused ? "resume" : "pause", paused ? "Resume" : "Pause", r.Op != null);
                add("cancel", "Cancel", r.Op != null);
            }
            add("vt", r.Hashing ? "VirusTotal \u2026" : "VirusTotal", done && !r.Hashing);
            if (done && ExifTool.Supported(d.Path ?? "")) add("exif", "EXIF", true);
            foreach (var b in r.Buttons) DrawButton(g, b, r);

            string line, eta = null, shortLine = null;
            if (done) line = "DONE \u00B7 " + Human(d.ReceivedBytes);
            else if (paused) line = "PAUSED \u00B7 " + Human(d.ReceivedBytes) + (d.TotalBytes > 0 ? " / " + Human(d.TotalBytes) : "");
            else if (retrying) line = st.Substring(0, 1).ToUpperInvariant() + st.Substring(1);
            else if (failed) line = "\u26A0 " + st;
            else
            {
                line = "DOWN " + Human((long)r.Rate) + "/s \u00B7 " + Human(d.ReceivedBytes) + (d.TotalBytes > 0 ? " / " + Human(d.TotalBytes) : "");
                if (r.Rate > 1 && d.TotalBytes > d.ReceivedBytes)
                {
                    double s = (d.TotalBytes - d.ReceivedBytes) / r.Rate;
                    eta = s < 90 ? ((int)s) + " s" : s < 5400 ? ((int)(s / 60)) + " min" : (s / 3600).ToString("0.#") + " h";
                    line += " \u00B7 " + eta;
                }
                shortLine = "DOWN " + Human((long)r.Rate) + "/s" + (eta != null ? " \u00B7 " + eta : "");
            }

            int sx = bx + 6;
            int room = Math.Max(10, xr - sx);
            if (shortLine != null && TextRenderer.MeasureText(g, line, fLine, Size.Empty, TextFormatFlags.NoPadding).Width > room) line = shortLine;
            TextRenderer.DrawText(g, line, fLine, new Rectangle(sx, y, Math.Max(10, xr - sx), BTN_H), Ink,
                TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);

            y += BTN_H + 5;

            PaintScope(g, r, new Rectangle(x0, y, xr - x0, SCOPE_H), done, paused, retrying, failed);
        }

        private void PaintScope(Graphics g, Row r, Rectangle well, bool done, bool paused, bool retrying, bool failed)
        {
            var c = Sunken(g, well);
            var oldClip = g.Clip; g.SetClip(c);
            using (var p = new Pen(GreenLo)) { for (int gx = c.X + 8; gx < c.Right; gx += 8) g.DrawLine(p, gx, c.Y, gx, c.Bottom); g.DrawLine(p, c.X, c.Y + c.Height / 2, c.Right, c.Y + c.Height / 2); }

            float peak = 1; foreach (var v in r.Scope) if (v != null && v.Rate > peak) peak = (float)v.Rate;

            Func<float, float> scale = v => v <= 0 ? 0 : (float)(Math.Log(1 + v) / Math.Log(1 + peak));
            int n = SAMPLES, bw = Math.Max(1, c.Width / n);
            int startX = c.Right - n * bw;
            r.ScopeRect = c; r.ScopeX0 = startX; r.ScopeBw = bw;
            using (var b = new SolidBrush(Green))
            using (var hi = new SolidBrush(GreenHi))
            using (var sel = new SolidBrush(Color.White))
                for (int i = 0; i < n; i++)
                {
                    var smp = r.Scope[(r.Head + i) % n];
                    float v = smp == null ? 0 : (float)smp.Rate;
                    int h = (int)(scale(v) * (c.Height - 4));
                    int bx = startX + i * bw;
                    bool isSel = smp != null && ReferenceEquals(smp, r.Selected), isHov = smp != null && ReferenceEquals(smp, r.Hover);
                    if (isSel || isHov)
                        using (var col = new SolidBrush(Color.FromArgb(isSel ? 0x55 : 0x30, GreenHi))) g.FillRectangle(col, bx, c.Y, Math.Max(1, bw), c.Height);
                    if (h <= 0) { if (!done && smp != null) g.FillRectangle(b, bx, c.Bottom - 2, bw, 1); continue; }
                    g.FillRectangle(isSel ? sel : (isHov || i >= n - 2) ? hi : b, bx, c.Bottom - 1 - h, Math.Max(1, bw - (bw > 2 ? 1 : 0)), h);
                }

            string tag = done ? "EOF" : paused ? "HOLD" : retrying ? "NO CARRIER" : failed ? "LOST" : null;
            if (tag != null)
            {
                bool blink = retrying && (DateTime.UtcNow.Millisecond / 500) == 0;
                if (!blink) TextRenderer.DrawText(g, tag, fMono, new Rectangle(c.X + 4, c.Y + 1, c.Width - 8, 14), GreenHi, TextFormatFlags.NoPadding);
            }

            var hv = r.Hover;
            string right = hv != null
                ? Ago(hv.At) + " \u00B7 " + Human((long)hv.Rate) + "/s \u00B7 " + Human(hv.Length)
                : "peak " + Human((long)peak) + "/s";
            TextRenderer.DrawText(g, right, fMono, new Rectangle(c.X, c.Y + 1, c.Width - 4, 14), hv != null ? GreenHi : Green, TextFormatFlags.Right | TextFormatFlags.NoPadding);
            g.Clip = oldClip;
        }

        private void DrawName(Graphics g, string name, Rectangle r)
        {
            string ext = DangerousExt(name);
            if (ext == null || !name.EndsWith(ext, StringComparison.OrdinalIgnoreCase))
            {
                TextRenderer.DrawText(g, name, fName, r, Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                if (ext != null) Outlined(g, "\u26A0", new Point(r.Right - 12, r.Y + 2));   
                return;
            }
            string stem = name.Substring(0, name.Length - ext.Length);
            int extW = TextRenderer.MeasureText(g, ext, fName, Size.Empty, TextFormatFlags.NoPadding).Width + 4;
            var stemR = new Rectangle(r.X, r.Y, Math.Max(10, r.Width - extW), r.Height);
            TextRenderer.DrawText(g, stem, fName, stemR, Ink, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            int stemW = Math.Min(stemR.Width, TextRenderer.MeasureText(g, stem, fName, Size.Empty, TextFormatFlags.NoPadding).Width);
            int ty = r.Y + (r.Height - TextRenderer.MeasureText(g, ext, fName, Size.Empty, TextFormatFlags.NoPadding).Height) / 2;
            Outlined(g, ext, new Point(r.X + stemW + 1, ty));
        }

        private void Outlined(Graphics g, string s, Point p)
        {
            for (int dx = -1; dx <= 1; dx++)
                for (int dy = -1; dy <= 1; dy++)
                    if (dx != 0 || dy != 0) TextRenderer.DrawText(g, s, fName, new Point(p.X + dx, p.Y + dy), Color.White, TextFormatFlags.NoPadding);
            TextRenderer.DrawText(g, s, fName, p, Red, TextFormatFlags.NoPadding);
        }

        private void DrawButton(Graphics g, Btn b, Row r)
        {
            bool down = ReferenceEquals(_pressed, b), over = ReferenceEquals(_hover, b) && b.Enabled;
            using (var f = new SolidBrush(down ? Color.FromArgb(0xE4, 0xE4, 0xE4) : over ? Color.FromArgb(0xF7, 0xF7, 0xF7) : BtnFace)) g.FillRectangle(f, b.R);

            Color tl = down ? BevLt : BevDk, brc = down ? BevDk : BevLt;
            int w1 = b.R.Right - 1, h1 = b.R.Bottom - 1;
            using (var p1 = new Pen(tl)) { g.DrawLine(p1, b.R.X, b.R.Y, w1, b.R.Y); g.DrawLine(p1, b.R.X, b.R.Y, b.R.X, h1); }
            using (var p2 = new Pen(brc)) { g.DrawLine(p2, b.R.X, h1, w1, h1); g.DrawLine(p2, w1, b.R.Y, w1, h1); }
            TextRenderer.DrawText(g, b.Text, fBtn, b.R, b.Enabled ? Ink : BevDk, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
        }

        private static Rectangle Sunken(Graphics g, Rectangle r)
        {
            using (var dk = new Pen(BevDk)) { g.DrawLine(dk, r.X, r.Y, r.Right - 1, r.Y); g.DrawLine(dk, r.X, r.Y, r.X, r.Bottom - 1); }
            using (var lt = new Pen(BevLt)) { g.DrawLine(lt, r.X, r.Bottom - 1, r.Right - 1, r.Bottom - 1); g.DrawLine(lt, r.Right - 1, r.Y, r.Right - 1, r.Bottom - 1); }
            var inner = new Rectangle(r.X + 1, r.Y + 1, r.Width - 2, r.Height - 2);
            using (var b = new SolidBrush(Color.Black)) g.FillRectangle(b, inner);
            return inner;
        }

        private static void Raised(Graphics g, Rectangle r)
        {
            int w1 = r.Right - 1, h1 = r.Bottom - 1;
            using (var lt = new Pen(BevLt)) { g.DrawLine(lt, r.X, r.Y, w1, r.Y); g.DrawLine(lt, r.X, r.Y, r.X, h1); }
            using (var dk = new Pen(BevDk)) { g.DrawLine(dk, r.X, h1, w1, h1); g.DrawLine(dk, w1, r.Y, w1, h1); }
        }

        private Btn HitBtn(Point p, out Row row)
        {
            foreach (var r in _rows) foreach (var b in r.Buttons) if (b.R.Contains(p)) { row = r; return b; }
            row = null; return null;
        }

        private ChunkSample HitScope(Point p, out Row row)
        {
            foreach (var r in _rows)
            {
                if (!r.ScopeRect.Contains(p) || r.ScopeBw <= 0) continue;
                row = r;
                int i = (p.X - r.ScopeX0) / r.ScopeBw;
                if (i < 0 || i >= SAMPLES) return null;

                for (int d = 0; d < 4; d++)
                    foreach (int k in new[] { i - d, i + d })
                        if (k >= 0 && k < SAMPLES) { var smp = r.Scope[(r.Head + k) % SAMPLES]; if (smp != null) return smp; }
                return null;
            }
            row = null; return null;
        }

        private ChunkInspectorWindow _inspector;

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Row srow; var smpH = HitScope(e.Location, out srow);
            foreach (var r in _rows) { var want = ReferenceEquals(r, srow) ? smpH : null; if (!ReferenceEquals(r.Hover, want)) { r.Hover = want; Invalidate(); } }
            if (smpH != null) { Cursor = Cursors.Cross; return; }
            Row row; var b = HitBtn(e.Location, out row);
            bool closeHit = false; foreach (var r in _rows) if (r.Close.Contains(e.Location)) closeHit = true;
            if (!ReferenceEquals(b, _hover)) { _hover = b; Invalidate(); }
            Cursor = (b != null && b.Enabled) || closeHit ? Cursors.Hand : Cursors.Default;
        }

        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); _hover = null; _pressed = null; foreach (var r in _rows) r.Hover = null; Invalidate(); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Row row; var b = HitBtn(e.Location, out row);
            if (b != null && b.Enabled && e.Button == MouseButtons.Left) { _pressed = b; Invalidate(); }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            var was = _pressed; _pressed = null; Invalidate();
            if (e.Button != MouseButtons.Left) return;

            Row srow; var smp = HitScope(e.Location, out srow);
            if (smp != null) { Inspect(srow, smp); return; }

            foreach (var r in _rows.ToArray())
                if (r.Close.Contains(e.Location)) { _rows.Remove(r); if (_rows.Count == 0 && !Pinned) { _tick.Stop(); Hide(); } else Relayout(); Invalidate(); return; }

            Row row; var b = HitBtn(e.Location, out row);
            if (b == null || !ReferenceEquals(b, was) || !b.Enabled) return;
            try { Act(row, b.Id); } catch (Exception ex) { ActivityLog.General("download toast: " + ex.Message); }
            Invalidate();
        }

        private void Act(Row r, string id)
        {
            var d = r.Rec;
            switch (id)
            {
                case "pause":
                    d.UserPaused = true;
                    if (r.Op != null && r.Op.State == CoreWebView2DownloadState.InProgress) r.Op.Pause();
                    d.State = "paused";
                    break;
                case "resume":
                    d.UserPaused = false;
                    d.State = "in progress";
                    if (r.Op != null) r.Op.Resume();
                    break;
                case "cancel":

                    if (MessageBox.Show(_owner, "Cancel this download and delete the partial file?\r\n\r\n" + d.FileName,
                            "Aloha \u2014 cancel download", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
                    d.UserPaused = false; d.UserCancelled = true;
                    if (r.Op != null) r.Op.Cancel();
                    break;
                case "retry":
                    if (_retry != null) { _retry(d); _rows.Remove(r); Relayout(); }
                    break;
                case "open":
                    if (DangerousExt(d.FileName) != null &&
                        MessageBox.Show(_owner, "." + DangerousExt(d.FileName) + " files can run code just by being opened.\r\n\r\nOpen " + d.FileName + " anyway?",
                            "Aloha \u2014 open file", MessageBoxButtons.YesNo, MessageBoxIcon.Warning, MessageBoxDefaultButton.Button2) != DialogResult.Yes) return;
                    if (System.IO.File.Exists(d.Path)) System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(d.Path) { UseShellExecute = true });
                    break;
                case "folder":
                    if (System.IO.File.Exists(d.Path)) System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + d.Path + "\"");
                    break;
                case "vt":
                    VirusTotal(r);
                    break;
                case "exif":
                    if (System.IO.File.Exists(d.Path))
                        new ExifWindow(d.Path, System.IO.Path.Combine(System.IO.Path.GetDirectoryName(d.Path) ?? "", "backup"), _openInTab, null).Show(_owner);
                    break;
            }
        }

        private async void VirusTotal(Row r)
        {
            if (r.Sha != null) { Open(r); return; }
            if (!System.IO.File.Exists(r.Rec.Path)) return;
            r.Hashing = true; Invalidate();
            try
            {
                using (var s = new System.IO.FileStream(r.Rec.Path, System.IO.FileMode.Open, System.IO.FileAccess.Read, System.IO.FileShare.ReadWrite))
                    r.Sha = await Aloha.Media.HashEngine.ComputeAsync(s, "SHA-256", s.Length);
            }
            catch (Exception ex) { ActivityLog.General("VirusTotal hash failed: " + ex.Message); }
            r.Hashing = false;
            if (!IsDisposed) { Invalidate(); if (r.Sha != null) Open(r); }
        }

        private void Open(Row r)
        {
            string url = "https://www.virustotal.com/gui/file/" + r.Sha.ToLowerInvariant();
            if (_openInTab != null) _openInTab(url);
        }

        private void Inspect(Row r, ChunkSample smp)
        {
            foreach (var x in _rows) x.Selected = null;
            r.Selected = smp; Invalidate();
            if (_inspector == null || _inspector.IsDisposed)
            {
                _inspector = new ChunkInspectorWindow();
                _inspector.FormClosed += (s, e) => { foreach (var x in _rows) x.Selected = null; if (!IsDisposed) Invalidate(); };
            }
            Func<ChunkSample, int, ChunkSample> step = (cur, dir) =>
            {
                int at = Array.IndexOf(r.Scope, cur); if (at < 0) return null;
                for (int k = 1; k < SAMPLES; k++)
                {
                    int idx = ((at + dir * k) % SAMPLES + SAMPLES) % SAMPLES;

                    int age = (idx - r.Head + SAMPLES) % SAMPLES, ageCur = (at - r.Head + SAMPLES) % SAMPLES;
                    if ((dir > 0 && age <= ageCur) || (dir < 0 && age >= ageCur)) return null;
                    if (r.Scope[idx] != null) return r.Scope[idx];
                }
                return null;
            };
            _inspector.Show(r.Rec, smp, step, s2 => { r.Selected = s2; if (!IsDisposed) Invalidate(); });
        }

        private static string Ago(DateTime utc)
        {
            double s = (DateTime.UtcNow - utc).TotalSeconds;
            return s < 1 ? "now" : s < 60 ? "\u2212" + s.ToString("0.0") + " s" : "\u2212" + ((int)(s / 60)) + " min";
        }

        internal static string Human(long b)
        {
            if (b < 1024) return b + " B";
            if (b < 1024 * 1024) return (b / 1024.0).ToString("0.#") + " KB";
            if (b < 1024L * 1024 * 1024) return (b / 1048576.0).ToString("0.#") + " MB";
            return (b / 1073741824.0).ToString("0.##") + " GB";
        }

        private static string DirOf(string path)
        {
            try { return string.IsNullOrEmpty(path) ? "" : System.IO.Path.GetDirectoryName(path) ?? ""; } catch { return ""; }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) { _tick.Dispose(); fName.Dispose(); fLine.Dispose(); fMono.Dispose(); fBtn.Dispose(); }
            base.Dispose(disposing);
        }
    }
}
