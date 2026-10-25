using System;
using System.Drawing;
using System.Windows.Forms;

namespace Aloha
{

    public class HelpWindow : DafyFrame
    {
        private static readonly Color Fab = Color.FromArgb(0xFA, 0xFA, 0xFB);
        private static readonly Color Ink = Color.Black;
        private static readonly Color Dim = Color.FromArgb(0x55, 0x55, 0x55);

        public HelpWindow() : base("OPT-HELP", "Help")
        {
            Size = new Size(520, 430);
            StartPosition = FormStartPosition.CenterScreen;
            ShowInTaskbar = true;
            ClientArea.BackColor = Fab;

            var p = new Panel { Dock = DockStyle.Fill, BackColor = Fab };
            ClientArea.Controls.Add(p);

            p.Controls.Add(new Label
            {
                Text = "ALOHA BROWSER", AutoSize = true, Left = 26, Top = 18,
                ForeColor = Ink, BackColor = Fab, Font = new Font("Consolas", 16f, FontStyle.Bold)
            });
            p.Controls.Add(new Label
            {
                Text = "v" + Form1.VERSION + "  \u00b7  quick reference", AutoSize = true, Left = 28, Top = 54,
                ForeColor = Dim, BackColor = Fab, Font = new Font("Consolas", 9f)
            });

            var body = new TextBox
            {
                Multiline = true, ReadOnly = true, ScrollBars = ScrollBars.Vertical, WordWrap = false,
                BorderStyle = BorderStyle.None, BackColor = Fab, ForeColor = Ink,
                Font = new Font("Consolas", 9.5f),
                Left = 28, Top = 84, Width = ClientArea.ClientSize.Width - 56,
                Height = ClientArea.ClientSize.Height - 100,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                TabStop = false
            };
            body.Text = string.Join("\r\n", new[]
            {
                "KEYBOARD",
                "    Ctrl+T          new tab",
                "    Ctrl+W          close tab",
                "    Ctrl+Shift+M    Tab Board",
                "    Ctrl+click      open a link in a new tab",
                "",
                "TOOLS  (Options menu)",
                "    Proxy            choose / save the network profile",
                "    Network map      live connection map",
                "    Live headers     watch request/response headers",
                "    Request tamper   block / redirect / rewrite requests",
                "    Intercept proxy  local intercepting proxy",
                "    Profiles         saved network profiles",
                "",
                "TOOLS  (Edit menu)",
                "    Inspector        the page console / forms inspector",
                "    Browser config   privacy flags, DNS-over-HTTPS, downloads",
                "    Find in page     Ctrl+F search",
                "",
                "CONSOLES",
                "    type 'help' inside the Console or the Inspector",
                "    for the full command lists."
            });
            p.Controls.Add(body);
            p.Resize += (s, e) =>
            {
                body.Width  = p.ClientSize.Width - 56;
                body.Height = p.ClientSize.Height - 100;
            };
        }
    }
}
