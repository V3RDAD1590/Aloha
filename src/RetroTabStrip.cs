using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Aloha
{

    public class RetroTabStrip : Panel
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
        private int hot = -1;
        private bool hotClose = false;
        private bool hotNew = false;

        private const int TabTop = 0;
        private const int TabMaxW = 200;
        private const int TabMinW = 90;
        private const int Slant = 0;
        private const int Pad = 11;
        private const int Overlap = 0;
        private const int CloseW = 18;
        private const int NewW = 34;

        private readonly List<Rectangle> tabRects = new List<Rectangle>();
        private readonly List<Rectangle> closeRects = new List<Rectangle>();
        private Rectangle newRect;

        private static readonly Color StripBg   = Color.FromArgb(0xF0, 0xF0, 0xF0);
        private static readonly Color TabActive = Color.FromArgb(0xFC, 0xFC, 0xFD);
        private static readonly Color TabIdle   = Color.FromArgb(0xD8, 0xD8, 0xDC);
        private static readonly Color TabIdleHot = Color.FromArgb(0xE6, 0xE6, 0xEA);
        private static readonly Color TabEdge   = Color.FromArgb(0xC4, 0xC4, 0xC8);
        private static readonly Color TxtActive = Color.FromArgb(0x1A, 0x1A, 0x1A);
        private static readonly Color TxtIdle   = Color.FromArgb(0x50, 0x50, 0x50);

        public event Action<int> ActivateRequested;
        public event Action<int> CloseRequested;
        public event Action NewTabRequested;

        private readonly Font font;
        private readonly Font boldFont;

        public RetroTabStrip()
        {
            SetStyle(ControlStyles.OptimizedDoubleBuffer | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.UserPaint | ControlStyles.ResizeRedraw, true);
            Height = 34;
            BackColor = StripBg;
            font = new Font("Segoe UI", 8.5f);
            boldFont = new Font("Segoe UI", 8.5f, FontStyle.Bold);
        }

        public int Count { get { return tabs.Count; } }
        public int ActiveIndex { get { return active; } }
        public object ActiveView { get { return (active >= 0 && active < tabs.Count) ? tabs[active].View : null; } }
        public object ViewAt(int i) { return (i >= 0 && i < tabs.Count) ? tabs[i].View : null; }

        public int AddTab(string title, object view)
        {
            tabs.Add(new TabItem(string.IsNullOrEmpty(title) ? "New Tab" : title, view));
            active = tabs.Count - 1;
            Invalidate();
            return active;
        }

        public void RemoveTab(int i)
        {
            if (i < 0 || i >= tabs.Count) return;
            tabs.RemoveAt(i);
            if (active >= tabs.Count) active = tabs.Count - 1;
            Invalidate();
        }

        public void SetActive(int i)
        {
            if (i < 0 || i >= tabs.Count) return;
            active = i; Invalidate();
        }

        public void SetTitle(int i, string title)
        {
            if (i < 0 || i >= tabs.Count) return;
            tabs[i].Title = string.IsNullOrEmpty(title) ? "New Tab" : title;
            Invalidate();
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

        private int TabWidth()
        {
            if (tabs.Count == 0) return TabMaxW;
            int avail = Width - NewW - 8;
            int w = (avail + (tabs.Count - 1) * Overlap) / Math.Max(1, tabs.Count);
            return Math.Max(TabMinW, Math.Min(TabMaxW, w));
        }

        private static GraphicsPath TabPath(Rectangle r)
        {
            var p = new GraphicsPath();
            int top = r.Top, bot = r.Bottom;

            p.AddLine(r.Left, bot, r.Left + Slant, top);
            p.AddLine(r.Left + Slant, top, r.Right - Slant, top);
            p.AddLine(r.Right - Slant, top, r.Right, bot);
            return p;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(StripBg);
            tabRects.Clear();
            closeRects.Clear();

            int tw = TabWidth();
            int x = 0;
            int top = TabTop;
            int bot = Height;

            for (int pass = 0; pass < 2; pass++)
            {
                int px = 0;
                for (int i = 0; i < tabs.Count; i++)
                {
                    Rectangle r = new Rectangle(px, top, tw, bot - top);
                    if (pass == 0) { tabRects.Add(r); }
                    bool act = (i == active);
                    bool drawNow = (pass == 0) ? !act : act;
                    if (drawNow)
                    {
                        DrawTab(g, r, tabs[i], act, i == hot);

                    }
                    if (pass == 0)
                    {
                        Rectangle cr = new Rectangle(r.Right - Slant - CloseW - 2,
                            r.Top + (r.Height - CloseW) / 2, CloseW, CloseW);
                        closeRects.Add(cr);
                    }
                    px += tw - Overlap;
                }
            }

            int lastRight = 0 + (tabs.Count > 0 ? (tabs.Count * (tw - Overlap) + Overlap) : 0);
            newRect = new Rectangle(lastRight + 2, top + 4, NewW, Height - top - 8);
            DrawNewButton(g, newRect, hotNew);
        }

        private void DrawTab(Graphics g, Rectangle r, TabItem t, bool active, bool hovered)
        {
            using (var path = TabPath(r))
            {
                Color face = active ? TabActive : (hovered ? TabIdleHot : TabIdle);
                using (var b = new SolidBrush(face)) g.FillPath(b, path);
                using (var pen = new Pen(TabEdge, 1f)) g.DrawPath(pen, path);
            }

            int cx = r.Left + Pad;
            int cw = r.Width - 2 * Pad;
            int cy = r.Top;
            int ch = r.Height;

            int textX = cx + 2;
            if (t.Icon != null)
            {
                int sz = 16, iy = cy + (ch - sz) / 2;
                try { g.DrawImage(t.Icon, new Rectangle(cx, iy, sz, sz)); } catch { }
                textX = cx + sz + 4;
            }

            int closeSlot = CloseW + 4;
            Rectangle tr = new Rectangle(textX, cy, Math.Max(0, r.Right - Slant - closeSlot - textX), ch);
            TextRenderer.DrawText(g, t.Title, active ? boldFont : font, tr,
                active ? TxtActive : TxtIdle,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            Rectangle cr = new Rectangle(r.Right - Slant - CloseW - 2,
                cy + (ch - CloseW) / 2, CloseW, CloseW);
            bool hotX = (hovered && hotClose);
            TextRenderer.DrawText(g, "\u00d7", hotX ? boldFont : font, cr,
                active ? TxtActive : TxtIdle,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private void DrawNewButton(Graphics g, Rectangle r, bool hovered)
        {
            if (hovered)
            {
                using (var b = new SolidBrush(Color.FromArgb(0x55, 0x55, 0x55)))
                    g.FillEllipse(b, r.Left, r.Top + r.Height / 2 - 9, 18, 18);
            }
            TextRenderer.DrawText(g, "+", boldFont, r, Color.FromArgb(0xdd, 0xdd, 0xdd),
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }

        private void HitTest(Point pt, out int tab, out bool close, out bool newBtn)
        {
            tab = -1; close = false; newBtn = false;
            if (newRect.Contains(pt)) { newBtn = true; return; }

            if (active >= 0 && active < tabRects.Count && InTab(tabRects[active], pt))
            {
                tab = active;
                if (active < closeRects.Count && closeRects[active].Contains(pt)) close = true;
                return;
            }
            for (int i = tabRects.Count - 1; i >= 0; i--)
            {
                if (InTab(tabRects[i], pt))
                {
                    tab = i;
                    if (i < closeRects.Count && closeRects[i].Contains(pt)) close = true;
                    return;
                }
            }
        }

        private static bool InTab(Rectangle r, Point pt)
        {
            if (pt.Y < r.Top || pt.Y > r.Bottom) return false;

            double f = (double)(pt.Y - r.Top) / Math.Max(1, r.Height);
            double left = r.Left + Slant * (1 - f);
            double right = r.Right - Slant * (1 - f);
            return pt.X >= left && pt.X <= right;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int tab; bool close; bool nb;
            HitTest(e.Location, out tab, out close, out nb);
            if (nb) { NewTabRequested?.Invoke(); return; }
            if (tab < 0) return;
            if (close) CloseRequested?.Invoke(tab);
            else ActivateRequested?.Invoke(tab);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int tab; bool close; bool nb;
            HitTest(e.Location, out tab, out close, out nb);
            if (tab != hot || close != hotClose || nb != hotNew)
            { hot = tab; hotClose = close; hotNew = nb; Invalidate(); }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hot != -1 || hotClose || hotNew) { hot = -1; hotClose = false; hotNew = false; Invalidate(); }
        }
    }
}
