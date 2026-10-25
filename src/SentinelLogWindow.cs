using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace Aloha
{

    public class SentinelLogWindow : DafyFrame
    {

        private static readonly Color Fab      = Color.FromArgb(0xFA, 0xFA, 0xFB);
        private static readonly Color Ink       = Color.FromArgb(0x20, 0x20, 0x20);
        private static readonly Color InkDim    = Color.FromArgb(0x70, 0x70, 0x70);
        private static readonly Color Lime       = Color.FromArgb(0x0a, 0x84, 0x2a);
        private static readonly Color Frame      = Color.FromArgb(0x80, 0x80, 0x80);

        private class Line { public string t, thr, msg, sig; }
        private readonly LinkedList<Line> lines = new LinkedList<Line>();
        private const int MAX_LINES = 400;
        private int scroll = 0;

        public class Finding
        {
            public string label;
            public RectangleF box;
            public string kind;
        }
        private readonly List<Finding> findings = new List<Finding>();
        private string lastSvg = null;
        private bool showSvg = false;

        private Panel canvas;
        private System.Windows.Forms.Timer tick;

        public SentinelLogWindow() : base("SENTINEL.LOG", "SENTINEL \u2014 log / inspect")
        {
            Size = new Size(620, 620);
            MinimumSize = new Size(460, 460);
            ClientArea.BackColor = Fab;

            canvas = new Panel { Dock = DockStyle.Fill, BackColor = Fab };
            typeof(Control).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(canvas, true, null);
            canvas.Paint += Canvas_Paint;
            canvas.MouseDown += Canvas_MouseDown;
            canvas.MouseWheel += (s, e) => { scroll = Math.Max(0, scroll + (e.Delta > 0 ? 3 : -3)); canvas.Invalidate(); };
            ClientArea.Controls.Add(canvas);

            KeyPreview = true;
            KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter && showSvg) { showSvg = false; canvas.Invalidate(); } };

            tick = new System.Windows.Forms.Timer { Interval = 120 };
            tick.Tick += (s, e) => canvas.Invalidate();
            tick.Start();
            FormClosed += (s, e) => { try { tick?.Stop(); tick?.Dispose(); } catch { } };
        }

        public void Push(string thread, string msg, string sig = null)
        {
            lines.AddFirst(new Line { t = DateTime.Now.ToString("HH:mm:ss"), thr = thread ?? "", msg = msg ?? "", sig = sig ?? "" });
            while (lines.Count > MAX_LINES) lines.RemoveLast();
        }

        public void SetFindings(IEnumerable<Finding> found, string svgText)
        {
            findings.Clear();
            if (found != null) findings.AddRange(found);
            lastSvg = svgText;
            showSvg = findings.Count > 0;
            Push("svg", "rendered " + findings.Count + " findings");
        }

        private void Canvas_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int W = canvas.Width, H = canvas.Height;
            g.Clear(Fab);

            int svgH = showSvg ? Math.Min(H / 2, W) : 0;
            int logTop = svgH > 0 ? svgH + 4 : 0;

            if (svgH > 0) DrawSvgPanel(g, 0, 0, W, svgH);
            DrawLog(g, 0, logTop, W, H - logTop);
        }

        private void DrawLog(Graphics g, int x, int y, int w, int h)
        {
            using (var bd = new Pen(Frame)) g.DrawRectangle(bd, x + 2, y + 2, w - 5, h - 5);
            using (var ff = new Font("Consolas", 7f))
            using (var ta = new SolidBrush(Ink))
            using (var tb = new SolidBrush(InkDim))
            using (var tg = new SolidBrush(Lime))
            {
                int rows = (h - 14) / 11;
                int yy = y + 7;
                int i = 0, skip = scroll;
                foreach (var ln in lines)
                {
                    if (skip-- > 0) continue;
                    if (i++ >= rows) break;

                    g.DrawString(ln.t, ff, tb, x + 6, yy);
                    g.DrawString(ln.thr, ff, tg, x + 56, yy);
                    string msg = ln.msg.Length > 26 ? ln.msg.Substring(0, 26) : ln.msg;
                    g.DrawString(msg, ff, ta, x + 92, yy);
                    yy += 11;
                }
            }
        }

        private void DrawSvgPanel(Graphics g, int x, int y, int w, int h)
        {
            using (var bd = new Pen(Lime)) g.DrawRectangle(bd, x + 2, y + 2, w - 5, h - 5);

            int m = 10;
            var page = new Rectangle(x + m, y + m, w - 2 * m, h - 2 * m);
            using (var pf = new Pen(Color.FromArgb(60, Lime))) g.DrawRectangle(pf, page);

            using (var ff = new Font("Consolas", 6.5f))
            using (var tl = new SolidBrush(Ink))
            {
                foreach (var f in findings)
                {

                    float fx = page.X + f.box.X * page.Width;
                    float fy = page.Y + f.box.Y * page.Height;
                    float fw = Math.Max(3, f.box.Width * page.Width);
                    float fh = Math.Max(3, f.box.Height * page.Height);
                    Color c = f.kind == "hidden" ? Color.FromArgb(0xff, 0x9a, 0x33)      
                            : f.kind == "form"   ? Color.FromArgb(0x5c, 0xc8, 0xe8)
                            : Lime;
                    using (var p = new Pen(c)) g.DrawRectangle(p, fx, fy, fw, fh);
                    using (var b = new SolidBrush(Color.FromArgb(30, c))) g.FillRectangle(b, fx, fy, fw, fh);
                    string lbl = f.label.Length > 18 ? f.label.Substring(0, 18) : f.label;
                    g.DrawString(lbl, ff, tl, fx + 1, fy - 8 < y ? fy + 1 : fy - 8);
                }
            }
            using (var hint = new Font("Consolas", 6.5f))
            using (var hb = new SolidBrush(InkDim))
                g.DrawString("click: copy/save  \u00b7  Enter: hide", hint, hb, x + 8, y + h - 12);
        }

        private void Canvas_MouseDown(object sender, MouseEventArgs e)
        {
            if (!showSvg) return;
            int W = canvas.Width, H = canvas.Height;
            int svgH = Math.Min(H / 2, W);
            if (e.Y > svgH) return;

            try
            {
                var sb = new System.Text.StringBuilder();
                foreach (var f in findings) sb.AppendLine(f.kind + "\t" + f.label +
                    "\t@(" + f.box.X.ToString("0.00") + "," + f.box.Y.ToString("0.00") + ")");
                string text = sb.ToString();
                try { Clipboard.SetText((lastSvg ?? "") + "\r\n\r\n" + text); } catch { }

                string dir = AlohaPaths.Tmp;
                string stamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                if (!string.IsNullOrEmpty(lastSvg))
                    File.WriteAllText(Path.Combine(dir, "probe_" + stamp + ".svg"), lastSvg);
                File.WriteAllText(Path.Combine(dir, "probe_" + stamp + ".txt"), text);
                Push("svg", "copied + saved probe_" + stamp);
            }
            catch (Exception ex) { Push("svg", "save err: " + ex.GetType().Name); }
        }
    }
}
