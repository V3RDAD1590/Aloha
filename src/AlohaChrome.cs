using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Aloha
{

    public class AlohaChrome : Panel
    {
        public class TabItem
        {
            public string Title;
            public object View;
            public Image Icon;
            public TabItem(string t, object v) { Title = t; View = v; }
        }

        private readonly List<TabItem> tabs = new List<TabItem>();
        private int active = -1;
        private int hotIdle = -1;
        private bool hotNew = false;

        private static readonly Color White    = Color.White;
        private static readonly Color BandTop   = Color.FromArgb(0xFC, 0xFC, 0xFD);
        private static readonly Color BandBot   = Color.FromArgb(0xDE, 0xDF, 0xE3);
        private static readonly Color Island    = Color.Black;
        private static readonly Color Lime      = Color.FromArgb(0xB4, 0xDC, 0xBA);
        private static readonly Color LimeDim   = Color.FromArgb(0x1C, 0xA8, 0x1C);
        private static readonly Color TabIdle   = Color.FromArgb(0xDD, 0xE0, 0xE4);
        private static readonly Color TabIdleHot= Color.FromArgb(0xE9, 0xEB, 0xEE);
        private static readonly Color TabEdge   = Color.FromArgb(0xC4, 0xC7, 0xCC);
        private static readonly Color TxtIdle   = Color.FromArgb(0x50, 0x50, 0x50);
        private static readonly Color CloseIdle = Color.FromArgb(0x80, 0x80, 0x84);
        private static readonly Color NubFill   = Color.FromArgb(0xEC, 0xEE, 0xF1);
        private static readonly Color NubEdge   = Color.FromArgb(0xA8, 0xAB, 0xB0);

        private const int CHROME_H     = 50;
        private const int STRIP_BOTTOM = 24;
        private const int TAB_TOP      = 4;
        private const int OMNI_BOT     = 48;
        private const int SLANT        = 11;
        private const int ACTIVE_W     = 172;
        private const int IDLE_W       = 160;
        private const int IDLE_OVERLAP = 11;
        private const int NAV_W = 30, NAV_H = 24, NAV_GAP = 4, PAD = 8;
        private const int NUB_GAP = 6;
        private const int CLOSE_HIT = 16;

        private readonly Font tabFont = new Font("Consolas", 8.5f);
        private TextBox omni;
        private FacetButton bBack, bFwd, bReload, bHome, bStar, bMenu;

        private readonly List<int> idleIndex = new List<int>();
        private readonly List<Rectangle> idleRects = new List<Rectangle>();
        private readonly List<Rectangle> idleClose = new List<Rectangle>();
        private Rectangle activeClose, newCube;
        private int omniLeft, omniRight;

        public event Action<int> ActivateRequested;
        public event Action<int> CloseRequested;
        public event Action NewTabRequested;
        public event Action<string> NavigateRequested;
        public event Action BackRequested, ForwardRequested, ReloadRequested, HomeRequested, MenuRequested;

        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern int SendMessage(IntPtr h, int m, int w, int l);
        [System.Runtime.InteropServices.DllImport("user32.dll")] static extern bool ReleaseCapture();
        private const int WM_NCLBUTTONDOWN = 0xA1, HT_CAPTION = 0x2;

        public AlohaChrome()
        {
            Height = CHROME_H;
            DoubleBuffered = true;
            SetStyle(ControlStyles.ResizeRedraw | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = White;

            omni = new TextBox
            {
                BorderStyle = BorderStyle.None,
                BackColor = Color.Black,
                ForeColor = Lime,
                Font = new Font("Consolas", 9.5f)
            };
            omni.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.SuppressKeyPress = true;
                    NavigateRequested?.Invoke(omni.Text.Trim());
                }
            };
            Controls.Add(omni);

            bBack   = MakeKey("\u2190", Lime,    () => BackRequested?.Invoke());
            bFwd    = MakeKey("\u2192", LimeDim, () => ForwardRequested?.Invoke());
            bReload = MakeKey("\u21bb", Lime,    () => ReloadRequested?.Invoke());
            bHome   = MakeKey("\u2302", Lime,    () => HomeRequested?.Invoke());
            bStar   = MakeKey("\u2606", Lime,    () => { });
            bMenu   = MakeKey("\u2630", Lime,    () => MenuRequested?.Invoke());

            Resize += (s, e) => { Relayout(); Invalidate(); };
            Relayout();
        }

        private FacetButton MakeKey(string glyph, Color edge, Action onClick)
        {
            var b = new FacetButton { Glyph = glyph, EdgeColor = edge, Width = NAV_W, Height = NAV_H };
            b.Click += (s, e) => onClick();
            Controls.Add(b);
            return b;
        }

        public int Count { get { return tabs.Count; } }
        public int ActiveIndex { get { return active; } }
        public object ActiveView { get { return (active >= 0 && active < tabs.Count) ? tabs[active].View : null; } }
        public object ViewAt(int i) { return (i >= 0 && i < tabs.Count) ? tabs[i].View : null; }

        public int AddTab(string title, object view)
        {
            tabs.Add(new TabItem(string.IsNullOrEmpty(title) ? "New Tab" : title, view));
            active = tabs.Count - 1;
            Relayout(); Invalidate();
            return active;
        }

        public void RemoveTab(int i)
        {
            if (i < 0 || i >= tabs.Count) return;
            tabs.RemoveAt(i);
            if (active >= tabs.Count) active = tabs.Count - 1;
            Relayout(); Invalidate();
        }

        public void SetActive(int i)
        {
            if (i < 0 || i >= tabs.Count) return;
            active = i; Relayout(); Invalidate();
        }

        public void SetTitle(int i, string title)
        {
            if (i < 0 || i >= tabs.Count) return;
            tabs[i].Title = title ?? ""; Invalidate();
        }

        public void SetIcon(int i, Image icon)
        {
            if (i < 0 || i >= tabs.Count) return;
            tabs[i].Icon = icon; Invalidate();
        }

        public void SetView(int i, object view)
        {
            if (i < 0 || i >= tabs.Count) return;
            tabs[i].View = view;
        }

        public void SetAddress(string s) { if (omni != null) omni.Text = s ?? ""; }

        private void Relayout()
        {
            int w = ClientSize.Width;
            if (w <= 0) return;

            int navY = CHROME_H - 2 - NAV_H;
            int x = PAD;
            if (bBack != null)   { bBack.Left = x;   bBack.Top = navY; }   x += NAV_W + NAV_GAP;
            if (bFwd != null)    { bFwd.Left = x;    bFwd.Top = navY; }    x += NAV_W + NAV_GAP;
            if (bReload != null) { bReload.Left = x; bReload.Top = navY; } x += NAV_W + NAV_GAP;
            if (bHome != null)   { bHome.Left = x;   bHome.Top = navY; }   x += NAV_W + NAV_GAP;

            int mLeft = w - PAD - NAV_W;
            int sLeft = mLeft - NAV_GAP - NAV_W;
            if (bMenu != null) { bMenu.Left = mLeft; bMenu.Top = navY; }
            if (bStar != null) { bStar.Left = sLeft; bStar.Top = navY; }

            omniLeft = x + 8;
            omniRight = sLeft - 8;
            if (omniRight < omniLeft + 60) omniRight = omniLeft + 60;

            if (omni != null)
            {
                omni.Left = omniLeft + 12;
                omni.Top = STRIP_BOTTOM + (OMNI_BOT - STRIP_BOTTOM - omni.Height) / 2;
                omni.Width = Math.Max(40, omniRight - omniLeft - 20);
            }

            idleIndex.Clear(); idleRects.Clear(); idleClose.Clear();
            int ix = omniLeft + ACTIVE_W - IDLE_OVERLAP;
            int last = omniLeft + ACTIVE_W;
            for (int i = 0; i < tabs.Count; i++)
            {
                if (i == active) continue;
                var bound = new Rectangle(ix, TAB_TOP, IDLE_W, STRIP_BOTTOM - TAB_TOP);
                var close = new Rectangle(ix + IDLE_W - SLANT - CLOSE_HIT - 2, TAB_TOP + 3, CLOSE_HIT, CLOSE_HIT);
                idleIndex.Add(i); idleRects.Add(bound); idleClose.Add(close);
                last = ix + IDLE_W;
                ix += IDLE_W - IDLE_OVERLAP;
            }

            int cx = last + NUB_GAP - 8;
            newCube = new Rectangle(cx, 8, 32, 14);

            activeClose = new Rectangle(omniLeft + ACTIVE_W - SLANT - CLOSE_HIT - 2, TAB_TOP + 3, CLOSE_HIT, CLOSE_HIT);
        }

        private GraphicsPath IslandPath()
        {
            int L = omniLeft, R = omniRight, aR = omniLeft + ACTIVE_W;
            var p = new GraphicsPath();
            p.AddLine(L, OMNI_BOT, L, STRIP_BOTTOM);
            p.AddLine(L, STRIP_BOTTOM, L + SLANT, TAB_TOP);
            p.AddLine(L + SLANT, TAB_TOP, aR - SLANT, TAB_TOP);
            p.AddLine(aR - SLANT, TAB_TOP, aR, STRIP_BOTTOM);
            p.AddLine(aR, STRIP_BOTTOM, R, STRIP_BOTTOM);
            p.AddLine(R, STRIP_BOTTOM, R, OMNI_BOT);
            p.CloseFigure();
            return p;
        }

        private static Point[] IdlePoly(Rectangle r)
        {
            return new[]
            {
                new Point(r.Left, r.Bottom),
                new Point(r.Left + SLANT, r.Top),
                new Point(r.Right - SLANT, r.Top),
                new Point(r.Right, r.Bottom)
            };
        }

        private Point[] CubePoly()
        {
            int cx = newCube.Left, yt = newCube.Top, yb = newCube.Bottom;

            return new[]
            {
                new Point(cx, yt), new Point(cx + 24, yt),
                new Point(cx + 32, yb), new Point(cx + 8, yb)
            };
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            int w = ClientSize.Width;

            using (var wb = new SolidBrush(White))
                g.FillRectangle(wb, 0, 0, w, STRIP_BOTTOM);
            using (var band = new LinearGradientBrush(
                new Rectangle(0, STRIP_BOTTOM, Math.Max(1, w), CHROME_H - STRIP_BOTTOM),
                BandTop, BandBot, LinearGradientMode.Vertical))
                g.FillRectangle(band, 0, STRIP_BOTTOM, w, CHROME_H - STRIP_BOTTOM);

            using (var fill = new SolidBrush(TabIdle))
            using (var hotf = new SolidBrush(TabIdleHot))
            using (var edge = new Pen(TabEdge))
            {
                for (int k = 0; k < idleIndex.Count; k++)
                {
                    var poly = IdlePoly(idleRects[k]);
                    g.FillPolygon(k == hotIdle ? hotf : fill, poly);
                    g.DrawLines(edge, poly);
                    DrawTabContent(g, idleRects[k], tabs[idleIndex[k]], TxtIdle, CloseIdle, idleClose[k]);
                }
            }

            using (var nf = new SolidBrush(NubFill))
            using (var ne = new Pen(NubEdge))
            {
                var cube = CubePoly();
                g.FillPolygon(nf, cube);
                g.DrawPolygon(ne, cube);
            }

            using (var path = IslandPath())
            using (var ink = new SolidBrush(Island))
                g.FillPath(ink, path);

            if (active >= 0 && active < tabs.Count)
            {
                var tr = new Rectangle(omniLeft, TAB_TOP, ACTIVE_W, STRIP_BOTTOM - TAB_TOP);
                DrawTabContent(g, tr, tabs[active], Lime, Lime, activeClose);
            }
        }

        private void DrawTabContent(Graphics g, Rectangle r, TabItem t, Color txt, Color closeCol, Rectangle close)
        {
            int textLeft = r.Left + SLANT + 6;
            if (t.Icon != null)
            {
                var ir = new Rectangle(textLeft, r.Top + (r.Height - 14) / 2, 14, 14);
                g.DrawImage(t.Icon, ir);
                textLeft += 18;
            }
            int textRight = close.Left - 4;
            var tf = new Rectangle(textLeft, r.Top, Math.Max(0, textRight - textLeft), r.Height);
            TextRenderer.DrawText(g, t.Title ?? "", tabFont, tf, txt,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            int cxm = close.Left + close.Width / 2, cym = close.Top + close.Height / 2;
            using (var cp = new Pen(closeCol, 1.2f))
            {
                g.DrawLine(cp, cxm - 3, cym - 3, cxm + 3, cym + 3);
                g.DrawLine(cp, cxm + 3, cym - 3, cxm - 3, cym + 3);
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            if (active >= 0 && activeClose.Contains(e.Location)) { CloseRequested?.Invoke(active); return; }
            for (int k = 0; k < idleIndex.Count; k++)
            {
                if (idleClose[k].Contains(e.Location)) { CloseRequested?.Invoke(idleIndex[k]); return; }
                if (idleRects[k].Contains(e.Location)) { ActivateRequested?.Invoke(idleIndex[k]); return; }
            }
            if (newCube.Contains(e.Location)) { NewTabRequested?.Invoke(); return; }

            var form = FindForm();
            if (form != null) { ReleaseCapture(); SendMessage(form.Handle, WM_NCLBUTTONDOWN, HT_CAPTION, 0); }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int oldHot = hotIdle; bool oldNew = hotNew;
            hotIdle = -1; hotNew = false;
            for (int k = 0; k < idleIndex.Count; k++)
                if (idleRects[k].Contains(e.Location)) hotIdle = k;
            hotNew = newCube.Contains(e.Location);

            bool overClickable = hotIdle >= 0 || hotNew
                || (active >= 0 && activeClose.Contains(e.Location));
            Cursor = overClickable ? Cursors.Hand : Cursors.Default;
            if (oldHot != hotIdle || oldNew != hotNew) Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hotIdle = -1; hotNew = false; Cursor = Cursors.Default; Invalidate();
        }
    }
}
