using System;
using System.Drawing;
using System.Windows.Forms;

namespace Aloha
{

    public class NodeInfoConsole : DafyFrame
    {
        private static readonly Color Lime = Color.FromArgb(0xB4, 0xDC, 0xBA);
        private static readonly Color Dim  = Color.FromArgb(0x7F, 0xAE, 0x86);

        private readonly string[] info;
        private readonly string target;
        private readonly bool canScan;
        private readonly string[] actions;
        private int sel = 0;
        private Font consoleFont, headFont;
        private Panel view;

        public event Action<string> OnNmap;
        public event Action<string> OnSiteMap;

        public NodeInfoConsole(string title, string[] infoLines, string actTarget, bool scannable)
            : base("OPT-NODE", title)
        {
            info = infoLines ?? new string[0];
            target = actTarget ?? "";
            canScan = scannable && target.Length > 0;
            actions = canScan
                ? new[] { "site-map  " + target }
                : new[] { "(no scannable host for this node)" };

            consoleFont = new Font("Consolas", 9.5f);
            headFont    = new Font("Lucida Console", 12f, FontStyle.Bold);

            this.ClientArea.BackColor = Color.Black;
            view = new DoubleBufferedPanel { Dock = DockStyle.Fill, BackColor = Color.Black };
            view.Paint += View_Paint;
            view.MouseMove += (s, e) => { int h = HitRow(e.Y); if (h >= 0 && h != sel) { sel = h; view.Invalidate(); } };
            view.MouseClick += (s, e) => { int h = HitRow(e.Y); if (h >= 0) { sel = h; view.Invalidate(); RunSelected(); } };
            view.MouseDown += (s, e) => view.Focus();
            this.ClientArea.Controls.Add(view);

            this.KeyPreview = true;
            this.KeyDown += (s, e) =>
            {
                if (!canScan) return;
                if (e.KeyCode == Keys.Up)   { sel = Math.Max(0, sel - 1); view.Invalidate(); e.Handled = true; }
                if (e.KeyCode == Keys.Down) { sel = Math.Min(actions.Length - 1, sel + 1); view.Invalidate(); e.Handled = true; }
                if (e.KeyCode == Keys.Enter){ RunSelected(); e.Handled = true; }
            };

            var btnClose = MakeLabeledButton("Close", () => Close());

            this.Width = 460; this.Height = 360;
            this.Shown += (s, e) => view.Focus();
        }

        private int ActionsTop => 12 + (info.Length + 1) * 18 + 14;

        private int HitRow(int my)
        {
            if (!canScan) return -1;
            int top = ActionsTop;
            for (int i = 0; i < actions.Length; i++)
            {
                int ry = top + i * 22;
                if (my >= ry - 2 && my <= ry + 18) return i;
            }
            return -1;
        }

        private void RunSelected()
        {
            if (!canScan) return;
            string t = target;
            { var h = OnSiteMap; Close(); h?.Invoke(t); }
        }

        private void View_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            int y = 12;
            using (var lime = new SolidBrush(Lime))
            using (var dim  = new SolidBrush(Dim))
            using (var selBg = new SolidBrush(Lime))
            {

                g.DrawString(info.Length > 0 ? info[0] : "node", headFont, lime, 8, y);
                y += 26;

                for (int i = 1; i < info.Length; i++)
                {
                    g.DrawString(info[i], consoleFont, dim, 10, y);
                    y += 18;
                }
                y = ActionsTop;
                g.DrawString("SCAN", consoleFont, dim, 10, y - 20);
                for (int i = 0; i < actions.Length; i++)
                {
                    int ry = y + i * 22;
                    if (canScan && i == sel)
                    {
                        var sz = g.MeasureString(actions[i], consoleFont);
                        g.FillRectangle(selBg, 8, ry - 1, sz.Width + 8, 18);
                        g.DrawString(actions[i], consoleFont, Brushes.Black, 12, ry);
                    }
                    else
                    {
                        g.DrawString(actions[i], consoleFont, canScan ? lime : dim, 12, ry);
                    }
                }
            }
        }
    }

    internal class DoubleBufferedPanel : Panel
    {
        public DoubleBufferedPanel() { this.DoubleBuffered = true; this.ResizeRedraw = true; }
    }
}
