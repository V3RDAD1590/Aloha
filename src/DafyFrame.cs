using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace Aloha
{

    public class DafyFrame : Form
    {
        [DllImport("user32.dll")] static extern int SendMessage(IntPtr h, int m, int w, int l);
        [DllImport("user32.dll")] static extern bool ReleaseCapture();
        const int WM_NCLBUTTONDOWN = 0xA1, HT_CAPTION = 0x2;

        protected const int TITLE_H = 22;
        private int headerH = TITLE_H;
        private   const int FOOTER_H = 26;
        private static readonly Color cFooter = Color.FromArgb(0xF0, 0xF0, 0xF0);

        public static Form MainWindow;

        public const int FooterGap = 2;

        protected virtual bool FloatsAboveMain { get { return true; } }
        private static readonly Font  sBtnFont = new Font("MS Sans Serif", 8f);

        private string panelTitle;
        private Timer  titleTimer;
        private float  marqueeOffset;
        private bool   titleMarquee;

        protected Panel ClientArea;
        private Panel footer;
        private Panel btnMax, btnClose, btnMin;
        private Panel footerExtra;
        private BevelPanel fIndentWide, fCube1;
        private Label sizeReadout;

        private bool gripDragging, gripDidDrag;
        private Point gripStart;
        private Size  gripStartSize;

        protected Panel Footer { get { return footer; } }

        protected int FooterRightLimit
        {
            get
            {
                if (btnMax != null && btnMax.Left > 0) return btnMax.Left;
                return (footer != null ? footer.ClientSize.Width : 600) - 130;
            }
        }

        protected int FooterContentLeft
        {
            get { return (btnClose != null ? btnClose.Right : FooterGap) + FooterGap; }
        }

        public DafyFrame() : this("", "") { }

        public DafyFrame(string code, string title)
        {
            panelTitle = title;

            FormBorderStyle = FormBorderStyle.None;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = cFooter;
            DoubleBuffered = true;
            Padding = new Padding(0, headerH, 0, 0);

            if (FloatsAboveMain && MainWindow != null && !ReferenceEquals(MainWindow, this))
            {
                try { Owner = MainWindow; } catch { }
            }

            try
            {
                string dir = System.IO.Path.GetDirectoryName(Application.ExecutablePath);
                string ico = System.IO.Path.Combine(dir, "OptIcon.ico");
                if (System.IO.File.Exists(ico))
                    this.Icon = new Icon(ico);
                else
                    try { this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); } catch { }
            }
            catch { }

            ClientArea = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.FromArgb(0xBB, 0xBB, 0xBB)
            };
            Controls.Add(ClientArea);

            SetupFooter();
            MinimumSize = new Size(360, 150);

            MouseDown += OnFrameMouseDown;
            Paint += PaintFrame;
            Resize += (s, e) => { Invalidate(); LayoutFooter(); };
            FormClosed += (s, e) => { titleTimer?.Stop(); titleTimer?.Dispose(); };
        }

        protected void SetHeaderHeight(int h)
        {
            headerH = Math.Max(0, h);
            Padding = new Padding(0, headerH, 0, 0);
            if (IsHandleCreated) { PerformLayout(); Invalidate(); }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x0084;
            const int G = 6;
            if (m.Msg == WM_NCHITTEST && IsHandleCreated && WindowState == FormWindowState.Normal)
            {
                try
                {
                    long lp = m.LParam.ToInt64();
                    var p = PointToClient(new Point((short)(lp & 0xFFFF), (short)((lp >> 16) & 0xFFFF)));
                    bool r = p.X >= ClientSize.Width - G, b = p.Y >= ClientSize.Height - G;
                    bool l = p.X <= G, tp = p.Y <= G;
                    if (r && b) { m.Result = (IntPtr)17; return; }
                    if (l && b) { m.Result = (IntPtr)16; return; }
                    if (r && tp) { m.Result = (IntPtr)14; return; }
                    if (l && tp) { m.Result = (IntPtr)13; return; }
                    if (r) { m.Result = (IntPtr)11; return; }
                    if (l) { m.Result = (IntPtr)10; return; }
                    if (b) { m.Result = (IntPtr)15; return; }
                    if (tp) { m.Result = (IntPtr)12; return; }
                }
                catch {  }
            }
            base.WndProc(ref m);
        }

        private void OnFrameMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            if (headerH > 0 && e.Y <= headerH)
            {
                ReleaseCapture();
                SendMessage(this.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0);
            }
        }

        private void PaintFrame(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            if (headerH <= 0) return;

            using (var brush = new LinearGradientBrush(
                new Rectangle(0, 0, Math.Max(1, Width), Math.Max(1, Height)),
                Color.FromArgb(252, 252, 253),
                Color.FromArgb(216, 216, 220),
                LinearGradientMode.Vertical))
                g.FillRectangle(brush, 0, 0, Width, headerH);

            using (var line = new Pen(Color.FromArgb(0xC8, 0xC8, 0xCC)))
                g.DrawLine(line, 0, headerH - 1, Width, headerH - 1);

            using (var f = new Font("Tahoma", 9f, FontStyle.Bold))
            {
                string t = panelTitle ?? "";
                float pad = 8f, avail = Width - pad * 2;
                SizeF sz = g.MeasureString(t, f);
                if (!titleMarquee || sz.Width <= avail)
                {
                    using (var sf = new StringFormat
                           { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center })
                        g.DrawString(t, f, Brushes.Black, new RectangleF(0, 0, Width, TITLE_H), sf);
                }
                else
                {
                    const float GAP = 60f;
                    float period = sz.Width + GAP;
                    float o = marqueeOffset % period;
                    float y = (TITLE_H - sz.Height) / 2f;
                    var st = g.Save();
                    g.SetClip(new RectangleF(pad, 0, avail, TITLE_H));
                    using (var sf = new StringFormat { FormatFlags = StringFormatFlags.NoWrap })
                    {
                        g.DrawString(t, f, Brushes.Black, pad - o, y, sf);
                        g.DrawString(t, f, Brushes.Black, pad - o + period, y, sf);
                    }
                    g.Restore(st);
                }
            }
        }

        protected void SetTitle(string text)
        {
            panelTitle = text ?? "";
            marqueeOffset = 0f;
            InvalidateHeader();
        }

        protected void EnableTitleMarquee()
        {
            titleMarquee = true;
            if (titleTimer == null)
            {
                titleTimer = new Timer { Interval = 40 };
                titleTimer.Tick += (s, e) => { marqueeOffset += 1.2f; InvalidateHeader(); };
                titleTimer.Start();
            }
        }

        private void InvalidateHeader()
        {
            if (IsHandleCreated) Invalidate(new Rectangle(0, 0, Math.Max(1, Width), TITLE_H));
        }

        private void SetupFooter()
        {
            footer = new Panel { Dock = DockStyle.Bottom, Height = FOOTER_H, BackColor = cFooter };

            btnMin = MakeFooterButton("min", () =>
            {
                try { Owner = null; } catch { }
                WindowState = FormWindowState.Minimized;
            });
            btnMin.Top = 3; btnMin.Left = 4;

            fIndentWide = new BevelPanel { Left = btnMin.Right + 4, Top = 3, Width = 200, Height = 20, BackColor = cFooter };
            fCube1 = new BevelPanel { Width = 20, Height = 20, Top = 3, BackColor = cFooter };

            btnMax = MakeFooterButton("max", () =>
                WindowState = (WindowState == FormWindowState.Maximized)
                    ? FormWindowState.Normal : FormWindowState.Maximized);
            btnClose = MakeFooterButton("close", () => Close());
            btnMax.Top = 3; btnClose.Top = 3;

            footer.Controls.Add(btnMin);
            footer.Controls.Add(fIndentWide);
            footer.Controls.Add(fCube1);

            MakeResizeGrip(fCube1);
            fCube1.Paint += (s, e) =>
            {
                var g = e.Graphics; int cw = fCube1.Width, chh = fCube1.Height;
                using (var hi = new Pen(Color.White)) using (var lo = new Pen(Color.FromArgb(0x80, 0x80, 0x80)))
                    for (int k = 4; k <= 12; k += 4)
                    {
                        g.DrawLine(lo, cw - 3, chh - k - 2, cw - k - 2, chh - 3);
                        g.DrawLine(hi, cw - 3, chh - k - 1, cw - k - 1, chh - 3);
                    }
            };
            footer.Controls.Add(btnMax);
            footer.Controls.Add(btnClose);

            sizeReadout = new Label
            {
                AutoSize = false, Left = 6, Height = 16, Width = 120, Top = 2,

                TextAlign = ContentAlignment.MiddleRight, BackColor = cFooter,
                ForeColor = Color.FromArgb(0x50, 0x50, 0x50),
                Font = new Font("Lucida Console", 7.5f, FontStyle.Bold)
            };
            sizeReadout.Left = btnMin.Right + 8;
            sizeReadout.Top = 5;
            footer.Controls.Add(sizeReadout);
            sizeReadout.BringToFront();

            fIndentWide.SendToBack();
            footer.Resize += (s, e) => LayoutFooter();

            Controls.Add(footer);
            LayoutFooter();
        }

        private void LayoutFooter()
        {
            if (footer == null || btnMax == null) return;
            int w = footer.ClientSize.Width;

            btnClose.Left = FooterGap;

            int right = w - FooterGap;
            if (fCube1 != null)
            {
                fCube1.Visible = WindowState != FormWindowState.Maximized;
                fCube1.Left = w - fCube1.Width - FooterGap;
                fCube1.BringToFront();
                if (fCube1.Visible) right = fCube1.Left - FooterGap;
            }
            btnMin.Left = right - btnMin.Width;
            btnMax.Left = btnMin.Left - btnMax.Width - FooterGap;

            int rightEdge = btnMax.Left;
            if (footerExtra != null && !footerExtra.IsDisposed)
            {
                footerExtra.Left = btnMax.Left - footerExtra.Width - FooterGap;
                rightEdge = footerExtra.Left;
            }

            int wellLeft = FooterContentLeft;
            int wellRight = rightEdge - FooterGap;
            fIndentWide.Left = wellLeft;
            fIndentWide.Width = Math.Max(20, wellRight - wellLeft);
            if (sizeReadout != null && sizeReadout.Visible)
            {
                sizeReadout.Left = wellLeft;
                sizeReadout.Width = Math.Max(20, wellRight - wellLeft - 4);
                sizeReadout.Text = this.ClientSize.Width + "\u00D7" + this.ClientSize.Height + " px";
            }
        }

        protected void HideSizeReadout()
        {
            if (sizeReadout != null) sizeReadout.Visible = false;
        }

        protected void SetFooterExtra(Panel p)
        {
            if (footer == null) return;
            if (footerExtra != null) footer.Controls.Remove(footerExtra);
            footerExtra = p;
            if (p != null) { p.Top = 3; footer.Controls.Add(p); p.BringToFront(); }
            LayoutFooter();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            LayoutFooter();
        }

        protected void LayoutFooterRow(params Control[] items)
        {
            if (footer == null || items == null) return;
            int x = FooterContentLeft;
            int limit = FooterRightLimit - FooterGap;
            foreach (var c in items)
            {
                if (c == null) continue;

                c.Top = Math.Max(0, (footer.ClientSize.Height - c.Height) / 2);
                c.Left = x;
                c.Visible = c.Right <= limit;
                c.BringToFront();
                x = c.Right + FooterGap;
            }
        }

        protected Panel MakeFooterToggle(string label, Func<bool> get, Action<bool> set)
        {
            var b = new Panel { Width = 46, Height = 20, Cursor = Cursors.Hand, BackColor = cFooter };
            bool hover = false;
            b.Paint += (s, e) =>
            {
                var g = e.Graphics;
                var r = b.ClientRectangle;
                bool on = false;
                try { on = get(); } catch { }

                Color fill = on ? Color.FromArgb(0x1E, 0x33, 0x24)
                                : (hover ? Color.FromArgb(0xF7, 0xF7, 0xF7) : cFooter);
                using (var br = new SolidBrush(fill)) g.FillRectangle(br, r);

                int w1 = r.Width - 1, h1 = r.Height - 1;
                Color tl = on ? Color.White : Color.FromArgb(0x80, 0x80, 0x80);
                Color br2 = on ? Color.FromArgb(0x80, 0x80, 0x80) : Color.White;
                using (var p1 = new Pen(tl))  { g.DrawLine(p1, 0, 0, w1, 0); g.DrawLine(p1, 0, 0, 0, h1); }
                using (var p2 = new Pen(br2)) { g.DrawLine(p2, 0, h1, w1, h1); g.DrawLine(p2, w1, 0, w1, h1); }

                TextRenderer.DrawText(g, label, new Font("Lucida Console", 7f, FontStyle.Bold), r,
                    on ? Color.FromArgb(0xB4, 0xDC, 0xBA) : Color.FromArgb(0x60, 0x60, 0x60),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            b.MouseEnter += (s, e) => { hover = true;  b.Invalidate(); };
            b.MouseLeave += (s, e) => { hover = false; b.Invalidate(); };
            b.Click += (s, e) =>
            {
                bool now = false;
                try { now = get(); } catch { }
                try { set(!now); } catch { }
                b.Invalidate();
            };
            return b;
        }

        private Panel MakeFooterButton(string kind, Action onClick)
        {
            var b = new Panel { Width = 44, Height = 20, Cursor = Cursors.Hand, BackColor = cFooter };
            bool hover = false, pressed = false;
            b.Paint += (s, e) =>
            {
                var g = e.Graphics;
                var r = b.ClientRectangle;

                Color fill = pressed ? Color.FromArgb(0xE4, 0xE4, 0xE4)
                                     : (hover ? Color.FromArgb(0xF7, 0xF7, 0xF7) : cFooter);
                using (var br = new SolidBrush(fill)) g.FillRectangle(br, r);
                int w1 = r.Width - 1, h1 = r.Height - 1;
                Color tl  = pressed ? Color.White : Color.FromArgb(0x80, 0x80, 0x80);
                Color brc = pressed ? Color.FromArgb(0x80, 0x80, 0x80) : Color.White;
                using (var pTl = new Pen(tl))  { g.DrawLine(pTl, 0, 0, w1, 0);  g.DrawLine(pTl, 0, 0, 0, h1); }
                using (var pBr = new Pen(brc)) { g.DrawLine(pBr, 0, h1, w1, h1); g.DrawLine(pBr, w1, 0, w1, h1); }
            };
            b.MouseEnter += (s, e) => { hover = true; b.Invalidate(); };
            b.MouseLeave += (s, e) => { hover = false; pressed = false; b.Invalidate(); };
            b.MouseDown  += (s, e) => { if (e.Button == MouseButtons.Left) { pressed = true; b.Invalidate(); } };
            b.MouseUp    += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                bool fire = pressed && b.ClientRectangle.Contains(e.Location);
                pressed = false; b.Invalidate();
                if (fire) onClick();
            };
            return b;
        }

        protected Panel MakeLabeledButton(string text, Action onClick)
        {
            int wText = TextRenderer.MeasureText(text, sBtnFont).Width;
            var b = new Panel { Width = Math.Max(48, wText + 18), Height = 20, Cursor = Cursors.Hand, BackColor = cFooter, Text = text };
            bool hover = false, pressed = false;
            b.Paint += (s, e) =>
            {
                var g = e.Graphics;
                var r = b.ClientRectangle;
                Color fill = pressed ? Color.FromArgb(0xE4, 0xE4, 0xE4)
                                     : (hover ? Color.FromArgb(0xF7, 0xF7, 0xF7) : cFooter);
                using (var br = new SolidBrush(fill)) g.FillRectangle(br, r);
                int w1 = r.Width - 1, h1 = r.Height - 1;
                Color tl  = pressed ? Color.White : Color.FromArgb(0x80, 0x80, 0x80);
                Color brc = pressed ? Color.FromArgb(0x80, 0x80, 0x80) : Color.White;
                using (var pTl = new Pen(tl))  { g.DrawLine(pTl, 0, 0, w1, 0);  g.DrawLine(pTl, 0, 0, 0, h1); }
                using (var pBr = new Pen(brc)) { g.DrawLine(pBr, 0, h1, w1, h1); g.DrawLine(pBr, w1, 0, w1, h1); }
                TextRenderer.DrawText(g, b.Text, sBtnFont, r, Color.Black,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            };
            b.MouseEnter += (s, e) => { hover = true; b.Invalidate(); };
            b.MouseLeave += (s, e) => { hover = false; pressed = false; b.Invalidate(); };
            b.MouseDown  += (s, e) => { if (e.Button == MouseButtons.Left) { pressed = true; b.Invalidate(); } };
            b.MouseUp    += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                bool fire = pressed && b.ClientRectangle.Contains(e.Location);
                pressed = false; b.Invalidate();
                if (fire) onClick();
            };
            return b;
        }

        protected void HideFooterCubes()
        {

            cubesHidden = true;
            LayoutFooter();
        }
        private bool cubesHidden = false;

        private DarkScrollBar aVBar, aHBar;
        private Panel aCanvas, aCorner;
        private const int ALOHA_SB = 17;

        protected Panel SetupAlohaScroll(Color canvasBack)
        {
            HideFooterCubes();
            aCanvas = new Panel { BackColor = canvasBack, AutoScroll = true };
            aVBar   = new DarkScrollBar(false, light: true) { Dock = DockStyle.None };
            aHBar   = new DarkScrollBar(true,  light: true) { Dock = DockStyle.None };
            aCorner = new Panel { BackColor = cFooter };
            ClientArea.Controls.Add(aCanvas);
            ClientArea.Controls.Add(aVBar);
            ClientArea.Controls.Add(aHBar);
            ClientArea.Controls.Add(aCorner);

            aCanvas.SendToBack();
            aVBar.BringToFront();
            aHBar.BringToFront();
            aCorner.BringToFront();
            aVBar.AttachScrollable(aCanvas);
            aHBar.AttachScrollable(aCanvas);
            MakeResizeGrip(aCorner);
            ClientArea.Resize += (s, e) => LayoutAlohaScroll();
            this.Shown        += (s, e) => LayoutAlohaScroll();
            LayoutAlohaScroll();
            return aCanvas;
        }

        protected Size AlohaContent
        {
            get
            {
                int w = ClientArea.ClientSize.Width  - ALOHA_SB;
                int h = ClientArea.ClientSize.Height - ALOHA_SB;
                return new Size(Math.Max(1, w), Math.Max(1, h));
            }
        }

        private int alohaBottomInset;

        protected void SetAlohaBottomInset(int px)
        {
            alohaBottomInset = Math.Max(0, px);
            LayoutAlohaScroll();
        }

        protected Rectangle AlohaBottomStripRect
        {
            get
            {
                int cw = ClientArea.ClientSize.Width, ch = ClientArea.ClientSize.Height;
                return new Rectangle(0, ch - ALOHA_SB - alohaBottomInset,
                                     Math.Max(0, cw - ALOHA_SB), alohaBottomInset);
            }
        }

        private void LayoutAlohaScroll()
        {
            if (aCanvas == null) return;
            int cw = ClientArea.ClientSize.Width, ch = ClientArea.ClientSize.Height;
            if (cw < ALOHA_SB + 1 || ch < ALOHA_SB + 1) return;

            int canvasH = alohaBottomInset > 0 ? ch - ALOHA_SB - alohaBottomInset : ch;
            aCanvas.SetBounds(0, 0, cw, Math.Max(ALOHA_SB, canvasH));
            aVBar.SetBounds(cw - ALOHA_SB, 0, ALOHA_SB, ch - ALOHA_SB);
            aHBar.SetBounds(0, ch - ALOHA_SB, cw - ALOHA_SB, ALOHA_SB);
            aCorner.SetBounds(cw - ALOHA_SB, ch - ALOHA_SB, ALOHA_SB, ALOHA_SB);
        }

        protected virtual bool FooterActsAsResizeGrip { get { return true; } }

        protected void MakeResizeGrip(Control grip)
        {
            grip.Cursor = Cursors.SizeNWSE;
            grip.MouseDown += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                gripDragging = true; gripDidDrag = false;
                gripStart = Cursor.Position; gripStartSize = this.Size;
            };
            grip.MouseMove += (s, e) =>
            {
                if (!gripDragging) return;
                var now = Cursor.Position;
                int dx = now.X - gripStart.X, dy = now.Y - gripStart.Y;
                if (!gripDidDrag && Math.Abs(dx) + Math.Abs(dy) < 4) return;
                gripDidDrag = true;
                if (WindowState == FormWindowState.Maximized) return;
                this.Size = new Size(gripStartSize.Width + dx, gripStartSize.Height + dy);
            };
            grip.MouseUp += (s, e) =>
            {
                if (e.Button != MouseButtons.Left) return;
                gripDragging = false; gripDidDrag = false;
            };
        }

        protected void StyleAlohaCorner(Panel corner, bool wireGrip)
        {
            if (corner == null) return;
            corner.BackColor = cFooter;
            if (wireGrip) MakeResizeGrip(corner);
        }
    }
}
