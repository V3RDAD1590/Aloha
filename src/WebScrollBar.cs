using System;
using System.Drawing;
using System.Globalization;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;

namespace Aloha
{

    public class WebScrollBar : Control
    {
        public bool Horizontal { get; private set; }
        private CoreWebView2 core;
        private Timer poll;

        const int ARROW = 17, THICK = 17;
        private Rectangle aRect, bRect, trackRect, thumbRect;
        private bool dragging;
        private int dragGrab;

        private double sPos, sMax = 1, sView = 1;

        private readonly Color cFace     = Color.FromArgb(0xF0, 0xF0, 0xF0);
        private readonly Color cTrack    = Color.FromArgb(0xF4, 0xF4, 0xF4);
        private readonly Color cHi       = Color.White;
        private readonly Color cHi2      = Color.FromArgb(0xFD, 0xFD, 0xFD);
        private readonly Color cLo       = Color.FromArgb(0xC8, 0xC8, 0xC8);
        private readonly Color cLo2      = Color.FromArgb(0xB0, 0xB0, 0xB0);
        private readonly Color cTrackSh1 = Color.FromArgb(0xDA, 0xDA, 0xDA);
        private readonly Color cTrackSh2 = Color.FromArgb(0xE6, 0xE6, 0xE6);
        private readonly Color cArrow    = Color.FromArgb(0x50, 0x50, 0x50);

        public WebScrollBar(bool horizontal)
        {
            Horizontal = horizontal;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            if (horizontal) Height = THICK; else Width = THICK;
            BackColor = cFace;
        }

        public void Attach(CoreWebView2 c)
        {
            core = c;
            poll = new Timer { Interval = 120 };
            poll.Tick += async (s, e) => await Refresh();
            poll.Start();
        }

        public void Detach()
        {
            try { if (poll != null) { poll.Stop(); poll.Dispose(); } } catch { }
            poll = null;
            core = null;
        }

        private new async Task Refresh()
        {
            var c = core;
            if (c == null) return;
            try
            {
                string js = Horizontal
                    ? "[window.scrollX,document.documentElement.scrollWidth,window.innerWidth]"
                    : "[window.scrollY,document.documentElement.scrollHeight,window.innerHeight]";
                string r = await c.ExecuteScriptAsync(js);
                if (core == null) return;
                if (string.IsNullOrEmpty(r) || r == "null") return;
                var parts = r.Trim('[', ']', ' ').Split(',');
                if (parts.Length < 3) return;
                double.TryParse(parts[0], NumberStyles.Any, CultureInfo.InvariantCulture, out sPos);
                double.TryParse(parts[1], NumberStyles.Any, CultureInfo.InvariantCulture, out sMax);
                double.TryParse(parts[2], NumberStyles.Any, CultureInfo.InvariantCulture, out sView);
                if (sMax < 1) sMax = 1;
                if (sView < 1) sView = 1;
                if (!dragging && !IsDisposed) Invalidate();
            }
            catch {  }
        }

        private void ScrollTo(double pos)
        {
            if (core == null) return;
            double maxScroll = Math.Max(0, sMax - sView);
            pos = Math.Max(0, Math.Min(pos, maxScroll));
            sPos = pos;
            string p = pos.ToString(CultureInfo.InvariantCulture);
            string js = Horizontal
                ? "window.scrollTo(" + p + ",window.scrollY)"
                : "window.scrollTo(window.scrollX," + p + ")";
            try { var _ = core.ExecuteScriptAsync(js); } catch { }
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Horizontal) PaintH(e.Graphics);
            else PaintV(e.Graphics);
        }

        private void PaintV(Graphics g)
        {
            aRect = new Rectangle(0, 0, Width, ARROW);
            bRect = new Rectangle(0, Height - ARROW, Width, ARROW);
            trackRect = new Rectangle(0, ARROW, Width, Math.Max(0, Height - 2 * ARROW));
            FillTrack(g, trackRect, true);

            if (sMax > sView && trackRect.Height > 8)
            {
                double frac = sView / sMax;
                int thumbH = Math.Max(18, (int)(trackRect.Height * frac));
                double maxScroll = Math.Max(1, sMax - sView);
                int travel = trackRect.Height - thumbH;
                int ty = trackRect.Top + (int)((sPos / maxScroll) * travel);
                thumbRect = new Rectangle(0, ty, Width, thumbH);
            }
            else thumbRect = Rectangle.Empty;

            DrawBevel(g, aRect); DrawBevel(g, bRect);
            if (!thumbRect.IsEmpty) DrawBevel(g, thumbRect);

            using (var p = new Pen(cArrow, 1.4f))
            {
                int cx = Width / 2;
                g.DrawLines(p, new[] { new Point(cx - 4, aRect.Top + 11), new Point(cx, aRect.Top + 6), new Point(cx + 4, aRect.Top + 11) });
                g.DrawLines(p, new[] { new Point(cx - 4, bRect.Top + 6), new Point(cx, bRect.Top + 11), new Point(cx + 4, bRect.Top + 6) });
            }
        }

