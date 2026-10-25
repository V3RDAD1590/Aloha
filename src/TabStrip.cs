using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using System.Drawing.Drawing2D;
using System.Windows.Forms.VisualStyles;

namespace Aloha
{

    public class TabStrip : Panel
    {
        public class TabItem
        {
            public string Title;
            public object View;
            public System.Drawing.Image Icon;

            public double T;
            public TabItem(string t, object v) { Title = t; View = v; }
        }

        private readonly List<TabItem> tabs = new List<TabItem>();
        private readonly List<Rectangle> tabRects = new List<Rectangle>();
        private readonly List<Rectangle> closeRects = new List<Rectangle>();
        private readonly List<int> visibleFlat = new List<int>();
        private int active = -1;
        private int hot = -1;
        private bool hotClose = false;
        private Font boldFont;

        private int currentStrip = 0;
        private int slotWidth = 600;
        private Rectangle dropRect;
        private bool hotDrop = false;

        private int extraRows = 0;

        private int dragFlat = -1;
        private Point dragStart;
        private bool dragging = false;

        public event Action<int> ActivateRequested;
        public event Action<int> CloseRequested;

        private const int TabW   = 200;
        private const int Gap    = 3;
        private const int Pad    = 8;
        private const int CloseW = 16;
        private const int DropW  = 12;
        private const int IcoSz  = 16;

