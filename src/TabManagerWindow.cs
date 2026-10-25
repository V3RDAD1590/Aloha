using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace Aloha
{

    public class TabManagerWindow : DafyFrame
    {
        public class TabInfo
        {
            public int Index;
            public string Host;
            public string Url;
            public bool Active;
        }

        private static readonly Color Green  = Color.FromArgb(0xB4, 0xDC, 0xBA);
        private static readonly Color Mid    = Color.FromArgb(0x5F, 0xB8, 0x7E);
        private static readonly Color Dim    = Color.FromArgb(0x7F, 0xAE, 0x86);
        private static readonly Color Border = Color.FromArgb(0x1E, 0x5C, 0x33);

        private readonly Func<List<TabInfo>> getTabs;
        private readonly Func<string> getEngine;
        private readonly Action<int> onActivate;
        private readonly Action<int> onClose;

        private Panel board;
        private Label footStatus;
        private Font hostFont, urlFont, tagFont;
        private System.Windows.Forms.Timer poll;

        private List<TabInfo> model = new List<TabInfo>();
        private string engine = "DIRECT";
        private readonly List<Rectangle> cellRects = new List<Rectangle>();
        private readonly List<Rectangle> closeRects = new List<Rectangle>();
        private int hotCell = -1;
        private bool hotClose = false;

        private const int Margin = 16, Gap = 14, CW = 290, CH = 84;

        public TabManagerWindow(Func<List<TabInfo>> getTabs, Func<string> getEngine,
                                Action<int> onActivate, Action<int> onClose)
            : base("OPT-TABS", "Tab Board")
        {
            this.getTabs = getTabs;
            this.getEngine = getEngine;
            this.onActivate = onActivate;
            this.onClose = onClose;

            Size = new Size(760, 460);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            ClientArea.BackColor = Color.Black;

            hostFont = new Font("Consolas", 10.5f, FontStyle.Bold);
            urlFont  = new Font("Consolas", 8f);
            tagFont  = new Font("Lucida Console", 7.5f);

            footStatus = new Label
            {
                AutoSize = false, Left = FooterContentLeft, Top = 5, Width = Math.Max(40, FooterRightLimit - FooterContentLeft - 6), Height = 16,
                Font = new Font("Lucida Console", 8.5f, FontStyle.Bold),
                ForeColor = Color.Black, BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft, Text = "tabs"
            };
            Footer.Controls.Add(footStatus);
            footStatus.BringToFront();
            HideSizeReadout();
            Footer.Resize += (s, e) => footStatus.Width = Math.Max(40, FooterRightLimit - FooterContentLeft - 6);

            board = new Panel { Dock = DockStyle.Fill, BackColor = Color.Black, Cursor = Cursors.Hand };
            typeof(Panel).GetProperty("DoubleBuffered",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(board, true, null);
            board.Paint += Board_Paint;
            board.MouseDown += Board_MouseDown;
            board.MouseMove += Board_MouseMove;
            board.MouseLeave += Board_MouseLeave;
            board.Resize += (s, e) => board.Invalidate();
            ClientArea.Controls.Add(board);

            poll = new System.Windows.Forms.Timer { Interval = 700 };
            poll.Tick += (s, e) => RefreshBoard();
            poll.Start();

            this.Shown += (s, e) => RefreshBoard();
            this.FormClosed += (s, e) =>
            {
                try { poll?.Stop(); poll?.Dispose(); } catch { }
                hostFont?.Dispose(); urlFont?.Dispose(); tagFont?.Dispose();
            };
        }

        private void RefreshBoard()
        {
            try { model = getTabs != null ? getTabs() : new List<TabInfo>(); } catch { model = new List<TabInfo>(); }
            try { engine = getEngine != null ? getEngine() : "DIRECT"; } catch { engine = "DIRECT"; }
            int n = model != null ? model.Count : 0;
            if (footStatus != null) footStatus.Text = n + (n == 1 ? " tab \u00b7 engine: " : " tabs \u00b7 engine: ") + engine;
            if (board != null) board.Invalidate();
        }

        private void Board_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int W = board.ClientSize.Width, H = board.ClientSize.Height;
            g.Clear(Color.Black);

            using (var grid = new Pen(Color.FromArgb(0x0E, 0x1A, 0x12)))
            {
                for (int gx = 0; gx < W; gx += 40) g.DrawLine(grid, gx, 0, gx, H);
                for (int gy = 0; gy < H; gy += 40) g.DrawLine(grid, 0, gy, W, gy);
            }

            cellRects.Clear();
            closeRects.Clear();

            if (model == null || model.Count == 0)
            {
                using (var db = new SolidBrush(Dim)) g.DrawString("(no tabs)", urlFont, db, Margin + 2, Margin + 2);
                return;
            }

            string shortEng = (engine ?? "DIRECT").Split(' ')[0].ToUpperInvariant();
            bool tunneled = !shortEng.Equals("DIRECT", StringComparison.OrdinalIgnoreCase);

            int cols = Math.Max(1, (W - 2 * Margin + Gap) / (CW + Gap));

            for (int i = 0; i < model.Count; i++)
            {
                var t = model[i];
                int col = i % cols, row = i / cols;
                int x = Margin + col * (CW + Gap);
                int y = Margin + row * (CH + Gap);
                var cell = new Rectangle(x, y, CW, CH);
                cellRects.Add(cell);

                bool act = t.Active;
                bool hot = (i == hotCell);

                using (var bg = new SolidBrush(act ? Color.FromArgb(0x0E, 0x24, 0x17) : Color.FromArgb(0x08, 0x0E, 0x0A)))
                    g.FillRectangle(bg, cell);
                using (var bp = new Pen(act ? Green : (hot ? Mid : Border)))
                    g.DrawRectangle(bp, cell.X, cell.Y, cell.Width - 1, cell.Height - 1);

                if (act) using (var sp = new SolidBrush(Green)) g.FillRectangle(sp, x + 1, y + 1, 3, CH - 2);

                int tx = x + (act ? 12 : 10);
                using (var hb = new SolidBrush(act ? Green : Mid))
                    g.DrawString(Clip(t.Host, 30), hostFont, hb, tx, y + 8);
                using (var ub = new SolidBrush(Dim))
                    g.DrawString(Clip(t.Url, 40), urlFont, ub, tx, y + 32);

                using (var tg = new SolidBrush(Dim))
                    g.DrawString("#" + (i + 1), tagFont, tg, tx, y + CH - 18);

                var sz = g.MeasureString(shortEng, tagFont);
                float ex = x + CW - sz.Width - 12;
                float ey = y + CH - 18;
                var dot = new Rectangle((int)ex - 13, (int)ey + 2, 7, 7);
                if (tunneled) { using (var db = new SolidBrush(Green)) g.FillEllipse(db, dot); }
                else { using (var dp = new Pen(Dim)) g.DrawEllipse(dp, dot); }
                using (var eb = new SolidBrush(tunneled ? Mid : Dim)) g.DrawString(shortEng, tagFont, eb, ex, ey);

                var cr = new Rectangle(x + CW - 22, y + 6, 16, 16);
                closeRects.Add(cr);
                using (var xp = new Pen((hot && hotClose) ? Green : Dim, 1.4f))
                {
                    g.DrawLine(xp, cr.Left + 4, cr.Top + 4, cr.Right - 4, cr.Bottom - 4);
                    g.DrawLine(xp, cr.Right - 4, cr.Top + 4, cr.Left + 4, cr.Bottom - 4);
                }
            }
        }

        private void Board_MouseDown(object sender, MouseEventArgs e)
        {
            for (int i = 0; i < cellRects.Count; i++)
            {
                if (!cellRects[i].Contains(e.Location)) continue;
                if (model == null || i >= model.Count) return;
                if (i < closeRects.Count && closeRects[i].Contains(e.Location))
                {
                    if (onClose != null) onClose(model[i].Index);
                }
                else
                {
                    if (onActivate != null) onActivate(model[i].Index);
                }
                RefreshBoard();
                return;
            }
        }

        private void Board_MouseMove(object sender, MouseEventArgs e)
        {
            int nc = -1; bool ncl = false;
            for (int i = 0; i < cellRects.Count; i++)
                if (cellRects[i].Contains(e.Location))
                {
                    nc = i;
                    ncl = (i < closeRects.Count && closeRects[i].Contains(e.Location));
                    break;
                }
            if (nc != hotCell || ncl != hotClose) { hotCell = nc; hotClose = ncl; board.Invalidate(); }
        }

        private void Board_MouseLeave(object sender, EventArgs e)
        {
            if (hotCell != -1 || hotClose) { hotCell = -1; hotClose = false; board.Invalidate(); }
        }

        private static string Clip(string s, int n)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Length <= n ? s : s.Substring(0, n - 1) + "\u2026";
        }
    }
}