        private void PaintH(Graphics g)
        {
            aRect = new Rectangle(0, 0, ARROW, Height);
            bRect = new Rectangle(Width - ARROW, 0, ARROW, Height);
            trackRect = new Rectangle(ARROW, 0, Math.Max(0, Width - 2 * ARROW), Height);
            FillTrack(g, trackRect, false);

            if (sMax > sView && trackRect.Width > 8)
            {
                double frac = sView / sMax;
                int thumbW = Math.Max(18, (int)(trackRect.Width * frac));
                double maxScroll = Math.Max(1, sMax - sView);
                int travel = trackRect.Width - thumbW;
                int tx = trackRect.Left + (int)((sPos / maxScroll) * travel);
                thumbRect = new Rectangle(tx, 0, thumbW, Height);
            }
            else thumbRect = Rectangle.Empty;

            DrawBevel(g, aRect); DrawBevel(g, bRect);
            if (!thumbRect.IsEmpty) DrawBevel(g, thumbRect);

            using (var p = new Pen(cArrow, 1.4f))
            {
                int cy = Height / 2;
                g.DrawLines(p, new[] { new Point(aRect.Left + 11, cy - 4), new Point(aRect.Left + 6, cy), new Point(aRect.Left + 11, cy + 4) });
                g.DrawLines(p, new[] { new Point(bRect.Left + 6, cy - 4), new Point(bRect.Left + 11, cy), new Point(bRect.Left + 6, cy + 4) });
            }
        }

        private void FillTrack(Graphics g, Rectangle t, bool topShadow)
        {
            using (var tb = new SolidBrush(cTrack))
                g.FillRectangle(tb, t);
            using (var sh1 = new Pen(cTrackSh1))
            using (var sh2 = new Pen(cTrackSh2))
            {
                if (topShadow)
                {
                    g.DrawLine(sh1, t.Left, t.Top, t.Right - 1, t.Top);
                    g.DrawLine(sh2, t.Left, t.Top + 1, t.Right - 1, t.Top + 1);
                }
                else
                {
                    g.DrawLine(sh1, t.Left, t.Top, t.Left, t.Bottom - 1);
                    g.DrawLine(sh2, t.Left + 1, t.Top, t.Left + 1, t.Bottom - 1);
                }
            }
        }

        private void DrawBevel(Graphics g, Rectangle r)
        {
            using (var face = new SolidBrush(cFace))
                g.FillRectangle(face, r);
            using (var light = new Pen(cHi))
            using (var light2 = new Pen(cHi2))
            using (var dark = new Pen(cLo))
            using (var dark2 = new Pen(cLo2))
            {
                g.DrawLine(light, r.Left, r.Top, r.Right - 1, r.Top);
                g.DrawLine(light, r.Left, r.Top, r.Left, r.Bottom - 1);
                g.DrawLine(light2, r.Left + 1, r.Top + 1, r.Right - 2, r.Top + 1);
                g.DrawLine(light2, r.Left + 1, r.Top + 1, r.Left + 1, r.Bottom - 2);
                g.DrawLine(dark2, r.Left, r.Bottom - 1, r.Right - 1, r.Bottom - 1);
                g.DrawLine(dark2, r.Right - 1, r.Top, r.Right - 1, r.Bottom - 1);
                g.DrawLine(dark, r.Left + 1, r.Bottom - 2, r.Right - 2, r.Bottom - 2);
                g.DrawLine(dark, r.Right - 2, r.Top + 1, r.Right - 2, r.Bottom - 2);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (core == null) return;
            if (aRect.Contains(e.Location)) ScrollTo(sPos - 48);
            else if (bRect.Contains(e.Location)) ScrollTo(sPos + 48);
            else if (!thumbRect.IsEmpty && thumbRect.Contains(e.Location))
            {
                dragging = true;
                dragGrab = Horizontal ? e.X - thumbRect.Left : e.Y - thumbRect.Top;
            }
            else if (trackRect.Contains(e.Location))
            {
                if (Horizontal) ScrollTo(e.X < thumbRect.Left ? sPos - sView : sPos + sView);
                else ScrollTo(e.Y < thumbRect.Top ? sPos - sView : sPos + sView);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!dragging || core == null || thumbRect.IsEmpty) return;
            double maxScroll = Math.Max(1, sMax - sView);
            if (Horizontal)
            {
                int travel = trackRect.Width - thumbRect.Width;
                if (travel <= 0) return;
                int tx = Math.Max(trackRect.Left, Math.Min(trackRect.Right - thumbRect.Width, e.X - dragGrab));
                double frac = (double)(tx - trackRect.Left) / travel;
                ScrollTo(frac * maxScroll);
            }
            else
            {
                int travel = trackRect.Height - thumbRect.Height;
                if (travel <= 0) return;
                int ty = Math.Max(trackRect.Top, Math.Min(trackRect.Bottom - thumbRect.Height, e.Y - dragGrab));
                double frac = (double)(ty - trackRect.Top) / travel;
                ScrollTo(frac * maxScroll);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; Invalidate(); }
    }
}
