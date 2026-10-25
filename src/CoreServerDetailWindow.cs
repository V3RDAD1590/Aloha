using System;
using System.Drawing;
using System.Text;
using System.Windows.Forms;

namespace Aloha
{

    public class CoreServerDetailWindow : DafyFrame
    {
        private static readonly Color Fab = Color.FromArgb(0xFA, 0xFA, 0xFB);

        private readonly CoreProxyClient.CoreServer srv;

        public event Action<CoreProxyClient.CoreServer> OnConnect;

        public CoreServerDetailWindow(CoreProxyClient.CoreServer s, string configJson)
            : base("OPT-SRV", s != null ? s.Display : "?")
        {
            srv = s;
            Size = new Size(620, 500);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            KeyPreview = true;
            ClientArea.BackColor = Fab;
            var canvas = SetupAlohaScroll(Fab);

            var font = new Font("Consolas", 9.5f);
            string text = BuildText(s, configJson);
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
            if (string.IsNullOrEmpty(v)) return;
            sb.Append(k.PadRight(13)).Append(v).Append("\r\n");
        }

        private static string BuildText(CoreProxyClient.CoreServer s, string cfgJson)
        {
            if (s == null) return "";
            var sb = new StringBuilder();
            Row(sb, "protocol", s.Protocol);
            Row(sb, "remark",   s.Remark);
            Row(sb, "address",  s.Address);
            Row(sb, "port",     s.Port > 0 ? s.Port.ToString() : "");
            if (s.Protocol == "shadowsocks") { Row(sb, "method", s.SsMethod); Row(sb, "password", s.SsPassword); }
            else Row(sb, s.Protocol == "trojan" ? "password" : "id", s.Id);
            Row(sb, "network",     s.Network);
            Row(sb, "security",    s.Tls);
            Row(sb, "sni",         s.Sni);
            Row(sb, "host",        s.Host);
            Row(sb, "path",        s.Path);
            Row(sb, "flow",        s.Flow);
            Row(sb, "fingerprint", s.Fingerprint);
            Row(sb, "publicKey",   s.PublicKey);
            Row(sb, "shortId",     s.ShortId);
            if (!string.IsNullOrEmpty(s.Raw))
                sb.Append("\r\nshare link\r\n").Append(s.Raw).Append("\r\n");
            if (!string.IsNullOrEmpty(cfgJson))
                sb.Append("\r\ncore config\r\n").Append(cfgJson).Append("\r\n");
            return sb.ToString();
        }
    }
}
