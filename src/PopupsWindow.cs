using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Aloha
{

    public class PopupEntry
    {
        public DateTime When;
        public string Uri;
        public PopupEntry(DateTime when, string uri) { When = when; Uri = uri; }
    }

    public class PopupsWindow : DafyFrame
    {
        private static readonly Color Green = Color.FromArgb(0xB4, 0xDC, 0xBA);
        private static readonly Color Dim   = Color.FromArgb(0x7F, 0xAE, 0x86);

        private readonly List<PopupEntry> queue;
        private readonly Action<string> onAllow;

        private Panel list;
        private Font headFont, rowFont;
        private int selIdx = -1, scrollTop = 0;
        private Rectangle upRect, downRect, thumbRect;

        private const int ROW_H = 18, HEAD_H = 0, SB_W = 16, ARROW_H = 16;

        public PopupsWindow(List<PopupEntry> sharedQueue, Action<string> allow)
            : base("OPT-POP", "Blocked popups")
        {
            queue   = sharedQueue ?? new List<PopupEntry>();
            onAllow = allow;

            Size = new Size(620, 360);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            KeyPreview = true;
            ClientArea.BackColor = Color.Black;

            headFont = new Font("Lucida Console", 8.5f, FontStyle.Bold);
            rowFont  = new Font("Consolas", 9f);

            list = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black };
            typeof(Panel).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(list, true, null);
            list.Paint += List_Paint;
            list.MouseDown += List_MouseDown;
            list.MouseWheel += (s, e) => ScrollBy(e.Delta > 0 ? -3 : 3);
            ClientArea.Controls.Add(list);

            var bAllow    = MakeLabeledButton("Allow",     () => AllowSel());
            var bDeny     = MakeLabeledButton("Deny",      () => DenySel());
            var bAllowAll = MakeLabeledButton("Allow all", () => AllowAll());
            var bClear    = MakeLabeledButton("Clear",     () => { queue.Clear(); selIdx = -1; scrollTop = 0; list.Invalidate(); });

            Footer.Controls.Add(bAllow);
            Footer.Controls.Add(bDeny);
            Footer.Controls.Add(bAllowAll);
            Footer.Controls.Add(bClear);
            bAllow.BringToFront(); bDeny.BringToFront(); bAllowAll.BringToFront(); bClear.BringToFront();

            void Layout()
            {

                LayoutFooterRow(bAllow, bDeny, bAllowAll, bClear);
            }
            Layout();
            Footer.Resize += (s, e) => Layout();

            this.KeyDown += (s, e) => { if (e.KeyCode == Keys.Escape) { Close(); e.Handled = true; } };
            this.FormClosed += (s, e) => { headFont?.Dispose(); rowFont?.Dispose(); };
        }

        public void RefreshList()
        {
            if (IsDisposed || list == null) return;
            ClampSel();
            list.Invalidate();
        }

        private void ClampSel()
        {
            if (queue.Count == 0) { selIdx = -1; return; }
            if (selIdx < 0)                 selIdx = 0;
            else if (selIdx >= queue.Count) selIdx = queue.Count - 1;
        }

        private int VisibleRows { get { return Math.Max(1, (list.ClientSize.Height - HEAD_H) / ROW_H); } }
        private int MaxScroll   { get { return Math.Max(0, queue.Count - VisibleRows); } }
        private void ScrollBy(int d) { scrollTop = Math.Max(0, Math.Min(MaxScroll, scrollTop + d)); list.Invalidate(); }

        private void AllowSel()
        {
            if (selIdx < 0 || selIdx >= queue.Count) return;
            string uri = queue[selIdx].Uri;
            queue.RemoveAt(selIdx);
            ClampSel();
            list.Invalidate();
            onAllow?.Invoke(uri);
        }

        private void DenySel()
        {
            if (selIdx < 0 || selIdx >= queue.Count) return;
            queue.RemoveAt(selIdx);
            ClampSel();
            list.Invalidate();
        }

        private void AllowAll()
        {
            var all = queue.ToArray();
            queue.Clear(); selIdx = -1; scrollTop = 0; list.Invalidate();
            foreach (var p in all) onAllow?.Invoke(p.Uri);
        }

        private void List_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int w = list.ClientSize.Width, h = list.ClientSize.Height;
            int listW = w - SB_W;
            int cTime = 8, cUri = 86;

            if (queue.Count == 0)
            {
                using (var gb = new SolidBrush(Dim)) g.DrawString("(no popups blocked yet)", rowFont, gb, cTime, HEAD_H + 6);
                DrawScrollbar(g, w, h);
                return;
            }

            var clip = g.Clip;
            g.SetClip(new Rectangle(0, HEAD_H, listW, h - HEAD_H));
            int end = Math.Min(queue.Count, scrollTop + VisibleRows);
            for (int i = scrollTop; i < end; i++)
            {
                var p = queue[i];
                int y = HEAD_H + (i - scrollTop) * ROW_H;
                bool seld = (i == selIdx);
                if (seld) using (var sb = new SolidBrush(Green)) g.FillRectangle(sb, 0, y, listW, ROW_H);
                Brush fg = seld ? Brushes.Black : new SolidBrush(Green);
                g.DrawString(p.When.ToString("HH:mm:ss"), rowFont, fg, cTime, y + 2);
                g.DrawString(Clip(p.Uri, 80), rowFont, fg, cUri, y + 2);
                if (!seld) fg.Dispose();
            }
            g.Clip = clip;
            DrawScrollbar(g, w, h);
        }

        private void DrawScrollbar(Graphics g, int w, int h)
        {
            int x = w - SB_W, top = HEAD_H, bot = h;
            upRect   = new Rectangle(x, top, SB_W, ARROW_H);
            downRect = new Rectangle(x, bot - ARROW_H, SB_W, ARROW_H);
            using (var track = new SolidBrush(Color.FromArgb(0xF4, 0xF4, 0xF4))) g.FillRectangle(track, x, top, SB_W, bot - top);
            int trackH = (bot - ARROW_H) - (top + ARROW_H);
            if (queue.Count > 0 && MaxScroll > 0 && trackH > 8)
            {
                double frac = (double)VisibleRows / queue.Count;
                int thumbH = Math.Max(14, (int)(trackH * frac));
                int range = trackH - thumbH;
                int ty = top + ARROW_H + (int)((double)scrollTop / MaxScroll * range);
                thumbRect = new Rectangle(x + 1, ty, SB_W - 2, thumbH);
                DrawBevelButton(g, thumbRect);
            }
            else thumbRect = Rectangle.Empty;
            DrawBevelButton(g, upRect); DrawBevelButton(g, downRect);
            DrawArrow(g, upRect, true); DrawArrow(g, downRect, false);
        }

        private void DrawBevelButton(Graphics g, Rectangle r)
        {
            using (var f = new SolidBrush(Color.FromArgb(0xF0, 0xF0, 0xF0))) g.FillRectangle(f, r);
            using (var hi = new Pen(Color.White))
            { g.DrawLine(hi, r.Left, r.Top, r.Right - 1, r.Top); g.DrawLine(hi, r.Left, r.Top, r.Left, r.Bottom - 1); }
            using (var sh = new Pen(Color.FromArgb(0xB0, 0xB0, 0xB0)))
            { g.DrawLine(sh, r.Left, r.Bottom - 1, r.Right - 1, r.Bottom - 1); g.DrawLine(sh, r.Right - 1, r.Top, r.Right - 1, r.Bottom - 1); }
        }

        private void DrawArrow(Graphics g, Rectangle r, bool up)
        {
            int cx = r.Left + r.Width / 2, cy = r.Top + r.Height / 2;
            using (var p = new Pen(Color.FromArgb(0x50, 0x50, 0x50), 1.4f))
            {
                if (up)
                    g.DrawLines(p, new[] { new Point(cx - 4, cy + 2), new Point(cx, cy - 3), new Point(cx + 4, cy + 2) });
                else
                    g.DrawLines(p, new[] { new Point(cx - 4, cy - 3), new Point(cx, cy + 2), new Point(cx + 4, cy - 3) });
            }
        }

        private void List_MouseDown(object sender, MouseEventArgs e)
        {
            if (upRect.Contains(e.Location))   { ScrollBy(-1); return; }
            if (downRect.Contains(e.Location)) { ScrollBy(1); return; }
            if (e.X >= list.ClientSize.Width - SB_W)
            { if (!thumbRect.IsEmpty) ScrollBy(e.Y < thumbRect.Top ? -VisibleRows : VisibleRows); return; }
            if (e.Y < HEAD_H || queue.Count == 0) return;
            int idx = scrollTop + (e.Y - HEAD_H) / ROW_H;
            if (idx < 0 || idx >= queue.Count) return;
            selIdx = idx; list.Invalidate();
            if (e.Clicks >= 2) AllowSel();
        }

        private static string Clip(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n - 1) + "\u2026";
        }
    }
}
