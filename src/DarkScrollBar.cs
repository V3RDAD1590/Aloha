using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Aloha
{

    public class DarkScrollBar : Control
    {
        [DllImport("user32.dll")] static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
        [DllImport("user32.dll")] static extern bool GetScrollInfo(IntPtr hwnd, int fnBar, ref SCROLLINFO si);
        [StructLayout(LayoutKind.Sequential)]
        struct SCROLLINFO { public uint cbSize; public uint fMask; public int nMin; public int nMax; public uint nPage; public int nPos; public int nTrackPos; }
        const int SBH = 0, SBV = 1;
        const uint SIF_ALL = 0x17;
        const int WM_HSCROLL = 0x0114, WM_VSCROLL = 0x0115;
        const int SB_THUMBPOSITION = 4, SB_ENDSCROLL = 8;
        const int SB_LINELEFT = 0, SB_LINERIGHT = 1, SB_PAGELEFT = 2, SB_PAGERIGHT = 3;

        const int EM_LINESCROLL = 0x00B6;
        const int EM_GETLINECOUNT = 0x00BA;
        const int EM_GETFIRSTVISIBLELINE = 0x00CE;

        public bool Horizontal { get; private set; }
        public bool NoArrows { get; set; }
        private readonly bool isLight;
        private Color cFace, cTrack, cHi, cHi2, cLo, cLo2, cTrackSh1, cTrackSh2, cArrow;

        private TextBoxBase target;
        private Control scrollable;
        private Timer poll;

        private readonly Timer repeat = new Timer { Interval = 350 };
        private int repeatDelta; private Rectangle repeatRect; private bool repeatTrack;

        private bool TargetH(out SCROLLINFO si)
        {
            si = new SCROLLINFO { cbSize = (uint)Marshal.SizeOf(typeof(SCROLLINFO)), fMask = SIF_ALL };
            if (target == null || !target.IsHandleCreated) return false;
            return GetScrollInfo(target.Handle, SBH, ref si) && si.nMax > si.nMin;
        }

        const int ARROW = 17;
        const int THICK = 17;
        private Rectangle aRect, bRect, trackRect, thumbRect, cornerRect;
        private bool dragging;
        private int dragOffset;
        private int hScroll;
        private int hMax = 1;

        private bool virtualMode;
        private int vTotal = 0, vPage = 1, vValue = 0;
        public event EventHandler ValueChanged;

        public void SetValueModel(int total, int page)
        {
            virtualMode = true;
            vTotal = Math.Max(0, total);
            vPage = Math.Max(1, page);
            vValue = Math.Max(0, Math.Min(vValue, Math.Max(0, vTotal - vPage)));
            Invalidate();
        }

        public int Value
        {
            get { return vValue; }
            set { SetValue(value, false); }
        }

        private void SetValue(int v, bool raise)
        {
            int max = Math.Max(0, vTotal - vPage);
            v = Math.Max(0, Math.Min(v, max));
            if (v == vValue) { if (raise) return; Invalidate(); return; }
            vValue = v;
            if (raise && ValueChanged != null) ValueChanged(this, EventArgs.Empty);
            Invalidate();
        }

        public DarkScrollBar(bool horizontal = false, bool light = false)
        {
            Horizontal = horizontal;
            isLight = light;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            if (horizontal) { Dock = DockStyle.Bottom; Height = THICK; }
            else { Dock = DockStyle.Right; Width = THICK; }

            if (light)
            {
                cFace = Color.FromArgb(0xF0, 0xF0, 0xF0); cTrack = Color.FromArgb(0xF4, 0xF4, 0xF4);
                cHi = Color.White; cHi2 = Color.FromArgb(0xFD, 0xFD, 0xFD);
                cLo = Color.FromArgb(0xC8, 0xC8, 0xC8); cLo2 = Color.FromArgb(0xB0, 0xB0, 0xB0);
                cTrackSh1 = Color.FromArgb(0xDA, 0xDA, 0xDA); cTrackSh2 = Color.FromArgb(0xE6, 0xE6, 0xE6);
                cArrow = Color.FromArgb(0x50, 0x50, 0x50);
            }
            else
            {
                cFace = Color.FromArgb(0xBB, 0xBB, 0xBB); cTrack = Color.FromArgb(0xBB, 0xBB, 0xBB);
                cHi = Color.White; cHi2 = Color.FromArgb(0xDD, 0xDD, 0xDD);
                cLo = Color.FromArgb(0x88, 0x88, 0x88); cLo2 = Color.FromArgb(0x60, 0x60, 0x60);
                cTrackSh1 = Color.FromArgb(0x88, 0x88, 0x88); cTrackSh2 = Color.FromArgb(0xA0, 0xA0, 0xA0);
                cArrow = Color.Black;
            }
            BackColor = cFace;
        }

        public void Attach(TextBoxBase tb)
        {
            target = tb;
            target.TextChanged += (s, e) => Invalidate();
            poll = new Timer { Interval = 120 };
            poll.Tick += (s, e) => Invalidate();
            poll.Start();
        }

        public void AttachScrollable(Control c)
        {
            scrollable = c;
            poll = new Timer { Interval = 120 };
            poll.Tick += (s, e) => Invalidate();
            poll.Start();
        }

        private SCROLLINFO GetSI()
        {
            var si = new SCROLLINFO();
            si.cbSize = (uint)Marshal.SizeOf(typeof(SCROLLINFO));
            si.fMask = SIF_ALL;
            if (scrollable != null && scrollable.IsHandleCreated)
                GetScrollInfo(scrollable.Handle, Horizontal ? SBH : SBV, ref si);
            return si;
        }

        private void ScrollToPos(int pos)
        {
            if (scrollable == null || !scrollable.IsHandleCreated) return;
            var si = GetSI();
            pos = Math.Max(si.nMin, Math.Min(si.nMax, pos));
            int bar = Horizontal ? WM_HSCROLL : WM_VSCROLL;
            SendMessage(scrollable.Handle, bar, (IntPtr)((pos << 16) | SB_THUMBPOSITION), IntPtr.Zero);
            SendMessage(scrollable.Handle, bar, (IntPtr)SB_ENDSCROLL, IntPtr.Zero);
            Invalidate();
        }

        private void ScrollToFraction(float f)
        {
            if (scrollable == null || !scrollable.IsHandleCreated) return;
            f = Math.Max(0f, Math.Min(1f, f));

            var tb = scrollable as TextBoxBase;
            if (tb != null)
            {
                if (Horizontal) return;
                int total = Math.Max(1, SendMessage(tb.Handle, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32());
                int lineH = Math.Max(1, TextRenderer.MeasureText("Wg", tb.Font).Height);
                int vis   = Math.Max(1, tb.ClientSize.Height / lineH);
                int targetLine = (int)(f * Math.Max(0, total - vis));
                int firstLine  = SendMessage(tb.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32();
                int delta = targetLine - firstLine;
                if (delta != 0) SendMessage(tb.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)delta);
                Invalidate();
                return;
            }

            var sc = scrollable as ScrollableControl;
            if (sc != null && sc.AutoScroll)
            {
                var cur = sc.AutoScrollPosition;
                int maxX = Math.Max(0, sc.DisplayRectangle.Width  - sc.ClientSize.Width);
                int maxY = Math.Max(0, sc.DisplayRectangle.Height - sc.ClientSize.Height);
                if (Horizontal) sc.AutoScrollPosition = new Point((int)(f * maxX), -cur.Y);
                else            sc.AutoScrollPosition = new Point(-cur.X, (int)(f * maxY));
                Invalidate();
                return;
            }

            var inf = GetSI();
            int range = Math.Max(1, (inf.nMax - (int)inf.nPage + 1) - inf.nMin);
            ScrollToPos(inf.nMin + (int)(f * range));
        }

        private void StepScrollable(int delta)
        {
            if (scrollable == null || !scrollable.IsHandleCreated) return;
            int dir = Math.Sign(delta);
            bool page = Math.Abs(delta) > 1;
            if (dir == 0) return;

            var tb = scrollable as TextBoxBase;
            if (tb != null)
            {
                if (Horizontal) { SendMessage(tb.Handle, EM_LINESCROLL, (IntPtr)delta, IntPtr.Zero); Invalidate(); return; }
                int lineH = Math.Max(1, TextRenderer.MeasureText("Wg", tb.Font).Height);
                int vis   = Math.Max(1, tb.ClientSize.Height / lineH);
                int step  = page ? Math.Max(1, vis - 1) * dir : dir;
                SendMessage(tb.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)step);
                Invalidate();
                return;
            }

            var sc = scrollable as ScrollableControl;
            if (sc != null && sc.AutoScroll)
            {
                var cur = sc.AutoScrollPosition;
                if (Horizontal)
                {
                    int maxX = Math.Max(0, sc.DisplayRectangle.Width - sc.ClientSize.Width);
                    int stepPx = page ? (int)(sc.ClientSize.Width * 0.9) : 24;
                    int newX = Math.Max(0, Math.Min(maxX, -cur.X + dir * stepPx));
                    sc.AutoScrollPosition = new Point(newX, -cur.Y);
                }
                else
                {
                    int maxY = Math.Max(0, sc.DisplayRectangle.Height - sc.ClientSize.Height);
                    int stepPx = page ? (int)(sc.ClientSize.Height * 0.9) : 24;
                    int newY = Math.Max(0, Math.Min(maxY, -cur.Y + dir * stepPx));
                    sc.AutoScrollPosition = new Point(-cur.X, newY);
                }
                Invalidate();
                return;
            }

            ScrollToPos(GetSI().nPos + delta);
        }

        private int LineCount()
        {
            if (virtualMode) return Math.Max(1, vTotal);
            if (scrollable != null) { var si = GetSI(); return Math.Max(1, si.nMax - si.nMin + 1); }
            return target == null ? 1 : Math.Max(1, SendMessage(target.Handle, EM_GETLINECOUNT, IntPtr.Zero, IntPtr.Zero).ToInt32());
        }
        private int FirstVisible()
        {
            if (virtualMode) return Math.Max(0, Math.Min(vValue, Math.Max(0, vTotal - vPage)));
            if (scrollable != null) { var si = GetSI(); return Math.Max(0, si.nPos - si.nMin); }
            return target == null ? 0 : SendMessage(target.Handle, EM_GETFIRSTVISIBLELINE, IntPtr.Zero, IntPtr.Zero).ToInt32();
        }
        private int VisibleLines()
        {
            if (virtualMode) return Math.Max(1, vPage);
            if (scrollable != null) { var si = GetSI(); return Math.Max(1, (int)si.nPage); }
            if (target == null) return 1;
            int lineH = TextRenderer.MeasureText("Wg", target.Font).Height;
            return Math.Max(1, target.ClientSize.Height / Math.Max(1, lineH));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            if (Horizontal) PaintHorizontal(e.Graphics);
            else PaintVertical(e.Graphics);
        }

        private void PaintVertical(Graphics g)
        {
            int total = LineCount(), vis = VisibleLines(), first = FirstVisible();

            aRect = NoArrows ? Rectangle.Empty : new Rectangle(0, 0, Width, ARROW);
            bRect = NoArrows ? Rectangle.Empty : new Rectangle(0, Height - ARROW, Width, ARROW);
            trackRect = NoArrows ? new Rectangle(0, 0, Width, Height)
                                 : new Rectangle(0, ARROW, Width, Math.Max(0, Height - 2 * ARROW));
            cornerRect = Rectangle.Empty;

            FillTrack(g, trackRect, true);

            if (total > vis && trackRect.Height > 8)
            {
                float frac = (float)vis / total;
                int thumbH = Math.Max(18, (int)(trackRect.Height * frac));
                int maxFirst = Math.Max(1, total - vis);
                int travel = trackRect.Height - thumbH;
                int ty = trackRect.Top + (int)((float)first / maxFirst * travel);
                thumbRect = new Rectangle(0, ty, Width, thumbH);
            }
            else thumbRect = Rectangle.Empty;

            if (!NoArrows) { DrawBevel(g, aRect); DrawBevel(g, bRect); }
            if (!thumbRect.IsEmpty) DrawBevel(g, thumbRect);

            if (!NoArrows)
            using (var p = new Pen(cArrow, 1.4f))
            {
                int cx = Width / 2;
                g.DrawLines(p, new[] { new Point(cx - 4, aRect.Top + 11), new Point(cx, aRect.Top + 6), new Point(cx + 4, aRect.Top + 11) });
                g.DrawLines(p, new[] { new Point(cx - 4, bRect.Top + 6), new Point(cx, bRect.Top + 11), new Point(cx + 4, bRect.Top + 6) });
            }
        }

        private void PaintHorizontal(Graphics g)
        {
            aRect = NoArrows ? Rectangle.Empty : new Rectangle(0, 0, ARROW, Height);
            bRect = NoArrows ? Rectangle.Empty : new Rectangle(Width - ARROW, 0, ARROW, Height);
            trackRect = NoArrows ? new Rectangle(0, 0, Width, Height)
                                 : new Rectangle(ARROW, 0, Math.Max(0, Width - 2 * ARROW), Height);
            cornerRect = Rectangle.Empty;

            FillTrack(g, trackRect, false);

            if (trackRect.Width > 8)
            {
                if (scrollable != null)
                {

                    var si = GetSI();
                    int total = Math.Max(1, si.nMax - si.nMin + 1);
                    int vis   = Math.Max(1, (int)si.nPage);
                    int first = Math.Max(0, si.nPos - si.nMin);
                    if (total > vis)
                    {
                        float frac = (float)vis / total;
                        int thumbW = Math.Max(24, (int)(trackRect.Width * frac));
                        int travel = trackRect.Width - thumbW;
                        int maxFirst = Math.Max(1, total - vis);
                        int tx = trackRect.Left + (int)((float)first / maxFirst * travel);
                        thumbRect = new Rectangle(tx, 0, thumbW, Height);
                    }
                    else thumbRect = Rectangle.Empty;
                }
                else if (target != null && TargetH(out var tsi))
                {
                    int total = Math.Max(1, tsi.nMax - tsi.nMin + 1);
                    int vis   = Math.Max(1, (int)tsi.nPage);
                    int first = Math.Max(0, tsi.nPos - tsi.nMin);
                    if (total > vis)
                    {
                        int thumbW = Math.Max(24, (int)(trackRect.Width * (float)vis / total));
                        int travel = trackRect.Width - thumbW;
                        int tx = trackRect.Left + (int)((float)first / Math.Max(1, total - vis) * travel);
                        thumbRect = new Rectangle(tx, 0, thumbW, Height);
                    }
                    else thumbRect = Rectangle.Empty;
                }
                else
                {
                    int thumbW = Math.Max(24, trackRect.Width / 3);
                    int travel = trackRect.Width - thumbW;
                    int tx = trackRect.Left + (int)((float)hScroll / Math.Max(1, hMax) * travel);
                    thumbRect = new Rectangle(tx, 0, thumbW, Height);
                }
            }
            else thumbRect = Rectangle.Empty;

            if (!NoArrows) { DrawBevel(g, aRect); DrawBevel(g, bRect); }
            if (!thumbRect.IsEmpty) DrawBevel(g, thumbRect);

            if (!NoArrows)
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
            if (target == null && scrollable == null && !virtualMode) return;
            if (e.Button != MouseButtons.Left) return;
            if (aRect.Contains(e.Location)) { Step(-1); StartRepeat(-1, aRect, false); }
            else if (bRect.Contains(e.Location)) { Step(1); StartRepeat(1, bRect, false); }
            else if (!thumbRect.IsEmpty && thumbRect.Contains(e.Location))
            {
                dragging = true;
                dragOffset = Horizontal ? e.X - thumbRect.Left : e.Y - thumbRect.Top;
            }
            else if (trackRect.Contains(e.Location))
            {
                int d;
                if (Horizontal) d = e.X < thumbRect.Left ? -8 : 8;
                else { int vis = VisibleLines(); d = e.Y < thumbRect.Top ? -vis : vis; }
                Step(d); StartRepeat(d, trackRect, true);
            }
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            if (!dragging || (target == null && scrollable == null && !virtualMode)) return;
            if (Horizontal)
            {
                int travel = trackRect.Width - thumbRect.Width;
                if (travel <= 0) return;
                int tx = Math.Max(trackRect.Left, Math.Min(trackRect.Right - thumbRect.Width, e.X - dragOffset));
                float frac = (float)(tx - trackRect.Left) / travel;
                if (scrollable != null) { ScrollToFraction(frac); Invalidate(); return; }
                if (target != null && TargetH(out var tsi))
                {
                    int pos = tsi.nMin + (int)(frac * Math.Max(0, tsi.nMax - tsi.nMin + 1 - (int)tsi.nPage));
                    SendMessage(target.Handle, WM_HSCROLL, (IntPtr)((pos << 16) | SB_THUMBPOSITION), IntPtr.Zero);
                    SendMessage(target.Handle, WM_HSCROLL, (IntPtr)SB_ENDSCROLL, IntPtr.Zero);
                }
                else
                {
                    int targetCol = (int)(frac * hMax);
                    int delta = targetCol - hScroll;
                    if (delta != 0 && target != null) { SendMessage(target.Handle, EM_LINESCROLL, (IntPtr)delta, IntPtr.Zero); hScroll = targetCol; }
                }
            }
            else
            {
                int total = LineCount(), vis = VisibleLines();
                int travel = trackRect.Height - thumbRect.Height;
                if (travel <= 0) return;
                int ty = Math.Max(trackRect.Top, Math.Min(trackRect.Bottom - thumbRect.Height, e.Y - dragOffset));
                float frac = (float)(ty - trackRect.Top) / travel;
                int targetFirst = (int)(frac * Math.Max(1, total - vis));
                if (virtualMode) SetValue(targetFirst, true);
                else if (scrollable != null) ScrollToFraction(frac);
                else { int delta = targetFirst - FirstVisible(); if (delta != 0) SendMessage(target.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)delta); }
            }
            Invalidate();
        }

        protected override void OnMouseUp(MouseEventArgs e) { dragging = false; repeat.Stop(); Invalidate(); }

        private void StartRepeat(int delta, Rectangle r, bool track)
        {
            repeatDelta = delta; repeatRect = r; repeatTrack = track;
            if (!repeatWired)
            {
                repeatWired = true;
                repeat.Tick += (s, e) =>
                {
                    var p = PointToClient(Cursor.Position);
                    if ((Control.MouseButtons & MouseButtons.Left) == 0 || IsDisposed) { repeat.Stop(); return; }
                    if (!repeatRect.Contains(p)) return;

                    if (repeatTrack && !thumbRect.IsEmpty && thumbRect.Contains(p)) return;
                    if (repeatTrack && !thumbRect.IsEmpty)
                        repeatDelta = Horizontal ? (p.X < thumbRect.Left ? -Math.Abs(repeatDelta) : Math.Abs(repeatDelta))
                                                 : (p.Y < thumbRect.Top ? -Math.Abs(repeatDelta) : Math.Abs(repeatDelta));
                    Step(repeatDelta);
                    repeat.Interval = 50;
                };
            }
            repeat.Stop(); repeat.Interval = 350; repeat.Start();
        }
        private bool repeatWired;

        protected override void Dispose(bool disposing)
        {
            if (disposing) { try { repeat.Dispose(); poll?.Dispose(); } catch { } }
            base.Dispose(disposing);
        }

        private void Step(int delta)
        {
            if (virtualMode) { SetValue(vValue + delta, true); return; }
            if (scrollable != null) { StepScrollable(delta); return; }
            if (target == null) return;
            if (Horizontal)
            {

                if (target is RichTextBox)
                {
                    int code = Math.Abs(delta) > 1 ? (delta < 0 ? SB_PAGELEFT : SB_PAGERIGHT) : (delta < 0 ? SB_LINELEFT : SB_LINERIGHT);
                    int n = Math.Abs(delta) > 1 ? 1 : 3;
                    for (int i = 0; i < n; i++) SendMessage(target.Handle, WM_HSCROLL, (IntPtr)code, IntPtr.Zero);
                    SendMessage(target.Handle, WM_HSCROLL, (IntPtr)SB_ENDSCROLL, IntPtr.Zero);
                }
                else SendMessage(target.Handle, EM_LINESCROLL, (IntPtr)delta, IntPtr.Zero);
                hScroll = Math.Max(0, hScroll + delta);
                hMax = Math.Max(hMax, hScroll + 1);
            }
            else SendMessage(target.Handle, EM_LINESCROLL, IntPtr.Zero, (IntPtr)delta);
            Invalidate();
        }
    }
}
