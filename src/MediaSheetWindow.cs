using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Aloha.Media
{
    public sealed class MediaSheetWindow : DafyFrame
    {

        private static readonly Color Bg      = Color.Black;
        private static readonly Color Txt     = Color.FromArgb(0xB4, 0xDC, 0xBA);
        private static readonly Color Dim     = Color.FromArgb(0x3E, 0x5C, 0x44);
        private static readonly Color Mid     = Color.FromArgb(0x7F, 0xAE, 0x86);
        private static readonly Color Bright  = Color.FromArgb(0xE6, 0xF4, 0xE8);
        private static readonly Color Yellow  = Color.FromArgb(0xFF, 0xD2, 0x3F);
        private static readonly Color Amber   = Color.FromArgb(0xFF, 0xB6, 0x2E);
        private static readonly Color BandTxt = Color.FromArgb(0x24, 0x18, 0x00);
        private static readonly Color Rule    = Color.FromArgb(0x1E, 0x33, 0x24);

        private sealed class Group { public string Name; public bool Broken; public string[] Algos; }
        private static readonly Group[] GROUPS =
        {
            new Group { Name = "Checksums · CRC / Adler", Algos = new[] { "CRC-32", "CRC-32C", "CRC-64/XZ", "Adler-32" } },
            new Group { Name = "Non-cryptographic · fast fingerprints", Algos = new[] { "xxHash3-128", "xxHash64", "MurmurHash3", "FNV-1a" } },
            new Group { Name = "Legacy · broken, identify only", Broken = true, Algos = new[] { "MD4", "MD5", "SHA-1" } },
            new Group { Name = "SHA-2", Algos = new[] { "SHA-224", "SHA-256", "SHA-384", "SHA-512", "SHA-512/256" } },
            new Group { Name = "SHA-3 / Keccak", Algos = new[] { "SHA3-256", "SHA3-512", "Keccak-256", "SHAKE128", "SHAKE256", "KangarooTwelve" } },
            new Group { Name = "BLAKE", Algos = new[] { "BLAKE2b-256", "BLAKE2s-128", "BLAKE3" } },
            new Group { Name = "Other / regional", Algos = new[] { "RIPEMD-160", "Whirlpool", "Tiger", "Skein-512", "Streebog-256", "SM3" } },
            new Group { Name = "Extendable-output (XOF)", Algos = new[] { "SHAKE256/4096" } },
        };

        private sealed class HashRow
        {
            public string Algo;
            public string Hex;
            public string Err;
            public int Pct = -1;
            public bool Busy;
            public Rectangle Button, Output;
        }

        private readonly string _url;
        private readonly IResourceResolver _resolver;
        private Meta _meta;
        private ResourceInfo _ri;
        private string _fail;
        private readonly List<HashRow> _rows = new List<HashRow>();
        private readonly Sheet _sheet;
        private Rectangle _computeAll;
        private readonly Font fMono   = new Font("Consolas", 9.5f);
        private readonly Font fSmall  = new Font("Consolas", 8.25f);
        private readonly Font fBand   = new Font("Consolas", 9.75f, FontStyle.Bold);
        private readonly Font fLabel  = new Font("Consolas", 8.25f);
        private readonly Queue<HashRow> _queue = new Queue<HashRow>();
        private bool _pumping;

        public static void Open(string url, IResourceResolver resolver)
        {
            var w = new MediaSheetWindow(url, resolver);
            if (MainWindow != null && !MainWindow.IsDisposed) w.Show(MainWindow); else w.Show();
            w.BeginRead();
        }

        private MediaSheetWindow(string url, IResourceResolver resolver)
            : base("MED-SHEET", NameOf(url))
        {
            _url = url; _resolver = resolver;
            Size = new Size(470, 620);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            KeyPreview = true;
            Font = new Font("Tahoma", 8.25f);
            ClientArea.BackColor = Bg;

            var canvas = SetupAlohaScroll(Bg);
            _sheet = new Sheet(this) { Left = 0, Top = 0, BackColor = Bg };
            canvas.Controls.Add(_sheet);
            foreach (var g in GROUPS) foreach (var a in g.Algos) _rows.Add(new HashRow { Algo = a });

            ClientArea.Resize += (s, e) => Relayout();
            Shown += (s, e) => Relayout();
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; } };
            FormClosed += (s, e) => { fMono.Dispose(); fSmall.Dispose(); fBand.Dispose(); fLabel.Dispose(); };
        }

        private static string NameOf(string url)
        {
            try
            {
                if (url.StartsWith("data:", StringComparison.OrdinalIgnoreCase)) return "(inline data)";
                string n = url.Split('/', '\\')[url.Split('/', '\\').Length - 1].Split('?', '#')[0];
                return n.Length == 0 ? url : Uri.UnescapeDataString(n);
            }
            catch { return url; }
        }

        private void BeginRead()
        {
            Task.Run(async () =>
            {
                try
                {
                    var ri = _resolver.Resolve(_url);
                    if (ri == null) throw new System.IO.IOException("cannot open resource");
                    var meta = await MediaMetadataHook.SniffAsync(ri);
                    Post(() => { _ri = ri; _meta = meta; SetTitle(meta.Name); Relayout(); });
                }
                catch (Exception ex) { Post(() => { _fail = ex.Message; Relayout(); }); }
            });
        }

        private void Post(Action a)
        {
            if (IsDisposed) return;
            try { BeginInvoke(a); } catch { }
        }

        private void Enqueue(HashRow r)
        {
            if (r.Busy || r.Hex != null || _ri == null) return;
            r.Busy = true; r.Err = null; r.Pct = 0; _sheet.Invalidate();
            bool start;
            lock (_queue) { _queue.Enqueue(r); start = !_pumping; if (start) _pumping = true; }
            if (start) Task.Run(Pump);
        }

        private async Task Pump()
        {
            while (true)
            {
                HashRow r;
                lock (_queue) { if (_queue.Count == 0) { _pumping = false; return; } r = _queue.Dequeue(); }
                try
                {
                    var prog = new Progress<int>(p => { r.Pct = p; _sheet.Invalidate(); });
                    string hex;
                    using (var s = _ri.Open()) hex = await HashEngine.ComputeAsync(s, r.Algo, _ri.Length, prog);
                    r.Hex = hex;
                }
                catch (Exception ex) { r.Err = ex.Message; }
                r.Busy = false; r.Pct = -1;
                Post(() => _sheet.Invalidate());
            }
        }

        private void Relayout()
        {
            if (_sheet == null) return;
            int w = Math.Max(300, AlohaContent.Width);
            _sheet.Width = w;
            _sheet.Height = Math.Max(AlohaContent.Height, _sheet.Measure(w));
            _sheet.Invalidate();
        }

        private sealed class Sheet : Panel
        {
            private readonly MediaSheetWindow W;
            public Sheet(MediaSheetWindow w)
            {
                W = w;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
                Cursor = Cursors.Default;
            }

            private const int PAD = 12, LABEL_W = 104, BTN_W = 62, BTN_H = 17, FP_W = 60, ROW_H = 24;

            public int Measure(int width) { using (var g = CreateGraphics()) return Draw(g, width, false); }

            protected override void OnPaint(PaintEventArgs e)
            {
                e.Graphics.Clear(Bg);
                Draw(e.Graphics, Width, true);
            }

            private int Draw(Graphics g, int w, bool paint)
            {
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
                int y = 8;
                var m = W._meta;

                if (W._fail != null)
                {
                    if (paint)
                    {
                        TextRenderer.DrawText(g, "could not read this file", W.fMono, new Point(PAD, y), Amber);
                        TextRenderer.DrawText(g, W._fail, W.fSmall, new Rectangle(PAD, y + 20, w - 2 * PAD, 200), Yellow, TextFormatFlags.WordBreak);
                    }
                    return y + 80;
                }
                if (m == null)
                {
                    if (paint) TextRenderer.DrawText(g, "reading file\u2026", W.fMono, new Point(PAD, y), Dim);
                    return y + 40;
                }

                int bandH = 24;
                if (paint)
                {
                    using (var b = new SolidBrush(Yellow)) g.FillRectangle(b, 0, y, w, bandH);
                    TextRenderer.DrawText(g, "\u26A0 " + m.Type, W.fBand, new Rectangle(PAD, y, w, bandH), BandTxt, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    TextRenderer.DrawText(g, m.Magic ?? "", W.fMono, new Rectangle(0, y, w - PAD, bandH), BandTxt, TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                y += bandH;

                if (!string.IsNullOrEmpty(m.Declared))
                {
                    int h2 = 20;
                    if (paint)
                    {
                        using (var b = new SolidBrush(Amber)) g.FillRectangle(b, 0, y, w, h2);
                        TextRenderer.DrawText(g, "\u26A0 EXTENSION SAYS ." + m.Declared.ToUpperInvariant() + " \u00B7 BYTES SAY " + m.Ext, W.fBand,
                            new Rectangle(PAD, y, w, h2), BandTxt, TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                    }
                    y += h2;
                }
                y += 8;

                foreach (var r in m.Rows)
                {
                    bool dim = r[1] != null && r[1].StartsWith("\u2014");
                    if (paint)
                    {
                        TextRenderer.DrawText(g, r[0].ToUpperInvariant(), W.fLabel, new Point(PAD, y + 2), Dim);
                        TextRenderer.DrawText(g, r[1], W.fMono, new Rectangle(PAD + LABEL_W, y, w - PAD - LABEL_W - PAD, 18), dim ? Dim : Txt, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                    }
                    y += 18;
                }
                {
                    var sha = W.RowFor("SHA-256");
                    string v = sha != null && sha.Hex != null ? sha.Hex : (m.Sha != null ? m.Sha : "\u2014 click to compute");
                    if (paint)
                    {
                        TextRenderer.DrawText(g, "SHA-256", W.fLabel, new Point(PAD, y + 2), Dim);
                        TextRenderer.DrawText(g, v, W.fMono, new Rectangle(PAD + LABEL_W, y, w - PAD - LABEL_W - PAD, 18), v.StartsWith("\u2014") ? Dim : Txt, TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                    }
                    y += 18;
                }
                if (!string.IsNullOrEmpty(m.Note))
                {
                    if (paint) TextRenderer.DrawText(g, m.Note, W.fSmall, new Rectangle(PAD, y + 2, w - 2 * PAD, 16), Dim, TextFormatFlags.EndEllipsis);
                    y += 18;
                }

                y = Section(g, w, y, "signature", paint);
                if (paint) TextRenderer.DrawText(g, m.Signature ?? "", W.fMono, new Rectangle(PAD, y, w - 2 * PAD, 40), Txt, TextFormatFlags.WordBreak);
                y += TextRenderer.MeasureText(g, m.Signature ?? "", W.fMono, new Size(w - 2 * PAD, 100), TextFormatFlags.WordBreak).Height + 2;

                y = Section(g, w, y, "hashes & fingerprints \u00B7 " + W._rows.Count, paint, out W._computeAll, "compute all");

                foreach (var grp in GROUPS)
                {
                    y += 6;
                    if (paint) TextRenderer.DrawText(g, grp.Name.ToUpperInvariant(), W.fLabel, new Point(PAD, y), grp.Broken ? Amber : Dim);
                    y += 18;
                    foreach (var algo in grp.Algos)
                    {
                        var r = W.RowFor(algo);
                        r.Button = new Rectangle(w - PAD - BTN_W, y + (ROW_H - BTN_H) / 2, BTN_W, BTN_H);
                        int fpX = r.Button.Left - 8 - FP_W;
                        r.Output = new Rectangle(PAD + 112, y, Math.Max(10, fpX - 8 - (PAD + 112)), ROW_H);
                        if (paint)
                        {
                            TextRenderer.DrawText(g, algo, W.fSmall, new Rectangle(PAD, y, 110, ROW_H), Mid, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                            string outTxt; Color outCol = Txt;
                            if (r.Hex != null) outTxt = r.Hex.Length > 22 ? r.Hex.Substring(0, 10) + "\u2026" + r.Hex.Substring(r.Hex.Length - 8) : r.Hex;
                            else if (r.Err != null) { outTxt = r.Err; outCol = Amber; }
                            else if (r.Busy) { outTxt = r.Pct > 0 ? "reading\u2026 " + r.Pct + "%" : "reading\u2026"; outCol = Dim; }
                            else { outTxt = "\u2014"; outCol = Dim; }
                            TextRenderer.DrawText(g, outTxt, W.fSmall, r.Output, outCol, TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                            if (r.Hex != null) DrawFingerprint(g, new Rectangle(fpX, y + (ROW_H - 10) / 2, FP_W, 10), r.Hex + algo);
                            if (r.Hex == null)
                            {
                                string bt = r.Busy ? "\u2026" : (r.Err != null ? "retry" : "compute");
                                DrawButton(g, r.Button, bt, !r.Busy);
                            }
                            using (var p = new Pen(Rule) { DashStyle = DashStyle.Dash }) g.DrawLine(p, PAD, y + ROW_H - 1, w - PAD, y + ROW_H - 1);
                        }
                        y += ROW_H;
                    }
                }
                return y + 12;
            }

            private int Section(Graphics g, int w, int y, string title, bool paint) { Rectangle none; return Section(g, w, y, title, paint, out none, null); }
            private int Section(Graphics g, int w, int y, string title, bool paint, out Rectangle btn, string btnText)
            {
                y += 9;
                if (paint) using (var p = new Pen(Rule) { DashStyle = DashStyle.Dash }) g.DrawLine(p, PAD, y, w - PAD, y);
                y += 7;
                btn = Rectangle.Empty;
                if (btnText != null)
                {
                    btn = new Rectangle(w - PAD - BTN_W - 18, y - 2, BTN_W + 18, BTN_H);
                    if (paint) DrawButton(g, btn, btnText, true, true);
                }
                if (paint) TextRenderer.DrawText(g, title.ToUpperInvariant(), W.fLabel, new Point(PAD, y), Dim);
                return y + 20;
            }

            private void DrawButton(Graphics g, Rectangle r, string text, bool enabled, bool solid = false)
            {
                if (solid)
                {
                    using (var b = new SolidBrush(Bright)) g.FillRectangle(b, r);
                    TextRenderer.DrawText(g, text, W.fSmall, r, Color.FromArgb(0x04, 0x16, 0x08), TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
                else
                {
                    using (var p = new Pen(enabled ? Dim : Rule)) g.DrawRectangle(p, r.X, r.Y, r.Width - 1, r.Height - 1);
                    TextRenderer.DrawText(g, text, W.fSmall, r, enabled ? Mid : Dim, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding);
                }
            }

            private static void DrawFingerprint(Graphics g, Rectangle r, string key)
            {
                uint h = 0; foreach (char c in key) h = unchecked(h * 131 + c);
                Func<double> rnd = () => { h = unchecked(h * 1103515245u + 12345u); return h / 4294967296.0; };
                int cw = r.Width / 10;
                for (int i = 0; i < 10; i++)
                {
                    double hue = 95 + Math.Floor(rnd() * 45), l = (42 + Math.Floor(rnd() * 26)) / 100.0;
                    using (var b = new SolidBrush(Hsl(hue, 0.9, l))) g.FillRectangle(b, r.X + i * cw, r.Y, cw - 1, r.Height);
                }
            }
            private static Color Hsl(double h, double s, double l)
            {
                double c = (1 - Math.Abs(2 * l - 1)) * s, x = c * (1 - Math.Abs((h / 60) % 2 - 1)), m = l - c / 2;
                double r = 0, g = 0, b = 0;
                if (h < 60) { r = c; g = x; } else if (h < 120) { r = x; g = c; } else if (h < 180) { g = c; b = x; }
                else if (h < 240) { g = x; b = c; } else if (h < 300) { r = x; b = c; } else { r = c; b = x; }
                return Color.FromArgb((int)((r + m) * 255), (int)((g + m) * 255), (int)((b + m) * 255));
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                bool hand = W._meta != null && (W._computeAll.Contains(e.Location) || HitRow(e.Location) != null);
                Cursor = hand ? Cursors.Hand : Cursors.Default;
            }

            private HashRow HitRow(Point p)
            {
                foreach (var r in W._rows)
                {
                    if (r.Hex == null && !r.Busy && r.Button.Contains(p)) return r;
                    if (r.Hex != null && r.Output.Contains(p)) return r;
                }
                return null;
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                base.OnMouseClick(e);
                if (W._meta == null || e.Button != MouseButtons.Left) return;
                if (W._computeAll.Contains(e.Location)) { foreach (var r in W._rows) W.Enqueue(r); return; }
                var hit = HitRow(e.Location);
                if (hit == null) return;
                if (hit.Hex != null) { try { Clipboard.SetText(hit.Hex); } catch { } return; }
                W.Enqueue(hit);
            }
        }

        private HashRow RowFor(string algo) { foreach (var r in _rows) if (r.Algo == algo) return r; return null; }
    }
}
