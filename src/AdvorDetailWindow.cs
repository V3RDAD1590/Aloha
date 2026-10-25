using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Aloha
{

    public class AdvorDetailWindow : DafyFrame
    {
        private static readonly Color Fab = Color.FromArgb(0xFA, 0xFA, 0xFB);

        private readonly TorControl.Relay node;

        public event Action<TorControl.Relay> OnUseExit;
        public event Action<TorControl.Relay> OnUseEntry;

        public AdvorDetailWindow(TorControl.Relay r)
            : base("OPT-NODE", (r != null ? r.Nick : "?"))
        {
            node = r;
            Size = new Size(580, 400);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            KeyPreview = true;
            ClientArea.BackColor = Fab;
            var canvas = SetupAlohaScroll(Fab);

            var font = new Font("Consolas", 9.5f);
            string text = BuildText(r);
            var info = new TextBox
            {
                Multiline = true, ReadOnly = true, WordWrap = false,
                ScrollBars = ScrollBars.None, BorderStyle = BorderStyle.None,
                BackColor = Fab, ForeColor = Color.Black, Font = font,
                Left = 0, Top = 0, Text = text
            };
            info.Size = MeasureBlock(text, font);
            info.Select(0, 0);
            canvas.Controls.Add(info);

            var btnExit  = MakeLabeledButton("Use as exit",  () => { var h = OnUseExit;  Close(); if (h != null) h(node); });
            var btnEntry = MakeLabeledButton("Use as entry", () => { var h = OnUseEntry; Close(); if (h != null) h(node); });
            var btnClose = MakeLabeledButton("Close", () => Close());
            Footer.Controls.Add(btnExit);
            Footer.Controls.Add(btnEntry);
            Footer.Controls.Add(btnClose);
            btnExit.BringToFront(); btnEntry.BringToFront(); btnClose.BringToFront();
            void Layout()
            {

                LayoutFooterRow(btnExit, btnEntry, btnClose);
            }
            Layout();
            Footer.Resize += (s2, e2) => Layout();

            this.KeyDown += (s2, e2) => { if (e2.KeyCode == Keys.Escape) { Close(); e2.Handled = true; } };
        }

        private static Size MeasureBlock(string text, Font f)
        {
            int maxW = 40, lines = 0;
            foreach (var ln in (text ?? "").Replace("\r", "").Split('\n'))
            {
                lines++;
                int w = TextRenderer.MeasureText(ln.Length == 0 ? " " : ln, f).Width;
                if (w > maxW) maxW = w;
            }
            if (lines == 0) lines = 1;
            int h = lines * (f.Height + 2) + 8;
            return new Size(Math.Min(maxW + 16, 30000), h);
        }

        private static string Bw(long kb)
        {
            if (kb >= 1024 * 1024) return (kb / 1048576.0).ToString("0.0") + " GB/s";
            if (kb >= 1024)        return (kb / 1024.0).ToString("0.0") + " MB/s";
            return kb + " KB/s";
        }
        private static string YN(bool b) { return b ? "yes" : "no"; }

        private static void Row(StringBuilder sb, string k, string v)
        {
            sb.Append(k.PadRight(13)).Append(string.IsNullOrEmpty(v) ? "?" : v).Append("\r\n");
        }

        private static string BuildText(TorControl.Relay n)
        {
            if (n == null) return "";
            var sb = new StringBuilder();
            Row(sb, "nickname",    n.Nick);
            Row(sb, "fingerprint", n.Fingerprint);
            Row(sb, "ip : ORPort", (string.IsNullOrEmpty(n.Ip) ? "?" : n.Ip) + " : " + n.ORPort);
            Row(sb, "region",      n.Country);
            Row(sb, "bandwidth",   Bw(n.BandwidthKb));
            Row(sb, "exit",   YN(n.IsExit));
            Row(sb, "guard",  YN(n.IsGuard));
            Row(sb, "fast",   YN(n.IsFast));
            Row(sb, "stable", YN(n.IsStable));
            string flags = (n.Flags != null && n.Flags.Count > 0) ? string.Join(" ", n.Flags) : "?";
            sb.Append("\r\nflags\r\n").Append(flags).Append("\r\n");
            return sb.ToString();
        }
    }
}
