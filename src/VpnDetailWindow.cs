using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Aloha
{

    public class VpnDetailWindow : DafyFrame
    {
        private static readonly Color Fab = Color.FromArgb(0xFA, 0xFA, 0xFB);

        private readonly VpnGateClient.Server srv;

        public event Action<VpnGateClient.Server> OnConnect;

        public VpnDetailWindow(VpnGateClient.Server s)
            : base("OPT-VPND", (s != null ? s.Host : "?"))
        {
            srv = s;
            Size = new Size(580, 440);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            KeyPreview = true;
            ClientArea.BackColor = Fab;
            var canvas = SetupAlohaScroll(Fab);

            var font = new Font("Consolas", 9.5f);
            string text = BuildText(s);
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

            var btnConnect = MakeLabeledButton("Connect", () => { var h = OnConnect; Close(); if (h != null) h(srv); });
            var btnClose   = MakeLabeledButton("Close", () => Close());
            Footer.Controls.Add(btnConnect);
            Footer.Controls.Add(btnClose);
            btnConnect.BringToFront(); btnClose.BringToFront();

            void Layout() { LayoutFooterRow(btnConnect, btnClose); }
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

        private static void Row(StringBuilder sb, string k, string v)
        {
            sb.Append(k.PadRight(13)).Append(string.IsNullOrEmpty(v) ? "?" : v).Append("\r\n");
        }

        private static string BuildText(VpnGateClient.Server s)
        {
            if (s == null) return "";
            var sb = new StringBuilder();
            Row(sb, "ip",          s.Ip);
            Row(sb, "endpoint",    (s.Proto == "?" ? "?" : s.Proto.ToUpperInvariant()) + " " + (s.Port > 0 ? s.Port.ToString() : "?"));
            Row(sb, "region",      s.Country + " (" + s.CountryShort + ")");
            Row(sb, "score",       s.Score.ToString("N0"));
            Row(sb, "ping",        s.Ping + " ms");
            Row(sb, "speed",       s.SpeedMbps);
            Row(sb, "sessions",    s.Sessions.ToString());
            Row(sb, "uptime",      s.UptimeText);
            Row(sb, "total users", s.TotalUsers.ToString("N0"));
            Row(sb, "total data",  s.TrafficText);
            Row(sb, "log policy",  string.IsNullOrEmpty(s.LogType) ? "?" : s.LogType);
            Row(sb, "operator",    string.IsNullOrEmpty(s.Operator) ? "?" : s.Operator);
            if (!string.IsNullOrEmpty(s.Message))
                sb.Append("\r\nmessage\r\n").Append(s.Message).Append("\r\n");
            return sb.ToString();
        }
    }
}