        public TabStrip()
        {
            DoubleBuffered = true;

            SetStyle(ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            SetStyle(ControlStyles.ResizeRedraw, true);
            boldFont = new Font(Font, FontStyle.Bold);
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (boldFont != null) boldFont.Dispose();
            boldFont = new Font(Font, FontStyle.Bold);
            Invalidate();
        }

        private int PerStrip
        {
            get
            {
                int noArrow = Math.Max(1, (slotWidth + Gap) / (TabW + Gap));
                if (!NeedsPagingRaw(noArrow)) return noArrow;
                return Math.Max(1, (slotWidth - DropW + Gap) / (TabW + Gap));
            }
        }

        private static int RowOf(TabItem t) { return (int)Math.Floor(t.T); }

        private int HighestOccupiedRow
        {
            get
            {
                int hi = 0;
                for (int i = 0; i < tabs.Count; i++) { int r = RowOf(tabs[i]); if (r > hi) hi = r; }
                return hi;
            }
        }

        private int StripCount
        {
            get { return Math.Max(1, HighestOccupiedRow + 1 + extraRows); }
        }

        private bool NeedsPagingRaw(int perGuess)
        {
            if (extraRows > 0) return true;
            if (tabs.Count == 0) return false;
            int hi = 0;
            for (int i = 0; i < tabs.Count; i++) { int r = RowOf(tabs[i]); if (r > hi) hi = r; }
            return hi > 0 || tabs.Count > perGuess;
        }

        private bool NeedsPaging { get { return StripCount > 1; } }

        private List<int> TabsOnRow(int row)
        {
            var outp = new List<int>();
            for (int i = 0; i < tabs.Count; i++) if (RowOf(tabs[i]) == row) outp.Add(i);
            outp.Sort((a, b) => tabs[a].T.CompareTo(tabs[b].T));
            return outp;
        }

        private void Reflow()
        {
            int maxStrip = StripCount - 1;
            if (currentStrip > maxStrip) currentStrip = maxStrip;
            if (currentStrip < 0) currentStrip = 0;
        }

        private double NextSlotOnRow(int row)
        {
            double frac = 0.0;
            var on = TabsOnRow(row);
            if (on.Count > 0)
            {
                double last = tabs[on[on.Count - 1]].T - row;
                frac = last + 0.001;
            }
            return row + frac;
        }

        public int Count { get { return tabs.Count; } }
        public int ActiveIndex { get { return active; } }
        public object ActiveView { get { return (active >= 0 && active < tabs.Count) ? tabs[active].View : null; } }
        public object ViewAt(int i) { return (i >= 0 && i < tabs.Count) ? tabs[i].View : null; }

        public int SlotWidth
        {
            get { return slotWidth; }
            set
            {
                int v = Math.Max(TabW, value);
                if (v == slotWidth) return;
                slotWidth = v;
                Reflow();
                ResizeToSlot();
                Invalidate();
            }
        }

        public int AddTab(string title, object view)
        {
            var item = new TabItem(string.IsNullOrEmpty(title) ? "tab" : title, view);

            int per = PerStrip;
            int targetRow = HighestOccupiedRow;
            if (TabsOnRow(targetRow).Count >= per) targetRow += 1;
            item.T = NextSlotOnRow(targetRow);

            tabs.Add(item);
            if (active < 0) active = 0;
            Reflow();
            currentStrip = RowOf(item);
            ResizeToSlot();
            Invalidate();
            return tabs.Count - 1;
        }

        public void RemoveTab(int i)
        {
            if (i < 0 || i >= tabs.Count) return;
            var gone = tabs[i].Icon;
            tabs.RemoveAt(i);
            if (gone != null) { try { gone.Dispose(); } catch { } }
            if (tabs.Count == 0) active = -1;
            else if (active >= tabs.Count) active = tabs.Count - 1;
            Reflow();
            if (active >= 0) currentStrip = RowOf(tabs[active]);
            ResizeToSlot();
            Invalidate();
        }

        private void ResizeToSlot()
        {
            int w = NeedsPaging ? slotWidth : Math.Max(1, tabs.Count * (TabW + Gap));
            if (Width != w) Width = w;
        }

        public void SetActive(int i)
        {
            if (i < 0 || i >= tabs.Count) return;
            active = i;
            currentStrip = RowOf(tabs[i]);
            Invalidate();
        }

        public void SetTitle(int i, string title)
        {
            if (i < 0 || i >= tabs.Count) return;
            tabs[i].Title = string.IsNullOrEmpty(title) ? "tab" : title;
            Invalidate();
        }

        public void SetIcon(int i, System.Drawing.Image icon)
        {
            if (i < 0 || i >= tabs.Count) return;

            System.Drawing.Image owned = null;
            if (icon != null) { try { owned = new System.Drawing.Bitmap(icon); } catch { owned = null; } }

            var prev = tabs[i].Icon;
            if (ReferenceEquals(prev, owned)) return;
            tabs[i].Icon = owned;
            if (prev != null) { try { prev.Dispose(); } catch { } }
            Invalidate();
        }

        public void SetView(int i, object view)
        {
            if (i < 0 || i >= tabs.Count) return;
            tabs[i].View = view;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            tabRects.Clear();
            closeRects.Clear();
            visibleFlat.Clear();
            dropRect = Rectangle.Empty;
            Reflow();

            var onRow = TabsOnRow(currentStrip);
            int x = 0;
            int h = Height;

            foreach (int i in onRow)
            {
                if (dragging && i == dragFlat) { x += TabW + Gap; continue; }
                Rectangle r = new Rectangle(x, 0, TabW, h);
                tabRects.Add(r);
                visibleFlat.Add(i);
                bool act = (i == active);
                bool hov = (i == hot);

                PushButtonState st = hov ? PushButtonState.Hot : PushButtonState.Normal;
                ButtonRenderer.DrawButton(g, new Rectangle(r.X, r.Y, r.Width, r.Height + 4), st);

                Rectangle cr = new Rectangle(r.Right - CloseW - 4, r.Top + (h - CloseW) / 2, CloseW, CloseW);
                closeRects.Add(cr);

                var ic = tabs[i].Icon;
                int iy = r.Top + (h - IcoSz) / 2;
                int ix = r.Left + Pad;
                if (ic != null)
                {

                    var oldI = g.InterpolationMode; var oldP = g.PixelOffsetMode;
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode   = PixelOffsetMode.HighQuality;
                    try { g.DrawImage(ic, new Rectangle(ix, iy, IcoSz, IcoSz)); } catch { }
                    g.InterpolationMode = oldI; g.PixelOffsetMode = oldP;
                }

                int iconW = IcoSz + 6;

                Rectangle tr = new Rectangle(r.Left + Pad + iconW, r.Top, r.Width - Pad - iconW - CloseW - 6, r.Height);
                TextRenderer.DrawText(g, tabs[i].Title, act ? boldFont : Font, tr,
                    SystemColors.ControlText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter |
                    TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

                TextRenderer.DrawText(g, "\u00d7", (i == hot && hotClose) ? boldFont : Font, cr,
                    SystemColors.ControlText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                x += TabW + Gap;
            }

            if (NeedsPaging)
            {
                dropRect = new Rectangle(slotWidth - DropW, 0, DropW, h);
                ComboBoxState cs = hotDrop ? ComboBoxState.Hot : ComboBoxState.Normal;
                try { ComboBoxRenderer.DrawDropDownButton(g, dropRect, cs); }
                catch
                {
                    ButtonRenderer.DrawButton(g, new Rectangle(dropRect.X, dropRect.Y, dropRect.Width, dropRect.Height + 4),
                        hotDrop ? PushButtonState.Hot : PushButtonState.Normal);
                    TextRenderer.DrawText(g, "\u25be", Font, dropRect, SystemColors.ControlText,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }
        }

        private void HitTest(Point pt, out int flatTab, out bool close)
        {
            flatTab = -1;
            close = false;
            for (int k = 0; k < tabRects.Count; k++)
            {
                if (tabRects[k].Contains(pt))
                {
                    flatTab = visibleFlat[k];
                    if (k < closeRects.Count && closeRects[k].Contains(pt)) close = true;
                    return;
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            if (NeedsPaging && dropRect.Contains(e.Location)) { OpenStripPicker(); return; }

            int tab; bool close;
            HitTest(e.Location, out tab, out close);
            if (tab < 0) return;
            if (close) { Action<int> h2 = CloseRequested; if (h2 != null) h2(tab); }
            else
            {

                dragFlat = tab;
                dragStart = e.Location;
                Action<int> h2 = ActivateRequested; if (h2 != null) h2(tab);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);

            if (dragFlat >= 0 && !dragging && (e.Button & MouseButtons.Left) != 0)
            {
                if (Math.Abs(e.X - dragStart.X) > 4 || Math.Abs(e.Y - dragStart.Y) > 4)
                    dragging = true;
            }

            if (dragging && dragFlat >= 0)
            {

                double frac = Math.Max(0.0, Math.Min(0.999, (double)e.X / Math.Max(1, slotWidth - DropW)));
                int row = currentStrip;
                if (NeedsPaging && dropRect.Contains(e.Location))
                    row = Math.Min(StripCount - 1, currentStrip + 1);
                tabs[dragFlat].T = row + frac;
                Invalidate();
                return;
            }

            bool overDrop = NeedsPaging && dropRect.Contains(e.Location);
            int tab; bool close;
            HitTest(e.Location, out tab, out close);
            if (tab != hot || close != hotClose || overDrop != hotDrop)
            {
                hot = tab; hotClose = close; hotDrop = overDrop; Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (dragging && dragFlat >= 0)
            {

                currentStrip = RowOf(tabs[dragFlat]);
                NormalizeRow(currentStrip);
                Reflow();
                Invalidate();
            }
            dragging = false;
            dragFlat = -1;
        }

        private void NormalizeRow(int row)
        {
            var on = TabsOnRow(row);
            for (int k = 0; k < on.Count; k++)
                tabs[on[k]].T = row + k * 0.01;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hot != -1 || hotClose || hotDrop) { hot = -1; hotClose = false; hotDrop = false; Invalidate(); }
        }

        private void OpenStripPicker()
        {
            var rows = new List<StripPicker.Row>();
            for (int s = 0; s < StripCount; s++)
                rows.Add(new StripPicker.Row { Index = s, HasTabs = TabsOnRow(s).Count > 0 });

            using (var pick = new StripPicker(rows, currentStrip, Font))
            {
                Point below = PointToScreen(new Point(dropRect.Left, dropRect.Bottom));
                pick.StartPosition = FormStartPosition.Manual;
                pick.Location = below;
                if (pick.ShowDialog(FindForm()) == DialogResult.OK)
                {
                    if (pick.AddRow)
                    {

                        extraRows++;
                        currentStrip = StripCount - 1;
                    }
                    else if (pick.Chosen >= 0)
                    {
                        currentStrip = Math.Max(0, Math.Min(StripCount - 1, pick.Chosen));
                    }
                    ResizeToSlot();
                    Invalidate();
                }
            }
        }

        private class StripPicker : Form
        {
            public class Row { public int Index; public bool HasTabs; }

            private readonly List<Row> rows;
            private int sel;
            public int Chosen = -1;
            public bool AddRow = false;

            private const int RowH = 20;
            private const int Wd   = 120;

            private int Total { get { return rows.Count + 1; } }
            private bool IsAddRow(int i) { return i == rows.Count; }

            public StripPicker(List<Row> r, int current, Font f)
            {
                rows = r;
                sel = Math.Max(0, Math.Min(rows.Count - 1, current));
                FormBorderStyle = FormBorderStyle.None;
                ShowInTaskbar = false;
                StartPosition = FormStartPosition.Manual;
                BackColor = SystemColors.Window;
                Font = f;
                DoubleBuffered = true;
                Width = Wd;
                Height = Math.Max(RowH, Total * RowH) + 2;
                KeyPreview = true;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.Clear(SystemColors.Window);
                using (var border = new Pen(SystemColors.ActiveBorder))
                    g.DrawRectangle(border, 0, 0, Width - 1, Height - 1);

                for (int i = 0; i < Total; i++)
                {
                    var rr = new Rectangle(1, 1 + i * RowH, Width - 2, RowH);
                    if (i == sel)
                        using (var b = new SolidBrush(SystemColors.Highlight)) g.FillRectangle(b, rr);

                    Color fg = (i == sel) ? SystemColors.HighlightText : SystemColors.WindowText;

                    if (IsAddRow(i))
                    {
                        var addR = new Rectangle(rr.Left + 4, rr.Top, rr.Width - 6, rr.Height);
                        TextRenderer.DrawText(g, "+ new row", Font, addR, fg,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                        continue;
                    }

                    var hashR = new Rectangle(rr.Left + 4, rr.Top, 14, rr.Height);
                    if (rows[i].HasTabs)
                        TextRenderer.DrawText(g, "#", Font, hashR, fg,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter);

                    var labelR = new Rectangle(rr.Left + 20, rr.Top, rr.Width - 22, rr.Height);
                    TextRenderer.DrawText(g, "row" + rows[i].Index, Font, labelR, fg,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                int i = (e.Y - 1) / RowH;
                if (i >= 0 && i < Total && i != sel) { sel = i; Invalidate(); }
                base.OnMouseMove(e);
            }

            protected override void OnMouseClick(MouseEventArgs e)
            {
                int i = (e.Y - 1) / RowH;
                if (i < 0 || i >= Total) { base.OnMouseClick(e); return; }
                if (IsAddRow(i)) { AddRow = true; DialogResult = DialogResult.OK; }
                else { Chosen = rows[i].Index; DialogResult = DialogResult.OK; }
                base.OnMouseClick(e);
            }

            protected override void OnKeyDown(KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Down) { sel = Math.Min(Total - 1, sel + 1); Invalidate(); e.Handled = true; }
                else if (e.KeyCode == Keys.Up) { sel = Math.Max(0, sel - 1); Invalidate(); e.Handled = true; }
                else if (e.KeyCode == Keys.Enter)
                {
                    if (IsAddRow(sel)) { AddRow = true; }
                    else { Chosen = rows[sel].Index; }
                    DialogResult = DialogResult.OK;
                }
                else if (e.KeyCode == Keys.Escape) { DialogResult = DialogResult.Cancel; }
                base.OnKeyDown(e);
            }

            protected override void OnDeactivate(EventArgs e)
            {
                base.OnDeactivate(e);
                if (DialogResult != DialogResult.OK) DialogResult = DialogResult.Cancel;
            }
        }
    }
}
