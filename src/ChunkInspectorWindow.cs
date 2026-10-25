using System;
using System.Drawing;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Aloha
{

    public sealed class ChunkSample
    {
        public DateTime At;
        public double Seconds;
        public double Rate;
        public long From, To;
        public bool Restarted;
        public string State;
        public long Length { get { return To >= From ? To - From + 1 : 0; } }
    }

    public sealed class ChunkInspectorWindow : DafyFrame
    {
        private static readonly Color Bg      = Color.Black;
        private static readonly Color Green   = Color.FromArgb(0xB4, 0xDC, 0xBA);
        private static readonly Color GreenHi = Color.FromArgb(0xE6, 0xF4, 0xE8);
        private static readonly Color GreenLo = Color.FromArgb(0x3A, 0x55, 0x40);
        private static readonly Color Amber   = Color.FromArgb(0xFF, 0xB6, 0x2E);

        private const int READ_MAX = 64 * 1024, DUMP = 512;

        private readonly Panel canvas;
        private readonly Sheet sheet;
        private readonly Font fMono = new Font("Consolas", 9f);
        private readonly Font fBold = new Font("Consolas", 9f, FontStyle.Bold);

        private DownloadRecord rec;
        private ChunkSample cur;
        private Func<ChunkSample, int, ChunkSample> step;
        private Action<ChunkSample> onSelect;

        private byte[] data; private string source, error; private double entropy; private int[] hist = new int[256];

        public ChunkInspectorWindow() : base("DL-CHUNK", "chunk")
        {
            Size = new Size(620, 560);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            KeyPreview = true;
            ClientArea.BackColor = Bg;
            canvas = SetupAlohaScroll(Bg);
            sheet = new Sheet(this) { BackColor = Bg, Left = 0, Top = 0 };
            canvas.Controls.Add(sheet);
            ClientArea.Resize += (s, e) => Relayout();
            Shown += (s, e) => Relayout();
            KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; }
                else if (e.KeyCode == Keys.Left)  { Walk(-1); e.Handled = true; }
                else if (e.KeyCode == Keys.Right) { Walk(+1); e.Handled = true; }
            };
            FormClosed += (s, e) => { fMono.Dispose(); fBold.Dispose(); };
        }

        public void Show(DownloadRecord record, ChunkSample sample, Func<ChunkSample, int, ChunkSample> walker, Action<ChunkSample> selected)
        {
            rec = record; step = walker; onSelect = selected;
            LoadSample(sample);
            if (!Visible) Show(MainWindow);
            BringToFront();
            try { Activate(); } catch { }
        }

        private void Walk(int dir)
        {
            if (cur == null || step == null) return;
            var nx = step(cur, dir);
            if (nx == null) return;
            LoadSample(nx);
            if (onSelect != null) onSelect(nx);
        }

        private void LoadSample(ChunkSample s)
        {
            cur = s; data = null; source = null; error = null; entropy = 0; hist = new int[256];
            SetTitle("chunk  ·  " + (rec != null ? rec.FileName : "") + "  ·  @" + s.From.ToString("N0"));
            if (s.Length <= 0) error = "no bytes arrived in this tick (a stall: the flat line)";
            else Read(s);
            Relayout();
        }

        private void Read(ChunkSample s)
        {
            foreach (var path in Candidates())
            {
                try
                {
                    if (!File.Exists(path)) continue;
                    using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                    {
                        if (fs.Length <= s.From) continue;
                        fs.Seek(s.From, SeekOrigin.Begin);
                        int want = (int)Math.Min(READ_MAX, Math.Min(s.Length, fs.Length - s.From));
                        var buf = new byte[want]; int got = 0, n;
                        while (got < want && (n = fs.Read(buf, got, want - got)) > 0) got += n;
                        if (got < want) Array.Resize(ref buf, got);
                        data = buf; source = path;
                        foreach (byte b in data) hist[b]++;
                        entropy = Entropy(hist, data.Length);
                        return;
                    }
                }
                catch (Exception ex) { error = ex.Message; }
            }
            if (error == null) error = "the bytes are not readable on disk yet — the engine is still holding them in a temp file Aloha can't locate";
        }

        private System.Collections.Generic.IEnumerable<string> Candidates()
        {
            if (rec == null || string.IsNullOrEmpty(rec.Path)) yield break;
            yield return rec.Path;
            yield return rec.Path + ".crdownload";
            string dir = null; try { dir = Path.GetDirectoryName(rec.Path); } catch { }
            if (dir == null || !Directory.Exists(dir)) yield break;
            string[] partials = new string[0];
            try { partials = Directory.GetFiles(dir, "*.crdownload"); } catch { }

            Array.Sort(partials, (a, b) => File.GetLastWriteTimeUtc(b).CompareTo(File.GetLastWriteTimeUtc(a)));
            foreach (var p in partials) yield return p;
        }

        private static double Entropy(int[] h, int n)
        {
            if (n <= 0) return 0;
            double e = 0;
            foreach (int c in h) if (c > 0) { double p = (double)c / n; e -= p * Math.Log(p, 2); }
            return e;
        }

        private static string Verdict(double e, byte[] d)
        {
            if (d == null || d.Length < 32) return "too small to judge";
            int printable = 0; foreach (byte b in d) if (b == 9 || b == 10 || b == 13 || (b >= 32 && b < 127)) printable++;
            if (printable > d.Length * 0.95) return e < 5.2 ? "plain text" : "text, dense (base64 / minified?)";
            if (e >= 7.9) return "compressed or encrypted";
            if (e >= 7.2) return "mostly compressed (media / archive body)";
            if (e >= 5.5) return "structured binary (code / tables)";
            return "sparse binary (padding / headers / zeros)";
        }

        private static string Magic(byte[] d)
        {
            if (d == null || d.Length < 4) return null;
            Func<int[], bool> at0 = sig => { if (d.Length < sig.Length) return false; for (int i = 0; i < sig.Length; i++) if (d[i] != sig[i]) return false; return true; };
            if (at0(new[] { 0x4D, 0x5A })) return "MZ — Windows executable (PE)";
            if (at0(new[] { 0x50, 0x4B, 0x03, 0x04 })) return "PK — ZIP container (zip / docx / jar / apk …)";
            if (at0(new[] { 0x25, 0x50, 0x44, 0x46 })) return "%PDF — PDF document";
            if (at0(new[] { 0x89, 0x50, 0x4E, 0x47 })) return "PNG image";
            if (at0(new[] { 0xFF, 0xD8, 0xFF })) return "JPEG image";
            if (at0(new[] { 0x47, 0x49, 0x46, 0x38 })) return "GIF image";
            if (at0(new[] { 0x1F, 0x8B })) return "gzip stream";
            if (at0(new[] { 0x37, 0x7A, 0xBC, 0xAF })) return "7-Zip archive";
            if (at0(new[] { 0x52, 0x61, 0x72, 0x21 })) return "RAR archive";
            if (at0(new[] { 0x7F, 0x45, 0x4C, 0x46 })) return "ELF executable";
            if (at0(new[] { 0x1A, 0x45, 0xDF, 0xA3 })) return "Matroska / WebM";
            if (d.Length > 8 && d[4] == 0x66 && d[5] == 0x74 && d[6] == 0x79 && d[7] == 0x70) return "ISO-BMFF (mp4 / mov / heic)";
            if (at0(new[] { 0xD0, 0xCF, 0x11, 0xE0 })) return "OLE2 compound file (legacy Office / msi)";
            return null;
        }

        private void Relayout()
        {
            if (sheet == null) return;
            int w = Math.Max(520, AlohaContent.Width);
            sheet.Width = w;
            using (var g = sheet.CreateGraphics()) sheet.Height = Math.Max(AlohaContent.Height, sheet.Draw(g, w, false));
            sheet.Invalidate();
        }

        private sealed class Sheet : Panel
        {
            private readonly ChunkInspectorWindow W;
            public Sheet(ChunkInspectorWindow w)
            {
                W = w;
                SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            }
            protected override void OnPaint(PaintEventArgs e) { e.Graphics.Clear(Bg); Draw(e.Graphics, Width, true); }

            private const int PAD = 12, LH = 16;

            public int Draw(Graphics g, int w, bool paint)
            {
                var s = W.cur; if (s == null) return 40;
                int y = 10;
                Action<string, string, Color> kv = (k, v, c) =>
                {
                    if (paint)
                    {
                        TextRenderer.DrawText(g, k, W.fMono, new Point(PAD, y), GreenLo, TextFormatFlags.NoPadding);
                        TextRenderer.DrawText(g, v, W.fMono, new Rectangle(PAD + 96, y, w - PAD * 2 - 96, LH), c, TextFormatFlags.NoPadding | TextFormatFlags.EndEllipsis);
                    }
                    y += LH;
                };

                kv("when", s.At.ToLocalTime().ToString("HH:mm:ss.fff") + "   (" + (s.Seconds * 1000).ToString("0") + " ms tick)", Green);
                kv("rate", DownloadToast.Human((long)s.Rate) + "/s", GreenHi);
                kv("range", s.Length > 0 ? s.From.ToString("N0") + " → " + s.To.ToString("N0") : "—", Green);
                kv("size", DownloadToast.Human(s.Length) + (s.Length > READ_MAX ? "   (first " + DownloadToast.Human(READ_MAX) + " analysed)" : ""), Green);
                kv("state", s.State + (s.Restarted ? "   · engine restarted from 0" : ""), Green);
                if (W.source != null) kv("source", W.source, GreenLo);

                if (W.data == null)
                {
                    y += 6;
                    if (paint) TextRenderer.DrawText(g, W.error ?? "", W.fMono, new Rectangle(PAD, y, w - PAD * 2, 60), Amber, TextFormatFlags.WordBreak | TextFormatFlags.NoPadding);
                    return y + 70;
                }

                if (s.From == 0) { string m = Magic(W.data); if (m != null) kv("magic", m, GreenHi); }
                kv("entropy", W.entropy.ToString("0.000") + " bits/byte   · " + Verdict(W.entropy, W.data), GreenHi);

                y += 4;
                var gauge = new Rectangle(PAD, y, w - PAD * 2, 8);
                if (paint)
                {
                    using (var p = new Pen(GreenLo)) g.DrawRectangle(p, gauge);
                    using (var b = new SolidBrush(Green)) g.FillRectangle(b, gauge.X + 1, gauge.Y + 1, (int)((gauge.Width - 1) * W.entropy / 8.0), gauge.Height - 1);
                    using (var p = new Pen(Color.Black)) for (int t = 1; t < 8; t++) { int tx = gauge.X + gauge.Width * t / 8; g.DrawLine(p, tx, gauge.Y + 1, tx, gauge.Bottom - 1); }
                }
                y += 16;

                if (paint) TextRenderer.DrawText(g, "byte histogram  00 → FF", W.fMono, new Point(PAD, y), GreenLo, TextFormatFlags.NoPadding);
                y += LH;
                var hr = new Rectangle(PAD, y, w - PAD * 2, 56);
                if (paint)
                {
                    using (var p = new Pen(GreenLo)) g.DrawRectangle(p, hr);
                    int max = 1; foreach (int c in W.hist) if (c > max) max = c;
                    float cw = (hr.Width - 2) / 256f;
                    using (var b = new SolidBrush(Green))
                    using (var hb = new SolidBrush(GreenHi))
                        for (int i = 0; i < 256; i++)
                        {
                            if (W.hist[i] == 0) continue;
                            int h = (int)((hr.Height - 3) * Math.Log(1 + W.hist[i]) / Math.Log(1 + max));
                            bool printable = i >= 32 && i < 127;
                            g.FillRectangle(printable ? hb : b, hr.X + 1 + i * cw, hr.Bottom - 1 - h, Math.Max(1f, cw - 0.3f), h);
                        }

                    using (var p = new Pen(Color.FromArgb(0x60, GreenHi)))
                    { float x1 = hr.X + 1 + 32 * cw, x2 = hr.X + 1 + 127 * cw; g.DrawLine(p, x1, hr.Bottom + 2, x2, hr.Bottom + 2); }
                }
                y += hr.Height + 12;

                if (paint) TextRenderer.DrawText(g, "first " + Math.Min(DUMP, W.data.Length) + " bytes   ← → walk bars · Esc closes", W.fMono, new Point(PAD, y), GreenLo, TextFormatFlags.NoPadding);
                y += LH + 2;
                int len = Math.Min(DUMP, W.data.Length);
                for (int off = 0; off < len; off += 16)
                {
                    if (paint)
                    {
                        var hex = new StringBuilder(); var asc = new StringBuilder();
                        for (int i = 0; i < 16; i++)
                        {
                            if (off + i < len) { byte b = W.data[off + i]; hex.Append(b.ToString("X2")).Append(i == 7 ? "  " : " "); asc.Append(b >= 32 && b < 127 ? (char)b : '·'); }
                            else hex.Append(i == 7 ? "    " : "   ");
                        }
                        TextRenderer.DrawText(g, (s.From + off).ToString("X8"), W.fMono, new Point(PAD, y), GreenLo, TextFormatFlags.NoPadding);
                        TextRenderer.DrawText(g, hex.ToString(), W.fMono, new Point(PAD + 76, y), Green, TextFormatFlags.NoPadding);
                        TextRenderer.DrawText(g, asc.ToString(), W.fMono, new Point(PAD + 76 + 412, y), GreenHi, TextFormatFlags.NoPadding);
                    }
                    y += LH - 1;
                }
                return y + 14;
            }
        }
    }
}
